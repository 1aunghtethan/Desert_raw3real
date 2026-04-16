using UnityEngine;
using ithappy.Animals_FREE;

/// <summary>
/// Simple wandering AI for animals.
/// </summary>
[RequireComponent(typeof(CreatureMover))]
public class AnimalAI : MonoBehaviour
{
    public AnimalData Data;
    private CreatureMover m_Mover;
    private Vector3 m_WanderTarget;
    private float m_NextWanderTime;

    private void Awake()
    {
        m_Mover = GetComponent<CreatureMover>();
        PickRandomTarget();
    }

    private void Update()
    {
        if (Time.time >= m_NextWanderTime)
        {
            PickRandomTarget();
        }

        // Drive the mover
        Vector3 direction = (m_WanderTarget - transform.position).normalized;
        Vector2 axis = new Vector2(direction.x, direction.z);
        
        // If we are close to the target, stop or slow down
        if (Vector3.Distance(transform.position, m_WanderTarget) < 0.5f)
        {
            axis = Vector2.zero;
        }

        m_Mover.SetInput(axis, m_WanderTarget, false, false);
    }

    private void PickRandomTarget()
    {
        float radius = Data != null ? Data.WanderRadius : 10f;
        Vector2 randomCircle = Random.insideUnitCircle * radius;
        m_WanderTarget = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
        
        // Snap to terrain height (skip if swimming via OasisFishAI)
        if (TerrainManager.Instance != null && GetComponent<OasisFishAI>() == null)
        {
            m_WanderTarget.y = TerrainManager.Instance.SampleHeight(new Vector3(m_WanderTarget.x, 0, m_WanderTarget.z));
        }

        float minInt = Data != null ? Data.WanderIntervalMin : 2f;
        float maxInt = Data != null ? Data.WanderIntervalMax : 5f;
        m_NextWanderTime = Time.time + Random.Range(minInt, maxInt);
    }
}
