using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD for the current player stats. Auto-binds to the new show UI/state hierarchy.
/// </summary>
public class PlayerStatsUI : MonoBehaviour
{
    private const string StateRootPath = "show UI/state";
    [Header("New State UI Fill Images")]
    public Image HealthFill;
    public Image HungerFill;
    public Image ThirstFill;
    public Image SleepFill;

    [Header("Temperature Display")]
    public TextMeshProUGUI TempTextDisplay;

    [Header("Low Stats Vignette")]
    [Range(0f, 1f)] public float LowStatVignetteThreshold = 0.10f;
    [Range(0f, 1f)] public float LowStatVignetteMaxAlpha = 0.45f;
    public float LowStatVignettePulseSpeed = 2.5f;
    public float LowStatVignetteFadeSpeed = 8f;
    [Range(0f, 1f)] public float LowStatVignetteInnerClearRadius = 0.68f;

    private cyclemanager _cycle;
    private PlayerStats _stats;
    private Canvas _lowStatVignetteCanvas;
    private Image _lowStatVignetteImage;
    private Sprite _lowStatVignetteSprite;
    private Texture2D _lowStatVignetteTexture;
    private float _currentLowStatVignetteAlpha;

    private Text _healthText;
    private Text _hungerText;
    private Text _thirstText;
    private Text _tempText;
    private Text _sleepText;

    private TMP_Text _healthTmpText;
    private TMP_Text _hungerTmpText;
    private TMP_Text _thirstTmpText;
    private TMP_Text _tempTmpText;
    private TMP_Text _sleepTmpText;

    private void Awake()
    {
        BindNewStateUi();
    }

    private void Start()
    {
        _stats = PlayerStats.Instance;
        _cycle = FindFirstObjectByType<cyclemanager>();
        CreateLowStatVignetteOverlay();

        Debug.Log("<color=cyan>[StatsUI]</color> New state HUD initialized.");
    }

    private void Update()
    {
        if (_stats == null)
            _stats = PlayerStats.Instance;

        if (_stats == null)
            return;

        UpdateStatWidget(HealthFill, _healthText, _healthTmpText, _stats.CurrentHealth, _stats.MaxHealth);
        UpdateStatWidget(HungerFill, _hungerText, _hungerTmpText, _stats.CurrentHunger, _stats.MaxHunger);
        UpdateStatWidget(ThirstFill, _thirstText, _thirstTmpText, _stats.CurrentThirst, _stats.MaxThirst);
        UpdateStatWidget(SleepFill, _sleepText, _sleepTmpText, _stats.CurrentSleep, _stats.MaxSleep);

        float tempVal = _stats != null
            ? _stats.CurrentEffectiveTemperature
            : (_cycle != null ? _cycle.CurrentTemperature : 25f);

        UpdateTemperatureWidget(tempVal);
        UpdateLowStatVignette();
    }

    private void BindNewStateUi()
    {
        Transform stateRoot = GameObject.Find(StateRootPath)?.transform;
        if (stateRoot == null)
        {
            Debug.LogWarning("[StatsUI] Could not find '" + StateRootPath + "'. New state HUD will not update.");
            return;
        }

        HealthFill = BindWidget(stateRoot, "heart", out _healthText, out _healthTmpText);
        ThirstFill = BindWidget(stateRoot, "thirsty", out _thirstText, out _thirstTmpText);
        HungerFill = BindWidget(stateRoot, "hunger", out _hungerText, out _hungerTmpText);
        BindTemperatureTextOnly(stateRoot);
        SleepFill = BindWidget(stateRoot, "sleep", out _sleepText, out _sleepTmpText);
    }

    private void BindTemperatureTextOnly(Transform stateRoot)
    {
        _tempText = null;
        _tempTmpText = null;

        Transform widget = stateRoot.Find("temperature");
        if (widget == null)
        {
            Debug.LogWarning("[StatsUI] Missing state widget 'temperature'.");
            return;
        }

        Transform fillTransform = widget.Find("vica");
        if (fillTransform != null)
            fillTransform.gameObject.SetActive(false);

        Transform textTransform = widget.Find("Text");
        if (textTransform == null)
            return;

        _tempText = textTransform.GetComponent<Text>();
        _tempTmpText = textTransform.GetComponent<TMP_Text>();
        if (_tempText != null)
            _tempText.raycastTarget = false;
        if (_tempTmpText != null)
            _tempTmpText.raycastTarget = false;
    }

    private static Image BindWidget(Transform stateRoot, string widgetName, out Text valueText, out TMP_Text valueTmpText)
    {
        valueText = null;
        valueTmpText = null;

        Transform widget = stateRoot.Find(widgetName);
        if (widget == null)
        {
            Debug.LogWarning("[StatsUI] Missing state widget '" + widgetName + "'.");
            return null;
        }

        Transform fillTransform = widget.Find("vica");
        Image fillImage = fillTransform != null ? fillTransform.GetComponent<Image>() : null;
        if (fillImage == null)
        {
            Debug.LogWarning("[StatsUI] Missing fill Image at '" + widgetName + "/vica'.");
        }
        else
        {
            fillImage.type = Image.Type.Filled;
            fillImage.raycastTarget = false;

            loadingtext legacyLoadingText = fillImage.GetComponent<loadingtext>();
            if (legacyLoadingText != null)
                legacyLoadingText.enabled = false;
        }

        Transform textTransform = widget.Find("Text");
        if (textTransform != null)
        {
            valueText = textTransform.GetComponent<Text>();
            valueTmpText = textTransform.GetComponent<TMP_Text>();
            if (valueText != null)
                valueText.raycastTarget = false;
            if (valueTmpText != null)
                valueTmpText.raycastTarget = false;
        }

        return fillImage;
    }

