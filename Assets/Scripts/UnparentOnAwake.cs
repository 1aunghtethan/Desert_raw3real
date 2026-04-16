using UnityEngine;

/// <summary>
/// DEPRECATED: Camera parenting is now handled by CameraModeSwitcher.
/// This script is kept as a no-op so existing scene references don't break.
/// You can safely remove this component from GameObjects in the Inspector.
/// </summary>
public class UnparentOnAwake : MonoBehaviour
{
    // Intentionally empty — CameraModeSwitcher handles parent/unparent now.
    // void Awake() { transform.SetParent(null); }  // OLD behavior removed
}
