using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages underwater visual effects when the camera enters an oasis.
/// Applies blue fog and a screen-wide color tint overlay.
/// </summary>
public class UnderwaterEffectManager : MonoBehaviour
{
    [Header("Underwater Settings")]
    public Color UnderwaterFogColor = new Color(0.12f, 0.35f, 0.55f, 1f); // Deep Cyan/Blue
    public float UnderwaterFogDensity = 0.18f;
    public FogMode UnderwaterFogMode = FogMode.Exponential;
    
    [Header("Transition Settings")]
    public float TransitionSpeed = 8.0f;
    [Tooltip("How deep the camera must be for 100% fog/tint effect.")]
    public float FullSubmersionDepth = 0.3f;
    [Tooltip("Start fading in slightly before hitting the exact surface for smoothness.")]
    public float SurfaceBuffer = 0.05f;

    [Tooltip("If true, a UI image will be created at runtime for a screen-wide tint.")]
    public bool UseScreenOverlay = true;
    public Color OverlayColor = new Color(0.2f, 0.5f, 0.8f, 0.35f); // Slightly more opaque blue

    [Header("Audio (Optional)")]
    public bool UseSoundFilter = true;
    [Range(0, 1)] public float LowPassFrequency = 0.2f;

    // Original settings to restore
    private bool _originalFogEnabled;
    private Color _originalFogColor;
    private float _originalFogDensity;
    private FogMode _originalFogMode;
    private Color _originalAmbientColor;

    private bool _isUnderwater;
    private float _targetSubmersion = 0f;
    private float _currentUnderwaterAlpha = 0f;
    
    private Image _screenOverlayImage;
    private Canvas _overlayCanvas;
    private Camera _currentMainCamera;
    private AudioLowPassFilter _audioFilter;

    void Start()
    {
        // Save initial settings (assuming desert defaults)
        _originalFogEnabled = RenderSettings.fog;
        _originalFogColor = RenderSettings.fogColor;
        _originalFogDensity = RenderSettings.fogDensity;
        _originalFogMode = RenderSettings.fogMode;
        _originalAmbientColor = RenderSettings.ambientLight;

        if (UseScreenOverlay)
        {
            CreateOverlayUI();
        }

        // Try to find or add low pass filter to the player or camera
        SetupAudio();
    }

    void CreateOverlayUI()
    {
        GameObject canvasGo = new GameObject("UnderwaterOverlayCanvas");
        _overlayCanvas = canvasGo.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 999;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        GameObject imageGo = new GameObject("UnderwaterTint");
        imageGo.transform.SetParent(canvasGo.transform);
        _screenOverlayImage = imageGo.AddComponent<Image>();
        _screenOverlayImage.color = new Color(OverlayColor.r, OverlayColor.g, OverlayColor.b, 0f);
        
        RectTransform rt = _screenOverlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        _screenOverlayImage.raycastTarget = false;
    }