    private static void UpdateStatWidget(Image fillImage, Text valueText, TMP_Text valueTmpText, float current, float max)
    {
        float ratio = GetStatRatio(current, max);
        if (fillImage != null)
            fillImage.fillAmount = ratio;

        SetText(valueText, valueTmpText, string.Format("{0}%", Mathf.RoundToInt(ratio * 100f)));
    }

    private void UpdateTemperatureWidget(float temp)
    {
        float roundedTemp = Mathf.Round(temp * 10f) / 10f;
        string text = string.Format("{0:0.0}\u00B0C", roundedTemp);
        SetText(_tempText, _tempTmpText, text);

        if (TempTextDisplay != null)
        {
            TempTextDisplay.SetText(text);
            ApplyTemperatureColor(TempTextDisplay, temp);
        }

        if (_tempText != null)
            _tempText.color = GetTemperatureColor(temp);
        if (_tempTmpText != null)
            _tempTmpText.color = GetTemperatureColor(temp);
    }

    private static void SetText(Text valueText, TMP_Text valueTmpText, string value)
    {
        if (valueText != null)
            valueText.text = value;
        if (valueTmpText != null)
            valueTmpText.text = value;
    }

    private static void ApplyTemperatureColor(TMP_Text text, float temp)
    {
        if (text != null)
            text.color = GetTemperatureColor(temp);
    }

    private static Color GetTemperatureColor(float temp)
    {
        if (temp > 40f)
            return new Color(1f, 0.3f, 0.1f);
        if (temp < 10f)
            return new Color(0.5f, 0.8f, 1f);

        return Color.white;
    }

    private void CreateLowStatVignetteOverlay()
    {
        if (_lowStatVignetteCanvas != null)
            return;

        GameObject canvasObj = new GameObject("LowStatsVignetteCanvas");
        canvasObj.transform.SetParent(transform, false);
        _lowStatVignetteCanvas = canvasObj.AddComponent<Canvas>();
        _lowStatVignetteCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _lowStatVignetteCanvas.sortingOrder = 950;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        GameObject imageObj = new GameObject("LowStatsRedVignette");
        imageObj.transform.SetParent(canvasObj.transform, false);
        _lowStatVignetteImage = imageObj.AddComponent<Image>();
        _lowStatVignetteImage.sprite = CreateLowStatVignetteSprite();
        _lowStatVignetteImage.color = new Color(1f, 0f, 0f, 0f);
        _lowStatVignetteImage.raycastTarget = false;

        RectTransform rect = _lowStatVignetteImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private Sprite CreateLowStatVignetteSprite()
    {
        const int size = 256;
        _lowStatVignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _lowStatVignetteTexture.name = "Generated Low Stats Red Vignette";
        _lowStatVignetteTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = (y / (size - 1f)) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (size - 1f)) * 2f - 1f;
                float edgeDistance = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LowStatVignetteInnerClearRadius, 1f, edgeDistance));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        _lowStatVignetteTexture.SetPixels(pixels);
        _lowStatVignetteTexture.Apply(false, true);

        _lowStatVignetteSprite = Sprite.Create(
            _lowStatVignetteTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f));

        return _lowStatVignetteSprite;
    }

    private void UpdateLowStatVignette()
    {
        if (_lowStatVignetteImage == null || _stats == null)
            return;

        float lowestRatio = Mathf.Min(
            GetStatRatio(_stats.CurrentHealth, _stats.MaxHealth),
            GetStatRatio(_stats.CurrentHunger, _stats.MaxHunger),
            GetStatRatio(_stats.CurrentThirst, _stats.MaxThirst),
            GetStatRatio(_stats.CurrentSleep, _stats.MaxSleep));

        float threshold = Mathf.Max(0.001f, LowStatVignetteThreshold);
        float danger = lowestRatio < threshold ? 1f - Mathf.Clamp01(lowestRatio / threshold) : 0f;
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.Max(0f, LowStatVignettePulseSpeed) * Mathf.PI * 2f);
        float targetAlpha = danger * Mathf.Clamp01(LowStatVignetteMaxAlpha) * pulse;

        _currentLowStatVignetteAlpha = Mathf.MoveTowards(
            _currentLowStatVignetteAlpha,
            targetAlpha,
            Mathf.Max(0.01f, LowStatVignetteFadeSpeed) * Time.unscaledDeltaTime);

        _lowStatVignetteImage.color = new Color(1f, 0f, 0f, _currentLowStatVignetteAlpha);
    }

    private static float GetStatRatio(float current, float max)
    {
        if (max <= 0f)
            return 1f;

        return Mathf.Clamp01(current / max);
    }

    private void OnDestroy()
    {
        if (_lowStatVignetteSprite != null)
            Destroy(_lowStatVignetteSprite);

        if (_lowStatVignetteTexture != null)
            Destroy(_lowStatVignetteTexture);
    }
}
