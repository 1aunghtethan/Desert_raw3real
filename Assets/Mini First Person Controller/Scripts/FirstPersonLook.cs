using UnityEngine;

public class FirstPersonLook : MonoBehaviour
{
    [Tooltip("The player/character transform to rotate horizontally. Auto-found if empty.")]
    public Transform character;
    public float sensitivity = 2;
    public float smoothing = 1.5f;

    Vector2 velocity;
    Vector2 frameVelocity;

    // Diagnostic: log once per enable to help debug
    private bool _loggedThisEnable;

    void Reset()
    {
        character = GetComponentInParent<FirstPersonMovement>()?.transform;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        FindCharacterIfNeeded();
    }

    void OnEnable()
    {
        _loggedThisEnable = false;
        FindCharacterIfNeeded();
    }

    void FindCharacterIfNeeded()
    {
        if (character != null) return;

        // Try parent hierarchy
        var fpm = GetComponentInParent<FirstPersonMovement>();
        if (fpm != null) { character = fpm.transform; return; }

        // Try scene-wide
        fpm = FindFirstObjectByType<FirstPersonMovement>();
        if (fpm != null) { character = fpm.transform; return; }
    }

    /// <summary>
    /// Returns the transform to use for yaw rotation.
    /// Falls back to transform.parent if character is not assigned.
    /// </summary>
    Transform GetYawTarget()
    {
        if (character != null) return character;
        if (transform.parent != null) return transform.parent;
        return null;
    }

    void Update()
    {
        Transform yawTarget = GetYawTarget();

        // Diagnostic log (once per enable)
        if (!_loggedThisEnable)
        {
            _loggedThisEnable = true;
            Debug.Log($"[FirstPersonLook] Update running. " +
                      $"character={(character != null ? character.name : "NULL")}, " +
                      $"parent={(transform.parent != null ? transform.parent.name : "NULL")}, " +
                      $"yawTarget={(yawTarget != null ? yawTarget.name : "NULL")}, " +
                      $"sensitivity={sensitivity}, smoothing={smoothing}, " +
                      $"cursorLock={Cursor.lockState}");
        }

        if (yawTarget == null)
        {
            Debug.LogWarning("[FirstPersonLook] No yaw target! Cannot rotate.");
            return;
        }

        // Get smooth velocity.
        Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        Vector2 rawFrameVelocity = Vector2.Scale(mouseDelta, Vector2.one * sensitivity);
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1 / smoothing);
        velocity += frameVelocity;
        velocity.y = Mathf.Clamp(velocity.y, -90, 90);

        // Rotate camera up-down and player left-right from velocity.
        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);
        yawTarget.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }
}
