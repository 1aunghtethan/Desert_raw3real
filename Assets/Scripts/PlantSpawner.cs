using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns plant prefabs (trees, cacti) on the sand terrain.
/// Follows the same chunk-based streaming pattern as MountainSpawner.
/// </summary>
public class PlantSpawner : MonoBehaviour
{
    public static PlantSpawner Instance;
    
    private TerrainManager _tm;
    private Dictionary<Vector2Int, List<GameObject>> _activePlants = new Dictionary<Vector2Int, List<GameObject>>();
    
    [Header("Manual Overrides (Drag & Drop here)")]
    public List<GameObject> PlantPrefabs = new List<GameObject>();
    public List<GameObject> JoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> TerrainGrassPrefabs = new List<GameObject>();

    [Header("Runtime Active Prefabs (Read Only)")]
    public List<GameObject> LoadedPlantPrefabs = new List<GameObject>();
    public List<GameObject> LoadedJoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> LoadedTerrainGrassPrefabs = new List<GameObject>();
    
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _activePlants.Clear();
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

        RemoveMissingPrefabs(LoadedPlantPrefabs);
        RemoveMissingPrefabs(LoadedJoshuaTreePrefabs);
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
        if (_activePlants.ContainsKey(coord)) return;

        // Deterministic random state for this chunk
        Random.State oldState = Random.state;
        Random.InitState(_tm.Config.Seed + coord.x * 12345 + coord.y * 54321 + 7);

        // Check for general plant spawn
        if (Random.value < _tm.Config.PlantSpawnChance)
        {
            SpawnPlant(coord, false);
        }

        SpawnTerrainGrass(coord);

        // Check for mini stone spawn
        if (_tm.Config.MiniStonePrefabs != null && _tm.Config.MiniStonePrefabs.Count > 0)
        {
            if (Random.value < _tm.Config.MiniStoneSpawnChance)
            {
                int stoneCount = _tm.Config.MiniStonesPerChunk;
                for (int i = 0; i < stoneCount; i++)
                {
                    SpawnMiniStone(coord);
                }
            }
        }

        // Check for small branch spawn
        if (_tm.Config.SmallBranchPrefabs != null && _tm.Config.SmallBranchPrefabs.Count > 0)
        {
            if (Random.value < _tm.Config.SmallBranchSpawnChance)
            {
                int branchCount = _tm.Config.SmallBranchesPerChunk;
                for (int i = 0; i < branchCount; i++)
                {
                    SpawnSmallBranch(coord);
                }
            }
        }

        // Handle Joshua Trees specifically based on mountain centers
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
                        InstantiatePlantAtPos(new Vector3(treeX, 0, treeZ), coord, true);
                    }
                }
                
                Random.state = oldMountainState;
            }
        }

        Random.state = oldState;
    }

    private void SpawnTerrainGrass(Vector2Int coord)
    {
        EnsureTerrainGrassPrefabs();
        if (LoadedTerrainGrassPrefabs == null || LoadedTerrainGrassPrefabs.Count == 0) return;

        int targetCount = Random.Range(_tm.Config.TerrainGrassCountPerChunk.x, _tm.Config.TerrainGrassCountPerChunk.y + 1);
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

            InstantiateTerrainGrassAtPos(position, coord);
            spawned++;
        }

        return spawned;
    }

    private bool IsValidTerrainGrassSpawnPoint(Vector3 worldPos, Vector2Int coord)
    {
        if (Vector3.Distance(worldPos, _tm.Config.PlayerSpawnPoint) < _tm.Config.SpawnSafeRadius)
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

    private void InstantiateTerrainGrassAtPos(Vector3 position, Vector2Int coord)
    {
        EnsureTerrainGrassPrefabs();
        if (LoadedTerrainGrassPrefabs == null || LoadedTerrainGrassPrefabs.Count == 0) return;

        float yPos = _tm.SampleHeight(position) - _tm.Config.TerrainGrassGroundingOffset;
        Vector3 pos = new Vector3(position.x, yPos, position.z);
        GameObject prefab = LoadedTerrainGrassPrefabs[Random.Range(0, LoadedTerrainGrassPrefabs.Count)];

        GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
        obj.transform.localScale = Vector3.one * _tm.Config.TerrainGrassScale;
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        obj.name = "TerrainGrass";

        PlantPhysics physics = obj.GetComponent<PlantPhysics>();
        if (physics != null)
            Destroy(physics);

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

    private void EnsureTerrainGrassPrefabs()
    {
        RemoveMissingPrefabs(LoadedTerrainGrassPrefabs);
    }

    /// <summary>
    /// Destroys plants for an unloaded chunk.
    /// </summary>
    public void UnloadPlantsForChunk(Vector2Int coord)
    {
        if (_activePlants.TryGetValue(coord, out List<GameObject> plants))
        {
            foreach (var plant in plants)
            {
                if (plant != null) Destroy(plant);
            }
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

        InstantiatePlantAtPos(worldPos, coord, isJoshuaTree);
        return true;
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

    private void InstantiatePlantAtPos(Vector3 position, Vector2Int coord, bool isJoshuaTree)
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

            // The FBX export has been normalized to Y-up natively, so no X rotation correction is needed.
            obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

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

            // Add PlantHealth script and assign data for Joshua Trees
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
        }
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

        InstantiateMiniStoneAtPos(worldPos, coord);
        return true;
    }

    private void InstantiateMiniStoneAtPos(Vector3 position, Vector2Int coord)
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
        }
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

        InstantiateSmallBranchAtPos(worldPos, coord);
        return true;
    }

    private void InstantiateSmallBranchAtPos(Vector3 position, Vector2Int coord)
    {
        float scale = Random.Range(_tm.Config.SmallBranchMinScale, _tm.Config.SmallBranchMaxScale);
        float yPos = _tm.SampleHeight(position) - _tm.Config.SmallBranchGroundingOffset;
        Vector3 pos = new Vector3(position.x, yPos, position.z);

        var prefabs = _tm.Config.SmallBranchPrefabs;
        if (prefabs.Count > 0)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
            if (prefab == null) return;

            GameObject obj;
            try
            {
                obj = Instantiate(prefab, pos, Quaternion.identity, transform);
            }
            catch (MissingReferenceException)
            {
                Debug.LogWarning("[PlantSpawner] Skipped a missing small branch prefab reference.");
                return;
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
        }
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
