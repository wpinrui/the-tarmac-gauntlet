using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What the cars reflect. A shared probe at the watched car draws the scene round it without the cars, one face a frame,
    /// and covers every car in sight. At High and up the cars nearest the camera get a sharp probe each, sitting in the car
    /// and drawing everything round it, the other cars too: the car's own body is hidden while its probe draws. Those probes
    /// take turns, a few faces a frame, nearest car first. A probe moved to another car is left out until it is drawn
    /// again, so the car shows the shared probe meanwhile, never an old view. All the probes are HDR, so the floodlights'
    /// and the pit's lenses stay bright in the paint at night. Sets <c>_TarmacCarGlint</c> from the night's lights for the
    /// paint (CarPaint.hlsl). Without <see cref="GraphicsOptions.CarReflections"/> the cars reflect the sky alone.
    /// Sits on the race camera beside <see cref="RaceCameras"/>.
    /// </summary>
    [RequireComponent(typeof(RaceCameras))]
    public sealed class CarReflections : MonoBehaviour
    {
        /// <summary>What each quality level draws, Low to Ultra.</summary>
        private readonly struct Tier
        {
            /// <summary>The shared probe's face, pixels.</summary>
            public readonly int Shared;
            /// <summary>How many cars get their own probe, its face in pixels, and how many of them draw a face each frame.</summary>
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
            new Tier(128, 3, 512, 2),
            new Tier(256, 5, 512, 3),
        };

        /// <summary>How far round the watched car the shared probe reaches, and how far it sees, m.</summary>
        private const float Reach = 4000f, Sight = 1500f;
        /// <summary>How far a car's own probe sees, m: the trackside and the cars about it.</summary>
        private const float NearSight = 600f;
        /// <summary>Cars further from the camera than this keep the shared probe, m.</summary>
        private const float NearReach = 80f;
        /// <summary>A car behind the camera counts as this much further off, m.</summary>
        private const float Behind = 40f;
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
            /// <summary>The car the probe's drawing now was started at.</summary>
            public CarController Drawing;
            public Renderer[] Body;
            public int Rendering = -1;
            /// <summary>True once the probe has been drawn whole at the car it is on.</summary>
            public bool Ready;
            public int Started;
        }

        private RaceCameras views;
        private ReflectionProbe shared;
        private int sharedRendering = -1;
        private bool sharedFirst;
        private readonly List<Near> nears = new List<Near>();
        private readonly List<CarController> picked = new List<CarController>();
        private readonly List<float> pickedScore = new List<float>();
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
                p.farClipPlane = NearSight;
                nears.Add(new Near { Probe = p });
            }
            foreach (Near n in nears)
            {
                if (n.Probe.resolution == tier.NearSize) continue;
                n.Probe.resolution = tier.NearSize;
                n.Ready = false;
                n.Probe.importance = WaitingImportance;
            }
        }

        private void LateUpdate()
        {
            Glint();
            if (!shared.enabled) return;
            if (Time.frameCount % 30 == 0) Fit();
            DrawShared();
            if (nears.Count == 0) return;
            Pick();
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

        /// <summary>The cars nearest the camera, one per probe, each probe kept on its car while that car is still picked.</summary>
        private void Pick()
        {
            picked.Clear();
            pickedScore.Clear();
            CarController[] cars = views.Cars;
            Vector3 at = transform.position, ahead = transform.forward;
            if (cars != null)
                foreach (CarController car in cars)
                {
                    if (car == null || !car.gameObject.activeInHierarchy) continue;
                    Vector3 to = car.transform.position - at;
                    float score = to.magnitude + (Vector3.Dot(to, ahead) < 0f ? Behind : 0f);
                    if (score > NearReach) continue;
                    int i = pickedScore.Count;
                    while (i > 0 && pickedScore[i - 1] > score) i--;
                    if (i >= nears.Count) continue;
                    picked.Insert(i, car);
                    pickedScore.Insert(i, score);
                    if (picked.Count > nears.Count)
                    {
                        picked.RemoveAt(picked.Count - 1);
                        pickedScore.RemoveAt(pickedScore.Count - 1);
                    }
                }

            // A probe whose car is no longer picked lets it go; the picked cars without a probe take the free ones.
            foreach (Near n in nears)
                if (n.Car != null && !picked.Contains(n.Car)) Release(n);
            foreach (CarController car in picked)
            {
                if (Holding(car)) continue;
                foreach (Near n in nears)
                {
                    if (n.Car != null || (n.Rendering >= 0 && !n.Probe.IsFinishedRendering(n.Rendering))) continue;
                    n.Car = car;
                    n.Body = car.GetComponentsInChildren<Renderer>(true);
                    n.Ready = false;
                    n.Probe.importance = WaitingImportance;
                    break;
                }
            }

            // Each probe's box follows its car every frame; only the drawing waits.
            foreach (Near n in nears)
            {
                if (n.Car == null) continue;
                Bounds b = BodyBounds(n.Car.transform);
                n.Probe.center = b.center - n.Probe.transform.position;
                n.Probe.size = b.size;
            }
        }

        private bool Holding(CarController car)
        {
            foreach (Near n in nears)
                if (n.Car == car) return true;
            return false;
        }

        private static void Release(Near n)
        {
            n.Car = null;
            n.Body = null;
            n.Ready = false;
            n.Probe.importance = WaitingImportance;
        }

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

        /// <summary>Starts drawing the probes that are due, nearest car first, then the longest waiting, up to the budget.</summary>
        private void DrawNear(int budget)
        {
            int busy = 0;
            foreach (Near n in nears)
            {
                if (n.Rendering < 0 || n.Probe.IsFinishedRendering(n.Rendering))
                {
                    if (n.Rendering >= 0 && n.Car != null && !n.Ready && n.Drawing == n.Car)
                    {
                        n.Ready = true;
                        n.Probe.importance = NearImportance;
                    }
                    n.Rendering = -1;
                }
                else busy++;
            }
            while (busy < budget)
            {
                Near next = null;
                foreach (CarController car in picked)
                {
                    foreach (Near n in nears)
                        if (n.Car == car && n.Rendering < 0 && (next == null || Due(n, next))) next = n;
                    if (next != null && !next.Ready) break;
                }
                if (next == null) return;
                next.Probe.transform.position = Capture(next.Car);
                next.Probe.center = BodyBounds(next.Car.transform).center - next.Probe.transform.position;
                next.Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                next.Started = Time.frameCount;
                next.Drawing = next.Car;
                next.Rendering = next.Probe.RenderProbe();
                busy++;
            }
        }

        /// <summary>True if a should be drawn before b: one not yet drawn at its car first, then the one waiting longest.</summary>
        private static bool Due(Near a, Near b)
        {
            if (a.Ready != b.Ready) return !a.Ready;
            return a.Started < b.Started;
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
            foreach (Near n in nears)
            {
                if (n.Car == null || n.Body == null || n.Rendering < 0 || n.Drawing != n.Car) continue;
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
