using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages a white border (outline) effect on a GameObject by adding/removing an outline material.
/// </summary>
public class OutlineController : MonoBehaviour
{
    [Header("Settings")]
    public Material OutlineMaterial;
    public float OutlineWidth = 0.02f;
    public Color OutlineColor = Color.white;

    private Renderer[] m_Renderers;
    private bool m_IsShowing = false;
    private static Material s_OutlineMatTemplate;

    private void Awake()
    {
        m_Renderers = GetComponentsInChildren<Renderer>();
        
        // Ensure we have an outline material
        if (OutlineMaterial == null)
        {
            if (s_OutlineMatTemplate == null)
            {
                Shader shader = Shader.Find("Custom/Outline");
                if (shader == null) shader = Shader.Find("Standard"); // Fallback
                s_OutlineMatTemplate = new Material(shader);
                s_OutlineMatTemplate.SetColor("_OutlineColor", OutlineColor);
                s_OutlineMatTemplate.SetFloat("_OutlineWidth", OutlineWidth);
            }
            OutlineMaterial = new Material(s_OutlineMatTemplate);
        }
    }

    public void ShowOutline(bool show)
    {
        if (m_IsShowing == show) return;
        m_IsShowing = show;

        foreach (var rend in m_Renderers)
        {
            if (rend == null) continue;

            List<Material> mats = new List<Material>(rend.sharedMaterials);
            
            if (show)
            {
                if (!mats.Contains(OutlineMaterial))
                {
                    mats.Add(OutlineMaterial);
                }
            }
            else
            {
                mats.Remove(OutlineMaterial);
            }

            rend.materials = mats.ToArray();
        }
    }
}
