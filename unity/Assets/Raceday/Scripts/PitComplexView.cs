using System.Collections.Generic;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit complex in the race: paints each garage in its team's colour, puts the Poly Haven tool carts and
    /// chests in every garage with a car (dropped once a garage is too small on screen to show them) with the pit box
    /// kit (<see cref="PitBoxKitView"/>), and lights the office windows as the sky darkens. The glass reflects the circuit
    /// in front of it: a probe out over the pit lane for each stretch of glass draws the scene without the cars, small
    /// and not HDR, once at the start and again only as the sky's light changes, one probe at a time.
    /// </summary>
    public sealed class PitComplexView : MonoBehaviour
    {
        public TimeOfDay Sky;
        public PitMaterials Materials;
        public PitComplex Complex;

        /// <summary>A garage's props are dropped below this share of the screen's height.</summary>
        private const float PropsCull = 0.05f;
        /// <summary>Real seconds between checks of the sky.</summary>
        private const float SkyCheck = 0.5f;

        /// <summary>The kit in and over each box with a car, which the crews take and put back.</summary>
        public PitBoxKitView Kit { get; private set; }

        private float nextCheck;
        private float lastNight = -1f;

        /// <summary>A glass probe sits this far out from the middle of its stretch of glass, m, and draws this far.</summary>
        private const float ProbeOut = 15f, ProbeSight = 600f;
        /// <summary>Its face, pixels.</summary>
        private const int ProbeSize = 128;
        /// <summary>It covers its glass and this much more all round, m, and outranks the sky's and the cars' probes there.</summary>
        private const float ProbeMargin = 1.5f;
        private const int ProbeImportance = 101;
        /// <summary>
        /// The glass is redrawn when the sky's light has changed this much since it was last drawn. It is first drawn
        /// this many real seconds in, once the rest of the circuit and its lights are up.
        /// </summary>
        private const float ProbeStep = 0.1f, FirstDraw = 3f;

        private readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        /// <summary>The probe drawing now, the next to draw, and the sky's light they were last drawn for.</summary>
        private int drawing = -1, toDraw = -1, drawId;
        private float drawnFor = float.NaN, firstDrawAt;

        /// <summary>
        /// Paints each car's garage in its colour and fits out each garage with a car. Garages no car uses stay plain
        /// and empty. <c>carGarages</c> is each car's garage.
        /// </summary>
        public void Fit(PitComplex complex, Color[] cars, int[] carGarages, int garages)
        {
            var paint = new Color[garages];
            for (int g = 0; g < garages; g++) paint[g] = PitMaterials.Empty;
            var used = new int[cars.Length];
            for (int i = 0; i < used.Length; i++)
            {
                int g = i < carGarages.Length ? carGarages[i] : -1;
                used[i] = g >= 0 && g < garages ? g : -1;
                if (used[i] >= 0) paint[used[i]] = cars[i];
            }
            Materials.Paint(paint);
            var fitted = new bool[garages];
            foreach (int g in used)
            {
                if (g < 0 || fitted[g]) continue;
                fitted[g] = true;
                Props(complex, g);
            }
            Kit = new GameObject("Pit box kit").AddComponent<PitBoxKitView>();
            Kit.transform.SetParent(transform, false);
            Kit.Build(complex, used, cars);
        }

        private void Props(PitComplex complex, int g)
        {
            GarageFrame f = complex.Frame(g);
            var kit = new GameObject("Garage " + (g + 1) + " kit");
            kit.transform.SetParent(transform, false);
            kit.transform.SetPositionAndRotation(f.Floor, Quaternion.LookRotation(f.Out, Vector3.up));
            foreach (GarageProp p in PitComplexBuilder.Props)
                HqProps.Place(kit.transform, new HqProp { Id = p.Id, Position = new Vector3(p.XIn(complex, g), p.Y, p.Z), Yaw = p.Yaw, Scale = 1f });
            Renderer[] renderers = kit.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
                foreach (Material m in r.sharedMaterials) m.enableInstancing = true;
            LODGroup lod = kit.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(PropsCull, renderers) });
            lod.RecalculateBounds();
        }

        private void Start()
        {
            firstDrawAt = Time.unscaledTime + FirstDraw;
            if (Materials == null || Complex == null || Complex.Garages == 0) return;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial != Materials.Glass) continue;
                Bounds b = r.bounds;
                Vector3 outward = Outward(b.center);
                var go = new GameObject("Pit glass reflections " + (probes.Count + 1));
                go.transform.SetParent(transform, false);
                go.transform.position = b.center + outward * ProbeOut;
                ReflectionProbe p = go.AddComponent<ReflectionProbe>();
                p.mode = ReflectionProbeMode.Realtime;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                p.resolution = ProbeSize;
                p.hdr = false;
                p.boxProjection = false;
                p.clearFlags = ReflectionProbeClearFlags.Skybox;
                p.cullingMask = ~(1 << CarBuilder.CarLayer);
                p.nearClipPlane = 0.3f;
                p.farClipPlane = ProbeSight;
                p.importance = ProbeImportance;
                // The box is centred on the probe: it reaches back over the glass with the margin.
                Vector3 reach = b.extents + Vector3.one * ProbeMargin;
                Vector3 away = go.transform.position - b.center;
                p.size = 2f * new Vector3(reach.x + Mathf.Abs(away.x), reach.y + Mathf.Abs(away.y), reach.z + Mathf.Abs(away.z));
                probes.Add(p);
            }
        }

        /// <summary>Out of the building towards the pit lane, at the garage nearest this point.</summary>
        private Vector3 Outward(Vector3 at)
        {
            GarageFrame best = Complex.Frame(0);
            float near = float.MaxValue;
            for (int g = 0; g < Complex.Garages; g++)
            {
                GarageFrame f = Complex.Frame(g);
                float d = (f.Floor - at).sqrMagnitude;
                if (d < near) { near = d; best = f; }
            }
            return best.Out;
        }

        /// <summary>Draws the glass probes in turn when the sky's light has moved on since they were drawn.</summary>
        private void Reflect()
        {
            if (probes.Count == 0 || Sky == null || Time.unscaledTime < firstDrawAt) return;
            if (drawing >= 0)
            {
                if (!probes[drawing].IsFinishedRendering(drawId)) return;
                drawing = -1;
            }
            if (toDraw < 0)
            {
                if (!float.IsNaN(drawnFor) && Mathf.Abs(Sky.Lights - drawnFor) < ProbeStep) return;
                drawnFor = Sky.Lights;
                toDraw = 0;
            }
            drawing = toDraw;
            drawId = probes[drawing].RenderProbe();
            toDraw = toDraw + 1 < probes.Count ? toDraw + 1 : -1;
        }

        private void Update()
        {
            Reflect();
            if (Sky == null || Materials == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + SkyCheck;
            float night = Sky.Lights;
            if (Mathf.Abs(night - lastNight) < 0.01f) return;
            lastNight = night;
            Materials.Night(night);
        }
    }
}
