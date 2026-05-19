using System.Collections;
using UnityEngine;

/// <summary>
/// Manages the physical weapon model in the player's hand.
/// Listens to Inventory selection changes and instantiates/destroys weapon prefabs.
/// Attach to the Player GameObject.
/// </summary>
public class EquipmentHolder : MonoBehaviour
{
    public static EquipmentHolder Instance { get; private set; }

    [Header("References")]
    [Tooltip("Active hold point where the selected hotbar item is shown. Created automatically if null.")]
    public Transform HandAnchor;

    [Tooltip("Animator that receives holding/throw parameters. Auto-found in children if empty.")]
    public Animator PlayerAnimator;

    [Tooltip("Public first-person hold point. Assign a child under the camera to control held item placement.")]
    public Transform FirstPersonHoldPoint;

    [Tooltip("Public third-person hold point. Assign a child under the player body to control held item placement.")]
    public Transform ThirdPersonHoldPoint;

    [Tooltip("Offset from the camera for first-person weapon positioning (Right, Up, Forward).")]
    public Vector3 FPHandOffset = new Vector3(0.7f, -0.5f, 0.9f);

    [Tooltip("Offset from the player body for third-person positioning.")]
    public Vector3 TPHandOffset = new Vector3(0.5f, 1.2f, 0.5f);

    [Tooltip("When enabled, assigned hold point transforms keep their Inspector position/rotation instead of being moved by FP/TP offsets.")]
    public bool UseAssignedHoldPointTransform = true;

    [Tooltip("When enabled, held items keep the prefab root's local position, rotation, and scale instead of ItemData hold offsets.")]
    public bool UsePrefabTransformWhenHeld = true;

    [Header("Tool/Throwable")]
    [Tooltip("ProjectileCurveVisualizer prefab for throwable item trajectory preview.")]
    public GameObject ToolVisualizerPrefab;

    [Header("Melee Throwable")]
    [Tooltip("ProjectileCurveVisualizer prefab for melee throwable trajectory preview.")]
    public GameObject MeleeVisualizerPrefab;

    [Header("Minecraft Style Animation")]
    public float SwayAmount = 2f;
    public float SwaySmoothness = 10f;
    public float BobSpeed = 12f;
    public float BobAmount = 0.05f;

    [Header("Throw Animation")]
    [Tooltip("Blend time used when forcing the player into the Throw animation.")]
    public float ThrowAnimationBlendTime = 0.05f;
    [Tooltip("Delay before the held stone/knife actually leaves the hand after throw starts.")]
    public float ThrowReleaseDelay = 0.25f;
    [Tooltip("How long movement and locomotion animation stay locked after throw starts.")]
    public float ThrowAnimationLockDuration = 0.7f;

    [Header("Raw Knife Attack")]
    [Tooltip("Movement speed multiplier while the Raw Knife upper-body attack plays.")]
    public float RawKnifeAttackMovementMultiplier = 0.6f;
    [Tooltip("How long the Raw Knife melee attack slows movement.")]
    public float RawKnifeAttackSlowDuration = 1.25f;

    [Header("Stone Spear Attack")]
    [Tooltip("Movement speed multiplier while the Stone Spear upper-body attack plays.")]
    public float StoneSpearAttackMovementMultiplier = 0.75f;
    [Tooltip("How long the Stone Spear melee attack slows movement.")]
    public float StoneSpearAttackSlowDuration = 0.45f;

    [Header("Aim Mode")]
    [Tooltip("Movement speed multiplier while aiming throwable stone or Raw Knife.")]
    public float AimMovementMultiplier = 0.15f;

    private float _bobTimer;

    private Inventory _inventory;
    private PlayerController _player;
    private GameObject _currentWeaponObj;
    private ItemBehaviour _currentBehaviour;
    private bool _usingAutoCreatedHoldPoint;
    private Coroutine _throwLockRoutine;
    private Coroutine _rawKnifeAttackRoutine;
    private Coroutine _stoneSpearAttackRoutine;
    private bool _rawKnifeAttackSlowed;
    private bool _stoneSpearAttackSlowed;
    private bool _isAimModeActive;
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int ThrowStateHash = Animator.StringToHash("Throw");
    private static readonly int RawKnifeAttackHash = Animator.StringToHash("RawKnifeAttack");
    private static readonly int StoneSpearAttackHash = Animator.StringToHash("StoneSpearAttack");
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    private static readonly int IsSpearAimingHash = Animator.StringToHash("IsSpearAiming");

