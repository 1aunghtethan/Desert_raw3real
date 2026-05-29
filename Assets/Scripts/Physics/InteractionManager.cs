using UnityEngine;
using GinjaGaming.FinalCharacterController;
using System.Collections;
using UnityEngine.UI;

/// <summary>
/// Handles player interaction with world objects (Meat, Water, etc.).
/// Performs a raycast through the visible UI cursor to detect LootItem components.
/// Shows a UI prompt and handles left-click pickup.
/// </summary>
public class InteractionManager : MonoBehaviour
{
    [Header("Settings")]
    public float InteractionRange = 4.0f;
    public LayerMask InteractionLayers = ~0; // Everything by default

    [Header("Visuals")]
    public Color PromptColor = Color.white;
    public int FontSize = 22;
    public RectTransform CursorRectTransform;
    public float CursorSphereRadius = 0.3f;

    [Header("Pickup Animation")]
    public float PickupGatherDuration = 1.2f;
    public float PickupActionDelay = 0.6f;
    public float PickupMovementMultiplier = 0.25f;
    public float PickupMovementSlowDuration = 1.2f;

    private LootItem m_CurrentTarget;
    private LootItem m_PreviousOutlineTarget;
    private OutlineController m_CurrentOutline;
    private Camera m_MainCamera;
    private PlayerController m_Controller;
    private PlayerActionsInput m_PlayerActionsInput;
    private ItemPlacer m_ItemPlacer;
    private InventoryPanelUI m_InventoryPanel;
    private CanvasGroup m_InventoryPanelCanvasGroup;
    private Animator m_Animator;
    private Coroutine m_PickupRoutine;
    private float m_PickupMovementSlowUntil;
    private int m_ActivePickupAnimationLayer = -1;
    private int m_ActivePickupAnimationStateHash;
    private float m_ActivePickupAnimationLength = 1f;
    private float m_PrePickupUpperBodyLayerWeight;
    private float m_PrePickupArmsOnlyLayerWeight;
    private bool m_PickupMutedOverlayLayers;

    // Hash for the Gathering state name (used with CrossFadeInFixedTime)
    private static readonly int GatheringStateHash = Animator.StringToHash("Gathering");
    private static readonly int NewPickupStateHash = Animator.StringToHash("newPickup");
    private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");
    private static readonly int ArmsOnlyIdleStateHash = Animator.StringToHash("ArmsOnlyIdle");
    private static readonly int IsGatheringHash = Animator.StringToHash("isGathering");
    private const int BaseLayerIndex = 0;
    private const int UpperBodyLayerIndex = 1;
    private const int ArmsOnlyLayerIndex = 2;

    public float MovementSpeedMultiplier => Time.time < m_PickupMovementSlowUntil ? Mathf.Clamp01(PickupMovementMultiplier) : 1f;
    public bool HasCursorPickupTarget => m_CurrentTarget != null;
    public bool IsPickupInProgress => m_PickupRoutine != null;

    private void Start()
    {
        m_Controller = GetComponent<PlayerController>();
        m_ItemPlacer = GetComponent<ItemPlacer>();
        m_InventoryPanel = FindFirstObjectByType<InventoryPanelUI>(FindObjectsInactive.Include);
        if (m_InventoryPanel != null)
            m_InventoryPanelCanvasGroup = m_InventoryPanel.GetComponent<CanvasGroup>();

        // Search in children too, in case PlayerActionsInput is on a child object
        m_PlayerActionsInput = GetComponent<PlayerActionsInput>();
        if (m_PlayerActionsInput == null)
            m_PlayerActionsInput = GetComponentInChildren<PlayerActionsInput>();

        // Find the Animator that has the isGathering parameter (via PlayerAnimation's serialized reference)
        FindCorrectAnimator();
        ResolveCursorRectTransform();
        DisableCursorRaycastTargets();

        UpdateCameraReference();
    }

