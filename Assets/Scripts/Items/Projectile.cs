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
    public float GravityScale = 1.0f;

    private float _damage;
    private float _maxRange;
    private Rigidbody _rb;
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
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized);
        }

        // Max range check
        float dist = Vector3.Distance(_startPos, transform.position);
        if (dist > _maxRange)
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

        _hasHit = true;

        // Deal damage
        IDamageable damageable = collision.collider.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            Vector3 hitPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
            damageable.TakeDamage(_damage, hitPoint, _rb.linearVelocity.normalized);
            Debug.Log($"[Projectile] Hit {collision.collider.name} for {_damage} damage!");
        }
        else
        {
            Debug.Log($"[Projectile] Stuck in {collision.collider.name}");
        }

        if (StickOnHit)
        {
            // Stick into the surface
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
            transform.SetParent(collision.transform);

            // Destroy after a while
            Destroy(gameObject, 15f);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
