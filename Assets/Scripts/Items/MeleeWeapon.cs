using UnityEngine;

/// <summary>
/// Melee weapon behavior — knife, sword, axe swing attack.
/// Attach to weapon prefab alongside the 3D model.
/// </summary>
public class MeleeWeapon : ItemBehaviour
{
    [Header("Melee Settings")]
    public float SwingAngle = 60f;
    public float SwingDuration = 0.2f;

    private bool _isSwinging = false;
    private float _swingTimer = 0f;
    private Quaternion _swingStartRot;
    private Quaternion _swingEndRot;
    private Quaternion _restRotation;
    private bool _hasHitThisSwing = false;

    public override void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        base.OnEquip(data, owner, cam);
        _restRotation = transform.localRotation;
    }

    void Update()
    {
        if (_isSwinging)
        {
            _swingTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_swingTimer / SwingDuration);

            // Swing forward then snap back
            if (t < 0.5f)
            {
                float swingT = t * 2f;
                swingT = Mathf.Sin(swingT * Mathf.PI * 0.5f); // Ease out
                transform.localRotation = Quaternion.Slerp(_swingStartRot, _swingEndRot, swingT);
            }
            else
            {
                float returnT = (t - 0.5f) * 2f;
                returnT = returnT * returnT; // Ease in
                transform.localRotation = Quaternion.Slerp(_swingEndRot, _restRotation, returnT);
            }

            // Hit detection during the forward swing
            if (t > 0.1f && t < 0.5f && !_hasHitThisSwing)
            {
                PerformHitDetection();
            }

            if (t >= 1f)
            {
                _isSwinging = false;
                transform.localRotation = _restRotation;
            }
        }
    }

    public override bool Use()
    {
        if (_isSwinging) return false;
        if (!base.Use()) return false;

        // Start swing animation
        _isSwinging = true;
        _swingTimer = 0f;
        _hasHitThisSwing = false;
        _swingStartRot = _restRotation;
        _swingEndRot = _restRotation * Quaternion.Euler(-SwingAngle, 0, 0);

        return true;
    }

    private void PerformHitDetection()
    {
        if (OwnerCamera == null) return;

        // Raycast from camera center
        Ray ray = new Ray(OwnerCamera.transform.position, OwnerCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, Data.Range);

        foreach (var hit in hits)
        {
            // Skip self
            if (hit.transform == OwnerTransform) continue;
            if (hit.transform.IsChildOf(OwnerTransform)) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            // Check for Damageable
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(Data.Damage, hit.point, ray.direction);
                _hasHitThisSwing = true;
                Debug.Log($"[Melee] Hit {hit.collider.name} for {Data.Damage} damage!");
                return;
            }

            // Visual feedback: hit the terrain or environment
            Debug.Log($"[Melee] Struck {hit.collider.name} at {hit.point}");
            _hasHitThisSwing = true;
            return;
        }
    }
}
