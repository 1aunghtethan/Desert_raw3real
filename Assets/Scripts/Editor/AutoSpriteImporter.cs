using UnityEngine;
using UnityEditor;
using System.IO;

public class AutoSpriteImporter
{
    [MenuItem("Tools/Force Convert To Sprites")]
    public static void ConvertAllInSpriteFolder()
    {
        string folderPath = "Assets/sprite";
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });

        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                count++;
            }
        }
        
        Debug.Log($"Successfully converted {count} images to Sprites in {folderPath}!");
    }
}
