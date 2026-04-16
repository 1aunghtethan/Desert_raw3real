using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Cycle Settings")]
    [Tooltip("Speed of the day-night cycle (degrees per second). 6 = full cycle in 60s.")]
    public float CycleSpeed = 6.0f;
    
    [Header("Light Settings")]
    public float MaxIntensity = 1.3f;
    public float MinIntensity = 0.05f;
    public Gradient LightColor;
    public Gradient AmbientColor;
    public Gradient SkyTint;
    
    [Header("Current State")]
    [Range(0, 360)]
    public float TimeOfDay = 0f;
    
    [Header("Temperature Settings (Real Desert: 0C - 50C)")]
    public float MinTemperature = 0f;
    public float MaxTemperature = 50f;
    public float CurrentTemperature { get; private set; }

    /// <summary>True when the sun is above the horizon (TimeOfDay 0-180).</summary>
    public bool IsDay => TimeOfDay > 0f && TimeOfDay < 180f;

    private Light _sun;

    void Start()
    {
        _sun = GetComponent<Light>();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        
        // Initialize gradients if they look unassigned (all white)
        InitializeGradients();
    }

    void OnValidate()
    {
        // Allow live preview in editor if needed, but careful with RenderSettings in OnValidate
    }

    private void Reset()
    {
        InitializeGradients();
    }

    private void InitializeGradients()
    {
        if (IsGradientEmpty(LightColor))
        {
            LightColor = CreateGradient(new Color(1f, 0.4f, 0.2f), Color.white, new Color(1f, 0.4f, 0.2f));
        }
        if (IsGradientEmpty(AmbientColor))
        {
            AmbientColor = CreateGradient(new Color(0.1f, 0.1f, 0.2f), new Color(0.5f, 0.5f, 0.6f), new Color(0.1f, 0.1f, 0.2f));
        }
        if (IsGradientEmpty(SkyTint))
        {
            SkyTint = CreateGradient(new Color(0.1f, 0.1f, 0.2f), new Color(0.5f, 0.7f, 1f), new Color(0.1f, 0.1f, 0.2f), Color.black);
        }
    }

    private bool IsGradientEmpty(Gradient g)
    {
        if (g == null || g.colorKeys.Length <= 2) 
        {
            // Check if it's just the default white gradient
            if (g != null && g.colorKeys.Length > 0 && g.colorKeys[0].color == Color.white && g.colorKeys[g.colorKeys.Length-1].color == Color.white)
                return true;
        }
        return g == null || g.colorKeys.Length == 0;
    }

    private Gradient CreateGradient(Color start, Color mid, Color end, Color? night = null)
    {
        Gradient g = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[night.HasValue ? 4 : 3];
        colorKeys[0] = new GradientColorKey(start, 0.2f); // Sunrise
        colorKeys[1] = new GradientColorKey(mid, 0.5f);   // Noon
        colorKeys[2] = new GradientColorKey(end, 0.8f);   // Sunset
        if (night.HasValue)
        {
            colorKeys[3] = new GradientColorKey(night.Value, 0.0f); // Night (loops around)
        }
        
        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(1f, 1f);
        
        g.SetKeys(colorKeys, alphaKeys);
        return g;
    }

    void Update()
    {
        // Advance time
        TimeOfDay += CycleSpeed * Time.deltaTime;
        if (TimeOfDay >= 360f) TimeOfDay = 0f;

        // Rotate Sun (X-axis)
        float xRotation = TimeOfDay;
        transform.rotation = Quaternion.Euler(xRotation, -30f, 0f);

        // Normalize time for gradient/intensity (0 to 1)
        float t = TimeOfDay / 360f;
        float angleRad = TimeOfDay * Mathf.Deg2Rad;
        float sunHeight = Mathf.Sin(angleRad); // Positive = Day, Negative = Night

        // Adjust Intensity and Color
        if (_sun != null)
        {
            _sun.intensity = Mathf.Lerp(MinIntensity, MaxIntensity, Mathf.Max(0, sunHeight));
            _sun.color = LightColor.Evaluate(t);
            _sun.enabled = sunHeight > -0.1f;
        }

        // Adjust Ambient Light and Sky
        RenderSettings.ambientLight = AmbientColor.Evaluate(t);

        // Update Temperature (Real desert: 0C at night, 50C at peak day)
        // sunHeight: 1 at noon, 0 at sunrise/sunset, -1 at midnight
        CurrentTemperature = Mathf.Lerp(MinTemperature, MaxTemperature, (sunHeight + 1f) / 2f);
        
        if (RenderSettings.skybox != null)
        {
            Color skyColor = SkyTint.Evaluate(t);
            if (RenderSettings.skybox.HasProperty("_SkyTint"))
                RenderSettings.skybox.SetColor("_SkyTint", skyColor);
            else if (RenderSettings.skybox.HasProperty("_Tint"))
                RenderSettings.skybox.SetColor("_Tint", skyColor);
        }
    }

    /// <summary>
    /// Advance time by the given degrees (used by the sleep system).
    /// </summary>
    public void AdvanceTime(float degrees)
    {
        TimeOfDay += degrees;
        if (TimeOfDay >= 360f) TimeOfDay -= 360f;
    }
}
