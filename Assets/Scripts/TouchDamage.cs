using UnityEngine;

/// <summary>
/// Attach to objects that should harm the player upon physical contact (e.g. Cacti, Thorns, Fire).
/// </summary>
public class TouchDamage : MonoBehaviour
{
    [Header("Damage Settings")]
    public float DamageAmount = 5f;
    [Tooltip("How often in seconds the player takes damage while touching this.")]
    public float DamageInterval = 1.0f;

    private float _lastDamageTime = -100f;

    private void OnCollisionStay(Collision collision)
    {
        TryDamage(collision.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDamage(other.gameObject);
    }

    private void TryDamage(GameObject target)
    {
        // Don't damage too frequently
        if (Time.time < _lastDamageTime + DamageInterval) return;

        // Check if it's the player
        if (target.CompareTag("Player"))
        {
            IDamageable damageable = target.GetComponent<IDamageable>();
            if (damageable != null)
            {
                // We'll pass the center of the trap object as the hit point source
                damageable.TakeDamage(DamageAmount, transform.position, (target.transform.position - transform.position).normalized);
                _lastDamageTime = Time.time;
            }
        }
    }
}