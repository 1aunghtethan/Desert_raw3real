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
    public int MaxStack = 1;

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
    [Tooltip("Dig strength multiplier when using this tool")]
    public float DigMultiplier = 1.5f;
    [Tooltip("Dig radius multiplier")]
    public float RadiusMultiplier = 1f;

    [Header("Consumable (only for Consumable type)")]
    public float HealthRestore = 0f;
    public float HungerRestore = 5f;
    public float SaturationRestore = 5f; // Stops hunger drain temporarily
    public float ThirstRestore = 0f;
}
