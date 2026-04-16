using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Editor script to permanently apply a vibrant red color to the Meat prefab.
/// This ensures the user sees the change in the Project window and Scene.
/// </summary>
public class MeatPrefabProcessor
{
    private const string PrefabPath = "Assets/Prefabs/Meat/Meat.prefab";
    private const string MaterialPath = "Assets/Materials/Meat_Vibrant.mat";

    [MenuItem("Sand/Process Meat Prefab")]
    public static void ProcessMeatPrefab()
    {
        Debug.Log("[MeatProcessor] Checking Meat prefab for color update...");

        // 1. Ensure Materials folder exists
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        // 2. Create/Update Material
        Material meatMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (meatMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            
            meatMat = new Material(shader);
            AssetDatabase.CreateAsset(meatMat, MaterialPath);
        }

        // Set vibrant red color
        Color vibrantRed = new Color(1.0f, 0.05f, 0.05f);
        if (meatMat.HasProperty("_BaseColor")) meatMat.SetColor("_BaseColor", vibrantRed);
        else if (meatMat.HasProperty("_Color")) meatMat.SetColor("_Color", vibrantRed);
        
        if (meatMat.HasProperty("_Smoothness")) meatMat.SetFloat("_Smoothness", 0.6f);
        if (meatMat.HasProperty("_Metallic")) meatMat.SetFloat("_Metallic", 0.1f);
        
        EditorUtility.SetDirty(meatMat);
        AssetDatabase.SaveAssets();

        // 3. Update Prefab
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[MeatProcessor] Could not find prefab at {PrefabPath}");
            return;
        }

        bool modified = false;
        Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            if (rend.sharedMaterial != meatMat)
            {
                rend.sharedMaterial = meatMat;
                modified = true;
            }
        }

        // 4. Ensure Components are present
        LootItem loot = prefabRoot.GetComponent<LootItem>();
        if (loot == null)
        {
            loot = prefabRoot.AddComponent<LootItem>();
            loot.PickupRadius = 4.0f;
            modified = true;
        }

        if (prefabRoot.GetComponent<OutlineController>() == null)
        {
            prefabRoot.AddComponent<OutlineController>();
            modified = true;
        }

        // 5. Ensure Rigidbody and Collider are set for physics
        Rigidbody rb = prefabRoot.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            modified = true;
        }

        if (modified)
        {
            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SavePrefabAsset(prefabRoot);
            Debug.Log($"[MeatProcessor] Prefab at {PrefabPath} updated with material and components.");
        }
        else
        {
            Debug.Log("[MeatProcessor] Prefab is already fully configured.");
        }
    }
}
