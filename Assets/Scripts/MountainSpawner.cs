using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns static, un-diggable stone mountains that are half-sunk into the sand.
/// Works in tandem with TerrainManager's chunk loading to stream mountains in and out.
/// </summary>
public class MountainSpawner : MonoBehaviour
{
    public static MountainSpawner Instance;
    
    private TerrainManager _tm;
    private Dictionary<Vector2Int, List<GameObject>> _activeMountains = new Dictionary<Vector2Int, List<GameObject>>();
    
    void Awake()
    {
        Instance = this;
    }

    public int ActiveMountainCount()
    {
        return _activeMountains.Count;
    }

    void Start()
    {
        _activeMountains.Clear();
        _tm = TerrainManager.Instance;
        
        if (_tm == null || _tm.Config == null)
        {
            Debug.LogWarning("MountainSpawner: Waiting for valid TerrainManager instance...");
        }
        else
        {
            if (_tm.Config.MountainPrefab == null)
            {
#if UNITY_EDITOR
                _tm.Config.MountainPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/mountain/moutain1.prefab");
#endif
            }

            if (_tm.Config.StonePrefabPaths != null && _tm.Config.LoadedStonePrefabs.Count == 0)
            {
#if UNITY_EDITOR
                foreach (var path in _tm.Config.StonePrefabPaths)
                {
                    GameObject p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (p != null) _tm.Config.LoadedStonePrefabs.Add(p);
                }
#endif
            }
        }
    }

    /// <summary>
    /// Evaluates a chunk coordinate to see if mountains or stones should spawn, and creates them.
    /// </summary>
    public void LoadMountainsForChunk(Vector2Int coord)
    {
        if (_tm == null || _tm.Config == null) _tm = TerrainManager.Instance;
        if (_tm == null || _tm.Config == null) return;

        // IDEMPOTENCY FIX: Don't load if already loaded for this coordinate
        if (_activeMountains.ContainsKey(coord)) return;
        
        // Use the chunk coordinate + a general seed to ensure deterministic spawning
        Random.State oldState = Random.state;
        Random.InitState(_tm.Config.Seed + coord.x * 73856 + coord.y * 19349);
        float rnd = Random.value;
        float spawnChance = _tm.Config.MountainTestMode ? 0.0f : _tm.Config.MountainSpawnChance;
        
        // MOUNTAIN TEST MODE: Force exactly one mountain at the designated test chunk
        bool forceSpawn = _tm.Config.MountainTestMode && coord == _tm.TestMountainChunk;
        
        float stoneThreshold = spawnChance + _tm.Config.StoneSpawnChance;

        // Check for massive mountain spawn
        if (forceSpawn || rnd < spawnChance)
        {
            SpawnObject(coord, true);
        }
        // Check for stone spawn (only if mountain didn't spawn, though you could have both)
        else if (rnd < stoneThreshold)
        {
            SpawnObject(coord, false);
        }

        // Restore random state
        Random.state = oldState;
    }

    /// <summary>
    /// Destroys mountains unloaded by the chunk system.
    /// </summary>
    public void UnloadMountainsForChunk(Vector2Int coord)
    {
        if (_activeMountains.TryGetValue(coord, out List<GameObject> mountains))
        {
            foreach (var mtn in mountains)
            {
                if (mtn != null) Destroy(mtn);
            }
            _activeMountains.Remove(coord);
        }
    }

    /// <summary>
    /// Unloads all mountains except those in the provided active set.
    /// </summary>
    public void UnloadDistantMountains(HashSet<Vector2Int> activeCoords)
    {
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in _activeMountains.Keys)
        {
            if (!activeCoords.Contains(coord))
            {
                toRemove.Add(coord);
            }
        }
        
