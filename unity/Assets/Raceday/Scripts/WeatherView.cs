using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Plays the race's weather (<see cref="WeatherPlan"/>) on the race clock: it sets
    /// <see cref="RaceWeather.Now"/> for the tyres, darkens the track and makes it shine as it gets
    /// wet, drops rain round the camera and throws spray up behind the cars nearest it. The sky does
    /// its own part (<see cref="TimeOfDay.Weather"/>). Cheap by design: the rain is one particle
    /// system that follows the camera, the spray a dozen small ones handed between the nearest cars,
    /// and the wet track only changes a few shared materials.
    /// </summary>
    public sealed class WeatherView : MonoBehaviour
    {
        [NonSerialized] public WeatherPlan Plan;
        [NonSerialized] public Func<float> RaceSeconds;
        [NonSerialized] public CircuitMaterials Materials;
        [NonSerialized] public RaceCar[] Cars;
        /// <summary>How far night has fallen, 0 to 1 (<see cref="SkyClock"/>): rain and spray catch the floodlights.</summary>
        [NonSerialized] public Func<float> Night;

        /// <summary>The weather now.</summary>
        public WeatherState Now { get; private set; } = WeatherState.Dry;

        /// <summary>How long a raindrop falls in sight, s, and how fast, m/s.</summary>
        private const float DropLife = 1.2f, DropSpeed = 8.5f;
        /// <summary>Most drops in the air round the camera at once.</summary>
        private const int MostDrops = 8000;
        /// <summary>Side of the square of rain round the camera, m.</summary>
        private const float RainBox = 36f;
        /// <summary>Cars that throw spray at once, the nearest to the camera within this range, m.</summary>
        private const int Sprays = 12;
        private const float SprayRange = 250f;
        /// <summary>Real seconds between choosing which cars throw spray.</summary>
        private const float PickEvery = 0.3f;

        private ParticleSystem rain;
        private Material rainMaterial, sprayMaterial;
        private Vector3 lastCamera;
        private bool haveCamera;
        private readonly Plume[] sprays = new Plume[Sprays];
        private float picked = -1f;
        private readonly List<Wettable> surfaces = new List<Wettable>();
        private float shown = -1f;
        private static readonly int TintId = Shader.PropertyToID("_Tint"), ShapeId = Shader.PropertyToID("_Shape"),
            GlossId = Shader.PropertyToID("_Smoothness");

        private void Start()
        {
            Shader drops = Resources.Load<Shader>("Weather/Drops");
            rainMaterial = new Material(drops) { name = "Rain" };
            rainMaterial.SetFloat(ShapeId, 0f);
            sprayMaterial = new Material(drops) { name = "Spray" };
            sprayMaterial.SetFloat(ShapeId, 1f);
            rain = MakeRain(rainMaterial);
            for (int i = 0; i < Sprays; i++) sprays[i] = new Plume(MakeSpray(sprayMaterial, i));

            if (Materials != null)
            {
                // Tarmac darkens a lot and turns glossy; paint and kerbs a little; concrete and gravel
                // darken; grass only goes a shade deeper.
                Wet(Materials.Asphalt, 0.45f, 0.72f);
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
            ShowWet(Now.Water);
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
        }

        // ---- The wet track -------------------------------------------------------------------

        private sealed class Wettable
        {
            public Material Material;
            public Color Dry;
            public float Gloss, Darken, WetGloss;
        }

        private void Wet(Material m, float darken, float wetGloss)
        {
            if (m == null) return;
            surfaces.Add(new Wettable { Material = m, Dry = m.color, Gloss = m.GetFloat(GlossId), Darken = darken, WetGloss = wetGloss });
        }

        /// <summary>
        /// The surfaces darken as soon as they are damp, start to shine once a film of water lies on
        /// them, and shine like a mirror where water stands.
        /// </summary>
        private void ShowWet(float water)
        {
            if (Mathf.Abs(water - shown) < 0.004f) return;
            shown = water;
            float damp = Mathf.Clamp01(water / 0.15f);
            float sheen = SkyClock.Smooth(0.1f, 0.9f, water);
            float standing = Mathf.Clamp01((water - 0.8f) / 1.5f);
            foreach (Wettable w in surfaces)
            {
                Color c = w.Dry * (1f - w.Darken * damp);
                c.a = w.Dry.a;
                w.Material.color = c;
                float gloss = Mathf.Lerp(w.Gloss, Mathf.Max(w.Gloss, w.WetGloss), sheen);
                if (w.WetGloss > 0.6f) gloss = Mathf.Min(gloss + 0.15f * standing, 0.9f);
                w.Material.SetFloat(GlossId, gloss);
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

        /// <summary>One car's spray: a small particle system handed between the cars nearest the camera.</summary>
        private sealed class Plume
        {
            public readonly ParticleSystem System;
            public RaceCar Car;
            public float Rear;
            public Vector3 Last;
            public bool Fresh;

            public Plume(ParticleSystem system) { System = system; }
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
            main.maxParticles = 80;
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
        /// Hands the spray to the cars nearest the camera now and then, then throws it up behind each
        /// one, more the wetter the track and the faster the car.
        /// </summary>
        private void Spray(Vector3 camera, Color light)
        {
            Color tint = light;
            tint.a = 0.42f;
            sprayMaterial.SetColor(TintId, tint);
            float amount = SkyClock.Smooth(0.15f, 0.9f, Now.Water);
            if (Cars == null) return;
            if (amount > 0f && (picked < 0f || Time.unscaledTime - picked >= PickEvery))
            {
                picked = Time.unscaledTime;
                Pick(camera);
            }
            float dt = Time.deltaTime;
            foreach (Plume s in sprays)
            {
                ParticleSystem.EmissionModule emission = s.System.emission;
                if (s.Car == null || amount <= 0f || dt <= 1e-5f)
                {
                    emission.rateOverTime = 0f;
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
                    emission.rateOverTime = 0f;
                    continue;
                }
                s.System.transform.SetPositionAndRotation(at, car.rotation);
                emission.rateOverTime = 75f * amount * Mathf.Clamp01((speed - 10f) / 45f);
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
            int keep = Mathf.Min(nearest.Count, Sprays);
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
