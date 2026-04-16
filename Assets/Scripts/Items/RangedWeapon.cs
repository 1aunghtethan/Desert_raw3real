using UnityEngine;

/// <summary>
/// Ranged weapon behavior — bow, crossbow firing.
/// Spawns a Projectile prefab when used.
/// </summary>
public class RangedWeapon : ItemBehaviour
{
    [Header("Ranged Settings")]
    public float ChargeTime = 0.5f;
    public GameObject VisualizerPrefab;
    private ProjectileCurveVisualizerSystem.ProjectileCurveVisualizer _visualizer;

    [Header("Arrow Spawn Position Offsets")]
    [Tooltip("Offset from the bow position in First Person mode")]
    public Vector3 FPSpawnOffset = Vector3.zero;
    [Tooltip("Offset from the bow position in Third Person mode")]
    public Vector3 TPSpawnOffset = Vector3.zero;

    [Header("Arrow Arc Settings")]
    [Tooltip("How much the arrow arcs upward (0 = straight, higher = more arc)")]
    public float ArcStrength = 0.15f;

    private bool _isCharging = false;
    private float _chargeTimer = 0f;
    private Quaternion _restRotation;

    public override void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        base.OnEquip(data, owner, cam);
        _restRotation = transform.localRotation;
    }

    public override void OnUnequip()
    {
        if (_visualizer != null)
        {
            Destroy(_visualizer.gameObject);
        }
        base.OnUnequip();
    }

    void Update()
    {
        // Visual draw-back while charging
        if (_isCharging)
        {
            _chargeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_chargeTimer / ChargeTime);

            // Slight pull-back animation
            float pullback = Mathf.Sin(t * Mathf.PI * 0.5f) * 15f;
            transform.localRotation = _restRotation * Quaternion.Euler(-pullback, 0, 0);

            UpdateVisualizer(t);
        }
        else if (_visualizer != null)
        {
            _visualizer.HideProjectileCurve();
        }
    }

    private void UpdateVisualizer(float chargeT)
    {
        if (VisualizerPrefab == null && _visualizer == null) return;

        if (_visualizer == null)
        {
            GameObject obj = Instantiate(VisualizerPrefab);
            _visualizer = obj.GetComponent<ProjectileCurveVisualizerSystem.ProjectileCurveVisualizer>();
            
            // Try to match gravity from projectile prefab
            if (Data != null && Data.ProjectilePrefab != null)
            {
                Projectile projScript = Data.ProjectilePrefab.GetComponent<Projectile>();
                if (projScript != null)
                {
                    _visualizer.gravity = 9.81f * projScript.GravityScale;
                }
            }
        }

        // Calculate same fire direction as in Fire()
        Camera activeCam = OwnerCamera;
        PlayerController player = OwnerTransform.GetComponent<PlayerController>();
        if (player != null) activeCam = player.GetActiveCamera();
        if (activeCam == null) return;

        Vector3 rayOrigin = activeCam.transform.position;
        Vector3 rayDirection = activeCam.transform.forward;
        float maxAimDist = Data.Range > 0 ? Data.Range : 100f;
        Vector3 targetPoint = rayOrigin + rayDirection * maxAimDist;

        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, maxAimDist);
        float closestDist = maxAimDist;
        foreach (var h in hits)
        {
            if (h.transform.root == OwnerTransform.root) continue;
            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                targetPoint = h.point;
            }
        }

        Vector3 spawnPos;
        if (player != null && player.CurrentMode == PlayerController.CameraMode.FirstPerson)
            spawnPos = transform.position + transform.TransformDirection(FPSpawnOffset);
        else
            spawnPos = transform.position + transform.TransformDirection(TPSpawnOffset);

        Vector3 toTarget = targetPoint - spawnPos;
        Vector3 fireDirection = toTarget.normalized;
        if (Vector3.Dot(fireDirection, OwnerTransform.forward) < 0) fireDirection = OwnerTransform.forward;

        float chargeMultiplier = Mathf.Lerp(0.5f, 1.5f, chargeT);
        Vector3 launchVelocity = fireDirection * (Data.ProjectileSpeed * chargeMultiplier);

        Vector3 updatedPos;
        RaycastHit hit;
        _visualizer.VisualizeProjectileCurve(spawnPos, 0f, launchVelocity, 0.05f, 0.01f, false, out updatedPos, out hit);
    }

    public override bool Use()
    {
        if (!base.Use()) return false;

        if (Data.ProjectilePrefab == null)
        {
            Debug.LogWarning($"[RangedWeapon] No projectile prefab assigned for {Data.ItemName}!");
            return false;
        }

        Fire();
        return true;
    }

    /// <summary>
    /// Hold right-click to start charging.
    /// </summary>
    public override bool AltUse()
    {
        _isCharging = true;
        _chargeTimer = 0f;
        return true;
    }

    private void Fire()
    {
        // Dynamically get the currently active camera from the player
        Camera activeCam = OwnerCamera;
        PlayerController player = OwnerTransform.GetComponent<PlayerController>();
        if (player != null)
        {
            activeCam = player.GetActiveCamera();
        }

        if (activeCam == null) return;

        // 1. Find the target point by raycasting from the center of the screen (active camera)
        Vector3 rayOrigin = activeCam.transform.position;
        Vector3 rayDirection = activeCam.transform.forward;
        float maxAimDist = Data.Range > 0 ? Data.Range : 100f;
        Vector3 targetPoint = default;

        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, maxAimDist);
        float closestDist = maxAimDist;
        bool foundValidHit = false;

        foreach (var h in hits)
        {
            // Ignore the user shooting the bow (e.g. hitting their own back in Third Person)
            if (h.transform.root == OwnerTransform.root) continue;

            if (h.distance < closestDist)
            {
                closestDist = h.distance;
                targetPoint = h.point;
                foundValidHit = true;
            }
        }

        if (!foundValidHit)
        {
            targetPoint = rayOrigin + rayDirection * maxAimDist;
        }

        // 2. Determine spawn position (mode-aware, both use bow position + offset)
        Vector3 spawnPos;

        if (player != null && player.CurrentMode == PlayerController.CameraMode.FirstPerson)
        {
            // First Person: bow position + FP offset
            spawnPos = transform.position + transform.TransformDirection(FPSpawnOffset);
        }
        else
        {
            // Third Person: bow position + TP offset
            spawnPos = transform.position + transform.TransformDirection(TPSpawnOffset);
        }

        // 3. Calculate arc trajectory — arrow launches upward, gravity pulls it into a tangential arc
        Vector3 toTarget = targetPoint - spawnPos;
        float distToTarget = toTarget.magnitude;
        Vector3 fireDirection = toTarget.normalized;
        
        // Safety check if target is too close or behind
        if (Vector3.Dot(fireDirection, OwnerTransform.forward) < 0)
        {
            fireDirection = OwnerTransform.forward;
        }

        // Arrow now shoots straight at the target (no arc applied)

        Quaternion spawnRot = Quaternion.LookRotation(fireDirection);

        GameObject projObj = Instantiate(Data.ProjectilePrefab, spawnPos, spawnRot);
        Projectile proj = projObj.GetComponent<Projectile>();

        if (proj == null)
            proj = projObj.AddComponent<Projectile>();

        // Calculate charge multiplier
        float chargeMultiplier = 1f;
        if (_isCharging)
        {
            chargeMultiplier = Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(_chargeTimer / ChargeTime));
            _isCharging = false;
            transform.localRotation = _restRotation;
            if (_visualizer != null) _visualizer.HideProjectileCurve();
        }

        proj.Initialize(
            fireDirection,
            Data.ProjectileSpeed * chargeMultiplier,
            Data.Damage * chargeMultiplier,
            Data.Range,
            OwnerTransform
        );

        Debug.Log($"[Ranged] Fired {Data.ItemName} towards screen center (charge: {chargeMultiplier:F1}x)");
    }
}
