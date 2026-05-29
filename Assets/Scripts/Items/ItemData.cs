using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the type of item for behavior routing.
/// </summary>
public enum ItemType
{
    Melee,
    Ranged,
    Tool,
    Consumable
}

/// <summary>
/// ScriptableObject that defines an item's properties.
/// Create via: Right-click > Create > Sand > Item Data
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Sand/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Basic Info")]
    public string ItemName = "New Item";
    public Sprite Icon;
    public ItemType Type = ItemType.Melee;
    public GameObject Prefab;
    public List<GameObject> PrefabVariants = new List<GameObject>();
    public int MaxStack = 1;

    [Header("Inventory & Placement")]
    [Tooltip("If disabled, this item cannot be added to the hotbar, backpack, or cursor.")]
    public bool CanStoreInInventory = true;
    [Tooltip("If disabled, this item cannot be placed from the hotbar or by direct crafting placement.")]
    public bool CanPlaceInWorld = true;
    [Tooltip("If enabled, crafting this item starts placement preview immediately instead of attaching it to the cursor.")]
    public bool DirectPlaceOnCraft;
    [Range(0f, 1f)]
    [Tooltip("Fraction of the placed object's visible height to sink below the terrain.")]
    public float PlacementSinkPercent;
    [Range(0f, 1f)]
    [Tooltip("Movement multiplier while this item is in direct placement preview.")]
    public float PlacementMoveMultiplier = 1f;
    public bool LockRunDuringPlacement;
    public bool LockJumpDuringPlacement;

    [Header("Combat")]
    public float Damage = 10f;
    [Tooltip("Attacks per second")]
    public float AttackSpeed = 1f;
    [Tooltip("Melee reach or projectile max range")]
    public float Range = 2f;

    [Header("Ranged (only for Ranged type)")]
    [Tooltip("Projectile prefab to spawn when firing")]
    public GameObject ProjectilePrefab;
    [Tooltip("Speed of the projectile")]
    public float ProjectileSpeed = 30f;

    [Header("World Drop")]
    [Tooltip("Prefab spawned when the item is dropped/placed in the world. If null, uses Prefab scaled down.")]
    public GameObject DropPrefab;
    [Tooltip("Should the dropped item spin and bob in the air? Disable for heavy physics objects like logs.")]
    public bool PickupsSpinAndBob = true;

    [Header("Positioning in Hand")]
    public Vector3 HoldPosition = Vector3.zero;
    public Vector3 HoldRotation = Vector3.zero;
    public float HoldScale = 1f;

    [Header("Tool Settings (only for Tool type)")]
    [Tooltip("Base sand depth removed by one dig click.")]
    public float DigPower = 0.35f;
    [Tooltip("Base sand height added by one place click.")]
    public float PlacePower = 0.35f;
    [Tooltip("Base dig brush radius.")]
    public float DigRadius = 1f;
    [Tooltip("Base place brush radius.")]
    public float PlaceRadius = 1f;
    [Tooltip("Dig strength multiplier when using this tool")]
    public float DigMultiplier = 1.5f;
    [Tooltip("Dig radius multiplier")]
    public float RadiusMultiplier = 1f;

    [Header("Consumable (only for Consumable type)")]
    public float HealthRestore = 0f;
    public float HungerRestore = 5f;
    public float SaturationRestore = 5f; // Stops hunger drain temporarily
    public float ThirstRestore = 0f;

    public GameObject GetRandomPrefab()
    {
        if (PrefabVariants != null && PrefabVariants.Count > 0)
        {
            return PrefabVariants[Random.Range(0, PrefabVariants.Count)];
        }

        return Prefab;
    }

    public GameObject GetDropPrefab()
    {
        return DropPrefab != null ? DropPrefab : GetRandomPrefab();
    }

    public GameObject GetPlacementPrefab()
    {
        return DropPrefab != null ? DropPrefab : (PrefabVariants != null && PrefabVariants.Count > 0 ? PrefabVariants[0] : Prefab);
    }
}
