using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;

public class TerrainManager : MonoBehaviour
{
    private static TerrainManager _instance;
    public static TerrainManager Instance
    {
        get
        {
            if (_instance == null || _instance.Config == null) 
            {
                var instances = FindObjectsByType<TerrainManager>(FindObjectsSortMode.None);
                foreach (var inst in instances)
                {
                    if (inst.Config != null) 
                    {
                        inst.PrepareRuntimeConfig();
                        _instance = inst;
                        break;
                    }
                }
            }
            return _instance;
        }
    }
    
    public TerrainConfig Config;
    public Transform Player;
    public GameObject ChunkPrefab; // Prefab with SandChunk component

    [Header("Streaming Settings")]
    public int LoadingRadius = 3;
    public float ColliderLODDistance = 500f;
    public float SimulationLODDistance = 400f;
    public int MaxChunkCreatesPerFrame = 1;
    public int MaxSceneryLoadsPerFrame = 1;
    public int MaxVegetationLoadsPerFrame = 1;
    public int MaxColliderBakesPerFrame = 1;
    public int MaxMeshRebuildsPerFrame = 1;

    private Dictionary<Vector2Int, SandChunk> _chunks = new Dictionary<Vector2Int, SandChunk>();
    private Vector2Int _currentChunkCoord;
    private List<SandChunk> _pendingMeshRebuilds = new List<SandChunk>();
    private Queue<Vector2Int> _pendingChunkCreates = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _pendingChunkCreateSet = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _pendingSceneryLoads = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _pendingSceneryLoadSet = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _pendingVegetationLoads = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _pendingVegetationLoadSet = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _pendingRealGrassLoads = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _pendingRealGrassLoadSet = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _desiredTerrainCoords = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _desiredSceneryCoords = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _desiredVegetationCoords = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _desiredRealGrassCoords = new HashSet<Vector2Int>();
    private int _heavyWorkPhase;

    // ── Oasis Management ──
    public struct OasisData
    {
        public Vector2 position;
        public float basinRadius;
        public float waterRadius;
        public float basinDepth;
        public Vector2Int chunkCoord; // Added to sync vegetation angle between generation and assets
    }
    private Dictionary<Vector2Int, List<GameObject>> _activeOasisAssets = new Dictionary<Vector2Int, List<GameObject>>();
    private Dictionary<Vector2Int, List<OasisData>> _oasisCache = new Dictionary<Vector2Int, List<OasisData>>();

    // ── Dynamic Bush Spawning ──
    private struct OasisBushSpawnData
    {
        public Vector3 center;         // Oasis center
        public float vegAngle;         // Vegetation quadrant angle
        public float waterLevel;       // Water level for this oasis
        public List<Vector3> palmPos;  // Positions of palms for bush clustering
        public bool bushesSpawned;     // Have bushes been dynamically spawned?
    }
    private Dictionary<Vector2Int, OasisBushSpawnData> _oasisBushData = new Dictionary<Vector2Int, OasisBushSpawnData>();
    private Dictionary<Vector2Int, List<GameObject>> _activeOasisBushes = new Dictionary<Vector2Int, List<GameObject>>();
    private float _bushCheckTimer = 0f;
    private bool _hasTeleportedToMountain = false;
    private bool _runtimeConfigPrepared;
    public Vector2Int TestMountainChunk { get; private set; } = new Vector2Int(-99999, -99999);

    void Awake()
    {
        _instance = this;
        PrepareRuntimeConfig();

        if (ChunkPrefab == null)
        {
            Debug.LogError("ChunkPrefab is not assigned in TerrainManager!");
        }
    }

    private void PrepareRuntimeConfig()
    {
        if (_runtimeConfigPrepared || Config == null)
        {
            return;
        }

        Config = Instantiate(Config);
        Config.name = $"{Config.name}_Runtime";
        Config.Seed = WorldSeedManager.EnsureSeed(Config.RandomizeSeedOnPlay, Config.Seed);
        _runtimeConfigPrepared = true;
        Debug.Log($"[TerrainManager] Runtime terrain seed: {Config.Seed}");
    }

    void Start()
    {
        if (Config == null) return;
        
        Debug.Log("[OasisFish] TerrainManager.Start: Prefab lists are now manually assigned in Config.");
        Debug.Log($"[OasisFish] Total fish prefabs available: {Config.OasisFishPrefabs.Count}");

        if (Config.MountainTestMode)
        {
            TestMountainChunk = new Vector2Int(0, 5);
        }

        PlacePlayerOnInitialTerrain();
    }

    private float _simTimer = 0f;

    private void PlacePlayerOnInitialTerrain()
    {
        if (Player == null) return;

        Vector3 spawnPoint = Config.PlayerSpawnPoint;
        float surfaceY = SampleHeight(spawnPoint);
        float clearance = GetPlayerGroundClearance();

        Player.position = new Vector3(spawnPoint.x, surfaceY + clearance + 0.25f, spawnPoint.z);

        Rigidbody playerBody = Player.GetComponent<Rigidbody>();
        if (playerBody != null)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        UpdateChunks();
        _heavyWorkPhase = 1;
        ProcessHeavyTerrainWork();
    }

    private float GetPlayerGroundClearance()
    {
        CapsuleCollider capsule = Player.GetComponent<CapsuleCollider>();
        if (capsule == null) return 1f;

        float scaledCenterY = capsule.center.y * Player.lossyScale.y;
        float scaledHalfHeight = capsule.height * 0.5f * Player.lossyScale.y;
        return Mathf.Max(0f, scaledHalfHeight - scaledCenterY);
    }

    void Update()
    {
        if (Player == null) return;

        UpdateChunks();
        ProcessHeavyTerrainWork();

        // 1. Every-Frame LOD Updates (Distance check must be fast)
        float chunkSizeWorld = (Config.ChunkSize - 1) * Config.CellSize;
        foreach (var chunk in _chunks.Values) {
            if (!chunk.IsInitialized) continue;
            
            float dist2D = Vector2.Distance(
                new Vector2(Player.position.x, Player.position.z), 
                new Vector2(chunk.transform.position.x + chunkSizeWorld * 0.5f, chunk.transform.position.z + chunkSizeWorld * 0.5f)
            );
            chunk.UpdateLOD(dist2D, Config.ColliderLODDistance, Config.SimulationLODDistance);
        }

        // 2. Throttled Simulation (0.1s interval)
        _simTimer += Time.deltaTime;
        if (_simTimer >= 0.1f)
        {
            float simStep = _simTimer;
            _simTimer = 0f;
            Simulate(simStep);
        }

        // 3. Dynamic Bush Spawning — bushes form from sand when player approaches palm trees
        _bushCheckTimer += Time.deltaTime;
        if (_bushCheckTimer >= 0.2f)
        {
            _bushCheckTimer = 0f;
            SpawnBushesNearPlayer();
        }
    }

