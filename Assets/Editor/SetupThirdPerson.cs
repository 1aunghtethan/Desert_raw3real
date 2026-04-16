using UnityEngine;
using UnityEditor;

public static class SetupThirdPerson
{
    [MenuItem("Sand/Setup Third Person Camera")]
    public static void Setup()
    {
        // Find the Player
        GameObject player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("Player not found!"); return; }

        // Find camera (child of Player)
        Transform camTrans = player.transform.Find("First Person Camera");
        if (camTrans == null)
        {
            // Maybe already renamed
            camTrans = player.transform.Find("Third Person Camera");
        }
        if (camTrans == null)
        {
            // Search all cameras
            Camera cam = Camera.main;
            if (cam != null) camTrans = cam.transform;
        }
        if (camTrans == null) { Debug.LogError("Camera not found!"); return; }

        // 1. DISABLE First Person Components
        var fpm = player.GetComponent<FirstPersonMovement>();
        if (fpm) fpm.enabled = false;

        var crouch = player.GetComponent<Crouch>();
        if (crouch) crouch.enabled = false;

        var fpl = camTrans.GetComponent<FirstPersonLook>();
        if (fpl) fpl.enabled = false;

        var zoom = camTrans.GetComponent<Zoom>();
        if (zoom) zoom.enabled = false;

        Debug.Log("✓ First person components disabled");

        // 2. UNPARENT the camera to scene root
        camTrans.SetParent(null);
        camTrans.gameObject.name = "Third Person Camera";

        // Position behind and above player
        Vector3 playerPos = player.transform.position;
        camTrans.position = playerPos + new Vector3(0, 3f, -5f);
        camTrans.rotation = Quaternion.Euler(15f, 0f, 0f);

        Debug.Log("✓ Camera unparented and positioned");

        // 3. ADD ThirdPersonCamera to the camera
        var tpc = camTrans.GetComponent<ThirdPersonCamera>();
        if (tpc == null) tpc = camTrans.gameObject.AddComponent<ThirdPersonCamera>();
        tpc.Target = player.transform;
        tpc.Distance = 5f;
        tpc.MinDistance = 2f;
        tpc.MaxDistance = 15f;
        tpc.TargetOffset = new Vector3(0, 1.5f, 0);
        tpc.MouseSensitivity = 3f;
        tpc.MinVerticalAngle = -30f;
        tpc.MaxVerticalAngle = 70f;
        tpc.enabled = true;

        Debug.Log("✓ ThirdPersonCamera added and configured");

        // 4. ADD ThirdPersonMovement to the player
        var tpm = player.GetComponent<ThirdPersonMovement>();
        if (tpm == null) tpm = player.AddComponent<ThirdPersonMovement>();
        tpm.CameraController = tpc;
        tpm.WalkSpeed = 5f;
        tpm.RunSpeed = 9f;
        tpm.enabled = true;

        Debug.Log("✓ ThirdPersonMovement added and configured");

        // 5. Make sure the capsule mesh is visible (for third person)
        Transform capsuleMesh = player.transform.Find("Capsule Mesh");
        if (capsuleMesh != null)
        {
            var mr = capsuleMesh.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = true;
        }

        // 6. Ensure Rigidbody settings are correct
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // Mark scene dirty
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene()
        );

        Debug.Log("✓ Third person setup complete! Press Play to test.");
    }
}
