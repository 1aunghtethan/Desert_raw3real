using System;
using UnityEngine;

public static class WorldSeedManager
{
    public const int MinSeed = 10000;
    public const int MaxSeed = 999999;

    private static readonly System.Random SeedRandom = new System.Random(unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode()));
    private static int currentSeed;

    public static bool HasSeed => currentSeed >= MinSeed && currentSeed <= MaxSeed;
    public static int CurrentSeed => currentSeed;

    public static int GenerateNewSeed()
    {
        currentSeed = SeedRandom.Next(MinSeed, MaxSeed + 1);
        Debug.Log($"[WorldSeed] Generated new world seed: {currentSeed}");
        return currentSeed;
    }

    public static int EnsureSeed()
    {
        return EnsureSeed(true, MinSeed);
    }

    public static int EnsureSeed(bool randomizeIfMissing, int fallbackSeed)
    {
        if (HasSeed)
        {
            return currentSeed;
        }

        if (randomizeIfMissing)
        {
            return GenerateNewSeed();
        }

        currentSeed = Mathf.Clamp(fallbackSeed, MinSeed, MaxSeed);
        Debug.Log($"[WorldSeed] Using configured world seed: {currentSeed}");
        return currentSeed;
    }
}
