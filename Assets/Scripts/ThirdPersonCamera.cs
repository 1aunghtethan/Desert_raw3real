using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform Target; // The player to follow

    [Header("Distance")]
    public float Distance = 5.0f;
    public float MinDistance = 1.5f;
    public float MaxDistance = 15.0f;
    public float ScrollSpeed = 2.0f;

    [Header("Rotation")]
    public float MouseSensitivity = 3.0f;
    public float MinVerticalAngle = -30f;
    public float MaxVerticalAngle = 70f;

    [Header("Smoothing")]
    public float PositionSmoothTime = 0f;
    public float RotationSmoothTime = 0f;

    [Header("Field of View")]
    public float FOV = 60f;

    [Header("Collision")]
    public float CollisionRadius = 0.3f;
    public LayerMask CollisionLayers = ~0; // Everything by default

    [Header("Offset")]
    [Tooltip("Camera target height above the player feet.")]
    public float CameraHeight = 1.5f;
    public float MinCameraHeight = 0.5f;
    public float MaxCameraHeight = 4f;
    public Vector3 TargetOffset = new Vector3(0, 1.5f, 0); // Look above player feet
    [Tooltip("Pushes the camera to the right (over the shoulder).")]
    public float RightOffset = 0.5f;

    [HideInInspector] public float Yaw;
    [HideInInspector] public float Pitch = 15f;
    private float _currentDistance;
    private Vector3 _smoothVelocity;
    private bool _cursorLocked = true;
    private Camera _cam;

    void Start()
    {
        _cam = GetComponent<Camera>();
        
        // Unparent from player so player body rotation doesn't affect camera
        transform.SetParent(null);

        _currentDistance = Distance;
        
        // Initialize rotation from current camera angle
        Vector3 angles = transform.eulerAngles;
        Yaw = angles.y;
        Pitch = angles.x;
        if (Pitch > 180f) Pitch -= 360f;

        LockCursor(true);
    }

    void LateUpdate()
    {
        if (Target == null) return;

        HandleInput();
        UpdateCameraPosition(false);
        
        // Apply FOV
        if (_cam != null) _cam.fieldOfView = FOV;
    }

    void HandleInput()
    {
        // Toggle cursor lock with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _cursorLocked = !_cursorLocked;
            LockCursor(_cursorLocked);
        }

        // Click to re-lock
        if (!_cursorLocked && Input.GetMouseButtonDown(0))
        {
            _cursorLocked = true;
            LockCursor(true);
        }

        if (!_cursorLocked) return;
        if (EquipmentHolder.Instance != null && EquipmentHolder.Instance.IsWoodShovelCameraLocked)
            return;

        // Mouse rotation
        float mouseX = Input.GetAxis("Mouse X") * MouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * MouseSensitivity;

        Yaw += mouseX;
        Pitch -= mouseY;
        Pitch = Mathf.Clamp(Pitch, MinVerticalAngle, MaxVerticalAngle);

        // Scroll wheel zoom effect disabled per user request
        // float scroll = Input.GetAxis("Mouse ScrollWheel");
        // if (Mathf.Abs(scroll) > 0.01f)
        // {
        //     Distance -= scroll * ScrollSpeed;
        //     Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        // }
    }

    public void ForceFollowTargetNow()
    {
        if (Target == null) return;

        _smoothVelocity = Vector3.zero;
        UpdateCameraPosition(true);
        if (_cam != null) _cam.fieldOfView = FOV;
    }

    void UpdateCameraPosition(bool instant)
    {
        TargetOffset.y = CameraHeight;
        Vector3 baseTargetPos = Target.position + TargetOffset;

        // Calculate desired camera rotation from angles
        Quaternion targetRotation = Quaternion.Euler(Pitch, Yaw, 0);
        
        // Add the right offset relative to the camera's rotation (over the shoulder)
        Vector3 targetPos = baseTargetPos + (targetRotation * Vector3.right * RightOffset);

        Vector3 desiredPosition = targetPos - (targetRotation * Vector3.forward * Distance);

        // Camera collision: raycast from target to desired position
        float finalDistance = Distance;
        Vector3 direction = (desiredPosition - targetPos).normalized;

        if (Physics.SphereCast(targetPos, CollisionRadius, direction, out RaycastHit hit, Distance, CollisionLayers))
        {
            finalDistance = hit.distance - CollisionRadius * 0.5f;
            finalDistance = Mathf.Max(finalDistance, MinDistance * 0.5f);
        }

        _currentDistance = finalDistance;

        // Final position
        Vector3 finalPosition = targetPos - (targetRotation * Vector3.forward * _currentDistance);  

        // Smooth movement
        if (!instant && PositionSmoothTime > 0)
        {
            transform.position = Vector3.SmoothDamp(transform.position, finalPosition, ref _smoothVelocity, PositionSmoothTime);
        }
        else
        {
            transform.position = finalPosition;
        }

        // Rotate to face targetRotation
        if (RotationSmoothTime > 0)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * RotationSmoothTime);
        }
        else
        {
            transform.rotation = targetRotation;
        }
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    public void SetCameraHeight(float height)
    {
        CameraHeight = Mathf.Clamp(height, MinCameraHeight, MaxCameraHeight);
    }

    public void AddCameraHeight(float amount)
    {
        SetCameraHeight(CameraHeight + amount);
    }

    /// <summary>
    /// Returns the camera's forward direction projected on the horizontal plane (for movement).
    /// </summary>
    public Vector3 GetFlatForward()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0;
        return fwd.normalized;
    }

    /// <summary>
    /// Returns the camera's right direction projected on the horizontal plane.
    /// </summary>
    public Vector3 GetFlatRight()
    {
        Vector3 right = transform.right;
        right.y = 0;
        return right.normalized;
    }
}
