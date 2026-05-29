using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Handles the player sleep mechanic with a fade-to-black screen effect.
/// - Rest hours: sleep is allowed from 18.5 through 5.5.
/// - Daytime: sleep is allowed only if the player is in shadow.
/// - After waking: sleep is blocked until SleepCooldownHours in-game hours pass.
/// - Sleep is blocked while health is actively reducing.
/// Attach to the Player GameObject.
/// </summary>
public class SleepSystem : MonoBehaviour
{
    [Header("Input")]
    public KeyCode SleepKey = KeyCode.T;

    [Header("Sleep Settings")]
    [Tooltip("How many degrees of time to advance when sleeping. 90 degrees = 6 in-game hours.")]
    public float TimeAdvanceDegrees = 90f;
    [Tooltip("Player can sleep from this clock hour until midnight, and from midnight until SleepAllowedEndHour.")]
    public float SleepAllowedStartHour = 18.5f;
    [Tooltip("Player can sleep until this clock hour after midnight.")]
    public float SleepAllowedEndHour = 5.5f;
    [Tooltip("How many in-game clock hours must pass after waking before sleeping again.")]
    public float SleepCooldownHours = 6f;

    [Header("Roof Shade Sleep")]
    [Tooltip("Daytime roof sleep is allowed when this percent of shade samples are covered by placed Roofs.")]
    [Range(0f, 1f)] public float RootSleepBlockThreshold = 0.8f;
    [Tooltip("Radius for the center/forward/back/left/right shade samples around the player.")]
    public float RootSleepShadeSampleRadius = 0.45f;
    [Tooltip("Maximum ray distance used when checking if Roof is the shade blocker.")]
    public float RootSleepShadeRayDistance = 100f;

    [Header("Screen Fade Timing (seconds)")]
    [Tooltip("Duration of fade to black.")]
    public float FadeInDuration = 2.5f;
    [Tooltip("Duration screen stays fully black.")]
    public float BlackDuration = 3f;
    [Tooltip("Duration of fade from black to clear.")]
    public float FadeOutDuration = 2.5f;

    [Header("References (auto-found if empty)")]
    public cyclemanager DayNight;
    public PlayerStats Stats;

    public bool IsSleeping { get; private set; }

    private PlayerController _playerController;
    private ThirdPersonCamera _tpCam;
    private Canvas _fadeCanvas;
    private Image _fadeImage;
    private float _lastSleepEndHour = -999f;

    private float CurrentHour => DayNight != null ? Mathf.Repeat(DayNight.currentTime, 24f) : 0f;

    private void Start()
    {
        if (DayNight == null) DayNight = FindFirstObjectByType<cyclemanager>();
        if (Stats == null) Stats = PlayerStats.Instance ?? FindFirstObjectByType<PlayerStats>();
        _playerController = GetComponent<PlayerController>();
        _tpCam = FindFirstObjectByType<ThirdPersonCamera>();

        CreateFadeOverlay();
    }

    private void Update()
    {
        if (IsSleeping) return;

        if (Input.GetKeyDown(SleepKey))
        {
            TrySleep();
        }
    }

    private void TrySleep()
    {
        if (DayNight == null || Stats == null) return;

        // Block sleep if health is reducing from hunger or thirst.
        // BUT allow sleep when the only damage is from sleep deprivation (since sleeping cures it).
        if (Stats.IsTakingNonSleepDamage)
        {
            Debug.Log("[SleepSystem] Can't sleep while health is reducing from hunger or thirst.");
            return;
        }

        float currentHour = CurrentHour;
        if (!HasSleepCooldownElapsed(currentHour))
        {
            float remaining = SleepCooldownHours - HoursSince(_lastSleepEndHour, currentHour);
            Debug.Log($"[SleepSystem] Can't sleep yet. Rest again in {remaining:F1} in-game hours.");
            return;
        }

        if (IsWithinSleepHours(currentHour))
        {
            StartSleep(TimeAdvanceDegrees);
            return;
        }

        if (Stats.IsInShadow || IsShelteredByRoof())
        {
            StartSleep(TimeAdvanceDegrees);
            return;
        }

        Debug.Log($"[SleepSystem] Can't sleep now. Sleep from {SleepAllowedStartHour:F1} to {SleepAllowedEndHour:F1}, or find shadow during the day.");
    }

    private bool IsShelteredByRoof()
    {
        if (DayNight == null || Stats == null)
            return false;

        Vector3 sunDir = -DayNight.transform.forward;
        if (sunDir.sqrMagnitude < 0.001f)
            return false;

        sunDir.Normalize();
        float sampleRadius = Mathf.Max(0f, RootSleepShadeSampleRadius);
        float rayDistance = Mathf.Max(0.1f, RootSleepShadeRayDistance);
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = transform.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.right;
        right.Normalize();

        Vector3 basePoint = transform.position + Vector3.up;
        Vector3[] samplePoints =
        {
            basePoint,
            basePoint + forward * sampleRadius,
            basePoint - forward * sampleRadius,
            basePoint + right * sampleRadius,
            basePoint - right * sampleRadius
        };

        int roofCoveredCount = 0;
        LayerMask shadowMask = Stats.ShadowLayerMask;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            if (!Physics.Raycast(samplePoints[i], sunDir, out RaycastHit hit, rayDistance, shadowMask, QueryTriggerInteraction.Ignore))
                continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            if (IsRootShadeBlocker(hit.collider))
                roofCoveredCount++;
        }

