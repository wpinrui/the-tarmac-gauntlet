using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The race's volumetric clouds and fog, drawn once the sky is: real clouds in place of the sky shader's painted ones,
    /// lit towards the sun and shaped by the same cloud map as their shadows (<see cref="CloudShadows"/>), and a low mist
    /// that the sun shines through in shafts between the trees and the floodlights light in beams at night. Each follows
    /// its graphics option. Both are worked out at a third of the resolution across and down at High and a half at Ultra,
    /// skipping the 3D noise wherever the cloud map is clear, then smoothed over frames and laid
    /// over the picture by depth (the Volumetrics shader). Only the race's own cameras draw them, and only while a race sky
    /// is published (<see cref="Atmosphere"/>): reflections keep the painted clouds.
    /// </summary>
    public sealed class VolumetricsFeature : ScriptableRendererFeature
    {
        /// <summary>1 while the camera drawing now has the volumetric clouds, so the sky shader leaves its own out.</summary>
        public static readonly int VolumetricCloudsId = Shader.PropertyToID("_TgVolumetricClouds");

        private VolumetricsPass pass;
        private Material march, resolve, composite;

        public override void Create()
        {
            pass = new VolumetricsPass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            // The clouds wait for their noise, worked out the first time they are wanted.
            bool clouds = GraphicsOptions.VolumetricClouds.On && CloudNoise.Texture != null, fog = GraphicsOptions.VolumetricFog.On;
            bool draw = Atmosphere.Present && (clouds || fog) && camera != null && renderingData.cameraData.cameraType == CameraType.Game
                && camera.targetTexture == null && Materials();
            Shader.SetGlobalFloat(VolumetricCloudsId, draw && clouds ? 1f : 0f);
            if (!draw) return;
            pass.Set(march, resolve, composite, clouds, fog);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        private bool Materials()
        {
            if (march != null) return true;
            Shader shader = Resources.Load<Shader>("Atmosphere/Volumetrics");
            if (shader == null) return false;
            march = CoreUtils.CreateEngineMaterial(shader);
            resolve = CoreUtils.CreateEngineMaterial(shader);
            composite = CoreUtils.CreateEngineMaterial(shader);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            Shader.SetGlobalFloat(VolumetricCloudsId, 0f);
            CoreUtils.Destroy(march);
            CoreUtils.Destroy(resolve);
            CoreUtils.Destroy(composite);
            march = resolve = composite = null;
        }

        private sealed class VolumetricsPass : ScriptableRenderPass
        {
            private const int March = 0, Resolve = 1, Composite = 2;
            /// <summary>The clouds' extinction at full thickness, per m: dense enough that their edges read crisp.</summary>
            private const float CloudExtinction = 0.05f;
            /// <summary>
            /// The march is worked out at the frame's size over this, across and down: a third at High, a half at Ultra, so the
            /// clouds' edges stay crisp. At 2560 x 1440 High marches 853 x 480 and Ultra 1280 x 720 with more steps.
            /// </summary>
            private const int HighDivisor = 3, UltraDivisor = 2;
            /// <summary>How far the mist is marched, m: past it the scene's haze carries on.</summary>
            private const float FogReach = 700f;
            /// <summary>A camera that moves further than this in a frame, m, or turns further, degrees, has cut to another shot.</summary>
            private const float CutMove = 8f, CutTurn = 30f;

            private static readonly int DepthId = Shader.PropertyToID("_TgDepth"), HistoryId = Shader.PropertyToID("_TgHistory"),
                NoiseId = Shader.PropertyToID("_TgCloudNoise3D"), InvViewProjId = Shader.PropertyToID("_TgInvViewProj"),
                PrevViewProjId = Shader.PropertyToID("_TgPrevViewProj"), CamPosId = Shader.PropertyToID("_TgCamPos"),
                LowSizeId = Shader.PropertyToID("_TgLowSize"), StepsId = Shader.PropertyToID("_TgSteps"),
                SlabId = Shader.PropertyToID("_TgCloudSlab");

            /// <summary>Each camera's two frames of smoothed volumetrics, the one written last, and where the camera was.</summary>
            private sealed class History
            {
                public readonly RTHandle[] Frames = new RTHandle[2];
                public int Last;
                public bool Valid;
                public Matrix4x4 ViewProj;
                public Pose Pose;

                public void Release()
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Frames[i]?.Release();
                        Frames[i] = null;
                    }
                    Valid = false;
                }
            }

            private sealed class PassData
            {
                public Material Material;
                public TextureHandle Depth, Source, History;
                public Matrix4x4 InvViewProj, PrevViewProj;
                public Vector4 CamPos, LowSize, Steps, Slab;
            }

            private readonly Dictionary<Camera, History> histories = new Dictionary<Camera, History>();
            private readonly List<Camera> gone = new List<Camera>();
            private Material march, resolve, composite;
            private bool clouds, fog;

            public VolumetricsPass()
            {
                profilingSampler = new ProfilingSampler("Volumetrics");
                requiresIntermediateTexture = true;
            }

            public void Set(Material marchMaterial, Material resolveMaterial, Material compositeMaterial, bool drawClouds, bool drawFog)
            {
                march = marchMaterial;
                resolve = resolveMaterial;
                composite = compositeMaterial;
                clouds = drawClouds;
                fog = drawFog;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle color = resources.activeColorTexture, depth = resources.cameraDepthTexture;
                if (!color.IsValid() || !depth.IsValid()) return;
                Camera camera = cameraData.camera;
                TextureDesc frame = renderGraph.GetTextureDesc(color);
                bool ultra = QualityLevels.Level >= 3;
                int divisor = ultra ? UltraDivisor : HighDivisor;
                int w = Mathf.Max(1, frame.width / divisor), h = Mathf.Max(1, frame.height / divisor);
                History history = HistoryFor(camera, w, h);

                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
                var pose = new Pose(camera.transform.position, camera.transform.rotation);
                bool cut = (pose.position - history.Pose.position).sqrMagnitude > CutMove * CutMove
                    || Quaternion.Angle(pose.rotation, history.Pose.rotation) > CutTurn;
                bool blend = history.Valid && !cut;
                Matrix4x4 before = blend ? history.ViewProj : viewProj;
                history.ViewProj = viewProj;
                history.Pose = pose;

                Atmosphere.Sky sky = Atmosphere.Now;
                float cloudBase = Atmosphere.CloudBase - 300f * sky.Overcast;
                var steps = new Vector4(clouds ? (ultra ? 48 : 32) : 0, fog ? (ultra ? 24 : 16) : 0, FogReach, blend ? 1f : 0f);
                var slab = new Vector4(cloudBase, cloudBase + Mathf.Lerp(900f, 1500f, sky.Overcast), CloudExtinction, clouds ? 1f : 0f);
                var camPos = new Vector4(pose.position.x, pose.position.y, pose.position.z, Time.frameCount & 63);
                var lowSize = new Vector4(1f / w, 1f / h, w, h);
                Matrix4x4 invViewProj = viewProj.inverse;

                TextureHandle marched = renderGraph.CreateTexture(new TextureDesc(w, h)
                {
                    name = "Volumetrics march",
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                });
                TextureHandle last = renderGraph.ImportTexture(history.Frames[history.Last]);
                history.Last ^= 1;
                TextureHandle now = renderGraph.ImportTexture(history.Frames[history.Last]);
                history.Valid = true;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetrics march", out PassData data, profilingSampler))
                {
                    Fill(data, march, depth, TextureHandle.nullHandle, TextureHandle.nullHandle, invViewProj, before, camPos, lowSize, steps, slab);
                    builder.UseTexture(depth);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture);
                    builder.SetRenderAttachment(marched, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        Apply(d);
                        Texture3D noise = CloudNoise.Texture;
                        if (noise != null) d.Material.SetTexture(NoiseId, noise);
                        Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, March);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetrics resolve", out PassData data, profilingSampler))
                {
                    Fill(data, resolve, depth, marched, last, invViewProj, before, camPos, lowSize, steps, slab);
                    builder.UseTexture(depth);
                    builder.UseTexture(marched);
                    builder.UseTexture(last);
                    builder.SetRenderAttachment(now, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        Apply(d);
                        d.Material.SetTexture(HistoryId, (Texture)d.History);
                        Blitter.BlitTexture(context.cmd, (RTHandle)d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, Resolve);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volumetrics composite", out PassData data, profilingSampler))
                {
                    Fill(data, composite, depth, now, TextureHandle.nullHandle, invViewProj, before, camPos, lowSize, steps, slab);
                    builder.UseTexture(depth);
                    builder.UseTexture(now);
                    builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        Apply(d);
                        Blitter.BlitTexture(context.cmd, (RTHandle)d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, Composite);
                    });
                }

                if (histories.Count > 4) Forget();
            }

            private static void Fill(PassData d, Material material, TextureHandle depth, TextureHandle source, TextureHandle history,
                Matrix4x4 invViewProj, Matrix4x4 prevViewProj, Vector4 camPos, Vector4 lowSize, Vector4 steps, Vector4 slab)
            {
                d.Material = material;
                d.Depth = depth;
                d.Source = source;
                d.History = history;
                d.InvViewProj = invViewProj;
                d.PrevViewProj = prevViewProj;
                d.CamPos = camPos;
                d.LowSize = lowSize;
                d.Steps = steps;
                d.Slab = slab;
            }

            private static void Apply(PassData d)
            {
                Material m = d.Material;
                m.SetTexture(DepthId, (Texture)d.Depth);
                m.SetMatrix(InvViewProjId, d.InvViewProj);
                m.SetMatrix(PrevViewProjId, d.PrevViewProj);
                m.SetVector(CamPosId, d.CamPos);
                m.SetVector(LowSizeId, d.LowSize);
                m.SetVector(StepsId, d.Steps);
                m.SetVector(SlabId, d.Slab);
            }

            /// <summary>The camera's history, made or remade at this size.</summary>
            private History HistoryFor(Camera camera, int w, int h)
            {
                if (!histories.TryGetValue(camera, out History history)) histories[camera] = history = new History();
                RTHandle first = history.Frames[0];
                if (first == null || first.rt == null || first.rt.width != w || first.rt.height != h)
                {
                    history.Release();
                    for (int i = 0; i < 2; i++)
                        history.Frames[i] = RTHandles.Alloc(w, h, GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Bilinear,
                            wrapMode: TextureWrapMode.Clamp, name: "Volumetrics history " + i);
                }
                return history;
            }

            private void Forget()
            {
                gone.Clear();
                foreach (KeyValuePair<Camera, History> h in histories)
                    if (h.Key == null) gone.Add(h.Key);
                foreach (Camera c in gone)
                {
                    histories[c].Release();
                    histories.Remove(c);
                }
            }

            public void Dispose()
            {
                foreach (History h in histories.Values) h.Release();
                histories.Clear();
            }
        }
    }
}
