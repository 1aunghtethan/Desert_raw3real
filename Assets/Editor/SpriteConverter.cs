using UnityEngine;
using UnityEditor;
using System.IO;

public class SpriteConverter : EditorWindow
{
    [MenuItem("Tools/Convert Sprite Folder")]
    public static void ConvertFolder()
    {
        string folderPath = "Assets/sprite";
        string[] files = Directory.GetFiles(folderPath, "*.png");

        foreach (string file in files)
        {
            TextureImporter importer = AssetImporter.GetAtPath(file) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
                Debug.Log($"Converted {file} to Sprite.");
            }
        }
    }
}
