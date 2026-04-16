using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class SetupLogQuarters
{
    [MenuItem("Tools/Setup Log Quarters")]
    public static void RunFix()
    {
        Debug.Log("[SetupLogQuarters] Setting up Quarter Logs...");

        string[] quarterNames = { "Log_Quarter_1", "Log_Quarter_2" };
        string prefabDir = "Assets/After_cut/for_tree/";
        string itemDir = "Assets/Resources/Items/";

        List<GameObject> quarterPrefabs = new List<GameObject>();

        foreach (string quarterName in quarterNames)
        {
            string prefabPath = prefabDir + quarterName + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) continue;

            quarterPrefabs.Add(prefab);

            // Scale them up 4x like the other logs
            prefab.transform.localScale = new Vector3(4f, 4f, 4f);

            // 1. Create or Find ItemData
            string itemDataPath = itemDir + quarterName + "_ItemData.asset";
            ItemData itemData = AssetDatabase.LoadAssetAtPath<ItemData>(itemDataPath);
            if (itemData == null)
            {
                itemData = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(itemData, itemDataPath);
            }

            var soItem = new SerializedObject(itemData);
            soItem.Update();
            soItem.FindProperty("ItemName").stringValue = quarterName.Replace("_", " ");
            soItem.FindProperty("Prefab").objectReferenceValue = prefab;
            soItem.FindProperty("DropPrefab").objectReferenceValue = prefab;
            soItem.FindProperty("Type").enumValueIndex = (int)ItemType.Tool; // Fix compile error: ItemType.Tool
            soItem.FindProperty("HoldScale").floatValue = 0.4f;
            soItem.FindProperty("HoldPosition").vector3Value = new Vector3(0.3f, -0.2f, 0.6f);
            soItem.FindProperty("HoldRotation").vector3Value = new Vector3(0, 90, 30);
            soItem.FindProperty("PickupsSpinAndBob").boolValue = false; // Heavy heavy heavy!
            soItem.ApplyModifiedProperties();

            // 2. Configure Prefab Components
            // Make sure it has a BoxCollider
            BoxCollider col = prefab.GetComponent<BoxCollider>();
            if (col == null) col = prefab.AddComponent<BoxCollider>();
            col.size = new Vector3(0.3f, 2f, 0.3f); // slightly thinner for quarters

            // Make sure it has a Rigidbody
            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            if (rb == null) rb = prefab.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.linearDamping = 0.5f;

            // Give it LootItem
            LootItem loot = prefab.GetComponent<LootItem>();
            if (loot == null) loot = prefab.AddComponent<LootItem>();
            loot.Data = itemData;

            // Remove PlantHealth
            PlantHealth ph = prefab.GetComponent<PlantHealth>();
            if (ph != null) GameObject.DestroyImmediate(ph, true);

            // Give it OutlineController
            OutlineController oc = prefab.GetComponent<OutlineController>();
            if (oc == null) oc = prefab.AddComponent<OutlineController>();
            
            // NO WorldItemSpin!
            WorldItemSpin spin = prefab.GetComponent<WorldItemSpin>();
            if (spin != null) GameObject.DestroyImmediate(spin, true);

            EditorUtility.SetDirty(prefab);
            EditorUtility.SetDirty(itemData);
        }

        // 3. Update LogData.asset's LootPrefabs
        string logDataPath = "Assets/Resources/Plants/LogData.asset";
        PlantData logData = AssetDatabase.LoadAssetAtPath<PlantData>(logDataPath);
        if (logData != null && quarterPrefabs.Count > 0)
        {
            var soLog = new SerializedObject(logData);
            soLog.Update();
            SerializedProperty lootProp = soLog.FindProperty("LootPrefabs");
            lootProp.ClearArray();

            for (int i = 0; i < quarterPrefabs.Count; i++)
            {
                lootProp.InsertArrayElementAtIndex(i);
                lootProp.GetArrayElementAtIndex(i).objectReferenceValue = quarterPrefabs[i];
            }
            soLog.FindProperty("LootCount").intValue = 4;
            soLog.ApplyModifiedProperties();
            EditorUtility.SetDirty(logData);
            Debug.Log("[SetupLogQuarters] Assigned quarters to LogData.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[SetupLogQuarters] Complete! Log Quarters configured as heavy dynamic items.");
    }
}
