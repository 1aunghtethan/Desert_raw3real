using System.Collections;
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
    private InteractionManager _interactionManager;
    private Coroutine _rawKnifeSwingSoundRoutine;
    private Coroutine _rawKnifeHitRoutine;
    private Coroutine _rawKnifeSecondHitRoutine;
    private Coroutine _stoneSpearHitRoutine;
    private float _rawKnifeAttackLockedUntil = -999f;
    private float _rawKnifeAttackStartedAt = -999f;
    private bool _rawKnifeSecondHitQueued;

    private const float RawKnifeSwingSoundDelay = 0.1f;
    private const float RawKnifeHitDelay = 0.2f;
    private const float RawKnifeSecondAttackDelayAfterConfirm = 0.6f;
    private const float RawKnifeSecondAttackPostHitLock = 0.05f;
    private const float RawKnifeAttackLockDuration = 0.8f;
    private const float RawKnifeFullAttackLockDuration = 1.2f;
    private const float RawKnifeMeleeHitRange = 2.5f;
    private const float StoneSpearHitDelay = 0.1f;

    public override void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        base.OnEquip(data, owner, cam);
        _interactionManager = owner != null ? owner.GetComponent<InteractionManager>() : null;
        _restRotation = transform.localRotation;
    }

    public override void OnUnequip()
    {
        base.OnUnequip();

        if (_rawKnifeSwingSoundRoutine != null)
            StopCoroutine(_rawKnifeSwingSoundRoutine);
        if (_rawKnifeHitRoutine != null)
            StopCoroutine(_rawKnifeHitRoutine);
        if (_rawKnifeSecondHitRoutine != null)
            StopCoroutine(_rawKnifeSecondHitRoutine);
        if (_stoneSpearHitRoutine != null)
            StopCoroutine(_stoneSpearHitRoutine);

        _rawKnifeSwingSoundRoutine = null;
        _rawKnifeHitRoutine = null;
        _rawKnifeSecondHitRoutine = null;
        _stoneSpearHitRoutine = null;
        _rawKnifeAttackLockedUntil = -999f;
        _rawKnifeAttackStartedAt = -999f;
        _rawKnifeSecondHitQueued = false;
        _interactionManager = null;
    }

    protected virtual void Update()
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
            if (!UsesDelayedMeleeHit() && t > 0.1f && t < 0.5f && !_hasHitThisSwing)
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
        if (IsRawKnife() && (_isSwinging || Time.time < _rawKnifeAttackLockedUntil))
        {
            if (EquipmentHolder.Instance == null || !EquipmentHolder.Instance.RequestRawKnifeFullAttackAnimation())
                return false;

            StartRawKnifeSecondAttack();
            return true;
        }

        if (_isSwinging) return false;

        if (!base.Use()) return false;

        // Start swing animation
        _isSwinging = true;
        if (IsRawKnife())
        {
            _rawKnifeAttackStartedAt = Time.time;
            _rawKnifeAttackLockedUntil = Time.time + RawKnifeAttackLockDuration;
            _rawKnifeSecondHitQueued = false;
            EquipmentHolder.Instance?.TriggerRawKnifeMeleeAnimation();
            StartRawKnifeSwingSound();
            StartRawKnifeDelayedHit();
        }
        else if (IsStoneSpear())
        {
            EquipmentHolder.Instance?.TriggerStoneSpearMeleeAnimation();
            PlaySwingSound();
            StartStoneSpearDelayedHit();
        }
        else if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponSwing);
        }
        _swingTimer = 0f;
        _hasHitThisSwing = false;
        _swingStartRot = _restRotation;
        _swingEndRot = _restRotation * Quaternion.Euler(-SwingAngle, 0, 0);

        return true;
    }

    private bool IsRawKnife()
    {
        return Data != null && Data.ItemName == "Raw Knife";
    }

    private bool IsStoneSpear()
    {
        return Data != null && Data.ItemName == "Stone Spear";
    }

    private bool UsesDelayedMeleeHit()
    {
        return IsRawKnife() || IsStoneSpear();
    }

    private void StartRawKnifeSwingSound()
    {
        if (_rawKnifeSwingSoundRoutine != null)
            StopCoroutine(_rawKnifeSwingSoundRoutine);

        _rawKnifeSwingSoundRoutine = StartCoroutine(RawKnifeSwingSoundRoutine());
    }

    private void StartRawKnifeDelayedHit()
    {
        if (_rawKnifeHitRoutine != null)
            StopCoroutine(_rawKnifeHitRoutine);

        _rawKnifeHitRoutine = StartCoroutine(RawKnifeHitRoutine());
    }

    private void StartRawKnifeSecondAttack()
    {
        if (_rawKnifeSecondHitQueued)
            return;

        _rawKnifeSecondHitQueued = true;
        _rawKnifeAttackLockedUntil = Mathf.Max(
            Mathf.Max(
                _rawKnifeAttackLockedUntil,
                Time.time + RawKnifeSecondAttackDelayAfterConfirm + RawKnifeSecondAttackPostHitLock),
            _rawKnifeAttackStartedAt + RawKnifeFullAttackLockDuration);

        if (_rawKnifeSecondHitRoutine != null)
            StopCoroutine(_rawKnifeSecondHitRoutine);

        _rawKnifeSecondHitRoutine = StartCoroutine(RawKnifeSecondHitRoutine());
    }

    private void StartStoneSpearDelayedHit()
    {
        if (_stoneSpearHitRoutine != null)
            StopCoroutine(_stoneSpearHitRoutine);

        _stoneSpearHitRoutine = StartCoroutine(StoneSpearHitRoutine());
    }

    private IEnumerator RawKnifeSwingSoundRoutine()
    {
        yield return new WaitForSeconds(RawKnifeSwingSoundDelay);
        PlaySwingSound();

        _rawKnifeSwingSoundRoutine = null;
    }

    private IEnumerator RawKnifeHitRoutine()
    {
        yield return new WaitForSeconds(RawKnifeHitDelay);

        if (!_hasHitThisSwing)
            PerformHitDetection();

        _rawKnifeHitRoutine = null;
    }

    private IEnumerator RawKnifeSecondHitRoutine()
    {
        yield return new WaitForSeconds(RawKnifeSecondAttackDelayAfterConfirm);

        PlaySwingSound();
        PerformHitDetection();
        _rawKnifeAttackLockedUntil = Mathf.Max(
            _rawKnifeAttackLockedUntil,
            Time.time + RawKnifeSecondAttackPostHitLock);
        _rawKnifeSecondHitRoutine = null;
    }

    private IEnumerator StoneSpearHitRoutine()
    {
        yield return new WaitForSeconds(StoneSpearHitDelay);

        if (!_hasHitThisSwing)
            PerformHitDetection();

        _stoneSpearHitRoutine = null;
    }

    private static void PlaySwingSound()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponSwing);
    }

    private void PerformHitDetection()
    {
        Camera cam = GetCurrentCamera();
        if (cam == null)
        {
            Debug.LogWarning("[Melee] No camera available for hit detection!");
            return;
        }

        Ray ray = GetHitDetectionRay(cam);
        float hitRange = IsRawKnife() ? RawKnifeMeleeHitRange : Data.Range;
        RaycastHit[] hits = Physics.RaycastAll(ray, hitRange, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        RaycastHit? firstNonDamageableHit = null;
        foreach (var hit in hits)
        {
            // Skip self
            if (hit.transform == OwnerTransform) continue;
            if (hit.transform.IsChildOf(OwnerTransform)) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            RoofSandStabilizer roof = hit.collider.GetComponentInParent<RoofSandStabilizer>();
            if (roof != null && roof.TryCut(Data, Data.Damage, hit.point, ray.direction))
            {
                _hasHitThisSwing = true;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponHit);
                Debug.Log($"[Melee] Cut roof with {Data.ItemName} for {Data.Damage} cut damage.");
                return;
            }

            // Check for Damageable
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(Data.Damage, hit.point, ray.direction);
                _hasHitThisSwing = true;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponHit);
                Debug.Log($"[Melee] Hit {hit.collider.name} for {Data.Damage} damage!");
                return;
            }

            if (!firstNonDamageableHit.HasValue)
                firstNonDamageableHit = hit;

            if (IsTerrainLikeHit(hit))
                continue;

            break;
        }

        if (firstNonDamageableHit.HasValue)
        {
            RaycastHit hit = firstNonDamageableHit.Value;
            Debug.Log($"[Melee] Struck {hit.collider.name} at {hit.point}");
            _hasHitThisSwing = true;
        }
    }

    private static bool IsTerrainLikeHit(RaycastHit hit)
    {
        if (hit.collider == null)
            return false;

        if (hit.collider.GetComponentInParent<SandChunk>() != null)
            return true;

        string objectName = hit.collider.gameObject.name.ToLowerInvariant();
        return objectName.Contains("sand") || objectName.Contains("terrain");
    }

    private Ray GetHitDetectionRay(Camera cam)
    {
        if (UsesDelayedMeleeHit())
        {
            if (_interactionManager == null && OwnerTransform != null)
                _interactionManager = OwnerTransform.GetComponent<InteractionManager>();

            if (_interactionManager != null)
                return _interactionManager.GetCursorWorldRay(cam);
        }

        return new Ray(cam.transform.position, cam.transform.forward);
    }
}
