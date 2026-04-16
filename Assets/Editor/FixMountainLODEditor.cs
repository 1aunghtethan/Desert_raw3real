using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class FixMountainLODEditor : EditorWindow
{
    [MenuItem("Tools/Fix Mountain LODs")]
    public static void FixLODs()
    {
        string prefabPath = "Assets/Prefabs/mountain/moutain1.prefab";
        GameObject prefabText = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (prefabText == null)
        {
            Debug.LogError("Could not find " + prefabPath);
            return;
        }

        // Open prefab for editing
        string path = AssetDatabase.GetAssetPath(prefabText);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);

        try {
            LODGroup lodGroup = contents.GetComponent<LODGroup>();
            if (lodGroup == null) lodGroup = contents.AddComponent<LODGroup>();

            List<Renderer> lod0 = new List<Renderer>();
            List<Renderer> lod1 = new List<Renderer>();
            List<Renderer> lod2 = new List<Renderer>();
            List<Renderer> lod3 = new List<Renderer>();

            foreach (Transform child in contents.transform)
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer == null) continue;

                string name = child.name.ToLower();
                // Match "lod0" or "l0" or similar
                if (name.Contains("lod0") || (name.Contains("_0") && !name.Contains("lod"))) lod0.Add(renderer);
                else if (name.Contains("lod1") || (name.Contains("_1") && !name.Contains("lod"))) lod1.Add(renderer);
                else if (name.Contains("lod2") || (name.Contains("_2") && !name.Contains("lod"))) lod2.Add(renderer);
                else if (name.Contains("lod3") || (name.Contains("_3") && !name.Contains("lod"))) lod3.Add(renderer);
                // Fallback: If it's a "Rock_Inst" without a specific LOD suffix, add to all? 
                // No, the hierarchy shows suffixes.
            }

            LOD[] lods = new LOD[4];
            lods[0] = new LOD(0.5f, lod0.ToArray());
            lods[1] = new LOD(0.2f, lod1.ToArray());
            lods[2] = new LOD(0.05f, lod2.ToArray());
            lods[3] = new LOD(0.01f, lod3.ToArray());

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log($"Mountain LODs fixed on {prefabPath}! LOD0: {lod0.Count}, LOD1: {lod1.Count}, LOD2: {lod2.Count}, LOD3: {lod3.Count}");
        }
        finally {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
