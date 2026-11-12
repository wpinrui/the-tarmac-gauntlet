using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The scene's post-processing, on its global volume: the ACES tone curve with a colour grade the scene sets
    /// (<see cref="Set"/>), bloom at half resolution, motion blur from the camera's own movement, and depth of field for a camera
    /// that asks for it each frame (<see cref="Focus"/>): the broadcast and replay cameras, never a driving view. Each follows its
    /// graphics option. When a camera cuts to another shot, the temporal upscaler forgets the old one and that frame has no blur.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public sealed class PostEffects : MonoBehaviour
    {
        /// <summary>A colour grade. Zero everywhere leaves the picture as the tone curve makes it.</summary>
        public struct Grade
        {
            /// <summary>Stops brighter.</summary>
            public float Exposure;
            /// <summary>-100 to 100.</summary>
            public float Contrast, Saturation;
            /// <summary>-100 (blue) to 100 (yellow), and -100 (green) to 100 (magenta).</summary>
            public float Temperature, Tint;
        }

        /// <summary>The scene's post-processing; null in a scene without it.</summary>
        public static PostEffects Current { get; private set; }

        /// <summary>
        /// The tone curve rolls the brightest light off into the screen and darkens the middle tones a little, stops: this much
        /// brighter keeps the middle tones where the scene was lit for.
        /// </summary>
        private const float ToneExposure = 0.45f;
        private const float BloomThreshold = 1.2f, BloomIntensity = 0.5f, BloomScatter = 0.65f;
        private const float BlurIntensity = 0.35f, BlurClamp = 0.05f;
        /// <summary>A camera that moves further than this in a frame, m, or turns further, degrees, has cut to another shot.</summary>
        private const float CutMove = 8f, CutTurn = 30f;
        /// <summary>
        /// Depth of field: sharp to a little past the subject, then blurring over a stretch as long again as the subject is far,
        /// m. Through a lens narrower than <see cref="LongLens"/> degrees the blur is at its widest.
        /// </summary>
        private const float FocusMargin = 0.15f, FocusPad = 2f, FocusFalloff = 1.5f, FocusFalloffPad = 10f, LongLens = 12f, WideLens = 45f;

        private VolumeProfile profile;
        private Tonemapping tone;
        private ColorAdjustments adjustments;
        private WhiteBalance balance;
        private Bloom bloom;
        private MotionBlur blur;
        private DepthOfField dof;
        /// <summary>Each camera that asked for depth of field: how far its subject is, m, and the frame it asked on.</summary>
        private readonly Dictionary<Camera, (float distance, int frame)> focus = new Dictionary<Camera, (float, int)>();
        private Grade grade;
        private readonly Dictionary<Camera, Pose> seen = new Dictionary<Camera, Pose>();
        private readonly List<Camera> gone = new List<Camera>();

        private void Awake()
        {
            Current = this;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Post-processing";
            tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            adjustments = profile.Add<ColorAdjustments>();
            balance = profile.Add<WhiteBalance>();
            bloom = profile.Add<Bloom>();
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.downscale.Override(BloomDownscaleMode.Half);
            bloom.highQualityFiltering.Override(false);
            blur = profile.Add<MotionBlur>();
            blur.mode.Override(MotionBlurMode.CameraOnly);
            blur.quality.Override(MotionBlurQuality.Low);
            blur.intensity.Override(BlurIntensity);
            blur.clamp.Override(BlurClamp);
            dof = profile.Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.active = false;

            Volume volume = GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            volume.enabled = true;

            GraphicsOptions.ColourGrading.Changed += Show;
            GraphicsOptions.Bloom.Changed += Show;
            GraphicsOptions.MotionBlur.Changed += Show;
            RenderPipelineManager.beginCameraRendering += Watch;
            Set(grade);
            Show(null);
        }

        private void OnDestroy()
        {
            GraphicsOptions.ColourGrading.Changed -= Show;
            GraphicsOptions.Bloom.Changed -= Show;
            GraphicsOptions.MotionBlur.Changed -= Show;
            RenderPipelineManager.beginCameraRendering -= Watch;
            if (Current == this) Current = null;
            Destroy(profile);
        }

        /// <summary>
        /// Asks for depth of field on this camera this frame, focused on a subject this far away, m. Call it every frame the camera
        /// should have it, after placing the camera: a camera that stops asking is sharp again.
        /// </summary>
        public static void Focus(Camera camera, float distance)
        {
            if (Current == null || camera == null) return;
            Current.focus[camera] = (Mathf.Max(distance, 0.5f), Time.frameCount);
        }

        /// <summary>Grades the picture from now on.</summary>
        public void Set(Grade g)
        {
            grade = g;
            adjustments.postExposure.Override(ToneExposure + g.Exposure);
            adjustments.contrast.Override(Mathf.Clamp(g.Contrast, -100f, 100f));
            adjustments.saturation.Override(Mathf.Clamp(g.Saturation, -100f, 100f));
            balance.temperature.Override(Mathf.Clamp(g.Temperature, -100f, 100f));
            balance.tint.Override(Mathf.Clamp(g.Tint, -100f, 100f));
        }

        private void Show(GraphicsOption _)
        {
            bool grading = GraphicsOptions.ColourGrading.On;
            tone.active = grading;
            adjustments.active = grading;
            balance.active = grading;
            bloom.active = GraphicsOptions.Bloom.On;
            blur.active = GraphicsOptions.MotionBlur.On;
        }

        /// <summary>Before each camera draws: a cut clears the upscaler's history and draws the frame without blur.</summary>
        private void Watch(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            var now = new Pose(camera.transform.position, camera.transform.rotation);
            bool cut = seen.TryGetValue(camera, out Pose before)
                && ((now.position - before.position).sqrMagnitude > CutMove * CutMove || Quaternion.Angle(now.rotation, before.rotation) > CutTurn);
            seen[camera] = now;
            if (cut) camera.GetUniversalAdditionalCameraData().resetHistory = true;
            blur.intensity.Override(cut ? 0f : BlurIntensity);
            Depth(camera);
            if (seen.Count > 8) Forget();
        }

        /// <summary>Depth of field for this camera if it asked this frame and the option is on, none otherwise.</summary>
        private void Depth(Camera camera)
        {
            bool on = GraphicsOptions.DepthOfField.On && focus.TryGetValue(camera, out var f) && f.frame == Time.frameCount;
            dof.active = on;
            if (!on) return;
            float d = focus[camera].distance;
            float start = d * (1f + FocusMargin) + FocusPad;
            dof.gaussianStart.Override(start);
            dof.gaussianEnd.Override(start + d * FocusFalloff + FocusFalloffPad);
            float lens = Mathf.InverseLerp(WideLens, LongLens, camera.fieldOfView);
            dof.gaussianMaxRadius.Override(Mathf.Lerp(0.6f, 1.5f, lens));
            dof.highQualitySampling.Override(QualityLevels.Level >= 2);
        }

        private void Forget()
        {
            gone.Clear();
            foreach (Camera c in seen.Keys) if (c == null) gone.Add(c);
            foreach (Camera c in gone) seen.Remove(c);
            gone.Clear();
            foreach (Camera c in focus.Keys) if (c == null) gone.Add(c);
            foreach (Camera c in gone) focus.Remove(c);
        }
    }
}
