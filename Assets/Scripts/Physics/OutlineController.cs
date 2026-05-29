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
    private Material m_RuntimeOutlineMaterial;
    private readonly Dictionary<Renderer, Material[]> m_OriginalMaterials = new Dictionary<Renderer, Material[]>();
    private static Material s_OutlineMatTemplate;
    private static bool s_OutlineWarningLogged;

    private void Awake()
    {
        m_Renderers = GetComponentsInChildren<Renderer>(true);
        CreateRuntimeOutlineMaterial();
    }

    private void OnDestroy()
    {
        if (m_IsShowing)
            ShowOutline(false);

        if (m_RuntimeOutlineMaterial != null)
        {
            Destroy(m_RuntimeOutlineMaterial);
            m_RuntimeOutlineMaterial = null;
        }
    }

    public void ShowOutline(bool show)
    {
        if (m_IsShowing == show) return;
        if (m_RuntimeOutlineMaterial == null && !CreateRuntimeOutlineMaterial()) return;

        m_IsShowing = show;

        foreach (var rend in m_Renderers)
        {
            if (rend == null) continue;

            if (show)
            {
                if (!m_OriginalMaterials.ContainsKey(rend))
                    m_OriginalMaterials[rend] = rend.sharedMaterials;

                List<Material> mats = new List<Material>(m_OriginalMaterials[rend]);
                if (!mats.Contains(m_RuntimeOutlineMaterial))
                {
                    mats.Add(m_RuntimeOutlineMaterial);
                }

                rend.sharedMaterials = mats.ToArray();
            }
            else
            {
                if (m_OriginalMaterials.TryGetValue(rend, out Material[] originalMaterials))
                    rend.sharedMaterials = originalMaterials;
            }
        }

        if (!show)
            m_OriginalMaterials.Clear();
    }

    private bool CreateRuntimeOutlineMaterial()
    {
        Material sourceMaterial = OutlineMaterial;
        if (sourceMaterial == null)
        {
            Shader shader = Shader.Find("Custom/Outline");
            if (!IsUsableOutlineShader(shader))
            {
                LogOutlineUnavailable();
                return false;
            }

            if (s_OutlineMatTemplate == null || s_OutlineMatTemplate.shader != shader)
                s_OutlineMatTemplate = new Material(shader) { name = "Runtime Outline Template" };

            sourceMaterial = s_OutlineMatTemplate;
        }

        if (!IsUsableOutlineShader(sourceMaterial.shader))
        {
            LogOutlineUnavailable();
            return false;
        }

        if (m_RuntimeOutlineMaterial != null)
            Destroy(m_RuntimeOutlineMaterial);

        m_RuntimeOutlineMaterial = new Material(sourceMaterial) { name = $"{name}_OutlineRuntime" };
        if (m_RuntimeOutlineMaterial.HasProperty("_OutlineColor"))
            m_RuntimeOutlineMaterial.SetColor("_OutlineColor", OutlineColor);
        if (m_RuntimeOutlineMaterial.HasProperty("_OutlineWidth"))
            m_RuntimeOutlineMaterial.SetFloat("_OutlineWidth", OutlineWidth);

        return true;
    }

    private static bool IsUsableOutlineShader(Shader shader)
    {
        return shader != null && shader.isSupported && shader.name == "Custom/Outline";
    }

    private static void LogOutlineUnavailable()
    {
        if (s_OutlineWarningLogged) return;

        Debug.LogWarning("[OutlineController] Custom/Outline shader is missing or unsupported; pickup outline skipped to preserve item materials.");
        s_OutlineWarningLogged = true;
    }
}
