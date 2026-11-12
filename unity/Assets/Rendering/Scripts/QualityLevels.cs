using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The game's quality levels, each its own pipeline asset in Resources/Quality. The asset sets the sun's shadows (cascades,
    /// map size and distance), the render scale and the upscaler: Low and Medium draw at 67% and 80% of the screen and STP
    /// upscales them, smoothing edges as it does; High draws at full size. Medium is the level for an Intel Arc B390 at
    /// 1920 x 1200 and 60 fps. Every game camera gets post-processing and SMAA, which smooths the edges wherever STP does not
    /// run. The ambient occlusion on the renderer follows its graphics option. The level is remembered between runs.
    /// </summary>
    public static class QualityLevels
    {
        public static readonly string[] Names = { "Low", "Medium", "High" };

        private const string Key = "settings.quality.level";
        private const int DefaultLevel = 1;
        private static readonly AntialiasingQuality[] Smaa = { AntialiasingQuality.Low, AntialiasingQuality.Medium, AntialiasingQuality.High };

        private static readonly UniversalRenderPipelineAsset[] assets = new UniversalRenderPipelineAsset[3];
        private static int level = -1;
        private static bool started;
        /// <summary>What the game changed, put back when it ends, since in the editor the changes would stay in the assets.</summary>
        private static RenderPipelineAsset pipelineBefore;
        private static ScriptableRendererFeature occlusion;
        private static bool occlusionBefore;

        /// <summary>0 Low, 1 Medium, 2 High.</summary>
        public static int Level
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Key, DefaultLevel), 0, Names.Length - 1);
            set
            {
                PlayerPrefs.SetInt(Key, Mathf.Clamp(value, 0, Names.Length - 1));
                PlayerPrefs.Save();
                Apply();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            if (started) return;
            started = true;
            pipelineBefore = QualitySettings.renderPipeline;
            var renderer = Resources.Load<UniversalRendererData>("Quality/Renderer");
            occlusion = null;
            if (renderer != null)
                foreach (ScriptableRendererFeature f in renderer.rendererFeatures)
                    if (f is ScreenSpaceAmbientOcclusion) occlusion = f;
            if (occlusion != null) occlusionBefore = occlusion.isActive;
            RenderPipelineManager.beginCameraRendering += Configure;
            GraphicsOptions.AmbientOcclusion.Changed += ShowOcclusion;
            Application.quitting += End;
            Apply();
            ShowOcclusion(GraphicsOptions.AmbientOcclusion);
        }

        private static void End()
        {
            Application.quitting -= End;
            RenderPipelineManager.beginCameraRendering -= Configure;
            GraphicsOptions.AmbientOcclusion.Changed -= ShowOcclusion;
            QualitySettings.renderPipeline = pipelineBefore;
            if (occlusion != null) occlusion.SetActive(occlusionBefore);
            level = -1;
            started = false;
        }

        private static void Apply()
        {
            level = Level;
            UniversalRenderPipelineAsset asset = assets[level];
            if (asset == null) asset = assets[level] = Resources.Load<UniversalRenderPipelineAsset>("Quality/" + Names[level]);
            if (asset == null)
            {
                Debug.LogError("Rendering: Resources/Quality/" + Names[level] + " is missing.");
                return;
            }
            if (QualitySettings.renderPipeline != asset) QualitySettings.renderPipeline = asset;
        }

        private static void ShowOcclusion(GraphicsOption option)
        {
            if (occlusion != null) occlusion.SetActive(option.On);
        }

        private static void Configure(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || level < 0) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (!data.renderPostProcessing) data.renderPostProcessing = true;
            if (data.antialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing) data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (data.antialiasingQuality != Smaa[level]) data.antialiasingQuality = Smaa[level];
        }
    }
}
