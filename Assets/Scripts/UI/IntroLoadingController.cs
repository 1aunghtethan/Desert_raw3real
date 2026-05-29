using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class IntroLoadingController : MonoBehaviour
{
    [Header("Loading")]
    [SerializeField] private Button playButton;
    [SerializeField] private GameObject loadingOverlay;
    [SerializeField] private Image loadingFillImage;
    [SerializeField] private GameObject loadingIndicatorPrefab;
    [SerializeField] private string targetSceneName = "MainScene";
    [SerializeField] private float minimumLoadingSeconds = 1.5f;

    [Header("Auto Setup")]
    [SerializeField] private string playButtonObjectName = "Button_Square_LightGray";
    [SerializeField, Range(0f, 1f)] private float playButtonBackgroundAlpha = 0f;
    [SerializeField] private string loadingBackgroundResourcePath = "Icons/Untitled design (1)";
    [SerializeField] private Sprite loadingBackgroundSprite;
    [SerializeField] private Sprite loading1Sprite;

    private Text loadingText;
    private GameObject loadingIndicatorInstance;
    private IntroMusicController introMusicController;
    private bool isLoading;

    private void Awake()
    {
        EnsureUiInput();
        ResolvePlayButton();
        ResolveLoadingOverlay(false);
        EnsureIntroMusic();

        if (loadingOverlay != null)
        {
            loadingOverlay.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(BeginLoading);
        }
    }

    private void OnDisable()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(BeginLoading);
        }
    }

    public void BeginLoading()
    {
        if (isLoading)
        {
            return;
        }

        WorldSeedManager.GenerateNewSeed();
        if (introMusicController != null)
            introMusicController.FadeOutAndStop();

        StartCoroutine(LoadTargetScene());
    }

    private IEnumerator LoadTargetScene()
    {
        isLoading = true;
        ResolveLoadingOverlay(true);

        if (playButton != null)
        {
            playButton.interactable = false;
        }

        if (loadingOverlay != null)
        {
            loadingOverlay.SetActive(true);
        }

        SetProgress(0f);
        AsyncOperation operation = SceneManager.LoadSceneAsync(targetSceneName);

        if (operation == null)
        {
            SetLoadingText("Scene not found");
            isLoading = false;

            if (playButton != null)
            {
                playButton.interactable = true;
            }

            yield break;
        }

        operation.allowSceneActivation = false;

        float elapsed = 0f;
        float minimumDuration = Mathf.Max(0.01f, minimumLoadingSeconds);

        while (elapsed < minimumDuration || operation.progress < 0.9f)
        {
            elapsed += Time.unscaledDeltaTime;
            float timedProgress = Mathf.Clamp01(elapsed / minimumDuration);
            float loadProgress = Mathf.Clamp01(operation.progress / 0.9f);
            SetProgress(Mathf.Min(1f, Mathf.Max(timedProgress, loadProgress)));
            yield return null;
        }

        SetProgress(1f);
        yield return new WaitForSecondsRealtime(0.15f);
        operation.allowSceneActivation = true;
    }

    private void ResolvePlayButton()
    {
        if (playButton == null)
        {
            GameObject playButtonObject = GameObject.Find(playButtonObjectName);
            if (playButtonObject != null)
            {
                playButton = playButtonObject.GetComponent<Button>();

                if (playButton == null)
                {
                    playButton = playButtonObject.AddComponent<Button>();
                }
            }
        }

        if (playButton == null)
        {
            return;
        }

        playButton.interactable = true;

        Image targetGraphic = FindButtonBackground(playButton.transform);
        if (targetGraphic != null)
        {
            Color color = targetGraphic.color;
            color.a = Mathf.Clamp01(playButtonBackgroundAlpha);
            targetGraphic.color = color;
            targetGraphic.raycastTarget = true;
            playButton.targetGraphic = targetGraphic;
        }
    }

    private void EnsureUiInput()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();

        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }
        else if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    private void EnsureIntroMusic()
    {
        introMusicController = FindFirstObjectByType<IntroMusicController>();
        if (introMusicController == null)
            introMusicController = gameObject.AddComponent<IntroMusicController>();
    }

    private void ResolveLoadingOverlay(bool createIfMissing)
    {
        if (loadingOverlay != null && loadingFillImage != null)
        {
            EnsureLoadingIndicatorInstance();
            loadingText = loadingOverlay.GetComponentInChildren<Text>(true);
            return;
        }

        Transform existingOverlay = transform.Find("LoadingOverlay");
        if (existingOverlay != null)
        {
            loadingOverlay = existingOverlay.gameObject;
            loadingFillImage = existingOverlay.Find("loading1")?.GetComponent<Image>()
                ?? existingOverlay.Find("LoadingBarTrack/LoadingBarFill")?.GetComponent<Image>();
            EnsureLoadingIndicatorInstance();
            loadingText = existingOverlay.GetComponentInChildren<Text>(true);
            return;
        }

        if (!createIfMissing)
        {
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
        }

        if (canvas == null)
        {
            return;
        }

        CreateLoadingOverlay(canvas.transform);
    }

    private void CreateLoadingOverlay(Transform parent)
    {
        loadingOverlay = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        loadingOverlay.transform.SetParent(parent, false);
        loadingOverlay.SetActive(false);

        RectTransform overlayRect = loadingOverlay.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        Image backgroundImage = loadingOverlay.GetComponent<Image>();
        backgroundImage.sprite = loadingBackgroundSprite != null
            ? loadingBackgroundSprite
            : Resources.Load<Sprite>(loadingBackgroundResourcePath);
        backgroundImage.color = Color.white;
        backgroundImage.raycastTarget = true;

        GameObject scrimObject = new GameObject("LoadingScrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        scrimObject.transform.SetParent(loadingOverlay.transform, false);
        StretchToParent(scrimObject.GetComponent<RectTransform>());
        Image scrimImage = scrimObject.GetComponent<Image>();
        scrimImage.color = new Color(0f, 0f, 0f, 0.28f);
        scrimImage.raycastTarget = false;

        if (EnsureLoadingIndicatorInstance())
            return;

        GameObject fillObject = new GameObject("loading1", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(loadingOverlay.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.5f, 0f);
        fillRect.anchorMax = new Vector2(0.5f, 0f);
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = new Vector2(0f, 96f);
        fillRect.sizeDelta = new Vector2(110f, 110f);

        loadingFillImage = fillObject.GetComponent<Image>();
        loadingFillImage.sprite = loading1Sprite;
        loadingFillImage.color = new Color(1f, 0.24f, 0.14f, 1f);
        loadingFillImage.type = Image.Type.Filled;
        loadingFillImage.fillMethod = Image.FillMethod.Radial360;
        loadingFillImage.fillOrigin = (int)Image.Origin360.Top;
        loadingFillImage.fillClockwise = true;
        loadingFillImage.fillAmount = 0f;
        loadingFillImage.raycastTarget = false;

        GameObject textObject = new GameObject("LoadingText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(loadingOverlay.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0f);
        textRect.anchorMax = new Vector2(0.5f, 0f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(0f, 24f);
        textRect.sizeDelta = new Vector2(360f, 34f);

        loadingText = textObject.GetComponent<Text>();
        loadingText.alignment = TextAnchor.MiddleCenter;
        loadingText.color = Color.white;
        loadingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        loadingText.fontSize = 25;
        loadingText.raycastTarget = false;
        SetLoadingText("Loading...");
    }

    private bool EnsureLoadingIndicatorInstance()
    {
        if (loadingOverlay == null || loadingIndicatorPrefab == null)
            return false;

        HideLegacyLoadingFill();

        if (loadingIndicatorInstance == null)
        {
            Transform existingIndicator = loadingOverlay.transform.Find(loadingIndicatorPrefab.name);
            if (existingIndicator != null)
                loadingIndicatorInstance = existingIndicator.gameObject;
        }

        if (loadingIndicatorInstance == null)
            loadingIndicatorInstance = Instantiate(loadingIndicatorPrefab, loadingOverlay.transform, false);

        RectTransform rectTransform = loadingIndicatorInstance.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0f, 104f);
            rectTransform.sizeDelta = new Vector2(110f, 110f);
        }

        foreach (Graphic graphic in loadingIndicatorInstance.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        loadingIndicatorInstance.SetActive(true);
        loadingFillImage = null;
        loadingText = loadingIndicatorInstance.GetComponentInChildren<Text>(true);
        return true;
    }

    private void HideLegacyLoadingFill()
    {
        Transform legacyFill = loadingOverlay.transform.Find("loading1");
        if (legacyFill != null && (loadingIndicatorPrefab == null || legacyFill.name != loadingIndicatorPrefab.name))
            legacyFill.gameObject.SetActive(false);

        Transform legacyTrack = loadingOverlay.transform.Find("LoadingBarTrack");
        if (legacyTrack != null)
            legacyTrack.gameObject.SetActive(false);
    }

    private static Image FindButtonBackground(Transform root)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "Bg")
            {
                return image;
            }
        }

        return root.GetComponentInChildren<Image>(true);
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private void SetProgress(float value)
    {
        if (loadingFillImage != null)
        {
            loadingFillImage.fillAmount = Mathf.Clamp01(value);
        }
    }

    private void SetLoadingText(string value)
    {
        if (loadingText != null)
        {
            loadingText.text = value;
        }
    }
}
