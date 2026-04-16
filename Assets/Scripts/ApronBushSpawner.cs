using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns all vegetation specific to mountain aprons, including Joshua Trees, bushes, and grass.
/// Kept separate from PlantSpawner to keep logic focused and modular.
/// </summary>
public class ApronBushSpawner : MonoBehaviour
{
    public static ApronBushSpawner Instance;

    private TerrainManager _tm;
    private Dictionary<Vector2Int, List<GameObject>> _activeApronVegetation = new Dictionary<Vector2Int, List<GameObject>>();

    // Publicly exposed lists so they can be seen in the Inspector and manually assigned (Drag & Drop)
    [Header("Manual Overrides (Drag & Drop here)")]
    public List<GameObject> JoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> BushPrefabs = new List<GameObject>();
    public List<GameObject> GrassPrefabs = new List<GameObject>();

    [Header("Runtime Active Prefabs (Read Only)")]
    public List<GameObject> LoadedJoshuaTreePrefabs = new List<GameObject>();
    public List<GameObject> LoadedMountainBushPrefabs = new List<GameObject>();
    public List<GameObject> LoadedMountainGrassPrefabs = new List<GameObject>();

    void Awake()
    {
        Instance = this;
        _tm = TerrainManager.Instance;
        if (_tm == null) _tm = GetComponent<TerrainManager>();
    }

    void Start()
    {
        _activeApronVegetation.Clear();
        _tm = TerrainManager.Instance;
        
        if (_tm == null)
            Debug.LogError("[ApronBushSpawner] Requires TerrainManager to be in the scene!");
            
        RefreshPrefabReferences();
    }

    public void RefreshPrefabReferences()
    {
        if (_tm == null || _tm.Config == null) return;

        // If manual overrides are provided on THIS component, use them; 
        // otherwise, use the GameObject lists from TerrainConfig (Manual Drag & Drop)
        if (JoshuaTreePrefabs.Count > 0) LoadedJoshuaTreePrefabs = JoshuaTreePrefabs;
        else LoadedJoshuaTreePrefabs = _tm.Config.JoshuaTreePrefabs;

        if (BushPrefabs.Count > 0) LoadedMountainBushPrefabs = BushPrefabs;
        else LoadedMountainBushPrefabs = _tm.Config.MountainBushPrefabs;

        if (GrassPrefabs.Count > 0) LoadedMountainGrassPrefabs = GrassPrefabs;
        else LoadedMountainGrassPrefabs = _tm.Config.MountainGrassPrefabs;
    }

    /// <summary>
    /// Loads mountain-specific vegetation for a given chunk coordinate.
    /// </summary>
    public void LoadBushesForChunk(Vector2Int coord)
    {
        Debug.Log($"[ApronBushSpawner] LoadBushesForChunk({coord}) entered.");
        if (_tm == null || _tm.Config == null) {
            _tm = TerrainManager.Instance;
            if (_tm == null) return;
        }
        
        if (_activeApronVegetation.ContainsKey(coord)) return;

        if (MountainSpawner.Instance == null) return;

        var nearbyMountains = MountainSpawner.Instance.GetNearbyMountains(coord);
        if (nearbyMountains.Count == 0) return;

        List<GameObject> chunkObjects = new List<GameObject>();
        _activeApronVegetation[coord] = chunkObjects;

        foreach (var mountain in nearbyMountains)
        {
            // Unique deterministic seed for this mountain+chunk combo
            int mountainSeed = _tm.Config.Seed + (int)(mountain.position.x * 37) + (int)(mountain.position.y * 19) + coord.x * 7 + coord.y * 13;
            Random.State oldState = Random.state;
            Random.InitState(mountainSeed);

            // 1. Spawning Joshua Trees (Per Mountain logic - kept for rarity)
            if (LoadedJoshuaTreePrefabs.Count > 0)
            {
                int treeCount = Random.Range(2, 6);
                for (int i = 0; i < treeCount; i++)
                {
                    TrySpawnInApron(mountain, coord, LoadedJoshuaTreePrefabs, true, chunkObjects);
                }
            }
            
            Random.state = oldState;
        }

        // Global chunk seed for per-chunk distribution
        Random.State globalOldState = Random.state;
        Random.InitState(_tm.Config.Seed + coord.x * 777 + coord.y * 888 + 99);

        // 2. Spawning Mountain Bushes (Search-based Density)
        if (LoadedMountainBushPrefabs.Count > 0)
        {
            int targetCount = Random.Range(_tm.Config.MountainBushCountPerChunk.x, _tm.Config.MountainBushCountPerChunk.y + 1);
            // Increase attempts to compensate for noise-based thinning
            int attempts = targetCount * 25; 
            int spawned = 0;
            for (int i = 0; i < attempts && spawned < targetCount; i++)
            {
                if (TrySpawnInChunkArea(coord, LoadedMountainBushPrefabs, false, chunkObjects, false))
                    spawned++;
            }
            Debug.Log($"[ApronBushSpawner] Chunk {coord}: Bush attempts={attempts}, spawned={spawned}/{targetCount}");
        }
        else
        {
            Debug.LogWarning($"[ApronBushSpawner] No Mountain Bush prefabs loaded!");
        }

        // 3. Spawning Mountain Grass (Search-based Density)
        if (LoadedMountainGrassPrefabs.Count > 0)
        {
            int targetCount = Random.Range(_tm.Config.MountainGrassCountPerChunk.x, _tm.Config.MountainGrassCountPerChunk.y + 1);
            // Increase attempts to compensate for noise-based thinning
            int attempts = targetCount * 25;
            int spawned = 0;
            for (int i = 0; i < attempts && spawned < targetCount; i++)
            {
                if (TrySpawnInChunkArea(coord, LoadedMountainGrassPrefabs, false, chunkObjects, true))
                    spawned++;
            }
            Debug.Log($"[ApronBushSpawner] Chunk {coord}: Grass attempts={attempts}, spawned={spawned}/{targetCount}");
        }
        else
        {
            Debug.LogWarning($"[ApronBushSpawner] No Mountain Grass prefabs loaded!");
        }


        Random.state = globalOldState;
    }


