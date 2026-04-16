using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dynamic Canvas-based HUD for Player Stats (Single Icon Version).
/// Manages Health, Hunger, Thirst, and Temperature fills.
/// </summary>
public class PlayerStatsUI : MonoBehaviour
{
    [Header("UI Fill Images (Assign these in Inspector!)")]
    public Image HealthFill;
    public Image HungerFill;
    public Image ThirstFill;
    public Image TempFill;
    public Image SleepFill;

    [Header("Temperature Display")]
    public TextMeshProUGUI TempTextDisplay;

    private DayNightCycle _cycle;
    private PlayerStats _stats;

    private void Start()
    {
        _stats = PlayerStats.Instance;
        _cycle = FindFirstObjectByType<DayNightCycle>();

        Debug.Log($"<color=cyan>[StatsUI]</color> HUD with single-icon mode initialized.");
    }

    private void Update()
    {
        if (_stats == null) _stats = PlayerStats.Instance;
        if (_stats == null) return;

        // Update fill amounts
        UpdateFillAmount(HealthFill, _stats.CurrentHealth, _stats.MaxHealth);
        UpdateFillAmount(HungerFill, _stats.CurrentHunger, _stats.MaxHunger);
        UpdateFillAmount(ThirstFill, _stats.CurrentThirst, _stats.MaxThirst);
        UpdateFillAmount(SleepFill, _stats.CurrentSleep, _stats.MaxSleep);
        
        float tempVal = (_cycle != null) ? _cycle.CurrentTemperature : 25f;
        UpdateFillAmount(TempFill, tempVal, 50f); // Assuming 50C is max for the bar

        UpdateTemperatureText(tempVal);
    }

    private void UpdateFillAmount(Image fillImage, float current, float max)
    {
        if (fillImage == null || max <= 0) return;

        float targetFill = Mathf.Clamp01(current / max);
        fillImage.fillAmount = targetFill;
    }

    private void UpdateTemperatureText(float temp)
    {
        if (TempTextDisplay == null) return;

        bool shade = (_stats != null && _stats.IsInShadow);
        float roundedTemp = Mathf.Round(temp * 10f) / 10f;
        
        string shadeText = shade ? " (SHADE)" : "";
        TempTextDisplay.text = $"{roundedTemp:F1}°C{shadeText}";

        // Color feedback
        if (temp > 40f) TempTextDisplay.color = new Color(1f, 0.3f, 0.1f);
        else if (temp < 10f) TempTextDisplay.color = new Color(0.5f, 0.8f, 1f);
        else TempTextDisplay.color = Color.white;
    }
}
