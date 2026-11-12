using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// Draws the menus' frosted glass for a camera with a <see cref="GlassBackdrop"/>: once the frame is drawn, the scene is
    /// shrunk to a quarter, blurred and made more colourful into <see cref="GlassBackdrop.Texture"/>.
    /// </summary>
    public sealed class GlassBlurFeature : ScriptableRendererFeature
    {
        private GlassBlurPass pass;
        private Material material;

        public override void Create()
        {
            pass = new GlassBlurPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || !camera.TryGetComponent(out GlassBackdrop backdrop) || !backdrop.isActiveAndEnabled) return;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("MenuGlassBlur");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            pass.Material = material;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class GlassBlurPass : ScriptableRenderPass
        {
            private const float BlurReferencePx = 26, ReferenceHeight = 900, Saturation = 1.5f;
            /// <summary>The shader's passes: a 4-tap shrink, the blur across, the blur down with the saturation.</summary>
            private const int Shrink = 0, Across = 1, Down = 2;
            private static readonly int SigmaId = Shader.PropertyToID("_Sigma"), SaturationId = Shader.PropertyToID("_Saturation");

            public Material Material;
            private RTHandle target;

            public GlassBlurPass()
            {
                profilingSampler = new ProfilingSampler("Menu glass");
                // The blur reads the finished frame, so it must not be drawn straight to the screen.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle source = resources.activeColorTexture;
                TextureDesc frame = renderGraph.GetTextureDesc(source);
                int w = Mathf.Max(1, frame.width / 4), h = Mathf.Max(1, frame.height / 4);

                RenderTexture glass = GlassBackdrop.Sized(w, h);
                if (target == null || target.rt != glass)
                {
                    target?.Release();
                    target = RTHandles.Alloc(glass);
                }

                Material.SetFloat(SigmaId, Mathf.Min(16, BlurReferencePx * frame.height / ReferenceHeight / 4));
                Material.SetFloat(SaturationId, Saturation);
                TextureHandle half = Temporary(renderGraph, "Menu glass half", Mathf.Max(1, frame.width / 2), Mathf.Max(1, frame.height / 2));
                TextureHandle quarter = Temporary(renderGraph, "Menu glass quarter", w, h);
                TextureHandle across = Temporary(renderGraph, "Menu glass across", w, h);
                Blit(renderGraph, source, half, Shrink);
                Blit(renderGraph, half, quarter, Shrink);
                Blit(renderGraph, quarter, across, Across);
                Blit(renderGraph, across, renderGraph.ImportTexture(target), Down);
            }

            public void Dispose()
            {
                target?.Release();
                target = null;
            }

            private void Blit(RenderGraph renderGraph, TextureHandle from, TextureHandle to, int shaderPass) =>
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(from, to, Material, shaderPass), "Menu glass");

            private static TextureHandle Temporary(RenderGraph renderGraph, string name, int w, int h) =>
                renderGraph.CreateTexture(new TextureDesc(w, h)
                {
                    name = name,
                    format = GraphicsFormat.R8G8B8A8_SRGB,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                });
        }
    }
}
