using UnityEngine;

/// <summary>
/// Attached to dropped log items. Continuously checks and corrects height
/// to ensure the log sits on the terrain surface. Self-destroys after grounding.
/// </summary>
public class LogGrounder : MonoBehaviour
{

    private float m_Timer = 0f;
    private const float DURATION = 1.0f;
    private Vector3 m_StartPos;
    private float m_TargetY;
    private bool m_Initialized = false;
    private float m_PivotToBottom = 0.15f; // Default fallback

    void Update()
    {
        if (!m_Initialized)
        {
            m_StartPos = transform.position;
            m_Initialized = true;
            
            // Calculate actual distance from pivot to bottom of mesh
            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // We want the vertical distance from the center (position) to the bottom of the bounds
                m_PivotToBottom = Mathf.Abs(transform.position.y - rend.bounds.min.y);
            }

            UpdateTargetHeight();
        }

        m_Timer += Time.deltaTime;
        float t = Mathf.Clamp01(m_Timer / DURATION);
        
        // Smoothly move to the ground
        UpdateTargetHeight();
        Vector3 pos = transform.position;
        pos.y = Mathf.Lerp(m_StartPos.y, m_TargetY, t);
        transform.position = pos;

        if (t >= 1.0f)
        {
            Destroy(this);
        }
    }

    private void UpdateTargetHeight()
    {
        if (TerrainManager.Instance != null)
        {
            float h = TerrainManager.Instance.SampleHeight(transform.position);
            if (!float.IsNaN(h))
            {
                // Target height is ground level + half-height of the log + small buffer
                m_TargetY = h + m_PivotToBottom + 0.02f; 
            }
            else
            {
                m_TargetY = transform.position.y;
            }
        }
    }
}
