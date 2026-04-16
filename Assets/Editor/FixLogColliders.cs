using UnityEngine;
using UnityEditor;

public static class FixLogColliders
{
    [MenuItem("Tools/Fix Log Colliders")]
    public static void RunFix()
    {
        Debug.Log("[FixLogColliders] Fixing Convex MeshColliders and adding Mass...");

        string[] prefabs = { "Log 1", "Log_2", "Log_Half", "Log_Quarter_1", "Log_Quarter_2" };
        string prefabDir = "Assets/After_cut/for_tree/";

        foreach (string name in prefabs)
        {
            string prefabPath = prefabDir + name + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) continue;

            // 1. Fix the MeshColliders turning them Convex
            MeshCollider[] mcs = prefab.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in mcs)
            {
                mc.convex = true;
            }

            // 2. Make them extraordinarily heavy so the player can't kick them, 
            // but they still fall dynamically from trees!
            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = 5000f;
                // Add lots of drag so they don't roll forever down a hill
                rb.linearDamping = 0.5f;
                rb.angularDamping = 0.5f;
            }

            EditorUtility.SetDirty(prefab);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[FixLogColliders] Complete!");
    }
}
