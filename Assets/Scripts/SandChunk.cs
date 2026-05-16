using UnityEngine;
using Unity.Collections;
using Unity.Jobs;


[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class SandChunk : MonoBehaviour
{
    public Vector2Int ChunkCoord;
    public TerrainConfig Config;
    
    // Data (Persistent for Jobs)
    // Double Buffering: One for reading (current state), one for writing (next state)
    public NativeArray<float> HeightsRead;
    public NativeArray<float> HeightsWrite;

    // Per-vertex flatness data for apron sand texture blending
    // 0 = desert (far from mountains), 1 = flat apron (close to mountain base)
    public NativeArray<float> FlatnessData;
    
    // Visuals
    // Neighbor Cache (8 Neighbors: N, S, E, W, NE, NW, SE, SW)
    // Order: 0=N, 1=S, 2=E, 3=W, 4=NE, 5=NW, 6=SE, 7=SW
    [HideInInspector] public SandChunk[] Neighbors = new SandChunk[8];

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MeshCollider _mc;
    private Mesh _mesh;

    [System.NonSerialized] public bool NeedsColliderBake;

    public bool IsInitialized { get; private set; }
    public bool IsSimulating { get; private set; } = true;
    public bool HasActiveFlow => _flowTimer > 0f;

    private float _flowTimer;

    // Counts
    private int _numSurfaceVerts;
    private int _numSkirtVerts;
    private int _totalVerts;
    private int _totalIndices;

    // Modified Flag for simulation
    public NativeArray<int> ModifiedFlag;

    // Temporary Buffers for batched mesh update
    private NativeArray<Vector3> _meshVerts;
    private NativeArray<Vector3> _meshNormals;
    private NativeArray<Vector2> _meshUVs;
    private NativeArray<Color> _meshColors;

    private bool _needsBake = true;

    public void Initialize(Vector2Int coord, TerrainConfig config, float[] initialData = null)
    {
        ChunkCoord = coord;
        Config = config;
        
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mc = GetComponent<MeshCollider>();
        
        // Explicitly enable shadows for lighting interaction
        _mr.receiveShadows = true;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        
        // Material Setup: Prefer ApronSandMaterial (SandBlend shader) if available
        if (Config.ApronSandMaterial != null)
        {
            _mr.sharedMaterial = Config.ApronSandMaterial;
        }
        else if (Config.SandMaterial != null)
        {
            _mr.sharedMaterial = Config.SandMaterial;
        }
        else
        {
            // Fallback: Create material with SandBlend shader or Standard
            Shader shader = Shader.Find("Custom/SandBlend");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.name = "GeneratedSandBlendMat";
                _mr.material = mat;
            }
        }

        // Apply Config Colors via MaterialPropertyBlock
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        _mr.GetPropertyBlock(block);
        block.SetColor("_BaseColor", Config.SandColor); 
        block.SetColor("_Color", Config.SandColor);
        if (Config.ApronSandTexture != null)
        {
            block.SetTexture("_SecondaryTex", Config.ApronSandTexture);
        }
        block.SetColor("_SecondaryColor", Config.ApronSandColor);
        _mr.SetPropertyBlock(block);
        
        _mesh = new Mesh();
        // IndexFormat auto-detect or force 32 bit if needed (Small chunks ok with 16)
        // 64x64 = 4096. + Skirts (~512). Total < 5000. 16 bit is fine.
        _mesh.MarkDynamic();
        _mesh.name = $"Chunk_{coord.x}_{coord.y}";
        _mf.sharedMesh = _mesh;

        // Fallback Floor Collider (Prevent falling through)
        // This is a safety net if the player slips through a mesh crack or digs too deep.
        BoxCollider bc = gameObject.AddComponent<BoxCollider>();
        float sizeVal = Config.ChunkSize * Config.CellSize;
        // Make it slightly wider (4.0f margin) to overlap neighbors and close physical gaps
        float thickness = 100.0f;
        bc.size = new Vector3(sizeVal + 4.0f, thickness, sizeVal + 4.0f);
        bc.center = new Vector3(sizeVal * 0.5f - 0.5f * Config.CellSize, -Config.BottomDepth - (thickness * 0.5f), sizeVal * 0.5f - 0.5f * Config.CellSize);

        // Pre-calculate Counts
        int size = Config.ChunkSize;
        _numSurfaceVerts = size * size;
        
        // 4 Skirts, 2 vertices per edge point (Top/Bottom)
        _numSkirtVerts = 8 * size; 
        
        int numBottomVerts = 4; // Simplified Quad
        _totalVerts = _numSurfaceVerts + _numSkirtVerts + numBottomVerts;

        // Allocate Memory
        HeightsRead = new NativeArray<float>(_numSurfaceVerts, Allocator.Persistent);
        HeightsWrite = new NativeArray<float>(_numSurfaceVerts, Allocator.Persistent);
        FlatnessData = new NativeArray<float>(_numSurfaceVerts, Allocator.Persistent);
        ModifiedFlag = new NativeArray<int>(1, Allocator.Persistent);
        
        if (initialData != null && initialData.Length == _numSurfaceVerts)
        {
            HeightsRead.CopyFrom(initialData);
            HeightsWrite.CopyFrom(initialData);
        }
        else
        {
            GenerateInitialTerrain();
            PrewarmTerrain(); // Settle sand before first render
        }

        GenerateTriangles();
        
        // Initial Mesh Creation
        ScheduleMeshUpdate(default).Complete();
        ApplyMeshUpdate();
        NeedsColliderBake = true;
        BakeCollider();
        
        IsInitialized = true;
    }

    void PrewarmTerrain()
    {
        // Run simulation for a few frames to settle the sand
        // This prevents the "falling through" issue caused by rapid initial flow
        // diverging from the throttled collider mesh.
        for (int i = 0; i < 10; i++)
        {
             var job = new SandJobs.SimulationJob
            {
                Size = Config.ChunkSize,
                FlowThreshold = Config.FlowThreshold,
                FlowSpeed = Config.FlowSpeed,
                ReadHeights = HeightsRead,
                WriteHeights = HeightsWrite,

                // Neighbors don't exist during prewarm
                ReadN = HeightsRead, ReadS = HeightsRead, ReadE = HeightsRead, ReadW = HeightsRead,
                HasN = false, HasS = false, HasE = false, HasW = false,
                
                ReadNE = HeightsRead, ReadNW = HeightsRead, ReadSE = HeightsRead, ReadSW = HeightsRead,
                HasNE = false, HasNW = false, HasSE = false, HasSW = false,

                ModifiedFlag = ModifiedFlag
            };
            job.Run(); // Run synchronously
            SwapBuffers();
        }
    }

    void GenerateInitialTerrain()
    {
        float worldXBase = ChunkCoord.x * (Config.ChunkSize - 1) * Config.CellSize;
        float worldZBase = ChunkCoord.y * (Config.ChunkSize - 1) * Config.CellSize;

        var nearbyMountains = MountainSpawner.Instance != null 
            ? MountainSpawner.Instance.GetNearbyMountains(ChunkCoord)
            : new System.Collections.Generic.List<MountainSpawner.MountainData>();

        // Query oases once per chunk (not per vertex)
        var nearbyOases = TerrainManager.Instance != null
            ? TerrainManager.Instance.GetNearbyOases(ChunkCoord)
            : new System.Collections.Generic.List<TerrainManager.OasisData>();

        if (nearbyMountains.Count > 0)
        {
            Debug.Log($"[SandChunk] Chunk {ChunkCoord} found {nearbyMountains.Count} nearby mountains. First at ({nearbyMountains[0].position.x:F0},{nearbyMountains[0].position.y:F0}) radius={nearbyMountains[0].radius:F0}");
        }

        for (int z = 0; z < Config.ChunkSize; z++)
        {
            for (int x = 0; x < Config.ChunkSize; x++)
            {
                float wx = worldXBase + x * Config.CellSize;
                float wz = worldZBase + z * Config.CellSize;
                
                float maxInf = 0f;
                float flatness = 0f;
                Vector2 pos = new Vector2(wx, wz);
                foreach(var m in nearbyMountains)
                {
                    float dist = Vector2.Distance(pos, m.position);
                    // Use stored footprint radius directly (the mountain mesh extent)
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
                        float t = 1.0f - ((dist - footprintScale) / m.flatRadius);
                        flatness = Mathf.Max(flatness, Mathf.SmoothStep(0f, 1f, t));
                    }
                    else if (dist < m.radius)
                    {
                        float t = 1.0f - ((dist - flatOuterScale) / m.extraRadius);
                        maxInf = Mathf.Max(maxInf, Mathf.SmoothStep(0f, 1f, t));
                    }
                }

                // Oasis basin influence
                float oasisInf = 0f;
                float oasisFlatInf = 0f;
                float oasisRimInf = 0f;
                float oasisShoreRidgeInf = 0f;

                foreach (var o in nearbyOases)
                {
                    float d = Vector2.Distance(pos, o.position);
                    
                    // Basin Dip — LINEAR t so the bowl slopes immediately from the rim
                    if (d < o.basinRadius)
                    {
                        float t = Mathf.Clamp01(1.0f - (d / o.basinRadius));
                        oasisInf = Mathf.Max(oasisInf, t);
                    }
                    
                    // ── Rim Ridge Elevation ──
                    float distFromRim = Mathf.Abs(d - o.basinRadius);
                    if (distFromRim < Config.OasisRimWidth) {
                        float rimT = 1.0f - (distFromRim / Config.OasisRimWidth);
                        oasisRimInf = Mathf.Max(oasisRimInf, Mathf.SmoothStep(0f, 1f, rimT));
                    }

                    // ── Shore Ridge for Vegetation Side ──
                    // Replicate deterministic angle calculation from TerrainManager
                    Random.State oldVegState = Random.state;
                    Random.InitState(Config.Seed + o.chunkCoord.x * 11111 + o.chunkCoord.y * 22222);
                    float vegAngle = Random.Range(0f, Mathf.PI * 2f);
                    Random.state = oldVegState;

                    float pointAngle = Mathf.Atan2(wz - o.position.y, wx - o.position.x);
                    float angleDiff = Mathf.Abs(Mathf.DeltaAngle(pointAngle * Mathf.Rad2Deg, vegAngle * Mathf.Rad2Deg));

                    if (angleDiff < 60f) 
                    {
                        float angleT = 1.0f - (angleDiff / 60f);
                        float angleFactor = Mathf.SmoothStep(0f, 1f, angleT);

                        float distFromShore = Mathf.Abs(d - o.waterRadius);
                        float ridgeWidth = 15f; 
                        if (distFromShore < ridgeWidth)
                        {
                            float distT = 1.0f - (distFromShore / ridgeWidth);
                            float ridgeInf = Mathf.SmoothStep(0f, 1f, distT) * angleFactor;
                            oasisShoreRidgeInf = Mathf.Max(oasisShoreRidgeInf, ridgeInf);
                        }
                    }

                    // Flat Beach / Noise Suppression
                    float flatRad = o.basinRadius + 100f;
                    if (d < flatRad) 
                    {
                        float tFlat = 1.0f - (d / flatRad);
                        oasisFlatInf = Mathf.Max(oasisFlatInf, Mathf.SmoothStep(0f, 1f, tFlat));
                    }
                }
                
                float fHeight = TerrainManager.Instance.GetSurfaceHeight(wx, wz, maxInf, flatness, oasisInf, oasisFlatInf, oasisRimInf, oasisShoreRidgeInf);
                
                if (maxInf > 0f) 
                {
                    // Only log rarely so we don't spam the console too much
                    if (x == 32 && z == 32)
                        Debug.Log($"Mountain Influence applied at {wx},{wz}: maxInf={maxInf}, flatness={flatness}");
                }
                
                int idx = x + z * Config.ChunkSize;
                HeightsRead[idx] = fHeight;
                HeightsWrite[idx] = fHeight;

                // Store flatness for vertex color blending (apron sand texture)
                // Use mountain influence as well so the transition extends beyond just the flat zone
                float baseBlend = Mathf.Max(flatness, maxInf * 0.5f);
                
                // Add Perlin noise to make the transition edge organic instead of a perfect circle
                float noise = Mathf.PerlinNoise(wx * 0.2f + Config.Seed, wz * 0.2f + Config.Seed);
                float blendFlatness = Mathf.Clamp01(baseBlend + (noise - 0.5f) * 0.7f);
                
                FlatnessData[idx] = blendFlatness;
            }
        }
    }

    public void SwapBuffers()
    {
        // Swap Read and Write buffers
        var temp = HeightsRead;
        HeightsRead = HeightsWrite;
        HeightsWrite = temp;
    }

    void GenerateTriangles()
    {
        int size = Config.ChunkSize;
        
        // Quad Counts
        int numSurfaceQuads = (size - 1) * (size - 1);
        int numSkirtQuads = (size - 1) * 4; 
        int numBottomQuads = 1; 
        
        // Surface (6) + Double-Sided Skirts (12) + Double-Sided Bottom (12)
        _totalIndices = (numSurfaceQuads * 6) + (numSkirtQuads * 12) + (numBottomQuads * 12); 
        int[] tris = new int[_totalIndices];
        int t = 0;

        // 1. Surface
        for (int z = 0; z < size - 1; z++)
        {
            for (int x = 0; x < size - 1; x++)
            {
                int root = x + z * size;
                tris[t++] = root; tris[t++] = root + size; tris[t++] = root + 1;
                tris[t++] = root + 1; tris[t++] = root + size; tris[t++] = root + size + 1;
            }
        }

        // 2. Skirts (Walls) - Double Sided
        int vBase = _numSurfaceVerts;
        
        // Helper to add a double-sided quad
        System.Action<int, int, int, int> addQuadDouble = (top, bot, topN, botN) => {
            // Face Out
            tris[t++] = top; tris[t++] = topN; tris[t++] = bot;
            tris[t++] = topN; tris[t++] = botN; tris[t++] = bot;
            // Face In
            tris[t++] = top; tris[t++] = bot; tris[t++] = topN;
            tris[t++] = topN; tris[t++] = bot; tris[t++] = botN;
        };

        // Strip 1: Top (z=size-1)
        for (int x = 0; x < size - 1; x++) {
            addQuadDouble(vBase+x, vBase+size+x, vBase+x+1, vBase+size+x+1);
        }
        vBase += 2 * size;

        // Strip 2: Bottom (z=0)
        for (int x = 0; x < size - 1; x++) {
            addQuadDouble(vBase+x, vBase+size+x, vBase+x+1, vBase+size+x+1);
        }
        vBase += 2 * size;

        // Strip 3: Right (x=size-1)
        for (int z = 0; z < size - 1; z++) {
            addQuadDouble(vBase+z, vBase+size+z, vBase+z+1, vBase+size+z+1);
        }
        vBase += 2 * size;

        // Strip 4: Left (x=0)
        for (int z = 0; z < size - 1; z++) {
            addQuadDouble(vBase+z, vBase+size+z, vBase+z+1, vBase+size+z+1);
        }

        // 3. Bottom Face (Double Sided)
        int b = _numSurfaceVerts + _numSkirtVerts;
        tris[t++] = b + 0; tris[t++] = b + 2; tris[t++] = b + 1;
        tris[t++] = b + 1; tris[t++] = b + 2; tris[t++] = b + 3;
        tris[t++] = b + 0; tris[t++] = b + 1; tris[t++] = b + 2;
        tris[t++] = b + 1; tris[t++] = b + 3; tris[t++] = b + 2;

        _mesh.vertices = new Vector3[_totalVerts]; 
        _mesh.triangles = tris;
    }

    public JobHandle ScheduleMeshUpdate(JobHandle dependsOn)
    {
        if (_mesh == null) return dependsOn;
        int size = Config.ChunkSize;
        
        // Cleanup old buffers if they exist (shouldn't if pipeline is clean)
        CleanupTempBuffers();

        _meshVerts = new NativeArray<Vector3>(_totalVerts, Allocator.TempJob);
        _meshNormals = new NativeArray<Vector3>(_totalVerts, Allocator.TempJob);
        _meshUVs = new NativeArray<Vector2>(_totalVerts, Allocator.TempJob);
        _meshColors = new NativeArray<Color>(_totalVerts, Allocator.TempJob);

        // Use Cached Neighbors for seamless normals and stitching
        SandChunk nN = Neighbors[0]; SandChunk nS = Neighbors[1];
        SandChunk nE = Neighbors[2]; SandChunk nW = Neighbors[3];
        SandChunk nNE = Neighbors[4]; SandChunk nNW = Neighbors[5];
        SandChunk nSE = Neighbors[6]; SandChunk nSW = Neighbors[7];

        // Schedule Mesh Job
        var job = new SandJobs.MeshJob {
            Size = size, CellSize = Config.CellSize, BottomDepth = Config.BottomDepth, Origin = transform.position,
            Heights = HeightsRead,
            
            HeightsN = (nN != null && nN.IsInitialized && nN.HeightsRead.IsCreated) ? nN.HeightsRead : HeightsRead,
            HeightsS = (nS != null && nS.IsInitialized && nS.HeightsRead.IsCreated) ? nS.HeightsRead : HeightsRead,
            HeightsE = (nE != null && nE.IsInitialized && nE.HeightsRead.IsCreated) ? nE.HeightsRead : HeightsRead,
            HeightsW = (nW != null && nW.IsInitialized && nW.HeightsRead.IsCreated) ? nW.HeightsRead : HeightsRead,
            
            ReadNE = (nNE != null && nNE.IsInitialized && nNE.HeightsRead.IsCreated) ? nNE.HeightsRead : HeightsRead,
            ReadNW = (nNW != null && nNW.IsInitialized && nNW.HeightsRead.IsCreated) ? nNW.HeightsRead : HeightsRead,
            ReadSE = (nSE != null && nSE.IsInitialized && nSE.HeightsRead.IsCreated) ? nSE.HeightsRead : HeightsRead,
            ReadSW = (nSW != null && nSW.IsInitialized && nSW.HeightsRead.IsCreated) ? nSW.HeightsRead : HeightsRead,

            HasN = nN != null && nN.IsInitialized, HasS = nS != null && nS.IsInitialized,
            HasE = nE != null && nE.IsInitialized, HasW = nW != null && nW.IsInitialized,
            HasNE = nNE != null && nNE.IsInitialized, HasNW = nNW != null && nNW.IsInitialized,
            HasSE = nSE != null && nSE.IsInitialized, HasSW = nSW != null && nSW.IsInitialized,

            FlatnessData = FlatnessData,
            HasFlatnessData = FlatnessData.IsCreated,

            Verts = _meshVerts, Normals = _meshNormals, UVs = _meshUVs, Colors = _meshColors
        };

        _needsBake = true; // Signal that collider should update after this mesh change
        return job.Schedule(dependsOn);
    }

    public void ApplyMeshUpdate()
    {
        if (!_meshVerts.IsCreated) return;

        // Apply to Mesh
        _mesh.SetVertices(_meshVerts);
        _mesh.SetNormals(_meshNormals);
        _mesh.SetUVs(0, _meshUVs);
        if (_meshColors.IsCreated) _mesh.SetColors(_meshColors);
        
        // STATIC BOUNDS
        if (_mesh.bounds.size.magnitude < 1.0f) {
             float s = Config.ChunkSize * Config.CellSize;
             _mesh.bounds = new Bounds(new Vector3(s*0.5f, 0, s*0.5f), new Vector3(s*2, 200, s*2));
        }
        
        // Signal that the collider needs to be re-synchronized with this new mesh
        _needsBake = true; 

        CleanupTempBuffers();
    }

    private void CleanupTempBuffers()
    {
        if (_meshVerts.IsCreated) _meshVerts.Dispose();
        if (_meshNormals.IsCreated) _meshNormals.Dispose();
        if (_meshUVs.IsCreated) _meshUVs.Dispose();
        if (_meshColors.IsCreated) _meshColors.Dispose();
    }

    public bool ModifyHeight(Vector3 worldPos, float amount, float radius)
    {
        // Convert worldPos to Local Grid Space
        float localX = (worldPos.x - transform.position.x) / Config.CellSize;
        float localZ = (worldPos.z - transform.position.z) / Config.CellSize;
        
        int centerX = Mathf.RoundToInt(localX); int centerZ = Mathf.RoundToInt(localZ);
        int r = Mathf.CeilToInt(radius / Config.CellSize);
        
        int size = Config.ChunkSize; bool modified = false;

        for (int z = -r; z <= r; z++) {
            for (int x = -r; x <= r; x++) {
                if (x*x + z*z > r*r) continue;
                
                int nx = centerX + x; int nz = centerZ + z;
                
                if (nx >= 0 && nx < size && nz >= 0 && nz < size) {
                    int idx = nx + nz * size;
                    HeightsRead[idx] += amount; HeightsWrite[idx] += amount; // Update both to be safe/instant
                    modified = true;
                }
            }
        }
        
        if (modified) {
            RestartFlow();
            ScheduleMeshUpdate(default).Complete();
            ApplyMeshUpdate();
        }

        return modified;
    }

    public void RestartFlow()
    {
        _flowTimer = Config != null ? Mathf.Max(0f, Config.FlowDurationAfterEdit) : 2.5f;
    }

    public void TickFlow(float deltaTime)
    {
        if (_flowTimer <= 0f) return;
        _flowTimer = Mathf.Max(0f, _flowTimer - deltaTime);
    }

    /// <summary>
    /// Gets the current height of the sand at a local point within the chunk.
    /// </summary>
    public float GetHeightAt(Vector3 worldPos)
    {
        if (!IsInitialized || !HeightsRead.IsCreated) return 0;

        float localX = (worldPos.x - transform.position.x) / Config.CellSize;
        float localZ = (worldPos.z - transform.position.z) / Config.CellSize;

        int x = Mathf.Clamp(Mathf.RoundToInt(localX), 0, Config.ChunkSize - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(localZ), 0, Config.ChunkSize - 1);

        return HeightsRead[x + z * Config.ChunkSize];
    }

    // LOD Settings
    // LOD Settings (Moved to Config)
    // private const float COLLIDER_ENABLE_DIST ...

    public void UpdateLOD(float distanceToPlayer, float colliderDist, float simDist)
    {
        // 1. Collider Enabled State
        bool shouldHaveCollider = distanceToPlayer < colliderDist;
        if (_mc.enabled != shouldHaveCollider) {
            _mc.enabled = shouldHaveCollider;
        }

        // 2. Physics Baking (deferred — TerrainManager processes 2 per frame)
        // Re-baking a MeshCollider is expensive. Flag it instead of baking immediately.
        if (shouldHaveCollider) {
            if (_needsBake || _mc.sharedMesh == null) {
                NeedsColliderBake = true;
                _needsBake = false;
            }
        }

        // 3. Simulation State
        IsSimulating = distanceToPlayer < simDist;
    }

    /// <summary>
    /// Performs the deferred MeshCollider bake. Called by TerrainManager (2 per frame).
    /// </summary>
    public void BakeCollider()
    {
        if (!NeedsColliderBake) return;
        _mc.sharedMesh = null;
        _mc.sharedMesh = _mesh;
        NeedsColliderBake = false;
    }

    /// <summary>
    /// Blends this chunk's border heights with existing neighbors so there are no visible seam lines.
    /// Called after neighbors are assigned and the chunk has been prewarmed.
    /// </summary>
    public void SyncEdgesFromNeighbors()
    {
        if (!IsInitialized || !HeightsRead.IsCreated) return;
        int size = Config.ChunkSize;
        int blendWidth = Mathf.Clamp(size / 4, 8, 16); // Wider blend zone: 8-16 cells deep

        // Cardinal edges: 0=N, 1=S, 2=E, 3=W
        SyncEdgeStrip(Neighbors[3], 3, size, blendWidth); // West
        SyncEdgeStrip(Neighbors[2], 2, size, blendWidth); // East
        SyncEdgeStrip(Neighbors[1], 1, size, blendWidth); // South
        SyncEdgeStrip(Neighbors[0], 0, size, blendWidth); // North
    }

    private void SyncEdgeStrip(SandChunk neighbor, int dir, int size, int blendWidth)
    {
        if (neighbor == null || !neighbor.IsInitialized || !neighbor.HeightsRead.IsCreated) return;

        var nH = neighbor.HeightsRead;

        for (int along = 0; along < size; along++)
        {
            // Get the neighbor's edge height at the shared border
            float neighborEdge;
            switch (dir)
            {
                case 0: neighborEdge = nH[along + 0 * size]; break;            // North: neighbor's y=0 row
                case 1: neighborEdge = nH[along + (size - 1) * size]; break;    // South: neighbor's y=size-1 row
                case 2: neighborEdge = nH[0 + along * size]; break;             // East:  neighbor's x=0 column
                case 3: neighborEdge = nH[(size - 1) + along * size]; break;    // West:  neighbor's x=size-1 column
                default: return;
            }

            // Blend from edge inward using smoothstep
            // depth=0 is the exact border row: hard-snap to average of both sides
            for (int depth = 0; depth < blendWidth; depth++)
            {
                int x, y;
                switch (dir)
                {
                    case 0: x = along; y = (size - 1) - depth; break; // North: y from top inward
                    case 1: x = along; y = depth; break;              // South: y from bottom inward
                    case 2: x = (size - 1) - depth; y = along; break; // East:  x from right inward
                    case 3: x = depth; y = along; break;              // West:  x from left inward
                    default: return;
                }

                int idx = x + y * size;

                if (depth == 0)
                {
                    // Hard-snap: force the exact border row to match the neighbor
                    float snapped = (neighborEdge + HeightsRead[idx]) * 0.5f;
                    HeightsRead[idx] = snapped;
                    HeightsWrite[idx] = snapped;
                }
                else
                {
                    float t = (float)depth / (blendWidth - 1);
                    t = t * t * (3f - 2f * t); // Smoothstep for natural transition

                    float blended = Mathf.Lerp(neighborEdge, HeightsRead[idx], t);
                    HeightsRead[idx] = blended;
                    HeightsWrite[idx] = blended;
                }
            }
        }
    }

    void OnDestroy()
    {
        CleanupTempBuffers();
        if (HeightsRead.IsCreated) HeightsRead.Dispose();
        if (HeightsWrite.IsCreated) HeightsWrite.Dispose();
        if (FlatnessData.IsCreated) FlatnessData.Dispose();
        if (ModifiedFlag.IsCreated) ModifiedFlag.Dispose();
        if (_mesh != null) Destroy(_mesh);
    }
}
