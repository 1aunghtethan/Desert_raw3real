using UnityEngine;

/// <summary>
/// Keeps plants grounded to the terrain surface at all times.
/// Trees stay kinematic (pinned) and only fall if sand is heavily eroded beneath them.
/// </summary>
public class PlantPhysics : MonoBehaviour
{
    public bool DisableFall = false;
    public bool UseCustomGroundingOffset = false;
    public float CustomGroundingOffset = 0.1f;
    private const string GroundTouchPointName = "groundtouchpoint";
    private Rigidbody _rb;
    private TerrainManager _tm;
    private Transform _groundTouchPoint;
    private bool _stabilityLost = false;
    private float _initialGroundHeight;
    private float _groundingOffset;

    /// <summary>
    /// How far the terrain must drop beneath the tree before it topples (meters).
    /// </summary>
    private const float EROSION_THRESHOLD = 2.0f;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _tm = TerrainManager.Instance;
        _groundTouchPoint = FindChildIgnoreCase(transform, GroundTouchPointName);
        
        if (_rb != null)
        {
            // Keep kinematic — trees should NOT have gravity pulling them down
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Store ground height at spawn and the grounding offset from config
        if (_tm != null)
        {
            _initialGroundHeight = SampleGroundHeightAtAnchor();
            
            // Determine grounding offset based on what this is
            if (UseCustomGroundingOffset)
                _groundingOffset = CustomGroundingOffset;
            else if (gameObject.name.Contains("TreeZoneTree"))
                _groundingOffset = _tm.Config.TreeZoneTreeGroundingOffset;
            else if (gameObject.name.Contains("Joshua") || gameObject.name.Contains("Tree"))
                _groundingOffset = _tm.Config.JoshuaTreeGroundingOffset;
            else if (_groundTouchPoint != null && gameObject.name.Contains("Grass"))
                _groundingOffset = 0f;
            else if (gameObject.name.Contains("TerrainGrass"))
                _groundingOffset = _tm.Config.TerrainGrassGroundingOffset;
            else if (gameObject.name.Contains("Grass"))
                _groundingOffset = 0.02f;
            else
                _groundingOffset = _tm.Config.PlantGroundingOffset;
        }
    }

    void LateUpdate()
    {
        if (_tm == null || _stabilityLost) return;

        // Continuously re-ground the tree to the terrain surface so it
        // follows any gentle terrain changes and never floats or sinks.
        float currentGroundHeight = SampleGroundHeightAtAnchor();
        if (float.IsNaN(currentGroundHeight) || float.IsInfinity(currentGroundHeight))
            return;

        // Check for major erosion — sand dropped far below the original spawn height
        if (!DisableFall && currentGroundHeight < _initialGroundHeight - EROSION_THRESHOLD)
        {
            LoseStability();
            return;
        }

        // Pin the tree to the current terrain surface
        AlignToGroundHeight(currentGroundHeight, _groundingOffset);
    }

    public static bool AlignGroundTouchPointToTerrain(GameObject obj, TerrainManager terrainManager, float groundingOffset)
    {
        if (obj == null || terrainManager == null)
            return false;

        Transform groundTouchPoint = FindChildIgnoreCase(obj.transform, GroundTouchPointName);
        if (groundTouchPoint == null)
            return false;

        float groundHeight = terrainManager.SampleHeight(groundTouchPoint.position);
        if (float.IsNaN(groundHeight) || float.IsInfinity(groundHeight))
            return false;

        float targetY = groundHeight - groundingOffset;
        float deltaY = targetY - groundTouchPoint.position.y;
        obj.transform.position += Vector3.up * deltaY;
        return true;
    }

    private float SampleGroundHeightAtAnchor()
    {
        Vector3 samplePosition = _groundTouchPoint != null ? _groundTouchPoint.position : transform.position;
        return _tm.SampleHeight(samplePosition);
    }

    private void AlignToGroundHeight(float groundHeight, float groundingOffset)
    {
        if (_groundTouchPoint != null)
        {
            float targetY = groundHeight - groundingOffset;
            float deltaY = targetY - _groundTouchPoint.position.y;
            transform.position += Vector3.up * deltaY;
            return;
        }

        Vector3 pos = transform.position;
        pos.y = groundHeight - groundingOffset;
        transform.position = pos;
    }

    private static Transform FindChildIgnoreCase(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, childName, System.StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindChildIgnoreCase(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    public void LoseStability()
    {
        _stabilityLost = true;
        
        if (_rb != null)
        {
            // Switch to dynamic physics so the tree can topple
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.None;

            // Small random torque for a natural fall direction
            Vector3 randomTorque = new Vector3(
                Random.Range(-1f, 1f),
                0,
                Random.Range(-1f, 1f)
            ).normalized * 5f;
            
            _rb.AddTorque(randomTorque, ForceMode.Impulse);
        }
        
        Debug.Log($"[PlantPhysics] {gameObject.name} lost stability and is falling!");
    }
}
