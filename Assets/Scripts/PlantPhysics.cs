using UnityEngine;

/// <summary>
/// Keeps plants grounded to the terrain surface at all times.
/// Trees stay kinematic (pinned) and only fall if sand is heavily eroded beneath them.
/// </summary>
public class PlantPhysics : MonoBehaviour
{
    public bool DisableFall = false;
    private Rigidbody _rb;
    private TerrainManager _tm;
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
            _initialGroundHeight = _tm.SampleHeight(transform.position);
            
            // Determine grounding offset based on what this is
            if (gameObject.name.Contains("Joshua") || gameObject.name.Contains("Tree"))
                _groundingOffset = _tm.Config.JoshuaTreeGroundingOffset;
            else if (gameObject.name.Contains("Grass"))
                _groundingOffset = 0.02f;
            else
                _groundingOffset = _tm.Config.PlantGroundingOffset;
        }
    }

    void Update()
    {
        if (_tm == null || _rb == null || _stabilityLost) return;

        // Continuously re-ground the tree to the terrain surface so it
        // follows any gentle terrain changes and never floats or sinks.
        float currentGroundHeight = _tm.SampleHeight(transform.position);

        // Check for major erosion — sand dropped far below the original spawn height
        if (!DisableFall && currentGroundHeight < _initialGroundHeight - EROSION_THRESHOLD)
        {
            LoseStability();
            return;
        }

        // Pin the tree to the current terrain surface
        Vector3 pos = transform.position;
        pos.y = currentGroundHeight - _groundingOffset;
        transform.position = pos;
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
