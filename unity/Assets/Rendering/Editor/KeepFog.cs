using UnityEditor;
using UnityEngine;

namespace TarmacGauntlet.Rendering.Editor
{
    /// <summary>
    /// Keeps the shaders' exponential fog variants in builds. The race turns its haze on at runtime (TimeOfDay), and no
    /// scene carries fog in its lighting settings, so the default stripping would leave the haze out of the built game.
    /// ProjectSettings is not in the repository, so this sets it each time the editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class KeepFog
    {
        static KeepFog()
        {
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty stripping = settings.FindProperty("m_FogStripping"), linear = settings.FindProperty("m_FogKeepLinear"),
                exp = settings.FindProperty("m_FogKeepExp"), exp2 = settings.FindProperty("m_FogKeepExp2");
            if (stripping == null || linear == null || exp == null || exp2 == null)
            {
                Debug.LogError("Rendering: the graphics settings have no fog stripping, so the race's haze may be stripped from builds.");
                return;
            }
            // 1 is Custom: keep the exponential fog the race uses, and only that, whatever the scenes hold.
            if (stripping.intValue == 1 && exp.boolValue && !linear.boolValue && !exp2.boolValue) return;
            stripping.intValue = 1;
            exp.boolValue = true;
            linear.boolValue = false;
            exp2.boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
