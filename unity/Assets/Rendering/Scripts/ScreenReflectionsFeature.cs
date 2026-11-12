using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// Screen-space reflections for the surfaces that ask for them with a "TarmacReflect" pass (TarmacReflect.hlsl): the
    /// wet track and its puddles and the cars' clear coat. Once the opaque pass is drawn, those surfaces draw what they
    /// reflect, each reflecting pixel traces a ray through the depth buffer, and where it hits something on screen that
    /// replaces the reflection probe's view in the frame. Where it hits nothing, or off the screen, the probe's stays.
    /// Follows <see cref="GraphicsOptions.ScreenReflections"/>, live.
    /// </summary>
    public sealed class ScreenReflectionsFeature : ScriptableRendererFeature
    {
        private ScreenReflectionsPass pass;
        private Material material;

        public override void Create()
        {
            pass = new ScreenReflectionsPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!GraphicsOptions.ScreenReflections.On) return;
            if (renderingData.cameraData.cameraType != CameraType.Game || renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("ScreenReflections");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            pass.Material = material;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class ScreenReflectionsPass : ScriptableRenderPass
        {
            /// <summary>How far a ray reaches, m, and how thick it takes what it passes behind to be, m.</summary>
            private const float Reach = 80f, Thickness = 0.4f;
            /// <summary>The most steps a ray takes at each quality level, Low upwards: one a pixel up to that.</summary>
            private static readonly int[] StepsAt = { 32, 48, 64, 96 };
            /// <summary>The shader's passes.</summary>
            private const int CopyPass = 0, TracePass = 1, CompositePass = 2;

            private static readonly ShaderTagId Tag = new ShaderTagId("TarmacReflect");
            private static readonly int SourceId = Shader.PropertyToID("_SsrSource"), SceneId = Shader.PropertyToID("_SsrScene"),
                DepthId = Shader.PropertyToID("_SsrDepth"), SurfaceId = Shader.PropertyToID("_SsrSurface"),
                ProbeId = Shader.PropertyToID("_SsrProbe"), TraceId = Shader.PropertyToID("_SsrTrace"),
                ViewProjId = Shader.PropertyToID("_SsrViewProj"), InvViewProjId = Shader.PropertyToID("_SsrInvViewProj"),
                CameraId = Shader.PropertyToID("_SsrCamera"), ForwardId = Shader.PropertyToID("_SsrForward"),
                SizeId = Shader.PropertyToID("_SsrSize"), NearId = Shader.PropertyToID("_SsrNear"), ReachId = Shader.PropertyToID("_SsrReach"),
                StepsId = Shader.PropertyToID("_SsrSteps"), ThicknessId = Shader.PropertyToID("_SsrThickness");

            public Material Material;

            public ScreenReflectionsPass()
            {
                profilingSampler = new ProfilingSampler("Screen reflections");
                // The composite reads the frame and writes it back, so it must not be drawn straight to the screen.
                requiresIntermediateTexture = true;
            }

            private sealed class SurfaceData
            {
                public RendererListHandle List;
            }

            private sealed class DrawData
            {
                public Material Material;
                public int Pass;
                public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
                public readonly int[] Ids = new int[4];
                public readonly TextureHandle[] Textures = new TextureHandle[4];
                public int Count;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();
                if (Material == null || resources.isActiveTargetBackBuffer) return;
                TextureHandle colour = resources.activeColorTexture, depth = resources.activeDepthTexture, depthTexture = resources.cameraDepthTexture;
                if (!colour.IsValid() || !depth.IsValid() || !depthTexture.IsValid()) return;

                TextureDesc frame = renderGraph.GetTextureDesc(colour);
                int w = frame.width, h = frame.height;
                TextureHandle surface = Target(renderGraph, "Screen reflections surface", w, h, true);
                TextureHandle probe = Target(renderGraph, "Screen reflections probe", w, h, true);
                TextureHandle trace = Target(renderGraph, "Screen reflections trace", w, h, false);
                TextureDesc copy = frame;
                copy.name = "Screen reflections scene";
                copy.clearBuffer = false;
                copy.msaaSamples = MSAASamples.None;
                TextureHandle scene = renderGraph.CreateTexture(copy);

                // What the reflecting surfaces reflect, drawn over themselves.
                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Screen reflections surfaces", out SurfaceData data, profilingSampler))
                {
                    DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(Tag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                    var list = new RendererListParams(renderingData.cullResults, drawing, new FilteringSettings(RenderQueueRange.opaque));
                    data.List = renderGraph.CreateRendererList(list);
                    builder.UseRendererList(data.List);
                    builder.SetRenderAttachment(surface, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(probe, 1, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.SetRenderFunc((SurfaceData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.List));
                }

                Camera camera = cameraData.camera;
                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), false) * cameraData.GetViewMatrix();
                int steps = StepsAt[Mathf.Clamp(QualityLevels.Level, 0, StepsAt.Length - 1)];

                Draw(renderGraph, "Screen reflections copy", CopyPass, scene, null, (SourceId, colour));
                Draw(renderGraph, "Screen reflections trace", TracePass, trace, p =>
                {
                    p.SetMatrix(ViewProjId, viewProj);
                    p.SetMatrix(InvViewProjId, viewProj.inverse);
                    p.SetVector(CameraId, camera.transform.position);
                    p.SetVector(ForwardId, camera.transform.forward);
                    p.SetVector(SizeId, new Vector4(w, h, 1f / w, 1f / h));
                    p.SetFloat(NearId, camera.nearClipPlane);
                    p.SetFloat(ReachId, Reach);
                    p.SetFloat(StepsId, steps);
                    p.SetFloat(ThicknessId, Thickness);
                }, (SceneId, scene), (DepthId, depthTexture), (SurfaceId, surface));
                Draw(renderGraph, "Screen reflections composite", CompositePass, colour,
                    p => p.SetVector(SizeId, new Vector4(w, h, 1f / w, 1f / h)),
                    (SceneId, scene), (SurfaceId, surface), (ProbeId, probe), (TraceId, trace));
            }

            /// <summary>A full-screen pass of the shader into a target, reading the textures given.</summary>
            private void Draw(RenderGraph renderGraph, string name, int shaderPass, TextureHandle target,
                System.Action<MaterialPropertyBlock> set, params (int id, TextureHandle texture)[] reads)
            {
                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(name, out DrawData data, profilingSampler))
                {
                    data.Material = Material;
                    data.Pass = shaderPass;
                    data.Properties.Clear();
                    set?.Invoke(data.Properties);
                    data.Count = reads.Length;
                    for (int i = 0; i < reads.Length; i++)
                    {
                        data.Ids[i] = reads[i].id;
                        data.Textures[i] = reads[i].texture;
                        builder.UseTexture(reads[i].texture, AccessFlags.Read);
                    }
                    builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext context) =>
                    {
                        for (int i = 0; i < d.Count; i++) d.Properties.SetTexture(d.Ids[i], (RTHandle)d.Textures[i]);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, d.Pass, MeshTopology.Triangles, 3, 1, d.Properties);
                    });
                }
            }

            private static TextureHandle Target(RenderGraph renderGraph, string name, int w, int h, bool clear) =>
                renderGraph.CreateTexture(new TextureDesc(w, h)
                {
                    name = name,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = clear,
                    clearColor = Color.clear,
                });
        }
    }
}
