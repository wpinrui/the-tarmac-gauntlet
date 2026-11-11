using System.Collections.Generic;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit crews in every garage with a car: Team HQ's crew_suit body and clips, in their team's colours, waiting
    /// in the garage and working each stop in step with its clock (<see cref="GarageCrew"/>). The stop's clock is
    /// read from the car's <see cref="PitService"/>, the AI's and the player's alike, and never written.
    ///
    /// Drawing 100 crews is kept cheap two ways. Each person is a still figure (standing, mid stride or crouched,
    /// baked from the clips once) that moves about the box, all of them sharing a few instanced meshes and dropped
    /// when the garage is small on screen. The crews nearest the camera swap their still figures for live skinned
    /// rigs that play the clips, at most <see cref="MaxLive"/> people at a time, the crews at work first.
    /// </summary>
    public sealed class PitCrews : MonoBehaviour
    {
        /// <summary>How the suits take the team's colours.</summary>
        public enum KitLook { TeamTrim, TeamSuit, Same }

        /// <summary>The kit look the crews wear.</summary>
        public const KitLook Kit = KitLook.TeamSuit;
        private const string BodyId = "crew_suit";
        /// <summary>Most people played as live rigs at once.</summary>
        private const int MaxLive = 48;
        /// <summary>A crew is played live while a person in it stands at least this share of the screen's height.</summary>
        private const float LiveShare = 0.035f;
        /// <summary>A garage's crew is dropped below this share of the screen's height, measured over <see cref="CrewZone"/>.</summary>
        private const float Cull = 0.1f;
        /// <summary>Size of the space a crew works in, garage front and box, m.</summary>
        private const float CrewZone = 30f;
        /// <summary>Real seconds between picks of the live crews.</summary>
        private const float PickEvery = 0.25f;
        private const float PersonHeight = 1.8f;
        /// <summary>The player's crew walk out once the player's car is in the pit lane this close to the box, m.</summary>
        private const float PlayerReady = 200f;
        private static readonly Color Charcoal = new Color(0.17f, 0.18f, 0.2f, 1f);
        private static readonly int ClothId = Shader.PropertyToID("_Cloth"), PanelId = Shader.PropertyToID("_Panel");

        private enum Figure { Standing, Walking, Low }

        private sealed class Live
        {
            public HqCrewRig Rig;
            public SkinnedMeshRenderer Skin;
            public Transform LeftHand, RightHand;
        }

        private RaceCar[] cars;
        private AiDriver[] drivers;
        private CarController[] controllers;
        private BoxCollider[] bodies;
        private PitLane lane;
        private HqCrewBody body;
        private Material[] materials;
        private int[] kitParts;
        private readonly Mesh[] stills = new Mesh[3];
        /// <summary>Each still figure's hands, left and right, in its own frame.</summary>
        private readonly Vector3[,] stillHands = new Vector3[3, 2];

        // By car: its garage's crew, still figures and live rigs (null where none).
        private GarageCrew[] crews;
        private MeshFilter[][] figures;
        private Renderer[][] figureRenderers;
        private Live[][] live;
        private MaterialPropertyBlock[] kits;
        private readonly Stack<Live> pool = new Stack<Live>();
        private int built;
        private float nextPick;
        private readonly List<(HqCrewBody.Clip, float, float)> layers = new List<(HqCrewBody.Clip, float, float)>();
        private readonly List<(int car, float share, bool busy)> candidates = new List<(int, float, bool)>();
        private readonly Plane[] frustum = new Plane[6];

        /// <summary>
        /// Puts a crew in the garage of every car: crew[i] people for car i (at most <see cref="PitComplex.StandbySpots"/>),
        /// in the colour colours[i].
        /// </summary>
        public void Build(PitComplex complex, PitLane pits, RaceCar[] raceCars, AiDriver[] aiDrivers, Color[] colours, int[] crew)
        {
            body = HqCrewBody.Load(BodyId);
            if (body == null) return;
            cars = raceCars;
            drivers = aiDrivers;
            lane = pits;
            int n = cars.Length;
            controllers = new CarController[n];
            bodies = new BoxCollider[n];
            crews = new GarageCrew[n];
            figures = new MeshFilter[n][];
            figureRenderers = new Renderer[n][];
            live = new Live[n][];
            kits = new MaterialPropertyBlock[n];
            Materials();
            Stills();
            var spots = new PitStopSpots(complex, pits);
            for (int i = 0; i < n; i++)
            {
                controllers[i] = cars[i].GetComponent<CarController>();
                bodies[i] = cars[i].GetComponent<BoxCollider>();
                int g = cars[i].Garage;
                if (g < 0 || crew[i] <= 0) continue;
                crews[i] = new GarageCrew(complex, spots, g, crew[i], 1000 + g);
                kits[i] = KitBlock(colours[i]);
                Figures(i);
            }
        }

        // ---------------------------------------------------------------- for the kit and the drivers

        /// <summary>
        /// Car i's garage crew, or null with none: each person's jobs (<see cref="CrewMember.Tasks"/>), the task they are
        /// on (<see cref="GarageCrew.Current"/>), its place and time, and where they stand (<see cref="GarageCrew.World"/>).
        /// </summary>
        public GarageCrew Crew(int car) => crews != null && car >= 0 && car < crews.Length ? crews[car] : null;

        /// <summary>The stop's clock car i's crew follows this frame.</summary>
        public StopClock ClockOf(int car) => Crew(car) == null ? default : Clock(car);

        /// <summary>Where a person of car i's crew has a hand, in the world: off the live rig near the camera, else off their still figure.</summary>
        public Vector3 Hand(int car, int person, bool right)
        {
            Live l = live[car][person];
            if (l != null) return (right ? l.RightHand : l.LeftHand).position;
            Transform t = figures[car][person].transform;
            return t.TransformPoint(stillHands[(int)FigureFor(crews[car].People[person]), right ? 1 : 0]);
        }

        // ---------------------------------------------------------------- materials and still figures

        /// <summary>The body's own materials, but the suit's cloth parts drawn with the kit shader, all instanced.</summary>
        private void Materials()
        {
            Material[] own = body.PartMaterials(body.Palette(0));
            Shader kit = Resources.Load<Shader>("PitCrew/CrewKit");
            var parts = new List<int>();
            materials = new Material[own.Length];
            for (int s = 0; s < own.Length; s++)
            {
                string name = body.MaterialName(s);
                string mask = name.Contains("Outfit_Top") ? "PitCrew/kit_top" : name.Contains("Outfit_Bottom") ? "PitCrew/kit_bottom" : null;
                if (mask == null || kit == null || Kit == KitLook.Same)
                {
                    materials[s] = new Material(own[s]) { enableInstancing = true };
                    continue;
                }
                var m = new Material(kit) { name = "Crew kit " + name, enableInstancing = true };
                m.color = own[s].color;
                m.mainTexture = own[s].mainTexture;
                if (own[s].HasProperty("_BumpMap")) m.SetTexture("_BumpMap", own[s].GetTexture("_BumpMap"));
                m.SetTexture("_Mask", Resources.Load<Texture2D>(mask));
                m.SetFloat("_Glossiness", own[s].GetFloat("_Glossiness"));
                materials[s] = m;
                parts.Add(s);
            }
            kitParts = parts.ToArray();
        }

        /// <summary>The team's colours for the kit parts, per the chosen look.</summary>
        private static MaterialPropertyBlock KitBlock(Color team)
        {
            var b = new MaterialPropertyBlock();
            team.a = 1f;
            Color clear = new Color(1f, 1f, 1f, 0f);
            b.SetColor(ClothId, Kit == KitLook.TeamSuit ? team : clear);
            b.SetColor(PanelId, Kit == KitLook.TeamSuit ? Charcoal : Kit == KitLook.TeamTrim ? team : clear);
            return b;
        }

        private void Paint(Renderer r, MaterialPropertyBlock kit)
        {
            foreach (int s in kitParts) r.SetPropertyBlock(kit, s);
        }

        /// <summary>Bakes the still figures: standing, mid stride and crouched at a wheel.</summary>
        private void Stills()
        {
            HqCrewRig rig = body.Build(transform, 0, true);
            var skin = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            (string clip, float time)[] poses = { ("Idle", 0.8f), ("Walk", 0.24f), ("Crouch", 1.0f) };
            for (int p = 0; p < poses.Length; p++)
            {
                layers.Clear();
                layers.Add((body.Clips[poses[p].clip], poses[p].time, 1f));
                rig.Pose(layers);
                var mesh = new Mesh { name = "Crew still " + poses[p].clip };
                skin.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                stills[p] = mesh;
                Transform root = rig.Root.transform;
                stillHands[p, 0] = root.InverseTransformPoint(rig.Find("LeftHand").position);
                stillHands[p, 1] = root.InverseTransformPoint(rig.Find("RightHand").position);
            }
            Destroy(rig.Root);
        }

        /// <summary>A still figure for everyone in car i's garage, under one LOD group that drops them all when small.</summary>
        private void Figures(int i)
        {
            GarageCrew crew = crews[i];
            var group = new GameObject("Garage " + (crew.Garage + 1) + " crew");
            group.transform.SetParent(transform, false);
            int n = crew.People.Length;
            figures[i] = new MeshFilter[n];
            figureRenderers[i] = new Renderer[n];
            live[i] = new Live[n];
            for (int k = 0; k < n; k++)
            {
                var go = new GameObject("Crew " + (k + 1));
                go.transform.SetParent(group.transform, false);
                figures[i][k] = go.AddComponent<MeshFilter>();
                figures[i][k].sharedMesh = stills[0];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = materials;
                r.shadowCastingMode = ShadowCastingMode.On;
                Paint(r, kits[i]);
                figureRenderers[i][k] = r;
                Place(go.transform, crew, crew.People[k]);
            }
            LODGroup lod = group.AddComponent<LODGroup>();
            lod.localReferencePoint = crew.World(new Vector2(0f, crew.Door));
            lod.size = CrewZone;
            lod.SetLODs(new[] { new LOD(Cull, figureRenderers[i]) });
        }

        /// <summary>Sets a person's root where they stand, facing their way: the bodies face -Z, as packed.</summary>
        private static void Place(Transform t, GarageCrew crew, CrewMember m)
        {
            Vector3 face = crew.WorldDirection(m.Facing);
            t.SetPositionAndRotation(crew.World(m.At), Quaternion.LookRotation(-face, Vector3.up));
        }

        private static Figure FigureFor(CrewMember m) => m.Low ? Figure.Low : m.Clip == "Walk" ? Figure.Walking : Figure.Standing;

        // ---------------------------------------------------------------- each frame

        private void Update()
        {
            if (crews == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < crews.Length; i++)
            {
                GarageCrew crew = crews[i];
                if (crew == null) continue;
                StopClock clock = Clock(i);
                bool wasResting = crew.AtRest;
                crew.Step(clock, dt);
                if (wasResting && crew.AtRest && live[i][0] == null) continue;
                for (int k = 0; k < crew.People.Length; k++)
                {
                    CrewMember m = crew.People[k];
                    Live l = live[i][k];
                    if (l != null)
                    {
                        Place(l.Rig.Root.transform, crew, m);
                        Pose(l, m);
                        continue;
                    }
                    Place(figures[i][k].transform, crew, m);
                    figures[i][k].sharedMesh = stills[(int)FigureFor(m)];
                }
            }
            if (Time.unscaledTime >= nextPick)
            {
                nextPick = Time.unscaledTime + PickEvery;
                PickLive();
            }
        }

        /// <summary>The stop's clock for car i's crew, from its pit plan or, while the player drives, its own service.</summary>
        private StopClock Clock(int i)
        {
            RaceCar race = cars[i];
            CarController car = controllers[i];
            GarageCrew crew = crews[i];
            var c = new StopClock();
            if (bodies[i] != null)
            {
                c.CarLength = bodies[i].size.z;
                c.CarWidth = bodies[i].size.x;
            }
            Vector3 toBox = crew.Box - race.transform.position;
            toBox.y = 0f;
            c.Car = crew.Local(race.transform.position);
            c.CarSpeed = car.Speed;
            c.CarNear = Mathf.Abs(c.Car.x) < 40f && Mathf.Abs(c.Car.y) < 12f;
            PitService service = null;
            PitPlan plan = car.AiDriving && drivers[i] != null ? drivers[i].Racer?.Pit : null;
            if (plan != null)
            {
                switch (plan.State)
                {
                    case PitPlan.Stage.ToBox: c.Stage = CrewStage.Ready; break;
                    case PitPlan.Stage.Stopped:
                        service = plan.Service;
                        // A retired car parks in its box for good, with no service: the crew stay in.
                        c.Stage = service == null || car.Supplies != null && car.Supplies.Retired ? CrewStage.Home : service.Done ? CrewStage.Release : CrewStage.Stop;
                        break;
                    case PitPlan.Stage.Leaving: c.Stage = CrewStage.Release; break;
                    default: c.Stage = CrewStage.Home; break;
                }
            }
            else if (!car.AiDriving)
            {
                service = race.Service;
                if (service != null) c.Stage = service.Done ? CrewStage.Release : CrewStage.Stop;
                else if (race.InPitLane && Vector3.Dot(toBox, crew.Forward) > -3f && toBox.sqrMagnitude < PlayerReady * PlayerReady
                         && (car.Supplies == null || !car.Supplies.Retired))
                    c.Stage = CrewStage.Ready;
            }
            if (service != null)
            {
                c.Elapsed = service.Elapsed;
                c.FuelFrom = service.FuelFrom;
                c.FuelTo = service.FuelTo;
                c.TyresFrom = service.TyresFrom;
                c.TyresTo = service.TyresTo;
                c.NewTyres = service.NewTyres;
                c.NewDriver = service.NewDriver;
                c.Duration = service.Duration;
            }
            return c;
        }

        /// <summary>Poses a live rig from the person's clip, with the last one fading out under it.</summary>
        private void Pose(Live l, CrewMember m)
        {
            layers.Clear();
            if (m.UnderWeight > 0f && m.Under != null) layers.Add((body.Clips[m.Under], ClipTime(m.Under, m.UnderTime, m.UnderLoop), m.UnderWeight));
            layers.Add((body.Clips[m.Clip], ClipTime(m.Clip, m.ClipTime, m.Loop), 1f - m.UnderWeight));
            l.Rig.Pose(layers);
        }

        private float ClipTime(string clip, float t, bool loop)
        {
            float d = body.Clips[clip].Duration;
            return loop ? Mathf.Repeat(t, d) : Mathf.Clamp(t, 0f, d);
        }

        // ---------------------------------------------------------------- live rigs

        /// <summary>
        /// Picks the crews to play live: those in view whose people stand at least <see cref="LiveShare"/> of the screen,
        /// crews at work first, then the biggest on screen, until <see cref="MaxLive"/> people.
        /// </summary>
        private void PickLive()
        {
            Camera cam = Camera.main;
            candidates.Clear();
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
                for (int i = 0; i < crews.Length; i++)
                {
                    GarageCrew crew = crews[i];
                    if (crew == null) continue;
                    Vector3 mid = crew.World(new Vector2(0f, crew.Door * 0.5f));
                    float share = PersonHeight / (Mathf.Max(Vector3.Distance(cam.transform.position, mid), 1f) * tan);
                    if (share < LiveShare || !GeometryUtility.TestPlanesAABB(frustum, new Bounds(mid + Vector3.up * 1.5f, Vector3.one * (crew.Door + 8f)))) continue;
                    candidates.Add((i, share, !crew.AtRest));
                }
                candidates.Sort((a, b) => a.busy != b.busy ? (a.busy ? -1 : 1) : b.share.CompareTo(a.share));
            }
            var keep = new bool[crews.Length];
            int people = 0;
            foreach (var c in candidates)
            {
                int n = crews[c.car].People.Length;
                if (people + n > MaxLive) continue;
                people += n;
                keep[c.car] = true;
            }
            for (int i = 0; i < crews.Length; i++)
            {
                if (crews[i] == null) continue;
                bool isLive = live[i][0] != null;
                if (isLive && !keep[i]) Freeze(i);
            }
            for (int i = 0; i < crews.Length; i++)
            {
                if (crews[i] == null) continue;
                bool isLive = live[i][0] != null;
                if (!isLive && keep[i]) Go(i);
            }
        }

        /// <summary>Car i's crew turn live: each takes a rig from the pool, and their still figure hides.</summary>
        private void Go(int i)
        {
            GarageCrew crew = crews[i];
            for (int k = 0; k < crew.People.Length; k++)
            {
                Live l = pool.Count > 0 ? pool.Pop() : NewRig();
                l.Rig.Root.SetActive(true);
                Paint(l.Skin, kits[i]);
                Place(l.Rig.Root.transform, crew, crew.People[k]);
                Pose(l, crew.People[k]);
                live[i][k] = l;
                figureRenderers[i][k].enabled = false;
            }
        }

        /// <summary>Car i's crew go back to still figures, their rigs back to the pool.</summary>
        private void Freeze(int i)
        {
            GarageCrew crew = crews[i];
            for (int k = 0; k < crew.People.Length; k++)
            {
                Live l = live[i][k];
                live[i][k] = null;
                l.Rig.Root.SetActive(false);
                pool.Push(l);
                figureRenderers[i][k].enabled = true;
                Place(figures[i][k].transform, crew, crew.People[k]);
                figures[i][k].sharedMesh = stills[(int)FigureFor(crew.People[k])];
            }
        }

        private Live NewRig()
        {
            HqCrewRig rig = body.Build(transform, 0, true);
            rig.Root.name = "Live crew " + ++built;
            var skin = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.sharedMaterials = materials;
            return new Live { Rig = rig, Skin = skin, LeftHand = rig.Find("LeftHand"), RightHand = rig.Find("RightHand") };
        }
    }
}
