using System;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The sky, the sun and the light they give, following the race clock from the start time through
    /// the 24 hours (<see cref="SkyClock"/>). One directional light is the sun by day and the moon at
    /// night. The ambient light and the haze take the sky's colours: the haze is exponential fog, so
    /// the far trees fade layer by layer into the air, thicker in the morning mist and under weather. The clouds drift with the wind,
    /// faster when the race runs faster and the wind blows. The weather (<see cref="WeatherSky"/>)
    /// spreads cloud over the sky, greys it and takes the sun's light and shadows. A sky-only
    /// reflection probe is redrawn as the sky changes, so shiny paint and a wet track reflect the sky
    /// they are under. The Sky detail graphics option picks the sky shader's fuller path. The clouds' shadows drift over the
    /// ground with them (<see cref="CloudShadows"/>), and the sky is published for the volumetric clouds, the mist and the
    /// sun's rays (<see cref="Atmosphere"/>). The colour grade follows the sun breaking through onto the camera's spot.
    /// </summary>
    public sealed class TimeOfDay : MonoBehaviour
    {
        /// <summary>Local time when the race starts, hours.</summary>
        public static float StartHour = DefaultStartHour;
        public const float DefaultStartHour = 15f;
        /// <summary>Race clock seconds since the start; zero until it is set.</summary>
        [NonSerialized] public Func<float> RaceSeconds;
        /// <summary>The weather now; the clear sky of the dry race until it is set.</summary>
        [NonSerialized] public Func<WeatherState> Weather;
        /// <summary>Height of the circuit's ground, m: the mist lies on it.</summary>
        [NonSerialized] public float Ground;

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
        /// <summary>The sun's light is matched for Linear colour space on a surface turned at least this far towards it (cosine).</summary>
        private const float MatchFacing = 0.3f;
        /// <summary>
        /// The low mist's density at the ground, per m: a trace on a clear afternoon, thick in the morning mist and the rain,
        /// and a little damp at night for the floodlights' beams. And how fast it thins with height, m.
        /// </summary>
        private const float MistClear = 0.0004f, MistMorning = 0.003f, MistRain = 0.0025f, MistNight = 0.0008f, MistHeight = 40f, MistRises = 60f;
        /// <summary>Real seconds the colour grade takes to follow the sun in and out of a cloud's shadow.</summary>
        private const float ShadeEase = 0.8f;

        private Light lamp;
        private Material sky;
        private ReflectionProbe probe;
        private Vector4 clouds;
        private float reflected = float.NaN, reflectedCloud;
        private const int CloudSize = 256;
        private float[] bigCloud, smallCloud;
        private Texture2D cloudNoise;
        private CloudShadows cloudShadows;
        /// <summary>How far the wind has carried the clouds, m, east and north, wrapped at the volumetric noise's period.</summary>
        private Vector2 drift;
        /// <summary>How deep in a cloud's shadow the camera's spot is, eased (<see cref="ShadeEase"/>); NaN until the first frame.</summary>
        private float shade = float.NaN;

        private static readonly int ZenithId = Shader.PropertyToID("_Zenith"), HorizonId = Shader.PropertyToID("_Horizon"),
            GlowId = Shader.PropertyToID("_Glow"), GroundId = Shader.PropertyToID("_Ground"), SunColorId = Shader.PropertyToID("_SunColor"), HazeId = Shader.PropertyToID("_Haze"),
            CloudLitId = Shader.PropertyToID("_CloudLit"), CloudShadeId = Shader.PropertyToID("_CloudShade"),
            SunDirId = Shader.PropertyToID("_SunDir"), MoonDirId = Shader.PropertyToID("_MoonDir"),
            CloudOffsetId = Shader.PropertyToID("_CloudOffset"), CoverId = Shader.PropertyToID("_Cover"), OvercastId = Shader.PropertyToID("_Overcast"),
            NightId = Shader.PropertyToID("_Night"), CloudScaleId = Shader.PropertyToID("_CloudScale");

        /// <summary>
        /// How far the circuit's lights are on, 0 to 1 (<see cref="SkyState.Lights"/>): follows the dusk and the weather, and
        /// moves no faster than <see cref="LightsRate"/> a second so a sudden change of weather fades rather than flicks.
        /// </summary>
        public float Lights { get; private set; }
        private const float LightsRate = 0.25f;
        private bool lightsSet;
        /// <summary>
        /// The weather's settled dusk for the lights (<see cref="WeatherSky.Settle"/>), the race clock it was settled at and
        /// the lights' darkness held under it (<see cref="WeatherSky.Hold"/>).
        /// </summary>
        private float dusk, duskAt, held;

        /// <summary>The hour of the day now, on the race clock.</summary>
        public float Hour => StartHour + (RaceSeconds != null ? RaceSeconds() : 0f) / 3600f;

        private void Awake()
        {
            lamp = gameObject.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.shadows = LightShadows.Soft;
            RenderSettings.sun = lamp;

            sky = new Material(Resources.Load<Shader>("Sky/Sky")) { name = "Sky" };
            cloudNoise = CloudTexture();
            sky.SetTexture("_CloudTex", cloudNoise);
            sky.SetFloat(CloudScaleId, CloudScale);
            ShowDetail(GraphicsOptions.SkyDetail);
            GraphicsOptions.SkyDetail.Changed += ShowDetail;
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;

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
            cloudShadows = new CloudShadows(lamp);
            if (GraphicsOptions.VolumetricClouds.On) CloudNoise.Prepare();
            Apply();
        }

        private void OnDestroy()
        {
            GraphicsOptions.SkyDetail.Changed -= ShowDetail;
            Atmosphere.Clear();
            cloudShadows?.Dispose();
        }

        /// <summary>Switches the sky shader's detailed path, and redraws the reflections with it.</summary>
        private void ShowDetail(GraphicsOption option)
        {
            if (option.On) sky.EnableKeyword("_SKY_DETAIL");
            else sky.DisableKeyword("_SKY_DETAIL");
            reflected = float.NaN;
        }

        private void Update()
        {
            float o = Weather != null ? WeatherSky.Overcast(Weather().Cloud) : 0f;
            Vector4 blown = Wind * (Time.deltaTime * (Weather != null ? Weather().Wind : 1f));
            clouds += blown;
            for (int k = 0; k < 4; k++) clouds[k] = Mathf.Repeat(clouds[k], 1f);
            // The map's clouds move the other way to its offset; the volumetric noise follows them.
            float tiles = CloudMap.TilesFor(CloudScale, o);
            drift.x = Mathf.Repeat(drift.x + blown.x / tiles, Atmosphere.NoisePeriod);
            drift.y = Mathf.Repeat(drift.y + blown.y / tiles, Atmosphere.NoisePeriod);
            Apply();
        }

        private void Apply()
        {
            float hour = Hour;
            SkyState s = SkyClock.At(hour, SkyClock.Look);
            float cover = SkyClock.Look.Cover, overcast = 0f, haze = s.Haze, shadows = 1f, cloud = 0f, rain = 0f, share = 0.3f;
            if (Weather != null)
            {
                WeatherState w = Weather();
                float now = RaceSeconds != null ? RaceSeconds() : 0f;
                dusk = lightsSet ? WeatherSky.Settle(dusk, w, now - duskAt) : WeatherSky.Dusk(w);
                duskAt = now;
                float lights = WeatherSky.Lights(s.SunElevation, dusk);
                held = lightsSet ? WeatherSky.Hold(held, lights, dusk) : lights;
                WeatherSky.Apply(ref s, w, held);
                cover = WeatherSky.Cover(w.Cloud);
                overcast = WeatherSky.Overcast(w.Cloud);
                haze = WeatherSky.Haze(haze, w);
                // Under a grey sky the light comes from everywhere and the shadows fade.
                shadows = Mathf.Lerp(0.15f, 1f, WeatherSky.Direct(w));
                rain = WeatherSky.Heavy(w.Rain);
                cloud = w.Cloud + rain;
                share = w.Cloud;
            }
            var map = new CloudMap
            {
                Big = bigCloud,
                Small = smallCloud,
                Size = CloudSize,
                Tiles = CloudMap.TilesFor(CloudScale, overcast),
                Cover = CloudMap.CoverFor(share),
                Overcast = overcast,
                Offset = clouds,
            };
            Camera eye = Camera.main;
            Vector3 spot = eye != null ? eye.transform.position : Vector3.zero;
            float shadeNow = map.Shading(spot, s.LightDir) * Mathf.Clamp01(CloudMap.Strength(overcast) / 0.85f);
            shade = float.IsNaN(shade) ? shadeNow : Mathf.Lerp(shade, shadeNow, 1f - Mathf.Exp(-Time.unscaledDeltaTime / ShadeEase));
            // The first sky with its weather sets the lights outright; after that they ease towards it.
            Lights = lightsSet ? Mathf.MoveTowards(Lights, s.Lights, LightsRate * Time.unscaledDeltaTime) : s.Lights;
            lightsSet |= Weather != null;
            PostEffects.Current?.Set(SkyGrade.For(s, overcast, rain, shade, SkyGrade.Patchy(share, overcast)));
            lamp.shadowStrength = shadows;
            lamp.transform.rotation = Quaternion.LookRotation(-s.LightDir);
            lamp.color = s.LightColour;
            // The sky's light levels were set in Gamma colour space: matched for Linear on the level ground, or on a
            // surface turned towards a low sun.
            lamp.intensity = LinearLight.Match(s.LightColour, s.LightIntensity, s.AmbientSky, Mathf.Max(s.LightDir.y, MatchFacing));
            lamp.enabled = s.LightIntensity > 0.001f;

            RenderSettings.ambientSkyColor = s.AmbientSky;
            RenderSettings.ambientEquatorColor = s.AmbientEquator;
            RenderSettings.ambientGroundColor = s.AmbientGround;
            RenderSettings.fogColor = s.Fog;
            RenderSettings.fogDensity = SkyClock.Density(haze);

            sky.SetColor(ZenithId, s.Zenith);
            sky.SetColor(HorizonId, s.Horizon);
            sky.SetColor(GlowId, s.Glow);
            sky.SetColor(GroundId, s.AmbientGround);
            sky.SetColor(HazeId, s.Fog);
            sky.SetColor(SunColorId, s.SunElevation > -2f ? s.LightColour : Color.black);
            sky.SetColor(CloudLitId, s.CloudLit);
            sky.SetColor(CloudShadeId, s.CloudShade);
            sky.SetVector(SunDirId, s.SunDir);
            sky.SetVector(MoonDirId, s.MoonDir);
            sky.SetVector(CloudOffsetId, clouds);
            sky.SetFloat(CoverId, cover);
            sky.SetFloat(OvercastId, overcast);
            sky.SetFloat(NightId, s.Night);

            float misty = SkyClock.Misty(hour);
            Atmosphere.Publish(new Atmosphere.Sky
            {
                SunDir = s.LightDir,
                Sun = lamp.enabled ? s.LightColour * s.LightIntensity : Color.black,
                CloudLit = s.CloudLit,
                CloudShade = s.CloudShade,
                Haze = s.Fog,
                HazeDensity = RenderSettings.fogDensity,
                CloudTex = cloudNoise,
                CloudTiles = map.Tiles,
                Cover = map.Cover,
                Overcast = overcast,
                ShadowStrength = CloudMap.Strength(overcast),
                CloudOffset = clouds,
                Drift = drift,
                MistDensity = MistClear + MistMorning * misty + MistRain * rain + MistNight * Lights,
                MistHeight = MistHeight + MistRises * misty,
                Ground = Ground,
            });
            cloudShadows?.Draw(spot);

            probe.intensity = Mathf.Lerp(1f, NightShine, Lights);
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
        private Texture2D CloudTexture()
        {
            const int size = CloudSize;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Cloud noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float[] big = bigCloud = CloudField.Fbm(size, 4, 5, 11), small = smallCloud = CloudField.Fbm(size, 8, 4, 29);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(big[i] * 255f), (byte)(small[i] * 255f), 0, 255);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
