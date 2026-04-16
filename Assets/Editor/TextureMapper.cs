using UnityEditor;
using UnityEngine;
using System.IO;

public class TextureMapper : MonoBehaviour
{
    [MenuItem("Tools/Re-Map Joshua Tree Textures")]
    public static void MapTextures()
    {
        string materialFolder = "Assets/Models/JoshuaTrees/LODs/Materials";
        string textureFolder = "Assets/Models/JoshuaTrees/Textures";

        if (!Directory.Exists(materialFolder) || !Directory.Exists(textureFolder))
        {
            Debug.LogError("Required folders (Materials or Textures) missing.");
            return;
        }

        // Texture names to look for
        string trunkTexName = "texture_pbr_20250901";
        // string normalTexName = "texture_pbr_20250901_normal";

        Texture2D trunkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(textureFolder, trunkTexName + ".png"));
        Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(textureFolder, trunkTexName + "_normal.png"));

        string[] matFiles = Directory.GetFiles(materialFolder, "*.mat");

        foreach (string matPath in matFiles)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) continue;

            if (mat.name.Contains("texture_pbr"))
            {
                if (trunkTex != null) mat.mainTexture = trunkTex;
                if (normalTex != null) mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            // Add similar logic for Green/Yellow leaves if they are unique
            
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Joshua Tree textures re-mapped successfully.");
    }
}
