using UnityEditor;

/// <summary>
/// Inspector wrapper for PlayerStatsUI. The old generated stats UI setup menu was removed
/// because MainScene now uses the hand-built show UI/state hierarchy.
/// </summary>
[CustomEditor(typeof(PlayerStatsUI))]
public class SetupStatsUI : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "PlayerStatsUI now auto-binds to show UI/state. The old generated HUD setup has been disabled.",
            MessageType.Info);
    }
}
