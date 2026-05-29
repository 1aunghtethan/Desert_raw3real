using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns plant prefabs (trees, cacti) on the sand terrain.
/// Follows the same chunk-based streaming pattern as MountainSpawner.
/// </summary>
public class PlantSpawner : MonoBehaviour
{
    public static PlantSpawner Instance;
    private const float MaxTerrainGrassScale = 90f;
    private const float MaxTerrainGrassSnapDistance = 2f;
    
    private TerrainManager _tm;
    private Dictionary<Vector2Int, List<GameObject>> _activePlants = new Dictionary<Vector2Int, List<GameObject>>();
    
    [Header("Manual Overrides (Drag & Drop here)")]
    public List<GameObject> PlantPrefabs = new List<GameObject>();
    public List<GameObject> JoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> TerrainGrassPrefabs = new List<GameObject>();
    public List<TreeZoneTreeSpawnEntry> TreeZoneTreeSpawnEntries = new List<TreeZoneTreeSpawnEntry>();
    public List<GameObject> TreeZoneTreePrefabs = new List<GameObject>();

    [Header("Runtime Active Prefabs (Read Only)")]
    public List<GameObject> LoadedPlantPrefabs = new List<GameObject>();
    public List<GameObject> LoadedJoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> LoadedTerrainGrassPrefabs = new List<GameObject>();
    public List<TreeZoneTreeSpawnEntry> LoadedTreeZoneTreeSpawnEntries = new List<TreeZoneTreeSpawnEntry>();
    public List<GameObject> LoadedTreeZoneTreePrefabs = new List<GameObject>();

    [Header("Spawn Performance")]
    [SerializeField] private int MaxVegetationSpawnObjectsPerFrame = 24;
    [SerializeField] private int MaxVegetationPlacementAttemptsPerFrame = 360;
    [SerializeField] private int MaxTreeZoneLargeObjectsPerFrame = 2;

    private Queue<Vector2Int> _pendingPlantLoadQueue = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _loadingPlantChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _loadedPlantChunks = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _pendingRealGrassLoadQueue = new Queue<Vector2Int>();
    private HashSet<Vector2Int> _loadingRealGrassChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> _loadedRealGrassChunks = new HashSet<Vector2Int>();
    private Coroutine _plantLoadProcessor;

    private class SpawnFrameBudget
    {
        public int SpawnedObjects;
        public int PlacementAttempts;

        public void Reset()
        {
            SpawnedObjects = 0;
            PlacementAttempts = 0;
        }
    }

    private class SpawnRandomContext
    {
        public Random.State ExternalState;
    }

    private enum TreeZoneSpawnPhase
    {
        Joshua,
        OtherLarge,
        Pickup,
        Grass
    }
    
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _activePlants.Clear();
        _pendingPlantLoadQueue.Clear();
        _loadingPlantChunks.Clear();
        _loadedPlantChunks.Clear();
        _pendingRealGrassLoadQueue.Clear();
        _loadingRealGrassChunks.Clear();
        _loadedRealGrassChunks.Clear();
        _plantLoadProcessor = null;
        _tm = TerrainManager.Instance;
        if (_tm == null)
            Debug.LogError("[PlantSpawner] Requires TerrainManager to be in the scene!");
            
        RefreshPrefabReferences();
        
