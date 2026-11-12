using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The cars' heat and friction: smoke off locked and spinning tyres, sparks where a car bottoms out at speed, haze
    /// behind the exhausts and brake discs glowing after a hard stop. It also sets how strongly the far tarmac shimmers in
    /// the heat (<see cref="HeatHazeFeature"/>) from the sun, the cloud and the wet. Cheap by design: smoke, sparks and haze
    /// are one particle system each, fed only by the cars nearest the camera, with a cap on cars and particles for each
    /// quality level; the discs' heat is a sum a car, and a car's glowing discs are four small quads drawn only while hot.
    /// Each part follows its graphics option and draws nothing with it off.
    /// </summary>
    public sealed class CarEffects : MonoBehaviour
    {
        [NonSerialized] public CarController[] Cars;
        /// <summary>How far the circuit's lights are on, 0 to 1 (<see cref="TimeOfDay.Lights"/>): the smoke catches them.</summary>
        [NonSerialized] public Func<float> Night;

        /// <summary>Caps at each quality level, Low, Medium, High and Ultra: a level past the last takes the last.</summary>
        private static readonly int[] SmokeCars = { 0, 6, 14, 24 }, SmokeMost = { 0, 260, 900, 1600 };
        private static readonly int[] SparkMost = { 0, 120, 400, 700 };
        private static readonly int[] HazeCars = { 0, 0, 6, 10 }, HazeMost = { 0, 0, 90, 160 };
        /// <summary>From High up, the smoke fades where it meets the scene, which needs its depth drawn.</summary>
        private const int SoftFrom = 2;
        /// <summary>Cars further from the camera than these throw nothing, m.</summary>
        private const float SmokeRange = 220f, HazeRange = 70f;
        /// <summary>Real seconds between choosing the cars nearest the camera, and between reading the quality level.</summary>
        private const float PickEvery = 0.3f, LevelEvery = 1f;
        /// <summary>Puffs a second from one fully sliding tyre at speed.</summary>
        private const float SmokeRate = 38f;
        /// <summary>A wheel squeezed past this share of its travel at speed scrapes the car's floor on the track.</summary>
        private const float ScrapeFrom = 0.92f;
        /// <summary>Sparks only fly past this speed, m/s, and a second at most from one corner.</summary>
        private const float SparkSpeed = 12f, SparkRate = 260f;
        /// <summary>Haze puffs a second from one car at full throttle.</summary>
        private const float HazeRate = 22f;
        /// <summary>Discs glow from inside the rim at this share of the wheel's radius, and sit this far in from its centre, m.</summary>
        private const float DiscRadius = 0.62f, DiscInset = 0.05f;

        private ParticleSystem smoke, sparks, haze;
        private Material smokeMaterial, sparkMaterial, hazeMaterial, discMaterial;
        private Mesh discMesh;
        private int level = -1;
        private float picked = -1f, levelRead = -1f;
        private readonly List<int> nearSmoke = new List<int>(), nearHaze = new List<int>();
        private readonly List<(float, int)> byDistance = new List<(float, int)>();
        /// <summary>Puffs and sparks owed to each car's corners, carried over between frames so slow rates still emit.</summary>
        private float[] smokeOwed, sparkOwed, hazeOwed;
        /// <summary>Seconds each car's tyres have kept sliding (<see cref="TyreSmoke.Step"/>).</summary>
        private float[] smokeHeld;
        /// <summary>Each car's disc temperatures, front then rear, °C, and its glowing discs once it has had any.</summary>
        private float[] discHeat;
        private Discs[] discs;
        private MaterialPropertyBlock block;

        private static readonly int GlowId = Shader.PropertyToID("_Glow"), SoftId = Shader.PropertyToID("_EffectsSoft"),
            NightId = Shader.PropertyToID("_EffectsNight");

        private sealed class Discs
        {
            public readonly MeshRenderer[] Views = new MeshRenderer[4];
            /// <summary>Each wheel's own tyre or rim: hidden, as when the crew has the wheel off, its disc is too.</summary>
            public readonly Renderer[] Wheels = new Renderer[4];
            public bool Shown;
        }

        private void Start()
        {
            smokeMaterial = new Material(Resources.Load<Shader>("Effects/Smoke")) { name = "Tyre smoke" };
            sparkMaterial = new Material(Resources.Load<Shader>("Effects/Sparks")) { name = "Sparks" };
            hazeMaterial = new Material(Resources.Load<Shader>("Effects/Haze")) { name = "Exhaust haze" };
            discMaterial = new Material(Resources.Load<Shader>("Effects/BrakeGlow")) { name = "Brake glow" };
            discMesh = Quad();
            block = new MaterialPropertyBlock();
            smoke = MakeSmoke();
            sparks = MakeSparks();
            haze = MakeHaze();
            ReadLevel();
            foreach (GraphicsOption o in new[] { GraphicsOptions.TyreSmoke, GraphicsOptions.Sparks, GraphicsOptions.ExhaustHaze, GraphicsOptions.BrakeGlow })
                o.Changed += OnOption;
        }

        private void OnDestroy()
        {
            foreach (GraphicsOption o in new[] { GraphicsOptions.TyreSmoke, GraphicsOptions.Sparks, GraphicsOptions.ExhaustHaze, GraphicsOptions.BrakeGlow })
                o.Changed -= OnOption;
            HeatHazeFeature.Shimmer = 0f;
            HeatHazeFeature.ExhaustHaze = false;
            HeatHazeFeature.SoftParticles = false;
            Shader.SetGlobalFloat(SoftId, 0f);
            Destroy(smokeMaterial);
            Destroy(sparkMaterial);
            Destroy(hazeMaterial);
            Destroy(discMaterial);
            Destroy(discMesh);
        }

        /// <summary>Clears what an option switched off had in the air or glowing.</summary>
        private void OnOption(GraphicsOption option)
        {
            if (smoke == null) return;
            if (!GraphicsOptions.TyreSmoke.On) smoke.Clear();
            if (!GraphicsOptions.Sparks.On) sparks.Clear();
            if (!GraphicsOptions.ExhaustHaze.On) haze.Clear();
            if (!GraphicsOptions.BrakeGlow.On && discs != null)
                foreach (Discs d in discs) Show(d, false);
            ReadLevel();
        }

        /// <summary>Sets the caps for the quality level now.</summary>
        private void ReadLevel()
        {
            levelRead = Time.unscaledTime;
            int now = QualityLevels.Level;
            bool soft = now >= SoftFrom && GraphicsOptions.TyreSmoke.On;
            HeatHazeFeature.SoftParticles = soft;
            Shader.SetGlobalFloat(SoftId, soft ? 1f : 0f);
            HeatHazeFeature.ExhaustHaze = At(HazeMost, now) > 0 && GraphicsOptions.ExhaustHaze.On;
            if (now == level) return;
            level = now;
            ParticleSystem.MainModule main = smoke.main;
            main.maxParticles = At(SmokeMost, level);
            main = sparks.main;
            main.maxParticles = At(SparkMost, level);
            main = haze.main;
            main.maxParticles = At(HazeMost, level);
            picked = -1f;
        }

        private static int At(int[] caps, int level) => caps[Mathf.Clamp(level, 0, caps.Length - 1)];

        private void Update()
        {
            if (Time.unscaledTime - levelRead >= LevelEvery) ReadLevel();
            WeatherState weather = RaceWeather.Now;
            HeatHazeFeature.Shimmer = GraphicsOptions.TarmacShimmer.On ? ShimmerNow(weather) : 0f;
            if (Cars == null) return;
            int n = Cars.Length;
            if (smokeOwed == null || smokeOwed.Length != n * 4)
            {
                smokeOwed = new float[n * 4];
                smokeHeld = new float[n * 4];
                sparkOwed = new float[n * 4];
                hazeOwed = new float[n];
                discHeat = new float[n * 2];
                for (int i = 0; i < discHeat.Length; i++) discHeat[i] = BrakeHeat.Ambient;
                discs = new Discs[n];
            }
            float dt = Time.deltaTime;
            Camera cam = Camera.main;
            if (cam != null && (picked < 0f || Time.unscaledTime - picked >= PickEvery))
            {
                picked = Time.unscaledTime;
                Pick(cam.transform.position);
            }

            float night = Night != null ? NightLook.On(Night()) : 0f;
            Color flood = NightLook.Flood * (0.3f * night);
            Shader.SetGlobalVector(NightId, new Vector4(flood.r, flood.g, flood.b, 0f));

            Glow(dt);
            if (dt <= 1e-5f) return;
            // Water on the track takes the heat out of a sliding tyre: little smoke in the wet.
            float dryness = 1f - 0.85f * weather.Wet;
            bool smoking = GraphicsOptions.TyreSmoke.On && smoke.main.maxParticles > 0;
            bool sparking = GraphicsOptions.Sparks.On && sparks.main.maxParticles > 0;
            if (smoking || sparking)
                foreach (int c in nearSmoke) Friction(c, dt, smoking, sparking, dryness);
            if (HeatHazeFeature.ExhaustHaze)
                foreach (int c in nearHaze) Exhaust(c, dt);
        }

        /// <summary>How strongly the far tarmac shimmers: under a high sun in a clear sky, on a dry track.</summary>
        private static float ShimmerNow(WeatherState weather)
        {
            Light sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled) return 0f;
            float height = -sun.transform.forward.y;
            float high = SkyClock.Smooth(0.25f, 0.75f, height);
            float clear = 1f - SkyClock.Smooth(0.35f, 0.95f, weather.Cloud);
            float dry = weather.Rain > 0f ? 0f : 1f - weather.Wet;
            return Mathf.Clamp01(high * clear * dry * Mathf.Clamp01(sun.intensity));
        }

        /// <summary>The cars nearest the camera that smoke and spark, and the fewer nearer still that show their haze.</summary>
        private void Pick(Vector3 camera)
        {
            byDistance.Clear();
            for (int c = 0; c < Cars.Length; c++)
            {
                CarController car = Cars[c];
                if (car == null || !car.isActiveAndEnabled || car.Asleep) continue;
                float d = (car.transform.position - camera).sqrMagnitude;
                if (d < SmokeRange * SmokeRange) byDistance.Add((d, c));
            }
            byDistance.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            nearSmoke.Clear();
            nearHaze.Clear();
            int smokeCars = At(SmokeCars, level), hazeCars = At(HazeCars, level);
            for (int i = 0; i < byDistance.Count; i++)
            {
                if (i < smokeCars) nearSmoke.Add(byDistance[i].Item2);
                if (i < hazeCars && byDistance[i].Item1 < HazeRange * HazeRange) nearHaze.Add(byDistance[i].Item2);
            }
        }

        // ---- Smoke and sparks ----------------------------------------------------------------

        private void Friction(int c, float dt, bool smoking, bool sparking, float dryness)
        {
            CarController car = Cars[c];
            if (car == null || car.Asleep) return;
            Vector3 velocity = car.Velocity;
            float speed = car.Speed;
            Transform body = car.transform;
            for (int w = 0; w < 4; w++)
            {
                int k = c * 4 + w;
                if (smoking)
                {
                    float skid = car.Skid(w, out Vector3 contact, out Vector3 normal);
                    float amount = TyreSmoke.Step(ref smokeHeld[k], skid, dt) * dryness;
                    if (amount > 0f)
                    {
                        // A wheel spinning on the spot smokes too, so the rate never falls to nothing at a standstill.
                        smokeOwed[k] += SmokeRate * amount * (0.45f + 0.55f * Mathf.Clamp01(speed / 30f)) * dt;
                        for (; smokeOwed[k] >= 1f; smokeOwed[k] -= 1f) Puff(contact + normal * 0.25f, velocity, body);
                    }
                    else smokeOwed[k] = 0f;
                }
                if (sparking && car.Squeeze(w, out float squeeze, out Vector3 ground) && speed > SparkSpeed)
                {
                    float scrape = SkyClock.Smooth(ScrapeFrom, 1f, squeeze) * Mathf.Clamp01((speed - SparkSpeed) / 40f);
                    if (scrape > 0f)
                    {
                        sparkOwed[k] += SparkRate * scrape * dt;
                        // Off the floor just inside the wheel, where the plank meets the track.
                        Vector3 under = ground + (body.position - ground) * 0.3f;
                        under += body.up * (0.04f - Vector3.Dot(under - ground, body.up));
                        for (; sparkOwed[k] >= 1f; sparkOwed[k] -= 1f) Spark(under, velocity, body);
                        continue;
                    }
                }
                sparkOwed[k] = 0f;
            }
        }

        private void Puff(Vector3 at, Vector3 carVelocity, Transform body)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at + UnityEngine.Random.insideUnitSphere * 0.15f,
                // The smoke leaves with part of the car's speed and boils up and out.
                velocity = carVelocity * UnityEngine.Random.Range(0.2f, 0.4f) + body.up * UnityEngine.Random.Range(0.4f, 1.1f)
                    + body.right * UnityEngine.Random.Range(-0.8f, 0.8f),
                startSize = UnityEngine.Random.Range(0.6f, 1.1f),
                startLifetime = UnityEngine.Random.Range(2.2f, 3.6f),
                rotation = UnityEngine.Random.Range(0f, 360f),
                startColor = Color.white,
            };
            smoke.Emit(p, 1);
        }

        private void Spark(Vector3 at, Vector3 carVelocity, Transform body)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = at + body.right * UnityEngine.Random.Range(-0.15f, 0.15f),
                // Sparks are flung back off the floor and bounce up, keeping most of the car's speed.
                velocity = carVelocity * UnityEngine.Random.Range(0.6f, 0.85f) - body.forward * UnityEngine.Random.Range(1f, 6f)
                    + body.up * UnityEngine.Random.Range(0.3f, 2.8f) + body.right * UnityEngine.Random.Range(-2.2f, 2.2f),
                startSize = UnityEngine.Random.Range(0.025f, 0.05f),
                startLifetime = UnityEngine.Random.Range(0.2f, 0.55f),
                startColor = Color.white,
            };
            sparks.Emit(p, 1);
        }

        // ---- Exhaust haze --------------------------------------------------------------------

        private void Exhaust(int c, float dt)
        {
            CarController car = Cars[c];
            if (car == null || car.Asleep) return;
            Transform body = car.transform;
            // Even at idle the exhaust is hot; it pours out with the throttle.
            hazeOwed[c] += HazeRate * (0.3f + 0.7f * car.Throttle) * dt;
            if (hazeOwed[c] < 1f) return;
            Vector3 at = body.TransformPoint(new Vector3(0f, 0.32f, -(car.Spec.HalfWheelbase + 0.95f)));
            Vector3 velocity = car.Velocity;
            for (; hazeOwed[c] >= 1f; hazeOwed[c] -= 1f)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = at + body.right * UnityEngine.Random.Range(-0.45f, 0.45f),
                    velocity = velocity * 0.55f - body.forward * UnityEngine.Random.Range(0.8f, 2f) + body.up * UnityEngine.Random.Range(0.2f, 0.6f),
                    startSize = UnityEngine.Random.Range(0.35f, 0.55f),
                    startLifetime = UnityEngine.Random.Range(0.45f, 0.8f),
                    rotation = UnityEngine.Random.Range(0f, 360f),
                    startColor = Color.white,
                };
                haze.Emit(p, 1);
            }
        }

        // ---- Brake glow ----------------------------------------------------------------------

        /// <summary>Heats and cools every car's discs, and shows the glowing ones.</summary>
        private void Glow(float dt)
        {
            bool on = GraphicsOptions.BrakeGlow.On;
            for (int c = 0; c < Cars.Length; c++)
            {
                CarController car = Cars[c];
                if (car == null || car.Spec == null) continue;
                CarSpec spec = car.Spec;
                float brake = car.Asleep ? 0f : car.Brake;
                float front = discHeat[c * 2] = BrakeHeat.Step(discHeat[c * 2], dt, brake, spec.BrakeDecel, spec.FrontBrakeShare * 0.5f, car.Speed);
                float rear = discHeat[c * 2 + 1] = BrakeHeat.Step(discHeat[c * 2 + 1], dt, brake, spec.BrakeDecel, (1f - spec.FrontBrakeShare) * 0.5f, car.Speed);
                float frontGlow = BrakeHeat.Glow(front), rearGlow = BrakeHeat.Glow(rear);
                bool hot = on && (frontGlow > 0.01f || rearGlow > 0.01f) && car.isActiveAndEnabled;
                Discs d = discs[c];
                if (!hot)
                {
                    if (d != null && d.Shown) Show(d, false);
                    continue;
                }
                if (d == null) d = discs[c] = Fit(car);
                if (!d.Shown) Show(d, true);
                for (int w = 0; w < 4; w++)
                {
                    MeshRenderer view = d.Views[w];
                    if (view == null) continue;
                    float g = w < 2 ? frontGlow : rearGlow;
                    Renderer wheel = d.Wheels[w];
                    view.enabled = g > 0.01f && (wheel == null || wheel.enabled);
                    if (!view.enabled) continue;
                    block.SetColor(GlowId, BrakeHeat.Colour(g));
                    view.SetPropertyBlock(block);
                }
            }
        }

        private static void Show(Discs d, bool shown)
        {
            d.Shown = shown;
            if (shown) return;
            foreach (MeshRenderer view in d.Views)
                if (view != null) view.enabled = false;
        }

        /// <summary>A glowing ring inside each of a car's wheels, inboard of the rim so the spokes stand in front of it.</summary>
        private Discs Fit(CarController car)
        {
            var d = new Discs();
            float size = car.Spec.WheelRadius * DiscRadius * 2f;
            for (int w = 0; w < 4; w++)
            {
                Transform pivot = car.WheelVisual(w);
                if (pivot == null) continue;
                d.Wheels[w] = pivot.GetComponentInChildren<Renderer>();
                var go = new GameObject("Brake glow");
                go.layer = pivot.gameObject.layer;
                go.transform.SetParent(pivot, false);
                // Inboard: along the axle towards the car's middle.
                float inward = Mathf.Sign(pivot.InverseTransformDirection(car.transform.position - pivot.position).x);
                go.transform.localPosition = new Vector3(inward * DiscInset / Mathf.Max(pivot.lossyScale.x, 1e-3f), 0f, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                go.transform.localScale = Vector3.one * (size / Mathf.Max(pivot.lossyScale.x, 1e-3f));
                go.AddComponent<MeshFilter>().sharedMesh = discMesh;
                MeshRenderer view = go.AddComponent<MeshRenderer>();
                view.sharedMaterial = discMaterial;
                view.shadowCastingMode = ShadowCastingMode.Off;
                view.receiveShadows = false;
                view.lightProbeUsage = LightProbeUsage.Off;
                view.reflectionProbeUsage = ReflectionProbeUsage.Off;
                view.enabled = false;
                d.Views[w] = view;
            }
            return d;
        }

        private static Mesh Quad()
        {
            var mesh = new Mesh { name = "Brake glow" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- The particle systems ------------------------------------------------------------

        private ParticleSystem MakeSystem(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startSpeed = 0f;
            main.maxParticles = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ps.Play();
            return ps;
        }

        private static Gradient Fade(float inAt, float outFrom, float most)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(most, inAt), new GradientAlphaKey(most * 0.8f, outFrom), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>
        /// Smoke: puffs that billow out to several times their size, slow in the air, rise and drift apart, and thin away
        /// over a few seconds, so a long lockup leaves a cloud hanging over the track.
        /// </summary>
        private ParticleSystem MakeSmoke()
        {
            ParticleSystem ps = MakeSystem("Tyre smoke", smokeMaterial);
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1000f;
            drag.dampen = 0f;
            drag.drag = 1.4f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            ParticleSystem.VelocityOverLifetimeModule drift = ps.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            drift.y = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            drift.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 2.2f), new Keyframe(1f, 4f)));
            ParticleSystem.RotationOverLifetimeModule turn = ps.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = Fade(0.06f, 0.35f, 0.55f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            // The shader lights each puff about its centre.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.Center,
            });
            renderer.maxParticleSize = 0.8f;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            return ps;
        }

        /// <summary>Sparks: tiny streaks stretched along their flight that fall, slow and cool from white to red.</summary>
        private ParticleSystem MakeSparks()
        {
            ParticleSystem ps = MakeSystem("Sparks", sparkMaterial);
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = 1f;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1000f;
            drag.dampen = 0f;
            drag.drag = 2.5f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            ParticleSystem.ColorOverLifetimeModule cool = ps.colorOverLifetime;
            cool.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[]
                {
                    new GradientColorKey(new Color(1f, 0.92f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.35f),
                    new GradientColorKey(new Color(0.9f, 0.18f, 0.03f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            cool.color = g;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.008f;
            renderer.lengthScale = 2f;
            return ps;
        }

        /// <summary>Exhaust haze: small, quick puffs that grow and fade behind the car, bending the scene behind them.</summary>
        private ParticleSystem MakeHaze()
        {
            ParticleSystem ps = MakeSystem("Exhaust haze", hazeMaterial);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.2f));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = Fade(0.1f, 0.4f, 1f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.maxParticleSize = 0.5f;
            return ps;
        }
    }
}
