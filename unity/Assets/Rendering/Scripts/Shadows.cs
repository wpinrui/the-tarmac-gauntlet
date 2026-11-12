using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>The sun's shadows, which URP keeps on its pipeline asset rather than in the quality settings.</summary>
    public static class Shadows
    {
        private static float? saved;

        private static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        /// <summary>How far from the camera shadows are drawn, m. The asset gets its own distance back when the game ends.</summary>
        public static float Distance
        {
            get
            {
                UniversalRenderPipelineAsset asset = Asset;
                return asset != null ? asset.shadowDistance : QualitySettings.shadowDistance;
            }
            set
            {
                UniversalRenderPipelineAsset asset = Asset;
                if (asset == null)
                {
                    QualitySettings.shadowDistance = value;
                    return;
                }
                if (saved == null)
                {
                    saved = asset.shadowDistance;
                    Application.quitting += Restore;
                }
                asset.shadowDistance = value;
            }
        }

        private static void Restore()
        {
            Application.quitting -= Restore;
            UniversalRenderPipelineAsset asset = Asset;
            if (asset != null && saved != null) asset.shadowDistance = saved.Value;
            saved = null;
        }
    }
}
