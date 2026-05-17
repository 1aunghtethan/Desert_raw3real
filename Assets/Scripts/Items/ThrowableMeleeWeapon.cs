using System.Collections;
using UnityEngine;

/// <summary>
/// Melee weapon that can also be thrown. Left-click swings normally.
/// Right-click toggles aim mode, then left-click throws the held weapon.
/// </summary>
public class ThrowableMeleeWeapon : MeleeWeapon
{
    [Tooltip("Assign the ProjectileCurveVisualizer prefab for trajectory preview.")]
    public GameObject VisualizerPrefab;
    [Tooltip("Only used by Stone Spear. Adjust this to tune the spear's visible rotation while flying.")]
    public Vector3 StoneSpearAirRotationOffset = new Vector3(46.146f, 147.333f, 44.851f);

    private ProjectileCurveVisualizerSystem.ProjectileCurveVisualizer _visualizer;
    private bool _isAiming;
    private bool _throwInProgress;
    private float _throwPower = 1f;

    protected override void Update()
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
                    _visualizer.gravity = 9.81f * 2.5f;
            }

            Camera cam = GetCurrentCamera();
            if (_visualizer != null && cam != null)
            {
                Vector3 spawnPos = cam.transform.position + cam.transform.forward * 3f;
                Vector3 velocity = (cam.transform.forward * 20f + Vector3.up * 5f) * _throwPower;
                _visualizer.VisualizeProjectileCurve(spawnPos, 0f, velocity, 0.05f, 0.01f, false, out _, out _);
            }
        }
        else
        {
            base.Update(); // normal swing animation
        }
    }

    public override bool AltUse()
    {
        _isAiming = !_isAiming;
        ThrowableItem.IsAiming = _isAiming;
        if (!_isAiming && _visualizer != null)
            _visualizer.HideProjectileCurve();
        return true;
    }

    public override void OnUnequip()
    {
        _isAiming = false;
        ThrowableItem.IsAiming = false;
        if (_visualizer != null)
            Destroy(_visualizer.gameObject);
    }

    public override bool Use()
    {
        if (_isAiming)
            return ThrowWeapon();
        return base.Use(); // normal melee swing
    }

    private bool ThrowWeapon()
    {
        if (_throwInProgress) return false;

        _isAiming = false;
        ThrowableItem.IsAiming = false;
        if (_visualizer != null)
        {
            _visualizer.HideProjectileCurve();
            Destroy(_visualizer.gameObject);
            _visualizer = null;
        }

        Camera cam = GetCurrentCamera();
        if (cam == null)
        {
            Debug.LogError("[ThrowableMelee] Cannot throw — no camera available!");
            return false;
        }
        if (OwnerTransform == null)
        {
            Debug.LogError("[ThrowableMelee] Cannot throw — OwnerTransform is null!");
            return false;
        }
        if (Data == null)
        {
            Debug.LogError("[ThrowableMelee] Cannot throw — Data is null!");
            return false;
        }

        Debug.Log($"[ThrowableMelee] Throwing {Data.ItemName} with power {_throwPower}");
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

        if (cam == null) cam = GetCurrentCamera();
        if (cam == null || OwnerTransform == null || Data == null)
        {
            _throwInProgress = false;
            yield break;
        }

        transform.SetParent(null);

        // Re-enable colliders that were disabled by EquipmentHolder at equip time
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = true;

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
        if (Data.ItemName == "Stone Spear")
        {
            proj.AirRotationOffset = StoneSpearAirRotationOffset;
            proj.StickToDamageableThenDrop = true;
            proj.DamageableDropDelay = 1f;
        }
        proj.GravityScale = 2.5f;
        proj.MaxBounces = Data.ItemName == "Stone Spear" ? 0 : 3;
        proj.Initialize(velocity.normalized, velocity.magnitude, Data.Damage, Data.Range, OwnerTransform);

        Collider col = GetComponent<Collider>();
        if (col != null && Data.ItemName != "Stone Spear")
        {
            PhysicsMaterial bouncy = new PhysicsMaterial("KnifeBounce");
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
