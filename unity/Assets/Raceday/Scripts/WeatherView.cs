using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Plays the race's weather (<see cref="WeatherPlan"/>) on the race clock: it sets
    /// <see cref="RaceWeather.Now"/> for the tyres, wets the track, drops rain round the camera and
    /// throws spray and mist up behind the cars nearest it. The sky does its own part
    /// (<see cref="TimeOfDay.Weather"/>). The paved surfaces are wet in the track shader: it reads the
    /// water's depth and the rain from a few globals set here, darkens and glosses the tarmac (its
    /// reflections are the scene's probes), clears a drier line where the cars run, and stands
    /// puddles rippled by the rain in the road's dips. The water follows the weather's own film,
    /// which builds in the rain and dries after it. Every other surface darkens and shines by its
    /// shared material. Cheap by design: the rain is one particle system that follows the camera,
    /// the spray and mist a few small ones handed between the nearest cars, a fixed number for the
    /// quality level, each with a cap on its particles. Each part has its graphics option.
    /// </summary>
    public sealed class WeatherView : MonoBehaviour
    {
        [NonSerialized] public WeatherPlan Plan;
        [NonSerialized] public Func<float> RaceSeconds;
        [NonSerialized] public CircuitMaterials Materials;
        [NonSerialized] public RaceCar[] Cars;
        /// <summary>How far the circuit's lights are on, 0 to 1 (<see cref="TimeOfDay.Lights"/>): rain and spray catch the floodlights.</summary>
        [NonSerialized] public Func<float> Night;

        /// <summary>The weather now.</summary>
        public WeatherState Now { get; private set; } = WeatherState.Dry;

        /// <summary>How long a raindrop falls in sight, s, and how fast, m/s.</summary>
        private const float DropLife = 1.2f, DropSpeed = 8.5f;
        /// <summary>Most drops in the air round the camera at once.</summary>
        private const int MostDrops = 8000;
        /// <summary>Side of the square of rain round the camera, m.</summary>
        private const float RainBox = 36f;
        /// <summary>Cars that throw spray at once at each quality level, the nearest to the camera within this range, m.</summary>
        private static readonly int[] SpraysAt = { 6, 12, 16, 24 };
        private const float SprayRange = 250f;
        /// <summary>Most particles in one car's spray, and in its mist.</summary>
        private const int MostSpray = 80, MostMist = 20;
        /// <summary>Real seconds the drawn water takes to catch up with the weather's, so a sudden change washes in.</summary>
        private const float WaterLag = 1.5f;
        /// <summary>Real seconds between choosing which cars throw spray.</summary>
        private const float PickEvery = 0.3f;

        private ParticleSystem rain;
        private Material rainMaterial, sprayMaterial;
        private Vector3 lastCamera;
        private bool haveCamera;
        private Material mistMaterial;
        private Plume[] sprays;
        private float picked = -1f;
        private readonly List<Wettable> surfaces = new List<Wettable>();
        private float shown = -1f, water = -1f;
        private bool shaded;
        private static readonly int TintId = Shader.PropertyToID("_Tint"), ShapeId = Shader.PropertyToID("_Shape"),
            GlossId = Shader.PropertyToID("_Smoothness"), WaterId = Shader.PropertyToID("_TrackWater"),
            RainId = Shader.PropertyToID("_TrackRain"), LineDryId = Shader.PropertyToID("_TrackLineDry"),
            PuddlesOnId = Shader.PropertyToID("_TrackPuddlesOn"), RipplesOnId = Shader.PropertyToID("_TrackRipplesOn"),
            RippleTexId = Shader.PropertyToID("_TrackRippleTex"), RippleTilesId = Shader.PropertyToID("_TrackRippleTiles");
        private static GraphicsOption[] TrackOptions => new[] { GraphicsOptions.WetTrack, GraphicsOptions.Puddles, GraphicsOptions.RainRipples };

        private void Start()
        {
            Shader drops = Resources.Load<Shader>("Weather/Drops");
            rainMaterial = new Material(drops) { name = "Rain" };
            rainMaterial.SetFloat(ShapeId, 0f);
            sprayMaterial = new Material(drops) { name = "Spray" };
            sprayMaterial.SetFloat(ShapeId, 1f);
            mistMaterial = new Material(drops) { name = "Mist" };
            mistMaterial.SetFloat(ShapeId, 1f);
            rain = MakeRain(rainMaterial);
            sprays = new Plume[SpraysAt[Mathf.Clamp(QualityLevels.Level, 0, SpraysAt.Length - 1)]];
            for (int i = 0; i < sprays.Length; i++) sprays[i] = new Plume(MakeSpray(sprayMaterial, i), MakeMist(mistMaterial, i));

            var ripples = new Texture2D(TrackTextures.RippleSize, TrackTextures.RippleSize, TextureFormat.RGBA32, true, true)
            {
                name = "Rain ripples", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            ripples.SetPixels(TrackTextures.Ripples(17));
            ripples.Apply(true, true);
            Shader.SetGlobalTexture(RippleTexId, ripples);
            Shader.SetGlobalFloat(RippleTilesId, 1f / TrackTextures.RippleMetres);
            foreach (GraphicsOption o in TrackOptions) o.Changed += OnTrackOption;
            GraphicsOptions.Spray.Changed += OnSprayOption;
            GraphicsOptions.SprayMist.Changed += OnSprayOption;

            if (Materials != null)
            {
                // Tarmac darkens a lot and turns glossy; paint and kerbs a little; concrete and gravel
                // darken; grass only goes a shade deeper.
                Wet(Materials.Asphalt, 0.45f, 0.72f);
                Wet(Materials.RoadEdge, 0.45f, 0.72f);
                Wet(Materials.PitAsphalt, 0.45f, 0.7f);
                Wet(Materials.White, 0.12f, 0.8f);
                Wet(Materials.Yellow, 0.12f, 0.8f);
                Wet(Materials.Kerb, 0.12f, 0.8f);
                Wet(Materials.Hatch, 0.12f, 0.8f);
                Wet(Materials.Concrete, 0.35f, 0.55f);
                Wet(Materials.Gravel, 0.35f, 0.3f);
                Wet(Materials.Verge, 0.15f, 0.05f);
                Wet(Materials.Grass, 0.15f, 0.05f);
                Wet(Materials.Terrain, 0.15f, 0.05f);
            }
        }

        private void Update()
        {
            if (Plan == null) return;
            Now = RaceWeather.Forced ?? Plan.At(RaceSeconds != null ? RaceSeconds() : 0f);
            RaceWeather.Now = Now;
            ShowWet(Now);
            Camera cam = Camera.main;
            if (cam == null) return;
            Color light = RenderSettings.fogColor * 1.35f + RenderSettings.ambientSkyColor * 0.3f;
            if (Night != null) light += NightLook.Flood * (0.35f * NightLook.On(Night()));
            Rain(cam.transform, light);
            Spray(cam.transform.position, light);
        }

        private void OnDestroy()
        {
            RaceWeather.Now = WeatherState.Dry;
            foreach (GraphicsOption o in TrackOptions) o.Changed -= OnTrackOption;
            GraphicsOptions.Spray.Changed -= OnSprayOption;
            GraphicsOptions.SprayMist.Changed -= OnSprayOption;
            Shader.SetGlobalFloat(WaterId, 0f);
            Shader.SetGlobalFloat(RainId, 0f);
        }

        /// <summary>Redraws the wet track at once with the options as they now stand.</summary>
        private void OnTrackOption(GraphicsOption option) => shown = -1f;

        /// <summary>Clears what an option switched off had in the air.</summary>
        private void OnSprayOption(GraphicsOption option)
        {
            if (sprays == null) return;
            foreach (Plume s in sprays)
            {
                if (!GraphicsOptions.Spray.On) s.System.Clear();
                if (!GraphicsOptions.SprayMist.On) s.Mist.Clear();
            }
        }

        // ---- The wet track -------------------------------------------------------------------

        private sealed class Wettable
        {
            public Material Material;
            public Color Dry;
            public float Gloss, Darken, WetGloss;
            /// <summary>On the track shader, which draws its own wet look while the wet track option is on.</summary>
            public bool Shaded;
        }

        private void Wet(Material m, float darken, float wetGloss)
        {
            if (m == null) return;
            bool paved = Array.IndexOf(Materials.Paved, m) >= 0;
            surfaces.Add(new Wettable { Material = m, Dry = m.color, Gloss = m.GetFloat(GlossId), Darken = darken, WetGloss = wetGloss, Shaded = paved });
        }

        /// <summary>
        /// The water drawn on the track catches up with the weather's over a second or two. The track shader gets it
        /// every frame with the rain; the other surfaces darken as soon as they are damp, start to shine once a film of
        /// water lies on them, and shine like a mirror where water stands.
        /// </summary>
        private void ShowWet(WeatherState w)
        {
            float target = w.Water;
            water = water < 0f ? target : water + (target - water) * (1f - Mathf.Exp(-Time.unscaledDeltaTime / WaterLag));
            float heavy = WeatherSky.Heavy(w.Rain);
            Shader.SetGlobalFloat(WaterId, water);
            Shader.SetGlobalFloat(RainId, w.Rain > 0f ? Mathf.Max(heavy, 0.08f) : 0f);
            // The cars sweep the film off their line: most of it once the rain stops, less while it keeps falling.
            Shader.SetGlobalFloat(LineDryId, Mathf.Lerp(0.8f, 0.35f, heavy));
            Shader.SetGlobalFloat(PuddlesOnId, GraphicsOptions.Puddles.On ? 1f : 0f);
            Shader.SetGlobalFloat(RipplesOnId, GraphicsOptions.RainRipples.On ? 1f : 0f);
            bool shade = GraphicsOptions.WetTrack.On;
            Materials?.ShowWet(shade && (water > 0.002f || w.Rain > 0f));

            if (Mathf.Abs(water - shown) < 0.004f && shade == shaded) return;
            shown = water;
            shaded = shade;
            float damp = Mathf.Clamp01(water / 0.15f);
            float sheen = SkyClock.Smooth(0.1f, 0.9f, water);
            float standing = Mathf.Clamp01((water - 0.8f) / 1.5f);
            foreach (Wettable s in surfaces)
            {
                if (s.Shaded && shade)
                {
                    s.Material.color = s.Dry;
                    s.Material.SetFloat(GlossId, s.Gloss);
                    continue;
                }
                Color c = s.Dry * (1f - s.Darken * damp);
                c.a = s.Dry.a;
                s.Material.color = c;
                float gloss = Mathf.Lerp(s.Gloss, Mathf.Max(s.Gloss, s.WetGloss), sheen);
                if (s.WetGloss > 0.6f) gloss = Mathf.Min(gloss + 0.15f * standing, 0.9f);
                s.Material.SetFloat(GlossId, gloss);
            }
        }

        // ---- Rain round the camera -----------------------------------------------------------

        private ParticleSystem MakeRain(Material material)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = DropLife;
            main.startSpeed = 0f;
            main.startSize = 0.022f;
            main.startColor = Color.white;
            main.maxParticles = MostDrops;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(RainBox, 0.5f, RainBox);
            ParticleSystem.VelocityOverLifetimeModule fall = ps.velocityOverLifetime;
            fall.enabled = true;
            fall.space = ParticleSystemSimulationSpace.World;
            fall.x = 0f;
            fall.y = -DropSpeed;
            fall.z = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0.05f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Keeps the square of rain over the camera, a little ahead of it when it moves, slanted by
        /// the wind, and as thick as the rain is heavy.
        /// </summary>
        private void Rain(Transform cam, Color light)
        {
            Vector3 at = cam.position;
            float dt = Time.deltaTime;
            Vector3 velocity = Vector3.zero;
            bool cut = !haveCamera || (at - lastCamera).sqrMagnitude > 30f * 30f;
            if (!cut && dt > 1e-5f) velocity = Vector3.ClampMagnitude((at - lastCamera) / dt, 90f);
            lastCamera = at;
            haveCamera = true;

            rain.transform.position = at + velocity * (0.6f * DropLife) + Vector3.up * (0.6f * DropLife * DropSpeed);
            float alive = Now.Rain > 0f ? Mathf.Min(600f + 450f * Now.Rain, MostDrops) : 0f;
            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = alive / DropLife;
            // Wind blows the rain sideways: the plan's wind, from the west.
            float gust = Mathf.Max(Now.Wind - 0.5f, 0f) * 2.2f;
            ParticleSystem.VelocityOverLifetimeModule fall = rain.velocityOverLifetime;
            fall.x = gust;
            fall.z = gust * 0.35f;
            Color tint = light;
            tint.a = 0.3f;
            rainMaterial.SetColor(TintId, tint);
            // After a camera cut the rain is all behind: fill the new view at once.
            if (cut && alive > 0f)
            {
                rain.Clear();
                rain.Emit((int)alive);
            }
        }

        // ---- Spray behind the cars -----------------------------------------------------------

        /// <summary>
        /// One car's spray and the mist it leaves hanging: two small particle systems handed between the cars nearest the
        /// camera.
        /// </summary>
        private sealed class Plume
        {
            public readonly ParticleSystem System, Mist;
            public RaceCar Car;
            public float Rear;
            public Vector3 Last;
            public bool Fresh;

            public Plume(ParticleSystem system, ParticleSystem mist) { System = system; Mist = mist; }
        }

        private ParticleSystem MakeSpray(Material material, int index)
        {
            var go = new GameObject($"Spray {index}");
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startColor = Color.white;
            main.maxParticles = MostSpray;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.6f, 0.2f, 0.6f);
            ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.3f;
            ParticleSystem.VelocityOverLifetimeModule drift = ps.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            drift.y = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            drift.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 0.6f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>
        /// A car's mist: a few broad, faint puffs that hang in the air behind it for a couple of seconds and drift up and
        /// apart, so a fast car on a wet track trails a haze and a pack leaves the air grey behind it.
        /// </summary>
        private ParticleSystem MakeMist(Material material, int index)
        {
            var go = new GameObject($"Mist {index}");
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.283f);
            main.startColor = Color.white;
            main.maxParticles = MostMist;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.2f, 0.4f, 1.5f);
            ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.45f;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            // No cap on its speed, only air drag slowing it.
            drag.limit = 1000f;
            drag.dampen = 0f;
            drag.drag = 1.6f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            ParticleSystem.VelocityOverLifetimeModule drift = ps.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            drift.y = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            drift.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.9f));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Broad puffs right at the lens would fill the screen: cap them to half its height.
            renderer.maxParticleSize = 0.5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Hands the spray to the cars nearest the camera now and then, then throws it up behind each
        /// one, more the wetter the track and the faster the car.
        /// </summary>
        private void Spray(Vector3 camera, Color light)
        {
            Color tint = light;
            tint.a = 0.42f;
            sprayMaterial.SetColor(TintId, tint);
            tint.a = 0.1f;
            mistMaterial.SetColor(TintId, tint);
            float amount = SkyClock.Smooth(0.15f, 0.9f, Now.Water);
            bool spray = GraphicsOptions.Spray.On, mist = GraphicsOptions.SprayMist.On;
            if (Cars == null) return;
            if (amount > 0f && (spray || mist) && (picked < 0f || Time.unscaledTime - picked >= PickEvery))
            {
                picked = Time.unscaledTime;
                Pick(camera);
            }
            float dt = Time.deltaTime;
            foreach (Plume s in sprays)
            {
                ParticleSystem.EmissionModule emission = s.System.emission, haze = s.Mist.emission;
                if (s.Car == null || amount <= 0f || dt <= 1e-5f)
                {
                    emission.rateOverTime = 0f;
                    haze.rateOverTime = 0f;
                    continue;
                }
                Transform car = s.Car.transform;
                Vector3 at = car.position - car.forward * s.Rear + car.up * 0.35f;
                float speed = s.Fresh ? 0f : (at - s.Last).magnitude / dt;
                s.Last = at;
                // Placed fresh this frame: move it there before it throws anything, so no trail is left between cars.
                if (s.Fresh)
                {
                    s.Fresh = false;
                    s.System.transform.SetPositionAndRotation(at, car.rotation);
                    s.Mist.transform.SetPositionAndRotation(at + car.up * 0.4f, car.rotation);
                    emission.rateOverTime = 0f;
                    haze.rateOverTime = 0f;
                    continue;
                }
                s.System.transform.SetPositionAndRotation(at, car.rotation);
                s.Mist.transform.SetPositionAndRotation(at + car.up * 0.4f, car.rotation);
                emission.rateOverTime = spray ? 75f * amount * Mathf.Clamp01((speed - 10f) / 45f) : 0f;
                // The mist only comes off a car at speed.
                haze.rateOverTime = mist ? 9f * amount * Mathf.Clamp01((speed - 22f) / 40f) : 0f;
            }
        }

        private readonly List<(float, RaceCar)> nearest = new List<(float, RaceCar)>();

        private void Pick(Vector3 camera)
        {
            nearest.Clear();
            foreach (RaceCar c in Cars)
            {
                if (c == null) continue;
                float d = (c.transform.position - camera).sqrMagnitude;
                if (d < SprayRange * SprayRange) nearest.Add((d, c));
            }
            nearest.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            int keep = Mathf.Min(nearest.Count, sprays.Length);
            var chosen = new HashSet<RaceCar>();
            for (int i = 0; i < keep; i++) chosen.Add(nearest[i].Item2);
            // Sprays keep a car that is still near; the rest go to the near cars without one.
            foreach (Plume s in sprays)
                if (s.Car != null && !chosen.Remove(s.Car)) s.Car = null;
            foreach (Plume s in sprays)
            {
                if (s.Car != null) continue;
                RaceCar next = null;
                foreach (RaceCar c in chosen) { next = c; break; }
                if (next == null) break;
                chosen.Remove(next);
                s.Car = next;
                CarController body = next.GetComponent<CarController>();
                s.Rear = body != null ? body.Spec.HalfWheelbase + 0.9f : 2.2f;
                s.Fresh = true;
            }
        }
    }
}
