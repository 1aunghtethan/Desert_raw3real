using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Unified player controller — handles movement, mouse look (FP), jump, and crouch.
/// Works in both first-person and third-person camera modes.
/// Movement direction is always relative to the currently active camera.
/// </summary>
public class PlayerController : MonoBehaviour
{
    public enum CameraMode { FirstPerson, ThirdPerson }

    [Header("Current Mode")]
    public CameraMode CurrentMode = CameraMode.ThirdPerson;

    // ─── Camera References ───
    [Header("Cameras")]
    [Tooltip("The first-person camera (child of player at head height).")]
    public Camera FPCamera;
    [Tooltip("The third-person camera (has ThirdPersonCamera script).")]
    public Camera TPCamera;
    [Tooltip("Reference to the ThirdPersonCamera orbit script on the TP camera.")]
    public ThirdPersonCamera TPCameraController;

    // ─── Movement ───
    [Header("Movement")]
    public float WalkSpeed = 5f;
    public float RunSpeed = 9f;
    public KeyCode RunKey = KeyCode.LeftShift;

    [Header("Run Exhaustion")]
    public float HotRunStartHour = 7f;
    public float HotRunEndHour = 16.5f;
    public float HotStraightRunLimit = 20f;
    public float NormalStraightRunLimit = 40f;
    public float RunExhaustionLockDuration = 10f;
    public float HeartbeatDuration = 12f;
    public float HeartbeatFadeOutDuration = 3f;

    // ─── Mouse Look (First Person) ───
    [Header("First Person Mouse Look")]
    public float MouseSensitivity = 2f;
    public float MouseSmoothing = 1.5f;
    [Tooltip("Camera local position when in first person.")]
    public Vector3 FPCameraLocalPos = new Vector3(0, 1.5f, 0);
    [Tooltip("Field of View for First Person camera.")]
    public float FPFieldOfView = 60f;

    private Vector2 _lookVelocity;
    private Vector2 _lookFrameVelocity;

    // ─── Jump ───
    [Header("Jump")]
    public float JumpForce = 6.5f;
    public float GroundCheckDistance = 0.15f;
    [Tooltip("Jump buffering is disabled by default so airborne presses do not trigger a later jump.")]
    public float JumpBufferSeconds = 0f;

    [Header("Running Jump Sync")]
    public bool SyncRunningJumpToAnimationEndpoint = true;
    public AnimationClip RunningJumpClip;
    [Tooltip("0 uses the larger of the clip root motion distance and current running momentum.")]
    public float RunningJumpForwardDistance = 0f;
    public float RunningJumpDistanceScale = 1f;
    public float RunningJumpMaxHorizontalSpeedMultiplier = 1.35f;
    public float RunningJumpGroundProbeDistance = 0.35f;

    // ─── Crouch ───
    [Header("Crouch")]
    public KeyCode CrouchKey = KeyCode.LeftControl;
    public float CrouchSpeed = 2f;
    public float CrouchHeadHeight = 1f;
    private float _defaultHeadHeight;
    private float _defaultColliderHeight;
    private bool _isCrouched;

    // ─── Rotation Smoothing (TP) ───
    [Header("Third Person")]
    [Tooltip("How fast the player turns in third person. Set to 0 for instant rotation with no walk delay.")]
    public float TPRotationSpeed = 0f;

