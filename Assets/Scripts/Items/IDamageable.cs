using UnityEngine;

/// <summary>
/// Interface for anything that can receive damage.
/// Implement on enemies, destructible objects, etc.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection);
}
