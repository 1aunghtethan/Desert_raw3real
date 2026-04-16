using UnityEngine;
using UnityEditor;

public class AddColliders {
    [MenuItem("Tools/Add Colliders")]
    public static void Run() {
        string path = "Assets/Prefabs/mountain/moutain1.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        int added = 0;
        foreach (Transform child in contents.transform) {
            if (child.name.Contains("LOD0") || child.name.EndsWith("_0")) {
                if (child.GetComponent<MeshCollider>() == null) {
                    child.gameObject.AddComponent<MeshCollider>();
                    added++;
                }
            }
        }
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
        Debug.Log("Added Colliders to " + added + " objects");
    }
}
