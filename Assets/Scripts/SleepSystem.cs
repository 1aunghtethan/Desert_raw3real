using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Handles the player sleep mechanic with a fade-to-black screen effect.
///   - Night (TimeOfDay >= 180): Can always sleep.
///   - Day (TimeOfDay < 180):    Can only sleep if in shadow. Time won't advance past 180.
///   - Can't sleep if health is actively reducing (starving/dehydrated).
///   - During sleep: hunger & thirst drain at a significantly reduced rate (configured in PlayerStats).
///   - Screen effect: fade in 2.5s → black 3s → fade out 2.5s = 8s total.
/// Attach to the Player GameObject.
/// </summary>
public class SleepSystem : MonoBehaviour
{
    [Header("Input")]
    public KeyCode SleepKey = KeyCode.T;

    [Header("Sleep Settings")]
    [Tooltip("How many degrees of time to advance when sleeping.")]
    public float TimeAdvanceDegrees = 90f;

    [Header("Screen Fade Timing (seconds)")]
    [Tooltip("Duration of fade to black.")]
    public float FadeInDuration = 2.5f;
    [Tooltip("Duration screen stays fully black.")]
    public float BlackDuration = 3f;
    [Tooltip("Duration of fade from black to clear.")]
    public float FadeOutDuration = 2.5f;

    [Header("References (auto-found if empty)")]
    public DayNightCycle DayNight;
    public PlayerStats Stats;

    /// <summary>True while the player is currently sleeping.</summary>
    public bool IsSleeping { get; private set; }

    private PlayerController _playerController;
    private ThirdPersonCamera _tpCam;

    // Screen overlay
    private Canvas _fadeCanvas;
    private Image _fadeImage;

    void Start()
    {
        if (DayNight == null) DayNight = FindFirstObjectByType<DayNightCycle>();
        if (Stats == null) Stats = PlayerStats.Instance ?? FindFirstObjectByType<PlayerStats>();
        _playerController = GetComponent<PlayerController>();
        _tpCam = FindFirstObjectByType<ThirdPersonCamera>();

        CreateFadeOverlay();
    }

    void Update()
    {
        if (IsSleeping) return; // Coroutine handles everything

        if (Input.GetKeyDown(SleepKey))
        {
            TrySleep();
        }
    }

    private void TrySleep()
    {
        if (DayNight == null || Stats == null) return;

        // --- Can't sleep while taking damage (starving, dehydrated) ---
        if (Stats.IsTakingDamage)
        {
            Debug.Log("[SleepSystem] Can't sleep — health is reducing! (starving or dehydrated)");
            return;
        }

        bool isDay = DayNight.IsDay;

        // --- Daytime: require shadow ---
        if (isDay)
        {
            if (!Stats.IsInShadow)
            {
                Debug.Log("[SleepSystem] Can't sleep during the day — find shadow first!");
                return;
            }

            // Check if time would go past 180 (sunset)
            float maxAdvance = 180f - DayNight.TimeOfDay;
            if (maxAdvance <= 1f)
            {
                Debug.Log("[SleepSystem] Can't sleep — too close to sunset.");
                return;
            }

            // Cap advance so we don't go past 180
            float advance = Mathf.Min(TimeAdvanceDegrees, maxAdvance);
            StartSleep(advance);
        }
        else
        {
            // --- Nighttime: always allowed ---
            StartSleep(TimeAdvanceDegrees);
        }
    }

    private void StartSleep(float degreesToAdvance)
    {
        IsSleeping = true;
        Stats.IsSleeping = true;

        // Disable player controls during sleep
        if (_playerController != null) _playerController.enabled = false;
        if (_tpCam != null) _tpCam.enabled = false;

        Debug.Log($"[SleepSystem] Sleeping... time will advance by {degreesToAdvance:F0}° (from {DayNight.TimeOfDay:F0}°)");
        StartCoroutine(SleepSequence(degreesToAdvance));
    }

    /// <summary>
    /// Fade in (2.5s) → hold black (3s, time advances here) → fade out (2.5s).
    /// </summary>
    private IEnumerator SleepSequence(float degreesToAdvance)
    {
        // ─── Phase 1: Fade to black ───
        float elapsed = 0f;
        while (elapsed < FadeInDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / FadeInDuration);
            SetOverlayAlpha(alpha);
            yield return null;
        }
        SetOverlayAlpha(1f);

        // ─── Phase 2: Stay black — advance time instantly ───
        DayNight.AdvanceTime(degreesToAdvance);
        Debug.Log($"[SleepSystem] Time advanced to {DayNight.TimeOfDay:F0}°");

        // Apply reduced stat drain for the skipped time period
        // Calculate how many real seconds the skipped degrees represent
        float originalSpeed = DayNight.CycleSpeed;
        if (originalSpeed > 0f)
        {
            float skippedRealSeconds = degreesToAdvance / originalSpeed;
            float sleepMult = Stats.SleepDecayMultiplier;
            float hungerDrain = Stats.HungerDecayRate * skippedRealSeconds * sleepMult;
            float thirstDrain = Stats.BaseThirstDecay * skippedRealSeconds * sleepMult;
            Stats.CurrentHunger = Mathf.Max(0, Stats.CurrentHunger - hungerDrain);
            Stats.CurrentThirst = Mathf.Max(0, Stats.CurrentThirst - thirstDrain);
        }

        // Hold black screen
        yield return new WaitForSeconds(BlackDuration);

        // ─── Phase 3: Fade from black ───
        elapsed = 0f;
        while (elapsed < FadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / FadeOutDuration);
            SetOverlayAlpha(alpha);
            yield return null;
        }
        SetOverlayAlpha(0f);

        // ─── Done ───
        WakeUp();
    }

    private void WakeUp()
    {
        if (Stats != null)
        {
            Stats.CurrentSleep = Stats.MaxSleep; // Restore sleep to max
        }
        IsSleeping = false;
        Stats.IsSleeping = false;

        // Re-enable player controls
        if (_playerController != null) _playerController.enabled = true;
        if (_tpCam != null) _tpCam.enabled = true;

        // Re-lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log($"[SleepSystem] Woke up at time {DayNight.TimeOfDay:F0}°");
    }

    // ═══════════════════════════════════════════
    // FADE OVERLAY
    // ═══════════════════════════════════════════

    private void CreateFadeOverlay()
    {
        // Create a dedicated screen-space overlay canvas for the fade
        GameObject canvasObj = new GameObject("SleepFadeCanvas");
        canvasObj.transform.SetParent(transform);
        _fadeCanvas = canvasObj.AddComponent<Canvas>();
        _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fadeCanvas.sortingOrder = 999; // On top of everything

        // Full-screen black image
        GameObject imgObj = new GameObject("FadeImage");
        imgObj.transform.SetParent(canvasObj.transform, false);
        _fadeImage = imgObj.AddComponent<Image>();
        _fadeImage.color = new Color(0f, 0f, 0f, 0f); // Start transparent
        _fadeImage.raycastTarget = false;

        // Stretch to fill screen
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