    /// <summary>The currently equipped item behaviour (null if empty hand).</summary>
    public ItemBehaviour CurrentBehaviour => _currentBehaviour;
    public ItemData CurrentItem => _inventory != null ? _inventory.SelectedItem : null;
    public bool IsThrowAnimationLocked { get; private set; }
    public bool IsAimModeActive => _isAimModeActive;
    public bool IsStoneSpearAttackActive => _stoneSpearAttackSlowed;
    public float MovementSpeedMultiplier
    {
        get
        {
            float multiplier = 1f;
            if (_rawKnifeAttackSlowed)
                multiplier *= RawKnifeAttackMovementMultiplier;
            if (_stoneSpearAttackSlowed)
                multiplier *= StoneSpearAttackMovementMultiplier;
            if (_isAimModeActive)
                multiplier *= AimMovementMultiplier;
            return Mathf.Clamp01(multiplier);
        }
    }

    private static bool IsThrowableStone(ItemData item)
    {
        return item != null && item.ItemName == "Stonemini";
    }

    private static bool IsThrowableMelee(ItemData item)
    {
        return item != null && (item.ItemName == "Raw Knife" || item.ItemName == "Stone Spear");
    }

    public static bool SupportsAimMode(ItemData item)
    {
        return item != null && (item.ItemName == "Raw Knife" || item.ItemName == "Stonemini" || item.ItemName == "Stone Spear");
    }

    public void ReleaseCurrentWeapon()
    {
        SetAimMode(false, null);
        _currentWeaponObj = null;
        _currentBehaviour = null;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _inventory = GetComponent<Inventory>();
        _player = GetComponent<PlayerController>();
        EnsurePlayerAnimator();

        if (_inventory == null)
        {
            Debug.LogError("[EquipmentHolder] Requires Inventory on same GameObject!");
            return;
        }

        EnsureHoldPoints();

        // Subscribe to inventory changes
        _inventory.OnSelectedItemChanged += OnSlotChanged;

        // Equip initial item
        EquipItem(_inventory.SelectedItem);
    }

    void OnDestroy()
    {
        if (_inventory != null)
            _inventory.OnSelectedItemChanged -= OnSlotChanged;
    }

    void Update()
    {
        HandleInput();
        UpdateHandPosition();
    }

    private void HandleInput()
    {
        if (_currentBehaviour == null) return;

        // Left-click = primary use (attack/consume)
        if (Input.GetMouseButtonDown(0))
        {
            _currentBehaviour.Use();
        }

        // Right-click = secondary use (only for weapons with AltUse)
        // NOTE: ItemPlacer handles right-click for non-weapon items (Tool/Consumable)
        // NOTE: ItemDropper handles Q-key dropping separately
        if (Input.GetMouseButtonDown(1))
        {
            _currentBehaviour?.AltUse();
        }
    }

    private void UpdateHandPosition()
    {
        if (_player == null) return;
        EnsureHoldPoints();
        if (HandAnchor == null) return;

        Camera cam = _player.GetActiveCamera();
        if (cam == null) return;

        if (_player.CurrentMode == PlayerController.CameraMode.FirstPerson)
        {
            if (FirstPersonHoldPoint == null)
            {
                FirstPersonHoldPoint = HandAnchor;
            }

            if (UseAssignedHoldPointTransform && !_usingAutoCreatedHoldPoint)
            {
                SetActiveHoldPoint(FirstPersonHoldPoint);
                ApplyCurrentItemHoldSettings();
                return;
            }

            // FP mode: parent to camera so it stays exactly with us
            if (FirstPersonHoldPoint.parent != cam.transform)
            {
                FirstPersonHoldPoint.SetParent(cam.transform, false);
            }

            Vector3 targetPos = FPHandOffset;
            Quaternion targetRot = Quaternion.identity;

            // Apply Bobbing based on movement input
            float moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).magnitude;
            if (moveInput > 0.1f)
            {
                _bobTimer += Time.deltaTime * BobSpeed;
                float bobY = Mathf.Sin(_bobTimer) * BobAmount;
                float bobX = Mathf.Cos(_bobTimer * 0.5f) * BobAmount;
                targetPos += new Vector3(bobX, bobY, 0);
            }
            else
            {
                // Smoothly return to center
                _bobTimer = 0f;
            }

            // Apply Sway based on mouse movement (inverted for natural lag)
            float mouseX = -Input.GetAxis("Mouse X") * SwayAmount;
            float mouseY = Input.GetAxis("Mouse Y") * SwayAmount;
            
            targetRot = Quaternion.Euler(mouseY, mouseX, 0); // Local rotation lag

            // Apply smoothing
            FirstPersonHoldPoint.localPosition = Vector3.Lerp(FirstPersonHoldPoint.localPosition, targetPos, Time.deltaTime * 15f);
            FirstPersonHoldPoint.localRotation = Quaternion.Slerp(FirstPersonHoldPoint.localRotation, targetRot, Time.deltaTime * SwaySmoothness);
            SetActiveHoldPoint(FirstPersonHoldPoint);
        }
        else
        {
            if (ThirdPersonHoldPoint == null)
            {
            ThirdPersonHoldPoint = HandAnchor;
            }

            if (UseAssignedHoldPointTransform && !_usingAutoCreatedHoldPoint)
            {
                SetActiveHoldPoint(ThirdPersonHoldPoint);
                ApplyCurrentItemHoldSettings();
                return;
            }

            // TP mode: parent to player body
            if (ThirdPersonHoldPoint.parent != transform)
            {
                ThirdPersonHoldPoint.SetParent(transform, false);
            }
            
            ThirdPersonHoldPoint.localPosition = TPHandOffset;
            ThirdPersonHoldPoint.localRotation = Quaternion.identity;
            SetActiveHoldPoint(ThirdPersonHoldPoint);
        }

