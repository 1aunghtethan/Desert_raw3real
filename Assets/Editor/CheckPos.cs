using UnityEngine;
using UnityEditor;

public class CheckPos {
    [MenuItem("Tools/Check Pos")]
    public static void Do() {
        GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/mountain/moutain1.prefab");
        foreach(Transform t in p.GetComponentsInChildren<Transform>()) {
            Renderer r = t.GetComponent<Renderer>();
            Debug.Log(t.name + " localPos: " + t.localPosition + (r != null ? " bounds center: " + r.bounds.center + " extents " + r.bounds.extents : ""));
        }
    }
}
