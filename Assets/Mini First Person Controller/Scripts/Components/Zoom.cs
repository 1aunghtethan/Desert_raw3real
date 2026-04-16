using UnityEngine;

[ExecuteInEditMode]
public class Zoom : MonoBehaviour
{
    Camera zoomCamera;
    public float defaultFOV = 60;
    public float maxZoomFOV = 15;
    [Range(0, 1)]
    public float currentZoom;
    public float sensitivity = 1;


    void Awake()
    {
        // Get the camera on this gameObject and the defaultZoom.
        zoomCamera = GetComponentInChildren<Camera>(); // Changed to GetComponentInChildren
        if (zoomCamera)
        {
            defaultFOV = zoomCamera.fieldOfView;
        }
    }

    void Update()
    {
        // Update the currentZoom and the camera's fieldOfView.
        if (zoomCamera != null)
        {
            currentZoom += Input.mouseScrollDelta.y * sensitivity * .05f;
            currentZoom = Mathf.Clamp01(currentZoom);
            zoomCamera.fieldOfView = Mathf.Lerp(defaultFOV, maxZoomFOV, currentZoom);
        }
    }
}
