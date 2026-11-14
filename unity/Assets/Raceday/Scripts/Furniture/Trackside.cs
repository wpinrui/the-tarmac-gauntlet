using System;
using System.Collections.Generic;
using TarmacGauntlet.TeamHq;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The trackside furniture in the scene: the start gantry, marshal posts with their marshals and flag panels,
    /// the distance boards, the armco's rails, posts and catch fences (<see cref="ArmcoBuilder"/>), and the hoardings,
    /// camera towers and cabins (<see cref="TracksideProps"/>). Each frame race
    /// control (<see cref="RaceFlags"/>) watches the cars and sets the start lights and every panel. All of it is solid:
    /// a car that leaves the road hits it as a barrier (<see cref="Barriers"/>).
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
        /// <summary>A cell of props shows its fine parts while taller than this share of the screen, and all of it while taller than the next.</summary>
        private const float PropDetail = 0.2f, PropCull = 0.07f;
        /// <summary>A camera operator stops being drawn once smaller than this share of the screen's height.</summary>
        private const float OperatorCull = 0.012f;
        /// <summary>The marshals' poses: clip and time, one per pose number the builder hands out.</summary>
        private static readonly (string clip, float time)[] Poses = { ("Idle", 0.6f), ("Nod", 0.4f) };

        /// <summary>Where the furniture stands.</summary>
        public TrackFurniture Placed { get; private set; }

        private Circuit circuit;
        private FurnitureMaterials materials;
        private RaceFlags flags;
        private Renderer[] panels;
        private RaceFlags.Panel[] shown;
        private Renderer red, green;
        private RaceFlags.Lights lights = (RaceFlags.Lights)(-1);
        private RaceFlags.Car[] states = new RaceFlags.Car[0];
        private readonly List<Renderer> armcoDetail = new List<Renderer>();
        /// <summary>The armco's catch fences and their posts: solid only while drawn, as the plain armco has none.</summary>
        private readonly List<Collider> fences = new List<Collider>();
        private Renderer[] armcoPlain = new Renderer[0];
        private GameObject props;

        /// <summary>Builds the furniture for a circuit laid by <paramref name="track"/>, clear of the grandstands and out from under the trees.</summary>
        public static Trackside Build(Circuit circuit, CircuitBuilder track, IReadOnlyList<StandFootprint> stands, IReadOnlyList<TreeSpot> trees)
        {
            Trackside t = new GameObject("Trackside").AddComponent<Trackside>();
            t.circuit = circuit;
            t.materials = new FurnitureMaterials();
            var placed = new TrackFurniture(circuit, track, stands);
            t.Placed = placed;
            var built = new FurnitureBuilder(circuit, placed, t.materials);
            Transform root = t.transform;

            foreach (CircuitPart part in built.Parts)
                t.Add(root, part.Name, part.Kit, part.Material, true, true);
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
            t.AddPeople(root, "Marshals", MarshalBody, true, built.Marshals, MarshalCull);

            var armco = new ArmcoBuilder(circuit, track.ArmcoRuns, stands, placed.GantryLeft, placed.GantryRight);
            var steel = new GameObject("Armco").transform;
            steel.SetParent(root, false);
            // The rails are hit as the circuit's plain armco, always there. The catch fences above them are hit only while drawn.
            foreach ((MeshKit kit, Material material, string name, bool solid) in new[]
            {
                (armco.Steel, t.materials.ArmcoSteel, "Armco steel", false),
                (armco.Fence, t.materials.CatchFence, "Armco catch fence", true),
                (armco.FencePosts, t.materials.Galvanised, "Armco catch fence posts", true),
            })
                foreach (MeshKit chunk in kit.Indexed().Chunks(ArmcoChunk))
                    if (!chunk.IsEmpty)
                    {
                        Renderer r = t.Add(steel, name, chunk, material, true, solid);
                        t.armcoDetail.Add(r);
                        if (solid) t.fences.Add(r.GetComponent<Collider>());
                    }

            var spots = new TracksideProps(circuit, track, stands, placed, armco.Ends, trees);
            t.AddProps(root, new PropBuilder(circuit, spots, t.materials));
            return t;
        }

        /// <summary>The armco's meshes are cut into cells this wide, so those off screen are skipped.</summary>
        private const float ArmcoChunk = 250f;

        /// <summary>
        /// The circuit's plain armco: drawn only while the armco's detail is off, and always there to hit. Its renderers
        /// swap with the rails and fences when the setting changes.
        /// </summary>
        public void SetPlainArmco(Renderer[] plain)
        {
            armcoPlain = plain ?? new Renderer[0];
            ShowArmco(GraphicsOptions.ArmcoDetail);
        }

        private void OnEnable()
        {
            GraphicsOptions.ArmcoDetail.Changed += ShowArmco;
            GraphicsOptions.TracksideProps.Changed += ShowProps;
        }

        private void OnDisable()
        {
            GraphicsOptions.ArmcoDetail.Changed -= ShowArmco;
            GraphicsOptions.TracksideProps.Changed -= ShowProps;
        }

        private void ShowProps(GraphicsOption option)
        {
            if (props != null) props.SetActive(option.On);
        }

        /// <summary>
        /// The props, a group per cell of the circuit: its fine parts drop out first as it shrinks on screen, then the
        /// whole cell. Each kind's meshes share one material across every cell, so they batch.
        /// </summary>
        private void AddProps(Transform parent, PropBuilder built)
        {
            props = new GameObject("Trackside props");
            props.transform.SetParent(parent, false);
            foreach (PropCell cell in built.Cells)
            {
                var holder = new GameObject("Props cell");
                holder.transform.SetParent(props.transform, false);
                holder.transform.position = cell.Centre;
                var coarse = new List<Renderer>();
                var all = new List<Renderer>();
                foreach (CircuitPart part in cell.Coarse) coarse.Add(AddAt(holder.transform, part.Name, part.Kit, part.Material, cell.Centre));
                all.AddRange(coarse);
                foreach (CircuitPart part in cell.Fine) all.Add(AddAt(holder.transform, part.Name + " detail", part.Kit, part.Material, cell.Centre));
                LODGroup lod = holder.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(PropDetail, all.ToArray()), new LOD(PropCull, coarse.ToArray()) });
                // Sized by the cell, not by what it holds, so every cell drops out at the same distance.
                lod.localReferencePoint = Vector3.zero;
                lod.size = PropBuilder.CellSize;
            }
            AddPeople(props.transform, "Camera operators", MarshalBody, false, built.Operators.ConvertAll(o => (o.feet, o.facing, 0)), OperatorCull);
            ShowProps(GraphicsOptions.TracksideProps);
        }

        /// <summary>A static mesh from world-space triangles, under a holder standing at <paramref name="origin"/>.</summary>
        private Renderer AddAt(Transform parent, string name, MeshKit kit, Material material, Vector3 origin)
        {
            var moved = new MeshKit();
            var v = kit.Vertices;
            var n = kit.Normals;
            var uv = kit.UVs;
            var tri = kit.Triangles;
            for (int i = 0; i < tri.Count; i += 3)
                moved.Triangle(v[tri[i]] - origin, v[tri[i + 1]] - origin, v[tri[i + 2]] - origin, n[tri[i]], n[tri[i + 1]], n[tri[i + 2]], uv[tri[i]], uv[tri[i + 1]], uv[tri[i + 2]]);
            return Add(parent, name, moved.Indexed(), material, true, true);
        }

        private void ShowArmco(GraphicsOption option)
        {
            bool detail = option.On;
            foreach (Renderer r in armcoDetail) r.enabled = detail;
            foreach (Collider c in fences) c.enabled = detail;
            foreach (Renderer r in armcoPlain) r.enabled = !detail;
        }

        /// <param name="solid">Cars hit it, as a barrier.</param>
        private Renderer Add(Transform parent, string name, MeshKit kit, Material material, bool shadows, bool solid = false)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            Mesh mesh = kit.ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (solid) Barriers.FromMesh(go, mesh);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return r;
        }

        /// <summary>
        /// People who stand still: a crew body baked once per pose, in orange overalls for the marshals, then placed as
        /// plain meshes that stop being drawn once too small to see.
        /// </summary>
        private void AddPeople(Transform parent, string name, string body, bool orange, List<(Vector3 feet, Vector3 facing, int pose)> spots, float cull)
        {
            if (spots.Count == 0) return;
            var meshes = new Mesh[Poses.Length];
            Material[] look = null;
            for (int p = 0; p < Poses.Length; p++)
            {
                meshes[p] = CrewStatue.Bake(body, Poses[p].clip, Poses[p].time, out Material[] mats);
                if (meshes[p] == null) return;
                look ??= orange ? Overalls(mats) : Instanced(mats);
            }
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            foreach (var (feet, facing, pose) in spots)
            {
                var go = new GameObject(name);
                go.transform.SetParent(holder, false);
                go.transform.position = feet;
                // The baked body faces -z.
                go.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[pose];
                Barriers.Person(go);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = look;
                r.shadowCastingMode = ShadowCastingMode.On;
                LODGroup lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(cull, new Renderer[] { r }) });
                lod.RecalculateBounds();
            }
        }

        /// <summary>The body's own materials, copied to draw instanced.</summary>
        private static Material[] Instanced(Material[] own)
        {
            var look = new Material[own.Length];
            for (int i = 0; i < own.Length; i++) look[i] = new Material(own[i]) { name = "Crew " + own[i].name, enableInstancing = true };
            return look;
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
