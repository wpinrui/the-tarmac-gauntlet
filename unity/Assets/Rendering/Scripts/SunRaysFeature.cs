using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The sun's rays on the race's screen, before the post-processing so the bloom catches them: the bright sky round the
    /// sun is smeared out from it at a quarter of the resolution, so light streams between the trees, the buildings and the
    /// clouds in front of it, with a glare round the sun as much of it as shows (the SunRays shader). With the lens flare
    /// option a faint string of ghosts crosses the screen from it. All of it fades as the sun goes off screen, behind
    /// thick cloud or down. Each follows its graphics option.
    /// </summary>
    public sealed class SunRaysFeature : ScriptableRendererFeature
    {
        private SunRaysPass pass;
        private Material mask, near, far, composite;

        public override void Create()
        {
            pass = new SunRaysPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!GraphicsOptions.SunRays.On || !Atmosphere.Present) return;
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || renderingData.cameraData.cameraType != CameraType.Game || camera.targetTexture != null) return;
            float fade = Fade(camera, Atmosphere.Now.SunDir);
            if (fade <= 0.001f || !Materials()) return;
            pass.Set(mask, near, far, composite, fade, GraphicsOptions.LensFlare.On);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        /// <summary>How much the rays show for this camera: none with the sun behind it or down, fading as it leaves the screen.</summary>
        public static float Fade(Camera camera, Vector3 sunDir)
        {
            Vector3 v = camera.WorldToViewportPoint(camera.transform.position + sunDir * 10000f);
            if (v.z <= 0f) return 0f;
            float edge = Mathf.Max(Mathf.Abs(v.x - 0.5f), Mathf.Abs(v.y - 0.5f));
            float onScreen = 1f - Smooth(0.45f, 0.9f, edge);
            return onScreen * Smooth(-0.02f, 0.06f, sunDir.y);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private bool Materials()
        {
            if (mask != null) return true;
            Shader shader = Resources.Load<Shader>("Atmosphere/SunRays");
            if (shader == null) return false;
            mask = CoreUtils.CreateEngineMaterial(shader);
            near = CoreUtils.CreateEngineMaterial(shader);
            far = CoreUtils.CreateEngineMaterial(shader);
            composite = CoreUtils.CreateEngineMaterial(shader);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(mask);
            CoreUtils.Destroy(near);
            CoreUtils.Destroy(far);
            CoreUtils.Destroy(composite);
            mask = near = far = composite = null;
        }

        private sealed class SunRaysPass : ScriptableRenderPass
        {
            private const int Mask = 0, Blur = 1, Composite = 2;
            /// <summary>Brightness the sky round the sun must pass to stream: the sun, its halo and sunlit cloud edges.</summary>
            private const float Threshold = 1.1f;
            /// <summary>The rays', the glare's and the lens flare's strength with the sun in full view.</summary>
            private const float RayStrength = 0.35f, GlareStrength = 0.5f, FlareStrength = 0.25f;
            /// <summary>Share of the way to the sun the first blur reaches, and the second.</summary>
            private const float NearReach = 0.3f, FarReach = 0.9f;

            private static readonly int DepthId = Shader.PropertyToID("_TgDepth"), MaskId = Shader.PropertyToID("_TgMask"),
                ViewProjId = Shader.PropertyToID("_TgViewProj"), CamPosId = Shader.PropertyToID("_TgCamPos"),
                RaysId = Shader.PropertyToID("_TgRays"), RayStepId = Shader.PropertyToID("_TgRayStep");

            private sealed class PassData
            {
                public Material Material;
                public TextureHandle Source, Depth, Mask;
                public Matrix4x4 ViewProj;
                public Vector4 CamPos, Rays, Step;
                public int Pass;
            }

            private Material mask, near, far, composite;
            private float fade;
            private bool flare;

            public SunRaysPass()
            {
                profilingSampler = new ProfilingSampler("Sun rays");
                requiresIntermediateTexture = true;
            }

            public void Set(Material maskMaterial, Material nearMaterial, Material farMaterial, Material compositeMaterial, float shown, bool lensFlare)
            {
                mask = maskMaterial;
                near = nearMaterial;
                far = farMaterial;
                composite = compositeMaterial;
                fade = shown;
                flare = lensFlare;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle color = resources.activeColorTexture, depth = resources.cameraDepthTexture;
                if (!color.IsValid() || !depth.IsValid()) return;
                TextureDesc frame = renderGraph.GetTextureDesc(color);
                int w = Mathf.Max(1, frame.width / 4), h = Mathf.Max(1, frame.height / 4);

                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
                Vector3 eye = cameraData.camera.transform.position;
                var camPos = new Vector4(eye.x, eye.y, eye.z, 0f);
                var rays = new Vector4(Threshold, RayStrength * fade, GlareStrength * fade, flare ? FlareStrength * fade : 0f);
                float aspect = (float)frame.width / Mathf.Max(1, frame.height);

                TextureHandle masked = Quarter(renderGraph, "Sun rays mask", w, h);
                TextureHandle streamed = Quarter(renderGraph, "Sun rays near", w, h);
                TextureHandle rayed = Quarter(renderGraph, "Sun rays far", w, h);

                Draw(renderGraph, "Sun rays mask", mask, Mask, color, depth, TextureHandle.nullHandle, masked, viewProj, camPos, rays,
                    new Vector4(0f, aspect, 0f, 0f));
                Draw(renderGraph, "Sun rays near", near, Blur, masked, TextureHandle.nullHandle, TextureHandle.nullHandle, streamed, viewProj,
                    camPos, rays, new Vector4(NearReach, aspect, 0f, 0f));
                Draw(renderGraph, "Sun rays far", far, Blur, streamed, TextureHandle.nullHandle, TextureHandle.nullHandle, rayed, viewProj,
                    camPos, rays, new Vector4(FarReach, aspect, 0f, 0f));
                Draw(renderGraph, "Sun rays composite", composite, Composite, rayed, TextureHandle.nullHandle, masked, color, viewProj,
                    camPos, rays, new Vector4(0f, aspect, 0f, 0f));
            }

            private void Draw(RenderGraph renderGraph, string name, Material material, int shaderPass, TextureHandle source, TextureHandle depth,
                TextureHandle maskTexture, TextureHandle target, Matrix4x4 viewProj, Vector4 camPos, Vector4 rays, Vector4 step)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out PassData data, profilingSampler))
                {
                    data.Material = material;
                    data.Source = source;
                    data.Depth = depth;
                    data.Mask = maskTexture;
                    data.ViewProj = viewProj;
                    data.CamPos = camPos;
                    data.Rays = rays;
                    data.Step = step;
                    data.Pass = shaderPass;
                    builder.UseTexture(source);
                    if (depth.IsValid()) builder.UseTexture(depth);
                    if (maskTexture.IsValid()) builder.UseTexture(maskTexture);
                    builder.SetRenderAttachment(target, 0, shaderPass == Composite ? AccessFlags.Write : AccessFlags.WriteAll);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        Material m = d.Material;
                        if (d.Depth.IsValid()) m.SetTexture(DepthId, (Texture)d.Depth);
                        if (d.Mask.IsValid()) m.SetTexture(MaskId, (Texture)d.Mask);
                        m.SetMatrix(ViewProjId, d.ViewProj);
                        m.SetVector(CamPosId, d.CamPos);
                        m.SetVector(RaysId, d.Rays);
                        m.SetVector(RayStepId, d.Step);
                        Blitter.BlitTexture(context.cmd, (RTHandle)d.Source, new Vector4(1f, 1f, 0f, 0f), m, d.Pass);
                    });
                }
            }

            private static TextureHandle Quarter(RenderGraph renderGraph, string name, int w, int h) =>
                renderGraph.CreateTexture(new TextureDesc(w, h)
                {
                    name = name,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                });
        }
    }
}
