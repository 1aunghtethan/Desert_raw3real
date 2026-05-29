using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class AnimalSpawnerSetup : EditorWindow
{
    [MenuItem("Tools/Setup Animal Spawner")]
    public static void SetupSpawner()
    {
        AnimalSpawner spawner = FindFirstObjectByType<AnimalSpawner>();
        
        if (spawner == null)
        {
            GameObject go = new GameObject("AnimalSpawner");
            spawner = go.AddComponent<AnimalSpawner>();
            Debug.Log("[AnimalSpawnerSetup] Created new AnimalSpawner GameObject.");
        }

        // 1. Find or create AnimalData folder
        string dataPath = "Assets/Data/Animals";
        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
            AssetDatabase.Refresh();
        }

        // 2. Find animal prefabs ONLY in the user specified folder
        List<string> searchFolders = new List<string> { 
            "Assets/Prefabs/animal"
        };

        string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders.ToArray());
        List<AnimalData> animalPool = new List<AnimalData>();
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            
            if (prefab == null) continue;

            // Process prefabs in this folder (Dog, Kitty, Deer, etc.)
            if (prefab != null)
            {
                if (path.Contains("Fish") || path.Contains("Dolfin") || path.Contains("Shark")) continue;

                // A. Ensure components exist on prefab
                GameObject prefabInstance = PrefabUtility.LoadPrefabContents(path);
                bool modified = false;

                // Ensure CreatureMover (namespace ithappy.Animals_FREE)
                if (prefabInstance.GetComponent<ithappy.Animals_FREE.CreatureMover>() == null)
                {
                    prefabInstance.AddComponent<ithappy.Animals_FREE.CreatureMover>();
                    modified = true;
                }

                if (prefabInstance.GetComponent<AnimalAI>() == null)
                {
                    prefabInstance.AddComponent<AnimalAI>();
                    modified = true;
                }

                if (prefabInstance.GetComponent<AnimalHealth>() == null)
                {
                    prefabInstance.AddComponent<AnimalHealth>();
                    modified = true;
                }

                // Ensure a collider for hit detection
                if (prefabInstance.GetComponent<Collider>() == null)
                {
                    CapsuleCollider cap = prefabInstance.AddComponent<CapsuleCollider>();
                    cap.center = Vector3.up * 0.5f;
                    cap.height = 1f;
                    cap.radius = 0.3f;
                    modified = true;
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabInstance, path);
                }
                PrefabUtility.UnloadPrefabContents(prefabInstance);

                // B. Create/Find AnimalData for this animal
                // Use a sanitized name: remove common suffixes like _v1, _001, etc.
                string animalName = prefab.name;
                animalName = System.Text.RegularExpressions.Regex.Replace(animalName, @"(_v[0-9]+|_00[0-9]+)$", "");
                // Ensure name is clean for filenames
                animalName = System.Text.RegularExpressions.Regex.Replace(animalName, @"[^a-zA-Z0-9_]", "");

                if (animalName != "Deer" && animalName != "Kitty") continue;
                
                string assetPath = $"{dataPath}/{animalName}_Data.asset";
                AnimalData data = AssetDatabase.LoadAssetAtPath<AnimalData>(assetPath);

                if (data == null)
                {
                    data = ScriptableObject.CreateInstance<AnimalData>();
                    data.AnimalName = animalName;
                    data.Prefab = prefab;
                    AssetDatabase.CreateAsset(data, assetPath);
                    Debug.Log($"[AnimalSpawnerSetup] Created AnimalData for {animalName}");
                }
                else
                {
                    data.Prefab = prefab;
                    EditorUtility.SetDirty(data);
                }

                if (!animalPool.Contains(data))
                {
                    animalPool.Add(data);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (animalPool.Count > 0)
        {
            spawner.AnimalPool = animalPool;
            spawner.DeerCount = 2;
            spawner.KittyCount = 1;
            spawner.AnimalCount = spawner.DeerCount + spawner.KittyCount;
            spawner.SpawnRange = 100f;
            spawner.DespawnDistance = 200f;
            
            EditorUtility.SetDirty(spawner);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimalSpawnerSetup] Assigned {animalPool.Count} AnimalData assets to the spawner.");
        }

        Selection.activeGameObject = spawner.gameObject;
    }
}
