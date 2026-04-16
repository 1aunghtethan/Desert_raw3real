using UnityEditor;
using UnityEngine;

public class AssignTextureScript
{
    [MenuItem("Tools/Assign JoshuaTexture")]
    public static void Execute()
    {
        string texPath = "Assets/prefeb/tree/joshuatree1_bark.jpg";
        Texture2D newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (newTex == null)
        {
            Debug.LogError("Texture not found: " + texPath);
            return;
        }

        string matPath = "Assets/prefeb/tree/joshuatree1_mat.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            // Unity 6+ Universal Render Pipeline default material shader is "Universal Render Pipeline/Lit"
            // If the project is Standard pipeline, it's "Standard"
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        // Set texture (URP uses _BaseMap, Standard uses _MainTex)
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", newTex);
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", newTex);

        // Optional: reduce smoothness to make it look like bark
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.05f);

        EditorUtility.SetDirty(mat);

        // Assign to the prefab
        string prefabPath = "Assets/prefeb/tree/joshuatree1.prefab";
        using (var editingScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            var prefabRoot = editingScope.prefabContentsRoot;
            MeshRenderer[] renderers = prefabRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                r.sharedMaterial = mat;
            }
            Debug.Log("Assigned " + mat.name + " to " + renderers.Length + " renderers in prefab.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Texture application complete.");
    }
}
