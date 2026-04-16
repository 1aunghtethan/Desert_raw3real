using UnityEngine;

/// <summary>
/// Simple 3D swimming AI for oasis fish.
/// Keeps the fish within the water volume and wanders randomly.
/// </summary>
public class OasisFishAI : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 1.2f;
    public float RotationSpeed = 1.5f;
    public float WanderIntervalMin = 4f;
    public float WanderIntervalMax = 8f;

    [Header("Constraints")]
    public Vector3 OasisCenter;
    public float SwimRadius = 20f;
    public float WaterLevel = 0f;
    public float MaxDepth = 3f;

    [Header("Schooling")]
    public int OasisID; 
    [Range(0, 1)] public float SchoolingChance = 0.75f;
    [Tooltip("Small jitter added to shared targets to keep school naturally spread.")]
    public float SchoolingJitter = 2.0f;

    private Vector3 _targetPoint;
    private float _nextTargetTime;
    private bool _isFleeing = false;
    private float _fleeTimer = 0f;

    // Static registry for shared schooling targets per oasis
    private static System.Collections.Generic.Dictionary<int, Vector3> _schoolTargets = new System.Collections.Generic.Dictionary<int, Vector3>();
    private static System.Collections.Generic.Dictionary<int, float> _schoolTargetSetTimes = new System.Collections.Generic.Dictionary<int, float>();

    void Start()
    {
        // Randomize initial target and position slightly to avoid synchronized swimming
        _nextTargetTime = Time.time + Random.Range(0, WanderIntervalMax);
        PickNewTarget();
        
        // Face target immediately on start
        Vector3 dir = (_targetPoint - transform.position).normalized;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);
    }

    void Update()
    {
        if (_isFleeing)
        {
            _fleeTimer -= Time.deltaTime;
            if (_fleeTimer <= 0) _isFleeing = false;
        }

        if (Time.time > _nextTargetTime || Vector3.Distance(transform.position, _targetPoint) < 0.8f)
        {
            PickNewTarget();
        }

        // Smooth rotation and movement
        Vector3 direction = (_targetPoint - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            // Banking effect: slightly tilt on turns
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
        }

        float speed = _isFleeing ? MoveSpeed * 2.5f : MoveSpeed;
        transform.position += transform.forward * speed * Time.deltaTime;

        // Final boundary safety check (snap back if drift out)
        ClampPosition();
    }

    public void Flee()
    {
        _isFleeing = true;
        _fleeTimer = 5f;
        PickNewTarget(); // Pick a new target immediately to swim away
    }

    private void PickNewTarget()
    {
        bool useSchooling = !_isFleeing && Random.value < SchoolingChance;
        
        if (useSchooling && OasisID != 0)
        {
            // Try to find or set a shared group target
            if (_schoolTargets.TryGetValue(OasisID, out Vector3 sharedTarget) && Time.time < _schoolTargetSetTimes[OasisID] + 2f)
            {
                // Follow the existing group target with a little individual jitter
                _targetPoint = sharedTarget + Random.insideUnitSphere * SchoolingJitter;
                _targetPoint.y = Mathf.Min(_targetPoint.y, WaterLevel - 0.2f); // Surface safety
            }
            else
            {
                // Be the "leader" and set a new group target
                _targetPoint = GetRandomPointInWater();
                _schoolTargets[OasisID] = _targetPoint;
                _schoolTargetSetTimes[OasisID] = Time.time;
            }
        }
        else
        {
            // Solo wandering or fleeing
            _targetPoint = GetRandomPointInWater();
        }
        
        float interval = _isFleeing ? Random.Range(1f, 3f) : Random.Range(WanderIntervalMin, WanderIntervalMax);
        _nextTargetTime = Time.time + interval;
    }

    private Vector3 GetRandomPointInWater()
    {
        Vector2 randomCircle = Random.insideUnitCircle * SwimRadius;
        float randomDepth = Random.Range(0.2f, Mathf.Max(0.5f, MaxDepth)); 
        return new Vector3(OasisCenter.x + randomCircle.x, WaterLevel - randomDepth, OasisCenter.z + randomCircle.y);
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        Vector2 pos2D = new Vector2(pos.x, pos.z);
        Vector2 center2D = new Vector2(OasisCenter.x, OasisCenter.z);

        if (Vector2.Distance(pos2D, center2D) > SwimRadius + 2f)
        {
            // Too far out, turn back
            PickNewTarget();
        }

        if (pos.y > WaterLevel - 0.2f)
        {
            // Keep fish at least 0.2m below surface
            pos.y = WaterLevel - 0.2f;
            transform.position = pos;
            PickNewTarget();
        }
    }
}