    void SetupAudio()
    {
        // Initial setup for current camera
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            _currentMainCamera = mainCam;
            SetupAudioForCamera(mainCam);
        }
    }

    void Update()
    {
        CheckUnderwaterStatus();
        UpdateVisuals();
    }

    void CheckUnderwaterStatus()
    {
        if (TerrainManager.Instance == null) return;

        // Dynamically get the currently active camera tagged as MainCamera
        Camera cam = Camera.main;
        if (cam == null) return;

        // If the main camera changed (e.g. switched FP/TP), update the audio filter reference
        if (cam != _currentMainCamera)
        {
            _currentMainCamera = cam;
            SetupAudioForCamera(cam);
        }

        Vector3 camPos = cam.transform.position;
        Vector2Int chunkCoord = new Vector2Int(
            Mathf.FloorToInt(camPos.x / TerrainManager.Instance.GetChunkSizeWorld()),
            Mathf.FloorToInt(camPos.z / TerrainManager.Instance.GetChunkSizeWorld())
        );

        List<TerrainManager.OasisData> oases = TerrainManager.Instance.GetNearbyOases(chunkCoord);
        
        float maxSubmersion = 0f;
        _isUnderwater = false;

        foreach (var oasis in oases)
        {
            float dist2D = Vector2.Distance(new Vector2(camPos.x, camPos.z), oasis.position);
            // Relax the radius check slightly for the transition
            if (dist2D < oasis.waterRadius + SurfaceBuffer)
            {
                // Calculate water level for this specific oasis
                float centerHeight = TerrainManager.Instance.SampleHeight(new Vector3(oasis.position.x, 0, oasis.position.y));
                float waterLevel = centerHeight + TerrainManager.Instance.Config.OasisWaterHeightOffset;

                // How deep are we? (Positive = underwater)
                float depth = waterLevel - camPos.y;
                
                // Calculate submersion 0..1
                // We start transition at -SurfaceBuffer (above water) and hit 100% at FullSubmersionDepth
                float submersion = Mathf.InverseLerp(-SurfaceBuffer, FullSubmersionDepth, depth);
                
                if (submersion > maxSubmersion)
                {
                    maxSubmersion = submersion;
                    if (depth > 0) _isUnderwater = true;
                }
            }
        }

        _targetSubmersion = maxSubmersion;
    }

    void SetupAudioForCamera(Camera cam)
    {
        if (!UseSoundFilter) return;

        _audioFilter = cam.GetComponent<AudioLowPassFilter>();
        if (_audioFilter == null) _audioFilter = cam.gameObject.AddComponent<AudioLowPassFilter>();
        
        // Ensure default state is clean
        if (_audioFilter != null)
        {
            _audioFilter.enabled = _isUnderwater && _currentUnderwaterAlpha > 0.01f;
            _audioFilter.cutoffFrequency = 22000;
        }
    }

    void UpdateVisuals()
    {
        // Use depth-based submersion but still smooth it over time for a feel-good transition
        _currentUnderwaterAlpha = Mathf.MoveTowards(_currentUnderwaterAlpha, _targetSubmersion, Time.deltaTime * TransitionSpeed);

        if (_currentUnderwaterAlpha > 0.001f)
        {
            // Apply Underwater Effects
            if (!RenderSettings.fog) RenderSettings.fog = true;
            
            // We use the alpha to blend colors and density
            RenderSettings.fogColor = Color.Lerp(_originalFogColor, UnderwaterFogColor, _currentUnderwaterAlpha);
            RenderSettings.fogDensity = Mathf.Lerp(_originalFogDensity, UnderwaterFogDensity, _currentUnderwaterAlpha);
            RenderSettings.fogMode = UnderwaterFogMode;

            if (_screenOverlayImage != null)
            {
                // Ensure overlay is active
                _screenOverlayImage.color = new Color(OverlayColor.r, OverlayColor.g, OverlayColor.b, OverlayColor.a * _currentUnderwaterAlpha);
            }

            if (_audioFilter != null)
            {
                _audioFilter.enabled = true;
                _audioFilter.cutoffFrequency = Mathf.Lerp(22000, 1500, _currentUnderwaterAlpha);
            }
        }
        else
        {
            // Fully out of water, restore original settings
            if (RenderSettings.fog != _originalFogEnabled) RenderSettings.fog = _originalFogEnabled;
            RenderSettings.fogColor = _originalFogColor;
            RenderSettings.fogDensity = _originalFogDensity;
            RenderSettings.fogMode = _originalFogMode;

            if (_screenOverlayImage != null)
            {
                _screenOverlayImage.color = new Color(OverlayColor.r, OverlayColor.g, OverlayColor.b, 0f);
            }

            if (_audioFilter != null)
            {
                _audioFilter.enabled = false;
            }
        }
    }

    private void OnDisable()
    {
        // Safety: Always restore settings if disabled
        RenderSettings.fog = _originalFogEnabled;
        RenderSettings.fogColor = _originalFogColor;
        RenderSettings.fogDensity = _originalFogDensity;
        RenderSettings.fogMode = _originalFogMode;
    }
}
