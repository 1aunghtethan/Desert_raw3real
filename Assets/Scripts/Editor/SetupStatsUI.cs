using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// Editor tool for PlayerStatsUI (Dynamic Canvas Version).
/// - "Setup Stats UI Parents": Creates basic location objects for manual design.
/// </summary>
[CustomEditor(typeof(PlayerStatsUI))]
public class SetupStatsUI : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        PlayerStatsUI statsUI = (PlayerStatsUI)target;
        
        GUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        
        // Manual assignment is now preferred in the Inspector
        /*
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("🔄 Manual Refresh Icons (Auto-Detect Children)", GUILayout.Height(35)))
        {
            statsUI.ManualRefreshIcons();
        }
        */
        
        GUI.backgroundColor = Color.white;
    }

    [MenuItem("Tools/Setup Stats UI Parents")]
    public static void CreateStatsUIParents()
    {
        // Find or create the canvas
        GameObject canvasObj = GameObject.Find("StatsCanvas");
        if (canvasObj == null) canvasObj = GameObject.Find("HotbarCanvas");
        
        if (canvasObj == null)
        {
            canvasObj = new GameObject("StatsCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create StatsCanvas");
        }

        Vector2 healthPos = new Vector2(-250f, 60f);
        Vector2 hungerPos = new Vector2(250f, 100f);
        Vector2 thirstPos = new Vector2(250f, 60f);
        Vector2 tempPos   = new Vector2(250f, 20f);

        RectTransform healthParent = CreateUIParent(canvasObj.transform, "HealthLocation", healthPos);
        RectTransform hungerParent = CreateUIParent(canvasObj.transform, "HungerLocation", hungerPos);
        RectTransform thirstParent = CreateUIParent(canvasObj.transform, "ThirstLocation", thirstPos);
        RectTransform tempParent   = CreateUIParent(canvasObj.transform, "TempLocation", tempPos);

        PlayerStatsUI statsUI = FindFirstObjectByType<PlayerStatsUI>();
        if (statsUI != null)
        {
            Undo.RecordObject(statsUI, "Assign Stats UI Parents");
            // Legacy parent fields are gone
            // statsUI.HealthIconsParent = healthParent;
            // statsUI.HungerIconsParent = hungerParent;
            // statsUI.ThirstIconsParent = thirstParent;
            // statsUI.TempIconsParent = tempParent;
            EditorUtility.SetDirty(statsUI);
            
            Debug.Log("<color=green>[SetupStatsUI]</color> Parents assigned! Create your icons inside these objects and click 'Manual Refresh Icons'.");
        }
        else
        {
            Debug.LogWarning("[SetupStatsUI] PlayerStatsUI not found in scene!");
        }
    }

    private static RectTransform CreateUIParent(Transform canvasTransform, string name, Vector2 position)
    {
        Transform existing = canvasTransform.Find(name);
        if (existing != null)
        {
            Debug.Log($"[SetupStatsUI] '{name}' already exists, reusing it.");
            return existing.GetComponent<RectTransform>();
        }

        GameObject obj = new GameObject(name);
        obj.transform.SetParent(canvasTransform, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(250f, 30f);

        Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
        return rt;
    }
}
