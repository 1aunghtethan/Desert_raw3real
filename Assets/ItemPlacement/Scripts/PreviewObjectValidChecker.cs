using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PreviewObjectValidChecker : MonoBehaviour
{
    [SerializeField] private LayerMask invalidLayers;
    public bool IsValid { get; private set; } = true;
    private HashSet<Collider> _collidingObjects = new HashSet<Collider>();

    public void Configure(LayerMask layers)
    {
        invalidLayers = layers;
        _collidingObjects.Clear();
        IsValid = true;
    }

    private void LateUpdate()
    {
        // Prune destroyed or disabled colliders
        _collidingObjects.RemoveWhere(c => c == null || !c.enabled);
        IsValid = _collidingObjects.Count == 0;
    }

    private bool IsGroundCollider(Collider col)
    {
        if (col == null) return false;
        if (col is TerrainCollider) return true;
        if (col.GetComponentInParent<Terrain>() != null) return true;
        if (col.GetComponentInParent<SandChunk>() != null) return true;
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        // Never count ground/terrain as an invalid obstacle
        if (IsGroundCollider(other)) return;

        if (((1 << other.gameObject.layer) & invalidLayers) != 0)
        {
            _collidingObjects.Add(other);
            IsValid = false;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        _collidingObjects.Remove(other);
        IsValid = _collidingObjects.Count == 0;
    }
}