    void Simulate(float deltaTime)
    {
        if (_chunks.Count == 0) return;

        int chunkCount = _chunks.Count;
        Unity.Collections.NativeArray<Unity.Jobs.JobHandle> simHandles = new Unity.Collections.NativeArray<Unity.Jobs.JobHandle>(chunkCount, Unity.Collections.Allocator.Temp);

        // 1. Reset Flags and Schedule Simulation
        int i = 0;
        List<SandChunk> chunkList = new List<SandChunk>(_chunks.Values);
        if (Config.RootSandStabilizationEnabled)
        {
            HashSet<SandChunk> chunksNeedingRootMask = new HashSet<SandChunk>();
            foreach (var chunk in chunkList)
            {
                if (chunk == null || !chunk.IsInitialized || !chunk.IsSimulating || !chunk.HasActiveFlow)
                    continue;

                chunksNeedingRootMask.Add(chunk);
                for (int n = 0; n < chunk.Neighbors.Length; n++)
                {
                    SandChunk neighbor = chunk.Neighbors[n];
                    if (neighbor != null && neighbor.IsInitialized)
                        chunksNeedingRootMask.Add(neighbor);
                }
            }

            foreach (SandChunk chunk in chunksNeedingRootMask)
                chunk.RefreshRootStabilityMask();
        }

        foreach (var chunk in chunkList)
        {
            if (!chunk.IsInitialized) {
                simHandles[i++] = default;
                continue;
            }

            Vector3 chunkPos = chunk.transform.position;
            float chunkSizeWorld = (Config.ChunkSize - 1) * Config.CellSize;
            Vector3 chunkCenter = chunkPos + new Vector3(chunkSizeWorld * 0.5f, 0, chunkSizeWorld * 0.5f);
            // LOD update moved to per-frame Update()
            if (!chunk.IsSimulating || !chunk.HasActiveFlow) {
                simHandles[i++] = default;
                continue;
            }

            // Use Cached Neighbors for cross-chunk flow (Faster than GetChunk)
            SandChunk nN = chunk.Neighbors[0];
            SandChunk nS = chunk.Neighbors[1];
            SandChunk nE = chunk.Neighbors[2];
            SandChunk nW = chunk.Neighbors[3];
            SandChunk nNE = chunk.Neighbors[4];
            SandChunk nNW = chunk.Neighbors[5];
            SandChunk nSE = chunk.Neighbors[6];
            SandChunk nSW = chunk.Neighbors[7];

            // Reset dirty flag
            chunk.ModifiedFlag[0] = 0;

            var job = new SandJobs.SimulationJob {
                Size = Config.ChunkSize, FlowThreshold = Config.FlowThreshold, FlowSpeed = Config.FlowSpeed,
                UseStabilityMask = Config.RootSandStabilizationEnabled && chunk.RootStabilityMask.IsCreated,
                RootSandEdgeFlowMultiplier = Mathf.Clamp01(Config.RootSandEdgeFlowMultiplier),
                ReadHeights = chunk.HeightsRead, WriteHeights = chunk.HeightsWrite,
                StabilityMask = chunk.RootStabilityMask,
                
                ReadN = (nN != null && nN.IsInitialized && nN.HeightsRead.IsCreated) ? nN.HeightsRead : chunk.HeightsRead,
                ReadS = (nS != null && nS.IsInitialized && nS.HeightsRead.IsCreated) ? nS.HeightsRead : chunk.HeightsRead,
                ReadE = (nE != null && nE.IsInitialized && nE.HeightsRead.IsCreated) ? nE.HeightsRead : chunk.HeightsRead,
                ReadW = (nW != null && nW.IsInitialized && nW.HeightsRead.IsCreated) ? nW.HeightsRead : chunk.HeightsRead,
                StabilityN = (nN != null && nN.IsInitialized && nN.RootStabilityMask.IsCreated) ? nN.RootStabilityMask : chunk.RootStabilityMask,
                StabilityS = (nS != null && nS.IsInitialized && nS.RootStabilityMask.IsCreated) ? nS.RootStabilityMask : chunk.RootStabilityMask,
                StabilityE = (nE != null && nE.IsInitialized && nE.RootStabilityMask.IsCreated) ? nE.RootStabilityMask : chunk.RootStabilityMask,
                StabilityW = (nW != null && nW.IsInitialized && nW.RootStabilityMask.IsCreated) ? nW.RootStabilityMask : chunk.RootStabilityMask,
                HasN = nN != null && nN.IsInitialized && nN.HeightsRead.IsCreated, 
                HasS = nS != null && nS.IsInitialized && nS.HeightsRead.IsCreated,
                HasE = nE != null && nE.IsInitialized && nE.HeightsRead.IsCreated, 
                HasW = nW != null && nW.IsInitialized && nW.HeightsRead.IsCreated,

                ReadNE = (nNE != null && nNE.IsInitialized && nNE.HeightsRead.IsCreated) ? nNE.HeightsRead : chunk.HeightsRead,
                ReadNW = (nNW != null && nNW.IsInitialized && nNW.HeightsRead.IsCreated) ? nNW.HeightsRead : chunk.HeightsRead,
                ReadSE = (nSE != null && nSE.IsInitialized && nSE.HeightsRead.IsCreated) ? nSE.HeightsRead : chunk.HeightsRead,
                ReadSW = (nSW != null && nSW.IsInitialized && nSW.HeightsRead.IsCreated) ? nSW.HeightsRead : chunk.HeightsRead,
                StabilityNE = (nNE != null && nNE.IsInitialized && nNE.RootStabilityMask.IsCreated) ? nNE.RootStabilityMask : chunk.RootStabilityMask,
                StabilityNW = (nNW != null && nNW.IsInitialized && nNW.RootStabilityMask.IsCreated) ? nNW.RootStabilityMask : chunk.RootStabilityMask,
                StabilitySE = (nSE != null && nSE.IsInitialized && nSE.RootStabilityMask.IsCreated) ? nSE.RootStabilityMask : chunk.RootStabilityMask,
                StabilitySW = (nSW != null && nSW.IsInitialized && nSW.RootStabilityMask.IsCreated) ? nSW.RootStabilityMask : chunk.RootStabilityMask,
                HasNE = nNE != null && nNE.IsInitialized && nNE.HeightsRead.IsCreated, 
                HasNW = nNW != null && nNW.IsInitialized && nNW.HeightsRead.IsCreated,
                HasSE = nSE != null && nSE.IsInitialized && nSE.HeightsRead.IsCreated, 
                HasSW = nSW != null && nSW.IsInitialized && nSW.HeightsRead.IsCreated,

                ModifiedFlag = chunk.ModifiedFlag
            };
            simHandles[i++] = job.Schedule();
        }

        // Wait for all simulation to finish
        Unity.Jobs.JobHandle.CompleteAll(simHandles);
        simHandles.Dispose();

        // 2. Batch Schedule Mesh Updates for Modified Chunks
        List<SandChunk> modifiedChunks = new List<SandChunk>();
        foreach (var chunk in chunkList) {
            if (chunk.IsInitialized && chunk.IsSimulating && chunk.HasActiveFlow) {
                chunk.SwapBuffers();
                chunk.TickFlow(deltaTime);
                if (chunk.ModifiedFlag[0] == 1) {
                    modifiedChunks.Add(chunk);
                }
            }
        }

        if (modifiedChunks.Count > 0) {
            Unity.Collections.NativeArray<Unity.Jobs.JobHandle> meshHandles = new Unity.Collections.NativeArray<Unity.Jobs.JobHandle>(modifiedChunks.Count, Unity.Collections.Allocator.Temp);
            for (int j = 0; j < modifiedChunks.Count; j++) {
                meshHandles[j] = modifiedChunks[j].ScheduleMeshUpdate(default);
            }
            Unity.Jobs.JobHandle.CompleteAll(meshHandles);
            meshHandles.Dispose();

            // 3. Apply Result
            foreach (var chunk in modifiedChunks) {
                chunk.ApplyMeshUpdate();
            }
        }
    }

