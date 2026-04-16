using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class MaterialFixer : MonoBehaviour
{
    [MenuItem("Tools/Extract and Fix Joshua Tree Materials")]
    public static void FixMaterials()
    {
        string[] modelPaths = {
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD0.fbx",
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD1.fbx",
            "Assets/Models/JoshuaTrees/LODs/joshuatree_LOD2.fbx"
        };

        string materialFolder = "Assets/Models/JoshuaTrees/Materials";
        if (!Directory.Exists(materialFolder)) Directory.CreateDirectory(materialFolder);

        foreach (string path in modelPaths)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            // Set Material location to External
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
            
            Debug.Log($"Extracted materials for: {path}");
        }

        AssetDatabase.Refresh();
        Debug.Log("Material extraction complete. Please check the JoshuaTree_LOD prefab.");
    }
}
