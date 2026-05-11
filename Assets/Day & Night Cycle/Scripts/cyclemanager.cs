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

    public AnimationCurve moonIntensityMultiplier;
    public AnimationCurve moonTemperatureCurve;

    [Space]
    [Header("🌕 Lunar Cycle Settings")]
    [Range(1, 30)] public int lunarDay = 1;

    [Tooltip("Speed at which the lunar day changes.")]
    public float lunarCycleSpeed = 1f;

    [Tooltip("Reference to moon visual model.")]
    public Transform moonModel;

    [Tooltip("Target the moon visual orbits around. If empty, the player is found automatically.")]
    public Transform moonOrbitTarget;

    [Tooltip("Distance of the generated moon visual from the orbit target.")]
    public float moonVisualDistance = 85f;

    [Tooltip("Size of the generated moon visual.")]
    public float moonVisualScale = 7.5f;

    [Space]
    [Header("🔁 Status Flags (Read-Only)")]
    public bool isDay = true;
    public bool sunActive = true;
    public bool moonActive = true;

    private Vector3 moonModelBaseScale = Vector3.one;
    private PlayerStats cachedPlayerStats;

    void Start()
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        EnsureMoonSetup();
        InitializeCurves();
        UpdateTimeText();
        CheckShadowStatus();
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
        UpdateLight();
        CheckShadowStatus();
        UpdateMoonPhase(true);
    }

    private void OnValidate()
    {
        InitializeCurves();
        if (currentTime <= 0f && TimeOfDay > 0f)
            currentTime = TimeOfDay / 15f;
        else
            TimeOfDay = Mathf.Repeat(currentTime * 15f, 360f);

        UpdateLight();
        CheckShadowStatus(false);
        UpdateMoonPhase(false);
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
        float moonRotation = moonTimeOfDay;

        sunLight.transform.localRotation = Quaternion.Euler(sunLatitude - 90f, sunLongitude, 0) * Quaternion.Euler(0, sunRotation, 0);
        if (moonLight != null)
        {
            moonLight.transform.localRotation = Quaternion.Euler(sunLatitude - 90f, sunLongitude, 0) * Quaternion.Euler(0, moonRotation, 0);
            UpdateMoonVisualPosition();
        }

        float normalizedTime = currentTime / 24f;
        float sunCurve = sunIntensityMultiplier.Evaluate(normalizedTime);

        sunLight.intensity = sunCurve * sunIntensity;

        if (moonLight != null)
        {
            float moonCurve = moonIntensityMultiplier.Evaluate(moonTimeOfDay / 360f);
            float phaseMultiplier = Mathf.Sin((float)lunarDay / 30f * Mathf.PI);
            moonLight.intensity = moonCurve * moonIntensity * phaseMultiplier;
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
    }

    void CheckShadowStatus(bool applyActiveState = true)
    {
        if (sunLight == null) sunLight = GetComponent<Light>();
        if (sunLight == null) return;

        float t = currentTime;

        isDay = t >= 6f && t <= 18f;

        sunLight.shadows = isDay ? LightShadows.Soft : LightShadows.None;
        if (moonLight != null)
            moonLight.shadows = !isDay ? LightShadows.Soft : LightShadows.None;

        sunActive = t >= 5.7f && t <= 18.3f;
        if (applyActiveState)
            sunLight.enabled = sunActive;

        moonActive = !(t >= 6.3f && t <= 17.7f);
        if (applyActiveState && moonLight != null)
            moonLight.enabled = moonActive;

        if (applyActiveState && Application.isPlaying && moonModel != null)
            moonModel.gameObject.SetActive(moonActive);
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
        Vector3 skyDirection = -moonLight.transform.forward;

        moonModel.position = center + skyDirection.normalized * moonVisualDistance;
        moonModel.rotation = Quaternion.LookRotation(moonModel.position - center, Vector3.up);
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

            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.68f, 0.78f, 1f, 1f);
            moonLight.intensity = moonIntensity;
            moonLight.shadows = LightShadows.Soft;
        }

        if (moonModel == null && moonLight != null)
        {
            Transform existingMoonVisual = moonLight.transform.Find("MoonVisual");
            if (existingMoonVisual != null)
            {
                moonModel = existingMoonVisual;
            }
            else
            {
                GameObject moonVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                moonVisual.name = "MoonVisual";
                moonVisual.transform.SetParent(null, true);

                Collider moonCollider = moonVisual.GetComponent<Collider>();
                if (moonCollider != null)
                    if (Application.isPlaying)
                        Destroy(moonCollider);
                    else
                        DestroyImmediate(moonCollider);

                moonModel = moonVisual.transform;
            }
        }
        else if (moonModel != null && moonModel.parent == moonLight.transform)
        {
            moonModel.SetParent(null, true);
        }

        if (moonModel != null)
        {
            moonModelBaseScale = Vector3.one * moonVisualScale;
            moonModel.localScale = moonModelBaseScale;
            ApplyMoonVisualMaterial();
            UpdateMoonVisualPosition();
        }
    }

    private void ApplyMoonVisualMaterial()
    {
        if (moonModel == null)
            return;

        Renderer moonRenderer = moonModel.GetComponent<Renderer>();
        if (moonRenderer == null)
            return;

        Shader moonShader = Shader.Find("Custom/MoonUnlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (moonShader == null)
        {
            Debug.LogWarning("[cyclemanager] Could not find a supported moon shader. Moon visual material was not changed.");
            return;
        }
        Material moonMaterial = moonRenderer.sharedMaterial;
        if (moonMaterial == null || moonMaterial.name != "Generated Moon Material")
        {
            moonMaterial = new Material(moonShader);
            moonMaterial.name = "Generated Moon Material";
            moonRenderer.material = moonMaterial;
        }
        else if (moonMaterial.shader != moonShader)
        {
            moonMaterial.shader = moonShader;
        }

        Color moonColor = moonActive ? new Color(0.95f, 0.94f, 0.82f, 1f) : new Color(0.55f, 0.56f, 0.52f, 1f);
        moonMaterial.color = moonColor;
        if (moonMaterial.HasProperty("_BaseColor"))
            moonMaterial.SetColor("_BaseColor", moonColor);
        if (moonMaterial.HasProperty("_GlowColor"))
            moonMaterial.SetColor("_GlowColor", new Color(0.55f, 0.67f, 1f, 1f));
        if (moonMaterial.HasProperty("_Phase"))
            moonMaterial.SetFloat("_Phase", ((lunarDay - 1f) / 29f) * 2f - 1f);
        if (moonMaterial.HasProperty("_Visibility"))
            moonMaterial.SetFloat("_Visibility", moonActive ? 1f : 0.2f);
        if (moonMaterial.HasProperty("_Color"))
            moonMaterial.SetColor("_Color", moonColor);
        if (moonMaterial.HasProperty("_EmissionColor"))
        {
            moonMaterial.EnableKeyword("_EMISSION");
            moonMaterial.SetColor("_EmissionColor", moonColor);
        }
    }
}
