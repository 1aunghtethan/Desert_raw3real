using UnityEditor;
using UnityEditor.Build;

namespace StarterAssets
{
    public static class ScriptingDefineUtils
    {
        public static bool CheckScriptingDefine(string scriptingDefine)
        {
            NamedBuildTarget buildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var defines = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
            return defines.Contains(scriptingDefine);
        }

        public static void SetScriptingDefine(string scriptingDefine)
        {
            NamedBuildTarget buildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var defines = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
            if (!defines.Contains(scriptingDefine))
            {
                defines += $";{scriptingDefine}";
                PlayerSettings.SetScriptingDefineSymbols(buildTarget, defines);
            }
        }

        public static void RemoveScriptingDefine(string scriptingDefine)
        {
            NamedBuildTarget buildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var defines = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
            if (defines.Contains(scriptingDefine))
            {
                string newDefines = defines.Replace(scriptingDefine, "");
                PlayerSettings.SetScriptingDefineSymbols(buildTarget, newDefines);
            }
        }
    }
}