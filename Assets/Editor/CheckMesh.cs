using UnityEngine;
using UnityEditor;

public class CheckMesh {
    [MenuItem("Tools/Check Mesh")]
    public static void Run() {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/mountain/moutain1.prefab");
        foreach(var mf in p.GetComponentsInChildren<MeshFilter>(true)) {
            Debug.Log(mf.name + " mesh: " + (mf.sharedMesh ? mf.sharedMesh.name : "NULL"));
        }
    }
}
