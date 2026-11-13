using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The podium on the race offices' front, built with the pit complex and standing there all race
    /// (<see cref="PodiumBuilder"/>), and the ceremonies played on it (<see cref="PodiumShow"/>). A ceremony's people are
    /// Team HQ's crew body as live rigs: the drivers in their race suits in their team's colours, the presenters in
    /// charcoal and white, the crews below in their team's kit. Each is posed from the show's clips, then their arms
    /// reach for the trophy or the champagne (<see cref="Reach"/>). The camera is the show's while a ceremony plays.
    /// </summary>
    public sealed class Podium : MonoBehaviour
    {
        private static readonly Color Charcoal = new Color(0.12f, 0.125f, 0.14f), SuitWhite = new Color(0.93f, 0.93f, 0.92f);
        private static readonly Color Gold = new Color(0.83f, 0.66f, 0.27f), Silver = new Color(0.78f, 0.79f, 0.81f), Bronze = new Color(0.62f, 0.4f, 0.22f);
        private static readonly int ClothId = Shader.PropertyToID("_Cloth"), PanelId = Shader.PropertyToID("_Panel");

        public PodiumPlan Plan { get; private set; }
        /// <summary>The pit lane's surface below the podium, over the garage floor there, m.</summary>
        public float LaneHeight { get; private set; }
        public IReadOnlyList<(Vector3 at, int place)> CrowdSpots => crowd;

        /// <summary>True while a ceremony plays.</summary>
        public bool Playing => show != null;

        private List<(Vector3 at, int place)> crowd;
        private HqCrewBody body;
        private Material[] kitMaterials;
        private int[] kitParts;
        private Material gilt, plinth, bottleGlass, foil, steel, sprayMaterial, confettiMaterial;
        private readonly Stack<Rig> pool = new Stack<Rig>();
        private readonly List<Rig> cast = new List<Rig>();
        private readonly List<(HqCrewBody.Clip, float, float)> layers = new List<(HqCrewBody.Clip, float, float)>();
        private readonly Transform[] trophies = new Transform[3], bottles = new Transform[3], buckets = new Transform[3];
        private readonly ParticleSystem[] sprays = new ParticleSystem[3];
        private ParticleSystem confetti;
        private PodiumSound sound;
        private readonly float[] sprayLevels = new float[3];
        private readonly Vector3[] mouths = new Vector3[3];
        private PodiumShow show;
        private float clock;
        private bool confettiFired;

        private sealed class Rig
        {
            public HqCrewRig Body;
            public SkinnedMeshRenderer Skin;
            public PodiumActor Actor;
            public Transform LeftArm, LeftForeArm, LeftHand, RightArm, RightForeArm, RightHand;
            /// <summary>Each hand's finger joints, knuckle to tip, finger by finger, then the thumb's.</summary>
            public Transform[] LeftFingers, RightFingers;
        }

        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };

        private static Transform[] FingersOf(HqCrewRig rig, string side)
        {
            var joints = new List<Transform>();
            foreach (string f in Fingers)
                for (int j = 1; j <= 3; j++) joints.Add(rig.Find(side + "Hand" + f + j));
            for (int j = 2; j <= 3; j++) joints.Add(rig.Find(side + "Hand" + "Thumb" + j));
            return joints.ToArray();
        }

        // ---------------------------------------------------------------- building

        /// <summary>Builds the podium on the race offices, its fixed meshes drawn with the pit building's concrete and its own.</summary>
        public static Podium Build(PitComplex complex, PitLane lane, PitMaterials materials, Transform parent)
        {
            var go = new GameObject("Podium");
            go.transform.SetParent(parent, false);
            Podium podium = go.AddComponent<Podium>();
            podium.Make(complex, lane, materials);
            return podium;
        }

        private void Make(PitComplex complex, PitLane lane, PitMaterials pit)
        {
            Plan = PodiumPlan.On(complex);
            crowd = PodiumPlan.Crowd(lane, Plan.Frame, complex);
            float lanes = 0f;
            foreach ((Vector3 at, _) in crowd) lanes += at.y;
            LaneHeight = crowd.Count > 0 ? lanes / crowd.Count : 0f;

            steel = Make("Podium steel", new Color(0.2f, 0.21f, 0.23f), 0.55f, 0.7f);
            Material step = Make("Podium steps", new Color(0.9f, 0.9f, 0.88f), 0.45f, 0f);
            Material board = Make("Podium board", new Color(0.09f, 0.16f, 0.34f), 0.35f, 0f);
            Material chequer = Make("Podium chequer", new Color(0.92f, 0.92f, 0.9f), 0.3f, 0f);
            Material door = Make("Podium doorway", new Color(0.012f, 0.012f, 0.014f), 0.1f, 0f);
            Material lamp = Make("Podium lamps", Color.white, 0.3f, 0f);
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor(Lit.EmissionColorId, new Color(1.2f, 1.15f, 1.05f));
            var glass = Make("Podium glass", new Color(0.6f, 0.68f, 0.72f, 0.22f), 0.95f, 0f);
            Lit.Glass(glass);
            Material[] bands = { Make("Podium gold", Gold, 0.75f, 1f), Make("Podium silver", Silver, 0.75f, 1f), Make("Podium bronze", Bronze, 0.7f, 1f) };
            var byName = new Dictionary<string, Material>
            {
                ["Podium slab"] = pit?.Concrete ?? step, ["Podium steel"] = steel, ["Podium glass"] = glass, ["Podium steps"] = step,
                ["Podium board"] = board, ["Podium chequer"] = chequer, ["Podium doorway"] = door, ["Podium lamps"] = lamp,
                ["Podium band 1"] = bands[0], ["Podium band 2"] = bands[1], ["Podium band 3"] = bands[2],
            };
            foreach ((string name, MeshKit kit) in new PodiumBuilder(Plan).Build().Parts())
            {
                if (kit.IsEmpty) continue;
                var part = new GameObject(name);
                part.transform.SetParent(transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh(name);
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterial = byName[name];
                r.shadowCastingMode = name == "Podium glass" || name == "Podium lamps" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }

            gilt = Make("Trophy gilt", Gold, 0.85f, 1f);
            plinth = Make("Trophy plinth", new Color(0.04f, 0.04f, 0.045f), 0.6f, 0f);
            bottleGlass = Make("Champagne bottle", new Color(0.03f, 0.09f, 0.05f), 0.92f, 0.1f);
            foil = Make("Champagne foil", new Color(0.86f, 0.7f, 0.3f), 0.7f, 1f);
            Mesh stand = BucketStand(), bucket = Simple(PodiumBuilder.BucketMesh(), "Ice bucket");
            for (int p = 0; p < 3; p++)
            {
                (MeshKit g, MeshKit b) = PodiumBuilder.TrophyMesh(PodiumShow.TrophyScale(p));
                trophies[p] = Prop("Trophy " + (p + 1), Two(g, b, "Trophy"), new[] { gilt, plinth });
                (MeshKit bg, MeshKit bf) = PodiumBuilder.BottleMesh();
                bottles[p] = Prop("Champagne " + (p + 1), Two(bg, bf, "Champagne"), new[] { bottleGlass, foil });
                buckets[p] = Prop("Ice bucket " + (p + 1), stand, new[] { steel });
                Transform b2 = Prop("Bucket", bucket, new[] { Make("Ice bucket steel", new Color(0.72f, 0.73f, 0.75f), 0.85f, 1f) });
                b2.SetParent(buckets[p], false);
                b2.localPosition = Vector3.up * (PodiumPlan.BucketTop - 0.22f);
                sprays[p] = Spray(p);
            }
            confetti = Confetti();
            Vector3 centre = Vector3.zero;
            foreach ((Vector3 at, _) in crowd) centre += at;
            sound = gameObject.AddComponent<PodiumSound>();
            sound.Make(Plan.Frame, crowd.Count > 0 ? centre / crowd.Count : new Vector3(0f, 0f, 18f));
            Hide();
        }

        private static Material Make(string name, Color colour, float smooth, float metal)
        {
            var m = new Material(Lit.Shader) { name = name, color = colour, enableInstancing = true };
            m.SetFloat(Lit.SmoothnessId, smooth);
            m.SetFloat(Lit.MetallicId, metal);
            return m;
        }

        private static Mesh Simple(MeshKit kit, string name) => kit.ToMesh(name);

        private static Mesh BucketStand() => PodiumBuilder.BucketStand().ToMesh("Ice bucket stand");

        /// <summary>Two kits as one mesh with a submesh each.</summary>
        private static Mesh Two(MeshKit a, MeshKit b, string name)
        {
            Mesh ma = a.ToMesh(name + " a"), mb = b.ToMesh(name + " b");
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = ma, transform = Matrix4x4.identity }, new CombineInstance { mesh = mb, transform = Matrix4x4.identity } }, false, false);
            mesh.RecalculateBounds();
            Destroy(ma);
            Destroy(mb);
            return mesh;
        }

        private Transform Prop(string name, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        // ---------------------------------------------------------------- the champagne and the confetti

        private Material SprayMaterial(bool paper)
        {
            Shader shader = Resources.Load<Shader>("Podium/Spray");
            var m = new Material(shader != null ? shader : Lit.Shader) { name = paper ? "Confetti" : "Champagne spray" };
            m.SetFloat("_Paper", paper ? 1f : 0f);
            return m;
        }

        private ParticleSystem Spray(int place)
        {
            if (sprayMaterial == null) sprayMaterial = SprayMaterial(false);
            ParticleSystem ps = System("Champagne spray " + (place + 1), sprayMaterial);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 8.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.gravityModifier = 0.7f;
            main.maxParticles = 1500;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 0.86f, 0.75f), new Color(0.96f, 0.88f, 0.62f, 0.55f));
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.008f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            return ps;
        }

        private ParticleSystem Confetti()
        {
            confettiMaterial = SprayMaterial(true);
            ParticleSystem ps = System("Confetti", confettiMaterial);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.035f, 0.05f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.018f, 0.028f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.gravityModifier = 0.08f;
            main.maxParticles = 4000;
            var colours = new Gradient();
            colours.mode = GradientMode.Fixed;
            colours.SetKeys(new[]
            {
                new GradientColorKey(Gold, 0.3f), new GradientColorKey(SuitWhite, 0.55f), new GradientColorKey(new Color(0.8f, 0.1f, 0.12f), 0.75f),
                new GradientColorKey(Silver, 1f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(colours) { mode = ParticleSystemGradientMode.RandomColor };
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(PodiumPlan.HalfWidth * 2f - 0.6f, 0.1f, 0.3f);
            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            drag.limit = 0.9f;
            drag.dampen = 0.15f;
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;
            PodiumFrame f = Plan.Frame;
            ps.transform.SetPositionAndRotation(f.At(0f, PodiumPlan.Floor + PodiumPlan.CanopyHeight - 0.4f, PodiumPlan.CanopyFront - 0.1f),
                Quaternion.LookRotation(f.Dir(0f, 0.55f, 0.85f).normalized, Vector3.up));
            return ps;
        }

        private ParticleSystem System(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
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

        // ---------------------------------------------------------------- playing

        /// <summary>
        /// Starts a ceremony: the show, and each place's team colour (winner first). Builds the people it needs, reusing
        /// those of a ceremony before.
        /// </summary>
        public void Play(PodiumShow ceremony, Color[] colours)
        {
            Stop();
            if (body == null)
            {
                body = HqCrewBody.Load(PitCrews.BodyId);
                if (body == null) return;
                kitMaterials = PitCrews.KitMaterials(body, out kitParts);
            }
            show = ceremony;
            clock = 0f;
            confettiFired = false;
            foreach (PodiumActor a in show.Actors)
            {
                Rig r = pool.Count > 0 ? pool.Pop() : NewRig();
                r.Actor = a;
                r.Body.Root.SetActive(true);
                r.Body.Root.transform.localScale = Vector3.one * a.Size;
                Dress(r, a, colours[a.Place]);
                cast.Add(r);
            }
            for (int p = 0; p < 3; p++)
            {
                bool used = show.Drivers[p] > 0;
                trophies[p].gameObject.SetActive(used);
                bottles[p].gameObject.SetActive(used);
                buckets[p].gameObject.SetActive(used);
                if (used) buckets[p].SetPositionAndRotation(Plan.Frame.At(PodiumPlan.Bucket(p, show.Drivers[p])), Quaternion.identity);
            }
            if (sound != null) sound.Begin();
            Step(0f);
        }

        /// <summary>Moves the ceremony on by dt seconds. False once it is over.</summary>
        public bool Advance(float dt)
        {
            if (show == null) return false;
            clock += dt;
            if (clock >= PodiumShow.Length) return false;
            Step(clock);
            return true;
        }

        /// <summary>The camera's shot now, in the world.</summary>
        public (Vector3 eye, Vector3 look, float fov) Shot()
        {
            PodiumShot s = (show ?? Empty).Shot(clock, LaneHeight);
            return (Plan.Frame.At(s.Eye), Plan.Frame.At(s.Look), s.Fov);
        }

        private static readonly PodiumShow Empty = new PodiumShow(new[] { 0, 0, 0 }, null);

        /// <summary>Ends the ceremony: its people put away, the props and the champagne hidden.</summary>
        public void Stop()
        {
            foreach (Rig r in cast)
            {
                r.Body.Root.SetActive(false);
                pool.Push(r);
            }
            cast.Clear();
            show = null;
            if (sound != null) sound.Stop();
            Hide();
        }

        private void Hide()
        {
            for (int p = 0; p < 3; p++)
            {
                if (trophies[p] != null) trophies[p].gameObject.SetActive(false);
                if (bottles[p] != null) bottles[p].gameObject.SetActive(false);
                if (buckets[p] != null) buckets[p].gameObject.SetActive(false);
                if (sprays[p] != null)
                {
                    ParticleSystem.EmissionModule e = sprays[p].emission;
                    e.rateOverTime = 0f;
                    sprays[p].Clear();
                }
            }
            if (confetti != null)
            {
                ParticleSystem.EmissionModule e = confetti.emission;
                e.rateOverTime = 0f;
                confetti.Clear();
            }
        }

        private Rig NewRig()
        {
            HqCrewRig rig = body.Build(transform, 0, true);
            rig.Root.name = "Podium person " + (cast.Count + pool.Count + 1);
            var skin = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.sharedMaterials = kitMaterials;
            return new Rig
            {
                Body = rig, Skin = skin,
                LeftArm = rig.Find("LeftArm"), LeftForeArm = rig.Find("LeftForeArm"), LeftHand = rig.Find("LeftHand"),
                RightArm = rig.Find("RightArm"), RightForeArm = rig.Find("RightForeArm"), RightHand = rig.Find("RightHand"),
                LeftFingers = FingersOf(rig, "Left"), RightFingers = FingersOf(rig, "Right"),
            };
        }

        /// <summary>Paints a person: a driver's race suit, a presenter's charcoal and white, a crew member's team kit.</summary>
        private void Dress(Rig r, PodiumActor a, Color team)
        {
            MaterialPropertyBlock kit;
            if (a.Role == PodiumRole.Driver) kit = PitCrews.DriverKit(team).suit;
            else if (a.Role == PodiumRole.Crew) kit = PitCrews.KitBlock(team);
            else
            {
                kit = new MaterialPropertyBlock();
                kit.SetColor(ClothId, Charcoal);
                kit.SetColor(PanelId, SuitWhite);
            }
            foreach (int s in kitParts) r.Skin.SetPropertyBlock(kit, s);
        }

        private void Step(float t)
        {
            PodiumFrame f = Plan.Frame;
            int crew = 0, cheering = 0;
            foreach (Rig r in cast)
            {
                PodiumActor a = r.Actor;
                PodiumPose pose = show.Pose(a, t);
                if (a.Role == PodiumRole.Crew)
                {
                    crew++;
                    if (pose.Clip == "Cheer") cheering++;
                }
                // Drivers wait out of sight inside the doorway until they walk out.
                bool shown = a.Role != PodiumRole.Driver || t >= show.StartOf(a);
                if (r.Body.Root.activeSelf != shown) r.Body.Root.SetActive(shown);
                if (!shown) continue;
                Vector3 face = f.Dir(pose.Face);
                r.Body.Root.transform.SetPositionAndRotation(f.At(pose.At), Quaternion.LookRotation(-face, Vector3.up));
                layers.Clear();
                if (pose.UnderWeight > 0f && pose.Under != null) layers.Add((body.Clips[pose.Under], ClipTime(pose.Under, pose.UnderTime, pose.UnderLoop), pose.UnderWeight));
                layers.Add((body.Clips[pose.Clip], ClipTime(pose.Clip, pose.ClipTime, pose.Loop), 1f - pose.UnderWeight));
                r.Body.Pose(layers);
                Vector3 right = Vector3.Cross(Vector3.up, face).normalized;
                if (pose.LeftWeight > 0f)
                    Hold(r.LeftArm, r.LeftForeArm, r.LeftHand, r.LeftFingers, false, f.At(pose.LeftHand), f.Dir(pose.LeftGrip), pose.LeftBar, face, -right, pose.LeftWeight);
                if (pose.RightWeight > 0f)
                    Hold(r.RightArm, r.RightForeArm, r.RightHand, r.RightFingers, true, f.At(pose.RightHand), f.Dir(pose.RightGrip), pose.RightBar, face, right, pose.RightWeight);
            }

            for (int p = 0; p < 3; p++)
            {
                if (show.Drivers[p] <= 0) continue;
                (Vector3 tb, Vector3 side) = show.Trophy(p, t);
                Vector3 sw = f.Dir(side).normalized;
                trophies[p].SetPositionAndRotation(f.At(tb), Quaternion.LookRotation(Vector3.Cross(sw, Vector3.up), Vector3.up));
                (Vector3 bb, Vector3 axis) = show.Bottle(p, t);
                bottles[p].SetPositionAndRotation(f.At(bb), Quaternion.FromToRotation(Vector3.up, f.Dir(axis).normalized));
                ParticleSystem.EmissionModule e = sprays[p].emission;
                sprayLevels[p] = -1f;
                if (show.Spray(p, t, out Vector3 mouth, out Vector3 aim))
                {
                    mouths[p] = mouth;
                    sprayLevels[p] = 1f - Mathf.Clamp01((t - PodiumShow.PopAt) / (PodiumShow.SprayEnd - PodiumShow.PopAt));
                    sprays[p].transform.SetPositionAndRotation(f.At(mouth), Quaternion.LookRotation(f.Dir(aim).normalized, Vector3.up));
                    // A burst as the cork goes, then a steady stream that thins as the bottle empties.
                    float since = t - PodiumShow.PopAt;
                    e.rateOverTime = since < 0.4f ? 1400f : Mathf.Lerp(520f, 160f, since / (PodiumShow.SprayEnd - PodiumShow.PopAt));
                }
                else e.rateOverTime = 0f;
            }
            if (!confettiFired && t >= PodiumShow.ConfettiAt)
            {
                confettiFired = true;
                confetti.Emit(3200);
            }
            if (sound != null)
            {
                // Before the spray, the corks pop from where the bottles are.
                for (int p = 0; p < 3; p++)
                    if (sprayLevels[p] < 0f && show.Drivers[p] > 0)
                    {
                        (Vector3 bb, Vector3 axis) = show.Bottle(p, t);
                        mouths[p] = bb + axis * PodiumShow.BottleLength;
                    }
                sound.Step(show, t, crew > 0 ? (float)cheering / crew : 0f, sprayLevels, mouths);
            }
        }

        private float ClipTime(string clip, float t, bool loop)
        {
            float d = body.Clips[clip].Duration;
            return loop ? Mathf.Repeat(t, d) : Mathf.Clamp(t, 0f, d);
        }

        /// <summary>The fingers' local axis that runs knuckle to tip, and the one that runs out of the palm (the bones' own).</summary>
        private static readonly Vector3 Along = Vector3.up, PalmOut = new Vector3(0f, 0f, PalmSign);
        /// <summary>Which way the palm faces along the hand's local z: the sign that turns a positive curl about x towards it.</summary>
        public const float PalmSign = -1f;
        /// <summary>Where the middle of the palm is from the wrist, along the fingers, m.</summary>
        private const float PalmLength = 0.075f;
        /// <summary>How far each joint of a finger bends closing round a bar, knuckle to tip, degrees, round a thin bar and a bottle.</summary>
        private static readonly float[] CurlThin = { 70f, 85f, 60f }, CurlThick = { 45f, 60f, 45f };

        /// <summary>
        /// A hand taking hold of a bar at a point: the hand turned so the bar runs across its palm, out past the thumb,
        /// the arm reaching to put the palm on the bar, and the fingers closed round it. With no bar, the arm just
        /// reaches the wrist to the point.
        /// </summary>
        private static void Hold(Transform upper, Transform lower, Transform hand, Transform[] fingers, bool rightHand, Vector3 at, Vector3 bar, float radius,
            Vector3 face, Vector3 outward, float weight)
        {
            if (hand == null) return;
            if (bar.sqrMagnitude < 1e-6f)
            {
                Reach(upper, lower, hand, at, Pole(face, outward, at, upper.position), weight);
                return;
            }
            bar.Normalize();
            // The thumb's side of the hand: the index finger's, along the hand's local x (+x on the right hand).
            Vector3 thumbSide = hand.rotation * (rightHand ? Vector3.right : Vector3.left);
            Quaternion held = Quaternion.FromToRotation(thumbSide, bar) * hand.rotation;
            // The wrist sits back from the bar by the palm's length and the bar's radius off the palm.
            Vector3 palm = held * (Along * PalmLength + PalmOut * (radius + 0.012f));
            Vector3 wrist = at - palm;
            Reach(upper, lower, hand, wrist, Pole(face, outward, wrist, upper.position), weight);
            hand.rotation = Quaternion.Slerp(hand.rotation, held, weight);
            float[] curl = radius > 0.025f ? CurlThick : CurlThin;
            for (int i = 0; i < fingers.Length; i++)
            {
                Transform j = fingers[i];
                if (j == null) continue;
                bool thumb = i >= 12;
                float angle = thumb ? 30f : curl[i % 3];
                j.localRotation *= Quaternion.AngleAxis(angle * PalmSign * weight, Vector3.right);
            }
        }

        /// <summary>The way an elbow points: down and out to its side, and out to the side more as the hand goes up.</summary>
        private static Vector3 Pole(Vector3 face, Vector3 outward, Vector3 target, Vector3 shoulder)
        {
            float high = Mathf.Clamp01((target.y - shoulder.y + 0.1f) / 0.4f);
            return (Vector3.down * (1f - high) + outward * (0.6f + 0.6f * high) - face * 0.25f).normalized;
        }

        /// <summary>
        /// Turns an arm so its wrist reaches the target, the elbow bent towards the pole, blended over the clip's pose by
        /// weight. Out of reach, the arm points straight at it.
        /// </summary>
        public static void Reach(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float weight)
        {
            if (upper == null || lower == null || end == null || weight <= 0f) return;
            Quaternion upper0 = upper.localRotation, lower0 = lower.localRotation;
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float la = (b - a).magnitude, lb = (c - b).magnitude;
            Vector3 toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
            Vector3 dir = toTarget.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(b - a, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = a + (dir * cosA + bend * sinA) * la;
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            Vector3 b2 = lower.position, c2 = end.position;
            lower.rotation = Quaternion.FromToRotation(c2 - b2, a + dir * d - b2) * lower.rotation;
            if (weight >= 1f) return;
            Quaternion upper1 = upper.localRotation, lower1 = lower.localRotation;
            upper.localRotation = Quaternion.Slerp(upper0, upper1, weight);
            lower.localRotation = Quaternion.Slerp(lower0, lower1, weight);
        }
    }
}