    public void UnloadBushesForChunk(Vector2Int coord)
    {
        if (_activeApronVegetation.TryGetValue(coord, out List<GameObject> objects))
        {
            foreach (var obj in objects)
            {
                if (obj != null) Destroy(obj);
            }
            _activeApronVegetation.Remove(coord);
        }
    }

    public void UnloadDistantBushes(HashSet<Vector2Int> activeCoords)
    {
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in _activeApronVegetation.Keys)
        {
            if (!activeCoords.Contains(coord))
                toRemove.Add(coord);
        }
        
        foreach (var coord in toRemove)
        {
            UnloadBushesForChunk(coord);
        }
    }


    private bool TrySpawnInChunkArea(Vector2Int coord, List<GameObject> prefabs, bool isJoshuaTree, List<GameObject> chunkObjects, bool isGrass = false)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float minX = coord.x * chunkSizeWorld;
        float minZ = coord.y * chunkSizeWorld;

        float posX = minX + Random.Range(0f, chunkSizeWorld);
        float posZ = minZ + Random.Range(0f, chunkSizeWorld);

        Vector3 worldPos = new Vector3(posX, 0, posZ);
        if (IsOnApronSand(worldPos))
        {
            // Clumping logic: Sample shared vegetation noise
            float clumpNoise = Mathf.PerlinNoise(worldPos.x * _tm.Config.MountainVegClumpScale + _tm.Config.Seed, worldPos.z * _tm.Config.MountainVegClumpScale + _tm.Config.Seed);
            
            // If noise is below threshold, this is a "thin" area. Substantially reduce spawn chance.
            if (clumpNoise < _tm.Config.MountainVegClumpThreshold)
            {
                // In thin areas, only 5% of attempts succeed
                if (Random.value > 0.05f) return false;
            }

            GameObject obj = InstantiateVegetation(worldPos, prefabs, isJoshuaTree, isGrass);
            if (obj != null) {
                chunkObjects.Add(obj);
                return true;
            }
        }
        return false;
    }

    private void TrySpawnInApron(MountainSpawner.MountainData mountain, Vector2Int coord, List<GameObject> prefabs, bool isJoshuaTree, List<GameObject> chunkObjects, bool isGrass = false)
    {
        // Still used for Joshua Trees (per-mountain logic)
        float angle = Random.Range(0f, Mathf.PI * 2f);
        // Use the dynamic flatRadius instead of the config base value
        float distance = mountain.footprintRadius + Random.Range(2f, mountain.flatRadius - 2f);

        float posX = mountain.position.x + Mathf.Cos(angle) * distance;
        float posZ = mountain.position.y + Mathf.Sin(angle) * distance;

        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float minX = coord.x * chunkSizeWorld;
        float maxX = minX + chunkSizeWorld;
        float minZ = coord.y * chunkSizeWorld;
        float maxZ = minZ + chunkSizeWorld;

        if (posX >= minX && posX < maxX && posZ >= minZ && posZ < maxZ)
        {
            Vector3 worldPos = new Vector3(posX, 0, posZ);
            if (IsOnApronSand(worldPos))
            {
                GameObject obj = InstantiateVegetation(worldPos, prefabs, isJoshuaTree, isGrass);
                if (obj != null) chunkObjects.Add(obj);
            }
        }
    }

    private GameObject InstantiateVegetation(Vector3 position, List<GameObject> prefabs, bool isJoshuaTree, bool isGrass)
    {
        float scale;
        if (isJoshuaTree)
        {
            scale = Random.Range(_tm.Config.JoshuaTreeMinScale, _tm.Config.JoshuaTreeMaxScale);
        }
        else if (isGrass)
        {
            scale = _tm.Config.MountainGrassScale;
        }
        else
        {
            scale = _tm.Config.MountainBushScale;
        }
            
        float groundingOffset = isJoshuaTree 
            ? _tm.Config.JoshuaTreeGroundingOffset 
            : (isGrass ? 0.02f : 0.05f);

        float yPos = _tm.SampleHeight(position) - groundingOffset; 
        Vector3 pos = new Vector3(position.x, yPos, position.z);
        
        GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
        GameObject obj = Instantiate(prefab, pos, Quaternion.identity, transform);
        obj.transform.localScale = Vector3.one * scale;
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Name correctly so PlantPhysics knows which grounding offset to use
        if (isJoshuaTree) obj.name = "JoshuaTree";
        else if (isGrass) obj.name = "MountainGrass";
        else obj.name = "MountainBush";

        if (isJoshuaTree)
        {
            SetupJoshuaTree(obj);
        }
        else 
        {
            // Add PlantPhysics to all small vegetation too (bushes/grass)
            // This ensures they stay grounded if the dune settles after spawning.
            var physics = obj.GetComponent<PlantPhysics>();
            if (physics == null) physics = obj.AddComponent<PlantPhysics>();
            physics.DisableFall = true; // Small plants don't fall over from erosion

            if (!isGrass)
            {
                CapsuleCollider cap = obj.GetComponent<CapsuleCollider>();
                if (cap == null) cap = obj.AddComponent<CapsuleCollider>();
                cap.radius = 0.5f;
                cap.height = 1.0f;
                cap.center = new Vector3(0, 0.5f, 0);
            }
            else
            {
                foreach (var col in obj.GetComponentsInChildren<Collider>())
                {
                    Destroy(col);
                }
            }
        }

        return obj;
    }

    private void SetupJoshuaTree(GameObject obj)
    {
        // Replicate the Joshua Tree setup from PlantSpawner
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        
        if (obj.GetComponent<PlantPhysics>() == null) obj.AddComponent<PlantPhysics>();
        
        PlantHealth health = obj.GetComponent<PlantHealth>();
        if (health == null) health = obj.AddComponent<PlantHealth>();
        if (health.Data == null)
        {
            health.Data = Resources.Load<PlantData>("Plants/JoshuaTreeData");
        }
        
        rb.mass = 10f;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        foreach (var meshCollider in obj.GetComponentsInChildren<MeshCollider>())
        {
            Destroy(meshCollider);
        }
        
        if (obj.GetComponent<CapsuleCollider>() == null)
        {
            CapsuleCollider cap = obj.AddComponent<CapsuleCollider>();
            cap.radius = 0.4f;
            cap.height = 4.0f;
            cap.center = new Vector3(0, 2.0f, 0);
        }
    }

    private bool IsOnApronSand(Vector3 worldPos)
    {
        if (MountainSpawner.Instance == null) return false;
        
        // Ensure TM is ready
        if (_tm == null) _tm = TerrainManager.Instance;
        if (_tm == null || _tm.Config == null) return false;

        float flatness = 0f;
        float influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(worldPos, out flatness);
        
        // Match the base blend logic from SandChunk.cs
        // baseBlend is high near the mountain (flatness=1) and fades out with influence
        float baseBlend = Mathf.Max(flatness, influence * 0.5f);
        
        // Match the Perlin noise from SandChunk.cs exactly
        float noise = Mathf.PerlinNoise(worldPos.x * 0.2f + _tm.Config.Seed, worldPos.z * 0.2f + _tm.Config.Seed);
        float blendFlatness = Mathf.Clamp01(baseBlend + (noise - 0.5f) * 0.7f);
        
        // Condition: Be on the "Rough Sand" texture 
        // We use a lower threshold (0.15) than the texture visual (0.5) to ensure plants 
        // cover the full visible "Rough" area and slightly feather into the desert.
        // We removed the flatness < 0.999f check to ensure plants can grow on the flat 
        // "Apron Base" area, which is where the Rough Sand texture is most prominent.
        bool valid = blendFlatness > 0.15f;

        // Diagnostic log: Only log if we found a valid spot to confirm it's working
        if (valid && Random.value < 0.05f) 
        {
             // Debug.Log($"[ApronBushSpawner] Valid spot at {worldPos.x:F1},{worldPos.z:F1}: blendFlatness={blendFlatness:F3}");
        }

        return valid;
    }
}
