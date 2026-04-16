using UnityEngine;


public static class SandUtils
{
    // Fast pseudo-random noise for sand variation
    public static float GetHeight(float x, float z, int seed)
    {
        // 1. Base Dune Shape (Sharper Ridges)
        // Matches SandMesh.cs logic for visual consistency
        float nBase = Mathf.PerlinNoise(x * 0.005f + seed, z * 0.005f + seed);
        float ridged = 1.0f - Mathf.Abs(2.0f * nBase - 1.0f);
        float fHeight = ridged * ridged * 25.0f; // Squared for sharper peaks
        
        // 2. Secondary Detail (Wind Ripples)
        float ripples = Mathf.PerlinNoise(x * 0.2f + seed, z * 0.2f + seed);
        fHeight += ripples * 0.5f;

        return fHeight;
    }

    // Helper to calculate flat index
    public static int GetIndex(int x, int y, int width)
    {
        return x + y * width;
    }
}
