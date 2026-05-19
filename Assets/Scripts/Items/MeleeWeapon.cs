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
    private Coroutine _rawKnifeSwingSoundRoutine;
    private Coroutine _rawKnifeHitRoutine;
    private Coroutine _stoneSpearHitRoutine;

    private const float RawKnifeSwingSoundDelay = 0.1f;
    private const float RawKnifeHitDelay = 0.2f;
    private const float StoneSpearHitDelay = 0.1f;

    public override void OnEquip(ItemData data, Transform owner, Camera cam)
    {
        base.OnEquip(data, owner, cam);
        _restRotation = transform.localRotation;
    }

    public override void OnUnequip()
    {
        base.OnUnequip();

        if (_rawKnifeSwingSoundRoutine != null)
            StopCoroutine(_rawKnifeSwingSoundRoutine);
        if (_rawKnifeHitRoutine != null)
            StopCoroutine(_rawKnifeHitRoutine);
        if (_stoneSpearHitRoutine != null)
            StopCoroutine(_stoneSpearHitRoutine);

        _rawKnifeSwingSoundRoutine = null;
        _rawKnifeHitRoutine = null;
        _stoneSpearHitRoutine = null;
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
        if (_isSwinging) return false;
        if (!base.Use()) return false;

        // Start swing animation
        _isSwinging = true;
        if (IsRawKnife())
        {
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

        // Raycast from camera center
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, Data.Range, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

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
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponHit);
                Debug.Log($"[Melee] Hit {hit.collider.name} for {Data.Damage} damage!");
                return;
            }

            // Visual feedback: hit the terrain or environment
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioManager.Instance.weaponHit);
            Debug.Log($"[Melee] Struck {hit.collider.name} at {hit.point}");
            _hasHitThisSwing = true;
            return;
        }
    }
}
