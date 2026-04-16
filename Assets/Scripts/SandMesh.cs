using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandMesh : MonoBehaviour
{
    // Static reference for height queries (set by the first/main tile)
    public static SandMesh MainInstance { get; private set; }
    private static List<SandMesh> s_allTiles = new List<SandMesh>();

    [Header("Chunk Settings")]
    public TerrainConfig Config;
    public Vector2Int ChunkCoord;

    // Simulation Data (Heights)
    private float[] m_simHeights;
    private float[] m_simHeightsBuffer;
    
    // Mesh Data
    private Mesh m_mesh;
    private Vector3[] m_visualVerts;
    private Vector3[] m_visualNormals;
    private Vector2[] m_visualUVs;
    private int[] m_visualTris;

    private int m_iMeshW;
    private int m_iMeshH;
    
    private MeshCollider m_meshCollider;

    void Awake()
    {
        if (MainInstance == null) MainInstance = this;
        s_allTiles.Add(this);
    }

    void OnDestroy()
    {
        s_allTiles.Remove(this);
    }

    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        if (Config == null) { Debug.LogError("TerrainConfig missing!"); return; }

        m_iMeshW = Config.ChunkSize;
        m_iMeshH = Config.ChunkSize;

        m_simHeights = new float[m_iMeshW * m_iMeshH];
        m_simHeightsBuffer = new float[m_iMeshW * m_iMeshH];

        // Seed with noise
        float wxBase = ChunkCoord.x * (Config.ChunkSize - 1) * Config.CellSize;
        float wzBase = ChunkCoord.y * (Config.ChunkSize - 1) * Config.CellSize;

        for (int y = 0; y < m_iMeshH; y++)
        {
            for (int x = 0; x < m_iMeshW; x++)
            {
                float wx = wxBase + x * Config.CellSize;
                float wz = wzBase + y * Config.CellSize;
                m_simHeights[x + y * m_iMeshW] = Mathf.PerlinNoise(wx * Config.NoiseScale, wz * Config.NoiseScale) * Config.HeightMultiplier;
            }
        }

        m_mesh = new Mesh();
        m_mesh.name = "Sand Volume Mesh";
        GetComponent<MeshFilter>().mesh = m_mesh;
        m_meshCollider = GetComponent<MeshCollider>();

        AllocateMeshArrays();
        GenerateStaticTriangles();
        UpdateMesh();
    }

    void AllocateMeshArrays()
    {
        int numSurfaceVerts = m_iMeshW * m_iMeshH;
        int perimeter = 2 * m_iMeshW + 2 * (m_iMeshH - 2);
        int numSkirtVerts = perimeter * 2;
        int numBottomVerts = m_iMeshW * m_iMeshH;

        int totalVerts = numSurfaceVerts + numSkirtVerts + numBottomVerts;
        m_visualVerts = new Vector3[totalVerts];
        m_visualNormals = new Vector3[totalVerts];
        m_visualUVs = new Vector2[totalVerts];
    }

    void GenerateStaticTriangles()
    {
        List<int> tris = new List<int>();

        // 1. Surface
        for (int y = 0; y < m_iMeshH - 1; y++)
        {
            for (int x = 0; x < m_iMeshW - 1; x++)
            {
                int root = x + y * m_iMeshW;
                tris.Add(root); tris.Add(root + m_iMeshW); tris.Add(root + 1);
                tris.Add(root + 1); tris.Add(root + m_iMeshW); tris.Add(root + m_iMeshW + 1);
            }
        }

        // 2. Skirts
        int vBase = m_iMeshW * m_iMeshH;
        // Skirt triangles logic (replicated from SandChunk)
        // ... omitted for brevity in legacy script but should match SandChunk for correctness
        
        // 3. Bottom Face (Double Sided)
        int bottomBase = vBase + (2 * m_iMeshW + 2 * (m_iMeshH - 2)) * 2;
        for (int y = 0; y < m_iMeshH - 1; y++)
        {
            for (int x = 0; x < m_iMeshW - 1; x++)
            {
                int root = bottomBase + x + y * m_iMeshW;
                // Upward face
                tris.Add(root); tris.Add(root + m_iMeshW); tris.Add(root + 1);
                tris.Add(root + 1); tris.Add(root + m_iMeshW); tris.Add(root + m_iMeshW + 1);
                // Downward face
                tris.Add(root); tris.Add(root + 1); tris.Add(root + m_iMeshW);
                tris.Add(root + 1); tris.Add(root + m_iMeshW + 1); tris.Add(root + m_iMeshW);
            }
        }

        m_visualTris = tris.ToArray();
        m_mesh.triangles = m_visualTris;
    }

    public void UpdateMesh()
    {
        // Simple update logic
        for (int y = 0; y < m_iMeshH; y++)
        {
            for (int x = 0; x < m_iMeshW; x++)
            {
                int i = x + y * m_iMeshW;
                m_visualVerts[i] = new Vector3(x * Config.CellSize, m_simHeights[i], y * Config.CellSize);
                m_visualNormals[i] = Vector3.up; // Placeholder
                m_visualUVs[i] = new Vector2((float)x / m_iMeshW, (float)y / m_iMeshH);
            }
        }

        // Skirts and Bottom update...
        
        m_mesh.vertices = m_visualVerts;
        m_mesh.normals = m_visualNormals;
        m_mesh.uv = m_visualUVs;
        m_mesh.RecalculateBounds();
        if (m_meshCollider) m_meshCollider.sharedMesh = m_mesh;
    }
}