        foreach (var coord in toRemove)
        {
            UnloadMountainsForChunk(coord);
        }
    }

    private void SpawnObject(Vector2Int coord, bool isMountain)
    {
        float chunkSizeWorld = (_tm.Config.ChunkSize - 1) * _tm.Config.CellSize;
        float worldX = coord.x * chunkSizeWorld + (isMountain ? chunkSizeWorld * 0.5f : Random.Range(0f, chunkSizeWorld));
        float worldZ = coord.y * chunkSizeWorld + (isMountain ? chunkSizeWorld * 0.5f : Random.Range(0f, chunkSizeWorld));

        // SAFE ZONE CHECK: Skip if too close to player spawn point
        // Mountains need a much larger safe radius due to their massive scale (800-1500)
        Vector3 spawnTarget = new Vector3(worldX, 0, worldZ);
        Vector3 playerSpawn = new Vector3(_tm.Config.PlayerSpawnPoint.x, 0, _tm.Config.PlayerSpawnPoint.z);
        float safeRadius = isMountain ? _tm.Config.MountainSafeRadius : _tm.Config.SpawnSafeRadius;
        
        // In Test Mode, ignore the safe radius for mountains to allow immediate spawning near player
        if (!_tm.Config.MountainTestMode && Vector3.Distance(spawnTarget, playerSpawn) < safeRadius)
        {
            return;
        }

        // OASIS CHECK: Never spawn mountains or stones inside an oasis basin!
        var nearbyOases = _tm.GetNearbyOases(coord);
        foreach (var oasis in nearbyOases)
        {
            if (Vector2.Distance(new Vector2(worldX, worldZ), oasis.position) <= oasis.basinRadius)
            {
                return;
            }
        }

        // Base height for mountains since the terrain is entirely governed by Flatness=1 right underneath it
        float surfaceHeight = isMountain ? _tm.Config.BaseHeight : _tm.SampleHeight(new Vector3(worldX, 0, worldZ));

        // STONES: Extra check for requested height range and water level
        if (!isMountain)
        {
            if (surfaceHeight < _tm.Config.StoneMinSpawnHeight || surfaceHeight > _tm.Config.StoneMaxSpawnHeight)
            {
                return;
            }
            
            // Prevent stones from spawning if the water is touching them (water is at BaseHeight)
            if (surfaceHeight < _tm.Config.BaseHeight + 0.5f)
            {
                return;
            }
        }

        float scale = isMountain ? Random.Range(_tm.Config.MountainMinScale, _tm.Config.MountainMaxScale) 
                                 : Random.Range(_tm.Config.StoneMinScale, _tm.Config.StoneMaxScale);
        
        // Grounding logic: For mountains, offset is a fraction of scale.
        // If the asset still has the old >1 value (like 3.0), clamp or fix it so we don't bury the mountain.
        float mountainGrounding = _tm.Config.MountainGroundingOffset;
        if (mountainGrounding > 1f) mountainGrounding = 0.2f; // Fallback

        float groundingOffset = isMountain 
            ? scale * mountainGrounding 
            : _tm.Config.StoneGroundingOffset;
        float yPos = surfaceHeight - groundingOffset; 

        Vector3 pos = new Vector3(worldX, yPos, worldZ);
        
        GameObject obj;
        GameObject prefabToUse = isMountain ? _tm.Config.MountainPrefab : null;
        if (!isMountain && _tm.Config.LoadedStonePrefabs != null && _tm.Config.LoadedStonePrefabs.Count > 0)
        {
            prefabToUse = _tm.Config.LoadedStonePrefabs[Random.Range(0, _tm.Config.LoadedStonePrefabs.Count)];
        }

        if (prefabToUse != null)
        {
            obj = Instantiate(prefabToUse, pos, Quaternion.identity, transform);
            
            // Randomize X/Z and Y scale independently for stones if they are prefabs
            if (!isMountain)
            {
                float verticalScale = scale * Random.Range(_tm.Config.StoneMinHeightScale, _tm.Config.StoneMaxHeightScale);
                obj.transform.localScale = new Vector3(scale, verticalScale, scale);
            }
            else
            {
                obj.transform.localScale = Vector3.one * scale;
            }

            obj.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        }
        else if (isMountain)
        {
            obj = new GameObject($"MassiveMountain_{coord.x}_{coord.y}");
            obj.transform.position = pos;
            obj.transform.parent = transform;
            
            MeshFilter mf = obj.AddComponent<MeshFilter>();
            MeshRenderer mr = obj.AddComponent<MeshRenderer>();
            
            if (_tm.Config.MountainMaterial != null)
                mr.sharedMaterial = _tm.Config.MountainMaterial;
            else
            {
                mr.material = new Material(Shader.Find("Standard"));
                mr.material.color = new Color(0.6f, 0.3f, 0.2f);
            }
            
            obj.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            
            // Generate the procedural mesh using the improved stratified geometry
            mf.sharedMesh = GenerateProceduralRockMesh(pos, true);
            
            // Procedural scaling: Mountains are taller
            float yMult = Random.Range(0.8f, 1.3f);
            obj.transform.localScale = new Vector3(scale, scale * yMult, scale);
            
            MeshCollider mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
        }
        else
        {
            // It's a stone but no prefab was found. We no longer want to form "ScatteredStone" procedural objects.
            return;
        }
        
        if (!_activeMountains.ContainsKey(coord))
            _activeMountains[coord] = new List<GameObject>();
            
        _activeMountains[coord].Add(obj);
    }

    /// <summary>
    /// Generates a Desert Mesa/Butte style rock mesh with multi-octave noise and stratification.
    /// </summary>
    private Mesh GenerateProceduralRockMesh(Vector3 worldPosOffset, bool isMountain)
    {
        Mesh mesh = new Mesh();
        mesh.name = "DesertRockProfile";

        int segments = isMountain ? 32 : 16; 
        int rings = isMountain ? 15 : 6;     
        
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        // Procedural Profile Generation logic
        float[] radii = new float[rings + 1];
        float[] heights = new float[rings + 1];
        
        for (int r = 0; r <= rings; r++)
        {
            float pct = r / (float)rings;
            // Base profile shape: tapering towards top
            float curve = isMountain ? Mathf.Pow(1.0f - pct, 0.4f) : Mathf.Pow(1.0f - pct, 0.7f);
            
            // Add stratification (shelves/cliffs) for mountains
            if (isMountain)
            {
                float stepScale = 5.0f;
                float stepNoise = (Mathf.Floor(pct * stepScale + Mathf.Sin(worldPosOffset.x * 0.1f)) / stepScale);
                curve = Mathf.Lerp(curve, stepNoise, 0.15f);
            }

            radii[r] = curve * 1.0f;
            heights[r] = pct;
        }

        for (int r = 0; r <= rings; r++)
        {
            float y = heights[r];
            float baseRadius = radii[r];

            for (int s = 0; s <= segments; s++) 
            {
                float pctS = s / (float)segments;
                float angle = pctS * Mathf.PI * 2f;
                
                float noiseX = Mathf.Cos(angle) * 1.5f + worldPosOffset.x * 0.13f;
                float noiseZ = Mathf.Sin(angle) * 1.5f + worldPosOffset.z * 0.17f;
                float noiseY = y * 2.5f + worldPosOffset.y * 0.11f;

                // Multi-octave "Jagged" Noise
                float noiseVal = 1.0f;
                {
                    // 1. Large scale shape
                    float n1 = Mathf.PerlinNoise(noiseX * 0.5f, noiseZ * 0.5f);
                    
                    // 2. Mid scale ridges (Ridged noise for sharp edges)
                    float n2 = Mathf.PerlinNoise(noiseX * 1.8f + noiseY, noiseZ * 1.8f);
                    n2 = 1.0f - Mathf.Abs(n2 * 2.0f - 1.0f);
                    
                    // 3. Small scale crags
                    float n3 = Mathf.PerlinNoise(noiseX * 7.0f, noiseZ * 7.0f + noiseY * 3.0f);
                    
                    float intensity = isMountain ? 0.35f : 0.7f;
                    noiseVal = 1.0f + (n1 * 0.5f + n2 * 0.6f + n3 * 0.2f - 0.6f) * intensity;
                }

                // Periodic vertical stratification for mesa look
                float driftScale = isMountain ? 25.0f : 10.0f;
                float verticalDrift = Mathf.Sin(y * driftScale + worldPosOffset.x) * (isMountain ? 0.01f : 0.03f) * (1.0f - y);

                float localX = Mathf.Cos(angle) * baseRadius * noiseVal;
                float localZ = Mathf.Sin(angle) * baseRadius * noiseVal;
                float finalY = y + verticalDrift;

                verts.Add(new Vector3(localX, finalY, localZ));
                uvs.Add(new Vector2(worldPosOffset.x + localX, worldPosOffset.z + localZ) * 0.1f);
            }
        }

        int vertsPerRow = segments + 1;
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int bottom = r * vertsPerRow + s;
                int top = (r + 1) * vertsPerRow + s;
                int bottomNext = bottom + 1;
                int topNext = top + 1;

                tris.Add(bottom);
                tris.Add(top);
                tris.Add(bottomNext);
                
                tris.Add(bottomNext);
                tris.Add(top);
                tris.Add(topNext);
            }
        }

        // Bottom Cap
        verts.Add(Vector3.zero);
        uvs.Add(new Vector2(0.5f, 0.5f));
        int bottomCenterIdx = verts.Count - 1;
        for (int s = 0; s < segments; s++)
        {
            tris.Add(bottomCenterIdx);
            tris.Add(s + 1);
            tris.Add(s);
        }

        // Top Cap (Closing the cylinder effectively)
        verts.Add(new Vector3(0, heights[rings], 0));
        uvs.Add(new Vector2(0.5f, 0.5f));
        int topCenterIdx = verts.Count - 1;
        int topRingBase = rings * vertsPerRow;
        for (int s = 0; s < segments; s++)
        {
            tris.Add(topCenterIdx);
            tris.Add(topRingBase + s);
            tris.Add(topRingBase + s + 1);
        }

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    public struct MountainData
    {
        public Vector2 position;
        public float radius;          // total outer radius (footprint + flat + influence)
        public float footprintRadius; // radius of the mountain mesh itself
        public float flatRadius;      // dynamic proportionally scaled flat ring
        public float extraRadius;     // dynamic proportionally scaled extra influence ring
    }

    private Dictionary<Vector2Int, List<MountainData>> _nearbyMountainsCache = new Dictionary<Vector2Int, List<MountainData>>();

    /// <summary>
    /// Gets a list of upcoming massive mountains that could influence terrain generation near the target chunk.
    /// Uses deterministic noise so it works even for unloaded chunks.
    /// </summary>
    public List<MountainData> GetNearbyMountains(Vector2Int targetCoord)
    {
        if (_nearbyMountainsCache.TryGetValue(targetCoord, out List<MountainData> cached))
        {
            return cached;
        }

        List<MountainData> result = new List<MountainData>();
        if (_tm == null || _tm.Config == null) return result;
        
        float chunkSizeWorld = _tm.GetChunkSizeWorld();
        Vector2 targetPos = new Vector2(targetCoord.x * chunkSizeWorld, targetCoord.y * chunkSizeWorld);
        
        // A mountain can have scale 1500 -> radius 1500 * 1.5. Chunk = 63. 36 chunks radius approx 2250 units.
        int checkRadius = 40; 
        
        for (int y = -checkRadius; y <= checkRadius; y++)
        {
            for (int x = -checkRadius; x <= checkRadius; x++)
            {
                Vector2Int coord = new Vector2Int(targetCoord.x + x, targetCoord.y + y);
                
                Random.State oldState = Random.state;
                Random.InitState(_tm.Config.Seed + coord.x * 73856 + coord.y * 19349);
                
                float spawnChance = _tm.Config.MountainTestMode ? 0.0f : _tm.Config.MountainSpawnChance;
                bool forceSpawn = _tm.Config.MountainTestMode && coord == _tm.TestMountainChunk;
                
                if (forceSpawn || Random.value < spawnChance)
                {
                    float worldX = coord.x * chunkSizeWorld + chunkSizeWorld * 0.5f; // Mountains are now centered
                    float worldZ = coord.y * chunkSizeWorld + chunkSizeWorld * 0.5f; 
                    float scale = Random.Range(_tm.Config.MountainMinScale, _tm.Config.MountainMaxScale);
                    
                    Vector3 spawnTarget = new Vector3(worldX, 0, worldZ);
                    Vector3 playerSpawn = new Vector3(_tm.Config.PlayerSpawnPoint.x, 0, _tm.Config.PlayerSpawnPoint.z);
                    
                    if (_tm.Config.MountainTestMode || Vector3.Distance(spawnTarget, playerSpawn) >= _tm.Config.MountainSafeRadius)
                    {
                        // Use the actual prefab mesh radius to determine the footprint
                        float footprintScale = scale * _tm.Config.MountainMeshRadiusMultiplier;
                        
                        // Mountains scale wildly, so scale the flat radius proportionally (around a nominal scale of 1000)
                        float scaleFactor = scale / 1000f;
                        float dynamicFlatRadius = _tm.Config.MountainFlatRadius * scaleFactor;
                        float dynamicExtraRadius = _tm.Config.MountainInfluenceExtraRadius * scaleFactor;
                        
                        float radius = footprintScale + dynamicFlatRadius + dynamicExtraRadius; 
                        float distToChunk = Vector2.Distance(targetPos, new Vector2(worldX, worldZ));
                        
                        if (distToChunk < radius + chunkSizeWorld * 1.5f)
                        {
                            result.Add(new MountainData {
                                position = new Vector2(worldX, worldZ),
                                radius = radius,
                                footprintRadius = footprintScale,
                                flatRadius = dynamicFlatRadius,
                                extraRadius = dynamicExtraRadius
                            });
                        }
                    }
                }
                
                Random.state = oldState;
            }
        }
        _nearbyMountainsCache[targetCoord] = result;
        return result;
    }

    public float GetMountainInfluenceAtPoint(Vector3 worldPos)
    {
        return GetMountainInfluenceAtPoint(worldPos, out _);
    }

    public float GetMountainInfluenceAtPoint(Vector3 worldPos, out float flatness)
    {
        flatness = 0f;
        // Ensure we have a valid TerrainManager reference
        if (_tm == null || _tm.Config == null) _tm = TerrainManager.Instance;
        if (_tm == null) return 0f;
        int cX = Mathf.FloorToInt(worldPos.x / _tm.GetChunkSizeWorld());
        int cZ = Mathf.FloorToInt(worldPos.z / _tm.GetChunkSizeWorld());
        
        var nearby = GetNearbyMountains(new Vector2Int(cX, cZ));
        if (nearby.Count > 0 && worldPos.y < 20f) // Only log for ground-level points
        {
             // Debug.Log($"Checking influence for {nearby.Count} nearby mountains at {worldPos}");
        }
        float maxInf = 0f;
        Vector2 pos = new Vector2(worldPos.x, worldPos.z);
        foreach(var m in nearby)
        {
            float dist = Vector2.Distance(pos, m.position);
            
            // Use the stored footprint radius directly
            float footprintScale = m.footprintRadius;
            float flatOuterScale = footprintScale + m.flatRadius;
            
            if (dist < footprintScale)
            {
                maxInf = 1f;
                flatness = 1f;
            }
            else if (dist < flatOuterScale)
            {
                maxInf = 1f;
                // Inner flat ring: fade flatness from 1.0 to 0.0
                float t = 1.0f - ((dist - footprintScale) / m.flatRadius);
                flatness = Mathf.Max(flatness, Mathf.SmoothStep(0f, 1f, t));
            }
            else if (dist < m.radius)
            {
                // Outer low-wave ring: fade influence from 1.0 to 0.0
                float t = 1.0f - ((dist - flatOuterScale) / m.extraRadius);
                maxInf = Mathf.Max(maxInf, Mathf.SmoothStep(0f, 1f, t));
            }
            
            if (maxInf >= 1f && flatness >= 1f) break; 
        }
        return maxInf;
    }
}
