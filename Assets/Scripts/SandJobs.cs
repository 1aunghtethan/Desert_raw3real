using UnityEngine;
using Unity.Jobs;
using Unity.Collections;


public static class SandJobs
{
    // Ported from SandMesh.cs "RunSimulation"
    // Runs as a single IJob per chunk to allow sequential, dependent cell updates (Gauss-Seidel style)
    // allowing "instant" sand flow behavior.
    public struct SimulationJob : IJob
    {
        public int Size; 
        public float FlowThreshold;
        public float FlowSpeed;

        [ReadOnly] public NativeArray<float> ReadHeights;
        public NativeArray<float> WriteHeights;
        
        // Neighbor reference data for cross-chunk flow (Cardinal)
        [ReadOnly] public NativeArray<float> ReadN, ReadS, ReadE, ReadW;
        public bool HasN, HasS, HasE, HasW;

        // Neighbor reference data for cross-chunk flow (Diagonal)
        [ReadOnly] public NativeArray<float> ReadNE, ReadNW, ReadSE, ReadSW;
        public bool HasNE, HasNW, HasSE, HasSW;

        public NativeArray<int> ModifiedFlag; 

        public void Execute()
        {
            NativeArray<float>.Copy(ReadHeights, WriteHeights, ReadHeights.Length);

            int w = Size;
            int h = Size;

            int[] nDX = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] nDY = { -1, -1, -1, 0, 0, 1, 1, 1 };

            bool anyModified = false;

            for (int i = 0; i < w * h; i++)
            {
                int x = i % w;
                int y = i / w;

                float hSelf = ReadHeights[i];
                float delta = 0;

                for (int n = 0; n < 8; n++)
                {
                    int nx = x + nDX[n];
                    int ny = y + nDY[n];

                    float hNeighbor;
                    bool isInternal = (nx >= 0 && nx < w && ny >= 0 && ny < h);

                    if (isInternal)
                    {
                        hNeighbor = ReadHeights[nx + ny * w];
                    }
                    else
                    {
                        // Ghost Cell Lookup (Bit-identical across chunks)
                        if (nx < 0 && ny >= h) { if (HasNW) hNeighbor = ReadNW[(w-1) + 0*w]; else continue; }
                        else if (nx >= w && ny >= h) { if (HasNE) hNeighbor = ReadNE[0 + 0*w]; else continue; }
                        else if (nx < 0 && ny < 0) { if (HasSW) hNeighbor = ReadSW[(w-1) + (h-1)*w]; else continue; }
                        else if (nx >= w && ny < 0) { if (HasSE) hNeighbor = ReadSE[0 + (h-1)*w]; else continue; }
                        else if (nx < 0 && HasW && ny >= 0 && ny < h) hNeighbor = ReadW[(w - 1) + ny * w];
                        else if (nx >= w && HasE && ny >= 0 && ny < h) hNeighbor = ReadE[0 + ny * w];
                        else if (ny < 0 && HasS && nx >= 0 && nx < w) hNeighbor = ReadS[x + (h - 1) * w];
                        else if (ny >= h && HasN && nx >= 0 && nx < w) hNeighbor = ReadN[x + 0 * w];
                        else continue; 
                    }

                    float diff = hSelf - hNeighbor;

                    // Flow Out
                    if (diff > FlowThreshold)
                    {
                        float flow = (diff - FlowThreshold) * FlowSpeed;
                        float maxFlow = diff * 0.45f; // Safety clamp to prevent oscillation
                        if (flow > maxFlow) flow = maxFlow;
                        delta -= flow;
                    }
                    // Flow In (only from neighbors, internal flow is handled by the neighbor's Flow Out)
                    else if (!isInternal && diff < -FlowThreshold)
                    {
                        float pullDiff = -diff; 
                        float flow = (pullDiff - FlowThreshold) * FlowSpeed;
                        float maxFlow = pullDiff * 0.45f;
                        if (flow > maxFlow) flow = maxFlow;
                        delta += flow;
                    }
                    else if (isInternal)
                    {
                        // Internal Flow In: Neighbor 'n' will flow into 'i' when the loop reaches 'n'
                        // So we look at 'n' as 'hSelf' and 'i' as 'hNeighbor' for that step.
                        float reverseDiff = hNeighbor - hSelf;
                        if (reverseDiff > FlowThreshold)
                        {
                            float flow = (reverseDiff - FlowThreshold) * FlowSpeed;
                            float maxFlow = reverseDiff * 0.45f;
                            if (flow > maxFlow) flow = maxFlow;
                            delta += flow;
                        }
                    }
                }

                if (Mathf.Abs(delta) > 0.001f)
                {
                    WriteHeights[i] = hSelf + delta;
                    anyModified = true;
                }
                else
                {
                    WriteHeights[i] = hSelf;
                }
            }
            
