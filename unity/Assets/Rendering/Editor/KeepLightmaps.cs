using UnityEditor;
using UnityEngine;

namespace TarmacGauntlet.Rendering.Editor
{
    /// <summary>
    /// Keeps the shaders' lightmap variants in builds. The Team HQ puts its baked lightmap on at runtime, and no scene
    /// carries baked lighting data, so the default stripping would leave the lightmap out of the built game.
    /// ProjectSettings is not in the repository, so this sets it each time the editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class KeepLightmaps
    {
        static KeepLightmaps()
        {
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty stripping = settings.FindProperty("m_LightmapStripping"), plain = settings.FindProperty("m_LightmapKeepPlain");
            if (stripping == null || plain == null)
            {
                Debug.LogError("Rendering: the graphics settings have no lightmap stripping, so the Team HQ's lightmap may be stripped from builds.");
                return;
            }
            // 1 is Custom: keep the plain (non-directional) lightmap variants whatever the scenes hold.
            if (stripping.intValue == 1 && plain.boolValue) return;
            stripping.intValue = 1;
            plain.boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
