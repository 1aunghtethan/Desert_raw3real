using UnityEngine;
using UnityEditor;
using System.IO;

public class BakePrefabIcons : EditorWindow
{
    [MenuItem("Sand/Items/Bake Prefab Icons to Sprites")]
    public static void BakeIcons()
    {
        // 1. Find all ItemData objects
        string[] guids = AssetDatabase.FindAssets("t:ItemData");
        
        string folderPath = "Assets/Resources/Icons";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.CreateFolder("Assets/Resources", "Icons");

        int processed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemData itemInfo = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            
            if (itemInfo != null && itemInfo.Prefab != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(itemInfo.Prefab);
                
                if (preview == null) 
                {
                    preview = AssetPreview.GetMiniThumbnail(itemInfo.Prefab);
                }

                if (preview != null)
                {
                    string safeName = itemInfo.ItemName.Replace(" ", "");
                    string iconPath = $"{folderPath}/{safeName}_Icon.png";
                    
                    // Duplicate the texture because AssetPreview textures are not readable
                    Texture2D readableCopy = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
                    RenderTexture tempRT = RenderTexture.GetTemporary(preview.width, preview.height);
                    Graphics.Blit(preview, tempRT);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = tempRT;
                    readableCopy.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                    readableCopy.Apply();
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(tempRT);

                    // Remove the dark background by making dark pixels transparent
                    Color[] pixels = readableCopy.GetPixels();
                    for (int p = 0; p < pixels.Length; p++)
                    {
                        // Unity preview background is roughly (0.22, 0.22, 0.22)
                        // Make any pixel close to that color transparent
                        float brightness = (pixels[p].r + pixels[p].g + pixels[p].b) / 3f;
                        if (brightness < 0.3f)
                        {
                            pixels[p] = new Color(0, 0, 0, 0); // Fully transparent
                        }
                    }
                    readableCopy.SetPixels(pixels);
                    readableCopy.Apply();

                    byte[] bytes = readableCopy.EncodeToPNG();
                    File.WriteAllBytes(iconPath, bytes);
                    
                    AssetDatabase.Refresh();

                    // Convert to Sprite
                    TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.alphaIsTransparency = true;
                        importer.SaveAndReimport();
                    }

                    // Assign to ItemData
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                    if (sprite != null)
                    {
                        itemInfo.Icon = sprite;
                        EditorUtility.SetDirty(itemInfo);
                        processed++;
                        Debug.Log($"[BakePrefabIcons] Baked icon for {itemInfo.ItemName}");
                    }
                }
            }
        }
        
        AssetDatabase.SaveAssets();
        Debug.Log($"[BakePrefabIcons] Finished! Baked {processed} icons.");
    }
}
