using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class cyclemanager : MonoBehaviour
{
    [Header("Compatibility")]
    [Tooltip("Speed of the day-night cycle in degrees per second. Used by sleep and survival systems.")]
    public float CycleSpeed = 6f;

    [Tooltip("Current time of day in degrees. 0 = sunrise, 90 = noon, 180 = sunset, 270 = midnight.")]
    [Range(0f, 360f)] public float TimeOfDay = 0f;

    public float MinTemperature = 0f;
    public float MaxTemperature = 50f;
    public float CurrentTemperature { get; private set; }
    public bool IsDay => TimeOfDay > 0f && TimeOfDay < 180f;

    [Header("⏱️ Time Progression")]
    [Tooltip("Current time of day in 24-hour format.")]
    [Range(0f, 24f)] public float currentTime;
    
    [Tooltip("Speed multiplier for time progression.")]
    public float timeSpeed = 1f;

    [Space]
    [Header("🕒 Current Time Display")]
    [Tooltip("Formatted time string for UI or display.")]
    public string currentTimeString;

    [Space]
    [Header("Fade Settings")]
    [Tooltip("Real seconds used to fade the sun light and moon visual in or out.")]
    [Min(0.01f)] public float fadeDurationSeconds = 4f;

    [Space]
    [Header("☀️ Sun Configuration")]
    public Light sunLight;
    
    [Tooltip("Sun latitude angle.")]
    [Range(0f, 90f)] public float sunLatitude = 20f;

    [Tooltip("Sun longitude angle.")]
    [Range(-180f, 180f)] public float sunLongitude = -90f;

    [Tooltip("Base intensity for sun.")]
    public float sunIntensity = 1f;

    public AnimationCurve sunIntensityMultiplier;
    public AnimationCurve sunTemperatureCurve;

    [Space]
    [Header("🌙 Moon Configuration")]
    public Light moonLight;
    
    [Tooltip("Moon delay behind the sun in TimeOfDay units/degrees.")]
    [Range(0f, 360f)] public float moonTimeDelay = 12f;

    [Tooltip("Legacy moon latitude value. The moon now follows the sun latitude.")]
    [Range(0f, 90f)] public float moonLatitude = 40f;

    [Tooltip("Moon longitude angle.")]
    [Range(-180f, 180f)] public float moonLongitude = 90f;

    [Tooltip("Base intensity for moon.")]
    public float moonIntensity = 1f;

    [Tooltip("If enabled, the moon light is positioned at MoonVisual and shines down from there.")]
    public bool useMoonVisualAsLightSource = true;

    [Tooltip("Moon spotlight range when using MoonVisual as the light source.")]
    public float moonLightRange = 500f;

    [Tooltip("Moon spotlight cone angle when using MoonVisual as the light source.")]
    [Range(1f, 179f)] public float moonSpotAngle = 70f;

    [Tooltip("Maximum moon terrain light intensity at night.")]
    public float moonTerrainLightIntensity = 0.45f;

    [Tooltip("Ambient light level at night. Keep this low so terrain stays dark.")]
    [Range(0f, 1f)] public float nightAmbientIntensity = 0.03f;

    [Tooltip("Ambient light level during the day.")]
    [Range(0f, 1f)] public float dayAmbientIntensity = 0.65f;

    public Color nightAmbientColor = new Color(0.03f, 0.04f, 0.07f, 1f);
    public Color dayAmbientColor = new Color(0.75f, 0.72f, 0.65f, 1f);

    public AnimationCurve moonIntensityMultiplier;
    public AnimationCurve moonTemperatureCurve;

    [Space]
    [Header("🌕 Lunar Cycle Settings")]
    [Range(1, 30)] public int lunarDay = 1;

    [Tooltip("Speed at which the lunar day changes.")]
    public float lunarCycleSpeed = 1f;

    [Tooltip("Reference to the moon visual object. Assign this manually in the Inspector.")]
    public Transform moonModel;

    [Tooltip("Texture used by the generated moon sphere.")]
    public Texture2D moonTexture;

    [Tooltip("Optional material for the moon visual. Leave empty to generate one automatically.")]
    public Material moonMaterialOverride;

    [Tooltip("Target the moon visual orbits around. If empty, the player is found automatically.")]
    public Transform moonOrbitTarget;

    [Tooltip("Distance of the generated moon visual from the orbit target.")]
    public float moonVisualDistance = 350f;

    [Tooltip("Size of the generated moon visual.")]
    public float moonVisualScale = 25f;

    [Tooltip("Minimum Y direction for the moon orbit. Higher = moon stays higher in sky. 0.05 = near horizon, 0.5 = never dips low.")]
    [Range(0.01f, 1f)] public float moonMinHorizon = 0.05f;

    [Space]
    [Header("🔁 Status Flags (Read-Only)")]
    public bool isDay = true;
    public bool sunActive = true;
    public bool moonActive = true;

    private Vector3 moonModelBaseScale = Vector3.one;
    private PlayerStats cachedPlayerStats;
    private float sunFade = 1f;
    private float moonFade = 1f;

    void Start()
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        EnsureMoonSetup();
        InitializeCurves();
        UpdateTimeText();
        CheckShadowStatus();
        InitializeFadeState();
        ApplyLightActiveState();
        ApplyMoonVisualActiveState();
    }

    void Update()
    {
        TimeOfDay += CycleSpeed * Time.deltaTime;
        if (TimeOfDay >= 360f)
        {
            TimeOfDay -= 360f;
            lunarDay = (lunarDay % 30) + 1;
        }

        currentTime = TimeOfDay / 15f;

        UpdateTimeText();
        CheckShadowStatus(false);
        UpdateFadeState(Time.deltaTime);
        UpdateLight();
        UpdateMoonPhase(true);
        ApplyLightActiveState();
        ApplyMoonVisualActiveState();
    }

    private void OnValidate()
    {
        InitializeCurves();
        if (currentTime <= 0f && TimeOfDay > 0f)
            currentTime = TimeOfDay / 15f;
        else
            TimeOfDay = Mathf.Repeat(currentTime * 15f, 360f);

        CheckShadowStatus(false);
        InitializeFadeState();
        UpdateLight();
        UpdateMoonPhase(false);
        ApplyLightActiveState();
        ApplyMoonVisualActiveState();
    }

    void UpdateTimeText()
    {
        currentTimeString = Mathf.Floor(currentTime).ToString("00") + ":" + ((currentTime % 1) * 60).ToString("00");
    }

    void UpdateLight()
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        if (sunLight == null) return;

        float sunRotation = currentTime / 24f * 360f;
        float moonTimeOfDay = Mathf.Repeat(TimeOfDay - moonTimeDelay, 360f);
        float moonRotation = moonTimeOfDay + 180f;

        sunLight.transform.localRotation = Quaternion.Euler(sunLatitude - 90f, sunLongitude, 0) * Quaternion.Euler(0, sunRotation, 0);
        if (moonLight != null)
        {
            moonLight.transform.localRotation = Quaternion.Euler(moonLatitude - 90f, sunLongitude + 180f, 0) * Quaternion.Euler(0, moonRotation, 0);
            UpdateMoonVisualPosition();
        }

        float normalizedTime = currentTime / 24f;
        float sunCurve = sunIntensityMultiplier.Evaluate(normalizedTime);

        sunLight.intensity = sunCurve * sunIntensity * sunFade;

        if (moonLight != null)
        {
            float moonCurve = moonIntensityMultiplier.Evaluate(moonTimeOfDay / 360f);
            float phaseMultiplier = Mathf.Sin((float)lunarDay / 30f * Mathf.PI);
            float clampedPhase = Mathf.Max(phaseMultiplier, 0.3f);
            float targetMoonIntensity = useMoonVisualAsLightSource ? moonTerrainLightIntensity : moonIntensity;
            moonLight.intensity = moonCurve * targetMoonIntensity * clampedPhase * moonFade;
        }

        sunLight.useColorTemperature = true;
        sunLight.colorTemperature = sunTemperatureCurve.Evaluate(normalizedTime) * 10000f;

        if (moonLight != null)
        {
            moonLight.useColorTemperature = true;
            moonLight.colorTemperature = moonTemperatureCurve.Evaluate(normalizedTime) * 10000f;
        }

        float sunHeight = Mathf.Sin(TimeOfDay * Mathf.Deg2Rad);
        CurrentTemperature = Mathf.Lerp(MinTemperature, MaxTemperature, (sunHeight + 1f) * 0.5f);

        UpdateAmbientLight();
    }

    void CheckShadowStatus(bool applyActiveState = true)
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        if (sunLight == null) return;

        float t = currentTime;

        sunActive = t >= 5.5f && t <= 18.5f;
        moonActive = (t >= 18.5f || t <= 5.5f);
        isDay = t >= 6f && t <= 18f;

        sunLight.shadows = isDay ? LightShadows.Soft : LightShadows.None;
        if (moonLight != null)
            moonLight.shadows = moonActive ? LightShadows.Soft : LightShadows.None;

        if (applyActiveState)
            ApplyLightActiveState();

        if (applyActiveState)
            ApplyMoonVisualActiveState();
    }

    void UpdateMoonPhase(bool allowSetup)
    {
        if (allowSetup)
            EnsureMoonSetup();
        else if (moonModel == null && moonLight != null)
            moonModel = moonLight.transform.Find("MoonVisual");

        if (moonModel != null)
            moonModel.localScale = moonModelBaseScale;
    }

    private void UpdateMoonVisualPosition()
    {
        if (moonModel == null || moonLight == null)
            return;

        Transform target = GetMoonOrbitTarget();
        Vector3 center = target != null ? target.position : transform.position;

        // Night window: 18.5h to 5.5h (in TimeOfDay degrees: 277.5 to 82.5)
        float nightStartDeg = 277.5f; // 18.5h * 15
        float nightEndDeg = 82.5f;    // 5.5h * 15
        float nightSpanDeg = (360f - nightStartDeg) + nightEndDeg; // = 165 degrees

        // How far through the night are we? 0 = moonrise (18.5h), 1 = moonset (5.5h)
        float progress = Mathf.Repeat(TimeOfDay - nightStartDeg, 360f) / nightSpanDeg;
        progress = Mathf.Clamp01(progress);

        // Half-circle arc: 0° at rise, 90° at peak (midnight), 180° at set
        float moonArcRad = progress * Mathf.PI;

        // Y = sin (0→1→0 = rise, peak, set), Z = -cos (sweeps from one horizon to the other)
        float y = Mathf.Sin(moonArcRad);
        float z = -Mathf.Cos(moonArcRad);

        Vector3 skyDirection = new Vector3(0f, y, z);
        skyDirection.Normalize();

        moonModel.position = center + skyDirection * moonVisualDistance;
        moonModel.rotation = Quaternion.LookRotation(center - moonModel.position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

        if (useMoonVisualAsLightSource && moonLight != null)
        {
            moonLight.type = LightType.Spot;
            moonLight.range = moonLightRange;
            moonLight.spotAngle = moonSpotAngle;
            moonLight.transform.position = moonModel.position;
            moonLight.transform.rotation = Quaternion.LookRotation(center - moonModel.position, Vector3.up);
        }
    }

    private Transform GetMoonOrbitTarget()
    {
        if (moonOrbitTarget != null)
            return moonOrbitTarget;

        if (cachedPlayerStats == null)
            cachedPlayerStats = PlayerStats.Instance ?? FindFirstObjectByType<PlayerStats>();

        return cachedPlayerStats != null ? cachedPlayerStats.transform : null;
    }

    public void AdvanceTime(float degrees)
    {
        TimeOfDay = Mathf.Repeat(TimeOfDay + degrees, 360f);
        currentTime = TimeOfDay / 15f;
        UpdateLight();
        CheckShadowStatus();
        UpdateMoonPhase(true);
    }

    private void InitializeCurves()
    {
        if (sunIntensityMultiplier == null || sunIntensityMultiplier.length == 0)
            sunIntensityMultiplier = AnimationCurve.EaseInOut(0f, 0f, 0.5f, 1f);

        if (moonIntensityMultiplier == null || moonIntensityMultiplier.length == 0)
            moonIntensityMultiplier = AnimationCurve.EaseInOut(0f, 1f, 0.5f, 0f);

        if (sunTemperatureCurve == null || sunTemperatureCurve.length == 0)
            sunTemperatureCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 0.5f);

        if (moonTemperatureCurve == null || moonTemperatureCurve.length == 0)
            moonTemperatureCurve = AnimationCurve.Linear(0f, 0.7f, 1f, 0.7f);
    }

    private void EnsureMoonSetup()
    {
        if (moonLight == null)
        {
            GameObject moonObject = GameObject.Find("Moon");
            if (moonObject == null)
                moonObject = new GameObject("Moon");

            moonObject.transform.position = new Vector3(0f, 10f, 0f);
            moonLight = moonObject.GetComponent<Light>();
            if (moonLight == null)
                moonLight = moonObject.AddComponent<Light>();

            moonLight.type = useMoonVisualAsLightSource ? LightType.Spot : LightType.Directional;
            moonLight.color = new Color(0.68f, 0.78f, 1f, 1f);
            moonLight.intensity = moonIntensity;
            moonLight.range = moonLightRange;
            moonLight.spotAngle = moonSpotAngle;
            moonLight.shadows = LightShadows.Soft;
        }

        // Keep the Moon GameObject alive while it is active or finishing a fade-out.
        if (moonLight != null && !moonLight.gameObject.activeSelf && (moonActive || moonFade > 0f))
            moonLight.gameObject.SetActive(true);

        if (moonModel == null && moonLight != null)
        {
            Transform existingMoonVisual = moonLight.transform.Find("MoonVisual");
            if (existingMoonVisual != null)
            {
                moonModel = existingMoonVisual;
            }
        }
        else if (moonModel != null && moonModel.parent == moonLight.transform)
        {
            moonModel.SetParent(null, true);
        }

        if (moonModel != null)
        {
            // Safety: strip any collider that survived (e.g. from saved scene)
            Collider leftover = moonModel.GetComponent<Collider>();
            if (leftover != null)
            {
                if (Application.isPlaying) Destroy(leftover);
                else DestroyImmediate(leftover);
            }
            moonModel.gameObject.layer = 2; // Ignore Raycast

            moonModelBaseScale = Vector3.one * moonVisualScale;
            moonModel.localScale = moonModelBaseScale;
            ApplyMoonVisualMaterial();
            UpdateMoonVisualPosition();
            ApplyMoonVisualActiveState();
        }
    }

    private void InitializeFadeState()
    {
        sunFade = sunActive ? 1f : 0f;
        moonFade = moonActive ? 1f : 0f;
    }

    private void UpdateFadeState(float deltaTime)
    {
        float fadeStep = deltaTime / Mathf.Max(0.01f, fadeDurationSeconds);
        sunFade = Mathf.MoveTowards(sunFade, sunActive ? 1f : 0f, fadeStep);
        moonFade = Mathf.MoveTowards(moonFade, moonActive ? 1f : 0f, fadeStep);
    }

    private void ApplyLightActiveState()
    {
        if (sunLight != null)
            sunLight.enabled = sunActive || sunFade > 0f;

        if (moonLight != null)
        {
            bool moonLightVisible = moonActive || moonFade > 0f;
            moonLight.gameObject.SetActive(moonLightVisible);
            moonLight.enabled = moonLightVisible;
        }
    }

    private void ApplyMoonVisualActiveState()
    {
        if (!Application.isPlaying || moonModel == null)
            return;

        moonModel.gameObject.SetActive(moonActive || moonFade > 0f);
    }

    private void UpdateAmbientLight()
    {
        float dayBlend = GetDayAmbientBlend(currentTime);
        float ambientIntensity = Mathf.Lerp(nightAmbientIntensity, dayAmbientIntensity, dayBlend);
        Color ambientColor = Color.Lerp(nightAmbientColor, dayAmbientColor, dayBlend);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor * ambientIntensity;
    }

    private static float GetDayAmbientBlend(float hour)
    {
        const float dawnStart = 5.5f;
        const float fullDayStart = 8f;
        const float fullDayEnd = 16f;
        const float duskEnd = 18.5f;

        if (hour < dawnStart || hour > duskEnd)
            return 0f;

        if (hour < fullDayStart)
            return Mathf.InverseLerp(dawnStart, fullDayStart, hour);

        if (hour > fullDayEnd)
            return 1f - Mathf.InverseLerp(fullDayEnd, duskEnd, hour);

        return 1f;
    }

    private static bool IsNightTime(float hour)
    {
        return hour >= 18.5f || hour <= 5.5f;
    }

    private void ApplyMoonVisualMaterial()
    {
        if (moonModel == null)
            return;

        EnsureMoonTexture();

        Renderer moonRenderer = moonModel.GetComponent<Renderer>();
        if (moonRenderer == null)
            return;

        // Try shaders in priority order — pick the first one that exists
        Shader moonShader = Shader.Find("Custom/MoonUnlit");
        if (moonShader == null) moonShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (moonShader == null) moonShader = Shader.Find("Unlit/Color");
        if (moonShader == null) moonShader = Shader.Find("Standard");

        if (moonShader == null)
        {
            Debug.LogWarning("[cyclemanager] Could not find any supported shader for moon visual.");
            return;
        }

        Material moonMaterial = moonMaterialOverride;
        if (moonMaterialOverride != null)
        {
            moonRenderer.sharedMaterial = moonMaterialOverride;
        }
        else
        {
            moonMaterial = moonRenderer.sharedMaterial;
            if (moonMaterial == null || moonMaterial.name != "Generated Moon Material")
            {
                moonMaterial = new Material(moonShader);
                moonMaterial.name = "Generated Moon Material";
                moonRenderer.sharedMaterial = moonMaterial;
            }
            else if (moonMaterial.shader != moonShader)
            {
                moonMaterial.shader = moonShader;
            }
        }

        // Render after skybox (Background=1000) but before transparent geometry
        moonMaterial.renderQueue = 2501;

        Color moonColor = moonActive
            ? new Color(0.95f, 0.94f, 0.82f, 1f)
            : new Color(0.55f, 0.56f, 0.52f, 1f);

        Color glowColor = new Color(0.85f, 0.88f, 1f, 1f);

        // Set every color property the shader might use
        if (moonMaterial.HasProperty("_Color"))
            moonMaterial.SetColor("_Color", moonColor);
        if (moonMaterial.HasProperty("_BaseColor"))
            moonMaterial.SetColor("_BaseColor", moonColor);
        if (moonMaterial.HasProperty("_BaseMap"))
            moonMaterial.SetTexture("_BaseMap", moonTexture);
        if (moonMaterial.HasProperty("_MainTex"))
            moonMaterial.SetTexture("_MainTex", moonTexture);
        moonMaterial.mainTexture = moonTexture;

        // Emission for glow
        if (moonMaterial.HasProperty("_EmissionColor"))
        {
            moonMaterial.EnableKeyword("_EMISSION");
            moonMaterial.SetColor("_EmissionColor", glowColor * 1.5f);
            moonMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        // Surface type = Opaque for URP shaders
        if (moonMaterial.HasProperty("_Surface"))
            moonMaterial.SetFloat("_Surface", 0f); // 0 = Opaque

        // Custom shader properties (if using Custom/MoonUnlit)
        if (moonMaterial.HasProperty("_GlowColor"))
            moonMaterial.SetColor("_GlowColor", glowColor);
        if (moonMaterial.HasProperty("_Phase"))
            moonMaterial.SetFloat("_Phase", ((lunarDay - 1f) / 29f) * 2f - 1f);
        if (moonMaterial.HasProperty("_Visibility"))
        {
            float baseVisibility = moonActive ? 1f : 0.2f;
            moonMaterial.SetFloat("_Visibility", baseVisibility * moonFade);
        }
    }

    private void EnsureMoonTexture()
    {
        if (moonTexture != null)
            return;

#if UNITY_EDITOR
        moonTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Day & Night Cycle/Textures/Moon 1.png");
#endif
    }

}