    // ─── Animation ───
    private Animator _animator;
    private InteractionManager _interactionManager;
    private ItemPlacer _itemPlacer;
    private cyclemanager _cycle;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    // ─── Private ───
    private Rigidbody _rb;
    private CapsuleCollider _capsule;
    private bool _isRunning;
    private Vector2 _moveInput;
    private bool _grounded;
    private bool _landingImpactPending;
    private float _landingImpactPlayTime;
    private int _groundedFrameSkip;
    private float _footstepTimer;
    private float _footstepMuteUntil;
    private float _groundedMuteUntil;
    private bool _jumpRequestedThisFrame;
    private float _straightRunTimer;
    private float _runLockedUntil;
    private Vector3 _lastAnimationPosition;
    private bool _hasLastAnimationPosition;
    private bool _isActuallyMoving;
    private bool _syncingRunningJumpEndpoint;
    private Vector3 _runningJumpDirection;
    private Vector3 _runningJumpEndpoint;
    private float _runningJumpSyncEndTime;
    private bool _runningJumpTouchedGround;
    private const float FootstepMoveInputThreshold = 0.01f;
    private const float ActualMoveSpeedThreshold = 0.05f;
    private const float JumpFootstepMuteSeconds = 0.2f;
    private const float LandingImpactDelaySeconds = 0.41f;
    private const float LandingGroundNormalThreshold = 0.45f;
    private const float RunningJumpFallbackDuration = 0.85f;
    private bool IsThrowLocked => EquipmentHolder.Instance != null && EquipmentHolder.Instance.IsThrowAnimationLocked;
    private bool IsEquipmentJumpLocked => EquipmentHolder.Instance != null &&
        (EquipmentHolder.Instance.IsDrinkUseLocked || EquipmentHolder.Instance.IsEatUseLocked || EquipmentHolder.Instance.IsWoodShovelUseLocked);

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _animator = GetComponentInChildren<Animator>();
        _interactionManager = GetComponent<InteractionManager>();
        _itemPlacer = GetComponent<ItemPlacer>();
        _cycle = FindFirstObjectByType<cyclemanager>();
    }

    void Start()
    {
        // Auto-find cameras if not assigned
        if (FPCamera == null || TPCamera == null)
        {
            Camera[] cams = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cams)
            {
                if (cam.GetComponent<ThirdPersonCamera>() != null)
                    TPCamera = cam;
                else
                    FPCamera = cam;
            }
            // Also check scene root for TP camera
            if (TPCamera == null)
            {
                ThirdPersonCamera tpc = FindFirstObjectByType<ThirdPersonCamera>(FindObjectsInactive.Include);
                if (tpc != null) TPCamera = tpc.GetComponent<Camera>();
            }
        }

        if (TPCamera != null && TPCameraController == null)
            TPCameraController = TPCamera.GetComponent<ThirdPersonCamera>();

        // Ensure TP camera knows its target
        if (TPCameraController != null)
            TPCameraController.Target = transform;

        // Store defaults for crouch
        if (FPCamera != null)
            _defaultHeadHeight = FPCameraLocalPos.y;
        if (_capsule != null)
            _defaultColliderHeight = _capsule.height;

        // Initialize FP look from current rotation
        Vector3 euler = transform.eulerAngles;
        _lookVelocity = new Vector2(euler.y, 0f);

        // Lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Try to find animator again if head-child finding failed in Awake
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // Disable root motion to prevent sinking/unintended movement
        if (_animator != null) _animator.applyRootMotion = false;
    }

    void Update()
    {
        HandleMouseLook();
        CaptureJumpInput();
        HandleJump();
        if (!IsThrowLocked)
            HandleCrouch();

        // Grounded check (throttled raycast — only 12 times/sec)
        bool grounded = IsGrounded();
        float actualHorizontalSpeed = GetActualHorizontalSpeed();
        _isActuallyMoving = actualHorizontalSpeed > ActualMoveSpeedThreshold;

        // Update Animator speed and grounded state
        if (_animator != null)
        {
            bool throwLocked = IsThrowLocked;
            Vector2 animationMoveInput = !throwLocked && _isActuallyMoving ? _moveInput : Vector2.zero;
            _animator.SetFloat(SpeedHash, throwLocked ? 0f : actualHorizontalSpeed);
            _animator.SetFloat(MoveXHash, animationMoveInput.x);
            _animator.SetFloat(MoveYHash, animationMoveInput.y);
            _animator.SetBool(GroundedHash, grounded);
        }

        HandleFootstepAudio(grounded, _isActuallyMoving);
        PlayPendingLandingImpactSound();

        // Apply FP FOV
        if (CurrentMode == CameraMode.FirstPerson && FPCamera != null)
        {
            FPCamera.fieldOfView = FPFieldOfView;
        }
    }

    void FixedUpdate()
    {
        HandleMovement();
    }

    void LateUpdate()
    {
        EnsureThirdPersonCameraTarget();

        if (_syncingRunningJumpEndpoint)
        {
            if (CurrentMode == CameraMode.ThirdPerson && TPCameraController != null)
                TPCameraController.ForceFollowTargetNow();
        }
    }

    // ═══════════════════════════════════════════════
    // MOVEMENT — works in both modes
    // ═══════════════════════════════════════════════
    private float GetActualHorizontalSpeed()
    {
        Vector3 currentPosition = transform.position;
        if (!_hasLastAnimationPosition)
        {
            _lastAnimationPosition = currentPosition;
            _hasLastAnimationPosition = true;
            return 0f;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = currentPosition - _lastAnimationPosition;
        delta.y = 0f;
        _lastAnimationPosition = currentPosition;
        return delta.magnitude / deltaTime;
    }

    private void HandleFootstepAudio(bool grounded, bool actuallyMoving)
    {
        bool isJumping = _animator != null &&
            (_animator.GetCurrentAnimatorStateInfo(0).IsName("Jump") ||
             _animator.GetCurrentAnimatorStateInfo(0).IsName("RunningJump"));
        bool isPickingUp = _interactionManager != null && _interactionManager.IsPickupInProgress;
        bool canPlayFootsteps = actuallyMoving && grounded && Time.time >= _footstepMuteUntil && !isJumping && !isPickingUp;

        if (canPlayFootsteps)
        {
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                AudioManager.Instance.PlayFootstep(_isRunning);
                _footstepTimer = _isRunning ? 0.3f : 0.5f;
            }
        }
        else
        {
            _footstepTimer = 0f;
            AudioManager.Current?.StopFootsteps();
        }
    }

    void HandleMovement()
    {
        if (IsThrowLocked)
        {
            StopRunningJumpEndpointSync();
            _isRunning = false;
            _moveInput = Vector2.zero;
            _straightRunTimer = 0f;

            Vector3 lockedVelocity = _rb.linearVelocity;
            lockedVelocity.x = 0f;
            lockedVelocity.z = 0f;
            _rb.linearVelocity = lockedVelocity;
            return;
        }

        bool runLocked = IsRunMovementLocked();
        bool placementRunLocked = IsPlacementRunLocked();
        bool exhaustionRunLocked = IsRunExhaustionLocked();
        Vector2 rawMoveInput = ReadRawMoveInput();

        bool hasMoveInput = rawMoveInput.sqrMagnitude > FootstepMoveInputThreshold;
        bool wantsRun = Input.GetKey(RunKey) && hasMoveInput && !_isCrouched;
        _isRunning = wantsRun && !runLocked && !placementRunLocked && !exhaustionRunLocked;

        UpdateRunExhaustion();

        float speed = CalculateCurrentMovementSpeed(_isRunning);

        float runBlend = _isRunning ? 2f : 1f;
        _moveInput = rawMoveInput * runBlend;

        Vector3 moveDir = GetCameraRelativeMoveDirection(rawMoveInput);

        if (TryApplyRunningJumpEndpointVelocity())
        {
            RotateThirdPersonBody(_runningJumpDirection);
            return;
        }

        Vector3 velocity = moveDir * speed;
        velocity.y = _rb.linearVelocity.y; // preserve gravity
        _rb.linearVelocity = velocity;

        // In TP mode, rotate player body to face camera direction or movement direction
        Vector3 targetForward = Vector3.forward;
        if (moveDir.sqrMagnitude > 0.01f)
        {
            targetForward = moveDir;
        }
        else if (TPCameraController != null)
        {
            targetForward = TPCameraController.GetFlatForward();
        }
        else if (TPCamera != null)
        {
            targetForward = TPCamera.transform.forward;
            targetForward.y = 0;
            targetForward.Normalize();
        }

        RotateThirdPersonBody(targetForward);
    }

    private Vector2 ReadRawMoveInput()
    {
        Vector2 rawMoveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        if (rawMoveInput.sqrMagnitude > 1f) rawMoveInput.Normalize();
        return rawMoveInput;
    }

    private Vector3 GetCameraRelativeMoveDirection(Vector2 rawMoveInput)
    {
        Vector3 moveDir;

        if (CurrentMode == CameraMode.FirstPerson)
        {
            moveDir = transform.forward * rawMoveInput.y + transform.right * rawMoveInput.x;
        }
        else
        {
            Vector3 camForward;
            Vector3 camRight;
            if (TPCameraController != null)
            {
                camForward = TPCameraController.GetFlatForward();
                camRight = TPCameraController.GetFlatRight();
            }
            else if (TPCamera != null)
            {
                camForward = TPCamera.transform.forward;
                camForward.y = 0f;
                camForward.Normalize();
                camRight = TPCamera.transform.right;
                camRight.y = 0f;
                camRight.Normalize();
            }
            else
            {
                camForward = transform.forward;
                camRight = transform.right;
            }
            moveDir = camForward * rawMoveInput.y + camRight * rawMoveInput.x;
        }

        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        return moveDir;
    }

    private float CalculateCurrentMovementSpeed(bool running)
    {
        float speed = running ? RunSpeed : WalkSpeed;
        if (_isCrouched) speed = CrouchSpeed;

        if (EquipmentHolder.Instance != null)
        {
            speed *= EquipmentHolder.Instance.MovementSpeedMultiplier;
        }

        if (_interactionManager != null)
        {
            speed *= _interactionManager.MovementSpeedMultiplier;
        }

        if (_itemPlacer != null)
        {
            speed *= _itemPlacer.ActivePlacementMoveMultiplier;
        }

        if (MountainSpawner.Instance != null && TerrainManager.Instance != null && TerrainManager.Instance.Config != null)
        {
            float influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(transform.position);
            float multiplier = Mathf.Lerp(1f, TerrainManager.Instance.Config.MountainSpeedMultiplier, influence);
            speed *= multiplier;
        }

        return speed;
    }

    private bool IsRunMovementLocked()
    {
        return EquipmentHolder.Instance != null &&
            (EquipmentHolder.Instance.IsAimModeActive ||
             EquipmentHolder.Instance.IsStoneSpearAttackActive ||
             EquipmentHolder.Instance.IsDrinkUseLocked ||
             EquipmentHolder.Instance.IsEatUseLocked ||
             EquipmentHolder.Instance.IsWoodShovelUseLocked);
    }

    private bool IsPlacementRunLocked()
    {
        return _itemPlacer != null && _itemPlacer.LockRunForActivePlacement;
    }

    private bool IsRunExhaustionLocked()
    {
        return Time.time < _runLockedUntil;
    }

    private void RotateThirdPersonBody(Vector3 targetForward)
    {
        if (CurrentMode == CameraMode.ThirdPerson)
        {
            if (targetForward.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(targetForward, Vector3.up);
                transform.rotation = TPRotationSpeed > 0f
                    ? Quaternion.Slerp(transform.rotation, targetRot, Time.fixedDeltaTime * TPRotationSpeed)
                    : targetRot;
            }
        }
    }

    // ═══════════════════════════════════════════════
    // MOUSE LOOK — only active in First Person
    // ═══════════════════════════════════════════════
    private void EnsureThirdPersonCameraTarget()
    {
        if (TPCameraController == null)
        {
            if (TPCamera != null)
                TPCameraController = TPCamera.GetComponent<ThirdPersonCamera>();

            if (TPCameraController == null)
            {
                ThirdPersonCamera tpc = FindFirstObjectByType<ThirdPersonCamera>(FindObjectsInactive.Include);
                if (tpc != null)
                {
                    TPCameraController = tpc;
                    TPCamera = tpc.GetComponent<Camera>();
                }
            }
        }

        if (TPCameraController != null && TPCameraController.Target != transform)
            TPCameraController.Target = transform;
    }

    private void UpdateRunExhaustion()
    {
        if (!_isRunning || !_isActuallyMoving)
        {
            _straightRunTimer = 0f;
            return;
        }

        _straightRunTimer += Time.fixedDeltaTime;
        if (_straightRunTimer < GetCurrentStraightRunLimit())
            return;

        _runLockedUntil = Time.time + Mathf.Max(0f, RunExhaustionLockDuration);
        _straightRunTimer = 0f;
        _isRunning = false;
        AudioManager.Instance.PlayHeartbeatLoop(HeartbeatDuration, HeartbeatFadeOutDuration);
    }

    private float GetCurrentStraightRunLimit()
    {
        float limit = IsHotRunTime() ? HotStraightRunLimit : NormalStraightRunLimit;
        return Mathf.Max(0.01f, limit);
    }

    private bool IsHotRunTime()
    {
        if (_cycle == null)
            return false;

        float currentHour = Mathf.Repeat(_cycle.currentTime, 24f);
        return currentHour >= HotRunStartHour && currentHour <= HotRunEndHour;
    }

    void HandleMouseLook()
    {
        if (CurrentMode != CameraMode.FirstPerson) return;
        if (FPCamera == null) return;
        if (EquipmentHolder.Instance != null && EquipmentHolder.Instance.IsWoodShovelCameraLocked)
        {
            _lookFrameVelocity = Vector2.zero;
            return;
        }

        Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        Vector2 rawFrameVel = mouseDelta * MouseSensitivity;
        _lookFrameVelocity = Vector2.Lerp(_lookFrameVelocity, rawFrameVel, 1f / MouseSmoothing);
        _lookVelocity += _lookFrameVelocity;
        _lookVelocity.y = Mathf.Clamp(_lookVelocity.y, -90f, 90f);

        // Yaw → rotate player body
        transform.localRotation = Quaternion.AngleAxis(_lookVelocity.x, Vector3.up);

        // Pitch → rotate FP camera
        FPCamera.transform.localRotation = Quaternion.AngleAxis(-_lookVelocity.y, Vector3.right);
    }

    // ═══════════════════════════════════════════════
    // JUMP
    // ═══════════════════════════════════════════════
    private void CaptureJumpInput()
    {
        _jumpRequestedThisFrame = Input.GetButtonDown("Jump");
    }

    void HandleJump()
    {
        if (!_jumpRequestedThisFrame)
            return;

        _jumpRequestedThisFrame = false;

        bool pickupLocked = _interactionManager != null && _interactionManager.IsPickupInProgress;
        bool placementJumpLocked = _itemPlacer != null && _itemPlacer.LockJumpForActivePlacement;
        if (IsThrowLocked || pickupLocked || IsEquipmentJumpLocked || placementJumpLocked || _syncingRunningJumpEndpoint || IsJumpAnimationActive() || !IsGroundedImmediate())
        {
            ResetJumpTrigger();
            return;
        }

        Vector2 rawMoveInput = ReadRawMoveInput();
        Vector3 jumpMoveDirection = GetCameraRelativeMoveDirection(rawMoveInput);
        bool shouldSyncRunningJump = CanStartRunningJumpEndpointSync(rawMoveInput, jumpMoveDirection);

        _rb.AddForce(Vector3.up * JumpForce);
        _groundedMuteUntil = Time.time + 0.15f;
        _footstepTimer = 0f;
        _footstepMuteUntil = Time.time + JumpFootstepMuteSeconds;
        AudioManager.Current?.StopFootsteps();

        if (shouldSyncRunningJump)
        {
            StartRunningJumpEndpointSync(jumpMoveDirection, CalculateCurrentMovementSpeed(true));
        }

        if (_animator != null)
        {
            _animator.ResetTrigger(JumpHash);
            _animator.SetTrigger(JumpHash);
        }
        ScheduleLandingImpactAfterJumpAnimation();
    }

    private void ResetJumpTrigger()
    {
        if (_animator != null)
            _animator.ResetTrigger(JumpHash);
    }

    private bool IsJumpAnimationActive()
    {
        if (_animator == null)
            return false;

        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (IsJumpAnimationState(current))
            return true;

        return _animator.IsInTransition(0) && IsJumpAnimationState(_animator.GetNextAnimatorStateInfo(0));
    }

    private static bool IsJumpAnimationState(AnimatorStateInfo state)
    {
        return state.IsName("Jump") || state.IsName("RunningJump");
    }

    private bool CanStartRunningJumpEndpointSync(Vector2 rawMoveInput, Vector3 jumpMoveDirection)
    {
        if (!SyncRunningJumpToAnimationEndpoint || _isCrouched)
            return false;

        if (!Input.GetKey(RunKey) || rawMoveInput.sqrMagnitude <= FootstepMoveInputThreshold)
            return false;

        if (jumpMoveDirection.sqrMagnitude <= 0.01f)
            return false;

        if (IsThrowLocked || IsEquipmentJumpLocked || IsRunMovementLocked() || IsPlacementRunLocked() || IsRunExhaustionLocked())
            return false;

        return _itemPlacer == null || !_itemPlacer.LockJumpForActivePlacement;
    }

    private void StartRunningJumpEndpointSync(Vector3 direction, float fallbackSpeed)
    {
        float duration = GetRunningJumpSyncDuration();
        float distance = GetRunningJumpSyncDistance(duration, fallbackSpeed);
        if (duration <= 0f || distance <= 0f)
            return;

        _runningJumpDirection = direction.normalized;
        _runningJumpEndpoint = transform.position + _runningJumpDirection * distance;
        _runningJumpSyncEndTime = Time.time + duration;
        _runningJumpTouchedGround = false;
        _syncingRunningJumpEndpoint = true;
    }

    private bool TryApplyRunningJumpEndpointVelocity()
    {
        if (!_syncingRunningJumpEndpoint)
            return false;

        if (ShouldStopRunningJumpEndpointSync())
        {
            StopRunningJumpEndpointSync();
            return false;
        }

        Vector3 toEndpoint = _runningJumpEndpoint - transform.position;
        toEndpoint.y = 0f;
        float remainingForwardDistance = Vector3.Dot(toEndpoint, _runningJumpDirection);
        if (remainingForwardDistance <= 0f)
        {
            StopRunningJumpEndpointSync();
            return false;
        }

        float remainingTime = Mathf.Max(_runningJumpSyncEndTime - Time.time, Time.fixedDeltaTime);
        float horizontalSpeed = Mathf.Min(remainingForwardDistance / remainingTime, GetRunningJumpMaxHorizontalSpeed());
        Vector3 velocity = _runningJumpDirection * horizontalSpeed;
        velocity.y = _rb.linearVelocity.y;
        _rb.linearVelocity = velocity;

        return true;
    }

    private bool ShouldStopRunningJumpEndpointSync()
    {
        if (Time.time >= _runningJumpSyncEndTime || _runningJumpDirection.sqrMagnitude <= 0.01f)
            return true;

        if (IsThrowLocked || IsEquipmentJumpLocked || IsRunMovementLocked() || IsPlacementRunLocked() || IsRunExhaustionLocked())
            return true;

        if (_itemPlacer != null && _itemPlacer.LockJumpForActivePlacement)
            return true;

        return Time.time >= _groundedMuteUntil && (_runningJumpTouchedGround || IsRunningJumpGroundedNow());
    }

    private float GetRunningJumpMaxHorizontalSpeed()
    {
        float baseSpeed = Mathf.Max(RunSpeed, CalculateCurrentMovementSpeed(true));
        return baseSpeed * Mathf.Max(0.1f, RunningJumpMaxHorizontalSpeedMultiplier);
    }

    private bool IsRunningJumpGroundedNow()
    {
        if (_rb.linearVelocity.y > 0.05f)
            return false;

        float probeDistance = Mathf.Max(GroundCheckDistance * 2f, RunningJumpGroundProbeDistance);
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        return Physics.Raycast(origin, Vector3.down, probeDistance, ~0, QueryTriggerInteraction.Ignore);
    }

    private void StopRunningJumpEndpointSync()
    {
        _syncingRunningJumpEndpoint = false;
        _runningJumpTouchedGround = false;
    }

    private float GetGroundClearance()
    {
        if (_capsule == null)
            return 1f;

        float scaledCenterY = _capsule.center.y * transform.lossyScale.y;
        float scaledHalfHeight = _capsule.height * 0.5f * transform.lossyScale.y;
        return Mathf.Max(0f, scaledHalfHeight - scaledCenterY);
    }

    private float GetRunningJumpSyncDuration()
    {
        if (RunningJumpClip != null && RunningJumpClip.length > 0f)
            return RunningJumpClip.length;

        return RunningJumpFallbackDuration;
    }

    private float GetRunningJumpSyncDistance(float duration, float fallbackSpeed)
    {
        float distance = RunningJumpForwardDistance;
        float momentumDistance = fallbackSpeed * duration;
#if UNITY_EDITOR
        if (distance <= 0f)
            distance = MeasureRunningJumpRootDistance();
#endif
        if (distance <= 0f && RunningJumpClip != null)
        {
            Vector3 averageSpeed = RunningJumpClip.averageSpeed;
            averageSpeed.y = 0f;
            distance = averageSpeed.magnitude * duration;
        }

        if (distance <= 0f)
            distance = momentumDistance;
        else
            distance = Mathf.Max(distance, momentumDistance);

        return Mathf.Max(0f, distance * Mathf.Max(0f, RunningJumpDistanceScale));
    }

