using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 20.0f;
    public float lookSpeed = 2.0f;
    public float fastMultiplier = 2.0f;

    private float rotationX = 0.0f;
    private float rotationY = 0.0f;

    void Start()
    {
        // Initialize rotation to current camera rotation
        Vector3 rot = transform.localRotation.eulerAngles;
        rotationY = rot.y;
        rotationX = rot.x;
    }

    void Update()
    {
        // Movement
        float currentSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= fastMultiplier;
        }

        float moveX = Input.GetAxis("Horizontal") * currentSpeed * Time.deltaTime;
        float moveZ = Input.GetAxis("Vertical") * currentSpeed * Time.deltaTime;
        float moveY = 0;

        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) moveY = currentSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) moveY = -currentSpeed * Time.deltaTime;

        transform.Translate(new Vector3(moveX, moveY, moveZ));

        // Rotation (Right Mouse Button to look roughly standardized for tools)
        if (Input.GetMouseButton(1))
        {
            rotationY += Input.GetAxis("Mouse X") * lookSpeed;
            rotationX -= Input.GetAxis("Mouse Y") * lookSpeed;
            rotationX = Mathf.Clamp(rotationX, -90, 90);

            transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
        }
    }
}
