using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Handles the initial game start fade-in effect.
/// Mirrors the logic from SleepSystem.cs for consistency.
/// </summary>
public class StartFadeManager : MonoBehaviour
{
    [Header("Fade Settings")]
    public float FadeDuration = 6.0f;
    public Color FadeColor = Color.black;

    private GameObject _fadeCanvasObj;
    private Image _fadeImage;

    void Start()
    {
        CreateFadeOverlay();
        StartCoroutine(FadeInSequence());
    }

    private void CreateFadeOverlay()
    {
        // Create a dedicated screen-space overlay canvas for the fade
        _fadeCanvasObj = new GameObject("StartFadeCanvas");
        Canvas canvas = _fadeCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // Stay on top of everything

        // Optional: Add CanvasScaler to ensure it covers screen correctly across resolutions
        _fadeCanvasObj.AddComponent<CanvasScaler>();

        // Full-screen image
        GameObject imgObj = new GameObject("FadeImage");
        imgObj.transform.SetParent(_fadeCanvasObj.transform, false);
        _fadeImage = imgObj.AddComponent<Image>();
        _fadeImage.color = FadeColor; // Start with full opacity
        _fadeImage.raycastTarget = false;

        // Stretch to fill screen
        RectTransform rt = _fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private IEnumerator FadeInSequence()
    {
        float elapsed = 0f;
        while (elapsed < FadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / FadeDuration);
            SetOverlayAlpha(alpha);
            yield return null;
        }

        SetOverlayAlpha(0f);
        
        // Clean up
        if (_fadeCanvasObj != null) Destroy(_fadeCanvasObj);
        Destroy(this);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (_fadeImage != null)
        {
            Color c = _fadeImage.color;
            c.a = alpha;
            _fadeImage.color = c;
        }
    }
}
