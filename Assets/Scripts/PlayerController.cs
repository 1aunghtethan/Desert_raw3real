using UnityEngine;

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
    public float JumpForce = 200f;
    public float GroundCheckDistance = 0.15f;

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
    public float TPRotationSpeed = 10f;

    // ─── Animation ───
    private Animator _animator;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    // ─── Private ───
    private Rigidbody _rb;
    private CapsuleCollider _capsule;
    private bool _isRunning;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _animator = GetComponentInChildren<Animator>();
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
        if (TPCameraController != null && TPCameraController.Target == null)
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
        HandleJump();
        HandleCrouch();

        // Update Animator speed and grounded state
        if (_animator != null)
        {
            float horizontalVel = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z).magnitude;
            _animator.SetFloat(SpeedHash, horizontalVel);

            bool grounded = IsGrounded();
            _animator.SetBool(GroundedHash, grounded);
        }

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

    // ═══════════════════════════════════════════════
    // MOVEMENT — works in both modes
    // ═══════════════════════════════════════════════
    void HandleMovement()
    {
        _isRunning = Input.GetKey(RunKey);
        float speed = _isRunning ? RunSpeed : WalkSpeed;
        if (_isCrouched) speed = CrouchSpeed;

        if (MountainSpawner.Instance != null && TerrainManager.Instance != null && TerrainManager.Instance.Config != null)
        {
            float influence = MountainSpawner.Instance.GetMountainInfluenceAtPoint(transform.position);
            float multiplier = Mathf.Lerp(1f, TerrainManager.Instance.Config.MountainSpeedMultiplier, influence);
            speed *= multiplier;
        }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 moveDir;

        if (CurrentMode == CameraMode.FirstPerson)
        {
            // FP: move relative to player's facing direction
            moveDir = transform.forward * v + transform.right * h;
        }
        else
        {
            // TP: move relative to the TP camera's flat direction
            Vector3 camForward, camRight;
            if (TPCameraController != null)
            {
                camForward = TPCameraController.GetFlatForward();
                camRight = TPCameraController.GetFlatRight();
            }
            else if (TPCamera != null)
            {
                camForward = TPCamera.transform.forward;
                camForward.y = 0; camForward.Normalize();
                camRight = TPCamera.transform.right;
                camRight.y = 0; camRight.Normalize();
            }
            else
            {
                camForward = transform.forward;
                camRight = transform.right;
            }
            moveDir = camForward * v + camRight * h;
        }

        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        Vector3 velocity = moveDir * speed;
        velocity.y = _rb.linearVelocity.y; // preserve gravity
        _rb.linearVelocity = velocity;

        // In TP mode, rotate player body to face camera direction or movement direction
        if (CurrentMode == CameraMode.ThirdPerson)
        {
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

            if (targetForward.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(targetForward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.fixedDeltaTime * TPRotationSpeed);
            }
        }
    }

    // ═══════════════════════════════════════════════
    // MOUSE LOOK — only active in First Person
    // ═══════════════════════════════════════════════
    void HandleMouseLook()
    {
        if (CurrentMode != CameraMode.FirstPerson) return;
        if (FPCamera == null) return;

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
    void HandleJump()
    {
        if (!Input.GetButtonDown("Jump")) return;

        if (IsGrounded())
        {
            _rb.AddForce(Vector3.up * JumpForce);
            if (_animator != null) _animator.SetTrigger(JumpHash);
        }
    }

    private bool IsGrounded()
    {
        // Ground check via raycast
        return Physics.Raycast(
            transform.position + Vector3.up * 0.01f,
            Vector3.down,
            GroundCheckDistance * 2f
        );
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
