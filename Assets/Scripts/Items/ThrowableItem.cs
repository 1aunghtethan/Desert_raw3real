using System.Collections;
using UnityEngine;

/// <summary>
/// For throwable items (like stones). Left-click throws, right-click aims.
/// Scroll wheel adjusts throw distance while aiming.
/// </summary>
public class ThrowableItem : ItemBehaviour
{
    [Tooltip("Assign the ProjectileCurveVisualizer prefab for trajectory preview.")]
    public GameObject VisualizerPrefab;

    public static bool IsAiming;

    private ProjectileCurveVisualizerSystem.ProjectileCurveVisualizer _visualizer;
    private bool _isAiming;
    private bool _throwInProgress;
    private float _throwPower = 1f;

    void Update()
    {
        if (_isAiming)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
                _throwPower = Mathf.Clamp(_throwPower + scroll * 0.1f, 0.3f, 2f);

            if (_visualizer == null && VisualizerPrefab != null)
            {
                GameObject obj = Instantiate(VisualizerPrefab);
                _visualizer = obj.GetComponent<ProjectileCurveVisualizerSystem.ProjectileCurveVisualizer>();
                if (_visualizer != null)
                {
                    _visualizer.gravity = 9.81f * 2.5f;
                    ConfigurePreviewIgnoredLayers(_visualizer);
                }
            }

            if (_visualizer != null && OwnerCamera != null)
            {
                Vector3 spawnPos = transform.position;
                Vector3 velocity = (OwnerCamera.transform.forward * 20f + Vector3.up * 5f) * _throwPower;
                _visualizer.VisualizeProjectileCurve(spawnPos, 0.6f, velocity, 0.05f, 0.01f, false, out _, out _);
            }
        }
    }

    public override bool AltUse()
    {
        _isAiming = !_isAiming;
        IsAiming = _isAiming;
        EquipmentHolder.Instance?.SetAimMode(_isAiming && EquipmentHolder.SupportsAimMode(Data), Data);
        if (!_isAiming && _visualizer != null)
            _visualizer.HideProjectileCurve();
        return true;
    }

    public override void OnUnequip()
    {
        _isAiming = false;
        IsAiming = false;
        EquipmentHolder.Instance?.SetAimMode(false, null);
        if (_visualizer != null)
            Destroy(_visualizer.gameObject);
    }

    public override bool Use()
    {
        if (_throwInProgress || IsOnCooldown()) return false;
        _isAiming = false;
        IsAiming = false;
        EquipmentHolder.Instance?.SetAimMode(false, null);
        if (_visualizer != null)
        {
            _visualizer.HideProjectileCurve();
            Destroy(_visualizer.gameObject);
            _visualizer = null;
        }

        Camera cam = OwnerCamera;
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        if (OwnerTransform == null || Data == null) return false;

        if (EquipmentHolder.Instance != null)
            EquipmentHolder.Instance.TriggerThrowAnimation();

        _throwInProgress = true;
        StartCoroutine(ReleaseAfterThrowDelay(cam, _throwPower));
        _lastUseTime = Time.time;
        return true;
    }

    private IEnumerator ReleaseAfterThrowDelay(Camera cam, float throwPower)
    {
        float delay = EquipmentHolder.Instance != null ? EquipmentHolder.Instance.GetThrowReleaseDelay() : 0f;
        float soundDelay = Mathf.Max(0f, delay - 0.15f);
        if (soundDelay > 0f)
            yield return new WaitForSeconds(soundDelay);

        if (Data != null)
            AudioManager.Instance.PlayThrowSound(Data.ItemName);

        float remainingDelay = delay - soundDelay;
        if (remainingDelay > 0f)
            yield return new WaitForSeconds(remainingDelay);

        if (cam == null) cam = Camera.main;
        if (cam == null || OwnerTransform == null || Data == null)
        {
            _throwInProgress = false;
            yield break;
        }

        transform.SetParent(null);

        Vector3 velocity = (cam.transform.forward * 20f + Vector3.up * 5f) * throwPower;

        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        if (rb == null)
        {
            _throwInProgress = false;
            yield break;
        }
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.mass = 0.5f;
        rb.linearVelocity = velocity;

        Projectile proj = gameObject.AddComponent<Projectile>();
        if (proj == null)
        {
            _throwInProgress = false;
            yield break;
        }
        proj.DestroyOnStick = false;
        proj.DestroyOnLifetime = false;
        proj.DestroyOnMaxRange = false;
        proj.GravityScale = 2.5f;
        proj.MaxBounces = 3;
        proj.Initialize(velocity.normalized, velocity.magnitude, Data.Damage, Data.Range, OwnerTransform);

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            PhysicsMaterial bouncy = new PhysicsMaterial("StoneBounce");
            bouncy.bounciness = 0.5f;
            bouncy.dynamicFriction = 0.1f;
            bouncy.staticFriction = 0.1f;
            bouncy.frictionCombine = PhysicsMaterialCombine.Minimum;
            bouncy.bounceCombine = PhysicsMaterialCombine.Maximum;
            col.material = bouncy;
        }

        LootItem loot = gameObject.AddComponent<LootItem>();
        loot.Data = Data;
        loot.PickupRadius = 2f;

        if (EquipmentHolder.Instance != null)
            EquipmentHolder.Instance.ReleaseCurrentWeapon();

        if (Inventory.Instance != null)
            Inventory.Instance.RemoveItem(Data, 1);

        _throwInProgress = false;
    }
}
