using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EquipmentHolder))]
public class EquipmentHolderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EquipmentHolder holder = (EquipmentHolder)target;
        ItemData currentItem = holder.CurrentItem;
        bool canSave = Application.isPlaying && !holder.UsePrefabTransformWhenHeld && holder.CurrentBehaviour != null && currentItem != null;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!canSave))
        {
            if (GUILayout.Button("Save Current Hold Position To ItemData"))
            {
                if (holder.SaveCurrentHoldTransformToItemData())
                {
                    EditorUtility.SetDirty(currentItem);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[EquipmentHolder] Saved hold transform to {currentItem.name}.");
                }
            }
        }

        if (holder.UsePrefabTransformWhenHeld)
        {
            EditorGUILayout.HelpBox("Prefab transform mode is enabled. Edit the item prefab root transform to change how it appears in hand.", MessageType.Info);
        }
        else if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode, equip an item, adjust the held object, then save its hold position.", MessageType.Info);
        }
        else if (!canSave)
        {
            EditorGUILayout.HelpBox("Equip an item before saving a hold position.", MessageType.Info);
        }
    }
}
