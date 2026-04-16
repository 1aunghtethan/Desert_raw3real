using UnityEditor;
using UnityEngine;

public class CheckPath : MonoBehaviour
{
    [MenuItem("Tools/Check Project Path")]
    public static void ShowPath()
    {
        Debug.Log($"Application.dataPath: {Application.dataPath}");
        Debug.Log($"Project path: {System.IO.Directory.GetParent(Application.dataPath).FullName}");
    }
}
