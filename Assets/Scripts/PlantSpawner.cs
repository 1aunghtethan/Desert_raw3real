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

    [Header("Runtime Active Prefabs (Read Only)")]
    public List<GameObject> LoadedPlantPrefabs = new List<GameObject>();
    public List<GameObject> LoadedJoshuaTreePrefabs = new List<GameObject>();
    
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
}
