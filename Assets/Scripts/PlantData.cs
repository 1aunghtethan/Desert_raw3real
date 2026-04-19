using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that defines a plant's properties (health, loot, etc).
/// Create via: Right-click > Create > Survival > Plant Data
/// </summary>
[CreateAssetMenu(fileName = "NewPlantData", menuName = "Survival/Plant Data")]
public class PlantData : ScriptableObject
{
    [Header("Basic Info")]
    public string PlantName = "Plant";
    public float MaxHealth = 100f;

    [Header("Loot Settings")]
    [Tooltip("List of possible prefabs to drop when destroyed.")]
    public List<GameObject> LootPrefabs = new List<GameObject>();
    
    [Tooltip("Number of items to spawn when the plant is destroyed.")]
    public int LootCount = 10;

    [Tooltip("If true, the plant will majestically topple 90 degrees over 3 seconds when cut. Set to false for small logs/items.")]
    public bool TopplesOnDeath = true;

    [Tooltip("How far from the center loot appears. Set to near 0 for a tight 'middle point' cluster.")]
    public float LootSpawnRadius = 0.2f;

    [Tooltip("Initial height above ground for loot drop. Recommended 0.5 for bushes/grass, 1.5 for trees.")]
    public float LootSpawnHeight = 0.5f;

    [Header("Visual Feedback")]
    public GameObject HitParticles;
}
