using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The scene's post-processing, on its global volume: the ACES tone curve with a colour grade the scene sets
    /// (<see cref="Set"/>), bloom at half resolution and motion blur from the camera's own movement. Each follows its graphics
    /// option. When a camera cuts to another shot, the temporal upscaler forgets the old one and that frame has no blur.
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

        private VolumeProfile profile;
        private Tonemapping tone;
        private ColorAdjustments adjustments;
        private WhiteBalance balance;
        private Bloom bloom;
        private MotionBlur blur;
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
            if (seen.Count > 8) Forget();
        }

        private void Forget()
        {
            gone.Clear();
            foreach (Camera c in seen.Keys) if (c == null) gone.Add(c);
            foreach (Camera c in gone) seen.Remove(c);
        }
    }
}
