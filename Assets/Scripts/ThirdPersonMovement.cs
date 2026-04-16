using UnityEngine;

/// <summary>
/// Third-person player movement that moves relative to the camera direction.
/// Replaces FirstPersonMovement when using ThirdPersonCamera.
/// </summary>
public class ThirdPersonMovement : MonoBehaviour
{
    [Header("Movement")]
    public float WalkSpeed = 5f;
    public float RunSpeed = 9f;
    public KeyCode RunKey = KeyCode.LeftShift;

    [Header("Rotation")]
    public float RotationSpeed = 10f; // How fast the player model rotates to face movement direction

    [Header("References")]
    public ThirdPersonCamera CameraController; // Assign the camera with ThirdPersonCamera

    private Rigidbody _rb;
    private bool _isRunning;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        // Auto-find camera if not assigned
        if (CameraController == null)
        {
            CameraController = FindFirstObjectByType<ThirdPersonCamera>();
        }
    }

    void FixedUpdate()
    {
        if (CameraController == null) return;

        _isRunning = Input.GetKey(RunKey);
        float currentSpeed = _isRunning ? RunSpeed : WalkSpeed;

        // Get input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // Calculate movement direction relative to camera
        Vector3 camForward = CameraController.GetFlatForward();
        Vector3 camRight = CameraController.GetFlatRight();
        Vector3 moveDirection = (camForward * vertical + camRight * horizontal);

        if (moveDirection.sqrMagnitude > 1f)
            moveDirection.Normalize();

        // Apply movement (preserve Y velocity for gravity/jump)
        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = _rb.linearVelocity.y;
        _rb.linearVelocity = velocity;

        // Rotate player to face movement direction
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * RotationSpeed);
        }
    }
}
