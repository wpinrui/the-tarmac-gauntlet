using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// The crew in Team HQ. One planner hands out tasks so the mechanics behave like a crew: coffee at the cart,
    /// writing at the desk, checking a wheel, a chat between two of them. Every spot is booked before anyone walks
    /// to it. A chat is only offered when two people are free in the same area, and the talk starts once both stand
    /// face to face. The first six people work the bench area in the crew shot; the rest work the other bays.
    ///
    /// A straight port of the approved viewer's crew.js (Team HQ viewer): the numbers are the same and the planner
    /// works in the viewer's frame (right-handed, +Z towards the bay doors). Only the people's roots, the props and the
    /// hips reads cross into Unity's frame, where Z is flipped and a yaw turns the other way.
    /// </summary>
    public sealed class HqCrew : MonoBehaviour
    {
        public const int Max = 16;

        private const float R = Mathf.Deg2Rad;
        private static readonly string[] BodyIds = { "crew_overalls", "crew_suit", "crew_vest" };
        private static readonly float[] BodySpeed = { 1.28f, 1.56f, 1.355f };   // m/s at clip rate 1, so the feet don't slide
        private const int Colourways = 6;

        // Props this places, in the viewer's frame: id, x, z, yaw.
        private static readonly (string id, float x, float z, float yaw)[] Props =
            { ("coffee_cart", -10.2f, -3.45f, 0), ("desk", -12.5f, -2.05f, 90), ("stool", -11.32f, -2.05f, 0) };
        private static readonly Vector3 Clipboard = new Vector3(-12.2f, 0.79f, -2.05f);   // on the desk, under the writer's hands

        private sealed class Zone
        {
            public (string kind, float x, float z, float yaw, int bay)[] Spots;   // bay -1: no car needed
            public (float x, float z)[][] Chats;
            public bool[] CartChat;
            public (float x, float z)[] Start;
        }

        // Areas. A wheel check needs a car in its bay (0 to 3, left to right); a chat is two spots that face each other.
        private static readonly Zone[] Zones =
        {
            new Zone   // bench area, in the crew shot
            {
                Spots = new[] { ("desk", -11.8f, -2.05f, -90f, -1), ("coffee", -9.75f, -2.45f, 200f, -1), ("wheel", -9.7f, 2.75f, 90f, 0), ("wheel", -9.7f, 0.25f, 90f, 0) },
                Chats = new[] { C(-10.75f, -2.5f, -10.2f, -1.8f), C(-11.35f, 0.55f, -10.45f, 0.95f), C(-11.25f, 2.55f, -10.35f, 2.2f) },
                CartChat = new[] { true, false, false },
                Start = new[] { (-11.4f, -0.8f), (-10.3f, -0.3f), (-11f, 1.6f), (-10.2f, 3f), (-11.5f, 3.1f), (-10.6f, -1.1f) },
            },
            new Zone   // between the first and second bays
            {
                Spots = new[] { ("wheel", -6.31f, 2.75f, -90f, 0), ("wheel", -6.31f, 0.25f, -90f, 0), ("wheel", -4.75f, 3.05f, 90f, 1), ("wheel", -4.75f, -0.05f, 90f, 1), ("chest", -4f, -5.95f, 180f, -1) },
                Chats = new[] { C(-6f, 4.7f, -5.1f, 4.9f), C(-6f, -1.7f, -5.1f, -1.4f) },
                CartChat = new[] { false, false },
                Start = new[] { (-5.5f, 1.5f), (-5.6f, -3f), (-5.4f, 5.3f) },
            },
            new Zone   // between the second and third bays
            {
                Spots = new[] { ("wheel", -1.25f, 3.05f, -90f, 1), ("chest", 1f, -5.95f, 180f, -1) },
                Chats = new[] { C(-0.9f, 4.7f, 0f, 4.9f), C(-0.8f, -1.6f, 0.1f, -1.3f) },
                CartChat = new[] { false, false },
                Start = new[] { (-0.4f, 1.5f), (-0.4f, -3.2f), (-0.5f, 5.4f) },
            },
            new Zone   // between the third and fourth bays
            {
                Spots = new[] { ("wheel", 5.31f, 2.75f, 90f, 3), ("wheel", 5.31f, 0.25f, 90f, 3), ("chest", 6f, -5.95f, 180f, -1) },
                Chats = new[] { C(3.9f, 4.7f, 4.8f, 4.9f), C(4f, -1.7f, 4.9f, -1.4f) },
                CartChat = new[] { false, false },
                Start = new[] { (4.4f, 1.5f), (4.5f, -3.2f), (4.3f, 5.4f) },
            },
            new Zone   // by the tyre stack
            {
                Spots = new[] { ("wheel", 8.69f, 2.75f, -90f, 3), ("wheel", 8.69f, 0.25f, -90f, 3), ("chest", 12f, -5.9f, 90f, -1), ("chest", 11.25f, -2.9f, 90f, -1) },
                Chats = new[] { C(9.4f, 4.7f, 10.3f, 4.9f), C(9.5f, -1.5f, 10.4f, -1.2f) },
                CartChat = new[] { false, false },
                Start = new[] { (10f, 1.5f), (10.2f, -3.5f), (10f, 5.4f) },
            },
        };

        private const int Shot = 6;                       // people in the bench area before the rest spill into the other bays
        private static readonly int[] Spill = { 1, 3, 2, 4 };   // order the other bays fill in
        private const float Pace = 0.85f;                 // walk playback rate; ground speed follows it so the feet don't slide
        private const float Turn = 2.6f;                  // rad/s
        private const float StepTurn = 0.3f;              // turns bigger than this (rad) at a spot are stepped with the walk, smaller ones settle while standing
        private const float Pivot = 4.5f, PivotErr = 0.6f;   // a change of direction bigger than PivotErr (rad) is a quick stepping turn at Pivot rad/s
        private const float Fade = 0.3f;                  // s to blend between clips
        private const float Scoot = 0.35f, ScootT = 0.8f; // the writer pulls the stool in this far once seated
        private const float Seat = 0.1f;                  // hips sit this far behind the stool's centre
        private static readonly Vector3 Mouth = new Vector3(0, 0.04f, 0.16f);   // the lips in front of the head bone, standing upright (viewer frame)
        private const float MugOut = 0.05f, Tip = 1.0f;   // mug centre this far off the palm; at the sip its top tips this far (rad) from upright towards the lips
        // Ivan's hand-set turn of the mug about its own centre and its shift (m, in the solved pose's frame), in the viewer's frame.
        private static readonly Quaternion MugFixViewer = new Quaternion(-0.10316162884972883f, 0.13716457658889902f, -0.5012971963964927f, 0.8480829429644707f);
        private static readonly Vector3 MugOffViewer = new Vector3(-0.011349015735991633f, 0.03253054694209387f, 0.03251097287797525f);
        private static readonly string[] Talk = { "TalkA", "TalkB", "TalkC" };

        /// <summary>Clips that carry the hips away from the feet; the root follows them.</summary>
        private static bool Moves(string clip) => clip.Contains("Sit") || clip.Contains("Crouch") || clip.Contains("Write") || clip.Contains("Cheer");

        private enum Mode { Idle, Walk, Face, ChatWait, Act }

        private sealed class Spot
        {
            public int Zone, Bay;
            public string Kind;
            public float X, Z, Yaw;
            public Person By;
        }

        private sealed class ChatEnd
        {
            public float X, Z, Yaw;
        }

        private sealed class ChatPlace
        {
            public int Zone;
            public bool Cart;
            public ChatEnd[] Ends;
            public Person[] By;
        }

        private sealed class Chat
        {
            public ChatPlace C;
            public Person[] Men;
            public int Turns;
            public Person Talker;
        }

        private sealed class Task
        {
            public string Kind;
            public Spot Spot;
            public Chat Chat;
        }

        private sealed class Step
        {
            public string Clip;
            public bool Loop, Reverse, Mug;
            public float Dur = -1, Scoot, ScootEnd;
        }

        private sealed class Layer
        {
            public HqCrewBody.Clip Clip;
            public float Time, Rate, W, To;
            public bool Loop;
        }

        private sealed class Current
        {
            public string Name;
            public Layer Lay;
            public float Len;
        }

        private sealed class Body
        {
            public HqCrewBody Data;
            public float Speed;
            public HqCrewRig Probe;
            public readonly Dictionary<string, Vector2> Hip = new Dictionary<string, Vector2>();
            public Vector3 MugPos;
            public Quaternion MugRot;
            public Quaternion[] Reach;   // forearm and arm turns that put the rim on the lips at the sip
            public float Rest, Peak;
        }

        private sealed class Person
        {
            public int I, Zone;
            public Body Body;
            public HqCrewRig Rig;
            public Transform Hips, Hand, Arm, Fore;
            public float S, Pace;
            public float X, Z, Yaw;   // viewer frame
            public readonly List<Layer> Layers = new List<Layer>();
            public Current Cur;
            public Vector2? Shift;
            public Mode Mode = Mode.Idle;
            public float Wait, StepT, FadeT = Fade;
            public Task Task;
            public readonly List<Step> Steps = new List<Step>();
            public Step Step;
            public string Last, LastTalk;
            public (float x, float z, float yaw) Goal;
            public (Chat chat, ChatEnd e)? Pending;
            public GameObject Mug;
        }

        private readonly List<Body> bodies = new List<Body>();
        private readonly List<Person> men = new List<Person>();
        private readonly List<Spot> spots = new List<Spot>();
        private readonly List<ChatPlace> chats = new List<ChatPlace>();
        private Transform stool;
        private Vector2 stoolHome, stoolAt;
        private Mesh mugMesh;
        private Material mugMaterial;
        private uint seed = 7;
        private bool ready;
        private int wanted = 6;

        /// <summary>Whether a bay (0 to 3, left to right) has a car standing in it. Without it, bays 0, 1 and 3 do, as in the viewer.</summary>
        public Func<int, bool> HasCar;

        /// <summary>How many people work in the building, 0 to 16.</summary>
        public int Count
        {
            get => wanted;
            set
            {
                wanted = Mathf.Clamp(value, 0, Max);
                if (ready) SetCount(wanted);
            }
        }

        private static (float x, float z)[] C(float ax, float az, float bx, float bz) => new[] { (ax, az), (bx, bz) };

        /* ---------- Frames ---------- */

        // The viewer's frame and Unity's differ by the sign of Z, so a yaw turns the other way.
        private static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);
        private static Quaternion YawToUnity(float yaw) => Quaternion.Euler(0, -yaw * Mathf.Rad2Deg, 0);
        private static Quaternion ToUnity(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

        private Vector2 ViewerXZ(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            return new Vector2(p.x, -p.z);
        }

        /* ---------- Random ---------- */

        // The viewer's generator, so both pick the same tasks in the same order.
        private float Rnd()
        {
            seed = seed * 1664525u + 1013904223u;
            return seed / 4294967296f;
        }

        private float Between(float a, float b) => a + Rnd() * (b - a);

        private static float Wrap(float a) => Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a));

        /* ---------- Setup ---------- */

        private void Start()
        {
            for (int b = 0; b < BodyIds.Length; b++)
            {
                HqCrewBody data = HqCrewBody.Load(BodyIds[b]);
                if (data == null) return;
                var body = new Body { Data = data, Speed = BodySpeed[b] };
                body.Probe = data.Build(transform, 0, false);
                body.Probe.Root.SetActive(false);
                bodies.Add(body);
            }
            foreach (Body b in bodies) Calibrate(b);

            for (int zi = 0; zi < Zones.Length; zi++)
            {
                Zone z = Zones[zi];
                foreach (var s in z.Spots) spots.Add(new Spot { Zone = zi, Kind = s.kind, X = s.x, Z = s.z, Yaw = s.yaw * R, Bay = s.bay });
                for (int c = 0; c < z.Chats.Length; c++)
                {
                    var (a, b) = (z.Chats[c][0], z.Chats[c][1]);
                    float yaw = Mathf.Atan2(b.x - a.x, b.z - a.z);
                    chats.Add(new ChatPlace
                    {
                        Zone = zi, Cart = z.CartChat[c],
                        Ends = new[] { new ChatEnd { X = a.x, Z = a.z, Yaw = yaw }, new ChatEnd { X = b.x, Z = b.z, Yaw = yaw + Mathf.PI } },
                    });
                }
            }

            foreach (var p in Props)
            {
                GameObject o = HqProps.Place(transform, new HqProp { Id = p.id, Position = ToUnity(p.x, 0, p.z), Yaw = -p.yaw, Scale = 1 });
                if (p.id == "stool" && o != null)
                {
                    stool = o.transform;
                    stoolHome = stoolAt = new Vector2(p.x, p.z);
                }
            }
            HqProps.Place(transform, new HqProp { Id = "clipboard", Position = ToUnity(Clipboard.x, Clipboard.y, Clipboard.z), Yaw = 90, Scale = 1 });
            ready = true;
            SetCount(wanted);
        }

        private bool CarIn(int bay) => HasCar != null ? HasCar(bay) : bay != 2;

        /* ---------- Drinking ---------- */

        /// <summary>
        /// For each body, at the peak of the sip: the mug's place in the hand is set so it tips its rim towards the lips,
        /// then a small forearm and arm correction (a few CCD passes) puts the rim on the mouth. The clip's own motion is kept.
        /// Worked in Unity's frame on the body's spare rig; the results are local to the hand and the arm bones.
        /// </summary>
        private void Calibrate(Body body)
        {
            HqCrewRig p = body.Probe;
            Transform hand = p.Find("LeftHand"), head = p.Find("Head"), arm = p.Find("LeftArm"), fore = p.Find("LeftForeArm");
            HqCrewBody.Clip drink = body.Data.Clips["Drink"];
            float rest = 0, peak = -1, pt = 0;
            bool first = true;
            for (float t = 0; t <= drink.Duration; t += 0.05f)
            {
                PoseAt(p, drink, t);
                float y = hand.position.y;
                if (first) { rest = y; first = false; }
                if (y > peak) { peak = y; pt = t; }
            }
            // the mouth, kept in the head bone's frame so it follows the head as it tips back
            PoseAt(p, drink, 0);
            Vector3 mouth = head.InverseTransformPoint(head.position + ToUnity(Mouth.x, Mouth.y, Mouth.z));
            PoseAt(p, drink, pt);
            var g = new GameObject("mug probe").transform;
            g.SetParent(hand, false);
            g.localScale = Vector3.one / hand.lossyScale.x;
            var rim = new GameObject("rim").transform;
            rim.SetParent(g, false);
            rim.localPosition = new Vector3(0, 0.0475f, 0);
            // mug body against the fingers on the palm side, handle back in the palm, top tipped towards the lips
            Vector3 Place()
            {
                Quaternion qh = hand.rotation;
                Vector3 h = hand.position, fing = qh * Vector3.up, aim = head.TransformPoint(mouth);
                // the palm side is the side that faces the mouth at the sip
                Vector3 palmN = aim - h;
                palmN = (palmN - fing * Vector3.Dot(palmN, fing)).normalized;
                Vector3 c = h + fing * 0.07f + palmN * MugOut;
                Vector3 toFace = aim - c;
                toFace.y = 0;
                toFace.Normalize();
                Vector3 ay = (Vector3.up * Mathf.Cos(Tip) + toFace * Mathf.Sin(Tip)).normalized;
                Vector3 ax = -palmN;
                ax = (ax - ay * Vector3.Dot(ax, ay)).normalized;
                Vector3 az = Vector3.Cross(ax, ay);
                g.rotation = Quaternion.LookRotation(az, ay);
                g.position = c;
                return aim;
            }
            Transform[] bones = { fore, arm };
            Quaternion[] d = { Quaternion.identity, Quaternion.identity };
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 aim = Place();
                for (int it = 0; it < 8; it++)
                    for (int k = 0; k < bones.Length; k++)
                    {
                        Transform b = bones[k];
                        Vector3 bp = b.position;
                        Quaternion par = b.parent.rotation;
                        Quaternion qw = Quaternion.FromToRotation(rim.position - bp, aim - bp);
                        Quaternion ql = Quaternion.Inverse(par) * qw * par;
                        b.localRotation = ql * b.localRotation;
                        d[k] = ql * d[k];
                    }
            }
            body.MugPos = g.localPosition;
            body.MugRot = g.localRotation;
            body.Reach = d;
            body.Rest = rest;
            body.Peak = peak;
            Destroy(g.gameObject);
        }

        private readonly List<(HqCrewBody.Clip, float, float)> one = new List<(HqCrewBody.Clip, float, float)>(1);

        private void PoseAt(HqCrewRig rig, HqCrewBody.Clip clip, float t)
        {
            one.Clear();
            one.Add((clip, t, 1));
            rig.Pose(one);
        }

        /// <summary>Hips position (x, z) in the body's own frame (viewer's) at time t of a clip.</summary>
        private Vector2 HipAt(Body body, string name, float t)
        {
            string key = name + "@" + t.ToString("F2");
            if (body.Hip.TryGetValue(key, out Vector2 v)) return v;
            PoseAt(body.Probe, body.Data.Clips[name], t);
            Transform root = body.Probe.Root.transform;
            Vector3 h = root.InverseTransformPoint(body.Probe.Find("Hips").position);
            return body.Hip[key] = new Vector2(h.x, -h.z);
        }

        private static Vector2 Local(Person m, float x, float z)
        {
            float c = Mathf.Cos(m.Yaw), s = Mathf.Sin(m.Yaw);
            return new Vector2((x * c + z * s) * m.S, (-x * s + z * c) * m.S);
        }

        /// <summary>Turns the drinking arm the rest of the way to the lips, in step with how high the hand is in the sip.</summary>
        private static void Reach(Person m)
        {
            Body d = m.Body;
            if (!CurIs(m, "Drink")) return;
            float w = Mathf.Clamp01((m.Hand.position.y / m.S - d.Rest) / (d.Peak - d.Rest));
            w = w * w * (3 - 2 * w) * m.Cur.Lay.W;
            m.Fore.localRotation = Quaternion.Slerp(Quaternion.identity, d.Reach[0], w) * m.Fore.localRotation;
            m.Arm.localRotation = Quaternion.Slerp(Quaternion.identity, d.Reach[1], w) * m.Arm.localRotation;
        }

        private void SetMug(Person m, bool on)
        {
            if (!on)
            {
                if (m.Mug != null) Destroy(m.Mug);
                m.Mug = null;
                return;
            }
            if (mugMesh == null) BuildMug();
            var o = new GameObject("Mug");
            o.transform.SetParent(m.Hand, false);
            float scale = 1 / m.Hand.lossyScale.x * m.S;
            o.transform.localScale = Vector3.one * scale;
            o.transform.localRotation = m.Body.MugRot * ToUnity(MugFixViewer).normalized;
            Vector3 off = ToUnity(MugOffViewer.x, MugOffViewer.y, MugOffViewer.z);
            o.transform.localPosition = m.Body.MugPos + m.Body.MugRot * off * scale;
            o.AddComponent<MeshFilter>().sharedMesh = mugMesh;
            MeshRenderer r = o.AddComponent<MeshRenderer>();
            r.sharedMaterial = mugMaterial;
            r.shadowCastingMode = ShadowCastingMode.On;
            m.Mug = o;
        }

        /// <summary>A plain mug: a tapered cup with a half-ring handle on +X, centred on the origin, Y up.</summary>
        private void BuildMug()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            const int seg = 20;
            const float rTop = 0.04f, rBot = 0.036f, h = 0.095f;
            // wall
            Vector3 slope = new Vector3(0, rTop - rBot, 0);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Vector3 nrm = (dir * h + Vector3.up * (rBot - rTop)).normalized;
                v.Add(dir * rTop + Vector3.up * h / 2); n.Add(nrm);
                v.Add(dir * rBot - Vector3.up * h / 2); n.Add(nrm);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 2;
                Tri(t, v, n, a, a + 1, b);
                Tri(t, v, n, b, a + 1, b + 1);
            }
            // top and bottom
            foreach (int side in new[] { 1, -1 })
            {
                int c = v.Count;
                v.Add(Vector3.up * side * h / 2); n.Add(Vector3.up * side);
                float r = side > 0 ? rTop : rBot;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2 / seg;
                    v.Add(new Vector3(Mathf.Sin(a) * r, side * h / 2, Mathf.Cos(a) * r)); n.Add(Vector3.up * side);
                }
                for (int i = 0; i < seg; i++) Tri(t, v, n, c, c + 1 + i, c + 2 + i);
            }
            // handle: half a ring in the XY plane, from the top of the cup round to the bottom on +X
            const int ru = 16, rv = 8;
            const float ring = 0.025f, tube = 0.008f;
            int s0 = v.Count;
            for (int i = 0; i <= ru; i++)
            {
                float u = Mathf.PI / 2 - i * Mathf.PI / ru;   // +Y round through +X to -Y
                Vector3 centre = new Vector3(Mathf.Cos(u) * ring + 0.04f, Mathf.Sin(u) * ring, 0);
                Vector3 outward = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);
                for (int j = 0; j <= rv; j++)
                {
                    float w = j * Mathf.PI * 2 / rv;
                    Vector3 nrm = outward * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w);
                    v.Add(centre + nrm * tube); n.Add(nrm);
                }
            }
            for (int i = 0; i < ru; i++)
                for (int j = 0; j < rv; j++)
                {
                    int a = s0 + i * (rv + 1) + j, b = a + rv + 1;
                    Tri(t, v, n, a, b, a + 1);
                    Tri(t, v, n, a + 1, b, b + 1);
                }
            mugMesh = new Mesh { name = "mug" };
            mugMesh.SetVertices(v);
            mugMesh.SetNormals(n);
            mugMesh.SetTriangles(t, 0);
            mugMesh.RecalculateBounds();
            mugMaterial = new Material(Lit.Shader) { name = "mug", color = new Color(0.949f, 0.937f, 0.910f) };
            mugMaterial.SetFloat("_Smoothness", 0.65f);
        }

        /// <summary>Adds a triangle wound so its front faces along its vertex normals (clockwise in Unity).</summary>
        private static void Tri(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0) (b, c) = (c, b);
            t.Add(a); t.Add(b); t.Add(c);
        }

        /* ---------- People ---------- */

        private Person MakeMan(int i)
        {
            Body body = bodies[i % bodies.Count];
            int cw = i / 3 % Colourways;
            int zone = i < Shot ? 0 : Spill[(i - Shot) % Spill.Length];
            var m = new Person { I = i, Body = body, Zone = zone };
            m.S = Between(0.96f, 1.04f);
            m.Pace = Between(0.95f, 1.05f);
            m.Wait = 0.3f + i * 0.45f + Rnd() * 1.2f;
            m.Rig = body.Data.Build(transform, cw, true);
            m.Rig.Root.name = "Crew " + i;
            m.Hips = m.Rig.Find("Hips");
            m.Hand = m.Rig.Find("LeftHand");
            m.Arm = m.Rig.Find("LeftArm");
            m.Fore = m.Rig.Find("LeftForeArm");
            int taken = 0;
            foreach (Person n in men) if (n.Zone == zone) taken++;
            var starts = Zones[zone].Start;
            var st = starts[taken % starts.Length];
            m.X = st.x + (taken >= starts.Length ? 0.6f : 0);
            m.Z = st.z;
            m.Yaw = Between(-1, 1) * Mathf.PI;
            Root(m);
            Play(m, "Idle", loop: true, from: Rnd() * body.Data.Clips["Idle"].Duration, fade: 0);
            return m;
        }

        private void Root(Person m)
        {
            Transform t = m.Rig.Root.transform;
            t.localPosition = ToUnity(m.X, 0, m.Z);
            t.localRotation = YawToUnity(m.Yaw);
            t.localScale = Vector3.one * m.S;
        }

        /* ---------- Clips: blend between them and keep the hips where they are ---------- */

        private void Play(Person m, string name, bool loop = false, float from = 0, bool reverse = false, float rate = 1, float fade = Fade, float dur = -1)
        {
            HqCrewBody.Clip clip = m.Body.Data.Clips[name];
            float t0 = reverse ? clip.Duration : from;
            // carry over any move the last blend had not finished
            float sx = 0, sz = 0;
            if (m.Shift.HasValue)
            {
                float k = 1 - m.Cur.Lay.W;
                sx = m.Shift.Value.x * k;
                sz = m.Shift.Value.y * k;
            }
            // after a clip that carries the hips away from the feet (sitting, crouching), move the root so the new clip starts with the hips where they are now
            if (m.Cur != null && Moves(m.Cur.Name))
            {
                Root(m);
                Vector2 w = ViewerXZ(m.Hips.position);
                Vector2 hip = HipAt(m.Body, name, t0), l = Local(m, hip.x, hip.y);
                sx = w.x - (m.X + l.x);
                sz = w.y - (m.Z + l.y);
            }
            m.Shift = sx != 0 || sz != 0 ? new Vector2(sx, sz) : (Vector2?)null;
            foreach (Layer old in m.Layers) old.To = 0;
            m.Layers.RemoveAll(old => old.Clip == clip);
            var lay = new Layer { Clip = clip, Time = t0, Rate = (reverse ? -1 : 1) * rate, Loop = loop, W = fade > 0 ? 0 : 1, To = 1 };
            m.Layers.Add(lay);
            m.FadeT = fade > 0 ? fade : 1e-3f;
            m.Cur = new Current { Name = name, Lay = lay, Len = dur >= 0 ? dur : (reverse ? clip.Duration : clip.Duration - from) / rate };
            if (fade <= 0 && m.Shift.HasValue)
            {
                m.X += m.Shift.Value.x;
                m.Z += m.Shift.Value.y;
                m.Shift = null;
            }
        }

        private readonly List<(HqCrewBody.Clip, float, float)> pose = new List<(HqCrewBody.Clip, float, float)>();

        private static void Blend(Person m, float dt)
        {
            foreach (Layer l in m.Layers)
            {
                float before = l.W;
                l.W += Mathf.Sign(l.To - l.W) * Mathf.Min(Mathf.Abs(l.To - l.W), dt / m.FadeT);
                if (m.Shift.HasValue && l == m.Cur.Lay)
                {
                    float k = l.W - before;
                    m.X += m.Shift.Value.x * k;
                    m.Z += m.Shift.Value.y * k;
                    if (l.W >= 1) m.Shift = null;
                }
            }
            m.Layers.RemoveAll(l => l.To == 0 && l.W <= 0);
        }

        /// <summary>Moves each clip on by dt (looping or holding its last frame) and poses the rig from them.</summary>
        private void Animate(Person m, float dt)
        {
            pose.Clear();
            foreach (Layer l in m.Layers)
            {
                float d = l.Clip.Duration;
                l.Time += dt * l.Rate;
                if (l.Loop) { if (d > 0) { l.Time %= d; if (l.Time < 0) l.Time += d; } }
                else l.Time = Mathf.Clamp(l.Time, 0, d);
                pose.Add((l.Clip, l.Time, l.W));
            }
            m.Rig.Pose(pose);
        }

        private static bool CurIs(Person m, string name) => m.Cur != null && m.Cur.Name == name;

        /* ---------- Planner ---------- */

        // nobody else is standing there (someone may still be finishing up on a spot that was just let go)
        private bool Clear(Person m, float x, float z)
        {
            foreach (Person n in men)
                if (n != m && n.Mode != Mode.Walk && Mathf.Sqrt((n.X - x) * (n.X - x) + (n.Z - z) * (n.Z - z)) < 0.7f) return false;
            return true;
        }

        private bool Open(Person m, Spot s) => s.By == null && (s.Bay < 0 || CarIn(s.Bay)) && Clear(m, s.X, s.Z);

        private Spot Book(Person m, string kind)
        {
            var free = spots.FindAll(s => s.Zone == m.Zone && s.Kind == kind && Open(m, s));
            if (free.Count == 0) return null;
            Spot pick = free[(int)Mathf.Floor(Rnd() * free.Count)];
            pick.By = m;
            return pick;
        }

        private void Release(Person m)
        {
            foreach (Spot s in spots) if (s.By == m) s.By = null;
            foreach (ChatPlace c in chats) if (c.By != null && Array.IndexOf(c.By, m) >= 0) c.By = null;
        }

        private static float Dist(Person a, float x, float z) => Mathf.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

        private static readonly (string kind, float weight)[] Kinds = { ("wheel", 2), ("desk", 2), ("coffee", 1.5f), ("chest", 1.5f) };

        private void Plan(Person m)
        {
            var opts = new List<(string kind, float weight)>();
            var kinds = new HashSet<string>();
            foreach (Spot s in spots) if (s.Zone == m.Zone && Open(m, s)) kinds.Add(s.Kind);
            foreach (var k in Kinds) if (kinds.Contains(k.kind) && m.Last != k.kind) opts.Add(k);
            // a partner is free now, or busy with something else and joins once done
            var partners = men.FindAll(n => n != m && n.Zone == m.Zone && n.Last != "chat" && !n.Pending.HasValue && (n.Mode == Mode.Idle || (n.Task != null && n.Task.Kind != "chat")));
            var pairs = chats.FindAll(c => c.Zone == m.Zone && c.By == null && Clear(m, c.Ends[0].X, c.Ends[0].Z) && Clear(m, c.Ends[1].X, c.Ends[1].Z));
            if (m.Last != "chat" && partners.Count > 0 && pairs.Count > 0) opts.Add(("chat", 3));
            if (opts.Count == 0)
            {
                m.Wait = Between(1, 2);
                return;
            }
            float total = 0;
            foreach (var o in opts) total += o.weight;
            float r = Rnd() * total;
            string pick = opts[0].kind;
            foreach (var o in opts)
                if ((r -= o.weight) <= 0) { pick = o.kind; break; }
            Release(m);
            if (pick == "chat")
            {
                // the nearest free partner, and the pair of spots that suits them best
                Person p = partners[0];
                foreach (Person n in partners)
                {
                    bool ni = n.Mode == Mode.Idle, pi = p.Mode == Mode.Idle;
                    if (ni != pi ? ni : Dist(n, m.X, m.Z) < Dist(p, m.X, m.Z)) p = n;
                }
                ChatPlace best = null;
                int bf = 0;
                float bd = 0;
                foreach (ChatPlace c in pairs)
                    for (int f = 0; f < 2; f++)
                    {
                        float d = Dist(m, c.Ends[f].X, c.Ends[f].Z) + Dist(p, c.Ends[1 - f].X, c.Ends[1 - f].Z);
                        if (best == null || d < bd) { best = c; bf = f; bd = d; }
                    }
                if (p.Mode == Mode.Idle) Release(p);
                best.By = new[] { m, p };
                var chat = new Chat { C = best, Men = new[] { m, p }, Turns = (int)Mathf.Floor(Between(3, 6)) };
                for (int k = 0; k < 2; k++)
                {
                    Person n = chat.Men[k];
                    ChatEnd e = best.Ends[k == 1 ? 1 - bf : bf];
                    if (n.Mode == Mode.Idle)
                    {
                        n.Task = new Task { Kind = "chat", Chat = chat };
                        n.Last = "chat";
                        Go(n, e.X, e.Z, e.Yaw);
                    }
                    else n.Pending = (chat, e);
                }
                return;
            }
            Spot sp = Book(m, pick);
            m.Task = new Task { Kind = pick, Spot = sp };
            m.Last = pick;
            if (pick == "desk")
            {
                Vector2 hip = HipAt(m.Body, "Sit", m.Body.Data.Clips["Sit"].Duration);
                // stand where the sit lands the hips on the stool
                float c = Mathf.Cos(sp.Yaw), sn = Mathf.Sin(sp.Yaw), bx = stoolAt.x - sn * Seat, bz = stoolAt.y - c * Seat;
                Go(m, bx - (hip.x * c + hip.y * sn) * m.S, bz - (-hip.x * sn + hip.y * c) * m.S, sp.Yaw);
            }
            else Go(m, sp.X, sp.Z, sp.Yaw);
        }

        private void Go(Person m, float x, float z, float yaw)
        {
            m.Goal = (x, z, yaw);
            m.Mode = Mode.Walk;
            if (!CurIs(m, "Walk")) Play(m, "Walk", loop: true, rate: Pace * m.Pace);
        }

        // What each task does once its person stands on the spot
        private List<Step> Script(Person m)
        {
            var w = new List<Step>();
            switch (m.Task.Kind)
            {
                case "wheel":
                    w.Add(new Step { Clip = Rnd() < 0.3f ? "CrouchDownSlow" : "CrouchDown" });
                    w.Add(new Step { Clip = "Crouch", Loop = true, Dur = Between(5, 9) });
                    w.Add(new Step { Clip = Rnd() < 0.5f ? "CrouchUpA" : "CrouchUpB" });
                    break;
                case "chest":
                    w.Add(new Step { Clip = "Idle", Loop = true, Dur = Between(4, 7) });
                    break;
                case "coffee":
                    w.Add(new Step { Clip = "Idle", Loop = true, Dur = 1.5f });
                    w.Add(new Step { Clip = "Drink", Mug = true });
                    w.Add(new Step { Clip = "Idle", Loop = true, Dur = 1 });
                    break;
                case "desk":
                    w.Add(new Step { Clip = "Sit" });
                    w.Add(new Step { Clip = "Write", Loop = true, Dur = Between(5, 8), Scoot = -Scoot });
                    if (Rnd() < 0.5f)
                    {
                        w.Add(new Step { Clip = "Cheer" });
                        w.Add(new Step { Clip = "Write", Loop = true, Dur = Between(3, 5) });
                    }
                    else w.Add(new Step { Clip = "Write", Loop = true, Dur = Between(3, 5) });
                    w[w.Count - 1].ScootEnd = Scoot;
                    w.Add(new Step { Clip = "Sit", Reverse = true });
                    break;
            }
            return w;
        }

        private void Next(Person m)
        {
            SetMug(m, false);
            if (m.Steps.Count == 0)
            {
                Finish(m);
                return;
            }
            Step st = m.Steps[0];
            m.Steps.RemoveAt(0);
            m.Step = st;
            m.StepT = 0;
            Play(m, st.Clip, loop: st.Loop, reverse: st.Reverse, dur: st.Dur);
            if (st.Mug) SetMug(m, true);
        }

        private void Finish(Person m)
        {
            if (m.Task != null && m.Task.Kind == "desk") ResetStool();
            m.Mode = Mode.Idle;
            m.Task = null;
            m.Step = null;
            m.Wait = Between(1, 3);
            if (!CurIs(m, "Idle")) Play(m, "Idle", loop: true);
            // someone is waiting to chat
            if (m.Pending.HasValue)
            {
                var (chat, e) = m.Pending.Value;
                m.Pending = null;
                Release(m);
                chat.C.By = chat.Men;
                m.Task = new Task { Kind = "chat", Chat = chat };
                m.Last = "chat";
                Go(m, e.X, e.Z, e.Yaw);
            }
        }

        // Chats: both stand face to face, one talks while the other nods, and they swap now and then
        private void ChatTick(Chat chat)
        {
            Person a = chat.Men[0], b = chat.Men[1];
            if (chat.Talker == null)
            {
                if (a.Mode != Mode.ChatWait || b.Mode != Mode.ChatWait) return;
                chat.Talker = Rnd() < 0.5f ? a : b;
                Say(chat);
                return;
            }
            Person t = chat.Talker, l = t == a ? b : a;
            if (t.StepT >= t.Cur.Len - Fade)
            {
                if (--chat.Turns <= 0)
                {
                    chat.C.By = null;
                    Finish(a);
                    Finish(b);
                    return;
                }
                if (Rnd() < 0.65f) chat.Talker = l;
                Say(chat);
                return;
            }
            // the listener nods, rests, nods again
            if (l.StepT >= l.Cur.Len)
            {
                l.StepT = 0;
                if (CurIs(l, "Nod")) Play(l, "Idle", loop: true, dur: Between(1.2f, 2.5f));
                else Play(l, "Nod");
            }
        }

        private void Say(Chat chat)
        {
            Person t = chat.Talker, l = chat.Men[0] == t ? chat.Men[1] : chat.Men[0];
            string name;
            do name = chat.C.Cart && Rnd() < 0.4f ? "Talk" : Talk[(int)Mathf.Floor(Rnd() * Talk.Length)];
            while (name == t.LastTalk && name != "Talk");
            t.LastTalk = name;
            t.StepT = 0;
            l.StepT = 0;
            if (name == "Talk") Play(t, "Talk", loop: true, from: Between(0, 30), dur: Between(6, 9));
            else Play(t, name);
            Play(l, "Nod");
        }

        /* ---------- Per frame ---------- */

        private void StepMan(Person m, float dt)
        {
            if (m.Mode == Mode.Idle)
            {
                m.Wait -= dt;
                if (m.Wait <= 0) Plan(m);
            }
            if (m.Mode == Mode.Walk)
            {
                var g = m.Goal;
                float dx = g.x - m.X, dz = g.z - m.Z, d = Mathf.Sqrt(dx * dx + dz * dz);
                float head = Mathf.Atan2(dx, dz), slow = 1;
                // give way: steer round anyone standing ahead, and let the earlier person through first
                foreach (Person n in men)
                {
                    if (n == m) continue;
                    float ox = n.X - m.X, oz = n.Z - m.Z, od = Mathf.Sqrt(ox * ox + oz * oz);
                    if (od > 1 || od > d + 0.3f) continue;
                    float ah = Mathf.Cos(Mathf.Atan2(ox, oz) - head);
                    if (ah < 0.3f) continue;
                    float side = Wrap(Mathf.Atan2(ox, oz) - head) > 0 ? -1 : 1;
                    head += side * Mathf.Min(1.2f, 1.6f * (1 - od));
                    if (od < 0.75f && (n.Mode != Mode.Walk || n.I < m.I)) slow = Mathf.Min(slow, od < 0.5f ? 0.2f : 0.5f);
                }
                // a big change of direction is a quick stepping turn: the feet keep stepping and the body swings round faster than the steps really would
                float err = Wrap(head - m.Yaw);
                bool pivot = Mathf.Abs(err) > PivotErr;
                m.Yaw += Mathf.Sign(err) * Mathf.Min(Mathf.Abs(err), (pivot ? Pivot : Turn) * dt);
                err = Wrap(head - m.Yaw);
                float v = Mathf.Min(m.Body.Speed * Pace * m.Pace * m.S * m.Cur.Lay.W * slow * (pivot ? 0.15f : Mathf.Max(0, Mathf.Cos(err))), d / Mathf.Max(dt, 1e-3f));
                m.X += Mathf.Sin(m.Yaw) * v * dt;
                m.Z += Mathf.Cos(m.Yaw) * v * dt;
                // a big turn left at the spot is taken in small steps; a small one just settles
                if (d < 0.06f)
                {
                    m.Mode = Mode.Face;
                    bool big = Mathf.Abs(Wrap(m.Goal.yaw - m.Yaw)) > StepTurn;
                    if (!big) Play(m, "Idle", loop: true);
                }
            }
            else if (m.Mode == Mode.Face || m.Mode == Mode.ChatWait)
            {
                float err = Wrap(m.Goal.yaw - m.Yaw);
                bool stepping = CurIs(m, "Walk");
                m.Yaw += Mathf.Sign(err) * Mathf.Min(Mathf.Abs(err), (stepping ? Pivot : 0.6f) * dt);
                if (stepping && Mathf.Abs(err) < 0.08f) Play(m, "Idle", loop: true);
                if (Mathf.Abs(err) < 0.03f && !stepping && m.Mode == Mode.Face)
                {
                    if (m.Task.Kind == "chat") m.Mode = Mode.ChatWait;
                    else
                    {
                        m.Mode = Mode.Act;
                        m.Steps.Clear();
                        m.Steps.AddRange(Script(m));
                        Next(m);
                    }
                }
            }
            if (m.Mode == Mode.Act && m.Step != null)
            {
                m.StepT += dt;
                Step st = m.Step;
                // the writer scoots the stool in after sitting, and back out before standing
                float s0 = 0, s1 = 0, amount = 0;
                bool scoot = false;
                if (st.Scoot != 0) { s0 = 0; s1 = ScootT; amount = st.Scoot; scoot = true; }
                else if (st.ScootEnd != 0) { s0 = m.Cur.Len - ScootT; s1 = m.Cur.Len; amount = st.ScootEnd; scoot = true; }
                if (scoot && m.StepT > s0 && m.StepT - dt < s1)
                {
                    float k = (Mathf.Min(m.StepT, s1) - Mathf.Max(m.StepT - dt, s0)) / (s1 - s0), dd = amount * k;
                    float ddx = Mathf.Sin(m.Yaw) * -dd, ddz = Mathf.Cos(m.Yaw) * -dd;
                    m.X += ddx;
                    m.Z += ddz;
                    stoolAt += new Vector2(ddx, ddz);
                    PlaceStool();
                }
                if (m.StepT >= m.Cur.Len - (m.Steps.Count > 0 ? Fade * 0.5f : 0)) Next(m);
            }
            if (m.Mode == Mode.ChatWait || (m.Task != null && m.Task.Kind == "chat")) m.StepT += dt;
            Blend(m, dt);
            Root(m);
            Animate(m, dt);
            Reach(m);
        }

        private readonly List<Chat> talking = new List<Chat>();

        private void Update()
        {
            if (!ready) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0, 0.1f);
            foreach (Person m in men) StepMan(m, dt);
            talking.Clear();
            foreach (Person m in men)
                if (m.Task != null && m.Task.Kind == "chat" && !talking.Contains(m.Task.Chat)) talking.Add(m.Task.Chat);
            foreach (Chat c in talking) ChatTick(c);
        }

        /* ---------- Crew size ---------- */

        private void SetCount(int n)
        {
            while (men.Count > n)
            {
                Person m = men[men.Count - 1];
                men.RemoveAt(men.Count - 1);
                Release(m);
                // a chat partner left on their own goes back to work
                foreach (Person o in men.ToArray())
                {
                    if (o.Pending.HasValue && Array.IndexOf(o.Pending.Value.chat.Men, m) >= 0) o.Pending = null;
                    if (o.Task != null && o.Task.Kind == "chat" && Array.IndexOf(o.Task.Chat.Men, m) >= 0)
                    {
                        o.Task.Chat.C.By = null;
                        Finish(o);
                    }
                }
                if (m.Task != null && m.Task.Kind == "desk") ResetStool();
                Destroy(m.Rig.Root);
            }
            while (men.Count < n) men.Add(MakeMan(men.Count));
        }

        private void ResetStool()
        {
            stoolAt = stoolHome;
            PlaceStool();
        }

        private void PlaceStool()
        {
            if (stool != null) stool.localPosition = ToUnity(stoolAt.x, 0, stoolAt.y);
        }
    }
}
