using UnityEngine;

/// <summary>
/// Base class attached to weapon prefabs for runtime behavior.
/// Subclass this for melee, ranged, tool, etc.
/// </summary>
public class ItemBehaviour : MonoBehaviour
{
    [HideInInspector] public ItemData Data;
    [HideInInspector] public Transform OwnerTransform;
    [HideInInspector] public Camera OwnerCamera;

    protected float _lastUseTime = -999f;

    /// <summary>
    /// Called when the item is equipped (spawned in hand).
    /// </summary>
    public virtual void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        Data = data;
        OwnerTransform = owner;
        OwnerCamera = cam;
    }

    /// <summary>
    /// Called when the item is unequipped (removed from hand).
    /// </summary>
    public virtual void OnUnequip()
    {
        // Override for cleanup
    }

    /// <summary>
    /// Called on left-click (primary action).
    /// Returns true if action was performed.
    /// </summary>
    public virtual bool Use()
    {
        if (Time.time - _lastUseTime < 1f / Data.AttackSpeed)
            return false;

        _lastUseTime = Time.time;
        return true;
    }

    /// <summary>
    /// Called on right-click (secondary action).
    /// </summary>
    public virtual bool AltUse()
    {
        return false;
    }

    /// <summary>
    /// Returns true if the item is on cooldown.
    /// </summary>
    public bool IsOnCooldown()
    {
        if (Data == null) return false;
        return Time.time - _lastUseTime < 1f / Data.AttackSpeed;
    }

    /// <summary>
    /// Returns cooldown progress 0-1 (1 = ready).
    /// </summary>
    public float GetCooldownProgress()
    {
        if (Data == null) return 1f;
        float cd = 1f / Data.AttackSpeed;
        float elapsed = Time.time - _lastUseTime;
        return Mathf.Clamp01(elapsed / cd);
    }
}
