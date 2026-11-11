using System;
using System.Collections.Generic;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The trackside furniture in the scene: the start gantry, marshal posts with their marshals and flag panels,
    /// and the distance boards. Each frame race control (<see cref="RaceFlags"/>) watches the cars and sets the start
    /// lights and every panel. None of it has a collider, so it never touches the racing.
    /// </summary>
    public sealed class Trackside : MonoBehaviour
    {
        /// <summary>The cars in the race.</summary>
        [NonSerialized] public RaceCar[] Cars;
        /// <summary>True once the race has started.</summary>
        [NonSerialized] public Func<bool> Racing;
        /// <summary>True while the field runs behind the pace car before the start.</summary>
        [NonSerialized] public Func<bool> PaceCarOut;

        /// <summary>Yellow and SC panels flash on and off this many times a second, in real time.</summary>
        private const float FlashRate = 1.25f;
        /// <summary>A marshal stops being drawn once smaller than this share of the screen's height.</summary>
        private const float MarshalCull = 0.012f;
        private const string MarshalBody = "crew_overalls";
        /// <summary>The marshals' poses: clip and time, one per pose number the builder hands out.</summary>
        private static readonly (string clip, float time)[] Poses = { ("Idle", 0.6f), ("Nod", 0.4f) };

        private Circuit circuit;
        private FurnitureMaterials materials;
        private RaceFlags flags;
        private Renderer[] panels;
        private RaceFlags.Panel[] shown;
        private Renderer red, green;
        private RaceFlags.Lights lights = (RaceFlags.Lights)(-1);
        private RaceFlags.Car[] states = new RaceFlags.Car[0];

        /// <summary>Builds the furniture for a circuit laid by <paramref name="track"/>.</summary>
        public static Trackside Build(Circuit circuit, CircuitBuilder track)
        {
            Trackside t = new GameObject("Trackside").AddComponent<Trackside>();
            t.circuit = circuit;
            t.materials = new FurnitureMaterials();
            var placed = new TrackFurniture(circuit, track);
            var built = new FurnitureBuilder(circuit, placed, t.materials);
            Transform root = t.transform;

            foreach (CircuitPart part in built.Parts)
                t.Add(root, part.Name, part.Kit, part.Material, true);
            t.red = t.Add(root, "Start lights red", built.RedLenses, t.materials.RedOff, false);
            t.green = t.Add(root, "Start lights green", built.GreenLenses, t.materials.GreenOff, false);

            var distances = new List<float>();
            t.panels = new Renderer[placed.Posts.Count];
            t.shown = new RaceFlags.Panel[placed.Posts.Count];
            for (int i = 0; i < placed.Posts.Count; i++)
            {
                distances.Add(placed.Posts[i].Distance);
                t.panels[i] = t.Add(root, "Flag panel " + (i + 1), built.Panels[i], t.materials.Panels[(int)RaceFlags.Panel.Off], false);
            }
            t.flags = new RaceFlags(circuit.Lap, distances);
            t.AddMarshals(root, built.Marshals);
            return t;
        }

        private Renderer Add(Transform parent, string name, MeshKit kit, Material material, bool shadows)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return r;
        }

        /// <summary>
        /// The marshals: a crew body in orange overalls baked once per pose, then placed as plain meshes that stop
        /// being drawn once too small to see.
        /// </summary>
        private void AddMarshals(Transform parent, List<(Vector3 feet, Vector3 facing, int pose)> spots)
        {
            var meshes = new Mesh[Poses.Length];
            Material[] look = null;
            for (int p = 0; p < Poses.Length; p++)
            {
                meshes[p] = CrewStatue.Bake(MarshalBody, Poses[p].clip, Poses[p].time, out Material[] mats);
                if (meshes[p] == null) return;
                look ??= Overalls(mats);
            }
            var holder = new GameObject("Marshals").transform;
            holder.SetParent(parent, false);
            foreach (var (feet, facing, pose) in spots)
            {
                var go = new GameObject("Marshal");
                go.transform.SetParent(holder, false);
                go.transform.position = feet;
                // The baked body faces -z.
                go.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[pose];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = look;
                r.shadowCastingMode = ShadowCastingMode.On;
                LODGroup lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(MarshalCull, new Renderer[] { r }) });
                lod.RecalculateBounds();
            }
        }

        /// <summary>The body's own materials with its clothes repainted marshal orange.</summary>
        private static Material[] Overalls(Material[] own)
        {
            Texture2D orange = Resources.Load<Texture2D>("Furniture/marshal_overalls");
            var look = new Material[own.Length];
            for (int i = 0; i < own.Length; i++)
            {
                look[i] = new Material(own[i]) { name = "Marshal " + own[i].name, enableInstancing = true };
                Texture tex = own[i].mainTexture;
                bool clothes = tex != null && (tex.name.EndsWith("_t0_c") || tex.name.EndsWith("_t2_c"));
                if (orange != null && clothes) look[i].mainTexture = orange;
            }
            return look;
        }

        private void Update()
        {
            if (flags == null || Cars == null) return;
            if (states.Length != Cars.Length) states = new RaceFlags.Car[Cars.Length];
            for (int i = 0; i < Cars.Length; i++)
            {
                RaceCar car = Cars[i];
                bool there = car != null && car.gameObject.activeInHierarchy;
                bool racing = there && car.enabled;
                states[i] = new RaceFlags.Car
                {
                    Distance = there ? Mathf.Repeat(car.Traffic.Distance, circuit.Lap) : 0f,
                    Speed = racing ? Mathf.Abs(car.Traffic.Speed) : 0f,
                    OnTrack = there && !car.InPitLane,
                    Parked = there && !racing,
                };
            }
            flags.Tick(Time.deltaTime, PaceCarOut != null && PaceCarOut(), Racing == null || Racing(), states);

            bool flashOn = Mathf.Repeat(Time.unscaledTime * FlashRate, 1f) < 0.6f;
            for (int p = 0; p < panels.Length; p++)
            {
                RaceFlags.Panel want = flags.Panels[p];
                bool flashes = want == RaceFlags.Panel.Yellow || want == RaceFlags.Panel.SafetyCar;
                if (flashes && !flashOn) want = RaceFlags.Panel.Off;
                if (want == shown[p]) continue;
                shown[p] = want;
                panels[p].sharedMaterial = materials.Panels[(int)want];
            }
            if (flags.StartLights != lights)
            {
                lights = flags.StartLights;
                red.sharedMaterial = lights == RaceFlags.Lights.Red ? materials.RedOn : materials.RedOff;
                green.sharedMaterial = lights == RaceFlags.Lights.Green ? materials.GreenOn : materials.GreenOff;
            }
        }
    }
}
