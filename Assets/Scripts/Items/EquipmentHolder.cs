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
    public float RawKnifeAttackSlowDuration = 1.2f;
    [Tooltip("Raw Knife first-click attack stops at this frame unless a second click happens before it.")]
    public int RawKnifeAttackStopFrame = 50;
    [Tooltip("Frame rate used by Assets/Animations/new/attack.anim.")]
    public float RawKnifeAttackFrameRate = 60f;
    [Tooltip("How long to stabilize the spine/head chain during a locomotion Raw Knife attack.")]
    public float RawKnifeHeadStabilizeDuration = 1.2f;

    [Header("Stone Spear Attack")]
    [Tooltip("Movement speed multiplier while the Stone Spear upper-body attack plays.")]
    public float StoneSpearAttackMovementMultiplier = 0.75f;
    [Tooltip("How long the Stone Spear melee attack slows movement.")]
    public float StoneSpearAttackSlowDuration = 0.45f;

    [Header("Aim Mode")]
    [Tooltip("Movement speed multiplier while aiming throwable stone or Raw Knife.")]
    public float AimMovementMultiplier = 0.15f;

    [Header("Swarded Cactaus Water")]
    public float DrinkUseEffectDelay = 2.5f;
    public float DrinkRunJumpLockDuration = 3.8f;
    public float DrinkAnimationBlendTime = 0.1f;

    [Header("Eating")]
    public float EatAnimationDuration = 1f;
    public float EatUseEffectDelay = 2.5f;
    public float EatMovementMultiplier = 0.3f;
    public float EatAnimationBlendTime = 0.1f;
    public Vector3 BeefEatLocalPosition = new Vector3(0.245f, -0.221f, 0.112f);
    public Vector3 BeefEatLocalEulerAngles = new Vector3(35.78f, -72.36f, -65.05f);
    public Vector3 BeefEatLocalScale = new Vector3(0.42f, 0.42f, 0.42f);

    [Header("Wood Shovel")]
    public float WoodShovelMovementMultiplier = 0.2f;
    public float WoodShovelAnimationBlendTime = 0.1f;
    public Vector3 WoodShovelActionLocalPosition = new Vector3(0.448f, -0.451f, 0.1417f);
    public Vector3 WoodShovelActionLocalEulerAngles = new Vector3(-24.14f, 162.63f, 45.38f);
    public Vector3 WoodShovelActionLocalScale = new Vector3(1f, 0.999f, 1f);

    private float _bobTimer;

    private Inventory _inventory;
    private PlayerController _player;
    private InteractionManager _interactionManager;
    private GameObject _currentWeaponObj;
    private ItemBehaviour _currentBehaviour;
    private bool _usingAutoCreatedHoldPoint;
    private Coroutine _throwLockRoutine;
    private Coroutine _rawKnifeAttackRoutine;
    private Coroutine _rawKnifeAnimationStopRoutine;
    private Coroutine _stoneSpearAttackRoutine;
    private Coroutine _drinkUseRoutine;
    private Coroutine _eatAnimationRoutine;
    private Coroutine _delayedEatUseRoutine;
    private bool _rawKnifeAttackSlowed;
    private bool _rawKnifeFullAttackRequested;
    private bool _rawKnifeFullAttackRequestWindowOpen;
    private bool _rawKnifeHeadStabilizeActive;
    private bool _stoneSpearAttackSlowed;
    private bool _isAimModeActive;
    private bool _drinkUseApplyingEffect;
    private bool _eatUseApplyingEffect;
    private bool _beefEatTransformActive;
    private bool _hasBeefEatRestTransform;
    private Vector3 _beefEatRestLocalPosition;
    private Quaternion _beefEatRestLocalRotation;
    private Vector3 _beefEatRestLocalScale;
    private bool _woodShovelActionTransformActive;
    private bool _hasWoodShovelRestTransform;
    private Vector3 _woodShovelRestLocalPosition;
    private Quaternion _woodShovelRestLocalRotation;
    private Vector3 _woodShovelRestLocalScale;
    private Animator _rawKnifeHeadStabilizeAnimator;
    private float _rawKnifeHeadStabilizeUntil = -999f;
    private bool _rawKnifeHeadStabilizeRestCached;
    private readonly Transform[] _rawKnifeHeadStabilizeBones = new Transform[5];
    private readonly Quaternion[] _rawKnifeHeadStabilizeLocalRotations = new Quaternion[5];
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");
    private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");
    private static readonly int ThrowHash = Animator.StringToHash("Throw");
    private static readonly int ThrowStateHash = Animator.StringToHash("Throw");
    private static readonly int RawKnifeAttackHash = Animator.StringToHash("RawKnifeAttack");
    private static readonly int RawKnifeStandAttackStateHash = Animator.StringToHash("RawKnifeStandAttack");
    private static readonly int StoneSpearAttackHash = Animator.StringToHash("StoneSpearAttack");
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    private static readonly int IsSpearAimingHash = Animator.StringToHash("IsSpearAiming");
    private static readonly int DrinkStateHash = Animator.StringToHash("newDrink");
    private static readonly int EatStateHash = Animator.StringToHash("new eat");
    private static readonly int ArmsOnlyIdleStateHash = Animator.StringToHash("ArmsOnlyIdle");
    private static readonly int WoodShovelDiggingStateHash = Animator.StringToHash("digging");
    private static readonly int WoodShovelPlaceStateHash = Animator.StringToHash("digPlace");
    private static readonly int UpperBodyIdleStateHash = Animator.StringToHash("UpperBodyIdle");
    private const int BaseLayerIndex = 0;
    private const int UpperBodyLayerIndex = 1;
    private const int ArmsOnlyLayerIndex = 2;
    private const float RawKnifeLocomotionAttackSpeedThreshold = 0.05f;
    private const float RawKnifeAttackAnimationBlendTime = 0.05f;
    private const string DelayedDrinkItemName = "Swarded Cactaus Water";
    private const string BeefItemName = "Beef";
    private const string DiedCatItemName = "Died Cat";
    private const string Log2ItemName = "Log 2";
    private const string LogHalfItemName = "Log Half";
    private const string SmallBranchItemName = "Small Branch";

    /// <summary>The currently equipped item behaviour (null if empty hand).</summary>
    public ItemBehaviour CurrentBehaviour => _currentBehaviour;
    public ItemData CurrentItem => _inventory != null ? _inventory.SelectedItem : null;
    public bool IsThrowAnimationLocked { get; private set; }
    public bool IsDrinkUseLocked { get; private set; }
    public bool IsEatUseLocked { get; private set; }
    public bool IsWoodShovelUseLocked { get; private set; }
    public bool IsWoodShovelCameraLocked { get; private set; }
    public bool IsEatUseApplyingEffect => _eatUseApplyingEffect;
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
            if (IsEatUseLocked)
                multiplier *= EatMovementMultiplier;
            if (IsWoodShovelUseLocked)
                multiplier *= WoodShovelMovementMultiplier;
            return Mathf.Clamp01(multiplier);
        }
    }

    private static bool IsThrowableStone(ItemData item)
    {
        return item != null && item.ItemName == "Stonemini";
    }

    private static bool IsWoodShovel(ItemData item)
    {
        return item != null && item.ItemName == "Wood Shovel";
    }

    private static bool IsBeef(ItemData item)
    {
        return item != null && item.ItemName == BeefItemName;
    }

    private bool ShouldUseItemDataHoldTransform(ItemData item)
    {
        return !UsePrefabTransformWhenHeld || IsForcedItemDataHoldTransform(item);
    }

    private static bool IsForcedItemDataHoldTransform(ItemData item)
    {
        return item != null && (item.ItemName == Log2ItemName
            || item.ItemName == LogHalfItemName
            || item.ItemName == SmallBranchItemName);
    }

    private static bool IsThrowableMelee(ItemData item)
    {
        return item != null && (item.ItemName == "Raw Knife" || item.ItemName == "Stone Spear");
    }

    private static bool IsDelayedDrinkItem(ItemData item)
    {
        return item != null && item.ItemName == DelayedDrinkItemName;
    }

    private static bool IsDelayedEatItem(ItemData item)
    {
        return item != null && (item.ItemName == BeefItemName || item.ItemName == DiedCatItemName);
    }

    public static bool SupportsAimMode(ItemData item)
    {
        return item != null && (item.ItemName == "Raw Knife" || item.ItemName == "Stonemini" || item.ItemName == "Stone Spear");
    }

    public void ReleaseCurrentWeapon()
    {
        SetAimMode(false, null);
        CancelDrinkUse();
        CancelDelayedEatUse();
        SetBeefEatTransformActive(false);
        SetWoodShovelUseLocked(false);
        ResetWoodShovelAnimation();
        _currentBehaviour?.OnUnequip();
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
        _interactionManager = GetComponent<InteractionManager>();
        EnsurePlayerAnimator();
        CacheRawKnifeHeadStabilizeRestPose();

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
        CancelRawKnifeAnimationStop();
        StopRawKnifeHeadStabilize();
        CancelDrinkUse();
        CancelDelayedEatUse();
        CancelEatAnimation();

        if (_inventory != null)
            _inventory.OnSelectedItemChanged -= OnSlotChanged;
    }

    void Update()
    {
        HandleInput();
        UpdateHandPosition();
    }

    void LateUpdate()
    {
        ApplyRawKnifeHeadStabilize();
    }

    private void HandleInput()
    {
        if (_currentBehaviour == null) return;

        // Left-click = primary use (attack/consume)
        if (Input.GetMouseButtonDown(0))
        {
            if (_interactionManager == null)
                _interactionManager = GetComponent<InteractionManager>();

            if (_interactionManager != null && _interactionManager.TryStartCursorPickup())
                return;

            if (IsDelayedDrinkItem(CurrentItem))
            {
                TryStartDelayedDrinkUse();
                return;
            }

            if (IsDelayedEatItem(CurrentItem))
            {
                TryStartDelayedEatUse();
                return;
            }

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
        if (_beefEatTransformActive && IsBeef(CurrentItem))
        {
            ApplyBeefEatTransform();
            return;
        }

        if (_woodShovelActionTransformActive && IsWoodShovel(CurrentItem))
        {
            ApplyWoodShovelActionTransform();
            return;
        }

        if (!ShouldUseItemDataHoldTransform(CurrentItem))
            return;

        // Apply item's hold settings dynamically so they can be tweaked in the Inspector live
        ApplyItemDataHoldTransform(CurrentItem, true);
    }

    private void ApplyItemDataHoldTransform(ItemData item, bool smoothScale)
    {
        if (_currentWeaponObj == null || item == null)
            return;

        // NaN Protection
        Vector3 pos = item.HoldPosition;
        if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) pos = Vector3.zero;

        _currentWeaponObj.transform.localPosition = pos;
        _currentWeaponObj.transform.localRotation = Quaternion.Euler(item.HoldRotation);

        float targetScale = item.HoldScale;
        if (float.IsNaN(targetScale)) targetScale = 1f;

        Vector3 scale = Vector3.one * targetScale;
        _currentWeaponObj.transform.localScale = smoothScale
            ? Vector3.Lerp(_currentWeaponObj.transform.localScale, scale, Time.deltaTime * 15f)
            : scale;
    }

    private void OnSlotChanged(int index, ItemData item)
    {
        EquipItem(item);
    }

    private void EquipItem(ItemData item)
    {
        EnsureHoldPoints();
        SetAimMode(false, null);
        if (!_drinkUseApplyingEffect)
            CancelDrinkUse();
        if (!_eatUseApplyingEffect)
            CancelDelayedEatUse();
        if (!_eatUseApplyingEffect)
            SetBeefEatTransformActive(false);
        CancelRawKnifeAnimationStop();

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

        if (ShouldUseItemDataHoldTransform(item))
            ApplyItemDataHoldTransform(item, false);

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
                    if (IsWoodShovel(item))
                    {
                        _currentBehaviour = _currentWeaponObj.AddComponent<WoodShovelItem>();
                    }
                    else if (IsThrowableStone(item))
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

        int attackLayer = PlayRawKnifeAttackAnimation();
        StartRawKnifeAnimationStopWindow(attackLayer);
    }

    public bool RequestRawKnifeFullAttackAnimation()
    {
        if (!_rawKnifeFullAttackRequestWindowOpen)
            return false;

        _rawKnifeFullAttackRequested = true;
        _rawKnifeFullAttackRequestWindowOpen = false;
        return true;
    }

    private int PlayRawKnifeAttackAnimation()
    {
        if (PlayerAnimator == null)
            return -1;

        if (HasAnimatorParameter(PlayerAnimator, RawKnifeAttackHash))
            PlayerAnimator.ResetTrigger(RawKnifeAttackHash);

        float speed = HasAnimatorParameter(PlayerAnimator, SpeedHash)
            ? PlayerAnimator.GetFloat(SpeedHash)
            : 0f;
        Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool hasMoveInput = moveInput.sqrMagnitude > 0.01f;

        bool playUpperBody = (speed > RawKnifeLocomotionAttackSpeedThreshold || hasMoveInput)
            && HasAnimatorState(PlayerAnimator, UpperBodyLayerIndex, RawKnifeAttackHash);

        if (playUpperBody)
        {
            StartRawKnifeHeadStabilize();
            PlayerAnimator.CrossFadeInFixedTime(
                RawKnifeAttackHash,
                RawKnifeAttackAnimationBlendTime,
                UpperBodyLayerIndex,
                0f);
            return UpperBodyLayerIndex;
        }

        if (HasAnimatorState(PlayerAnimator, 0, RawKnifeStandAttackStateHash))
        {
            PlayerAnimator.CrossFadeInFixedTime(
                RawKnifeStandAttackStateHash,
                RawKnifeAttackAnimationBlendTime,
                0,
                0f);
            return 0;
        }

        return -1;
    }

    private void StartRawKnifeAnimationStopWindow(int attackLayer)
    {
        CancelRawKnifeAnimationStop();

        if (attackLayer < 0)
            return;

        _rawKnifeFullAttackRequested = false;
        _rawKnifeFullAttackRequestWindowOpen = true;
        _rawKnifeAnimationStopRoutine = StartCoroutine(RawKnifeAnimationStopWindowRoutine(attackLayer));
    }

    private IEnumerator RawKnifeAnimationStopWindowRoutine(int attackLayer)
    {
        float frameRate = Mathf.Max(1f, RawKnifeAttackFrameRate);
        float stopDelay = Mathf.Max(0f, RawKnifeAttackStopFrame) / frameRate;
        if (stopDelay > 0f)
            yield return new WaitForSeconds(stopDelay);

        _rawKnifeFullAttackRequestWindowOpen = false;

        if (!_rawKnifeFullAttackRequested)
        {
            int stopStateHash = attackLayer == UpperBodyLayerIndex
                ? UpperBodyIdleStateHash
                : LocomotionStateHash;
            CrossFadeLayerState(stopStateHash, attackLayer, RawKnifeAttackAnimationBlendTime);

            if (attackLayer == UpperBodyLayerIndex)
                StopRawKnifeHeadStabilize();
        }

        _rawKnifeAnimationStopRoutine = null;
    }

    private void CancelRawKnifeAnimationStop()
    {
        if (_rawKnifeAnimationStopRoutine != null)
        {
            StopCoroutine(_rawKnifeAnimationStopRoutine);
            _rawKnifeAnimationStopRoutine = null;
        }

        _rawKnifeFullAttackRequested = false;
        _rawKnifeFullAttackRequestWindowOpen = false;
    }

    private void StartRawKnifeHeadStabilize()
    {
        CacheRawKnifeHeadStabilizeRestPose();
        if (!_rawKnifeHeadStabilizeRestCached)
            return;

        _rawKnifeHeadStabilizeUntil = Time.time + Mathf.Max(0f, RawKnifeHeadStabilizeDuration);
        _rawKnifeHeadStabilizeActive = true;
    }

    private void StopRawKnifeHeadStabilize()
    {
        _rawKnifeHeadStabilizeActive = false;
        _rawKnifeHeadStabilizeUntil = -999f;
    }

    private void ApplyRawKnifeHeadStabilize()
    {
        if (!_rawKnifeHeadStabilizeActive)
            return;

        if (Time.time >= _rawKnifeHeadStabilizeUntil)
        {
            StopRawKnifeHeadStabilize();
            return;
        }

        for (int i = 0; i < _rawKnifeHeadStabilizeBones.Length; i++)
        {
            Transform bone = _rawKnifeHeadStabilizeBones[i];
            if (bone != null)
                bone.localRotation = _rawKnifeHeadStabilizeLocalRotations[i];
        }
    }

    private void CacheRawKnifeHeadStabilizeBones()
    {
        if (PlayerAnimator == null)
            return;

        if (_rawKnifeHeadStabilizeAnimator == PlayerAnimator && _rawKnifeHeadStabilizeBones[4] != null)
            return;

        _rawKnifeHeadStabilizeAnimator = PlayerAnimator;
        _rawKnifeHeadStabilizeRestCached = false;
        _rawKnifeHeadStabilizeBones[0] = GetAnimatorBoneOrChild(HumanBodyBones.Spine, "mixamorig:Spine");
        _rawKnifeHeadStabilizeBones[1] = GetAnimatorBoneOrChild(HumanBodyBones.Chest, "mixamorig:Spine1");
        _rawKnifeHeadStabilizeBones[2] = GetAnimatorBoneOrChild(HumanBodyBones.UpperChest, "mixamorig:Spine2");
        _rawKnifeHeadStabilizeBones[3] = GetAnimatorBoneOrChild(HumanBodyBones.Neck, "mixamorig:Neck");
        _rawKnifeHeadStabilizeBones[4] = GetAnimatorBoneOrChild(HumanBodyBones.Head, "mixamorig:Head");
    }

    private void CacheRawKnifeHeadStabilizeRestPose()
    {
        CacheRawKnifeHeadStabilizeBones();

        if (_rawKnifeHeadStabilizeRestCached)
            return;

        bool hasBone = false;
        for (int i = 0; i < _rawKnifeHeadStabilizeBones.Length; i++)
        {
            Transform bone = _rawKnifeHeadStabilizeBones[i];
            if (bone == null)
                continue;

            _rawKnifeHeadStabilizeLocalRotations[i] = bone.localRotation;
            hasBone = true;
        }

        _rawKnifeHeadStabilizeRestCached = hasBone;
    }

    private Transform GetAnimatorBoneOrChild(HumanBodyBones bone, string fallbackName)
    {
        Transform result = PlayerAnimator != null && PlayerAnimator.isHuman
            ? PlayerAnimator.GetBoneTransform(bone)
            : null;

        return result != null ? result : FindChildRecursive(PlayerAnimator != null ? PlayerAnimator.transform : transform, fallbackName);
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildRecursive(root.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
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

    private void TryStartDelayedDrinkUse()
    {
        if (_drinkUseRoutine != null)
            return;

        if (_currentBehaviour == null || !IsDelayedDrinkItem(CurrentItem))
            return;

        _drinkUseRoutine = StartCoroutine(DelayedDrinkUseRoutine(_currentBehaviour, CurrentItem));
    }

    private IEnumerator DelayedDrinkUseRoutine(ItemBehaviour behaviour, ItemData item)
    {
        IsDrinkUseLocked = true;
        EnsurePlayerAnimator();
        CrossFadeLayerState(DrinkStateHash, BaseLayerIndex, DrinkAnimationBlendTime);

        float effectDelay = Mathf.Max(0f, DrinkUseEffectDelay);
        float lockDuration = Mathf.Max(effectDelay, DrinkRunJumpLockDuration);

        if (effectDelay > 0f)
            yield return new WaitForSeconds(effectDelay);

        if (_currentBehaviour == behaviour && CurrentItem == item && IsDelayedDrinkItem(CurrentItem))
        {
            _drinkUseApplyingEffect = true;
            try
            {
                behaviour.Use();
            }
            finally
            {
                _drinkUseApplyingEffect = false;
            }
        }

        float remainingLockTime = lockDuration - effectDelay;
        if (remainingLockTime > 0f)
            yield return new WaitForSeconds(remainingLockTime);

        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, DrinkAnimationBlendTime);
        IsDrinkUseLocked = false;
        _drinkUseRoutine = null;
    }

    private void CancelDrinkUse()
    {
        if (_drinkUseRoutine != null)
        {
            StopCoroutine(_drinkUseRoutine);
            _drinkUseRoutine = null;
        }

        IsDrinkUseLocked = false;
        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, DrinkAnimationBlendTime);
    }

    private void TryStartDelayedEatUse()
    {
        if (_delayedEatUseRoutine != null)
            return;

        if (_currentBehaviour == null || !IsDelayedEatItem(CurrentItem) || _currentBehaviour.IsOnCooldown())
            return;

        if (!CanEatDelayedItem(CurrentItem))
            return;

        _delayedEatUseRoutine = StartCoroutine(DelayedEatUseRoutine(_currentBehaviour, CurrentItem));
    }

    private static bool CanEatDelayedItem(ItemData item)
    {
        if (!IsDelayedEatItem(item) || PlayerStats.Instance == null)
            return true;

        if (PlayerStats.Instance.CurrentHunger < PlayerStats.Instance.MaxHunger * 0.8f)
            return true;

        Debug.Log($"[Equipment] {item.ItemName} cannot be eaten while hunger is above 80%.");
        return false;
    }

    private IEnumerator DelayedEatUseRoutine(ItemBehaviour behaviour, ItemData item)
    {
        IsEatUseLocked = true;
        EnsurePlayerAnimator();
        SetBeefEatTransformActive(IsBeef(item));

        if (_eatAnimationRoutine != null)
        {
            StopCoroutine(_eatAnimationRoutine);
            _eatAnimationRoutine = null;
        }

        CrossFadeLayerState(EatStateHash, BaseLayerIndex, EatAnimationBlendTime);

        float effectDelay = Mathf.Max(0f, EatUseEffectDelay);
        if (effectDelay > 0f)
            yield return new WaitForSeconds(effectDelay);

        if (_currentBehaviour == behaviour && CurrentItem == item && IsDelayedEatItem(CurrentItem))
        {
            _eatUseApplyingEffect = true;
            try
            {
                behaviour.Use();
            }
            finally
            {
                _eatUseApplyingEffect = false;
            }
        }

        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, EatAnimationBlendTime);
        SetBeefEatTransformActive(false);
        IsEatUseLocked = false;
        _delayedEatUseRoutine = null;
    }

    private void CancelDelayedEatUse()
    {
        if (_delayedEatUseRoutine != null)
        {
            StopCoroutine(_delayedEatUseRoutine);
            _delayedEatUseRoutine = null;
        }

        IsEatUseLocked = false;
        _eatUseApplyingEffect = false;
        SetBeefEatTransformActive(false);
        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, EatAnimationBlendTime);
    }

    public void PlayEatAnimation()
    {
        if (IsEatUseLocked)
            return;

        if (_eatAnimationRoutine != null)
            StopCoroutine(_eatAnimationRoutine);

        _eatAnimationRoutine = StartCoroutine(EatAnimationRoutine());
    }

    private IEnumerator EatAnimationRoutine()
    {
        CrossFadeLayerState(EatStateHash, BaseLayerIndex, EatAnimationBlendTime);

        float duration = Mathf.Max(0f, EatAnimationDuration);
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, EatAnimationBlendTime);
        _eatAnimationRoutine = null;
    }

    private void CancelEatAnimation()
    {
        if (_eatAnimationRoutine != null)
        {
            StopCoroutine(_eatAnimationRoutine);
            _eatAnimationRoutine = null;
        }

        CrossFadeLayerState(LocomotionStateHash, BaseLayerIndex, EatAnimationBlendTime);
    }

    private void CrossFadeUpperBodyState(int stateHash)
    {
        CrossFadeUpperBodyState(stateHash, DrinkAnimationBlendTime);
    }

    private void CrossFadeUpperBodyState(int stateHash, float blendTime)
    {
        CrossFadeLayerState(stateHash, UpperBodyLayerIndex, blendTime);
    }

    private void CrossFadeLayerState(int stateHash, int layerIndex, float blendTime)
    {
        EnsurePlayerAnimator();
        if (PlayerAnimator == null || PlayerAnimator.layerCount <= layerIndex)
            return;

        PlayerAnimator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, blendTime), layerIndex, 0f);
    }

    private void SetBeefEatTransformActive(bool active)
    {
        if (active && (!IsBeef(CurrentItem) || _currentWeaponObj == null))
        {
            _beefEatTransformActive = false;
            _hasBeefEatRestTransform = false;
            return;
        }

        if (active)
        {
            if (!_beefEatTransformActive)
            {
                Transform heldTransform = _currentWeaponObj.transform;
                _beefEatRestLocalPosition = heldTransform.localPosition;
                _beefEatRestLocalRotation = heldTransform.localRotation;
                _beefEatRestLocalScale = heldTransform.localScale;
                _hasBeefEatRestTransform = true;
            }

            _beefEatTransformActive = true;
            ApplyBeefEatTransform();
            return;
        }

        _beefEatTransformActive = false;
        RestoreBeefEatTransform();
    }

    private void ApplyBeefEatTransform()
    {
        if (_currentWeaponObj == null)
            return;

        Transform heldTransform = _currentWeaponObj.transform;
        heldTransform.localPosition = BeefEatLocalPosition;
        heldTransform.localRotation = Quaternion.Euler(BeefEatLocalEulerAngles);
        heldTransform.localScale = BeefEatLocalScale;
    }

    private void RestoreBeefEatTransform()
    {
        if (!_hasBeefEatRestTransform || _currentWeaponObj == null)
        {
            _hasBeefEatRestTransform = false;
            return;
        }

        Transform heldTransform = _currentWeaponObj.transform;
        heldTransform.localPosition = _beefEatRestLocalPosition;
        heldTransform.localRotation = _beefEatRestLocalRotation;
        heldTransform.localScale = _beefEatRestLocalScale;
        _hasBeefEatRestTransform = false;
    }

    public void SetWoodShovelUseLocked(bool locked)
    {
        IsWoodShovelUseLocked = locked;
    }

    public void SetWoodShovelCameraLocked(bool locked)
    {
        IsWoodShovelCameraLocked = locked;
    }

    public void SetWoodShovelActionTransformActive(bool active)
    {
        if (!IsWoodShovel(CurrentItem) || _currentWeaponObj == null)
        {
            _woodShovelActionTransformActive = false;
            _hasWoodShovelRestTransform = false;
            return;
        }

        if (active)
        {
            if (!_woodShovelActionTransformActive)
            {
                Transform heldTransform = _currentWeaponObj.transform;
                _woodShovelRestLocalPosition = heldTransform.localPosition;
                _woodShovelRestLocalRotation = heldTransform.localRotation;
                _woodShovelRestLocalScale = heldTransform.localScale;
                _hasWoodShovelRestTransform = true;
            }

            _woodShovelActionTransformActive = true;
            ApplyWoodShovelActionTransform();
            return;
        }

        _woodShovelActionTransformActive = false;
        RestoreWoodShovelRestTransform();
    }

    private void ApplyWoodShovelActionTransform()
    {
        if (_currentWeaponObj == null)
            return;

        Transform heldTransform = _currentWeaponObj.transform;
        heldTransform.localPosition = WoodShovelActionLocalPosition;
        heldTransform.localRotation = Quaternion.Euler(WoodShovelActionLocalEulerAngles);
        heldTransform.localScale = WoodShovelActionLocalScale;
    }

    private void RestoreWoodShovelRestTransform()
    {
        if (!_hasWoodShovelRestTransform || _currentWeaponObj == null)
        {
            _hasWoodShovelRestTransform = false;
            return;
        }

        Transform heldTransform = _currentWeaponObj.transform;
        heldTransform.localPosition = _woodShovelRestLocalPosition;
        heldTransform.localRotation = _woodShovelRestLocalRotation;
        heldTransform.localScale = _woodShovelRestLocalScale;
        _hasWoodShovelRestTransform = false;
    }

    public void PlayWoodShovelDiggingAnimation()
    {
        CrossFadeUpperBodyState(WoodShovelDiggingStateHash, WoodShovelAnimationBlendTime);
    }

    public void HoldWoodShovelDiggingPose()
    {
        EnsurePlayerAnimator();
        if (PlayerAnimator == null || PlayerAnimator.layerCount <= UpperBodyLayerIndex)
            return;

        PlayerAnimator.Play(WoodShovelDiggingStateHash, UpperBodyLayerIndex, 0.999f);
        PlayerAnimator.Update(0f);
    }

    public void PlayWoodShovelPlaceAnimation()
    {
        CrossFadeUpperBodyState(WoodShovelPlaceStateHash, WoodShovelAnimationBlendTime);
    }

    public void ResetWoodShovelAnimation()
    {
        CrossFadeUpperBodyState(UpperBodyIdleStateHash, WoodShovelAnimationBlendTime);
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
            HasAnimatorParameter(PlayerAnimator, IsSpearAimingHash) ||
            HasAnimatorState(PlayerAnimator, UpperBodyLayerIndex, WoodShovelDiggingStateHash) ||
            HasAnimatorState(PlayerAnimator, UpperBodyLayerIndex, WoodShovelPlaceStateHash) ||
            HasAnimatorState(PlayerAnimator, BaseLayerIndex, DrinkStateHash) ||
            HasAnimatorState(PlayerAnimator, BaseLayerIndex, EatStateHash))
            return;

        PlayerAnimator = null;
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
        {
            if (HasAnimatorParameter(animator, ThrowHash) ||
                HasAnimatorParameter(animator, RawKnifeAttackHash) ||
                HasAnimatorParameter(animator, StoneSpearAttackHash) ||
                HasAnimatorParameter(animator, IsAimingHash) ||
                HasAnimatorParameter(animator, IsSpearAimingHash) ||
                HasAnimatorState(animator, UpperBodyLayerIndex, WoodShovelDiggingStateHash) ||
                HasAnimatorState(animator, UpperBodyLayerIndex, WoodShovelPlaceStateHash) ||
                HasAnimatorState(animator, BaseLayerIndex, DrinkStateHash) ||
                HasAnimatorState(animator, BaseLayerIndex, EatStateHash))
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

    private static bool HasAnimatorState(Animator animator, int layerIndex, int stateHash)
    {
        return animator != null
            && animator.layerCount > layerIndex
            && animator.HasState(layerIndex, stateHash);
    }
}
