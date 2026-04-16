using UnityEditor;
using UnityEngine;
using System.IO;

public class ShaderFixer : MonoBehaviour
{
    [MenuItem("Tools/Upgrade Joshua Tree Materials to URP")]
    public static void UpgradeMaterials()
    {
        string materialFolder = "Assets/Models/JoshuaTrees/LODs/Materials";
        if (!Directory.Exists(materialFolder))
        {
            Debug.LogError($"Material folder not found: {materialFolder}");
            return;
        }

        string[] matFiles = Directory.GetFiles(materialFolder, "*.mat");
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        
        if (urpLit == null)
        {
            // Fallback to Standard or just report
            Debug.LogWarning("URP Lit shader not found. Suggest checking project pipeline.");
            urpLit = Shader.Find("Standard");
        }

        foreach (string matPath in matFiles)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            Texture mainTex = mat.GetTexture("_MainTex");
            Color col = mat.color;

            mat.shader = urpLit;

            // Re-assign textures for URP naming conventions if needed
            if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
            mat.SetColor("_BaseColor", col);

            EditorUtility.SetDirty(mat);
            Debug.Log($"Upgraded material: {matPath} to {urpLit.name}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
