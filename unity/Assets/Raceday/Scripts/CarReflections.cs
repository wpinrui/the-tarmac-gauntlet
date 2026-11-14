using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What the cars reflect. A shared probe at the watched car draws the scene round it without the cars, one face a frame,
    /// and covers every car in sight. At High and up the cars get probes of their own, sitting in the car and drawing
    /// everything round it, the other cars too, as far as the shared probe sees: the car's own body is hidden while its
    /// probe draws. Which car has which, and when each is drawn, is <see cref="ProbePlan"/>'s: the watched car always
    /// has one, drawn whole every frame, and the cars nearest the camera share the rest, a face a frame, each taking its
    /// probe in and giving it back over half a second, so no car's paint jumps between probes. A probe's share of its car
    /// is its blend distance: its box reaches <see cref="Margin"/> past the body, so a blend distance of half Margin over the
    /// share gives every point of the body that share, the shared probe the rest. All the probes are HDR, so the
    /// floodlights' and the pit's lenses stay bright in the paint at night. Sets <c>_TarmacCarGlint</c> from the night's
    /// lights for the paint (CarPaint.hlsl). Without <see cref="GraphicsOptions.CarReflections"/> the cars reflect the sky
    /// alone. Sits on the race camera beside <see cref="RaceCameras"/>.
    /// </summary>
    [RequireComponent(typeof(RaceCameras))]
    public sealed class CarReflections : MonoBehaviour
    {
        /// <summary>What each quality level draws, Low to Ultra.</summary>
        private readonly struct Tier
        {
            /// <summary>The shared probe's face, pixels.</summary>
            public readonly int Shared;
            /// <summary>
            /// How many cars get their own probe, the watched car's among them, its face in pixels, and how many of the
            /// others draw a face each frame.
            /// </summary>
            public readonly int Near, NearSize, Busy;

            public Tier(int shared, int near = 0, int nearSize = 0, int busy = 0)
            {
                Shared = shared;
                Near = near;
                NearSize = nearSize;
                Busy = busy;
            }
        }

        private static readonly Tier[] Tiers =
        {
            new Tier(64),
            new Tier(128),
            new Tier(128, 3, 512, 1),
            new Tier(256, 5, 512, 2),
        };

        /// <summary>How far round the watched car the shared probe reaches, and how far it sees, m.</summary>
        private const float Reach = 4000f, Sight = 1500f;
        /// <summary>The probe's height above the car's origin: about the middle of its body, m.</summary>
        private const float Height = 0.8f;
        /// <summary>A car's probe covers its body and this much more all round, m.</summary>
        private const float Margin = 0.35f;
        /// <summary>
        /// A car's own probe matches the shared one's importance rather than outranking it. Outranking, two cars' probes
        /// took both of a 200 m road piece's probe slots wherever two cars drove on it, the shared probe dropped out, and
        /// the road round them flicked to URP's sky fallback and back as probes were handed between cars. Matched, the
        /// road piece takes the probe it overlaps most, the shared one, and a car still takes its own: Unity blends equal
        /// probes by how much of the renderer each holds, and URP lets the smaller one win inside its box.
        /// </summary>
        private const int SharedImportance = 100, NearImportance = SharedImportance, WaitingImportance = 0;

        private sealed class Near
        {
            public ReflectionProbe Probe;
            public CarController Car;
            public Renderer[] Body;
            public int Rendering = -1;
        }

        private RaceCameras views;
        private ReflectionProbe shared;
        private int sharedRendering = -1;
        private bool sharedFirst;
        private readonly List<Near> nears = new List<Near>();
        private ProbePlan plan = new ProbePlan(0);
        private readonly List<Vector3?> places = new List<Vector3?>();
        private readonly List<(int slot, bool whole)> draws = new List<(int, bool)>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private int level = -1;
        private TimeOfDay sky;
        private static readonly int GlintId = Shader.PropertyToID("_TarmacCarGlint");

        private void Awake()
        {
            views = GetComponent<RaceCameras>();
            shared = NewProbe("Car reflections");
            shared.size = Vector3.one * Reach;
            shared.importance = SharedImportance;
            shared.cullingMask = ~(1 << CarBuilder.CarLayer);
            shared.farClipPlane = Sight;
            GraphicsOptions.CarReflections.Changed += Toggled;
            RenderPipelineManager.beginCameraRendering += HideOwnCar;
            RenderPipelineManager.endCameraRendering += ShowOwnCar;
            Shader.SetGlobalFloat(GlintId, 0f);
            Toggled(GraphicsOptions.CarReflections);
        }

        private void OnDestroy()
        {
            GraphicsOptions.CarReflections.Changed -= Toggled;
            RenderPipelineManager.beginCameraRendering -= HideOwnCar;
            RenderPipelineManager.endCameraRendering -= ShowOwnCar;
            ShowOwnCar(default, null);
            if (shared != null) Destroy(shared.gameObject);
            foreach (Near n in nears)
                if (n.Probe != null) Destroy(n.Probe.gameObject);
            Shader.SetGlobalFloat(GlintId, 0f);
        }

        private static ReflectionProbe NewProbe(string name)
        {
            var go = new GameObject(name);
            ReflectionProbe p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.hdr = true;
            p.boxProjection = false;
            p.clearFlags = ReflectionProbeClearFlags.Skybox;
            p.nearClipPlane = 0.3f;
            return p;
        }

        private void Toggled(GraphicsOption option)
        {
            level = -1;
            sharedRendering = -1;
            sharedFirst = true;
            shared.enabled = option.On;
            Fit();
        }

        /// <summary>Sizes the probes for the quality level now.</summary>
        private void Fit()
        {
            int now = QualityLevels.Level;
            if (now == level) return;
            level = now;
            Tier tier = Tiers[Mathf.Clamp(level, 0, Tiers.Length - 1)];
            bool on = GraphicsOptions.CarReflections.On;
            if (shared.resolution != tier.Shared)
            {
                shared.resolution = tier.Shared;
                sharedFirst = true;
            }
            int count = on ? tier.Near : 0;
            while (nears.Count > count)
            {
                Near last = nears[nears.Count - 1];
                nears.RemoveAt(nears.Count - 1);
                if (last.Probe != null) Destroy(last.Probe.gameObject);
            }
            while (nears.Count < count)
            {
                ReflectionProbe p = NewProbe("Car reflections " + (nears.Count + 1));
                p.importance = WaitingImportance;
                p.cullingMask = ~0;
                p.farClipPlane = Sight;
                nears.Add(new Near { Probe = p });
            }
            if (plan.Slots.Length != nears.Count) plan = new ProbePlan(nears.Count);
            for (int i = 0; i < nears.Count; i++)
            {
                Near n = nears[i];
                if (n.Probe.resolution == tier.NearSize) continue;
                n.Probe.resolution = tier.NearSize;
                plan.Invalidate(i);
            }
        }

        private void LateUpdate()
        {
            Glint();
            if (!shared.enabled) return;
            if (Time.frameCount % 30 == 0) Fit();
            DrawShared();
            if (nears.Count == 0) return;
            DrawNear(Tiers[Mathf.Clamp(level, 0, Tiers.Length - 1)].Busy);
        }

        /// <summary>The paint's night boost, from how far the circuit's lights are on.</summary>
        private void Glint()
        {
            if (sky == null) sky = FindAnyObjectByType<TimeOfDay>();
            Shader.SetGlobalFloat(GlintId, sky != null ? sky.Lights : 0f);
        }

        private void DrawShared()
        {
            if (sharedRendering >= 0 && !shared.IsFinishedRendering(sharedRendering)) return;
            CarController car = views.Car;
            shared.transform.position = car != null ? car.transform.position + Vector3.up * Height : transform.position;
            shared.timeSlicingMode = sharedFirst ? ReflectionProbeTimeSlicingMode.NoTimeSlicing : ReflectionProbeTimeSlicingMode.IndividualFaces;
            sharedFirst = false;
            sharedRendering = shared.RenderProbe();
        }

        /// <summary>Steps the plan, sets each probe's car and share, and starts the drawing it asks for.</summary>
        private void DrawNear(int busy)
        {
            CarController[] cars = views.Cars;
            places.Clear();
            int watched = -1;
            if (cars != null)
                for (int c = 0; c < cars.Length; c++)
                {
                    CarController car = cars[c];
                    bool racing = car != null && car.gameObject.activeInHierarchy;
                    places.Add(racing ? car.transform.position : (Vector3?)null);
                    if (racing && car == views.Car) watched = c;
                }
            plan.Step(Time.deltaTime, Time.frameCount, transform.position, transform.forward, places, watched, busy, Finished, draws);
            for (int i = 0; i < nears.Count; i++)
            {
                Near n = nears[i];
                ProbePlan.Slot s = plan.Slots[i];
                CarController car = s.Car >= 0 ? cars[s.Car] : null;
                if (car != n.Car)
                {
                    n.Car = car;
                    n.Body = car != null ? car.GetComponentsInChildren<Renderer>(true) : null;
                }
                if (car != null)
                {
                    // The box follows its car every frame; only the drawing waits.
                    Bounds b = BodyBounds(car.transform);
                    n.Probe.center = b.center - n.Probe.transform.position;
                    n.Probe.size = b.size;
                }
                n.Probe.importance = s.Weight > 0f ? NearImportance : WaitingImportance;
                n.Probe.blendDistance = 0.5f * Margin / Mathf.Max(s.Weight, 1e-3f);
                if (s.Rendering) continue;
                n.Rendering = -1;
            }
            foreach ((int slot, bool whole) in draws)
            {
                Near n = nears[slot];
                n.Probe.transform.position = Capture(n.Car);
                n.Probe.center = BodyBounds(n.Car.transform).center - n.Probe.transform.position;
                n.Probe.timeSlicingMode = whole ? ReflectionProbeTimeSlicingMode.NoTimeSlicing : ReflectionProbeTimeSlicingMode.IndividualFaces;
                n.Rendering = n.Probe.RenderProbe();
            }
        }

        private bool Finished(int slot) => nears[slot].Rendering < 0 || nears[slot].Probe.IsFinishedRendering(nears[slot].Rendering);

        /// <summary>The car's body, with the margin, as a box in the world.</summary>
        private static Bounds BodyBounds(Transform car)
        {
            Vector3 half = CarBuilder.BoxSize * 0.5f + Vector3.one * Margin;
            half.y += 0.3f;
            Vector3 x = car.right * half.x, y = car.up * half.y, z = car.forward * half.z;
            var extent = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(car.TransformPoint(CarBuilder.BoxCentre), extent * 2f);
        }

        private static Vector3 Capture(CarController car) => car.transform.position + Vector3.up * Height;

        /// <summary>
        /// While a car's own probe draws a face, hides that car, so the probe sees round it rather than its insides. A probe's
        /// face is drawn from the probe's position, which is how its camera is told apart.
        /// </summary>
        private void HideOwnCar(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Reflection) return;
            Vector3 at = camera.transform.position;
            for (int i = 0; i < nears.Count; i++)
            {
                Near n = nears[i];
                ProbePlan.Slot s = plan.Slots[i];
                if (n.Car == null || n.Body == null || n.Rendering < 0 || !s.Rendering || s.Drawing != s.Car) continue;
                if ((n.Probe.transform.position - at).sqrMagnitude > 0.0025f) continue;
                foreach (Renderer r in n.Body)
                {
                    if (r == null || r.forceRenderingOff) continue;
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
                return;
            }
        }

        private void ShowOwnCar(ScriptableRenderContext context, Camera camera)
        {
            if (hidden.Count == 0) return;
            foreach (Renderer r in hidden)
                if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }
    }
}