#if UNITY_EDITOR
    private float MeasureRunningJumpRootDistance()
    {
        if (RunningJumpClip == null)
            return 0f;

        bool hasX = TryGetRootCurveEndpoints("RootT.x", out float firstX, out float lastX);
        bool hasZ = TryGetRootCurveEndpoints("RootT.z", out float firstZ, out float lastZ);

        if (hasZ && Mathf.Abs(lastZ - firstZ) > 0.001f)
            return Mathf.Abs(lastZ - firstZ);

        if (hasX || hasZ)
            return Vector2.Distance(new Vector2(firstX, firstZ), new Vector2(lastX, lastZ));

        return 0f;
    }

    private bool TryGetRootCurveEndpoints(string propertyName, out float firstValue, out float lastValue)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(RunningJumpClip))
        {
            if (binding.propertyName != propertyName)
                continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(RunningJumpClip, binding);
            if (curve == null || curve.keys.Length == 0)
                break;

            firstValue = curve.keys[0].value;
            lastValue = curve.keys[curve.keys.Length - 1].value;
            return true;
        }

        firstValue = 0f;
        lastValue = 0f;
        return false;
    }
#endif

    private void OnCollisionEnter(Collision collision)
    {
        RegisterRunningJumpGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RegisterRunningJumpGroundContact(collision);
    }

    private void RegisterRunningJumpGroundContact(Collision collision)
    {
        if (!_syncingRunningJumpEndpoint)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y < LandingGroundNormalThreshold)
                continue;

            _runningJumpTouchedGround = true;
            return;
        }
    }

    private void ScheduleLandingImpactAfterJumpAnimation()
    {
        _landingImpactPending = true;
        _landingImpactPlayTime = Time.time + LandingImpactDelaySeconds;
        _footstepMuteUntil = _landingImpactPlayTime + JumpFootstepMuteSeconds;
    }

    private void PlayPendingLandingImpactSound()
    {
        if (!_landingImpactPending || Time.time < _landingImpactPlayTime)
            return;

        _landingImpactPending = false;
        AudioManager.Instance.PlaySFX(AudioManager.Instance.landingImpact);
    }

    private bool IsGrounded()
    {
        if (Time.time < _groundedMuteUntil)
        {
            _grounded = false;
            _groundedFrameSkip = 5;
            return false;
        }

        _groundedFrameSkip++;
        if (_groundedFrameSkip >= 5)
        {
            _groundedFrameSkip = 0;
            _grounded = Physics.Raycast(
                transform.position + Vector3.up * 0.01f,
                Vector3.down,
                GroundCheckDistance * 2f
            );
        }
        return _grounded;
    }

    private bool IsGroundedImmediate()
    {
        if (Time.time < _groundedMuteUntil)
        {
            _grounded = false;
            _groundedFrameSkip = 5;
            return false;
        }

        _grounded = Physics.Raycast(
            transform.position + Vector3.up * 0.01f,
            Vector3.down,
            GroundCheckDistance * 2f,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        _groundedFrameSkip = 0;
        return _grounded;
    }

    // ═══════════════════════════════════════════════
    // CROUCH
    // ═══════════════════════════════════════════════
    void HandleCrouch()
    {
        if (Input.GetKey(CrouchKey))
        {
            if (!_isCrouched)
            {
                _isCrouched = true;

                // Lower FP camera
                if (FPCamera != null && CurrentMode == CameraMode.FirstPerson)
                {
                    Vector3 pos = FPCamera.transform.localPosition;
                    pos.y = CrouchHeadHeight;
                    FPCamera.transform.localPosition = pos;
                }

                // Shrink capsule
                if (_capsule != null)
                {
                    float lowering = _defaultHeadHeight - CrouchHeadHeight;
                    _capsule.height = Mathf.Max(_defaultColliderHeight - lowering, 0.1f);
                    _capsule.center = Vector3.up * _capsule.height * 0.5f;
                }
            }
        }
        else if (_isCrouched)
        {
            _isCrouched = false;

            // Restore FP camera
            if (FPCamera != null)
            {
                FPCamera.transform.localPosition = FPCameraLocalPos;
            }

            // Restore capsule
            if (_capsule != null)
            {
                _capsule.height = _defaultColliderHeight;
                _capsule.center = Vector3.up * _capsule.height * 0.5f;
            }
        }
    }

    // ═══════════════════════════════════════════════
    // PUBLIC — called by CameraModeSwitcher
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Called by CameraModeSwitcher when switching modes.
    /// Syncs internal look state for smooth transitions.
    /// </summary>
    public void SetMode(CameraMode mode)
    {
        CameraMode oldMode = CurrentMode;
        CurrentMode = mode;

        if (mode == CameraMode.FirstPerson)
        {
            // Sync FP look from current camera orientation
            SyncLookFromCamera();
        }
    }

    /// <summary>
    /// Seeds internal mouse look velocity from the current camera world rotation.
    /// Called when switching to FP mode for a smooth transition.
    /// </summary>
    void SyncLookFromCamera()
    {
        // Use whichever camera was last active (TP camera view angle)
        Camera srcCam = (TPCamera != null && TPCamera.gameObject.activeInHierarchy) ? TPCamera : FPCamera;
        if (srcCam == null) return;

        Vector3 euler = srcCam.transform.eulerAngles;
        float yaw = euler.y;
        float pitch = euler.x;
        if (pitch > 180f) pitch -= 360f;

        _lookVelocity = new Vector2(yaw, pitch);
        _lookFrameVelocity = Vector2.zero;

        // Set player body yaw immediately
        transform.rotation = Quaternion.Euler(0, yaw, 0);

        // Set FP camera pitch
        if (FPCamera != null)
        {
            FPCamera.transform.localRotation = Quaternion.AngleAxis(-pitch, Vector3.right);
        }
    }

    /// <summary>
    /// Returns the currently active camera.
    /// </summary>
    public Camera GetActiveCamera()
    {
        if (CurrentMode == CameraMode.FirstPerson) return FPCamera;
        return TPCamera;
    }
}