        Debug.Log($"[PlantSpawner] Started. Loaded prefabs count: {LoadedPlantPrefabs.Count}");
    }

    public void RefreshPrefabReferences()
    {
        if (_tm == null || _tm.Config == null) return;

        if (PlantPrefabs.Count > 0) LoadedPlantPrefabs = PlantPrefabs;
        else if (_tm.Config.LoadedPlantPrefabs != null) LoadedPlantPrefabs = _tm.Config.LoadedPlantPrefabs;

        if (JoshuaTreePrefabs.Count > 0) LoadedJoshuaTreePrefabs = JoshuaTreePrefabs;
        else if (_tm.Config.LoadedJoshuaTreePrefabs != null) LoadedJoshuaTreePrefabs = _tm.Config.LoadedJoshuaTreePrefabs;

        if (TerrainGrassPrefabs.Count > 0) LoadedTerrainGrassPrefabs = TerrainGrassPrefabs;
        else if (_tm.Config.TerrainGrassPrefabs != null) LoadedTerrainGrassPrefabs = _tm.Config.TerrainGrassPrefabs;

        RemoveMissingTreeZoneEntries(TreeZoneTreeSpawnEntries);
        if (HasValidTreeZoneEntries(TreeZoneTreeSpawnEntries))
            LoadedTreeZoneTreeSpawnEntries = TreeZoneTreeSpawnEntries;
        else if (HasValidTreeZoneEntries(_tm.Config.TreeZoneTreeSpawnEntries))
            LoadedTreeZoneTreeSpawnEntries = _tm.Config.TreeZoneTreeSpawnEntries;
        else
            LoadedTreeZoneTreeSpawnEntries = new List<TreeZoneTreeSpawnEntry>();

        if (TreeZoneTreePrefabs.Count > 0) LoadedTreeZoneTreePrefabs = TreeZoneTreePrefabs;
        else if (_tm.Config.TreeZoneTreePrefabs != null) LoadedTreeZoneTreePrefabs = _tm.Config.TreeZoneTreePrefabs;

        RemoveMissingPrefabs(LoadedPlantPrefabs);
        RemoveMissingPrefabs(LoadedJoshuaTreePrefabs);
        RemoveMissingTreeZoneEntries(LoadedTreeZoneTreeSpawnEntries);
        RemoveMissingPrefabs(LoadedTreeZoneTreePrefabs);
        EnsureTerrainGrassPrefabs();
    }

    public bool TryGetRandomTerrainGrassNear(Vector3 center, float radius, out Vector3 grassPosition)
    {
        grassPosition = center;

        if (!TryGetRandomTerrainGrassNear(center, radius, out GameObject selected))
            return false;

        grassPosition = selected.transform.position;
        return true;
    }

    public bool TryGetRandomTerrainGrassNear(Vector3 center, float radius, out GameObject selected)
    {
        selected = null;

        float sqrRadius = radius * radius;
        int matchedCount = 0;

        foreach (List<GameObject> plants in _activePlants.Values)
        {
            for (int i = plants.Count - 1; i >= 0; i--)
            {
                GameObject plant = plants[i];
                if (plant == null)
                {
                    plants.RemoveAt(i);
                    continue;
                }

                if (!IsTerrainGrassObject(plant))
                    continue;

                Vector3 offset = plant.transform.position - center;
                offset.y = 0f;
                if (offset.sqrMagnitude > sqrRadius)
                    continue;

                matchedCount++;
                if (Random.Range(0, matchedCount) == 0)
                    selected = plant;
            }
        }

        if (selected == null)
            return false;

        return true;
    }

    private static bool IsTerrainGrassObject(GameObject plant)
    {
        LootItem loot = plant.GetComponent<LootItem>();
        if (loot == null || loot.Data == null)
            return false;

        return loot.Data.name == "RealGrass_ItemData" || loot.Data.ItemName == "Real Grass";
    }

    /// <summary>
    /// Loads plants for a given chunk coordinate.
    /// </summary>
    public void LoadPlantsForChunk(Vector2Int coord)
    {
        if (_tm == null || _tm.Config == null) return;
        if (_loadedPlantChunks.Contains(coord)) return;
        if (_loadingPlantChunks.Contains(coord)) return;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _loadingPlantChunks.Add(coord);
        _pendingPlantLoadQueue.Enqueue(coord);

        if (_plantLoadProcessor == null)
            _plantLoadProcessor = StartCoroutine(ProcessPlantLoadQueue());
    }

    public bool IsRealGrassLoadedForChunk(Vector2Int coord)
    {
        return _loadedRealGrassChunks.Contains(coord) || _loadingRealGrassChunks.Contains(coord);
    }

    public void LoadRealGrassForChunk(Vector2Int coord)
    {
        if (_tm == null || _tm.Config == null) return;
        if (_loadedRealGrassChunks.Contains(coord)) return;
        if (_loadingRealGrassChunks.Contains(coord)) return;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _loadingRealGrassChunks.Add(coord);
        _pendingRealGrassLoadQueue.Enqueue(coord);

        if (_plantLoadProcessor == null)
            _plantLoadProcessor = StartCoroutine(ProcessPlantLoadQueue());
    }

    private IEnumerator ProcessPlantLoadQueue()
    {
        yield return null;

        while (_pendingPlantLoadQueue.Count > 0 || _pendingRealGrassLoadQueue.Count > 0)
        {
            if (_pendingPlantLoadQueue.Count > 0)
            {
                Vector2Int coord = _pendingPlantLoadQueue.Dequeue();
                if (!_loadingPlantChunks.Contains(coord) || !_activePlants.ContainsKey(coord))
                    continue;

                yield return LoadPlantsForChunkIncremental(coord);
                continue;
            }

            Vector2Int grassCoord = _pendingRealGrassLoadQueue.Dequeue();
            if (!_loadingRealGrassChunks.Contains(grassCoord) || !_activePlants.ContainsKey(grassCoord))
                continue;

            yield return LoadRealGrassForChunkIncremental(grassCoord);
        }

        _plantLoadProcessor = null;
    }

    private IEnumerator LoadPlantsForChunkIncremental(Vector2Int coord)
    {
        SpawnRandomContext randomContext = new SpawnRandomContext { ExternalState = Random.state };
        Random.InitState(_tm.Config.Seed + coord.x * 12345 + coord.y * 54321 + 7);

        SpawnFrameBudget budget = new SpawnFrameBudget();
        bool isTreeZoneChunk = TerrainManager.IsTreeZoneChunk(coord, _tm.Config);

        if (isTreeZoneChunk)
        {
            yield return SpawnTreeZoneGroveIncrementalPhased(coord, budget, randomContext);
            if (ShouldStopPlantLoad(coord))
            {
                Random.state = randomContext.ExternalState;
                yield break;
            }
        }

        if (Random.value < _tm.Config.PlantSpawnChance)
        {
            if (SpawnPlant(coord, false))
                budget.SpawnedObjects++;

            if (ShouldPauseVegetationSpawn(budget))
            {
                Random.State jobState = Random.state;
                Random.state = randomContext.ExternalState;
                yield return null;
                randomContext.ExternalState = Random.state;
                Random.state = jobState;
                budget.Reset();
                if (ShouldStopPlantLoad(coord))
                {
                    Random.state = randomContext.ExternalState;
                    yield break;
                }
            }
        }

        if (_tm.Config.MiniStonePrefabs != null && _tm.Config.MiniStonePrefabs.Count > 0)
        {
            if (Random.value < _tm.Config.MiniStoneSpawnChance)
            {
                int stoneCount = _tm.Config.MiniStonesPerChunk;
                for (int i = 0; i < stoneCount; i++)
                {
                    budget.PlacementAttempts++;

                    if (SpawnMiniStone(coord))
                        budget.SpawnedObjects++;

                    if (ShouldPauseVegetationSpawn(budget))
                    {
                        Random.State jobState = Random.state;
                        Random.state = randomContext.ExternalState;
                        yield return null;
                        randomContext.ExternalState = Random.state;
                        Random.state = jobState;
                        budget.Reset();
                        if (ShouldStopPlantLoad(coord))
                        {
                            Random.state = randomContext.ExternalState;
                            yield break;
                        }
                    }
                }
            }
        }

        if (_tm.Config.SmallBranchPrefabs != null && _tm.Config.SmallBranchPrefabs.Count > 0)
        {
            if (Random.value < _tm.Config.SmallBranchSpawnChance)
            {
                int branchCount = _tm.Config.SmallBranchesPerChunk;
                for (int i = 0; i < branchCount; i++)
                {
                    budget.PlacementAttempts++;

                    if (SpawnSmallBranch(coord))
                        budget.SpawnedObjects++;

                    if (ShouldPauseVegetationSpawn(budget))
                    {
                        Random.State jobState = Random.state;
                        Random.state = randomContext.ExternalState;
                        yield return null;
                        randomContext.ExternalState = Random.state;
                        Random.state = jobState;
                        budget.Reset();
                        if (ShouldStopPlantLoad(coord))
                        {
                            Random.state = randomContext.ExternalState;
                            yield break;
                        }
                    }
                }
            }
        }

        if (LoadedJoshuaTreePrefabs.Count > 0 && MountainSpawner.Instance != null)
        {
            var nearbyMountains = MountainSpawner.Instance.GetNearbyMountains(coord);
            foreach (var mountain in nearbyMountains)
            {
                // Unique deterministic seed for this mountain
                int mountainSeed = _tm.Config.Seed + (int)(mountain.position.x * 37) + (int)(mountain.position.y * 19);
                Random.State oldMountainState = Random.state;
                Random.InitState(mountainSeed);

                // 2 to 5 trees total per mountain!
                int treeCount = Random.Range(2, 6);

                for (int i = 0; i < treeCount; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    // Place it randomly within the flat apron
                    float distance = mountain.footprintRadius + Random.Range(2f, _tm.Config.MountainFlatRadius - 2f);

                    float treeX = mountain.position.x + Mathf.Cos(angle) * distance;
                    float treeZ = mountain.position.y + Mathf.Sin(angle) * distance;

                    // Bounds check for the current chunk
                    float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
                    float minX = coord.x * chunkSizeWorld;
                    float maxX = minX + chunkSizeWorld;
                    float minZ = coord.y * chunkSizeWorld;
                    float maxZ = minZ + chunkSizeWorld;

                    if (treeX >= minX && treeX < maxX && treeZ >= minZ && treeZ < maxZ
                        && IsOnApronSand(new Vector3(treeX, 0, treeZ)))
                    {
                        if (InstantiatePlantAtPos(new Vector3(treeX, 0, treeZ), coord, true))
                            budget.SpawnedObjects++;

                        if (ShouldPauseVegetationSpawn(budget))
                        {
                            Random.State jobState = Random.state;
                            Random.state = randomContext.ExternalState;
                            yield return null;
                            randomContext.ExternalState = Random.state;
                            Random.state = jobState;
                            budget.Reset();
                            if (ShouldStopPlantLoad(coord))
                            {
                                Random.state = randomContext.ExternalState;
                                yield break;
                            }
                        }
                    }
                }

                Random.state = oldMountainState;
            }
        }

        Random.state = randomContext.ExternalState;
        _loadedPlantChunks.Add(coord);
        _loadingPlantChunks.Remove(coord);
    }

    private IEnumerator LoadRealGrassForChunkIncremental(Vector2Int coord)
    {
        SpawnRandomContext randomContext = new SpawnRandomContext { ExternalState = Random.state };
        Random.InitState(_tm.Config.Seed + coord.x * 12345 + coord.y * 54321 + 700001);

        SpawnFrameBudget budget = new SpawnFrameBudget();

        EnsureTerrainGrassPrefabs();
        if (ShouldSpawnRegularTerrainGrassForChunk(coord) &&
            LoadedTerrainGrassPrefabs != null &&
            LoadedTerrainGrassPrefabs.Count > 0)
        {
            Vector2Int countRange = _tm.Config.TerrainGrassCountPerChunk;
            int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
            int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
            int targetCount = Random.Range(minCount, maxCount + 1);
            int attempts = targetCount * 8;
            int spawnedFormations = 0;

            for (int i = 0; i < attempts && spawnedFormations < targetCount; i++)
            {
                budget.PlacementAttempts++;

                int blades = TrySpawnTerrainGrassFormation(coord);
                if (blades > 0)
                {
                    spawnedFormations++;
                    budget.SpawnedObjects += blades;
                }

                if (ShouldPauseVegetationSpawn(budget))
                {
                    Random.State jobState = Random.state;
                    Random.state = randomContext.ExternalState;
                    yield return null;
                    randomContext.ExternalState = Random.state;
                    Random.state = jobState;
                    budget.Reset();

                    if (ShouldStopRealGrassLoad(coord))
                    {
                        Random.state = randomContext.ExternalState;
                        yield break;
                    }
                }
            }
        }

        if (TerrainManager.IsTreeZoneChunk(coord, _tm.Config))
        {
            yield return SpawnTreeZoneRealGrassIncremental(coord, budget, randomContext);
            if (ShouldStopRealGrassLoad(coord))
            {
                Random.state = randomContext.ExternalState;
                yield break;
            }
        }

        Random.state = randomContext.ExternalState;
        _loadedRealGrassChunks.Add(coord);
        _loadingRealGrassChunks.Remove(coord);
    }

    private IEnumerator SpawnTreeZoneRealGrassIncremental(Vector2Int coord, SpawnFrameBudget budget, SpawnRandomContext randomContext)
    {
        RemoveMissingTreeZoneEntries(LoadedTreeZoneTreeSpawnEntries);
        if (LoadedTreeZoneTreeSpawnEntries == null || LoadedTreeZoneTreeSpawnEntries.Count == 0)
            yield break;

        for (int i = 0; i < LoadedTreeZoneTreeSpawnEntries.Count; i++)
        {
            TreeZoneTreeSpawnEntry entry = LoadedTreeZoneTreeSpawnEntries[i];
            if (entry == null || entry.Prefab == null || !IsTerrainGrassPrefab(entry.Prefab))
                continue;

            if (Random.value > Mathf.Clamp01(entry.SpawnChance))
                continue;

            yield return SpawnTreeZoneGrassEntryIncremental(coord, entry, budget, randomContext);

            if (ShouldStopRealGrassLoad(coord))
                yield break;
        }
    }

    private IEnumerator SpawnTreeZoneGrassEntryIncremental(
        Vector2Int coord,
        TreeZoneTreeSpawnEntry entry,
        SpawnFrameBudget budget,
        SpawnRandomContext randomContext)
    {
        Vector2Int countRange = entry.CountRange;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        int attemptsMultiplier = Mathf.Max(1, _tm.Config.TreeZoneGrassSpawnAttemptsMultiplier);
        int attempts = Mathf.Max(24, targetCount * attemptsMultiplier);
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < targetCount; i++)
        {
            budget.PlacementAttempts++;

            if (TryGetTreeZoneGrassGradientPosition(coord, out Vector3 position))
            {
                if (InstantiateTreeZoneGrassAtPos(position, coord, entry))
                    budget.SpawnedObjects++;

                spawned++;
            }

            if (ShouldPauseTreeZoneSpawn(budget, false))
            {
                Random.State jobState = Random.state;
                Random.state = randomContext.ExternalState;
                yield return null;
                randomContext.ExternalState = Random.state;
                Random.state = jobState;
                budget.Reset();

                if (ShouldStopRealGrassLoad(coord))
                    yield break;
            }
        }
    }

    private bool ShouldSpawnRegularTerrainGrassForChunk(Vector2Int coord)
    {
        if (_tm == null || _tm.Config == null || _tm.Player == null)
            return true;

        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float radius = Mathf.Max(0f, _tm.Config.RealGrassLoadRadiusMeters);
        if (chunkSizeWorld <= 0f || radius <= 0f)
            return false;

        float minX = coord.x * chunkSizeWorld;
        float maxX = minX + chunkSizeWorld;
        float minZ = coord.y * chunkSizeWorld;
        float maxZ = minZ + chunkSizeWorld;
        Vector3 playerPosition = _tm.Player.position;
        float closestX = Mathf.Clamp(playerPosition.x, minX, maxX);
        float closestZ = Mathf.Clamp(playerPosition.z, minZ, maxZ);
        float dx = playerPosition.x - closestX;
        float dz = playerPosition.z - closestZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private IEnumerator SpawnTreeZoneGroveIncrementalPhased(Vector2Int coord, SpawnFrameBudget budget, SpawnRandomContext randomContext)
    {
        RemoveMissingTreeZoneEntries(LoadedTreeZoneTreeSpawnEntries);
        RemoveMissingPrefabs(LoadedTreeZoneTreePrefabs);
        bool hasEntries = LoadedTreeZoneTreeSpawnEntries != null && LoadedTreeZoneTreeSpawnEntries.Count > 0;
        bool hasFallbackPrefabs = LoadedTreeZoneTreePrefabs != null && LoadedTreeZoneTreePrefabs.Count > 0;
        if (!hasEntries && !hasFallbackPrefabs)
            yield break;

        List<Vector3> spawnedPositions = new List<Vector3>();
        List<TreeZoneTreeSpawnEntry> selectedEntries = new List<TreeZoneTreeSpawnEntry>();

        if (hasEntries)
        {
            foreach (TreeZoneTreeSpawnEntry entry in LoadedTreeZoneTreeSpawnEntries)
            {
                if (entry == null || entry.Prefab == null)
                    continue;

                if (Random.value <= Mathf.Clamp01(entry.SpawnChance))
                    selectedEntries.Add(entry);
            }
        }
        else
        {
            foreach (GameObject fallbackPrefab in LoadedTreeZoneTreePrefabs)
            {
                if (fallbackPrefab == null)
                    continue;

                selectedEntries.Add(new TreeZoneTreeSpawnEntry
                {
                    Prefab = fallbackPrefab,
                    SpawnChance = 1f,
                    CountRange = new Vector2Int(1, 1),
                    ScaleMultiplier = _tm.Config.TreeZoneTreeScaleMultiplier,
                    GroundingOffset = _tm.Config.TreeZoneTreeGroundingOffset,
                    MinSpacing = _tm.Config.TreeZoneTreeMinSpacing
                });
            }
        }

        yield return SpawnTreeZonePhaseIncremental(coord, selectedEntries, spawnedPositions, TreeZoneSpawnPhase.Joshua, budget, randomContext);
        if (ShouldStopPlantLoad(coord)) yield break;

        yield return SpawnTreeZonePhaseIncremental(coord, selectedEntries, spawnedPositions, TreeZoneSpawnPhase.OtherLarge, budget, randomContext);
        if (ShouldStopPlantLoad(coord)) yield break;

        yield return SpawnTreeZonePhaseIncremental(coord, selectedEntries, spawnedPositions, TreeZoneSpawnPhase.Pickup, budget, randomContext);
        if (ShouldStopPlantLoad(coord)) yield break;

        yield return SpawnTreeZoneMiniStonesIncremental(coord, spawnedPositions, budget, randomContext);
    }

    private IEnumerator SpawnTreeZonePhaseIncremental(
        Vector2Int coord,
        List<TreeZoneTreeSpawnEntry> entries,
        List<Vector3> spawnedPositions,
        TreeZoneSpawnPhase phase,
        SpawnFrameBudget budget,
        SpawnRandomContext randomContext)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            TreeZoneTreeSpawnEntry entry = entries[i];
            if (!IsEntryInTreeZonePhase(entry, phase))
                continue;

            bool largeObjectPhase = phase == TreeZoneSpawnPhase.Joshua || phase == TreeZoneSpawnPhase.OtherLarge;
            yield return SpawnTreeZoneEntryIncremental(coord, entry, spawnedPositions, budget, randomContext, largeObjectPhase);

            if (ShouldStopPlantLoad(coord))
                yield break;
        }
    }

    private IEnumerator SpawnTreeZoneEntryIncremental(
        Vector2Int coord,
        TreeZoneTreeSpawnEntry entry,
        List<Vector3> spawnedPositions,
        SpawnFrameBudget budget,
        SpawnRandomContext randomContext,
        bool largeObjectPhase)
    {
        if (entry == null || entry.Prefab == null)
            yield break;

        if (_tm.Config.TreeZoneGrassGradientEnabled && IsTerrainGrassPrefab(entry.Prefab))
        {
            Vector2Int countRange = entry.CountRange;
            int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
            int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
            int targetCount = Random.Range(minCount, maxCount + 1);
            int attemptsMultiplier = Mathf.Max(1, _tm.Config.TreeZoneGrassSpawnAttemptsMultiplier);
            int attempts = Mathf.Max(24, targetCount * attemptsMultiplier);
            int spawned = 0;

            for (int i = 0; i < attempts && spawned < targetCount; i++)
            {
                budget.PlacementAttempts++;

                if (TryGetTreeZoneGrassGradientPosition(coord, out Vector3 position))
                {
                    if (InstantiateTreeZoneGrassAtPos(position, coord, entry))
                        budget.SpawnedObjects++;

                    spawned++;
                }

                if (ShouldPauseTreeZoneSpawn(budget, false))
                {
                    Random.State jobState = Random.state;
                    Random.state = randomContext.ExternalState;
                    yield return null;
                    randomContext.ExternalState = Random.state;
                    Random.state = jobState;
                    budget.Reset();

                    if (ShouldStopPlantLoad(coord))
                        yield break;
                }
            }

            yield break;
        }

        Vector2Int countRangeForEntry = entry.CountRange;
        int minEntryCount = Mathf.Max(0, Mathf.Min(countRangeForEntry.x, countRangeForEntry.y));
        int maxEntryCount = Mathf.Max(minEntryCount, Mathf.Max(countRangeForEntry.x, countRangeForEntry.y));
        int targetEntryCount = Random.Range(minEntryCount, maxEntryCount + 1);
        int entryAttempts = Mathf.Max(24, targetEntryCount * 12);
        int spawnedForEntry = 0;

        for (int i = 0; i < entryAttempts && spawnedForEntry < targetEntryCount; i++)
        {
            budget.PlacementAttempts++;

            float minSpacing = entry.MinSpacing > 0f ? entry.MinSpacing : _tm.Config.TreeZoneTreeMinSpacing;
            if (TryGetTreeZoneTreePosition(coord, spawnedPositions, minSpacing, out Vector3 position))
            {
                if (InstantiateTreeZoneTreeAtPos(position, coord, entry))
                    budget.SpawnedObjects++;

                spawnedPositions.Add(position);
                spawnedForEntry++;
            }

            if (ShouldPauseTreeZoneSpawn(budget, largeObjectPhase))
            {
                Random.State jobState = Random.state;
                Random.state = randomContext.ExternalState;
                yield return null;
                randomContext.ExternalState = Random.state;
                Random.state = jobState;
                budget.Reset();

                if (ShouldStopPlantLoad(coord))
                    yield break;
            }
        }
    }

    private bool IsEntryInTreeZonePhase(TreeZoneTreeSpawnEntry entry, TreeZoneSpawnPhase phase)
    {
        if (entry == null || entry.Prefab == null)
            return false;

        bool isGrass = IsTerrainGrassPrefab(entry.Prefab);
        bool isBranch = IsSmallBranchPrefab(entry.Prefab);
        bool isJoshua = IsJoshuaTreePrefab(entry.Prefab);

        switch (phase)
        {
            case TreeZoneSpawnPhase.Joshua:
                return isJoshua && !isGrass && !isBranch;
            case TreeZoneSpawnPhase.OtherLarge:
                return !isJoshua && !isGrass && !isBranch;
            case TreeZoneSpawnPhase.Pickup:
                return isBranch;
            case TreeZoneSpawnPhase.Grass:
                return isGrass;
            default:
                return false;
        }
    }

    private IEnumerator SpawnTreeZoneMiniStonesIncremental(Vector2Int coord, List<Vector3> spawnedPositions, SpawnFrameBudget budget, SpawnRandomContext randomContext)
    {
        if (_tm.Config.MiniStonePrefabs == null || _tm.Config.MiniStonePrefabs.Count == 0)
            yield break;

        Vector2Int countRange = _tm.Config.TreeZoneMiniStoneCountPerZone;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        int attempts = Mathf.Max(24, targetCount * 12);
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < targetCount; i++)
        {
            budget.PlacementAttempts++;

            if (TryGetTreeZoneTreePosition(coord, spawnedPositions, 1f, out Vector3 position))
            {
                if (InstantiateMiniStoneAtPos(position, coord))
                    budget.SpawnedObjects++;

                spawnedPositions.Add(position);
                spawned++;
            }

            if (ShouldPauseVegetationSpawn(budget))
            {
                Random.State jobState = Random.state;
                Random.state = randomContext.ExternalState;
                yield return null;
                randomContext.ExternalState = Random.state;
                Random.state = jobState;
                budget.Reset();
                if (ShouldStopPlantLoad(coord))
                    yield break;
            }
        }
    }

    private bool ShouldPauseVegetationSpawn(SpawnFrameBudget budget)
    {
        int objectBudget = Mathf.Max(1, MaxVegetationSpawnObjectsPerFrame);
        int attemptBudget = Mathf.Max(1, MaxVegetationPlacementAttemptsPerFrame);
        return budget.SpawnedObjects >= objectBudget || budget.PlacementAttempts >= attemptBudget;
    }

    private bool ShouldPauseTreeZoneSpawn(SpawnFrameBudget budget, bool largeObjectPhase)
    {
        int objectBudget = largeObjectPhase
            ? Mathf.Max(1, MaxTreeZoneLargeObjectsPerFrame)
            : Mathf.Max(1, MaxVegetationSpawnObjectsPerFrame);
        int attemptBudget = Mathf.Max(1, MaxVegetationPlacementAttemptsPerFrame);
        return budget.SpawnedObjects >= objectBudget || budget.PlacementAttempts >= attemptBudget;
    }

    private bool ShouldStopPlantLoad(Vector2Int coord)
    {
        return !_loadingPlantChunks.Contains(coord) || !_activePlants.ContainsKey(coord);
    }

    private bool ShouldStopRealGrassLoad(Vector2Int coord)
    {
        return !_loadingRealGrassChunks.Contains(coord) || !_activePlants.ContainsKey(coord);
    }

    private void SpawnTreeZoneGrove(Vector2Int coord)
    {
        if (!TerrainManager.IsTreeZoneChunk(coord, _tm.Config))
            return;

        RemoveMissingTreeZoneEntries(LoadedTreeZoneTreeSpawnEntries);
        RemoveMissingPrefabs(LoadedTreeZoneTreePrefabs);
        bool hasEntries = LoadedTreeZoneTreeSpawnEntries != null && LoadedTreeZoneTreeSpawnEntries.Count > 0;
        bool hasFallbackPrefabs = LoadedTreeZoneTreePrefabs != null && LoadedTreeZoneTreePrefabs.Count > 0;
        if (!hasEntries && !hasFallbackPrefabs)
            return;

        List<Vector3> spawnedPositions = new List<Vector3>();

        if (hasEntries)
        {
            foreach (TreeZoneTreeSpawnEntry entry in LoadedTreeZoneTreeSpawnEntries)
            {
                if (entry == null || entry.Prefab == null)
                    continue;

                if (Random.value > Mathf.Clamp01(entry.SpawnChance))
                    continue;

                SpawnTreeZoneEntry(coord, entry, spawnedPositions);
            }

            SpawnTreeZoneMiniStones(coord, spawnedPositions);
            return;
        }

        foreach (GameObject fallbackPrefab in LoadedTreeZoneTreePrefabs)
        {
            if (fallbackPrefab == null)
                continue;

            TreeZoneTreeSpawnEntry fallbackEntry = new TreeZoneTreeSpawnEntry
            {
                Prefab = fallbackPrefab,
                SpawnChance = 1f,
                CountRange = new Vector2Int(1, 1),
                ScaleMultiplier = _tm.Config.TreeZoneTreeScaleMultiplier,
                GroundingOffset = _tm.Config.TreeZoneTreeGroundingOffset,
                MinSpacing = _tm.Config.TreeZoneTreeMinSpacing
            };
            SpawnTreeZoneEntry(coord, fallbackEntry, spawnedPositions);
        }

        SpawnTreeZoneMiniStones(coord, spawnedPositions);
    }

    private void SpawnTreeZoneMiniStones(Vector2Int coord, List<Vector3> spawnedPositions)
    {
        if (_tm.Config.MiniStonePrefabs == null || _tm.Config.MiniStonePrefabs.Count == 0)
            return;

        Vector2Int countRange = _tm.Config.TreeZoneMiniStoneCountPerZone;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        int attempts = Mathf.Max(24, targetCount * 12);
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < targetCount; i++)
        {
            if (!TryGetTreeZoneTreePosition(coord, spawnedPositions, 1f, out Vector3 position))
                continue;

            InstantiateMiniStoneAtPos(position, coord);
            spawnedPositions.Add(position);
            spawned++;
        }
    }

    private void SpawnTreeZoneEntry(Vector2Int coord, TreeZoneTreeSpawnEntry entry, List<Vector3> spawnedPositions)
    {
        if (_tm.Config.TreeZoneGrassGradientEnabled && IsTerrainGrassPrefab(entry.Prefab))
        {
            SpawnTreeZoneGrassGradient(coord, entry);
            return;
        }

        Vector2Int countRange = entry.CountRange;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        int attempts = Mathf.Max(24, targetCount * 12);
        int spawnedForEntry = 0;

        for (int i = 0; i < attempts && spawnedForEntry < targetCount; i++)
        {
            float minSpacing = entry.MinSpacing > 0f ? entry.MinSpacing : _tm.Config.TreeZoneTreeMinSpacing;
            if (!TryGetTreeZoneTreePosition(coord, spawnedPositions, minSpacing, out Vector3 position))
                continue;

            InstantiateTreeZoneTreeAtPos(position, coord, entry);
            spawnedPositions.Add(position);
            spawnedForEntry++;
        }
    }

    private void SpawnTreeZoneGrassGradient(Vector2Int coord, TreeZoneTreeSpawnEntry entry)
    {
        if (entry == null || entry.Prefab == null)
            return;

        Vector2Int countRange = entry.CountRange;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        if (targetCount <= 0)
            return;

        int attemptsMultiplier = Mathf.Max(1, _tm.Config.TreeZoneGrassSpawnAttemptsMultiplier);
        int attempts = Mathf.Max(24, targetCount * attemptsMultiplier);
        int spawned = 0;

        for (int i = 0; i < attempts && spawned < targetCount; i++)
        {
            if (!TryGetTreeZoneGrassGradientPosition(coord, out Vector3 position))
                continue;

            InstantiateTreeZoneGrassAtPos(position, coord, entry);
            spawned++;
        }
    }

    private bool TryGetTreeZoneGrassGradientPosition(Vector2Int coord, out Vector3 position)
    {
        position = Vector3.zero;

        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        if (chunkSizeWorld <= 0f)
            return false;

        float outerRadius = Mathf.Max(0f, _tm.Config.TreeZoneGrassOuterRadius);
        Vector2 center = new Vector2(
            coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f,
            coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f);
        float gradientRadius = chunkSizeWorld * 0.70710678f + outerRadius;
        float angleFromCenter = Random.Range(0f, Mathf.PI * 2f);
        float centerBiasPower = Mathf.Max(0.01f, _tm.Config.TreeZoneGrassCenterBiasPower);
        float distanceFromCenter = gradientRadius * Mathf.Pow(Random.value, centerBiasPower);
        Vector3 candidate = new Vector3(
            center.x + Mathf.Cos(angleFromCenter) * distanceFromCenter,
            0f,
            center.y + Mathf.Sin(angleFromCenter) * distanceFromCenter);

        float jitterMin = Mathf.Max(0f, _tm.Config.TreeZoneGrassGroupRadiusMin);
        float jitterMax = Mathf.Max(jitterMin, _tm.Config.TreeZoneGrassGroupRadiusMax);
        if (jitterMax > 0f)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(jitterMin, jitterMax);
            candidate.x += Mathf.Cos(angle) * distance;
            candidate.z += Mathf.Sin(angle) * distance;
        }

        if (!IsValidTreeZoneGrassSpawnPoint(candidate, coord, outerRadius))
            return false;

        float density = GetTreeZoneGrassDensity(candidate, coord, outerRadius);
        if (Random.value > density)
            return false;

        position = candidate;
        return true;
    }

    private float GetTreeZoneGrassDensity(Vector3 worldPos, Vector2Int coord, float outerRadius)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        Vector2 center = new Vector2(
            coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f,
            coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f);
        float centerToCorner = chunkSizeWorld * 0.70710678f;
        float gradientRadius = Mathf.Max(0.01f, centerToCorner + outerRadius);
        float t = Mathf.Clamp01(Vector2.Distance(new Vector2(worldPos.x, worldPos.z), center) / gradientRadius);
        t = Mathf.SmoothStep(0f, 1f, t);

        float centerDensity = Mathf.Clamp01(_tm.Config.TreeZoneGrassCenterDensity);
        float outerDensity = Mathf.Clamp01(_tm.Config.TreeZoneGrassOuterDensity);
        return Mathf.Clamp01(Mathf.Lerp(centerDensity, outerDensity, t));
    }

    private bool IsValidTreeZoneGrassSpawnPoint(Vector3 worldPos, Vector2Int coord, float outerRadius)
    {
        if (GetDistanceFromTreeZoneChunkBounds(coord, worldPos.x, worldPos.z) > outerRadius)
            return false;

        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius)
            return false;

        if (HighwayWinManager.TryApplyHighwayFlattening(worldPos.x, worldPos.z, 0f, out _, out float highwayBlend)
            && highwayBlend > 0.01f)
            return false;

        float terrainHeight = _tm.SampleHeight(worldPos);
        if (float.IsNaN(terrainHeight) || float.IsInfinity(terrainHeight))
            return false;

        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases)
        {
            if (Vector2.Distance(new Vector2(worldPos.x, worldPos.z), o.position) < o.basinRadius + 10f)
                return false;
        }

        return true;
    }

    private float GetDistanceFromTreeZoneChunkBounds(Vector2Int coord, float worldX, float worldZ)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float minX = coord.x * chunkSizeWorld;
        float minZ = coord.y * chunkSizeWorld;
        float maxX = minX + chunkSizeWorld;
        float maxZ = minZ + chunkSizeWorld;

        float dx = Mathf.Max(Mathf.Max(minX - worldX, 0f), worldX - maxX);
        float dz = Mathf.Max(Mathf.Max(minZ - worldZ, 0f), worldZ - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private bool TryGetTreeZoneTreePosition(Vector2Int coord, List<Vector3> existingPositions, float minSpacing, out Vector3 position)
    {
        position = Vector3.zero;

        if (!TerrainManager.TryGetTreeZoneAreaBounds(coord, _tm.Config, out float areaMinX, out float areaMaxX, out float areaMinZ, out float areaMaxZ))
            return false;

        float areaWidth = Mathf.Max(0f, areaMaxX - areaMinX);
        float edgeInset = Mathf.Clamp(_tm.Config.TreeZoneEdgeBlendMeters, 0f, areaWidth * 0.45f);
        float minX = areaMinX + edgeInset;
        float maxX = areaMaxX - edgeInset;
        float minZ = areaMinZ + edgeInset;
        float maxZ = areaMaxZ - edgeInset;
        if (maxX <= minX || maxZ <= minZ)
            return false;

        float worldX = Random.Range(minX, maxX);
        float worldZ = Random.Range(minZ, maxZ);
        Vector3 worldPos = new Vector3(worldX, 0f, worldZ);

        if (!IsValidTreeZoneTreeSpawnPoint(worldPos, coord, existingPositions, minSpacing))
            return false;

        position = worldPos;
        return true;
    }

    private bool IsValidTreeZoneTreeSpawnPoint(Vector3 worldPos, Vector2Int coord, List<Vector3> existingPositions, float minSpacing)
    {
        if (TerrainManager.GetTreeZoneInfluence(coord, _tm.Config, worldPos.x, worldPos.z) <= 0f)
            return false;

        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius)
            return false;

        if (HighwayWinManager.TryApplyHighwayFlattening(worldPos.x, worldPos.z, 0f, out _, out float highwayBlend)
            && highwayBlend > 0.01f)
            return false;

        float terrainHeight = _tm.SampleHeight(worldPos);
        if (float.IsNaN(terrainHeight) || float.IsInfinity(terrainHeight))
            return false;

        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases)
        {
            if (Vector2.Distance(new Vector2(worldPos.x, worldPos.z), o.position) < o.basinRadius + 20f)
                return false;
        }

        float minSpacingSqr = Mathf.Max(0f, minSpacing) * Mathf.Max(0f, minSpacing);
        foreach (Vector3 existing in existingPositions)
        {
            Vector3 offset = existing - worldPos;
            offset.y = 0f;
            if (offset.sqrMagnitude < minSpacingSqr)
                return false;
        }

        return true;
    }

    private bool InstantiateTreeZoneTreeAtPos(Vector3 position, Vector2Int coord, TreeZoneTreeSpawnEntry entry)
    {
        GameObject prefab = entry != null ? entry.Prefab : null;
        if (prefab == null)
            return false;

        if (IsTerrainGrassPrefab(prefab))
        {
            return InstantiateTreeZoneGrassAtPos(position, coord, entry);
        }

        if (IsSmallBranchPrefab(prefab))
        {
            return InstantiateTreeZoneSmallBranchAtPos(position, coord, entry);
        }

        float groundingOffset = entry.GroundingOffset;
        float yPos = _tm.SampleHeight(position) - groundingOffset;
        Vector3 pos = new Vector3(position.x, yPos, position.z);

        GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
        obj.name = "TreeZoneTree";
        float scaleMultiplier = Mathf.Max(0.01f, entry.ScaleMultiplier);
        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, Vector3.one * scaleMultiplier);
        obj.transform.rotation = GetPlantSpawnRotation(prefab);
        PlantPhysics.AlignGroundTouchPointToTerrain(obj, _tm, groundingOffset);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();

        PlantPhysics physics = obj.GetComponent<PlantPhysics>();
        if (physics == null) physics = obj.AddComponent<PlantPhysics>();
        physics.DisableFall = true;
        physics.UseCustomGroundingOffset = true;
        physics.CustomGroundingOffset = groundingOffset;

        PlantHealth health = obj.GetComponent<PlantHealth>();
        if (health == null) health = obj.AddComponent<PlantHealth>();
        if (health.Data == null)
            health.Data = Resources.Load<PlantData>("Plants/JoshuaTreeData");

        rb.mass = 10f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _activePlants[coord].Add(obj);
        return true;
    }

    private bool InstantiateTreeZoneSmallBranchAtPos(Vector3 position, Vector2Int coord, TreeZoneTreeSpawnEntry entry)
    {
        GameObject prefab = entry != null ? entry.Prefab : null;
        if (prefab == null)
            return false;

        float groundingOffset = entry.GroundingOffset;
        float yPos = _tm.SampleHeight(position) - groundingOffset;
        if (float.IsNaN(yPos) || float.IsInfinity(yPos))
            return false;

        Vector3 pos = new Vector3(position.x, yPos, position.z);
        GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
        obj.name = "SmallBranch";

        float scaleMultiplier = Mathf.Max(0.01f, entry.ScaleMultiplier);
        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, Vector3.one * scaleMultiplier);
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        SnapObjectBottomToY(obj, yPos);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();

        Collider collider = obj.GetComponent<Collider>();
        if (collider == null && obj.GetComponentsInChildren<Collider>().Length == 0)
        {
            BoxCollider box = obj.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.5f;
        }

        LootItem loot = obj.GetComponent<LootItem>();
        if (loot == null) loot = obj.AddComponent<LootItem>();
        loot.Data = Resources.Load<ItemData>("Items/SmallBranch_ItemData");
        loot.PickupRadius = 2.0f;

        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.None;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _activePlants[coord].Add(obj);
        return true;
    }

    private bool InstantiateTreeZoneGrassAtPos(Vector3 position, Vector2Int coord, TreeZoneTreeSpawnEntry entry)
    {
        if (entry == null || entry.Prefab == null)
            return false;

        float groundY = _tm.SampleHeight(position);
        if (float.IsNaN(groundY) || float.IsInfinity(groundY))
            return false;

        Vector3 pos = new Vector3(position.x, groundY, position.z);
        GameObject obj = Instantiate(entry.Prefab, pos, Quaternion.identity, transform);
        obj.name = "TreeZoneGrass";

        float scaleMultiplier = Mathf.Max(0.01f, entry.ScaleMultiplier);
        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, Vector3.one * scaleMultiplier);
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        if (!PlantPhysics.AlignGroundTouchPointToTerrain(obj, _tm, entry.GroundingOffset))
            SnapRendererBottomToTerrain(obj, groundY, entry.GroundingOffset, MaxTerrainGrassSnapDistance);

        PlantPhysics physics = obj.GetComponent<PlantPhysics>();
        if (physics == null) physics = obj.AddComponent<PlantPhysics>();
        physics.DisableFall = true;
        physics.UseCustomGroundingOffset = true;
        physics.CustomGroundingOffset = entry.GroundingOffset;

        PlantHealth health = obj.GetComponent<PlantHealth>();
        if (health != null)
            Destroy(health);

        int itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
            SetLayerRecursive(obj, itemLayer);

        EnsureGrassPickupCollider(obj);

        LootItem loot = obj.GetComponent<LootItem>();
        if (loot == null) loot = obj.AddComponent<LootItem>();
        loot.Data = Resources.Load<ItemData>("Items/RealGrass_ItemData");
        loot.PickupRadius = 2.0f;

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _activePlants[coord].Add(obj);
        return true;
    }

    private static bool IsTerrainGrassPrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        string prefabName = prefab.name.ToLowerInvariant();
        if (prefabName.Contains("realgrass") || prefabName.Contains("terraingrass"))
            return true;

        LootItem loot = prefab.GetComponent<LootItem>();
        if (loot == null || loot.Data == null)
            return false;

        return loot.Data.name == "RealGrass_ItemData" || loot.Data.ItemName == "Real Grass";
    }

    private static bool IsSmallBranchPrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        string prefabName = prefab.name.Replace(" ", string.Empty).ToLowerInvariant();
        if (prefabName.Contains("smallbranch"))
            return true;

        LootItem loot = prefab.GetComponent<LootItem>();
        if (loot == null || loot.Data == null)
            return false;

        string itemName = (loot.Data.ItemName ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        return loot.Data.name == "SmallBranch_ItemData" || itemName == "smallbranch";
    }

    private static bool IsJoshuaTreePrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        string prefabName = prefab.name.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return prefabName.Contains("joshuatree");
    }

    private static Quaternion GetPlantSpawnRotation(GameObject prefab)
    {
        float yaw = Random.Range(0f, 360f);
        return IsPricklyPearCactusPrefab(prefab)
            ? Quaternion.Euler(43.64f, yaw, 0f)
            : Quaternion.Euler(0f, yaw, 0f);
    }

    private static bool IsPricklyPearCactusPrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        string prefabName = prefab.name.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return prefabName.Contains("pricklypearcactus");
    }

    private void SpawnTerrainGrass(Vector2Int coord)
    {
        EnsureTerrainGrassPrefabs();
        if (LoadedTerrainGrassPrefabs == null || LoadedTerrainGrassPrefabs.Count == 0) return;

        Vector2Int countRange = _tm.Config.TerrainGrassCountPerChunk;
        int minCount = Mathf.Max(0, Mathf.Min(countRange.x, countRange.y));
        int maxCount = Mathf.Max(minCount, Mathf.Max(countRange.x, countRange.y));
        int targetCount = Random.Range(minCount, maxCount + 1);
        int attempts = targetCount * 8;
        int spawnedFormations = 0;

        for (int i = 0; i < attempts && spawnedFormations < targetCount; i++)
        {
            int blades = TrySpawnTerrainGrassFormation(coord);
            if (blades <= 0) continue;

            spawnedFormations++;
        }
    }

    private int TrySpawnTerrainGrassFormation(Vector2Int coord)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float worldX = coord.x * chunkSizeWorld + Random.Range(0f, chunkSizeWorld);
        float worldZ = coord.y * chunkSizeWorld + Random.Range(0f, chunkSizeWorld);
        Vector3 center = new Vector3(worldX, 0, worldZ);

        if (!IsValidTerrainGrassSpawnPoint(center, coord)) return 0;

        float soloChance = Mathf.Clamp01(_tm.Config.TerrainGrassSoloFormationChance);
        int bladeCount = Random.value < soloChance ? 1 : RollGrassGroupSize();
        float radiusMin = Mathf.Max(0f, _tm.Config.TerrainGrassGroupRadiusMin);
        float radiusMax = Mathf.Max(radiusMin, _tm.Config.TerrainGrassGroupRadiusMax);
        float radius = bladeCount == 1 ? 0f : Random.Range(radiusMin, radiusMax);

        int spawned = 0;
        for (int i = 0; i < bladeCount; i++)
        {
            Vector3 position = center;
            if (i > 0 || bladeCount > 1)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(0f, radius);
                position.x += Mathf.Cos(angle) * distance;
                position.z += Mathf.Sin(angle) * distance;
            }

            if (!IsValidTerrainGrassSpawnPoint(position, coord)) continue;

            if (InstantiateTerrainGrassAtPos(position, coord))
                spawned++;
        }

        return spawned;
    }

    private bool IsValidTerrainGrassSpawnPoint(Vector3 worldPos, Vector2Int coord)
    {
        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius)
            return false;

        if (HighwayWinManager.TryApplyHighwayFlattening(worldPos.x, worldPos.z, 0f, out _, out float highwayBlend)
            && highwayBlend > 0.01f)
            return false;

        float terrainHeight = _tm.SampleHeight(worldPos);
        if (float.IsNaN(terrainHeight) || float.IsInfinity(terrainHeight))
            return false;

        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases)
        {
            if (Vector2.Distance(new Vector2(worldPos.x, worldPos.z), o.position) < o.basinRadius + 10f)
                return false;
        }

        return true;
    }

    private int RollGrassGroupSize()
    {
        float roll = Random.value;

        if (roll <= 0.30f) return 3;
        if (roll <= 0.60f) return 4;
        if (roll <= 0.75f) return 5;
        if (roll <= 0.90f) return 6;
        if (roll <= 0.9333f) return 7;
        if (roll <= 0.9666f) return 8;

        return 9;
    }

    private bool InstantiateTerrainGrassAtPos(Vector3 position, Vector2Int coord)
    {
        EnsureTerrainGrassPrefabs();
        if (LoadedTerrainGrassPrefabs == null || LoadedTerrainGrassPrefabs.Count == 0) return false;

        float groundY = _tm.SampleHeight(position);
        if (float.IsNaN(groundY) || float.IsInfinity(groundY))
            return false;

        Vector3 pos = new Vector3(position.x, groundY, position.z);
        GameObject prefab = LoadedTerrainGrassPrefabs[Random.Range(0, LoadedTerrainGrassPrefabs.Count)];

        GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
        float grassScale = Mathf.Clamp(_tm.Config.TerrainGrassScale, 0.01f, MaxTerrainGrassScale);
        obj.transform.localScale = Vector3.one * grassScale;
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        if (!PlantPhysics.AlignGroundTouchPointToTerrain(obj, _tm, 0f))
            SnapRendererBottomToTerrain(obj, groundY, _tm.Config.TerrainGrassGroundingOffset, MaxTerrainGrassSnapDistance);
        obj.name = "TerrainGrass";

        PlantPhysics physics = obj.GetComponent<PlantPhysics>();
        if (physics == null) physics = obj.AddComponent<PlantPhysics>();
        physics.DisableFall = true;

        PlantHealth health = obj.GetComponent<PlantHealth>();
        if (health != null)
            Destroy(health);

        int itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
            SetLayerRecursive(obj, itemLayer);

        EnsureGrassPickupCollider(obj);

        LootItem loot = obj.GetComponent<LootItem>();
        if (loot == null) loot = obj.AddComponent<LootItem>();
        loot.Data = Resources.Load<ItemData>("Items/RealGrass_ItemData");
        loot.PickupRadius = 2.0f;

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        if (!_activePlants.ContainsKey(coord))
            _activePlants[coord] = new List<GameObject>();

        _activePlants[coord].Add(obj);
        return true;
    }

    private static void SnapRendererBottomToTerrain(GameObject obj, float terrainHeight, float groundingOffset, float maxSnapDistance)
    {
        if (obj == null)
            return;

        Vector3 anchoredPosition = obj.transform.position;
        anchoredPosition.y = terrainHeight - groundingOffset;
        obj.transform.position = anchoredPosition;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float targetBottomY = terrainHeight - groundingOffset;
        float deltaY = targetBottomY - bounds.min.y;
        if (Mathf.Abs(deltaY) > maxSnapDistance)
            return;

        obj.transform.position += Vector3.up * deltaY;
    }

    private void EnsureGrassPickupCollider(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            col.isTrigger = false;
        }

        if (colliders.Length > 0)
            return;

        Renderer renderer = obj.GetComponentInChildren<Renderer>();
        BoxCollider collider = obj.AddComponent<BoxCollider>();
        collider.isTrigger = false;

        if (renderer == null)
            return;

        collider.center = obj.transform.InverseTransformPoint(renderer.bounds.center);

        Vector3 localSize = obj.transform.InverseTransformVector(renderer.bounds.size);
        collider.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z)
        );
    }

    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static void RemoveMissingPrefabs(List<GameObject> prefabs)
    {
        if (prefabs == null)
            return;

        for (int i = prefabs.Count - 1; i >= 0; i--)
        {
            if (prefabs[i] == null)
                prefabs.RemoveAt(i);
        }
    }

    private static void RemoveMissingTreeZoneEntries(List<TreeZoneTreeSpawnEntry> entries)
    {
        if (entries == null)
            return;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] == null || entries[i].Prefab == null)
                entries.RemoveAt(i);
        }
    }

    private static bool HasValidTreeZoneEntries(List<TreeZoneTreeSpawnEntry> entries)
    {
        if (entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].Prefab != null)
                return true;
        }

        return false;
    }

    private void EnsureTerrainGrassPrefabs()
    {
        RemoveMissingPrefabs(LoadedTerrainGrassPrefabs);
    }

    /// <summary>
    /// Destroys plants for an unloaded chunk.
    /// </summary>
    public void UnloadPlantsForChunk(Vector2Int coord)
    {
        _loadingPlantChunks.Remove(coord);
        _loadedPlantChunks.Remove(coord);

        if (_activePlants.TryGetValue(coord, out List<GameObject> plants))
        {
            for (int i = plants.Count - 1; i >= 0; i--)
            {
                GameObject plant = plants[i];
                if (plant == null)
                {
                    plants.RemoveAt(i);
                    continue;
                }

                if (IsTerrainGrassObject(plant))
                    continue;

                Destroy(plant);
                plants.RemoveAt(i);
            }

            if (plants.Count == 0 && !_loadedRealGrassChunks.Contains(coord) && !_loadingRealGrassChunks.Contains(coord))
                _activePlants.Remove(coord);
        }
    }

    public void UnloadRealGrassForChunk(Vector2Int coord)
    {
        _loadingRealGrassChunks.Remove(coord);
        _loadedRealGrassChunks.Remove(coord);

        if (_activePlants.TryGetValue(coord, out List<GameObject> plants))
        {
            for (int i = plants.Count - 1; i >= 0; i--)
            {
                GameObject plant = plants[i];
                if (plant == null)
                {
                    plants.RemoveAt(i);
                    continue;
                }

                if (!IsTerrainGrassObject(plant))
                    continue;

                Destroy(plant);
                plants.RemoveAt(i);
            }

            if (plants.Count == 0 && !_loadedPlantChunks.Contains(coord) && !_loadingPlantChunks.Contains(coord))
                _activePlants.Remove(coord);
        }
    }

    /// <summary>
    /// Unloads all plants except those in the active set.
    /// </summary>
    public void UnloadDistantPlants(HashSet<Vector2Int> activeCoords)
    {
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in _activePlants.Keys)
        {
            if (!activeCoords.Contains(coord))
                toRemove.Add(coord);
        }
        
        foreach (var coord in toRemove)
        {
            UnloadPlantsForChunk(coord);
        }
    }

    public void UnloadDistantRealGrass(HashSet<Vector2Int> activeCoords)
    {
        HashSet<Vector2Int> toRemoveSet = new HashSet<Vector2Int>(_loadedRealGrassChunks);
        foreach (var coord in _loadingRealGrassChunks)
            toRemoveSet.Add(coord);

        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in toRemoveSet)
            if (!activeCoords.Contains(coord))
                toRemove.Add(coord);

        foreach (var coord in toRemove)
        {
            UnloadRealGrassForChunk(coord);
        }
    }

    private bool SpawnPlant(Vector2Int coord, bool isJoshuaTree)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float worldX = coord.x * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        float worldZ = coord.y * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        Vector3 worldPos = new Vector3(worldX, 0, worldZ);

        // 1. Check Safe Zone (no plants near player spawn)
        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius)
            return false;

        // 2. Check Oasis radius (don't spawn desert generic plants inside oasis basins)
        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases) {
            if (Vector2.Distance(new Vector2(worldX, worldZ), o.position) < o.basinRadius + 20f) {
                return false;
            }
        }

        if (isJoshuaTree)
        {
            // Joshua Trees ONLY spawn on apron sand (mountain-influenced terrain)
            if (!IsOnApronSand(worldPos))
                return false;
        }

        return InstantiatePlantAtPos(worldPos, coord, isJoshuaTree);
    }

    /// <summary>
    /// Returns true if the world position is on apron sand (near a mountain base).
    /// Checks mountain influence/flatness — must be above threshold to count as apron sand.
    /// </summary>
    private bool IsOnApronSand(Vector3 worldPos)
    {
        if (MountainSpawner.Instance == null) return false;
        
        float flatness = 0f;
        float influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(worldPos, out flatness);
        
        // Apron sand blend = max(flatness, influence * 0.5) — same formula as SandChunk
        float apronBlend = Mathf.Max(flatness, influence * 0.5f);
        
        // Must be on apron sand (blend > 0.3) but not inside the mountain mesh (flatness < 0.99)
        return apronBlend > 0.3f && flatness < 0.99f;
    }

    private bool InstantiatePlantAtPos(Vector3 position, Vector2Int coord, bool isJoshuaTree)
    {
        float scale = isJoshuaTree 
            ? Random.Range(_tm.Config.JoshuaTreeMinScale, _tm.Config.JoshuaTreeMaxScale)
            : Random.Range(_tm.Config.PlantMinScale, _tm.Config.PlantMaxScale);
        
        float groundingOffset = isJoshuaTree 
            ? _tm.Config.JoshuaTreeGroundingOffset 
            : _tm.Config.PlantGroundingOffset;

        // Ground the plant at surface height minus the configurable grounding offset
        float yPos = _tm.SampleHeight(position) - groundingOffset; 
        Vector3 pos = new Vector3(position.x, yPos, position.z);
        
        var prefabList = isJoshuaTree ? LoadedJoshuaTreePrefabs : LoadedPlantPrefabs;

        if (prefabList.Count > 0)
        {
            GameObject prefab = prefabList[Random.Range(0, prefabList.Count)];
            GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
            obj.transform.localScale = Vector3.one * scale;
            
            // Name correctly so PlantPhysics knows which grounding offset to use
            if (isJoshuaTree) obj.name = "JoshuaTree";
            else obj.name = "DesertPlant";

            obj.transform.rotation = GetPlantSpawnRotation(prefab);

            // Add Rigidbody for physics interaction
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null) rb = obj.AddComponent<Rigidbody>();
            
            // Add PlantPhysics script to handle proximity-based activation and GROUNDING
            var physics = obj.GetComponent<PlantPhysics>();
            if (physics == null) physics = obj.AddComponent<PlantPhysics>();
            
            if (!isJoshuaTree)
            {
                physics.DisableFall = true; // Small plants don't fall over
            }

            // Add PlantHealth script and assign data while the source prefab is still known.
            if (isJoshuaTree)
            {
                PlantHealth health = obj.GetComponent<PlantHealth>();
                if (health == null) health = obj.AddComponent<PlantHealth>();
                
                // Assign data if it's currently missing
                if (health.Data == null)
                {
                    health.Data = Resources.Load<PlantData>("Plants/JoshuaTreeData");
                }
            }
            else
            {
                AssignDesertPlantHealthData(obj, prefab);
            }
            
            // Trees stay kinematic — PlantPhysics pins them to terrain surface
            rb.mass = 10f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;

            if (!_activePlants.ContainsKey(coord))
                _activePlants[coord] = new List<GameObject>();

            _activePlants[coord].Add(obj);
            return true;
        }

        return false;
    }

    private static void AssignDesertPlantHealthData(GameObject obj, GameObject sourcePrefab)
    {
        PlantHealth health = obj.GetComponent<PlantHealth>();
        if (health == null)
            return;

        if (health.Data != null)
            return;

        PlantData data = LoadPlantDataForPrefab(sourcePrefab);
        if (data != null)
            health.Data = data;
    }

    private static PlantData LoadPlantDataForPrefab(GameObject sourcePrefab)
    {
        string prefabName = sourcePrefab != null ? sourcePrefab.name.ToLowerInvariant() : string.Empty;

        if (prefabName.Contains("cactus01_m")) return Resources.Load<PlantData>("Plants/cactus01_m_Data");
        if (prefabName.Contains("cactus02_m")) return Resources.Load<PlantData>("Plants/cactus02_m_Data");
        if (prefabName.Contains("cactus03_m")) return Resources.Load<PlantData>("Plants/cactus03_m_Data");
        if (prefabName.Contains("cactus04")) return Resources.Load<PlantData>("Plants/Cactus04_Data");
        if (prefabName.Contains("cactus_01") || prefabName.Contains("cactus 01")) return Resources.Load<PlantData>("Plants/Cactus_01_Data");
        if (prefabName.Contains("bush 4")) return Resources.Load<PlantData>("Plants/Grass1Data");
        if (prefabName.Contains("bush 5")) return Resources.Load<PlantData>("Plants/Grass2Data");
        if (prefabName.Contains("bush01") || prefabName.Contains("bush 1")) return Resources.Load<PlantData>("Plants/bush1Data");
        if (prefabName.Contains("bush02") || prefabName.Contains("bush 2")) return Resources.Load<PlantData>("Plants/bush2Data");

        return null;
    }

    private bool SpawnMiniStone(Vector2Int coord)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float worldX = coord.x * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        float worldZ = coord.y * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        Vector3 worldPos = new Vector3(worldX, 0, worldZ);

        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius) return false;

        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases) {
            if (Vector2.Distance(new Vector2(worldX, worldZ), o.position) < o.basinRadius + 5f) {
                return false;
            }
        }

        return InstantiateMiniStoneAtPos(worldPos, coord);
    }

    private bool InstantiateMiniStoneAtPos(Vector3 position, Vector2Int coord)
    {
        float scale = Random.Range(_tm.Config.MiniStoneMinScale, _tm.Config.MiniStoneMaxScale);
        float yPos = _tm.SampleHeight(position) - _tm.Config.MiniStoneGroundingOffset; 
        Vector3 pos = new Vector3(position.x, yPos, position.z);
        
        var prefabs = _tm.Config.MiniStonePrefabs;
        if (prefabs.Count > 0)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
            GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
            obj.transform.localScale = Vector3.one * scale;
            obj.name = "MiniStone";
            
            obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null) rb = obj.AddComponent<Rigidbody>();
            
            PlantHealth health = obj.GetComponent<PlantHealth>();
            if (health != null)
            {
                var physics = obj.GetComponent<PlantPhysics>();
                if (physics == null) physics = obj.AddComponent<PlantPhysics>();
                physics.DisableFall = true; 
            }

            // Make it collectible
            LootItem loot = obj.GetComponent<LootItem>();
            if (loot == null) loot = obj.AddComponent<LootItem>();
            loot.Data = Resources.Load<ItemData>("Items/Stonemini_ItemData");
            loot.PickupRadius = 2.0f; // Small pickup radius for small stones


            rb.mass = 5f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;
            rb.isKinematic = false;
            rb.useGravity = true;

            if (!_activePlants.ContainsKey(coord))
                _activePlants[coord] = new List<GameObject>();
                
            _activePlants[coord].Add(obj);
            return true;
        }

        return false;
    }

    private bool SpawnSmallBranch(Vector2Int coord)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float worldX = coord.x * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        float worldZ = coord.y * chunkSizeWorld + Random.Range(chunkSizeWorld * 0.1f, chunkSizeWorld * 0.9f);
        Vector3 worldPos = new Vector3(worldX, 0, worldZ);

        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius) return false;

        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var o in nearbyOases)
        {
            if (Vector2.Distance(new Vector2(worldX, worldZ), o.position) < o.basinRadius + 5f)
            {
                return false;
            }
        }

        return InstantiateSmallBranchAtPos(worldPos, coord);
    }

    private bool InstantiateSmallBranchAtPos(Vector3 position, Vector2Int coord)
    {
        float scale = Random.Range(_tm.Config.SmallBranchMinScale, _tm.Config.SmallBranchMaxScale);
        float yPos = _tm.SampleHeight(position) - _tm.Config.SmallBranchGroundingOffset;
        Vector3 pos = new Vector3(position.x, yPos, position.z);

        var prefabs = _tm.Config.SmallBranchPrefabs;
        if (prefabs.Count > 0)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
            if (prefab == null) return false;

            GameObject obj;
            try
            {
                obj = Instantiate(prefab, pos, Quaternion.identity, transform);
            }
            catch (MissingReferenceException)
            {
                Debug.LogWarning("[PlantSpawner] Skipped a missing small branch prefab reference.");
                return false;
            }

            obj.transform.localScale = Vector3.one * scale;
            obj.name = "SmallBranch";
            obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            SnapObjectBottomToY(obj, yPos);

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null) rb = obj.AddComponent<Rigidbody>();

            Collider collider = obj.GetComponent<Collider>();
            if (collider == null && obj.GetComponentsInChildren<Collider>().Length == 0)
            {
                BoxCollider box = obj.AddComponent<BoxCollider>();
                box.size = Vector3.one * 0.5f;
            }

            LootItem loot = obj.GetComponent<LootItem>();
            if (loot == null) loot = obj.AddComponent<LootItem>();
            loot.Data = Resources.Load<ItemData>("Items/SmallBranch_ItemData");
            loot.PickupRadius = 2.0f;

            rb.mass = 1f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.None;

            if (!_activePlants.ContainsKey(coord))
                _activePlants[coord] = new List<GameObject>();

            _activePlants[coord].Add(obj);
            return true;
        }

        return false;
    }

    private void SnapObjectBottomToY(GameObject obj, float groundY)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float deltaY = groundY - bounds.min.y;
        obj.transform.position += Vector3.up * deltaY;
    }
}