    private void UpdateChunks()
    {
        float chunkSizeWorld = GetChunkSizeWorld();
        if (chunkSizeWorld <= 0) return;

        // --- RESTORED MountainTestMode Teleportation ---
        if (Config.MountainTestMode && !_hasTeleportedToMountain)
        {
            _hasTeleportedToMountain = true;
            _oasisCache.Clear(); // Ensure cache is fresh for the first teleport

            Vector3 targetPos = new Vector3(TestMountainChunk.x * chunkSizeWorld + chunkSizeWorld * 0.5f, 0, TestMountainChunk.y * chunkSizeWorld + chunkSizeWorld * 0.5f);
            
            // Try to find a valid spot on the apron sand to teleport the player.
            // Mountain footprints are huge, so search outward from the center instead of probing nearby random points.
            Vector3 treeWorldPos = targetPos;
            bool found = false;
            float searchStartRadius = Mathf.Max(10f, chunkSizeWorld * 0.5f);
            float searchRadius = Mathf.Max(chunkSizeWorld, Config.MountainMaxScale * Config.MountainMeshRadiusMultiplier);
            if (MountainSpawner.Instance != null)
            {
                List<MountainSpawner.MountainData> nearbyMountains = MountainSpawner.Instance.GetNearbyMountains(TestMountainChunk);
                for (int i = 0; i < nearbyMountains.Count; i++)
                {
                    MountainSpawner.MountainData mountain = nearbyMountains[i];
                    if (Vector2.Distance(mountain.position, new Vector2(targetPos.x, targetPos.z)) <= chunkSizeWorld)
                    {
                        searchStartRadius = Mathf.Max(searchStartRadius, mountain.footprintRadius);
                        searchRadius = Mathf.Max(chunkSizeWorld, mountain.radius);
                        break;
                    }
                }
            }

            float radiusStep = Mathf.Max(10f, chunkSizeWorld * 0.5f);
            const int angleSteps = 24;
            for (float searchDistance = searchStartRadius; searchDistance <= searchRadius && !found; searchDistance += radiusStep)
            {
                for (int angleIndex = 0; angleIndex < angleSteps; angleIndex++)
                {
                    float angle = (Mathf.PI * 2f * angleIndex) / angleSteps;
                    Vector3 checkPos = targetPos + new Vector3(Mathf.Cos(angle) * searchDistance, 0, Mathf.Sin(angle) * searchDistance);
                    float flatness = 0f;
                    float influence = MountainSpawner.Instance != null ? MountainSpawner.Instance.GetMountainInfluenceAtPoint(checkPos, out flatness) : 0f;
                    float apronBlend = Mathf.Max(flatness, influence * 0.5f);

                    if (apronBlend > 0.3f && flatness < 0.99f)
                    {
                        treeWorldPos = checkPos;
                        found = true;
                        break;
                    }
                }
            }

            if (found)
            {
                Player.position = treeWorldPos + Vector3.up * 10f;
                Player.LookAt(targetPos);
                Debug.Log($"[TerrainManager] Teleported to mountain test spot: {treeWorldPos}");
            }
            else
            {
                Player.position = targetPos + Vector3.up * 10f;
                Debug.LogWarning("[TerrainManager] Could not find perfect apron spot, teleporting to center.");
            }
        }

        Vector2Int playerChunk = new Vector2Int(
            Mathf.FloorToInt(Player.position.x / chunkSizeWorld), 
            Mathf.FloorToInt(Player.position.z / chunkSizeWorld)
        );

        if (playerChunk == _currentChunkCoord && _chunks.Count > 0) return;
        
        // Clear caches when moving chunks
        _oasisCache.Clear();
        _oasisBushData.Clear();
        
        _currentChunkCoord = playerChunk;

        int radius = LoadingRadius;

        // 1. Loading Radius for Sand/Terrain
        HashSet<Vector2Int> activeCoords = new HashSet<Vector2Int>();
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int coord = playerChunk + new Vector2Int(x, y);
                float dist = Vector2.Distance(playerChunk, coord);
                if (dist <= radius) activeCoords.Add(coord);
            }
        }

        // 2. Loading Radius for Mountains
        int mRadius = Config.MountainLoadingRadius;
        HashSet<Vector2Int> mountainCoords = new HashSet<Vector2Int>();
        for (int y = -mRadius; y <= mRadius; y++)
        {
            for (int x = -mRadius; x <= mRadius; x++)
            {
                Vector2Int coord = playerChunk + new Vector2Int(x, y);
                float dist = Vector2.Distance(playerChunk, coord);
                if (dist <= mRadius) mountainCoords.Add(coord);
            }
        }

        // 3. Loading Radius for Vegetation
        int vRadius = Config.VegetationLoadingRadius;
        HashSet<Vector2Int> vegetationCoords = new HashSet<Vector2Int>();
        for (int y = -vRadius; y <= vRadius; y++)
        {
            for (int x = -vRadius; x <= vRadius; x++)
            {
                Vector2Int coord = playerChunk + new Vector2Int(x, y);
                float dist = Vector2.Distance(playerChunk, coord);
                if (dist <= vRadius) vegetationCoords.Add(coord);
            }
        }

        _desiredTerrainCoords = activeCoords;
        _desiredSceneryCoords = mountainCoords;
        _desiredVegetationCoords = vegetationCoords;
        _desiredRealGrassCoords = BuildDesiredRealGrassCoords(Player.position, chunkSizeWorld);

        QueueMissingCoordsByDistance(_desiredSceneryCoords, _pendingSceneryLoads, _pendingSceneryLoadSet, coord => true);
        QueueMissingCoordsByDistance(_desiredTerrainCoords, _pendingChunkCreates, _pendingChunkCreateSet, coord => !_chunks.ContainsKey(coord));
        QueueMissingCoordsByDistance(_desiredVegetationCoords, _pendingVegetationLoads, _pendingVegetationLoadSet, coord => true);
        QueueMissingCoordsByDistance(_desiredRealGrassCoords, _pendingRealGrassLoads, _pendingRealGrassLoadSet, coord =>
            PlantSpawner.Instance == null || !PlantSpawner.Instance.IsRealGrassLoadedForChunk(coord));

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kvp in _chunks)
        {
            if (!_desiredTerrainCoords.Contains(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var coord in toRemove)
        {
            DestroyChunk(coord);
        }

        if (toRemove.Count > 0)
        {
            RefreshAllNeighbors();
        }

        if (MountainSpawner.Instance != null) MountainSpawner.Instance.UnloadDistantMountains(_desiredSceneryCoords);
        UnloadDistantOases(_desiredSceneryCoords);
        if (PlantSpawner.Instance != null) PlantSpawner.Instance.UnloadDistantPlants(_desiredVegetationCoords);
        if (PlantSpawner.Instance != null) PlantSpawner.Instance.UnloadDistantRealGrass(_desiredRealGrassCoords);
        if (ApronBushSpawner.Instance != null) ApronBushSpawner.Instance.UnloadDistantBushes(_desiredVegetationCoords);
    }

    private bool ProcessHeavyTerrainWork()
    {
        for (int i = 0; i < 6; i++)
        {
            int phase = _heavyWorkPhase;
            _heavyWorkPhase = (_heavyWorkPhase + 1) % 6;

            if (phase == 0 && ProcessPendingSceneryLoads())
                return true;

            if (phase == 1 && ProcessPendingChunkCreates())
                return true;

            if (phase == 2 && ProcessPendingVegetationLoads())
                return true;

            if (phase == 3 && ProcessPendingRealGrassLoads())
                return true;

            if (phase == 4 && ProcessPendingColliderBakes())
                return true;

            if (phase == 5 && ProcessPendingMeshRebuilds())
                return true;
        }

        return false;
    }

    private bool ProcessPendingSceneryLoads()
    {
        int budget = Mathf.Max(0, MaxSceneryLoadsPerFrame);
        int processed = 0;

        while (_pendingSceneryLoads.Count > 0 && processed < budget)
        {
            Vector2Int coord = _pendingSceneryLoads.Dequeue();
            _pendingSceneryLoadSet.Remove(coord);

            if (!_desiredSceneryCoords.Contains(coord))
                continue;

            if (MountainSpawner.Instance != null)
                MountainSpawner.Instance.LoadMountainsForChunk(coord);

            LoadOasisAssetsForChunk(coord);
            processed++;
        }

        return processed > 0;
    }

    private bool ProcessPendingChunkCreates()
    {
        int budget = Mathf.Max(0, MaxChunkCreatesPerFrame);
        int processed = 0;

        while (_pendingChunkCreates.Count > 0 && processed < budget)
        {
            Vector2Int coord = _pendingChunkCreates.Dequeue();
            _pendingChunkCreateSet.Remove(coord);

            if (!_desiredTerrainCoords.Contains(coord) || _chunks.ContainsKey(coord))
                continue;

            CreateChunk(coord);
            RefreshAllNeighbors();

            if (_chunks.TryGetValue(coord, out SandChunk chunk))
            {
                chunk.SyncEdgesFromNeighbors();
                AddPendingMeshRebuild(chunk);
                QueueNeighborMeshRebuilds(chunk);
            }

            processed++;
        }

        return processed > 0;
    }

    private bool ProcessPendingVegetationLoads()
    {
        int budget = Mathf.Max(0, MaxVegetationLoadsPerFrame);
        int processed = 0;

        while (_pendingVegetationLoads.Count > 0 && processed < budget)
        {
            Vector2Int coord = _pendingVegetationLoads.Dequeue();
            _pendingVegetationLoadSet.Remove(coord);

            if (!_desiredVegetationCoords.Contains(coord))
                continue;

            if (PlantSpawner.Instance != null)
                PlantSpawner.Instance.LoadPlantsForChunk(coord);

            if (ApronBushSpawner.Instance != null)
                ApronBushSpawner.Instance.LoadBushesForChunk(coord);

            processed++;
        }

        return processed > 0;
    }

    private bool ProcessPendingRealGrassLoads()
    {
        int budget = Mathf.Max(0, MaxVegetationLoadsPerFrame);
        int processed = 0;

        while (_pendingRealGrassLoads.Count > 0 && processed < budget)
        {
            Vector2Int coord = _pendingRealGrassLoads.Dequeue();
            _pendingRealGrassLoadSet.Remove(coord);

            if (!_desiredRealGrassCoords.Contains(coord))
                continue;

            if (PlantSpawner.Instance != null)
                PlantSpawner.Instance.LoadRealGrassForChunk(coord);

            processed++;
        }

        return processed > 0;
    }

    private HashSet<Vector2Int> BuildDesiredRealGrassCoords(Vector3 playerPosition, float chunkSizeWorld)
    {
        HashSet<Vector2Int> coords = new HashSet<Vector2Int>();
        float radius = Config != null ? Mathf.Max(0f, Config.RealGrassLoadRadiusMeters) : 0f;
        if (chunkSizeWorld <= 0f || radius <= 0f)
            return coords;

        float treeZoneGrassPadding = Config != null ? Mathf.Max(0f, Config.TreeZoneGrassOuterRadius) : 0f;
        int scanRadius = Mathf.CeilToInt((radius + treeZoneGrassPadding + chunkSizeWorld) / chunkSizeWorld) + 1;
        float radiusSqr = radius * radius;

        for (int y = -scanRadius; y <= scanRadius; y++)
        {
            for (int x = -scanRadius; x <= scanRadius; x++)
            {
                Vector2Int coord = _currentChunkCoord + new Vector2Int(x, y);
                if (DoesChunkBoundsIntersectCircle(coord, playerPosition, radiusSqr, chunkSizeWorld, 0f))
                {
                    coords.Add(coord);
                    continue;
                }

                if (TerrainManager.IsTreeZoneChunk(coord, Config) &&
                    DoesChunkBoundsIntersectCircle(coord, playerPosition, radiusSqr, chunkSizeWorld, treeZoneGrassPadding))
                {
                    coords.Add(coord);
                }
            }
        }

        return coords;
    }

    private static bool DoesChunkBoundsIntersectCircle(
        Vector2Int coord,
        Vector3 circleCenter,
        float radiusSqr,
        float chunkSizeWorld,
        float padding)
    {
        float minX = coord.x * chunkSizeWorld - padding;
        float maxX = coord.x * chunkSizeWorld + chunkSizeWorld + padding;
        float minZ = coord.y * chunkSizeWorld - padding;
        float maxZ = coord.y * chunkSizeWorld + chunkSizeWorld + padding;
        float closestX = Mathf.Clamp(circleCenter.x, minX, maxX);
        float closestZ = Mathf.Clamp(circleCenter.z, minZ, maxZ);
        float dx = circleCenter.x - closestX;
        float dz = circleCenter.z - closestZ;
        return dx * dx + dz * dz <= radiusSqr;
    }

    private bool ProcessPendingColliderBakes()
    {
        int budget = Mathf.Max(0, MaxColliderBakesPerFrame);
        int processed = 0;

        foreach (var chunk in _chunks.Values)
        {
            if (processed >= budget) break;
            if (chunk.NeedsColliderBake)
            {
                chunk.BakeCollider();
                processed++;
            }
        }

        return processed > 0;
    }

    private bool ProcessPendingMeshRebuilds()
    {
        int budget = Mathf.Max(0, MaxMeshRebuildsPerFrame);
        int processed = 0;

        for (int i = _pendingMeshRebuilds.Count - 1; i >= 0 && processed < budget; i--)
        {
            SandChunk chunk = _pendingMeshRebuilds[i];
            if (chunk == null || !_chunks.ContainsKey(chunk.ChunkCoord))
            {
                _pendingMeshRebuilds.RemoveAt(i);
                continue;
            }

            chunk.ScheduleMeshUpdate(default).Complete();
            chunk.ApplyMeshUpdate();
            _pendingMeshRebuilds.RemoveAt(i);
            processed++;
        }

        return processed > 0;
    }

    private void QueueMissingCoordsByDistance(
        HashSet<Vector2Int> desiredCoords,
        Queue<Vector2Int> pendingQueue,
        HashSet<Vector2Int> pendingSet,
        System.Func<Vector2Int, bool> shouldQueue)
    {
        List<Vector2Int> coords = new List<Vector2Int>(desiredCoords);
        coords.Sort((a, b) => GetChunkCoordDistanceScore(a).CompareTo(GetChunkCoordDistanceScore(b)));

        foreach (var coord in coords)
        {
            if (!shouldQueue(coord) || pendingSet.Contains(coord))
                continue;

            pendingQueue.Enqueue(coord);
            pendingSet.Add(coord);
        }
    }

    private int GetChunkCoordDistanceScore(Vector2Int coord)
    {
        int dx = coord.x - _currentChunkCoord.x;
        int dy = coord.y - _currentChunkCoord.y;
        return dx * dx + dy * dy;
    }

    private void RefreshAllNeighbors()
    {
        foreach (var chunk in _chunks.Values)
        {
            RefreshNeighbors(chunk);
        }
    }

    private void QueueNeighborMeshRebuilds(SandChunk chunk)
    {
        for (int n = 0; n < chunk.Neighbors.Length; n++)
        {
            SandChunk neighbor = chunk.Neighbors[n];
            if (neighbor != null && neighbor.IsInitialized)
                AddPendingMeshRebuild(neighbor);
        }
    }

    private void AddPendingMeshRebuild(SandChunk chunk)
    {
        if (chunk == null || _pendingMeshRebuilds.Contains(chunk))
            return;

        _pendingMeshRebuilds.Add(chunk);
    }

    void RefreshNeighbors(SandChunk chunk)
    {
        Vector2Int coord = chunk.ChunkCoord;
        chunk.Neighbors[0] = GetChunk(coord + new Vector2Int(0, 1));  // N
        chunk.Neighbors[1] = GetChunk(coord + new Vector2Int(0, -1)); // S
        chunk.Neighbors[2] = GetChunk(coord + new Vector2Int(1, 0));  // E
        chunk.Neighbors[3] = GetChunk(coord + new Vector2Int(-1, 0)); // W
        chunk.Neighbors[4] = GetChunk(coord + new Vector2Int(1, 1));  // NE
        chunk.Neighbors[5] = GetChunk(coord + new Vector2Int(-1, 1)); // NW
        chunk.Neighbors[6] = GetChunk(coord + new Vector2Int(1, -1)); // SE
        chunk.Neighbors[7] = GetChunk(coord + new Vector2Int(-1, -1));// SW
    }

    private Dictionary<Vector2Int, float[]> _persistentChunkData = new Dictionary<Vector2Int, float[]>();

    void CreateChunk(Vector2Int coord)
    {
        GameObject go = Instantiate(ChunkPrefab, Vector3.zero, Quaternion.identity);
        
        // Position offset matches SandChunk generation logic
        float chunkSizeWorld = (Config.ChunkSize - 1) * Config.CellSize;
        go.transform.position = new Vector3(coord.x * chunkSizeWorld, 0, coord.y * chunkSizeWorld);
        go.name = $"Chunk_{coord.x}_{coord.y}";
        go.transform.parent = transform;

        SandChunk chunk = go.GetComponent<SandChunk>();
        if (chunk == null) chunk = go.AddComponent<SandChunk>();
        
        // Check for saved data
        float[] savedData = null;
        if (_persistentChunkData.TryGetValue(coord, out float[] data))
        {
            savedData = data;
        }

        chunk.Initialize(coord, Config, savedData);
        // Force initial LOD based on current settings
        float dist2D = Vector2.Distance(
            new Vector2(Player.position.x, Player.position.z),
            new Vector2(go.transform.position.x + chunkSizeWorld*0.5f, go.transform.position.z + chunkSizeWorld*0.5f)
        );
        chunk.UpdateLOD(dist2D, Config.ColliderLODDistance, Config.SimulationLODDistance);
        
        _chunks.Add(coord, chunk);
    }

    public void ModifyHeight(Vector3 worldPos, float amount, float radius)
    {
        // Calculate relevant chunk coordinates that could be affected by the radius
        float chunkSizeWorld = GetChunkSizeWorld();
        
        int minX = Mathf.FloorToInt((worldPos.x - radius) / chunkSizeWorld);
        int maxX = Mathf.FloorToInt((worldPos.x + radius) / chunkSizeWorld);
        int minZ = Mathf.FloorToInt((worldPos.z - radius) / chunkSizeWorld);
        int maxZ = Mathf.FloorToInt((worldPos.z + radius) / chunkSizeWorld);
        bool modifiedAnyChunk = false;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (_chunks.TryGetValue(new Vector2Int(x, z), out SandChunk chunk))
                {
                    if (chunk.ModifyHeight(worldPos, amount, radius))
                    {
                        ActivateFlowAround(chunk);
                        modifiedAnyChunk = true;
                    }
                }
            }
        }

        if (modifiedAnyChunk && Player != null && Vector3.Distance(Player.position, worldPos) <= 10f)
        {
            AudioManager.Instance.PlaySandFlowAt(worldPos);
        }
    }

    private void ActivateFlowAround(SandChunk chunk)
    {
        chunk.RestartFlow();

        for (int i = 0; i < chunk.Neighbors.Length; i++)
        {
            SandChunk neighbor = chunk.Neighbors[i];
            if (neighbor != null && neighbor.IsInitialized)
            {
                neighbor.RestartFlow();
            }
        }
    }

    public SandChunk GetChunk(Vector2Int coord)
    {
        if (_chunks.TryGetValue(coord, out SandChunk chunk)) return chunk;
        return null;
    }

    /// <summary>
    /// Samples the current sand height at the given world position.
    /// </summary>
    public float SampleHeight(Vector3 worldPos)
    {
        float chunkSizeWorld = GetChunkSizeWorld();
        if (chunkSizeWorld <= 0) return 0f;
        int pX = Mathf.FloorToInt(worldPos.x / chunkSizeWorld);
        int pZ = Mathf.FloorToInt(worldPos.z / chunkSizeWorld);
        
        if (_chunks.TryGetValue(new Vector2Int(pX, pZ), out SandChunk chunk))
        {
            float chunkHeight = chunk.GetHeightAt(worldPos);
            if (HighwayWinManager.TryApplyHighwayFlattening(worldPos.x, worldPos.z, chunkHeight, out float highwayHeight, out _))
                return highwayHeight;

            return chunkHeight;
        }
        
        float influence = 0f;
        float flatness = 0f;
        if (MountainSpawner.Instance != null)
        {
            influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(worldPos, out flatness);
        }

        // Oasis basin influence (use cache to avoid recalculating every frame)
        float oasisInf = 0f;
        float oasisFlatInf = 0f;
        float oasisRimInf = 0f;
        float oasisShoreRidgeInf = 0f;

        Vector2Int chunkKey = new Vector2Int(pX, pZ);
        List<OasisData> oases;
        if (!_oasisCache.TryGetValue(chunkKey, out oases)) {
            oases = GetNearbyOases(chunkKey);
            _oasisCache[chunkKey] = oases;
        }
        foreach (var o in oases) {
            float d = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), o.position);
            
            // Dip & flat beach influence (inside basin)
            if (d < o.basinRadius) {
                float t = Mathf.Clamp01(1.0f - (d / o.basinRadius));
                oasisInf = Mathf.Max(oasisInf, t);
            }
            
            // ── Rim Ridge Elevation ──
            float distFromRim = Mathf.Abs(d - o.basinRadius);
            if (distFromRim < Config.OasisRimWidth) {
                float rimT = 1.0f - (distFromRim / Config.OasisRimWidth);
                oasisRimInf = Mathf.Max(oasisRimInf, Mathf.SmoothStep(0f, 1f, rimT));
            }

            // ── NEW: Shore Ridge for Vegetation Side ──
            // Center the ridge at the vegetation side deterministically
            Random.State oldVegState = Random.state;
            Random.InitState(Config.Seed + o.chunkCoord.x * 11111 + o.chunkCoord.y * 22222);
            float vegAngle = Random.Range(0f, Mathf.PI * 2f);
            Random.state = oldVegState;

            float pointAngle = Mathf.Atan2(worldPos.z - o.position.y, worldPos.x - o.position.x);
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(pointAngle * Mathf.Rad2Deg, vegAngle * Mathf.Rad2Deg));

            if (angleDiff < 60f) // 120 degree wedge (1/3 of shore)
            {
                float angleT = 1.0f - (angleDiff / 60f);
                float angleFactor = Mathf.SmoothStep(0f, 1f, angleT);

                // Distance falloff centered at waterRadius (forming a ridge/bank)
                float distFromShore = Mathf.Abs(d - o.waterRadius);
                float ridgeWidth = 15f; 
                if (distFromShore < ridgeWidth)
                {
                    float distT = 1.0f - (distFromShore / ridgeWidth);
                    float ridgeInf = Mathf.SmoothStep(0f, 1f, distT) * angleFactor;
                    oasisShoreRidgeInf = Mathf.Max(oasisShoreRidgeInf, ridgeInf);
                }
            }

            // Delete sand wave noise up to basinRadius + 100
            float flatRadius = o.basinRadius + 100f;
            if (d < flatRadius) {
                float tFlat = 1.0f - (d / flatRadius);
                oasisFlatInf = Mathf.Max(oasisFlatInf, Mathf.SmoothStep(0f, 1f, tFlat));
            }
        }

        return GetSurfaceHeight(worldPos.x, worldPos.z, influence, flatness, oasisInf, oasisFlatInf, oasisRimInf, oasisShoreRidgeInf);
    }

    public float GetChunkSizeWorld()
    {
        return (Config.ChunkSize - 1) * Config.CellSize;
    }

    public struct TreeZoneTerrainInfluence
    {
        public float FlatChunkInfluence;
        public float LowWaveInfluence;
        public float BaseHeightInfluence;
        public float TargetBaseHeight;
        public Vector2Int SourceCoord;
    }

    public struct TreeZoneTextureInfluence
    {
        public float Influence;
        public Vector2 NormalizedPosition;
        public Vector2Int SourceCoord;
    }

    public static bool IsTreeZoneChunk(Vector2Int coord, TerrainConfig config)
    {
        if (config == null)
            return false;

        if (config.TreeZoneUseHighwayFlatSpacing
            && HighwayWinManager.TryGetFlatCorridorReference(out HighwayWinManager.FlatCorridorReference reference))
        {
            return IsHighwayAnchoredTreeZoneChunk(coord, config, reference);
        }

        if (config.TreeZoneSpawnChance <= 0f)
            return false;

        float chance = Mathf.Clamp01(config.TreeZoneSpawnChance);
        return HashToUnit(config.Seed, coord.x, coord.y, 3109) < chance;
    }

    public static bool IsHighwayAnchoredTreeZoneChunk(Vector2Int coord, TerrainConfig config, HighwayWinManager.FlatCorridorReference reference)
    {
        if (config == null)
            return false;

        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        float spacing = Mathf.Max(1f, config.TreeZoneSpacingFromFlatMeters);
        if (chunkSizeWorld <= 0f || spacing <= 0f)
            return false;

        int perpendicularChunkIndex = reference.PerpendicularAxisIsX ? coord.x : coord.y;
        int alongChunkIndex = reference.PerpendicularAxisIsX ? coord.y : coord.x;
        float halfChunk = chunkSizeWorld * 0.5f;
        float perpendicularCenter = perpendicularChunkIndex * chunkSizeWorld + halfChunk;
        float alongCenter = alongChunkIndex * chunkSizeWorld + halfChunk;

        float distanceFromRoadCenter = Mathf.Abs(perpendicularCenter - reference.CenterCoordinate);
        float distanceFromFlatEdge = distanceFromRoadCenter - Mathf.Max(0f, reference.OuterHalfWidth);
        if (distanceFromFlatEdge <= 0f)
            return false;

        int perpendicularRing = Mathf.RoundToInt(distanceFromFlatEdge / spacing);
        if (perpendicularRing < 1)
            return false;

        float side = perpendicularCenter >= reference.CenterCoordinate ? 1f : -1f;
        float targetPerpendicular = reference.CenterCoordinate + side * (Mathf.Max(0f, reference.OuterHalfWidth) + perpendicularRing * spacing);
        if (perpendicularChunkIndex != GetNearestChunkIndexForWorldCoordinate(targetPerpendicular, chunkSizeWorld))
            return false;

        int alongRing = Mathf.RoundToInt((alongCenter - reference.AlongAnchorCoordinate) / spacing);
        float targetAlong = reference.AlongAnchorCoordinate + alongRing * spacing;
        return alongChunkIndex == GetNearestChunkIndexForWorldCoordinate(targetAlong, chunkSizeWorld);
    }

    private static bool CanEvaluateTreeZones(TerrainConfig config)
    {
        if (config == null)
            return false;

        return config.TreeZoneSpawnChance > 0f
            || (config.TreeZoneUseHighwayFlatSpacing && config.TreeZoneSpacingFromFlatMeters > 0f);
    }

    private static int GetNearestChunkIndexForWorldCoordinate(float worldCoordinate, float chunkSizeWorld)
    {
        return Mathf.RoundToInt((worldCoordinate - chunkSizeWorld * 0.5f) / chunkSizeWorld);
    }

    public static float GetTreeZoneInfluence(Vector2Int coord, TerrainConfig config, float worldX, float worldZ)
    {
        if (!IsTreeZoneChunk(coord, config))
            return 0f;

        if (!TryGetTreeZoneAreaBounds(coord, config, out float minX, out float maxX, out float minZ, out float maxZ))
            return 0f;

        if (worldX < minX || worldX > maxX || worldZ < minZ || worldZ > maxZ)
            return 0f;

        float edgeDistance = Mathf.Min(Mathf.Min(worldX - minX, maxX - worldX), Mathf.Min(worldZ - minZ, maxZ - worldZ));
        float zoneWidth = Mathf.Max(0.01f, maxX - minX);
        float edgeBlend = Mathf.Clamp(config.TreeZoneEdgeBlendMeters, 0.01f, zoneWidth * 0.5f);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance / edgeBlend));
    }

    public static bool TryGetTreeZoneAreaBounds(Vector2Int coord, TerrainConfig config, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;

        if (config == null)
            return false;

        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        if (chunkSizeWorld <= 0f)
            return false;

        float areaWidth = GetTreeZoneAreaWidthMeters(coord, config, chunkSizeWorld);
        float halfWidth = areaWidth * 0.5f;
        float centerX = coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f;
        float centerZ = coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f;

        minX = centerX - halfWidth;
        maxX = centerX + halfWidth;
        minZ = centerZ - halfWidth;
        maxZ = centerZ + halfWidth;
        return true;
    }

    public static float GetTreeZoneAreaWidthMeters(Vector2Int coord, TerrainConfig config)
    {
        if (config == null)
            return 0f;

        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        return GetTreeZoneAreaWidthMeters(coord, config, chunkSizeWorld);
    }

    private static float GetTreeZoneAreaWidthMeters(Vector2Int coord, TerrainConfig config, float chunkSizeWorld)
    {
        if (config == null || chunkSizeWorld <= 0f)
            return 0f;

        float minArea = Mathf.Max(1f, config.TreeZoneMinAreaMeters);
        float maxArea = Mathf.Max(1f, config.TreeZoneMaxAreaMeters);
        if (minArea > maxArea)
        {
            float oldMin = minArea;
            minArea = maxArea;
            maxArea = oldMin;
        }

        if (Mathf.Approximately(minArea, maxArea))
            return minArea;

        float t = HashToUnit(config.Seed, coord.x, coord.y, 7331);
        return Mathf.Lerp(minArea, maxArea, t);
    }

    public static TreeZoneTextureInfluence GetTreeZoneTextureInfluence(Vector2Int sampleCoord, TerrainConfig config, float worldX, float worldZ)
    {
        TreeZoneTextureInfluence result = new TreeZoneTextureInfluence
        {
            Influence = 0f,
            NormalizedPosition = Vector2.zero,
            SourceCoord = sampleCoord
        };

        if (!CanEvaluateTreeZones(config))
            return result;

        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        if (chunkSizeWorld <= 0f)
            return result;

        float maxAreaWidth = Mathf.Max(1f, Mathf.Max(config.TreeZoneMinAreaMeters, config.TreeZoneMaxAreaMeters));
        float scanDistance = chunkSizeWorld + maxAreaWidth * 0.5f;
        int scanRadius = Mathf.Max(1, Mathf.CeilToInt(scanDistance / chunkSizeWorld));

        for (int z = -scanRadius; z <= scanRadius; z++)
        {
            for (int x = -scanRadius; x <= scanRadius; x++)
            {
                Vector2Int coord = sampleCoord + new Vector2Int(x, z);
                if (!IsTreeZoneChunk(coord, config))
                    continue;

                if (!TryGetTreeZoneAreaBounds(coord, config, out float minX, out float maxX, out float minZ, out float maxZ))
                    continue;

                if (worldX < minX || worldX > maxX || worldZ < minZ || worldZ > maxZ)
                    continue;

                float edgeDistance = Mathf.Min(Mathf.Min(worldX - minX, maxX - worldX), Mathf.Min(worldZ - minZ, maxZ - worldZ));
                float textureEdgeBlend = Mathf.Max(0.01f, config.TreeZoneTextureEdgeBlendMeters);
                float influence = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edgeDistance / textureEdgeBlend));
                if (influence <= result.Influence)
                    continue;

                float width = Mathf.Max(0.01f, maxX - minX);
                float depth = Mathf.Max(0.01f, maxZ - minZ);
                result.Influence = influence;
                result.NormalizedPosition = new Vector2(
                    Mathf.Clamp01((worldX - minX) / width),
                    Mathf.Clamp01((worldZ - minZ) / depth));
                result.SourceCoord = coord;
            }
        }

        return result;
    }

    public static float GetTreeZoneBaseHeight(Vector2Int coord, TerrainConfig config)
    {
        if (config == null)
            return 0f;

        float minHeight = Mathf.Min(config.TreeZoneMinBaseHeight, config.TreeZoneMaxBaseHeight);
        float maxHeight = Mathf.Max(config.TreeZoneMinBaseHeight, config.TreeZoneMaxBaseHeight);
        if (Mathf.Approximately(minHeight, maxHeight))
            return minHeight;

        float t = HashToUnit(config.Seed, coord.x, coord.y, 4217);
        return Mathf.Lerp(minHeight, maxHeight, t);
    }

    public static TreeZoneTerrainInfluence GetTreeZoneTerrainInfluence(Vector2Int sampleCoord, TerrainConfig config, float worldX, float worldZ)
    {
        TreeZoneTerrainInfluence result = new TreeZoneTerrainInfluence
        {
            FlatChunkInfluence = 0f,
            LowWaveInfluence = 0f,
            BaseHeightInfluence = 0f,
            TargetBaseHeight = config != null ? config.BaseHeight : 0f,
            SourceCoord = sampleCoord
        };

        if (!CanEvaluateTreeZones(config))
            return result;

        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        if (chunkSizeWorld <= 0f)
            return result;

        float lowWaveRadius = Mathf.Max(0f, config.TreeZoneLowWaveRadius);
        float maxAreaWidth = Mathf.Max(1f, Mathf.Max(config.TreeZoneMinAreaMeters, config.TreeZoneMaxAreaMeters));
        float scanDistance = chunkSizeWorld + lowWaveRadius + maxAreaWidth * 0.5f;
        int scanRadius = Mathf.Max(1, Mathf.CeilToInt(scanDistance / chunkSizeWorld));

        for (int z = -scanRadius; z <= scanRadius; z++)
        {
            for (int x = -scanRadius; x <= scanRadius; x++)
            {
                Vector2Int coord = sampleCoord + new Vector2Int(x, z);
                if (!IsTreeZoneChunk(coord, config))
                    continue;

                float flatInfluence = GetTreeZoneInfluence(coord, config, worldX, worldZ);
                if (flatInfluence > result.FlatChunkInfluence)
                {
                    result.FlatChunkInfluence = flatInfluence;
                    result.SourceCoord = coord;
                }

                float baseHeightInfluence = flatInfluence * Mathf.Clamp01(config.TreeZoneFlatStrength);

                if (lowWaveRadius <= 0f)
                {
                    if (baseHeightInfluence > result.BaseHeightInfluence)
                    {
                        result.BaseHeightInfluence = baseHeightInfluence;
                        result.TargetBaseHeight = GetTreeZoneBaseHeight(coord, config);
                    }
                    continue;
                }

                float distanceFromChunk = GetDistanceFromTreeZoneAreaBounds(coord, config, worldX, worldZ);
                if (distanceFromChunk <= lowWaveRadius)
                {
                    float edgeBlend = Mathf.Max(0.01f, config.TreeZoneLowWaveEdgeBlend);
                    float fadeStart = Mathf.Max(0f, lowWaveRadius - edgeBlend);
                    float lowWaveInfluence = distanceFromChunk <= fadeStart
                        ? 1f
                        : 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distanceFromChunk - fadeStart) / edgeBlend));

                    result.LowWaveInfluence = Mathf.Max(result.LowWaveInfluence, lowWaveInfluence);
                    baseHeightInfluence = Mathf.Max(baseHeightInfluence, lowWaveInfluence * Mathf.Clamp01(config.TreeZoneLowWaveStrength));
                }

                if (baseHeightInfluence > result.BaseHeightInfluence)
                {
                    result.BaseHeightInfluence = baseHeightInfluence;
                    result.TargetBaseHeight = GetTreeZoneBaseHeight(coord, config);
                    result.SourceCoord = coord;
                }
            }
        }

        return result;
    }

    private static float GetDistanceFromChunkBounds(Vector2Int coord, TerrainConfig config, float worldX, float worldZ)
    {
        float chunkSizeWorld = (config.ChunkSize - 1) * config.CellSize;
        float minX = coord.x * chunkSizeWorld;
        float minZ = coord.y * chunkSizeWorld;
        float maxX = minX + chunkSizeWorld;
        float maxZ = minZ + chunkSizeWorld;

        float dx = Mathf.Max(Mathf.Max(minX - worldX, 0f), worldX - maxX);
        float dz = Mathf.Max(Mathf.Max(minZ - worldZ, 0f), worldZ - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float GetDistanceFromTreeZoneAreaBounds(Vector2Int coord, TerrainConfig config, float worldX, float worldZ)
    {
        if (!TryGetTreeZoneAreaBounds(coord, config, out float minX, out float maxX, out float minZ, out float maxZ))
            return float.PositiveInfinity;

        float dx = Mathf.Max(Mathf.Max(minX - worldX, 0f), worldX - maxX);
        float dz = Mathf.Max(Mathf.Max(minZ - worldZ, 0f), worldZ - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float HashToUnit(int seed, int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)(x * 374761393);
            h ^= (uint)(y * 668265263);
            h ^= (uint)salt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777216f;
        }
    }

    /// <summary>
    /// Centralized procedural height calculation. 
    /// Matches the logic in SandChunk.GenerateInitialTerrain.
    /// </summary>
    public float GetSurfaceHeight(float worldX, float worldZ, float mountainInfluence = 0f, float flatness = 0f, float oasisDipInfluence = 0f, float oasisFlatInfluence = 0f, float oasisRimInfluence = 0f, float oasisShoreRidgeInfluence = 0f)
    {
        if (Config == null) return 0;

        // FIXED: Use a "wrapped" seed for noise offsets to prevent floating point precision loss with very large seeds.
        // This ensures the terrain remains varied and has high-quality dunes even with multi-digit seeds.
        float s = (float)(Config.Seed % 50000);

        // Layer 1: "High Wave" - Sharp, tall dunes (Default)
        float freqHigh = Config.NoiseScale;
        float nHigh = Mathf.PerlinNoise(worldX * freqHigh + s, worldZ * freqHigh + s);
        float ridgedHigh = 1.0f - Mathf.Abs(2.0f * nHigh - 1.0f);
        float noiseHeightHigh = ridgedHigh * ridgedHigh;
        float heightHigh = noiseHeightHigh * Config.HeightMultiplier;

        // Layer 2: "Low Wave" - Smoother, less frequent dunes near mountains
        float freqLow = Config.NoiseScale * Config.MountainNoiseScaleMultiplier;
        float nLow = Mathf.PerlinNoise(worldX * freqLow + s * 1.5f, worldZ * freqLow + s * 1.5f);
        
        float ripple = Mathf.Sin(worldX * Config.SecondaryNoiseScale) * Mathf.Cos(worldZ * Config.SecondaryNoiseScale);
        float noiseHeightLow = (nLow * nLow) * 0.5f + ripple * 0.05f; 
        
        float effectiveFlattening = Config.MountainFlatteningFactor; 
        float flattenedMultiplier = Config.HeightMultiplier * effectiveFlattening;
        float heightLow = noiseHeightLow * flattenedMultiplier;

        // Blend between High and Low wave based on MOUNTAIN influence
        float finalNoiseHeight = Mathf.Lerp(heightHigh, heightLow, mountainInfluence);

        // 1. Calculate a very small 'micro wave' for the flat area
        float microWave = (Mathf.Sin(worldX * 0.2f) + Mathf.Cos(worldZ * 0.2f)) * (Config.MicroWaveHeight * 0.16f) 
                        + Mathf.PerlinNoise(worldX * 0.08f + s, worldZ * 0.08f + s) * Config.MicroWaveHeight;

        float chunkSizeWorld = GetChunkSizeWorld();
        Vector2Int sampleCoord = chunkSizeWorld > 0f
            ? new Vector2Int(Mathf.FloorToInt(worldX / chunkSizeWorld), Mathf.FloorToInt(worldZ / chunkSizeWorld))
            : Vector2Int.zero;
        TreeZoneTerrainInfluence treeZoneInfluence = GetTreeZoneTerrainInfluence(sampleCoord, Config, worldX, worldZ);
        float treeZoneFlattening = Mathf.Max(
            treeZoneInfluence.FlatChunkInfluence * Mathf.Clamp01(Config.TreeZoneFlatStrength),
            treeZoneInfluence.LowWaveInfluence * Mathf.Clamp01(Config.TreeZoneLowWaveStrength));
        float treeZoneOasisSuppression = 1f - Mathf.Clamp01(oasisDipInfluence + oasisFlatInfluence);
        treeZoneFlattening *= treeZoneOasisSuppression;
        float baseHeight = Mathf.Lerp(
            Config.BaseHeight,
            treeZoneInfluence.TargetBaseHeight,
            Mathf.Clamp01(treeZoneInfluence.BaseHeightInfluence * treeZoneOasisSuppression));
        
        // 2. Calculate patch-based asymmetrical pileup using low-frequency noise
        float pileUpNoise = Mathf.PerlinNoise(worldX * Config.MountainPileupNoiseScale + s, 
                                              worldZ * Config.MountainPileupNoiseScale + s);
        
        float pileupFactor = Mathf.Clamp01((pileUpNoise - 0.4f) * 2.0f); 
        float pileUpHeight = Mathf.Pow(Mathf.Clamp01(flatness), 3.0f) * pileupFactor * Config.MountainPileupMaxHeight; 
        
        // Suppress mountain pileup inside the oasis basin to keep the bowl clean
        pileUpHeight *= (1.0f - oasisDipInfluence);

        // Blend the heavy dunes down to the gentle micro waves — oasis completely flattens dunes out to basin+100
        float combinedFlatness = Mathf.Max(Mathf.Max(Mathf.Clamp01(flatness), oasisFlatInfluence), treeZoneFlattening);
        
        // ── Oasis Concave Basin Logic ──
        // Use a CONSTANT rim height (no noise) so the bowl interior is perfectly smooth.
        // microWave noise would create bumps on the basin floor — we don't want that.
        float rimHeight = Config.BaseHeight;
        
        if (oasisDipInfluence > 0f)
        {
            // Scale depth dynamically based on radius (baseline = 50m)
            float scaleFactor = Mathf.Max(1.0f, Config.OasisBasinRadius / 50f);
            float dynamicDepth = Config.OasisBasinDepth * 2.5f * scaleFactor;
            
            // LINEAR DIP: constant slope from rim to center.
            //   oasisDipInfluence = 0 at the rim, 1 at the center.
            //   The slope is the same everywhere — no flat spots at all.
            //   The center is always the absolute lowest point.
            float oasisDip = oasisDipInfluence * dynamicDepth;
            
            // Inside the basin: start from BaseHeight + Rim and carve downward.
            float h = rimHeight + (oasisRimInfluence * Config.OasisRimHeight) - oasisDip;
            h = Mathf.Max(h, -Config.BottomDepth + 1f);
            if (HighwayWinManager.TryApplyHighwayFlattening(worldX, worldZ, h, out float highwayHeight, out _))
                return highwayHeight;

            return h;
        }
        
        // Outside the basin but within the flat transition zone: enforce rim as a floor.
        // This prevents any sand noise valley from cutting below the bowl edge.
        float flattenedDunes = Mathf.Lerp(finalNoiseHeight, microWave, combinedFlatness);
        if (oasisFlatInfluence > 0f)
        {
            finalNoiseHeight = Mathf.Max(flattenedDunes, Mathf.Abs(microWave));
        }
        else
        {
            finalNoiseHeight = flattenedDunes;
        }

        // Add Base + Noise + Pileup + Rim Height + NEW Shore Ridge
        float hFinal = baseHeight + finalNoiseHeight + pileUpHeight + (oasisRimInfluence * Config.OasisRimHeight) + (oasisShoreRidgeInfluence * Config.OasisRimHeight * 0.5f);
        hFinal = Mathf.Max(hFinal, -Config.BottomDepth + 1f);
        if (HighwayWinManager.TryApplyHighwayFlattening(worldX, worldZ, hFinal, out float finalHighwayHeight, out _))
            return finalHighwayHeight;

        return hFinal;
    }

    #region Oasis Logic

    private bool IsTooCloseToMountain(Vector2 worldPos, float minDistance)
    {
        if (MountainSpawner.Instance == null) return false;
        float chunkSizeWorld = GetChunkSizeWorld();
        int pX = Mathf.FloorToInt(worldPos.x / chunkSizeWorld);
        int pZ = Mathf.FloorToInt(worldPos.y / chunkSizeWorld);
        var mountains = MountainSpawner.Instance.GetNearbyMountains(new Vector2Int(pX, pZ));
        foreach (var m in mountains)
        {
            if (Vector2.Distance(worldPos, m.position) < minDistance)
                return true;
        }
        return false;
    }

    public List<OasisData> GetNearbyOases(Vector2Int targetCoord)
    {
        List<OasisData> result = new List<OasisData>();
        if (Config == null) return result;

        float chunkSizeWorld = GetChunkSizeWorld();
        // Extend search radius by 100 to catch the low-wave influence zone
        int searchRadius = Mathf.CeilToInt((Config.OasisBasinRadius + 100f) / chunkSizeWorld) + 1;

        // Test mode: spawn exactly ONE oasis at a fixed chunk near player
        if (Config.OasisTestMode)
        {
            // Pick a test chunk just outside safe radius
            int testOffsetChunks = Mathf.CeilToInt(Config.OasisSafeRadius / chunkSizeWorld) + 1;
            Vector2Int spawnChunk = new Vector2Int(
                Mathf.FloorToInt(Config.PlayerSpawnPoint.x / chunkSizeWorld),
                Mathf.FloorToInt(Config.PlayerSpawnPoint.z / chunkSizeWorld)
            );
            Vector2Int testCoord = spawnChunk + new Vector2Int(testOffsetChunks, 0);

            // Check if testCoord is within search range of targetCoord
            if (Mathf.Abs(testCoord.x - targetCoord.x) <= searchRadius
                && Mathf.Abs(testCoord.y - targetCoord.y) <= searchRadius)
            {
                float worldX = testCoord.x * chunkSizeWorld + chunkSizeWorld * 0.5f;
                float worldZ = testCoord.y * chunkSizeWorld + chunkSizeWorld * 0.5f;
                result.Add(new OasisData {
                    position = new Vector2(worldX, worldZ),
                    basinRadius = Config.OasisBasinRadius,
                    waterRadius = Config.OasisWaterRadius,
                    basinDepth = Config.OasisBasinDepth,
                    chunkCoord = testCoord
                });
            }
            return result;
        }

        // Normal mode: deterministic random per chunk
        for (int y = -searchRadius; y <= searchRadius; y++) {
            for (int x = -searchRadius; x <= searchRadius; x++) {
                Vector2Int coord = targetCoord + new Vector2Int(x, y);

                Random.State oldState = Random.state;
                Random.InitState(Config.Seed + coord.x * 54321 + coord.y * 98765);

                if (Random.value < Config.OasisSpawnChance) {
                    float worldX = coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f;
                    float worldZ = coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f;

                    Vector3 pos = new Vector3(worldX, 0, worldZ);
                    if (Vector3.Distance(pos, Config.PlayerSpawnPoint) >= Config.OasisSafeRadius
                        && !IsTooCloseToMountain(new Vector2(worldX, worldZ), Config.OasisMountainMinDistance)) {
                        result.Add(new OasisData {
                            position = new Vector2(worldX, worldZ),
                            basinRadius = Config.OasisBasinRadius,
                            waterRadius = Config.OasisWaterRadius,
                            basinDepth = Config.OasisBasinDepth,
                            chunkCoord = coord
                        });
                    }
                }
                Random.state = oldState;
            }
        }
        return result;
    }

    public void LoadOasisAssetsForChunk(Vector2Int coord)
    {
        if (_activeOasisAssets.ContainsKey(coord)) return;

        float chunkSizeWorld = GetChunkSizeWorld();
        bool shouldSpawn = false;

        if (Config.OasisTestMode)
        {
            // Test mode: only spawn at the fixed test chunk
            int testOffsetChunks = Mathf.CeilToInt(Config.OasisSafeRadius / chunkSizeWorld) + 1;
            Vector2Int spawnChunk = new Vector2Int(
                Mathf.FloorToInt(Config.PlayerSpawnPoint.x / chunkSizeWorld),
                Mathf.FloorToInt(Config.PlayerSpawnPoint.z / chunkSizeWorld)
            );
            Vector2Int testCoord = spawnChunk + new Vector2Int(testOffsetChunks, 0);
            shouldSpawn = (coord == testCoord);
        }
        else
        {
            Random.State oldState = Random.state;
            Random.InitState(Config.Seed + coord.x * 54321 + coord.y * 98765);
            shouldSpawn = (Random.value < Config.OasisSpawnChance);
            Random.state = oldState;
        }

        if (!shouldSpawn) return;

        float worldX = coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f;
        float worldZ = coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f;
        Vector3 center = new Vector3(worldX, 0, worldZ);

        if (!Config.OasisTestMode) {
            if (Vector3.Distance(center, Config.PlayerSpawnPoint) < Config.OasisSafeRadius
                || IsTooCloseToMountain(new Vector2(worldX, worldZ), Config.OasisMountainMinDistance)) {
                return;
            }
        }

        // Use a deterministic seed for vegetation placement
        Random.State vegState = Random.state;
        Random.InitState(Config.Seed + coord.x * 11111 + coord.y * 22222);

        // ALWAYS pick the vegetation angle FIRST so it matches the terrain generation in SampleHeight
        float oasisVegAngle = Random.Range(0f, Mathf.PI * 2f);

        List<GameObject> assets = new List<GameObject>();

        // Dynamically calculate the very bottom center of the concave bowl
        float centerHeight = SampleHeight(center);
        
        // Water level is explicitly offset above the deepest part of the concave middle
        float dynamicWaterLevel = centerHeight + Config.OasisWaterHeightOffset;

        // Water — scale to cover OasisWaterRadius area, and sink it into the calculated bowl height
        if (Config.OasisWaterPrefab != null) {
            GameObject water = Instantiate(Config.OasisWaterPrefab,
                new Vector3(worldX, dynamicWaterLevel, worldZ), Quaternion.identity, transform);
            // Scale water to match OasisWaterRadius (assuming prefab base mesh ~10 units wide)
            float waterScale = Config.OasisWaterRadius * 0.2f;
            water.transform.localScale = new Vector3(waterScale, 1f, waterScale);
            assets.Add(water);
        }

        // Spawn Fish school
        SpawnOasisFish(center, dynamicWaterLevel, Config.OasisWaterRadius, assets);

        // Palms — ensure they form closely around the visible water
        int palmCount = Config.OasisPalmCount;
        int spawnedPalms = 0;
        int maxPalmAttempts = 2000;
        int palmAttempts = 0;
        
        List<Vector3> palmPositions = new List<Vector3>();

        while (spawnedPalms < palmCount && palmAttempts < maxPalmAttempts) {
            palmAttempts++;
            // Confine to a 120 degree wedge (1/3 of shore) on the 'vegetation side'
            float angle = oasisVegAngle + Random.Range(-Mathf.PI / 3f, Mathf.PI / 3f);
            
            // SHORELINE SEEKER: Seek the "touching point" (coastal) between water and sand
            float dist = 0f;
            bool foundShore = false;
            // Scan from 5m out to BasinRadius to find the exact shoreline transition
            for (float d = 5.0f; d < Config.OasisBasinRadius; d += 2.0f) {
                Vector3 checkPos = center + new Vector3(Mathf.Cos(angle) * d, 0, Mathf.Sin(angle) * d);
                float h = SampleHeight(checkPos);
                if (h >= dynamicWaterLevel - 0.1f) {
                    dist = d + 2.5f; // Place 2.5 meters back from the coastal point into the sand
                    foundShore = true;
                    break;
                }
            }
            if (!foundShore) continue;

            Vector3 pPos = center + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
            
            // Final vertical alignment via Raycast (or SampleHeight fallback)
            Vector3 palmRayOrigin = new Vector3(pPos.x, 500f, pPos.z);
            if (Physics.Raycast(palmRayOrigin, Vector3.down, out RaycastHit palmHit, 1000f)) {
                pPos.y = palmHit.point.y;
            } else {
                pPos.y = SampleHeight(pPos);
            }
            
            // Verify final position is within a reasonable shore height range
            if (pPos.y < dynamicWaterLevel - 0.2f) continue; 
            if (pPos.y > dynamicWaterLevel + 4.0f) continue;
            
            // Overlap check: ensure palms are spawned at least ~2 meters apart from each other
            bool isTooClose = false;
            foreach (var spawnedPos in palmPositions) {
                if (Vector3.Distance(spawnedPos, pPos) < 2.0f) {
                    isTooClose = true;
                    break;
                }
            }
            if (isTooClose) continue;

            if (Config.OasisPalmPrefabs.Count > 0) {
                float palmScale = Random.Range(8.0f, 10.0f); // Massive scale (8x to 10x)
                // Adjust vertical position: sink a portion into the sand
                Vector3 palmSpawnPos = pPos;
                palmSpawnPos.y -= palmScale * 0.35f; 

                GameObject palm = Instantiate(
                    Config.OasisPalmPrefabs[Random.Range(0, Config.OasisPalmPrefabs.Count)],
                    palmSpawnPos, Quaternion.Euler(0, Random.Range(0, 360f), 0), transform);
                palm.transform.localScale = Vector3.one * palmScale;
                
                // Add simple collider so it registers for plant protection (digging prevention)
                if (palm.GetComponent<Collider>() == null) {
                    CapsuleCollider cap = palm.AddComponent<CapsuleCollider>();
                    cap.radius = 0.4f;
                    cap.height = 10.0f;
                    cap.center = new Vector3(0, 5.0f, 0);
                }
                
                assets.Add(palm);
                palmPositions.Add(pPos); // Keep ground position for bushes to reference
                spawnedPalms++;
            } else {
                break;
            }
        }

        // Instead of spawning bushes now, we store the oasis parameters.
        // Bushes will spawn dynamically when the player gets close enough.
        // We also store the palm positions to allow bushes to spread around them.
        _oasisBushData[coord] = new OasisBushSpawnData {
            center = center,
            vegAngle = oasisVegAngle,
            waterLevel = dynamicWaterLevel,
            palmPos = palmPositions,
            bushesSpawned = false
        };

        _activeOasisAssets[coord] = assets;
        Random.state = vegState;
    }

    private void SpawnOasisFish(Vector3 center, float waterLevel, float radius, List<GameObject> assets)
    {
        if (Config.OasisFishPrefabs == null || Config.OasisFishPrefabs.Count == 0) {
            Debug.LogWarning("[OasisFish] SpawnOasisFish skipped: No fish prefabs assigned in Config!");
            return;
        }

        int count = Config.OasisFishCountPerOasis;
        for (int i = 0; i < count; i++)
        {
            // Spawn closer to surface and spread out in radius
            Vector2 randomCircle = Random.insideUnitCircle * (radius * 0.9f);
            Vector3 spawnPos = new Vector3(center.x + randomCircle.x, waterLevel - 0.2f, center.z + randomCircle.y);

            GameObject prefab = Config.OasisFishPrefabs[Random.Range(0, Config.OasisFishPrefabs.Count)];
            // Instantiate with identity rotation first to make bounds calculation easier
            GameObject fish = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
            fish.name = "OasisFish_" + i;

            // Visibility Boost: Massive fish as requested
            fish.transform.localScale = Vector3.one * 10.0f;

            // Combat & Behavior
            if (fish.GetComponent<AnimalHealth>() == null) fish.AddComponent<AnimalHealth>();
            
            OasisFishAI ai = fish.GetComponent<OasisFishAI>();
            if (ai == null) ai = fish.AddComponent<OasisFishAI>();
            
            // Assign a unique ID based on the oasis position so fish can identify their group
            ai.OasisID = center.GetHashCode(); 
            
            ai.OasisCenter = center;
            ai.SwimRadius = radius * 0.95f;
            ai.WaterLevel = waterLevel;
            ai.MaxDepth = Config.OasisFishMaxSwimDepth;

            // Ensure collider for attack detection - matched to dynamic scale
            // FORCED CLEANUP: Purge any pre-existing colliders that might be improperly sized in the prefab
            foreach (var c in fish.GetComponentsInChildren<Collider>()) 
            {
                // We cannot destroy CharacterController because scripts like CreatureMover depend on it
                if (c is CharacterController) continue; 
                Destroy(c);
            }
            
            // Shrink existing CharacterController if it exists so it doesn't interfere with our precise box
            var cc = fish.GetComponent<CharacterController>();
            if (cc != null) 
            {
                cc.radius = 0.01f;
                cc.height = 0.01f;
                cc.center = Vector3.zero;
            }

            // Add fresh dynamic BoxCollider for accurate hit detection
            var box = fish.AddComponent<BoxCollider>();
            
            // Dynamically calculate bounds from all relevant renderers
            Bounds combinedBounds = new Bounds(fish.transform.position, Vector3.zero);
            Renderer[] renderers = fish.GetComponentsInChildren<Renderer>();
            bool foundRenderer = false;
            foreach (var r in renderers)
            {
                // Ignore particles and other non-physical effects
                if (r is ParticleSystemRenderer) continue;

                if (!foundRenderer) { combinedBounds = r.bounds; foundRenderer = true; }
                else combinedBounds.Encapsulate(r.bounds);
            }

            if (foundRenderer)
            {
                // Convert world bounds to local space
                box.center = fish.transform.InverseTransformPoint(combinedBounds.center);
                box.size = fish.transform.InverseTransformVector(combinedBounds.size);
                
                // Slightly shrink to ensure it stays "inside" the mesh slightly
                box.size *= 0.9f;
            }
            else
            {
                // Fallback to previous small size if no renderer found
                box.size = new Vector3(0.04f, 0.04f, 0.1f);
                box.center = Vector3.zero;
            }
            
            // Now apply random rotation after collider is set
            fish.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            
            assets.Add(fish);
        }
    }

    public void SpawnBushesNearPlayer()
    {
        if (Config == null || Config.OasisBushPrefabs.Count == 0) return;

        float sqrTriggerDist = Config.BushSpawnTriggerDistance * Config.BushSpawnTriggerDistance;
        Vector3 playerPos = Player.position;

        // Collect coordinates to avoid modifying dictionary during iteration
        List<Vector2Int> chunksToSpawn = new List<Vector2Int>();

        foreach (var kvp in _oasisBushData)
        {
            if (kvp.Value.bushesSpawned) continue;

            // Check distance to oasis center
            if ((playerPos - kvp.Value.center).sqrMagnitude <= sqrTriggerDist * 4f) // Larger trigger for whole oasis
            {
                chunksToSpawn.Add(kvp.Key);
            }
        }

        foreach (var coord in chunksToSpawn)
        {
            OasisBushSpawnData data = _oasisBushData[coord];
            SpawnDynamicBushesForOasis(coord, data);
            data.bushesSpawned = true;
            _oasisBushData[coord] = data; // Update struct in dict
        }
    }

    private void SpawnDynamicBushesForOasis(Vector2Int coord, OasisBushSpawnData data)
    {
        if (!_activeOasisBushes.ContainsKey(coord))
        {
            _activeOasisBushes[coord] = new List<GameObject>();
        }

        List<GameObject> chunkBushes = _activeOasisBushes[coord];
        
        int bushCount = Config.OasisBushCount;
        int maxAttempts = Mathf.Max(2000, bushCount * 10);
        // Focused Spawning: Place 8-10 bushes around each palm tree location
        foreach (var tPos in data.palmPos) {
            int bushesThisTree = Random.Range(8, 11);
            for (int i = 0; i < bushesThisTree; i++) {
                // Pick a small radius around the tree (1.0m to 3.0m)
                float r = Random.Range(1.0f, 3.0f);
                float a = Random.Range(0, Mathf.PI * 2f);
                Vector3 bPos = new Vector3(tPos.x + Mathf.Cos(a) * r, 0, tPos.z + Mathf.Sin(a) * r);
                
                // Get precise vertical position
                Vector3 rayOrigin = new Vector3(bPos.x, 500f, bPos.z);
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f)) {
                    bPos.y = hit.point.y;
                } else {
                    bPos.y = SampleHeight(bPos);
                }

                // SUITABILITY: Check slope to ensure ground is reasonably flat for a bush
                float hCenter = bPos.y;
                float hOff = SampleHeight(bPos + new Vector3(0.5f, 0, 0.5f));
                float slope = Mathf.Abs(hCenter - hOff) / 0.707f; // Approx diagonal distance slope
                bool isSuitable = slope < 0.8f; // Skip steep basin walls

                // SHORE ZONE: Check if spot is coastal (within 4m of water surface)
                bool inShoreZone = bPos.y >= data.waterLevel - 0.2f && bPos.y <= data.waterLevel + 4.0f;

                if (!isSuitable || !inShoreZone) continue;

                GameObject prefab = Config.OasisBushPrefabs[Random.Range(0, Config.OasisBushPrefabs.Count)];
                GameObject bush = Instantiate(prefab, bPos, Quaternion.Euler(0, Random.Range(0, 360f), 0), transform);
                bush.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f); // Small scale variety
                chunkBushes.Add(bush);
            }
        }
    }

    public void UnloadDistantOases(HashSet<Vector2Int> activeCoords)
    {
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in _activeOasisAssets.Keys) {
            if (!activeCoords.Contains(coord)) toRemove.Add(coord);
        }
        foreach (var coord in toRemove) {
            // Destroy static assets (water, palms)
            foreach (var go in _activeOasisAssets[coord]) {
                if (go != null) Destroy(go);
            }
            _activeOasisAssets.Remove(coord);
            
            // Destroy dynamic bushes
            if (_activeOasisBushes.ContainsKey(coord)) {
                foreach (var go in _activeOasisBushes[coord]) {
                    if (go != null) Destroy(go);
                }
                _activeOasisBushes.Remove(coord);
            }
            
            // Clear tracking data
            if (_oasisBushData.ContainsKey(coord)) {
                _oasisBushData.Remove(coord);
            }
        }
    }

    #endregion

    void DestroyChunk(Vector2Int coord)
    {
        if (_chunks.TryGetValue(coord, out SandChunk chunk))
        {
            // SAVE DATA before destroying
            if (chunk.IsInitialized)
            {
                // We need to copy NativeArray to managed array to persist it
                float[] data = new float[chunk.HeightsRead.Length];
                chunk.HeightsRead.CopyTo(data);
                
                if (_persistentChunkData.ContainsKey(coord))
                {
                    _persistentChunkData[coord] = data;
                }
                else
                {
                    _persistentChunkData.Add(coord, data);
                }
            }

            // IMPORTANT: NativeArrays must be disposed!
            // chunk.OnDestroy() handles this if GameObject is destroyed.
            Destroy(chunk.gameObject);
            _chunks.Remove(coord);
        }
    }
}