        float roofCoverageRatio = (float)roofCoveredCount / samplePoints.Length;
        return roofCoverageRatio >= Mathf.Clamp01(RootSleepBlockThreshold);
    }

    private static bool IsRootShadeBlocker(Collider collider)
    {
        if (collider == null)
            return false;

        if (collider.GetComponentInParent<RoofSandStabilizer>() != null)
            return true;

        Transform root = collider.transform.root;
        return root != null
            && (root.name.StartsWith("roof_Placed", System.StringComparison.OrdinalIgnoreCase)
                || root.name.StartsWith("root_Placed", System.StringComparison.OrdinalIgnoreCase));
    }

    private void StartSleep(float degreesToAdvance)
    {
        IsSleeping = true;
        Stats.IsSleeping = true;

        if (_playerController != null) _playerController.enabled = false;
        if (_tpCam != null) _tpCam.enabled = false;

        Debug.Log($"[SleepSystem] Sleeping... time will advance by {degreesToAdvance:F0} degrees from {CurrentHour:F1}.");
        StartCoroutine(SleepSequence(degreesToAdvance));
    }

    private IEnumerator SleepSequence(float degreesToAdvance)
    {
        float elapsed = 0f;
        while (elapsed < FadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / FadeInDuration));
            yield return null;
        }
        SetOverlayAlpha(1f);

        DayNight.AdvanceTime(degreesToAdvance);
        Debug.Log($"[SleepSystem] Time advanced to {CurrentHour:F1}.");

        ApplySkippedTimeStatDrain(degreesToAdvance);

        yield return new WaitForSeconds(BlackDuration);

        elapsed = 0f;
        while (elapsed < FadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / FadeOutDuration);
            SetOverlayAlpha(alpha);
            yield return null;
        }
        SetOverlayAlpha(0f);

        WakeUp();
    }

    private void ApplySkippedTimeStatDrain(float degreesToAdvance)
    {
        float originalSpeed = DayNight.CycleSpeed;
        if (originalSpeed <= 0f) return;

        float skippedRealSeconds = degreesToAdvance / originalSpeed;
        float sleepMult = Stats.SleepDecayMultiplier;
        float hungerDrain = Stats.HungerDecayRate * skippedRealSeconds * sleepMult;
        float thirstDrain = Stats.BaseThirstDecay * skippedRealSeconds * sleepMult;
        Stats.CurrentHunger = Mathf.Max(0, Stats.CurrentHunger - hungerDrain);
        Stats.CurrentThirst = Mathf.Max(0, Stats.CurrentThirst - thirstDrain);
    }

    private void WakeUp()
    {
        if (Stats != null)
        {
            Stats.CurrentSleep = Stats.MaxSleep;
            Stats.IsSleeping = false;
        }

        IsSleeping = false;
        _lastSleepEndHour = CurrentHour;

        if (_playerController != null) _playerController.enabled = true;
        if (_tpCam != null) _tpCam.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log($"[SleepSystem] Woke up at {CurrentHour:F1}. Next sleep allowed after {SleepCooldownHours:F1} in-game hours.");
    }

    private bool IsWithinSleepHours(float hour)
    {
        hour = Mathf.Repeat(hour, 24f);
        float start = Mathf.Repeat(SleepAllowedStartHour, 24f);
        float end = Mathf.Repeat(SleepAllowedEndHour, 24f);

        if (start <= end)
            return hour >= start && hour <= end;

        return hour >= start || hour <= end;
    }

    private bool HasSleepCooldownElapsed(float currentHour)
    {
        return _lastSleepEndHour < 0f || HoursSince(_lastSleepEndHour, currentHour) >= SleepCooldownHours;
    }

    private static float HoursSince(float fromHour, float toHour)
    {
        return Mathf.Repeat(toHour - fromHour, 24f);
    }

    private void CreateFadeOverlay()
    {
        GameObject canvasObj = new GameObject("SleepFadeCanvas");
        canvasObj.transform.SetParent(transform);
        _fadeCanvas = canvasObj.AddComponent<Canvas>();
        _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fadeCanvas.sortingOrder = 999;

        GameObject imgObj = new GameObject("FadeImage");
        imgObj.transform.SetParent(canvasObj.transform, false);
        _fadeImage = imgObj.AddComponent<Image>();
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);
        _fadeImage.raycastTarget = false;

        RectTransform rt = _fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (_fadeImage != null)
        {
            _fadeImage.color = new Color(0f, 0f, 0f, alpha);
        }
    }
}
