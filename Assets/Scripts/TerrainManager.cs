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

    private Dictionary<Vector2Int, SandChunk> _chunks = new Dictionary<Vector2Int, SandChunk>();
    private Vector2Int _currentChunkCoord;

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
    public Vector2Int TestMountainChunk { get; private set; } = new Vector2Int(-99999, -99999);

    void Awake()
    {
        _instance = this;
        if (ChunkPrefab == null)
        {
            Debug.LogError("ChunkPrefab is not assigned in TerrainManager!");
        }
    }

    void Start()
    {
        if (Config == null) return;
        
        Debug.Log("[OasisFish] TerrainManager.Start: initializing prefab list.");
        Config.LoadedOasisFishPrefabs.Clear();

#if UNITY_EDITOR
        if (Config.OasisWaterPrefab == null)
            Config.OasisWaterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/for_oasis/Water Specular Mirror.prefab");
            
        foreach (var p in Config.OasisPalmPrefabPaths) {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (prefab != null) Config.LoadedOasisPalmPrefabs.Add(prefab);
        }
        foreach (var p in Config.OasisBushPrefabPaths) {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (prefab != null) Config.LoadedOasisBushPrefabs.Add(prefab);
        }
        foreach (var p in Config.OasisFishPrefabPaths) {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (prefab != null) {
                Config.LoadedOasisFishPrefabs.Add(prefab);
                Debug.Log($"[OasisFish] Loaded prefab: {p}");
            } else {
                Debug.LogError($"[OasisFish] FAILED to load prefab at: {p}");
            }
        }
#endif
        Debug.Log($"[OasisFish] Total fish prefabs loaded: {Config.LoadedOasisFishPrefabs.Count}");

        if (Config.MountainTestMode)
        {
            TestMountainChunk = new Vector2Int(0, 5);
        }
    }

    private float _simTimer = 0f;

    void Update()
    {
        if (Player == null) return;

        UpdateChunks();

        // 1. Every-Frame LOD Updates (Distance check must be fast)
        float chunkSizeWorld = (Config.ChunkSize - 1) * Config.CellSize;
        foreach (var chunk in _chunks.Values) {
            if (!chunk.IsInitialized) continue;
            
            float dist2D = Vector2.Distance(
                new Vector2(Player.position.x, Player.position.z), 
                new Vector2(chunk.transform.position.x + chunkSizeWorld * 0.5f, chunk.transform.position.z + chunkSizeWorld * 0.5f)
            );
            chunk.UpdateLOD(dist2D, Config.ColliderLODDistance, Config.SimulationLODDistance, Player.position);
        }

        // 2. Throttled Simulation (0.1s interval)
        _simTimer += Time.deltaTime;
        if (_simTimer >= 0.1f)
        {
            _simTimer = 0f;
            Simulate();
        }

        // 3. Dynamic Bush Spawning — bushes form from sand when player approaches palm trees
        _bushCheckTimer += Time.deltaTime;
        if (_bushCheckTimer >= 0.2f)
        {
            _bushCheckTimer = 0f;
            SpawnBushesNearPlayer();
        }
    }

    void Simulate()
    {
        if (_chunks.Count == 0) return;

        int chunkCount = _chunks.Count;
        Unity.Collections.NativeArray<Unity.Jobs.JobHandle> simHandles = new Unity.Collections.NativeArray<Unity.Jobs.JobHandle>(chunkCount, Unity.Collections.Allocator.Temp);

        // 1. Reset Flags and Schedule Simulation
        int i = 0;
        List<SandChunk> chunkList = new List<SandChunk>(_chunks.Values);
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
            if (!chunk.IsSimulating) {
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
                ReadHeights = chunk.HeightsRead, WriteHeights = chunk.HeightsWrite,
                
                ReadN = (nN != null && nN.IsInitialized && nN.HeightsRead.IsCreated) ? nN.HeightsRead : chunk.HeightsRead,
                ReadS = (nS != null && nS.IsInitialized && nS.HeightsRead.IsCreated) ? nS.HeightsRead : chunk.HeightsRead,
                ReadE = (nE != null && nE.IsInitialized && nE.HeightsRead.IsCreated) ? nE.HeightsRead : chunk.HeightsRead,
                ReadW = (nW != null && nW.IsInitialized && nW.HeightsRead.IsCreated) ? nW.HeightsRead : chunk.HeightsRead,
                HasN = nN != null && nN.IsInitialized && nN.HeightsRead.IsCreated, 
                HasS = nS != null && nS.IsInitialized && nS.HeightsRead.IsCreated,
                HasE = nE != null && nE.IsInitialized && nE.HeightsRead.IsCreated, 
                HasW = nW != null && nW.IsInitialized && nW.HeightsRead.IsCreated,

                ReadNE = (nNE != null && nNE.IsInitialized && nNE.HeightsRead.IsCreated) ? nNE.HeightsRead : chunk.HeightsRead,
                ReadNW = (nNW != null && nNW.IsInitialized && nNW.HeightsRead.IsCreated) ? nNW.HeightsRead : chunk.HeightsRead,
                ReadSE = (nSE != null && nSE.IsInitialized && nSE.HeightsRead.IsCreated) ? nSE.HeightsRead : chunk.HeightsRead,
                ReadSW = (nSW != null && nSW.IsInitialized && nSW.HeightsRead.IsCreated) ? nSW.HeightsRead : chunk.HeightsRead,
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
            if (chunk.IsInitialized && chunk.IsSimulating) {
                chunk.SwapBuffers();
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
            
            // Try to find a valid spot on the apron sand to teleport the player
            Vector3 treeWorldPos = targetPos;
            bool found = false;
            for (int i = 0; i < 50; i++)
            {
                Vector3 checkPos = targetPos + new Vector3(Random.Range(-50, 50), 0, Random.Range(-50, 50));
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

        // 4. Load/Unload Assets (Mountains and Oases can load early as they don't depend on prewarmed sand)
        if (MountainSpawner.Instance != null)
        {
            foreach (var coord in mountainCoords)
            {
                MountainSpawner.Instance.LoadMountainsForChunk(coord);
                LoadOasisAssetsForChunk(coord);
            }
            MountainSpawner.Instance.UnloadDistantMountains(mountainCoords);
            UnloadDistantOases(mountainCoords);
        }
        else
        {
            foreach (var coord in mountainCoords)
            {
                LoadOasisAssetsForChunk(coord);
            }
            UnloadDistantOases(mountainCoords);
        }

        // 5. Cleanup old terrain chunks
        List<Vector2Int> newlyCreated = new List<Vector2Int>();
        foreach (var coord in activeCoords)
        {
            if (!_chunks.ContainsKey(coord))
            {
                CreateChunk(coord);
                newlyCreated.Add(coord);
            }
        }

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kvp in _chunks)
        {
            if (!activeCoords.Contains(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var coord in toRemove)
        {
            DestroyChunk(coord);
        }

        // 6. Neighbor cache, Edge Syncing, and Mesh Rebuilding
        if (newlyCreated.Count > 0 || toRemove.Count > 0)
        {
            foreach (var chunk in _chunks.Values) {
                RefreshNeighbors(chunk);
            }
            
            foreach (var coord in newlyCreated)
            {
                if (_chunks.TryGetValue(coord, out SandChunk chunk))
                {
                    chunk.SyncEdgesFromNeighbors();
                }
            }

            // Sync edges for existing neighbors as well
            HashSet<SandChunk> toRebuild = new HashSet<SandChunk>();
            foreach (var coord in newlyCreated)
            {
                if (_chunks.TryGetValue(coord, out SandChunk c))
                {
                    toRebuild.Add(c);
                    for (int n = 0; n < 8; n++)
                    {
                        SandChunk nb = c.Neighbors[n];
                        if (nb != null && nb.IsInitialized && !newlyCreated.Contains(nb.ChunkCoord))
                        {
                            toRebuild.Add(nb);
                        }
                    }
                }
            }

            if (toRebuild.Count > 0)
            {
                List<SandChunk> chunkList = new List<SandChunk>(toRebuild);
                Unity.Collections.NativeArray<Unity.Jobs.JobHandle> handles = 
                    new Unity.Collections.NativeArray<Unity.Jobs.JobHandle>(chunkList.Count, Unity.Collections.Allocator.Temp);
                for (int m = 0; m < chunkList.Count; m++) handles[m] = chunkList[m].ScheduleMeshUpdate(default);
                Unity.Jobs.JobHandle.CompleteAll(handles);
                handles.Dispose();
                for (int m = 0; m < chunkList.Count; m++) chunkList[m].ApplyMeshUpdate();
            }
        }

        // 7. Load/Unload Vegetation (LAST: ensures chunks exist and are prewarmed for grounding)
        foreach (var coord in vegetationCoords)
        {
            if (PlantSpawner.Instance != null) PlantSpawner.Instance.LoadPlantsForChunk(coord);
            if (ApronBushSpawner.Instance != null) ApronBushSpawner.Instance.LoadBushesForChunk(coord);
        }

        if (PlantSpawner.Instance != null) PlantSpawner.Instance.UnloadDistantPlants(vegetationCoords);
        if (ApronBushSpawner.Instance != null) ApronBushSpawner.Instance.UnloadDistantBushes(vegetationCoords);
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
        chunk.UpdateLOD(dist2D, Config.ColliderLODDistance, Config.SimulationLODDistance, Player.position);
        
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

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (_chunks.TryGetValue(new Vector2Int(x, z), out SandChunk chunk))
                {
                    chunk.ModifyHeight(worldPos, amount, radius);
                }
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
            return chunk.GetHeightAt(worldPos);
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
        
        // 2. Calculate patch-based asymmetrical pileup using low-frequency noise
        float pileUpNoise = Mathf.PerlinNoise(worldX * Config.MountainPileupNoiseScale + s, 
                                              worldZ * Config.MountainPileupNoiseScale + s);
        
        float pileupFactor = Mathf.Clamp01((pileUpNoise - 0.4f) * 2.0f); 
        float pileUpHeight = Mathf.Pow(Mathf.Clamp01(flatness), 3.0f) * pileupFactor * Config.MountainPileupMaxHeight; 
        
        // Suppress mountain pileup inside the oasis basin to keep the bowl clean
        pileUpHeight *= (1.0f - oasisDipInfluence);

        // Blend the heavy dunes down to the gentle micro waves — oasis completely flattens dunes out to basin+100
        float combinedFlatness = Mathf.Max(Mathf.Clamp01(flatness), oasisFlatInfluence);
        
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
            return Mathf.Max(h, -Config.BottomDepth + 1f);
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
        float hFinal = Config.BaseHeight + finalNoiseHeight + pileUpHeight + (oasisRimInfluence * Config.OasisRimHeight) + (oasisShoreRidgeInfluence * Config.OasisRimHeight * 0.5f);
        return Mathf.Max(hFinal, -Config.BottomDepth + 1f);
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
            
            // Focus search strictly outside the water radius to prevent trees in the water.
            float dist = Random.Range(Config.OasisWaterRadius + 1.0f, Config.OasisWaterRadius * 1.5f); 
            Vector3 pPos = center + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
            
            // Raycast from high above to find actual terrain mesh surface
            Vector3 palmRayOrigin = new Vector3(pPos.x, 500f, pPos.z);
            if (Physics.Raycast(palmRayOrigin, Vector3.down, out RaycastHit palmHit, 1000f)) {
                pPos.y = palmHit.point.y;
            } else {
                pPos.y = SampleHeight(pPos);
            }
            
            // SHORELINE RULE: Compare ACTUAL ground level to water level. Must be above water level.
            // LOOSENED: Increased max height from +2.0 to +5.0 to ensure trees always find a spot even in steep oasis banks.
            if (pPos.y < dynamicWaterLevel + 0.05f) continue; // Ensure strictly above water
            if (pPos.y > dynamicWaterLevel + 5.0f) continue; 
            
            // Overlap check: ensure palms are spawned at least ~2 meters apart from each other
            bool isTooClose = false;
            foreach (var spawnedPos in palmPositions) {
                if (Vector3.Distance(spawnedPos, pPos) < 2.0f) {
                    isTooClose = true;
                    break;
                }
            }
            if (isTooClose) continue;

            if (Config.LoadedOasisPalmPrefabs.Count > 0) {
                float palmScale = Random.Range(1.5f, 3.0f);
                // "10 percentage sink tree bottom in the sand": Lower the actual visual position based on its size
                Vector3 palmSpawnPos = pPos;
                palmSpawnPos.y -= palmScale * 0.4f;

                GameObject palm = Instantiate(
                    Config.LoadedOasisPalmPrefabs[Random.Range(0, Config.LoadedOasisPalmPrefabs.Count)],
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
        if (Config.LoadedOasisFishPrefabs == null || Config.LoadedOasisFishPrefabs.Count == 0) {
            Debug.LogWarning("[OasisFish] SpawnOasisFish skipped: No fish prefabs loaded!");
            return;
        }

        int count = Config.OasisFishCountPerOasis;
        Debug.Log($"[OasisFish] Spawning {count} fish for oasis at {center}");

        for (int i = 0; i < count; i++)
        {
            // Spawn closer to surface and spread out in radius
            Vector2 randomCircle = Random.insideUnitCircle * (radius * 0.9f);
            Vector3 spawnPos = new Vector3(center.x + randomCircle.x, waterLevel - 0.2f, center.z + randomCircle.y);

            GameObject prefab = Config.LoadedOasisFishPrefabs[Random.Range(0, Config.LoadedOasisFishPrefabs.Count)];
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
            Debug.Log($"[OasisFish] Spawned massive {fish.name} at {spawnPos} (Scale 10)");
        }
    }

    public void SpawnBushesNearPlayer()
    {
        if (Config == null || Config.LoadedOasisBushPrefabs.Count == 0) return;

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
        int spawned = 0;
        int attempts = 0;
        
        while (spawned < bushCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Spawn strictly on the vegetation side (120 degree wedge, 1/3 of shore)
            float angle = data.vegAngle + Random.Range(-Mathf.PI / 3f, Mathf.PI / 3f);
            
            // Search along shoreline starting from water edge
            float dist = Random.Range(Config.OasisWaterRadius, Config.OasisWaterRadius * 1.5f);
            Vector3 bPos = data.center + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);

            // Raycast for actual terrain mesh surface
            float groundY;
            Vector3 rayOrigin = new Vector3(bPos.x, 500f, bPos.z);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f)) {
                groundY = hit.point.y;
            } else {
                groundY = SampleHeight(bPos);
            }
            bPos.y = groundY;

            // CONDITION: Must be in shore zone (<= 3m above water) OR near a tree (<= 1.5m radius)
            // LOOSENED: Increased shore zone height from +1.0 to +3.0 and distance from 1.25 to 1.5
            // This ensures bushes reliably spawn on the shoreline for any oasis seed.
            bool inShoreZone = bPos.y >= data.waterLevel && bPos.y <= data.waterLevel + 3.0f && dist <= Config.OasisWaterRadius * 1.50f;
            bool nearTree = false;
            foreach (var tPos in data.palmPos) {
                if (Vector3.Distance(new Vector3(bPos.x, 0, bPos.z), new Vector3(tPos.x, 0, tPos.z)) <= 1.5f) {
                    nearTree = true;
                    break;
                }
            }

            if (!inShoreZone && !nearTree) continue;

            GameObject prefab = Config.LoadedOasisBushPrefabs[Random.Range(0, Config.LoadedOasisBushPrefabs.Count)];
            GameObject bush = Instantiate(prefab, bPos, Quaternion.Euler(0, Random.Range(0, 360f), 0), transform);
            bush.transform.localScale = Vector3.one;

            chunkBushes.Add(bush);

            spawned++;
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
