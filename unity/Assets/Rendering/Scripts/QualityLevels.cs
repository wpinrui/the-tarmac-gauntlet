using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The game's quality levels, each its own pipeline asset in Resources/Quality. The asset sets the sun's shadows (cascades,
    /// map size, distance and bias), the other lights' shadow map, the render scale and the upscaler: Low and Medium draw at
    /// 67% and 80% of the screen and STP upscales them, smoothing edges as it does; High and Ultra draw at full size and STP
    /// runs there as temporal anti-aliasing, so thin rails, fences and leaves hold still. Medium is the level for an Intel
    /// Arc B390 at 1920 x 1200 and 60 fps, Ultra for an RTX 4070 at 2560 x 1440 and 60 fps.
    /// Low and Medium share a renderer; High and Ultra each have their own, with finer ambient occlusion at full resolution
    /// that darkens the ambient light fully and the sunlight a little. Every game camera gets post-processing and SMAA;
    /// URP swaps the SMAA for STP wherever STP runs, so SMAA only smooths the edges on hardware that cannot run STP.
    /// The level is remembered between runs.
    /// <para>
    /// Each renderer carries URP's Screen Space Shadows, drawn with the game's own shader (ScreenShadows.shader): soft
    /// penumbrae, cascades blended at their edges and contact shadows, as much of each as the level's
    /// <see cref="ShadowQuality"/> sets. It is on while its graphics option is, and the ambient occlusion likewise.
    /// </para>
    /// </summary>
    public static class QualityLevels
    {
        public static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };

        private const string Key = "settings.quality.level";
        private const int DefaultLevel = 1;
        private static readonly AntialiasingQuality[] Smaa =
            { AntialiasingQuality.Low, AntialiasingQuality.Medium, AntialiasingQuality.High, AntialiasingQuality.High };
        /// <summary>The renderers in Resources/Quality, each used by one or more of the levels' assets.</summary>
        private static readonly string[] Renderers = { "Renderer", "RendererHigh", "RendererUltra" };

        /// <summary>The screen shadow shader's settings at one quality level.</summary>
        public readonly struct ShadowQuality
        {
            /// <summary>Penumbra half width per metre between a shadow's caster and where it falls: a little wider than the sun makes it.</summary>
            public readonly float Spread;
            /// <summary>Shadow map taps filtering the penumbra, and searching for its caster.</summary>
            public readonly int FilterTaps, SearchTaps;
            /// <summary>The widest penumbra, in shadow map texels.</summary>
            public readonly float WidestTexels;
            /// <summary>Contact shadows: how far towards the sun they look, m, in how many steps, drawn out to how far from the camera, m.</summary>
            public readonly float ContactReach;
            public readonly int ContactSteps;
            public readonly float ContactFar, ContactStrength;
            /// <summary>The share of each cascade's radius, at its edge, blended into the next cascade.</summary>
            public readonly float CascadeBlend;

            public ShadowQuality(float spread, int filterTaps, int searchTaps, float widestTexels, float contactReach,
                int contactSteps, float contactFar, float contactStrength, float cascadeBlend)
            {
                Spread = spread;
                FilterTaps = filterTaps;
                SearchTaps = searchTaps;
                WidestTexels = widestTexels;
                ContactReach = contactReach;
                ContactSteps = contactSteps;
                ContactFar = contactFar;
                ContactStrength = contactStrength;
                CascadeBlend = cascadeBlend;
            }
        }

        /// <summary>The screen shadows at each level, Low to Ultra. Low and Medium only use them if the player turns them on.</summary>
        public static readonly ShadowQuality[] Shadows =
        {
            new ShadowQuality(0.006f, 8, 8, 6f, 0.3f, 6, 40f, 0.8f, 0f),
            new ShadowQuality(0.007f, 12, 12, 10f, 0.35f, 8, 60f, 0.85f, 0.1f),
            new ShadowQuality(0.008f, 16, 16, 16f, 0.4f, 12, 80f, 0.9f, 0.12f),
            new ShadowQuality(0.008f, 32, 24, 24f, 0.5f, 20, 120f, 0.9f, 0.15f),
        };

        private static readonly int SoftId = Shader.PropertyToID("_TarmacShadowSoft"),
            ContactId = Shader.PropertyToID("_TarmacShadowContact"), BlendId = Shader.PropertyToID("_TarmacShadowBlend"),
            FrameId = Shader.PropertyToID("_TarmacShadowFrame");

        private static readonly UniversalRenderPipelineAsset[] assets = new UniversalRenderPipelineAsset[4];
        private static int level = -1;
        private static bool started;
        /// <summary>What the game changed, put back when it ends, since in the editor the changes would stay in the assets.</summary>
        private static RenderPipelineAsset pipelineBefore;
        private static readonly List<ScriptableRendererFeature> occlusion = new List<ScriptableRendererFeature>();
        private static readonly List<ScriptableRendererFeature> screenShadows = new List<ScriptableRendererFeature>();
        private static readonly Dictionary<ScriptableRendererFeature, bool> activeBefore = new Dictionary<ScriptableRendererFeature, bool>();

        /// <summary>0 Low, 1 Medium, 2 High, 3 Ultra.</summary>
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
            occlusion.Clear();
            screenShadows.Clear();
            activeBefore.Clear();
            foreach (string name in Renderers)
            {
                var renderer = Resources.Load<UniversalRendererData>("Quality/" + name);
                if (renderer == null)
                {
                    Debug.LogError("Rendering: Resources/Quality/" + name + " is missing.");
                    continue;
                }
                foreach (ScriptableRendererFeature f in renderer.rendererFeatures)
                {
                    if (f is ScreenSpaceAmbientOcclusion) occlusion.Add(f);
                    // URP's own feature is internal, so it is found by its type's name.
                    else if (f != null && f.GetType().Name == "ScreenSpaceShadows") screenShadows.Add(f);
                    else continue;
                    activeBefore[f] = f.isActive;
                }
            }
            RenderPipelineManager.beginCameraRendering += Configure;
            GraphicsOptions.AmbientOcclusion.Changed += ShowOcclusion;
            GraphicsOptions.SoftShadows.Changed += ShowScreenShadows;
            Application.quitting += End;
            Apply();
            ShowOcclusion(GraphicsOptions.AmbientOcclusion);
            ShowScreenShadows(GraphicsOptions.SoftShadows);
        }

        private static void End()
        {
            Application.quitting -= End;
            RenderPipelineManager.beginCameraRendering -= Configure;
            GraphicsOptions.AmbientOcclusion.Changed -= ShowOcclusion;
            GraphicsOptions.SoftShadows.Changed -= ShowScreenShadows;
            QualitySettings.renderPipeline = pipelineBefore;
            foreach (KeyValuePair<ScriptableRendererFeature, bool> f in activeBefore) f.Key.SetActive(f.Value);
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
            ShadowQuality s = Shadows[level];
            Shader.SetGlobalVector(SoftId, new Vector4(s.Spread, s.FilterTaps, s.SearchTaps, s.WidestTexels));
            Shader.SetGlobalVector(ContactId, new Vector4(s.ContactReach, s.ContactSteps, s.ContactFar, s.ContactStrength));
            Shader.SetGlobalVector(BlendId, new Vector4(s.CascadeBlend, 0f, 0f, 0f));
        }

        private static void ShowOcclusion(GraphicsOption option)
        {
            foreach (ScriptableRendererFeature f in occlusion) f.SetActive(option.On);
        }

        private static void ShowScreenShadows(GraphicsOption option)
        {
            foreach (ScriptableRendererFeature f in screenShadows) f.SetActive(option.On);
        }

        private static void Configure(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || level < 0) return;
            // The screen shadows' noise turns each frame, and STP averages it out. STP needs compute shaders; without them
            // nothing averages it, so it stays still rather than crawl.
            Shader.SetGlobalFloat(FrameId, SystemInfo.supportsComputeShaders ? Time.frameCount & 63 : 0);
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (!data.renderPostProcessing) data.renderPostProcessing = true;
            if (data.antialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing) data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (data.antialiasingQuality != Smaa[level]) data.antialiasingQuality = Smaa[level];
        }
    }
}
