using UnityEngine;

/// <summary>
/// Simple camera mode switcher — press V to toggle between First Person and Third Person.
/// Just sets one camera active and the other inactive.
/// Attach to the Player GameObject.
/// </summary>
public class CameraModeSwitcher : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode ToggleKey = KeyCode.V;

    [Header("References")]
    public PlayerController Player;
    public Camera FPCamera;
    public Camera TPCamera;
    public ThirdPersonCamera TPCameraController;

    [Header("Starting Mode")]
    public PlayerController.CameraMode StartMode = PlayerController.CameraMode.ThirdPerson;

    void Start()
    {
        // Auto-find references
        if (Player == null) Player = GetComponent<PlayerController>();
        if (Player == null) Player = GetComponentInParent<PlayerController>();
        if (Player == null) Player = FindFirstObjectByType<PlayerController>();

        if (Player == null)
        {
            Debug.LogError("[CameraModeSwitcher] No PlayerController found! Assign it in the Inspector.");
            enabled = false;
            return;
        }

        if (FPCamera == null) FPCamera = Player.FPCamera;
        if (TPCamera == null) TPCamera = Player.TPCamera;
        if (TPCameraController == null) TPCameraController = Player.TPCameraController;

        // Apply starting mode
        ApplyMode(StartMode);
    }

    void Update()
    {
        if (Player == null) return;

        if (Input.GetKeyDown(ToggleKey))
        {
            var newMode = (Player.CurrentMode == PlayerController.CameraMode.FirstPerson)
                ? PlayerController.CameraMode.ThirdPerson
                : PlayerController.CameraMode.FirstPerson;

            ApplyMode(newMode);
        }
    }

    void ApplyMode(PlayerController.CameraMode mode)
    {
        bool isFP = (mode == PlayerController.CameraMode.FirstPerson);

        // ── Sync rotation for smooth transition ──
        if (isFP && TPCamera != null && TPCameraController != null)
        {
            // Switching TO FP: use TP camera's current view angle
            // PlayerController.SetMode will handle internal sync
        }
        else if (!isFP && FPCamera != null && TPCameraController != null)
        {
            // Switching TO TP: seed orbit yaw/pitch from current player rotation
            Vector3 euler = FPCamera.transform.eulerAngles;
            float yaw = euler.y;
            float pitch = euler.x;
            if (pitch > 180f) pitch -= 360f;
            pitch = Mathf.Clamp(pitch, TPCameraController.MinVerticalAngle, TPCameraController.MaxVerticalAngle);

            TPCameraController.Yaw = yaw;
            TPCameraController.Pitch = pitch;
        }

        // ── Toggle cameras ──
        if (FPCamera != null) FPCamera.gameObject.SetActive(isFP);
        if (TPCamera != null) TPCamera.gameObject.SetActive(!isFP);

        // ── Tell PlayerController ──
        if (Player != null) Player.SetMode(mode);

        // ── Cursor ──
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

}
