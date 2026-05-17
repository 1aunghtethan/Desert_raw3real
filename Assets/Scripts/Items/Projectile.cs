using System.Collections;
using UnityEngine;

/// <summary>
/// Projectile that flies forward with gravity and deals damage on hit.
/// Used by RangedWeapon for arrows/bolts.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Settings")]
    public float Lifetime = 10f;
    public bool StickOnHit = true;
    [Tooltip("If false, the projectile stays permanently when it sticks (stone throw).")]
    public bool DestroyOnStick = true;
    [Tooltip("If false, the projectile is not destroyed after Lifetime expires.")]
    public bool DestroyOnLifetime = true;
    [Tooltip("If false, the projectile is not destroyed when it flies beyond max range.")]
    public bool DestroyOnMaxRange = true;
    public float GravityScale = 1.0f;
    [Tooltip("Number of bounces on terrain before sticking. 0 = stick on first hit (arrows).")]
    public int MaxBounces = 0;
    [Tooltip("Visual rotation offset applied after the projectile faces its velocity.")]
    public Vector3 AirRotationOffset;
    [Tooltip("For persistent thrown weapons: briefly hold on damageable hit, then fall as pickup.")]
    public bool StickToDamageableThenDrop;
    public float DamageableDropDelay = 1f;

    private float _damage;
    private float _maxRange;
    private Rigidbody _rb;
    private int _bouncesLeft;
    private Vector3 _startPos;
    private Transform _owner;
    private bool _hasHit = false;
    private Collider _collider;
    private bool _isInitialized = false;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public void Initialize(Vector3 direction, float speed, float damage, float maxRange, Transform owner)
    {
        _isInitialized = true;
        _damage = damage;
        _maxRange = maxRange;
        _owner = owner;
        _startPos = transform.position;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();

        _rb.useGravity = false; // Custom gravity
        _rb.isKinematic = false; // Must be false to set velocity
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.linearVelocity = direction.normalized * speed;
        _rb.mass = 0.1f;

        _bouncesLeft = MaxBounces;
        _hasHit = false;

        _collider = GetComponent<Collider>();
        if (_collider == null)
        {
            // Add a small capsule collider for arrow shape
            CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
            cap.radius = 0.05f;
            cap.height = 0.5f;
            cap.direction = 2; // Z axis
            _collider = cap;
        }

        // Force all mesh colliders on the projectile to be convex since it's a dynamic rigidbody
        foreach (var meshCol in GetComponentsInChildren<MeshCollider>())
        {
            meshCol.convex = true;
        }

        // Ignore collision with owner
        Collider[] ownerColliders = owner.GetComponentsInChildren<Collider>();
        foreach (var oc in ownerColliders)
        {
            Physics.IgnoreCollision(_collider, oc, true);
        }

        if (DestroyOnLifetime)
            Destroy(gameObject, Lifetime);
    }

    void FixedUpdate()
    {
        if (_hasHit || !_isInitialized || _rb == null) return;

        // Apply custom gravity
        _rb.linearVelocity += Vector3.down * (9.81f * GravityScale * Time.fixedDeltaTime);

        // Orient arrow in flight direction
        if (_rb.linearVelocity.sqrMagnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized) * Quaternion.Euler(AirRotationOffset);
        }

        // Max range check
        float dist = Vector3.Distance(_startPos, transform.position);
        if (DestroyOnMaxRange && dist > _maxRange)
        {
            Destroy(gameObject);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_hasHit) return;

        // Ignore owner
        if (_owner != null && (collision.transform == _owner || collision.transform.IsChildOf(_owner)))
            return;

        // Deal damage to enemies — stone bounces off and keeps flying
        IDamageable damageable = collision.collider.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            Vector3 hitPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
            damageable.TakeDamage(_damage, hitPoint, _rb.linearVelocity.normalized);
            Debug.Log($"[Projectile] Hit {collision.collider.name} for {_damage} damage!");

            if (StickToDamageableThenDrop)
            {
                _hasHit = true;
                StartCoroutine(DropAfterDamageableHit());
                return;
            }
        }

        // Terrain / non-damageable hit — bounce or stick
        _bouncesLeft--;
        if (_bouncesLeft > 0)
        {
            // Bounce: keep some velocity, don't stick yet
            Vector3 vel = _rb.linearVelocity;
            float speed = vel.magnitude * 0.7f;
            Vector3 dir = Vector3.Reflect(vel.normalized, collision.contacts[0].normal);
            _rb.linearVelocity = dir * speed;
            Debug.Log($"[Projectile] Bounced off {collision.collider.name} ({_bouncesLeft} bounces left)");
        }
        else
        {
            // No bounces left — stick permanently
            Debug.Log($"[Projectile] Stuck in {collision.collider.name}");
            _hasHit = true;
            StickToSurface(collision);
        }
    }

    private void StickToSurface(Collision collision)
    {
        _rb.linearVelocity = Vector3.zero;
        _rb.isKinematic = true;

        // Temporary projectiles can attach to the hit object, but persistent pickup
        // projectiles must stay in world space. Terrain/chunk objects may unload when
        // the player walks away, which would otherwise hide or destroy the pickup too.
        if (DestroyOnStick)
        {
            transform.SetParent(collision.transform);
        }
        else
        {
            transform.SetParent(null, true);
        }

        if (DestroyOnStick)
            Destroy(gameObject, 15f);
    }

    private IEnumerator DropAfterDamageableHit()
    {
        transform.SetParent(null, true);
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _rb.useGravity = false;

        float delay = Mathf.Max(0f, DamageableDropDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }
}
