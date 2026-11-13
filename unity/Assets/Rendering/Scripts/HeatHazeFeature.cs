using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The race's heat haze. On a hot, sunny day (<see cref="Shimmer"/>) the air over far tarmac shimmers: once the post
    /// effects and the temporal upscaler are done, the frame is copied and redrawn with a rising wobble, strongest where the
    /// view runs low along the ground into the distance. Coming after the upscaler, the wobble is never smeared into its
    /// history. The cars' exhaust haze is drawn by its own particles, which bend the scene behind them: while
    /// <see cref="ExhaustHaze"/> is set this asks for the copy of the scene they read. Each follows its graphics option, and draws nothing at all with it off.
    /// </summary>
    public sealed class HeatHazeFeature : ScriptableRendererFeature
    {
        /// <summary>How strongly the far tarmac shimmers now, 0 to 1. Set by the race from the sun, the cloud and the wet.</summary>
        public static float Shimmer;
        /// <summary>True while something draws exhaust haze, which reads the scene's colour behind it.</summary>
        public static bool ExhaustHaze;
        /// <summary>
        /// True while something draws soft particles, which fade where they meet the scene and so read its depth. Set by
        /// the race at High and above, where the depth costs little.
        /// </summary>
        public static bool SoftParticles;
        /// <summary>
        /// True while the cars' headlamp beams are drawn: they read the scene's depth, to light only the ground. Set by the
        /// race at night, at every quality level.
        /// </summary>
        public static bool HeadlampBeams;

        private ShimmerPass shimmer;
        private InputPass inputs;
        private Material material;

        public override void Create()
        {
            shimmer = new ShimmerPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            inputs = new InputPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            bool haze = ExhaustHaze && GraphicsOptions.ExhaustHaze.On;
            bool soft = SoftParticles, beams = HeadlampBeams;
            if (haze || soft || beams)
            {
                ScriptableRenderPassInput wanted = ScriptableRenderPassInput.None;
                if (haze) wanted |= ScriptableRenderPassInput.Color;
                if (soft) wanted |= ScriptableRenderPassInput.Depth;
                if (beams) wanted |= ScriptableRenderPassInput.Depth;
                inputs.ConfigureInput(wanted);
                renderer.EnqueuePass(inputs);
            }
            if (Shimmer <= 0.01f || !GraphicsOptions.TarmacShimmer.On) return;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("HeatShimmer");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            shimmer.Material = material;
            shimmer.Strength = Mathf.Clamp01(Shimmer);
            shimmer.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(shimmer);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        /// <summary>Draws nothing: it only asks the renderer for the scene's colour or depth that the effects read.</summary>
        private sealed class InputPass : ScriptableRenderPass
        {
            public InputPass() => profilingSampler = new ProfilingSampler("Heat haze inputs");

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { }
        }

        private sealed class ShimmerPass : ScriptableRenderPass
        {
            /// <summary>The shimmer starts this far out and is at its strongest by the next, m.</summary>
            private const float Near = 45f, Far = 220f;
            /// <summary>How far the shimmer moves the scene at its strongest, pixels on a screen 1080 high.</summary>
            private const float Reach = 2.2f;
            private static readonly int StrengthId = Shader.PropertyToID("_ShimmerStrength"),
                ParamsId = Shader.PropertyToID("_ShimmerParams"), DepthId = Shader.PropertyToID("_ShimmerDepth"),
                InvViewProjId = Shader.PropertyToID("_ShimmerInvViewProj");

            public Material Material;
            public float Strength;

            private sealed class PassData
            {
                public TextureHandle Source, Depth;
                public Material Material;
            }

            public ShimmerPass()
            {
                profilingSampler = new ProfilingSampler("Tarmac shimmer");
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                TextureHandle frame = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(frame);
                desc.name = "Tarmac shimmer copy";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                desc.filterMode = FilterMode.Bilinear;
                desc.wrapMode = TextureWrapMode.Clamp;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(frame, copy, Vector2.one, Vector2.zero, passName: "Tarmac shimmer copy");

                Material.SetFloat(StrengthId, Strength);
                Material.SetVector(ParamsId, new Vector4(Near, Far, Reach, 0f));
                // The camera's own, as the post effects may leave other matrices set.
                var cameraData = frameData.Get<UniversalCameraData>();
                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
                Material.SetMatrix(InvViewProjId, viewProj.inverse);
                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Tarmac shimmer", out PassData data, profilingSampler))
                {
                    data.Source = copy;
                    data.Depth = resources.cameraDepthTexture;
                    data.Material = Material;
                    builder.UseTexture(copy);
                    builder.UseTexture(data.Depth);
                    builder.SetRenderAttachment(frame, 0);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                    {
                        d.Material.SetTexture(DepthId, (RTHandle)d.Depth);
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0);
                    });
                }
            }
        }
    }
}
