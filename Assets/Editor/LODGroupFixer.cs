using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class LODGroupFixer : MonoBehaviour
{
    [MenuItem("Tools/Configure Joshua Tree LODGroup")]
    public static void ConfigureLOD()
    {
        GameObject root = GameObject.Find("JoshuaTree_LOD_Root");
        if (root == null)
        {
            Debug.LogError("JoshuaTree_LOD_Root not found in scene.");
            return;
        }

        LODGroup lodGroup = root.GetComponent<LODGroup>();
        if (lodGroup == null) lodGroup = root.AddComponent<LODGroup>();

        LOD[] lods = new LOD[3];
        
        // LOD 0
        Transform t0 = root.transform.Find("joshuatree_LOD0_Inst");
        if (t0 != null) {
            Renderer[] renderers = t0.GetComponentsInChildren<Renderer>();
            lods[0] = new LOD(0.6f, renderers);
        }

        // LOD 1
        Transform t1 = root.transform.Find("joshuatree_LOD1_Inst");
        if (t1 != null) {
            Renderer[] renderers = t1.GetComponentsInChildren<Renderer>();
            lods[1] = new LOD(0.3f, renderers);
        }

        // LOD 2
        Transform t2 = root.transform.Find("joshuatree_LOD2_Inst");
        if (t2 != null) {
            Renderer[] renderers = t2.GetComponentsInChildren<Renderer>();
            lods[2] = new LOD(0.1f, renderers);
        }

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();
        
        Debug.Log("Joshua Tree LODGroup configured successfully.");
    }

    [MenuItem("Tools/Save Joshua Tree Prefab")]
    public static void SavePrefab()
    {
        GameObject root = GameObject.Find("JoshuaTree_LOD_Root");
        if (root == null) return;

        string path = "Assets/prefeb/tree/JoshuaTree_LOD.prefab";
        
        // Ensure directory exists
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.AutomatedAction);
        Debug.Log($"Prefab saved to: {path}");
    }
}
