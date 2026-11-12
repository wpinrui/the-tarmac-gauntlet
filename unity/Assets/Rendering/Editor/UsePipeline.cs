using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Rendering.Editor
{
    /// <summary>
    /// Renders the project with the Universal Render Pipeline. ProjectSettings is not in the repository, so a fresh clone
    /// opens on the built-in pipeline: this puts the game's pipeline asset in the graphics settings each time the editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class UsePipeline
    {
        private const string AssetPath = "Assets/Rendering/Settings/Pipeline.asset";

        static UsePipeline()
        {
            // The asset database can still be loading here, so the pipeline is set a moment later.
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("Rendering: " + AssetPath + " is missing, the project stays on the built-in pipeline.");
                return;
            }
            if (GraphicsSettings.defaultRenderPipeline != asset) GraphicsSettings.defaultRenderPipeline = asset;
            // A quality level with its own pipeline would override the default one.
            if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != asset) QualitySettings.renderPipeline = null;
        }
    }
}
