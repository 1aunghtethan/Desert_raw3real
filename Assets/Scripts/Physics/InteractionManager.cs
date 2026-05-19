using UnityEngine;
using GinjaGaming.FinalCharacterController;
using System.Collections;

/// <summary>
/// Handles player interaction with world objects (Meat, Water, etc.).
/// Performs a raycast from the camera to detect LootItem components.
/// Shows a UI prompt and handles the 'E' key for pickup.
/// </summary>
public class InteractionManager : MonoBehaviour
{
    [Header("Settings")]
    public float InteractionRange = 4.0f;
    public LayerMask InteractionLayers = ~0; // Everything by default
    public KeyCode InteractionKey = KeyCode.E;

    [Header("Visuals")]
    public Color PromptColor = Color.white;
    public int FontSize = 22;

    [Header("Pickup Animation")]
    public float PickupGatherDuration = 0.8f;
    public float PickupActionDelay = 0.6f;
    public float PickupMovementMultiplier = 0.25f;

    private LootItem m_CurrentTarget;
    private LootItem m_PreviousOutlineTarget;
    private OutlineController m_CurrentOutline;
    private Camera m_MainCamera;
    private PlayerController m_Controller;
    private PlayerActionsInput m_PlayerActionsInput;
    private Animator m_Animator;
    private Coroutine m_PickupRoutine;

    // Hash for the Gathering state name (used with CrossFadeInFixedTime)
    private static readonly int GatheringStateHash = Animator.StringToHash("Gathering");
    private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");
    private static readonly int IsGatheringHash = Animator.StringToHash("isGathering");

    private GUIStyle m_PromptStyle;
    private GUIStyle m_ShadowStyle;
    public float MovementSpeedMultiplier => m_PickupRoutine != null ? Mathf.Clamp01(PickupMovementMultiplier) : 1f;

    private void Start()
    {
        m_Controller = GetComponent<PlayerController>();

        // Search in children too, in case PlayerActionsInput is on a child object
        m_PlayerActionsInput = GetComponent<PlayerActionsInput>();
        if (m_PlayerActionsInput == null)
            m_PlayerActionsInput = GetComponentInChildren<PlayerActionsInput>();

        // Find the Animator that has the isGathering parameter (via PlayerAnimation's serialized reference)
        FindCorrectAnimator();

        UpdateCameraReference();
    }

    private void FindCorrectAnimator()
    {
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

        // Restore: Detect EVERY frame for maximum responsiveness ("one frame")
        PerformDetection();

        // Frame-perfect input handling
        if (m_CurrentTarget != null && Input.GetKeyDown(InteractionKey) && m_PickupRoutine == null)
        {
            m_PickupRoutine = StartCoroutine(PickupAfterGathering(m_CurrentTarget));
        }

        UpdateOutline(m_CurrentTarget);
    }

    private void PerformDetection()
    {
        Ray ray = m_MainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        float maxDist = InteractionRange + 20f;

        // Restore: Use the user-configured layers instead of forcing "Item"
        int layerMask = InteractionLayers.value;

        // Restore: Simple, responsive SphereCastAll for easier targeting
        m_CurrentTarget = null;
        RaycastHit[] hits = Physics.SphereCastAll(ray, 0.3f, maxDist, layerMask);
        
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

    // LEGACY GUI: Standard OnGUI with Style Caching for FPS
    private void OnGUI()
    {
        if (m_CurrentTarget == null) return;

        if (m_PromptStyle == null)
        {
            m_PromptStyle = new GUIStyle(GUI.skin.label);
            // Use the skin's default font to avoid "invalid font reference" warnings during reloads
            m_PromptStyle.font = GUI.skin.font; 
            m_PromptStyle.alignment = TextAnchor.MiddleCenter;
            m_PromptStyle.fontSize = FontSize;
            m_PromptStyle.fontStyle = FontStyle.Bold;
            m_PromptStyle.normal.textColor = PromptColor;
            m_ShadowStyle = new GUIStyle(m_PromptStyle);
            m_ShadowStyle.normal.textColor = Color.black;
        }

        string prompt = $"[ {InteractionKey} ] Pick up {((m_CurrentTarget.Data != null) ? m_CurrentTarget.Data.ItemName : "Item")}";
        Rect rect = new Rect(Screen.width / 2 - 150, Screen.height / 2 + 50, 300, 50);
        
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), prompt, m_ShadowStyle);
        GUI.Label(rect, prompt, m_PromptStyle);
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
        // Method 1: Trigger via PlayerActionsInput pipeline (sets GatherPressed → PlayerAnimation reads it)
        if (m_PlayerActionsInput == null)
        {
            m_PlayerActionsInput = GetComponent<PlayerActionsInput>();
            if (m_PlayerActionsInput == null)
                m_PlayerActionsInput = GetComponentInChildren<PlayerActionsInput>();
        }

        if (m_PlayerActionsInput != null)
            m_PlayerActionsInput.TriggerGathering(PickupGatherDuration);

        // Method 2: Also directly force-play the Gathering animation via CrossFade
        // This is the reliable fallback that works regardless of parameter pipeline issues
        if (m_Animator == null)
            FindCorrectAnimator();

        if (m_Animator != null)
        {
            m_Animator.CrossFadeInFixedTime(GatheringStateHash, 0.15f);
        }
    }

    private void StopPickupAnimation()
    {
        if (m_Animator != null)
        {
            m_Animator.CrossFadeInFixedTime(LocomotionStateHash, 0.2f);
        }
    }

    private IEnumerator PickupAfterGathering(LootItem target)
    {
        PlayPickupAnimation();

        yield return new WaitForSeconds(PickupActionDelay);

        if (target != null)
        {
            if (AudioManager.Instance != null && target.Data != null)
                AudioManager.Instance.PlayPickupSound(target.Data.ItemName);
            target.RequestPickup();
        }

        float remainingAnimationTime = Mathf.Max(0f, PickupGatherDuration - PickupActionDelay);
        if (remainingAnimationTime > 0f)
            yield return new WaitForSeconds(remainingAnimationTime);

        StopPickupAnimation();

        m_PickupRoutine = null;
    }
}
