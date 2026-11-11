using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The sky, the sun and the light they give, following the race clock from the start time through
    /// the 24 hours (<see cref="SkyClock"/>). One directional light is the sun by day and the moon at
    /// night. The ambient light and the fog take the sky's colours. The clouds drift with the wind,
    /// faster when the race runs faster and the wind blows. The weather (<see cref="WeatherSky"/>)
    /// spreads cloud over the sky, greys it and takes the sun's light and shadows. A sky-only
    /// reflection probe is redrawn as the sky changes, so shiny paint and a wet track reflect the sky
    /// they are under.
    /// </summary>
    public sealed class TimeOfDay : MonoBehaviour
    {
        /// <summary>Local time when the race starts, hours.</summary>
        public const float StartHour = 15f;
        /// <summary>Race clock seconds since the start; zero until it is set.</summary>
        [NonSerialized] public Func<float> RaceSeconds;
        /// <summary>The weather now; the clear sky of the dry race until it is set.</summary>
        [NonSerialized] public Func<WeatherState> Weather;

        /// <summary>Cloud drift, texture tiles a second, for the big and the small cloud layers.</summary>
        private static readonly Vector4 Wind = new Vector4(0.0035f, 0.0012f, 0.0052f, 0.0021f);
        private const float CloudScale = 0.45f;
        /// <summary>Redraw the reflections once the clock has moved this far, hours.</summary>
        private const float ReflectionStep = 0.05f;
        /// <summary>Redraw the reflections once the cloud has changed this much.</summary>
        private const float ReflectionCloud = 0.04f;
        /// <summary>
        /// How bright the sky's reflection is at night. The floodlights' pools multiply everything under
        /// them, the reflection too, so a wet track reflecting the glowing night sky would shine white.
        /// </summary>
        private const float NightShine = 0.2f;

        private Light lamp;
        private Material sky;
        private ReflectionProbe probe;
        private Vector4 clouds;
        private float reflected = float.NaN, reflectedCloud;

        private static readonly int ZenithId = Shader.PropertyToID("_Zenith"), HorizonId = Shader.PropertyToID("_Horizon"),
            GlowId = Shader.PropertyToID("_Glow"), GroundId = Shader.PropertyToID("_Ground"), SunColorId = Shader.PropertyToID("_SunColor"),
            CloudLitId = Shader.PropertyToID("_CloudLit"), CloudShadeId = Shader.PropertyToID("_CloudShade"),
            SunDirId = Shader.PropertyToID("_SunDir"), MoonDirId = Shader.PropertyToID("_MoonDir"),
            CloudOffsetId = Shader.PropertyToID("_CloudOffset"), CoverId = Shader.PropertyToID("_Cover"), OvercastId = Shader.PropertyToID("_Overcast"),
            NightId = Shader.PropertyToID("_Night"), CloudScaleId = Shader.PropertyToID("_CloudScale");

        /// <summary>The hour of the day now, on the race clock.</summary>
        public float Hour => StartHour + (RaceSeconds != null ? RaceSeconds() : 0f) / 3600f;

        private void Awake()
        {
            lamp = gameObject.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.shadows = LightShadows.Soft;
            RenderSettings.sun = lamp;

            sky = new Material(Resources.Load<Shader>("Sky/Sky")) { name = "Sky" };
            sky.SetTexture("_CloudTex", CloudNoise());
            sky.SetFloat(CloudScaleId, CloudScale);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            QualitySettings.shadowDistance = 400f;

            var reflections = new GameObject("Sky reflections");
            reflections.transform.SetParent(transform, false);
            probe = reflections.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0;
            probe.resolution = 64;
            probe.hdr = false;
            probe.size = new Vector3(40000f, 8000f, 40000f);
            Apply();
        }

        private void Update()
        {
            clouds += Wind * (Time.deltaTime * (Weather != null ? Weather().Wind : 1f));
            for (int k = 0; k < 4; k++) clouds[k] = Mathf.Repeat(clouds[k], 1f);
            Apply();
        }

        private void Apply()
        {
            float hour = Hour;
            SkyState s = SkyClock.At(hour, SkyClock.Look);
            float cover = SkyClock.Look.Cover, overcast = 0f, haze = SkyClock.Look.Haze, shadows = 1f, cloud = 0f;
            if (Weather != null)
            {
                WeatherState w = Weather();
                WeatherSky.Apply(ref s, w);
                cover = WeatherSky.Cover(w.Cloud);
                overcast = WeatherSky.Overcast(w.Cloud);
                haze = WeatherSky.Haze(haze, w);
                // Under a grey sky the light comes from everywhere and the shadows fade.
                shadows = Mathf.Lerp(0.15f, 1f, WeatherSky.Direct(w));
                cloud = w.Cloud + WeatherSky.Heavy(w.Rain);
            }
            lamp.shadowStrength = shadows;
            lamp.transform.rotation = Quaternion.LookRotation(-s.LightDir);
            lamp.color = s.LightColour;
            lamp.intensity = s.LightIntensity;
            lamp.enabled = s.LightIntensity > 0.001f;

            RenderSettings.ambientSkyColor = s.AmbientSky;
            RenderSettings.ambientEquatorColor = s.AmbientEquator;
            RenderSettings.ambientGroundColor = s.AmbientGround;
            RenderSettings.fogColor = s.Fog;
            RenderSettings.fogStartDistance = haze * 0.2f;
            RenderSettings.fogEndDistance = haze;

            sky.SetColor(ZenithId, s.Zenith);
            sky.SetColor(HorizonId, s.Horizon);
            sky.SetColor(GlowId, s.Glow);
            sky.SetColor(GroundId, s.AmbientGround);
            sky.SetColor(SunColorId, s.SunElevation > -2f ? s.LightColour : Color.black);
            sky.SetColor(CloudLitId, s.CloudLit);
            sky.SetColor(CloudShadeId, s.CloudShade);
            sky.SetVector(SunDirId, s.SunDir);
            sky.SetVector(MoonDirId, s.MoonDir);
            sky.SetVector(CloudOffsetId, clouds);
            sky.SetFloat(CoverId, cover);
            sky.SetFloat(OvercastId, overcast);
            sky.SetFloat(NightId, s.Night);

            probe.intensity = Mathf.Lerp(1f, NightShine, s.Night);
            if (float.IsNaN(reflected) || Mathf.Abs(hour - reflected) >= ReflectionStep || Mathf.Abs(cloud - reflectedCloud) >= ReflectionCloud)
            {
                reflected = hour;
                reflectedCloud = cloud;
                probe.RenderProbe();
            }
        }

        /// <summary>
        /// Tileable cloud noise: red is big soft masses, green finer detail. Each is a few octaves of
        /// value noise on a lattice that wraps, so the texture repeats without a seam.
        /// </summary>
        private static Texture2D CloudNoise()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Cloud noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float[] big = CloudField.Fbm(size, 4, 5, 11), small = CloudField.Fbm(size, 8, 4, 29);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(big[i] * 255f), (byte)(small[i] * 255f), 0, 255);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
