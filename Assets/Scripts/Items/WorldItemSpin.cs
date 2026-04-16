using UnityEngine;

/// <summary>
/// Makes a dropped world item slowly rotate and bob up/down for visual appeal.
/// Automatically added to items dropped in the world.
/// </summary>
public class WorldItemSpin : MonoBehaviour
{
    public float RotationSpeed = 40f;
    public float BobSpeed = 2f;
    public float BobHeight = 0.15f;

    private Vector3 _startPos;
    private float _timeOffset;
    private Rigidbody _rb;
    private bool _isSettled;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _timeOffset = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        // Don't spin/bob while the item is still moving from a throw
        if (_rb != null && !_rb.IsSleeping() && _rb.linearVelocity.magnitude > 0.1f)
        {
            _isSettled = false;
            return;
        }

        if (!_isSettled)
        {
            _isSettled = true;
            _startPos = transform.position;

            // Make it kinematic so it floats nicely
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
        }

        // Rotate
        transform.Rotate(Vector3.up, RotationSpeed * Time.deltaTime, Space.World);

        // Bob up and down
        Vector3 pos = _startPos;
        pos.y += Mathf.Sin((Time.time + _timeOffset) * BobSpeed) * BobHeight;
        transform.position = pos;
    }
}