            if (anyModified) ModifiedFlag[0] = 1;
        }
    }

    // Ported from SandMesh.cs "UpdateVisuals" / "GenerateMeshTopology"
    public struct MeshJob : IJob
    {
        public int Size;
        public float CellSize; // Same as m_fScale
        public float BottomDepth; 
        public Vector3 Origin; // Chunk World Position (Read-only context)

        [ReadOnly] public NativeArray<float> Heights;
        
        // Neighbor Heights for seamless normals
        [ReadOnly] public NativeArray<float> HeightsN, HeightsS, HeightsE, HeightsW;
        [ReadOnly] public NativeArray<float> ReadNE, ReadNW, ReadSE, ReadSW;
        public bool HasN, HasS, HasE, HasW;
        public bool HasNE, HasNW, HasSE, HasSW;

        // Flatness data for texture blending (0 = desert, 1 = near mountain)
        [ReadOnly] public NativeArray<float> FlatnessData;
        public bool HasFlatnessData;

        // Outputs
        public NativeArray<Vector3> Verts;
        public NativeArray<Vector2> UVs;
        public NativeArray<Vector3> Normals;
        public NativeArray<Color> Colors;

        public void Execute()
        {
            int w = Size;
            int h = Size;
            
            float minSkirtY = Origin.y - BottomDepth; 
            
            // UV tiling factor — world-space UVs for seamless cross-chunk texturing
            // E.g. setting this to 0.25f means the texture repeats every 4 world units (meters).
            float uvScale = 2.0f;
            
            // 1. Surface with Persistent Stitching
            float overlap = 0.1f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    float hSelf = Heights[i];

                    // --- STITCHING LOGIC ---
                    // Average shared edges/corners with 8 neighbors.
                    // This creates a shared "ground truth" height for the seam.
                    int count = 1;
                    float sum = hSelf;

                    // Cardinal Edges
                    if (x == 0 && HasW) { sum += HeightsW[(w - 1) + y * w]; count++; }
                    if (x == w - 1 && HasE) { sum += HeightsE[0 + y * w]; count++; }
                    if (y == 0 && HasS) { sum += HeightsS[x + (h - 1) * w]; count++; }
                    if (y == h - 1) { 
                        if (HasN) { sum += HeightsN[x + 0 * w]; count++; }
                    }

                    // Corners (4-way intersections)
                    if (x == 0 && y == 0 && HasSW) { sum += ReadSW[(w - 1) + (h - 1) * w]; count++; }
                    if (x == w - 1 && y == 0 && HasSE) { sum += ReadSE[0 + (h - 1) * w]; count++; }
                    if (x == 0 && y == h - 1 && HasNW) { sum += ReadNW[(w - 1) + 0 * w]; count++; }
                    if (x == w - 1 && y == h - 1 && HasNE) { sum += ReadNE[0 + 0 * w]; count++; }

                    hSelf = sum / count;
                    float localY = hSelf - Origin.y;

                    // SURFACE VERTICES: No overlap coordinate shift (perfect grid alignment)
                    Verts[i] = new Vector3(x * CellSize, localY, y * CellSize);
                    
                    // World-space UVs: continuous across chunk boundaries (no texture seam)
                    float worldX = Origin.x + x * CellSize;
                    float worldZ = Origin.z + y * CellSize;
                    UVs[i] = new Vector2(worldX * uvScale, worldZ * uvScale);

                    // Vertex color: alpha = flatness (drives apron sand blend)
                    float flatness = (HasFlatnessData && i < FlatnessData.Length) ? FlatnessData[i] : 0f;
                    Colors[i] = new Color(1f, 1f, 1f, flatness);

                    // Normals (Seamless Central Difference)
                    // Sample neighbors at [x-1, x+1, y-1, y+1] across chunk boundaries
                    float hL, hR, hD, hU;
                    
                    // X-Axis (West/East)
                    if (x > 1) hL = Heights[i - 1]; 
                    else if (x == 1) {
                        // Stitch the boundary neighbor for hL consistency
                        float rawL = Heights[i - 1];
                        float nL = (HasW) ? HeightsW[(w - 1) + y * w] : rawL;
                        hL = (rawL + nL) * 0.5f;
                    }
                    else if (HasW) hL = HeightsW[(w - 2) + y * w]; // Sample "deep" into West neighbor
                    else hL = hSelf;

                    if (x < w - 2) hR = Heights[i + 1];
                    else if (x == w - 2) {
                        // Stitch the boundary neighbor for hR consistency
                        float rawR = Heights[i + 1];
                        float nR = (HasE) ? HeightsE[0 + y * w] : rawR;
                        hR = (rawR + nR) * 0.5f;
                    }
                    else if (HasE) hR = HeightsE[1 + y * w]; // Sample "deep" into East neighbor
                    else hR = hSelf;

                    // Y-Axis (South/North)
                    if (y > 1) hD = Heights[i - w];
                    else if (y == 1) {
                        // Stitch the boundary neighbor for hD consistency
                        float rawD = Heights[i - w];
                        float nD = (HasS) ? HeightsS[x + (h - 1) * w] : rawD;
                        hD = (rawD + nD) * 0.5f;
                    }
                    else if (HasS) hD = HeightsS[x + (h - 1) * w - w]; // Sample "deep" into South
                    else hD = hSelf;

                    if (y < h - 2) hU = Heights[i + w];
                    else if (y == h - 2) {
                        // Stitch the boundary neighbor for hU consistency
                        float rawU = Heights[i + w];
                        float nU = (HasN) ? HeightsN[x + 0 * w] : rawU;
                        hU = (rawU + nU) * 0.5f;
                    }
                    else if (HasN) hU = HeightsN[x + 1 * w]; // Sample "deep" into North
                    else hU = hSelf;

                    float dx = hR - hL;
                    float dy = hU - hD; 
                    Normals[i] = new Vector3(-dx, 2.0f * CellSize, -dy).normalized;
                }
            }

            // 2. Skirts (Walls) - Use stitched surface heights to match surface edge exactly
            int vIdx = w * h;

            // Top (Along X, at Z = max)
            for (int x = 0; x < w; x++) {
                float vx = x * CellSize;
                if (x == 0) vx -= overlap; if (x == w - 1) vx += overlap;
                float stitchedY = Verts[x + (h-1)*w].y; // Use stitched surface height
                Verts[vIdx + x] = new Vector3(vx, stitchedY, (h-1)*CellSize + overlap);
                Verts[vIdx + w + x] = new Vector3(vx, -BottomDepth, (h-1)*CellSize + overlap);
                Normals[vIdx + x] = Vector3.forward; Normals[vIdx + w + x] = Vector3.forward;
                UVs[vIdx + x] = Vector2.zero; UVs[vIdx+w+x] = Vector2.zero;
                Colors[vIdx + x] = new Color(1f,1f,1f,0f); Colors[vIdx+w+x] = new Color(1f,1f,1f,0f);
            }
            vIdx += 2 * w;

            // Bottom (Along X, at Z = 0)
            for (int x = 0; x < w; x++) {
                float vx = x * CellSize;
                if (x == 0) vx -= overlap; if (x == w - 1) vx += overlap;
                float stitchedY = Verts[x].y; // Use stitched surface height
                Verts[vIdx + x] = new Vector3(vx, stitchedY, -overlap);
                Verts[vIdx + w + x] = new Vector3(vx, -BottomDepth, -overlap);
                Normals[vIdx + x] = Vector3.back; Normals[vIdx + w + x] = Vector3.back;
                UVs[vIdx + x] = Vector2.zero; UVs[vIdx+w+x] = Vector2.zero;
                Colors[vIdx + x] = new Color(1f,1f,1f,0f); Colors[vIdx+w+x] = new Color(1f,1f,1f,0f);
            }
            vIdx += 2 * w;

            // Right (Along Z, at X = max)
            for (int y = 0; y < h; y++) {
                float vz = y * CellSize;
                if (y == 0) vz -= overlap; if (y == h - 1) vz += overlap;
                float stitchedY = Verts[(w-1) + y*w].y; // Use stitched surface height
                Verts[vIdx + y] = new Vector3((w-1)*CellSize + overlap, stitchedY, vz);
                Verts[vIdx+h+y] = new Vector3((w-1)*CellSize + overlap, -BottomDepth, vz);
                Normals[vIdx + y] = Vector3.right; Normals[vIdx+h+y] = Vector3.right;
                UVs[vIdx + y] = Vector2.zero; UVs[vIdx+h+y] = Vector2.zero;
                Colors[vIdx + y] = new Color(1f,1f,1f,0f); Colors[vIdx+h+y] = new Color(1f,1f,1f,0f);
            }
            vIdx += 2 * h;

            // Left (Along Z, at X = 0)
            for (int y = 0; y < h; y++) {
                float vz = y * CellSize;
                if (y == 0) vz -= overlap; if (y == h - 1) vz += overlap;
                float stitchedY = Verts[y*w].y; // Use stitched surface height
                Verts[vIdx + y] = new Vector3(-overlap, stitchedY, vz);
                Verts[vIdx+h+y] = new Vector3(-overlap, -BottomDepth, vz);
                Normals[vIdx + y] = Vector3.left; Normals[vIdx+h+y] = Vector3.left;
                UVs[vIdx + y] = Vector2.zero; UVs[vIdx+h+y] = Vector2.zero;
                Colors[vIdx + y] = new Color(1f,1f,1f,0f); Colors[vIdx+h+y] = new Color(1f,1f,1f,0f);
            }
            vIdx += 2 * h;

            // 3. Bottom Face — Expanded Quad
            float bottomY = -BottomDepth;
            float maxSide = (w - 1) * CellSize;
            Verts[vIdx + 0] = new Vector3(-overlap,           bottomY, -overlap);
            Verts[vIdx + 1] = new Vector3(maxSide + overlap, bottomY, -overlap);
            Verts[vIdx + 2] = new Vector3(-overlap,           bottomY, maxSide + overlap);
            Verts[vIdx + 3] = new Vector3(maxSide + overlap, bottomY, maxSide + overlap);
            
            Normals[vIdx + 0] = Vector3.up; Normals[vIdx + 1] = Vector3.up; 
            Normals[vIdx + 2] = Vector3.up; Normals[vIdx + 3] = Vector3.up; 
            UVs[vIdx+0] = Vector2.zero; UVs[vIdx+1] = Vector2.zero;
            UVs[vIdx+2] = Vector2.zero; UVs[vIdx+3] = Vector2.zero;
            Colors[vIdx+0] = new Color(1f,1f,1f,0f); Colors[vIdx+1] = new Color(1f,1f,1f,0f);
            Colors[vIdx+2] = new Color(1f,1f,1f,0f); Colors[vIdx+3] = new Color(1f,1f,1f,0f);
        }
    }
}
