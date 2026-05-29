using System.Collections;
using UnityEngine;

/// <summary>
/// Held behavior for the wood shovel: left-click digs sand, right-click places sand.
/// </summary>
public class WoodShovelItem : ItemBehaviour
{
    public float SandAmountPerClick = 0.35f;
    public float BrushRadius = 1f;
    public float DigEffectDelay = 2.1f;
    public float DigAnimationDuration = 2.8f;
    public float PlaceEffectDelay = 0.4f;

    private Transform _sandVisual;
    private Coroutine _actionRoutine;
    private bool _isBusy;
    private bool _hasLoadedSand;
    private bool _holdDigPose;

    public override void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        base.OnEquip(data, owner, cam);
        _sandVisual = FindSandVisual(transform);
        SetSandVisualActive(false);
        EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(false);
        ClearShovelLockAndAnimation();
    }

    public override void OnUnequip()
    {
        CancelActionRoutine();
        _hasLoadedSand = false;
        _isBusy = false;
        _holdDigPose = false;
        SetSandVisualActive(false);
        EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(false);
        ClearShovelLockAndAnimation();
        base.OnUnequip();
    }

    private void Update()
    {
        if (_holdDigPose && _hasLoadedSand && !_isBusy)
            EquipmentHolder.Instance?.HoldWoodShovelDiggingPose();
    }

    public override bool Use()
    {
        if (_isBusy || _hasLoadedSand || IsOnCooldown())
            return false;

        _actionRoutine = StartCoroutine(DigRoutine());
        return true;
    }

    public override bool AltUse()
    {
        if (_isBusy || !_hasLoadedSand || IsOnCooldown())
            return false;

        _actionRoutine = StartCoroutine(PlaceRoutine());
        return true;
    }

    private IEnumerator DigRoutine()
    {
        _isBusy = true;
        _holdDigPose = false;
        EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(true);
        EquipmentHolder.Instance?.SetWoodShovelUseLocked(true);
        EquipmentHolder.Instance?.SetWoodShovelCameraLocked(true);
        EquipmentHolder.Instance?.PlayWoodShovelDiggingAnimation();

        float effectDelay = Mathf.Max(0f, DigEffectDelay);
        if (effectDelay > 0f)
            yield return new WaitForSeconds(effectDelay);

        if (TryModifySand(-1f))
        {
            EquipmentHolder.Instance?.SetWoodShovelCameraLocked(false);
            _lastUseTime = Time.time;
            _hasLoadedSand = true;
            SetSandVisualActive(true);

            float holdDelay = Mathf.Max(0f, DigAnimationDuration - effectDelay);
            if (holdDelay > 0f)
                yield return new WaitForSeconds(holdDelay);

            if (_hasLoadedSand)
            {
                _holdDigPose = true;
                EquipmentHolder.Instance?.HoldWoodShovelDiggingPose();
            }
        }
        else
        {
            EquipmentHolder.Instance?.SetWoodShovelCameraLocked(false);
            SetSandVisualActive(false);
            EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(false);
            EquipmentHolder.Instance?.ResetWoodShovelAnimation();
            EquipmentHolder.Instance?.SetWoodShovelUseLocked(false);
        }

        _isBusy = false;
        _actionRoutine = null;
    }

    private IEnumerator PlaceRoutine()
    {
        _isBusy = true;
        _holdDigPose = false;
        EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(true);
        EquipmentHolder.Instance?.SetWoodShovelUseLocked(true);
        EquipmentHolder.Instance?.SetWoodShovelCameraLocked(true);
        EquipmentHolder.Instance?.PlayWoodShovelPlaceAnimation();

        float effectDelay = Mathf.Max(0f, PlaceEffectDelay);
        if (effectDelay > 0f)
            yield return new WaitForSeconds(effectDelay);

        if (TryModifySand(1f))
        {
            EquipmentHolder.Instance?.SetWoodShovelCameraLocked(false);
            _lastUseTime = Time.time;
            _hasLoadedSand = false;
            SetSandVisualActive(false);
            EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(false);
            EquipmentHolder.Instance?.ResetWoodShovelAnimation();
            EquipmentHolder.Instance?.SetWoodShovelUseLocked(false);
        }
        else
        {
            EquipmentHolder.Instance?.SetWoodShovelCameraLocked(false);
            _holdDigPose = true;
            EquipmentHolder.Instance?.HoldWoodShovelDiggingPose();
        }

        _isBusy = false;
        _actionRoutine = null;
    }

    private void CancelActionRoutine()
    {
        if (_actionRoutine == null)
            return;

        StopCoroutine(_actionRoutine);
        _actionRoutine = null;
    }

    private void SetSandVisualActive(bool active)
    {
        if (_sandVisual != null)
            _sandVisual.gameObject.SetActive(active);
    }

    private void ClearShovelLockAndAnimation()
    {
        EquipmentHolder.Instance?.SetWoodShovelUseLocked(false);
        EquipmentHolder.Instance?.SetWoodShovelCameraLocked(false);
        EquipmentHolder.Instance?.SetWoodShovelActionTransformActive(false);
        EquipmentHolder.Instance?.ResetWoodShovelAnimation();
    }

    private static Transform FindSandVisual(Transform root)
    {
        if (root == null)
            return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != root && child.name.Equals("sand", System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private bool TryModifySand(float direction)
    {
        if (TerrainManager.Instance == null)
            return false;

        Camera cam = GetCurrentCamera();
        if (cam == null)
            return false;

        float maxDistance = Data != null ? Mathf.Max(0.1f, Data.Range) : 6f;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);

        if (!TryGetClosestSandHit(hits, out RaycastHit sandHit))
            return false;

        if (IsNearProtectedPlant(sandHit.point))
            return false;

        float strengthMultiplier = Data != null ? Mathf.Max(0f, Data.DigMultiplier) : 1f;
        float radiusMultiplier = Data != null ? Mathf.Max(0.01f, Data.RadiusMultiplier) : 1f;
        float basePower = Data != null
            ? (direction < 0f ? Data.DigPower : Data.PlacePower)
            : SandAmountPerClick;
        float baseRadius = Data != null
            ? (direction < 0f ? Data.DigRadius : Data.PlaceRadius)
            : BrushRadius;
        float amount = Mathf.Abs(basePower) * strengthMultiplier * Mathf.Sign(direction);

        if (MountainSpawner.Instance != null && TerrainManager.Instance.Config != null)
        {
            float influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(sandHit.point);
            amount *= Mathf.Lerp(1f, TerrainManager.Instance.Config.MountainHardnessFactor, influence);
        }

        TerrainManager.Instance.ModifyHeight(sandHit.point, amount, Mathf.Max(0.01f, baseRadius) * radiusMultiplier);
        return true;
    }

    private static bool TryGetClosestSandHit(RaycastHit[] hits, out RaycastHit closestHit)
    {
        closestHit = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponent<SandChunk>() == null)
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestHit = hit;
            found = true;
        }

        return found;
    }

    private static bool IsNearProtectedPlant(Vector3 point)
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(point, 2.5f);
        foreach (Collider col in nearbyColliders)
        {
            if (col == null)
                continue;

            if (col.GetComponent<SandChunk>() != null)
                continue;

            if (col.CompareTag("Player") || col.GetComponentInParent<PlayerController>() != null)
                continue;

            string rootName = col.transform.root.name.ToLowerInvariant();
            if (col.GetComponentInParent<PlantPhysics>() != null
                || rootName.Contains("bush")
                || rootName.Contains("palm")
                || rootName.Contains("tree")
                || rootName.Contains("plant")
                || rootName.Contains("cactus"))
            {
                return true;
            }
        }

        return false;
    }
}