    private void FindCorrectAnimator()
    {
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
        {
            if (IsPickupAnimator(animator))
            {
                m_Animator = animator;
                return;
            }
        }

        // Try to find the Animator used by PlayerAnimation (which is serialized to the correct one)
        var playerAnim = GetComponentInChildren<GinjaGaming.FinalCharacterController.PlayerAnimation>();
        if (playerAnim != null)
        {
            // PlayerAnimation has a [SerializeField] Animator - get it via reflection or just find the Animator on the same object
            m_Animator = playerAnim.GetComponent<Animator>();
            if (m_Animator == null)
                m_Animator = playerAnim.GetComponentInChildren<Animator>();
        }

        // Fallback: find any Animator in children
        if (m_Animator == null)
            m_Animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        // Fetch camera every frame to ensure interaction follows orientation
        UpdateCameraReference();
        if (m_MainCamera == null) return;

        if (IsInventoryPanelOpen())
        {
            ClearCurrentTarget();
            return;
        }

        // Restore: Detect EVERY frame for maximum responsiveness ("one frame")
        PerformDetection();

        if (Input.GetMouseButtonDown(0))
            TryStartCursorPickup();

        UpdateOutline(m_CurrentTarget);
    }

    public bool TryStartCursorPickup()
    {
        if (m_PickupRoutine != null)
        {
            AudioManager.Current?.StopFootsteps();
            return true;
        }

        if (m_ItemPlacer != null && m_ItemPlacer.IsPlacementModeActive)
            return false;

        if (IsInventoryPanelOpen())
            return false;

        UpdateCameraReference();
        if (m_MainCamera == null)
            return false;

        PerformDetection();
        if (m_CurrentTarget == null)
            return false;

        m_PickupRoutine = StartCoroutine(PickupAfterGathering(m_CurrentTarget));
        return true;
    }

