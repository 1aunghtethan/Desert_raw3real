using UnityEditor;
using UnityEngine;

public class FixRockCollider : EditorWindow
{
    [MenuItem("Tools/Fix Rock Collider")]
    public static void Fix()
    {
        string path = "Assets/Prefabs/rock/rock07_m.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError("Could not find prefab at " + path);
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        Transform child = instance.transform.Find("default");
        if (child != null)
        {
            if (child.GetComponent<MeshCollider>() == null)
            {
                child.gameObject.AddComponent<MeshCollider>();
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log("Successfully added MeshCollider to " + path);
            }
            else
            {
                Debug.Log("MeshCollider already exists on " + path);
            }
        }
        else
        {
            Debug.LogError("Could not find child 'default' on prefab " + path);
        }
        
        DestroyImmediate(instance);
    }
}
