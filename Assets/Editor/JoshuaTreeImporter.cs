using UnityEditor;
using UnityEngine;

public class JoshuaTreeImporter : MonoBehaviour
{
    [MenuItem("Tools/Force Import Joshua Trees")]
    public static void ForceImport()
    {
        string[] paths = {
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD0.fbx",
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD1.fbx",
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD2.fbx"
        };

        foreach (string path in paths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"Forced import of: {path}");
        }
        
        AssetDatabase.Refresh();
    }
}
