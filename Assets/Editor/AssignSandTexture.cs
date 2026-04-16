using UnityEngine;
using UnityEditor;

public static class AssignSandTexture
{
    [MenuItem("Sand/Assign Sand Texture")]
    public static void Assign()
    {
        // Load the material
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SandMat.mat");
        if (mat == null)
        {
            Debug.LogError("SandMat.mat not found at Assets/Materials/SandMat.mat");
            return;
        }

        // Set the normal map import type FIRST
        TextureImporter normalImporter = AssetImporter.GetAtPath("Assets/Textures/SandNormal_Photo.png") as TextureImporter;
        if (normalImporter != null)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }

        // Load textures
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/SandTexture_Photo.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/SandNormal_Photo.png");

        if (albedo != null)
        {
            // URP uses _BaseMap, Standard uses _MainTex
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", albedo);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", albedo);
            
            // Set tiling for sand detail
            if (mat.HasProperty("_BaseMap"))
                mat.SetTextureScale("_BaseMap", new Vector2(8, 8));
            if (mat.HasProperty("_MainTex"))
                mat.SetTextureScale("_MainTex", new Vector2(8, 8));
                
            Debug.Log("✓ Sand albedo texture assigned to SandMat!");
        }
        else
        {
            Debug.LogError("SandTexture_Photo.png not found!");
        }

        if (normal != null)
        {
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 0.5f);
                mat.EnableKeyword("_NORMALMAP");
                
                if (mat.HasProperty("_BumpMap"))
                    mat.SetTextureScale("_BumpMap", new Vector2(8, 8));
            }
            Debug.Log("✓ Sand normal map assigned to SandMat!");
        }
        else
        {
            Debug.LogWarning("SandNormal_Photo.png not found, skipping normal map.");
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("✓ SandMat updated and saved!");
    }
}