    private void PerformDetection()
    {
        Ray ray = m_MainCamera.ScreenPointToRay(GetCursorScreenPoint());
        float maxDist = InteractionRange + 20f;

        // Restore: Use the user-configured layers instead of forcing "Item"
        int layerMask = InteractionLayers.value;

        // Restore: Simple, responsive SphereCastAll for easier targeting
        m_CurrentTarget = null;
        RaycastHit[] hits = Physics.SphereCastAll(ray, CursorSphereRadius, maxDist, layerMask);
        
        if (hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (CheckHit(hit)) break;
            }
        }
    }

    private bool CheckHit(RaycastHit hit)
    {
        if (hit.collider.transform.root == transform.root) return false;

        LootItem item = hit.collider.GetComponentInParent<LootItem>();
        if (item == null) item = hit.collider.GetComponentInChildren<LootItem>();

        if (item != null)
        {
            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist <= InteractionRange)
            {
                m_CurrentTarget = item;
                return true;
            }
        }
        return false;
    }

    private void ClearCurrentTarget()
    {
        m_CurrentTarget = null;
        UpdateOutline(null);
    }

    public Vector2 GetCursorScreenPoint()
    {
        if (CursorRectTransform == null)
            ResolveCursorRectTransform();

        if (CursorRectTransform == null)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Canvas canvas = CursorRectTransform.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera;

        return RectTransformUtility.WorldToScreenPoint(uiCamera, CursorRectTransform.position);
    }

    public Ray GetCursorWorldRay(Camera sourceCamera = null)
    {
        Camera rayCamera = sourceCamera;
        if (rayCamera == null)
        {
            UpdateCameraReference();
            rayCamera = m_MainCamera;
        }

        if (rayCamera == null)
            rayCamera = Camera.main;

        if (rayCamera == null)
            return new Ray(transform.position, transform.forward);

        return rayCamera.ScreenPointToRay(GetCursorScreenPoint());
    }

    private void ResolveCursorRectTransform()
    {
        if (CursorRectTransform != null)
            return;

        GameObject cursorImage = GameObject.Find("show UI/cursor/Image");
        if (cursorImage == null)
        {
            GameObject cursorRoot = GameObject.Find("show UI/cursor");
            if (cursorRoot != null)
            {
                Transform imageChild = cursorRoot.transform.Find("Image");
                cursorImage = imageChild != null ? imageChild.gameObject : cursorRoot;
            }
        }

        if (cursorImage != null)
            CursorRectTransform = cursorImage.GetComponent<RectTransform>();
    }

    private void DisableCursorRaycastTargets()
    {
        if (CursorRectTransform == null)
            return;

        Transform root = CursorRectTransform.parent != null && CursorRectTransform.parent.name == "cursor"
            ? CursorRectTransform.parent
            : CursorRectTransform;

        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            image.raycastTarget = false;
        }
    }

    private bool IsInventoryPanelOpen()
    {
        if (m_InventoryPanel == null)
        {
            m_InventoryPanel = FindFirstObjectByType<InventoryPanelUI>(FindObjectsInactive.Include);
            m_InventoryPanelCanvasGroup = m_InventoryPanel != null ? m_InventoryPanel.GetComponent<CanvasGroup>() : null;
        }

        if (m_InventoryPanel == null)
            return false;

        if (m_InventoryPanelCanvasGroup != null)
            return m_InventoryPanelCanvasGroup.alpha > 0.5f;

        return m_InventoryPanel.gameObject.activeInHierarchy;
    }

    private void UpdateOutline(LootItem target)
    {
        if (m_PreviousOutlineTarget == target) return;

        // Turn off previous
        if (m_PreviousOutlineTarget != null)
        {
            var ctrl = m_PreviousOutlineTarget.GetComponent<OutlineController>();
            if (ctrl != null) ctrl.ShowOutline(false);
        }

        // Turn on current
        if (target != null)
        {
            var ctrl = target.GetComponent<OutlineController>();
            if (ctrl == null) ctrl = target.gameObject.AddComponent<OutlineController>();
            ctrl.ShowOutline(true);
        }

        m_PreviousOutlineTarget = target;
    }

    private void UpdateCameraReference()
    {
        if (m_Controller != null) m_MainCamera = m_Controller.GetActiveCamera();
        if (m_MainCamera == null) m_MainCamera = Camera.main;
    }

    private void PlayPickupAnimation()
    {
        AudioManager.Current?.StopFootsteps();

        // Method 1: Trigger via PlayerActionsInput pipeline (sets GatherPressed → PlayerAnimation reads it)
        if (m_Animator == null)
            FindCorrectAnimator();

        if (m_Animator != null)
        {
            int pickupLayer = GetPickupAnimationLayer(m_Animator);
            if (pickupLayer >= 0)
            {
                if (pickupLayer == BaseLayerIndex)
                    MutePickupOverlayLayers();

                m_Animator.Play(NewPickupStateHash, pickupLayer, 0f);
                m_Animator.Update(0f);
                m_ActivePickupAnimationLayer = pickupLayer;
                m_ActivePickupAnimationStateHash = NewPickupStateHash;
                m_ActivePickupAnimationLength = GetCurrentStateLength(m_Animator, pickupLayer, PickupGatherDuration);
            }
            else
            {
                if (m_PlayerActionsInput == null)
                {
                    m_PlayerActionsInput = GetComponent<PlayerActionsInput>();
                    if (m_PlayerActionsInput == null)
                        m_PlayerActionsInput = GetComponentInChildren<PlayerActionsInput>();
                }

                if (m_PlayerActionsInput != null)
                    m_PlayerActionsInput.TriggerGathering(PickupGatherDuration);

                m_Animator.CrossFadeInFixedTime(GatheringStateHash, 0.15f);
                m_ActivePickupAnimationLayer = 0;
                m_ActivePickupAnimationStateHash = GatheringStateHash;
                m_ActivePickupAnimationLength = PickupGatherDuration;
            }
        }
    }

    private void StopPickupAnimation()
    {
        if (m_Animator != null)
        {
            if (m_ActivePickupAnimationLayer == ArmsOnlyLayerIndex && HasAnimatorState(m_Animator, ArmsOnlyLayerIndex, ArmsOnlyIdleStateHash))
                m_Animator.CrossFadeInFixedTime(ArmsOnlyIdleStateHash, 0.15f, ArmsOnlyLayerIndex, 0f);
            else
                m_Animator.CrossFadeInFixedTime(LocomotionStateHash, 0.2f);
        }

        RestorePickupOverlayLayers();
        m_ActivePickupAnimationLayer = -1;
        m_ActivePickupAnimationStateHash = 0;
        m_ActivePickupAnimationLength = 1f;
    }

    private void MutePickupOverlayLayers()
    {
        if (m_Animator == null || m_PickupMutedOverlayLayers)
            return;

        m_PrePickupUpperBodyLayerWeight = GetLayerWeightSafe(m_Animator, UpperBodyLayerIndex);
        m_PrePickupArmsOnlyLayerWeight = GetLayerWeightSafe(m_Animator, ArmsOnlyLayerIndex);

        SetLayerWeightSafe(m_Animator, UpperBodyLayerIndex, 0f);
        SetLayerWeightSafe(m_Animator, ArmsOnlyLayerIndex, 0f);
        m_PickupMutedOverlayLayers = true;
    }

    private void RestorePickupOverlayLayers()
    {
        if (m_Animator == null || !m_PickupMutedOverlayLayers)
            return;

        SetLayerWeightSafe(m_Animator, UpperBodyLayerIndex, m_PrePickupUpperBodyLayerWeight);
        SetLayerWeightSafe(m_Animator, ArmsOnlyLayerIndex, m_PrePickupArmsOnlyLayerWeight);
        m_PickupMutedOverlayLayers = false;
    }

    private IEnumerator PickupAfterGathering(LootItem target)
    {
        PlayPickupAnimation();
        m_PickupMovementSlowUntil = Time.time + Mathf.Max(0f, PickupMovementSlowDuration);

        float elapsed = 0f;
        float actionDelay = Mathf.Max(0f, PickupActionDelay);
        float totalAnimationTime = Mathf.Max(PickupGatherDuration, actionDelay);

        while (elapsed < actionDelay)
        {
            ForcePickupAnimationAt(elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ForcePickupAnimationAt(actionDelay);

        if (target != null)
        {
            if (AudioManager.Instance != null && target.Data != null)
                AudioManager.Instance.PlayPickupSound(target.Data.ItemName);
            target.RequestPickup();
        }

        while (elapsed < totalAnimationTime)
        {
            ForcePickupAnimationAt(elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ForcePickupAnimationAt(totalAnimationTime);

        StopPickupAnimation();

        m_PickupMovementSlowUntil = 0f;
        m_PickupRoutine = null;
    }

    private void ForcePickupAnimationAt(float elapsed)
    {
        if (m_Animator == null || m_ActivePickupAnimationLayer < 0 || m_ActivePickupAnimationStateHash == 0)
            return;

        if (m_ActivePickupAnimationLayer == BaseLayerIndex)
            MutePickupOverlayLayers();

        float stateLength = Mathf.Max(0.01f, m_ActivePickupAnimationLength);
        float normalizedTime = Mathf.Clamp(elapsed / stateLength, 0f, 0.999f);
        m_Animator.Play(m_ActivePickupAnimationStateHash, m_ActivePickupAnimationLayer, normalizedTime);
    }

    private static bool HasAnimatorState(Animator animator, int layerIndex, int stateHash)
    {
        return animator != null
            && animator.layerCount > layerIndex
            && animator.HasState(layerIndex, stateHash);
    }

    private static bool IsPickupAnimator(Animator animator)
    {
        return HasAnimatorState(animator, ArmsOnlyLayerIndex, NewPickupStateHash)
            || HasAnimatorState(animator, BaseLayerIndex, NewPickupStateHash)
            || HasAnimatorState(animator, BaseLayerIndex, GatheringStateHash)
            || HasAnimatorParameter(animator, IsGatheringHash);
    }

    private static int GetPickupAnimationLayer(Animator animator)
    {
        if (HasAnimatorState(animator, BaseLayerIndex, NewPickupStateHash))
            return BaseLayerIndex;

        if (HasAnimatorState(animator, ArmsOnlyLayerIndex, NewPickupStateHash))
            return ArmsOnlyLayerIndex;

        return -1;
    }

    private static float GetLayerWeightSafe(Animator animator, int layerIndex)
    {
        return animator != null && animator.layerCount > layerIndex
            ? animator.GetLayerWeight(layerIndex)
            : 0f;
    }

    private static void SetLayerWeightSafe(Animator animator, int layerIndex, float weight)
    {
        if (animator != null && animator.layerCount > layerIndex)
            animator.SetLayerWeight(layerIndex, weight);
    }

    private static float GetCurrentStateLength(Animator animator, int layerIndex, float fallbackLength)
    {
        if (animator == null || animator.layerCount <= layerIndex)
            return Mathf.Max(0.01f, fallbackLength);

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        if (stateInfo.length <= 0f)
            return Mathf.Max(0.01f, fallbackLength);

        return stateInfo.length;
    }

    private static bool HasAnimatorParameter(Animator animator, int parameterHash)
    {
        if (animator == null) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash)
                return true;
        }

        return false;
    }
}