        ApplyCurrentItemHoldSettings();
    }

    private void ApplyCurrentItemHoldSettings()
    {
        if (UsePrefabTransformWhenHeld)
            return;

        // Apply item's hold settings dynamically so they can be tweaked in the Inspector live
        if (_currentWeaponObj != null && CurrentItem != null)
        {
            // NaN Protection
            Vector3 pos = CurrentItem.HoldPosition;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) pos = Vector3.zero;
            
            _currentWeaponObj.transform.localPosition = pos;
            _currentWeaponObj.transform.localRotation = Quaternion.Euler(CurrentItem.HoldRotation);
            
            // Allow manual scale tweaking. We use Lerp to smooth out slider dragging
            float targetScale = CurrentItem.HoldScale;
            if (float.IsNaN(targetScale)) targetScale = 1f;

            _currentWeaponObj.transform.localScale = Vector3.Lerp(_currentWeaponObj.transform.localScale, Vector3.one * targetScale, Time.deltaTime * 15f);
        }
    }

    private void OnSlotChanged(int index, ItemData item)
    {
        EquipItem(item);
    }

    private void EquipItem(ItemData item)
    {
        EnsureHoldPoints();
        SetAimMode(false, null);

        // Cleanup old weapon
        if (_currentBehaviour != null)
        {
            _currentBehaviour.OnUnequip();
        }
        if (_currentWeaponObj != null)
        {
            Destroy(_currentWeaponObj);
            _currentWeaponObj = null;
            _currentBehaviour = null;
        }

        if (item == null || item.Prefab == null)
        {
            SetHoldingAnimation(false);
            return;
        }

        // Spawn weapon
        try
        {
            _currentWeaponObj = Instantiate(item.Prefab, HandAnchor, false);
        }
        catch (System.InvalidCastException)
        {
            Debug.LogWarning($"[Equipment] {item.ItemName} has an invalid hand prefab reference. Leaving hand empty.");
            return;
        }

        if (!UsePrefabTransformWhenHeld)
        {
            _currentWeaponObj.transform.localPosition = item.HoldPosition;
            _currentWeaponObj.transform.localRotation = Quaternion.Euler(item.HoldRotation);
            _currentWeaponObj.transform.localScale = Vector3.one * item.HoldScale;
        }

        // Get or add appropriate behaviour
        _currentBehaviour = _currentWeaponObj.GetComponent<ItemBehaviour>();
        if (_currentBehaviour == null)
        {
            // Auto-add based on ItemType
            switch (item.Type)
            {
                case ItemType.Melee:
                    if (IsThrowableMelee(item))
                    {
                        var tk = _currentWeaponObj.AddComponent<ThrowableMeleeWeapon>();
                        tk.VisualizerPrefab = MeleeVisualizerPrefab;
                        _currentBehaviour = tk;
                    }
                    else
                        _currentBehaviour = _currentWeaponObj.AddComponent<MeleeWeapon>();
                    break;
                case ItemType.Ranged:
                    _currentBehaviour = _currentWeaponObj.AddComponent<RangedWeapon>();
                    break;
                case ItemType.Tool:
                    if (IsThrowableStone(item))
                    {
                        var throwable = _currentWeaponObj.AddComponent<ThrowableItem>();
                        throwable.VisualizerPrefab = ToolVisualizerPrefab;
                        _currentBehaviour = throwable;
                    }
                    else
                    {
                        _currentBehaviour = _currentWeaponObj.AddComponent<ItemBehaviour>();
                    }
                    break;
                case ItemType.Consumable:
                    _currentBehaviour = _currentWeaponObj.AddComponent<ConsumableItem>();
                    break;
                default:
                    _currentBehaviour = _currentWeaponObj.AddComponent<ItemBehaviour>();
                    break;
            }
        }

        // Initialize
        Camera cam = (_player != null) ? _player.GetActiveCamera() : Camera.main;
        _currentBehaviour.OnEquip(item, transform, cam);

        // Disable physics on the held weapon
        Rigidbody rb = _currentWeaponObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Disable colliders on held weapon so it doesn't bump the player
        foreach (var col in _currentWeaponObj.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // Clean up any world-drop components that might be on the prefab
        // so it doesn't try to hover or show pickup outlines in the player's hand!
        var loot = _currentWeaponObj.GetComponent<LootItem>();
        if (loot != null) Destroy(loot);
        
        var outline = _currentWeaponObj.GetComponent<OutlineController>();
        if (outline != null) Destroy(outline);
        
        var wSpin = _currentWeaponObj.GetComponent<WorldItemSpin>();
        if (wSpin != null) Destroy(wSpin);

        Debug.Log($"[Equipment] Equipped: {item.ItemName}");
        SetHoldingAnimation(true);
    }

    public void TriggerThrowAnimation()
    {
        EnsurePlayerAnimator();
        SetAimMode(false, null);
        StartThrowLock();

        if (PlayerAnimator != null)
        {
            PlayerAnimator.ResetTrigger(ThrowHash);
            PlayerAnimator.CrossFadeInFixedTime(ThrowStateHash, ThrowAnimationBlendTime, 0, 0f);
            PlayerAnimator.ResetTrigger(ThrowHash);
        }
    }

    public void TriggerRawKnifeMeleeAnimation()
    {
        if (CurrentItem == null || CurrentItem.ItemName != "Raw Knife")
            return;

        EnsurePlayerAnimator();
        StartRawKnifeAttackSlow();

        if (PlayerAnimator != null && HasAnimatorParameter(PlayerAnimator, RawKnifeAttackHash))
        {
            PlayerAnimator.ResetTrigger(RawKnifeAttackHash);
            PlayerAnimator.SetTrigger(RawKnifeAttackHash);
        }
    }

    public void TriggerStoneSpearMeleeAnimation()
    {
        if (CurrentItem == null || CurrentItem.ItemName != "Stone Spear")
            return;

        EnsurePlayerAnimator();
        StartStoneSpearAttackSlow();

        if (PlayerAnimator != null && HasAnimatorParameter(PlayerAnimator, StoneSpearAttackHash))
        {
            PlayerAnimator.ResetTrigger(StoneSpearAttackHash);
            PlayerAnimator.SetTrigger(StoneSpearAttackHash);
        }
    }

    public void SetAimMode(bool isAiming, ItemData item)
    {
        if (isAiming && !SupportsAimMode(item))
            return;

        _isAimModeActive = isAiming;
        EnsurePlayerAnimator();

        bool isSpearAiming = isAiming && item != null && item.ItemName == "Stone Spear";
        bool isThrowableAiming = isAiming && !isSpearAiming;

        if (PlayerAnimator != null && HasAnimatorParameter(PlayerAnimator, IsAimingHash))
        {
            PlayerAnimator.SetBool(IsAimingHash, isThrowableAiming);
        }

        if (PlayerAnimator != null && HasAnimatorParameter(PlayerAnimator, IsSpearAimingHash))
        {
            PlayerAnimator.SetBool(IsSpearAimingHash, isSpearAiming);
        }
    }

    public float GetThrowReleaseDelay()
    {
        return Mathf.Max(0f, ThrowReleaseDelay);
    }

    public bool SaveCurrentHoldTransformToItemData()
    {
        if (_currentWeaponObj == null || CurrentItem == null)
            return false;

        Transform heldTransform = _currentWeaponObj.transform;
        CurrentItem.HoldPosition = heldTransform.localPosition;
        CurrentItem.HoldRotation = heldTransform.localEulerAngles;
        CurrentItem.HoldScale = heldTransform.localScale.x;
        return true;
    }

    private void StartThrowLock()
    {
        if (_throwLockRoutine != null)
            StopCoroutine(_throwLockRoutine);

        _throwLockRoutine = StartCoroutine(ThrowLockRoutine());
    }

    private IEnumerator ThrowLockRoutine()
    {
        IsThrowAnimationLocked = true;

        float duration = Mathf.Max(ThrowReleaseDelay, ThrowAnimationLockDuration);
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        IsThrowAnimationLocked = false;
        _throwLockRoutine = null;
    }

    private void StartRawKnifeAttackSlow()
    {
        if (_rawKnifeAttackRoutine != null)
            StopCoroutine(_rawKnifeAttackRoutine);

        _rawKnifeAttackRoutine = StartCoroutine(RawKnifeAttackSlowRoutine());
    }

    private IEnumerator RawKnifeAttackSlowRoutine()
    {
        _rawKnifeAttackSlowed = true;

        float duration = Mathf.Max(0f, RawKnifeAttackSlowDuration);
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        _rawKnifeAttackSlowed = false;
        _rawKnifeAttackRoutine = null;
    }

    private void StartStoneSpearAttackSlow()
    {
        if (_stoneSpearAttackRoutine != null)
            StopCoroutine(_stoneSpearAttackRoutine);

        _stoneSpearAttackRoutine = StartCoroutine(StoneSpearAttackSlowRoutine());
    }

    private IEnumerator StoneSpearAttackSlowRoutine()
    {
        _stoneSpearAttackSlowed = true;

        float duration = Mathf.Max(0f, StoneSpearAttackSlowDuration);
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        _stoneSpearAttackSlowed = false;
        _stoneSpearAttackRoutine = null;
    }

    private void EnsureHoldPoints()
    {
        if (HandAnchor != null)
        {
            if (FirstPersonHoldPoint == null) FirstPersonHoldPoint = HandAnchor;
            if (ThirdPersonHoldPoint == null) ThirdPersonHoldPoint = HandAnchor;
            return;
        }

        Camera cam = _player != null ? _player.GetActiveCamera() : Camera.main;
        bool useFirstPerson = cam != null && (_player == null || _player.CurrentMode == PlayerController.CameraMode.FirstPerson);
        Transform assignedHoldPoint = useFirstPerson ? FirstPersonHoldPoint : ThirdPersonHoldPoint;
        if (assignedHoldPoint == null)
        {
            assignedHoldPoint = FirstPersonHoldPoint != null ? FirstPersonHoldPoint : ThirdPersonHoldPoint;
        }

        if (assignedHoldPoint != null)
        {
            HandAnchor = assignedHoldPoint;
            if (FirstPersonHoldPoint == null) FirstPersonHoldPoint = HandAnchor;
            if (ThirdPersonHoldPoint == null) ThirdPersonHoldPoint = HandAnchor;
            return;
        }

        Transform parent = transform;
        if (useFirstPerson)
        {
            parent = cam.transform;
        }

        GameObject anchor = new GameObject("HoldPoint");
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = parent == transform ? TPHandOffset : FPHandOffset;
        HandAnchor = anchor.transform;
        _usingAutoCreatedHoldPoint = true;

        if (FirstPersonHoldPoint == null) FirstPersonHoldPoint = HandAnchor;
        if (ThirdPersonHoldPoint == null) ThirdPersonHoldPoint = HandAnchor;
    }

    private void SetActiveHoldPoint(Transform holdPoint)
    {
        if (holdPoint == null || HandAnchor == holdPoint) return;

        HandAnchor = holdPoint;
        if (_currentWeaponObj != null && _currentWeaponObj.transform.parent != HandAnchor)
        {
            _currentWeaponObj.transform.SetParent(HandAnchor, false);
        }
    }

    private void SetHoldingAnimation(bool isHolding)
    {
        EnsurePlayerAnimator();
        if (PlayerAnimator != null) PlayerAnimator.SetBool(IsHoldingHash, isHolding);
    }

    private void EnsurePlayerAnimator()
    {
        if (HasAnimatorParameter(PlayerAnimator, ThrowHash) ||
            HasAnimatorParameter(PlayerAnimator, IsHoldingHash) ||
            HasAnimatorParameter(PlayerAnimator, RawKnifeAttackHash) ||
            HasAnimatorParameter(PlayerAnimator, StoneSpearAttackHash) ||
            HasAnimatorParameter(PlayerAnimator, IsAimingHash) ||
            HasAnimatorParameter(PlayerAnimator, IsSpearAimingHash))
            return;

        PlayerAnimator = null;
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
        {
            if (HasAnimatorParameter(animator, ThrowHash) ||
                HasAnimatorParameter(animator, RawKnifeAttackHash) ||
                HasAnimatorParameter(animator, StoneSpearAttackHash) ||
                HasAnimatorParameter(animator, IsAimingHash) ||
                HasAnimatorParameter(animator, IsSpearAimingHash))
            {
                PlayerAnimator = animator;
                return;
            }
        }
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
