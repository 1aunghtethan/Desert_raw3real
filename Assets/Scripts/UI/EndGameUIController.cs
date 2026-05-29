using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndGameUIController : MonoBehaviour
{
    public static EndGameUIController Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject survivedPanel;
    [SerializeField] private GameObject diedPanel;

    [Header("Survived Buttons")]
    [SerializeField] private Button survivedReplayButton;
    [SerializeField] private Button survivedReturnHomeButton;

    [Header("Died Buttons")]
    [SerializeField] private Button diedReplayButton;
    [SerializeField] private Button diedReturnHomeButton;

    [Header("Control")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private ThirdPersonCamera thirdPersonCamera;

    [Header("Scenes")]
    [SerializeField] private string introSceneName = "intro";
    [SerializeField] private string gameSceneName = "MainScene";

    private Rigidbody playerBody;

    private void Awake()
    {
        Instance = this;
        ResolveReferences();
        HidePanels();
    }

    private void OnEnable()
    {
        ResolveReferences();
        AddButtonListeners();
    }

    private void OnDisable()
    {
        RemoveButtonListeners();
    }

    public static bool TryShowDied()
    {
        EndGameUIController controller = ResolveInstance();
        if (controller == null)
            return false;

        controller.ShowDiedPanel();
        return true;
    }

    public static bool TryShowSurvived()
    {
        EndGameUIController controller = ResolveInstance();
        if (controller == null)
            return false;

        controller.ShowSurvivedPanel();
        return true;
    }

    public void ShowDiedPanel()
    {
        ShowPanel(diedPanel, survivedPanel);
    }

    public void ShowSurvivedPanel()
    {
        ShowPanel(survivedPanel, diedPanel);
    }

    public void Replay()
    {
        Time.timeScale = 1f;
        WorldSeedManager.GenerateNewSeed();
        SceneManager.LoadScene(gameSceneName);
    }

    public void ReturnHome()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(introSceneName);
    }

    private static EndGameUIController ResolveInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindFirstObjectByType<EndGameUIController>(FindObjectsInactive.Include);
        if (Instance != null)
            return Instance;

        GameObject endGameRoot = GameObject.Find("show UI/survived and died");
        if (endGameRoot == null)
            endGameRoot = GameObject.Find("survived and died");

        if (endGameRoot != null)
            Instance = endGameRoot.AddComponent<EndGameUIController>();

        return Instance;
    }

    private void ResolveReferences()
    {
        if (survivedPanel == null)
            survivedPanel = FindChildGameObject("survived");

        if (diedPanel == null)
            diedPanel = FindChildGameObject("die");

        if (survivedPanel != null)
        {
            if (survivedReplayButton == null)
                survivedReplayButton = FindButton(survivedPanel.transform, "replay");

            if (survivedReturnHomeButton == null)
                survivedReturnHomeButton = FindButton(survivedPanel.transform, "returnHome");
        }

        if (diedPanel != null)
        {
            if (diedReplayButton == null)
                diedReplayButton = FindButton(diedPanel.transform, "replay");

            if (diedReturnHomeButton == null)
                diedReturnHomeButton = FindButton(diedPanel.transform, "returnHome");
        }

        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);

        if (thirdPersonCamera == null)
            thirdPersonCamera = FindFirstObjectByType<ThirdPersonCamera>(FindObjectsInactive.Include);

        if (playerBody == null && playerController != null)
            playerBody = playerController.GetComponent<Rigidbody>();
    }

    private void AddButtonListeners()
    {
        if (survivedReplayButton != null)
            survivedReplayButton.onClick.AddListener(Replay);

        if (diedReplayButton != null)
            diedReplayButton.onClick.AddListener(Replay);

        if (survivedReturnHomeButton != null)
            survivedReturnHomeButton.onClick.AddListener(ReturnHome);

        if (diedReturnHomeButton != null)
            diedReturnHomeButton.onClick.AddListener(ReturnHome);
    }

    private void RemoveButtonListeners()
    {
        if (survivedReplayButton != null)
            survivedReplayButton.onClick.RemoveListener(Replay);

        if (diedReplayButton != null)
            diedReplayButton.onClick.RemoveListener(Replay);

        if (survivedReturnHomeButton != null)
            survivedReturnHomeButton.onClick.RemoveListener(ReturnHome);

        if (diedReturnHomeButton != null)
            diedReturnHomeButton.onClick.RemoveListener(ReturnHome);
    }

    private void ShowPanel(GameObject panelToShow, GameObject panelToHide)
    {
        ResolveReferences();
        Time.timeScale = 1f;

        if (panelToHide != null)
            panelToHide.SetActive(false);

        if (panelToShow != null)
            panelToShow.SetActive(true);

        DisablePlayerControl();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HidePanels()
    {
        if (survivedPanel != null)
            survivedPanel.SetActive(false);

        if (diedPanel != null)
            diedPanel.SetActive(false);
    }

    private void DisablePlayerControl()
    {
        if (playerBody != null)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }

        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = false;

        if (playerController != null)
            playerController.enabled = false;
    }

    private GameObject FindChildGameObject(string childName)
    {
        Transform child = transform.Find(childName);
        if (child != null)
            return child.gameObject;

        foreach (Transform descendant in GetComponentsInChildren<Transform>(true))
        {
            if (descendant.name == childName)
                return descendant.gameObject;
        }

        return null;
    }

    private static Button FindButton(Transform panelRoot, string buttonName)
    {
        foreach (Button button in panelRoot.GetComponentsInChildren<Button>(true))
        {
            if (button.name == buttonName)
                return button;
        }

        return null;
    }
}
