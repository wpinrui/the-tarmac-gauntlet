using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>A place to hang a floodlight: its lens, the point its bracket fixes to, and the spot it aims at.</summary>
    public readonly struct LampMount
    {
        public readonly Vector3 Position, Anchor, Target;

        public LampMount(Vector3 position, Vector3 anchor, Vector3 target)
        {
            Position = position;
            Anchor = anchor;
            Target = target;
        }
    }

    public enum MainStandShape { Cantilever, Barrel, Open }
    public enum CornerStandShape { Bleachers, Terrace, Covered }
    public enum StandColours { Green, Blue, Cream }

    /// <summary>The stands' look in the race.</summary>
    public static class StandLook
    {
        public const MainStandShape Main = MainStandShape.Cantilever;
        public const CornerStandShape Corners = CornerStandShape.Covered;
        public const StandColours Colours = StandColours.Blue;
    }

    /// <summary>Where a stand goes, how many rows it has and how full it is.</summary>
    public sealed class StandSite
    {
        public string Name;
        /// <summary>The corner it watches, or 0 for the start line.</summary>
        public int Corner;
        /// <summary>The start line's stand, opposite the pits.</summary>
        public bool Main;
        /// <summary>On the inside of the corner rather than the outside.</summary>
        public bool Inside;
        /// <summary>How far it reaches before the corner (or the start line) and past its end, metres.</summary>
        public float Before, After;
        public int Rows;
        /// <summary>Share of the seats taken.</summary>
        public float Fill;
    }

    /// <summary>The ground a stand and its fence take up, for other scenery to keep off.</summary>
    public readonly struct StandFootprint
    {
        /// <summary>0 = left of the track, 1 = right.</summary>
        public readonly int Side;
        /// <summary>Lap distances where it starts and ends, metres, running forwards (To may be past the line).</summary>
        public readonly float From, To;
        /// <summary>Distance from the centreline to the back of the stand, metres.</summary>
        public readonly float Far;

        public StandFootprint(int side, float from, float to, float far) { Side = side; From = from; To = to; Far = far; }

        /// <summary>True when a point at this lap distance and lateral offset (left positive) is on the footprint.</summary>
        public bool Covers(float lap, float distance, float lateral) =>
            (Side == 0 ? lateral > 0 : lateral < 0) && Mathf.Abs(lateral) < Far && Mathf.Repeat(distance - From, lap) <= Mathf.Repeat(To - From, lap);
    }

    /// <summary>
    /// Grandstands on the main straight opposite the pits and at the big corners, a modest seated crowd and catch
    /// fencing behind the armco in front of them. Everything is placed from the circuit data: each stand sits a
    /// walkway behind the furthest armco along its stretch, its tiers following the ground. Nothing here is solid,
    /// so racing never touches it. The crowd is one mesh per area, each fan one card (<see cref="CrowdCard"/>) that the crowd
    /// shader turns to the camera and draws a seated fan on, two triangles a fan.
    /// </summary>
    public sealed class Grandstands
    {
        public static readonly StandSite[] Sites =
        {
            new StandSite { Name = "Main grandstand", Main = true, Before = 200f, After = 150f, Rows = 14, Fill = 0.24f },
            new StandSite { Name = "Turn 1 stand", Corner = 1, Inside = true, Before = 70f, After = 35f, Rows = 9, Fill = 0.14f },
            new StandSite { Name = "Turn 7 stand", Corner = 7, Before = 45f, After = 55f, Rows = 9, Fill = 0.11f },
            new StandSite { Name = "Turn 10 stand", Corner = 10, Before = 75f, After = 60f, Rows = 9, Fill = 0.12f },
            new StandSite { Name = "Turn 12 stand", Corner = 12, Inside = true, Before = 85f, After = 40f, Rows = 9, Fill = 0.15f },
        };

        // Cross-section, metres.
        private const float Walkway = 3f;        // armco to the stand's front wall
        private const float FenceBack = 0.8f;    // catch fence stands this far behind the armco
        private const float FenceHeight = 4f;
        private const float FenceLean = 1f;      // top section leans this far towards the track
        private const float FenceOverhang = 15f; // fence carries on past each end of a stand
        private const float PostSpacing = 4f;
        private const float Tread = 0.85f;
        private const float Rise = 0.42f;
        private const float FrontWall = 1.6f;
        private const float Skirt = 0.5f;        // walls reach this far into the ground
        private const float Bay = 12f;           // roof columns
        private const float Aisle = 1.2f;
        private const float BlockLength = 22f;   // seats between aisles
        private const float SeatSpacing = 0.5f;
        private const float Station = 4f;        // tiers are flat between stations this far apart

        public readonly MainStandShape MainShape;
        public readonly CornerStandShape CornerShape;
        public readonly List<StandFootprint> Footprints = new List<StandFootprint>();
        /// <summary>How many fans were seated.</summary>
        public int Fans { get; private set; }
        /// <summary>Where floodlights hang under the front edge of each covered stand's roof, one a bay, aimed at the track.</summary>
        public readonly List<LampMount> RoofLamps = new List<LampMount>();

        private readonly Circuit c;
        private readonly CircuitBuilder track;
        private readonly StandMaterials m;
        private readonly List<CircuitPart> parts = new List<CircuitPart>();
        private readonly MeshKit concrete = new MeshKit(), steel = new MeshKit(), cladding = new MeshKit(), seats = new MeshKit(),
            roof = new MeshKit(), bank = new MeshKit(), crowd = new MeshKit(), mesh = new MeshKit(), posts = new MeshKit();

        /// <param name="track">Built already, so the ground has settled and the armco is where it will be.</param>
        /// <param name="materials">May be null outside Unity (checks and previews).</param>
        public Grandstands(Circuit circuit, CircuitBuilder track, StandMaterials materials, MainStandShape main, CornerStandShape corner)
        {
            c = circuit;
            this.track = track;
            m = materials;
            MainShape = main;
            CornerShape = corner;
        }

        public List<CircuitPart> Build()
        {
            parts.Clear();
            RoofLamps.Clear();
            for (int i = 0; i < Sites.Length; i++) Stand(Sites[i], i);
            Emit("Stand concrete", concrete, m?.Concrete, true);
            Emit("Stand steel", steel, m?.Steel, true);
            Emit("Stand cladding", cladding, m?.Cladding, true);
            Emit("Stand seats", seats, m?.Seats, true);
            Emit("Stand roofs", roof, m?.Roof, true);
            Emit("Stand banks", bank, m?.Bank, true);
            Emit("Crowd", crowd, m?.Crowd, false);
            Emit("Catch fence", mesh, m?.FenceMesh, false);
            Emit("Catch fence posts", posts, m?.FencePosts, false);
            return parts;
        }

        private void Emit(string name, MeshKit kit, Material material, bool shadows)
        {
            if (kit.IsEmpty) return;
            foreach (MeshKit chunk in kit.Chunks(150f)) parts.Add(new CircuitPart(name, chunk, material, false, shadows));
        }

        // ---------------------------------------------------------------- placing a stand

        /// <summary>The side a site's stand is on: 0 = left.</summary>
        public int SideOf(StandSite site)
        {
            if (site.Main) return c.Data.pit.side == "left" ? 1 : 0;
            int dir = Corner(site).arcs[0].dir;
            return (dir > 0) == site.Inside ? 1 : 0;   // a right-hander runs wide to the left
        }

        /// <summary>Lap distances where a site's stand starts and ends.</summary>
        public (float from, float to) Span(StandSite site)
        {
            if (site.Main) return (Mathf.Repeat(-site.Before, c.Lap), site.After);
            CornerData corner = Corner(site);
            return (Mathf.Repeat(corner.s - site.Before, c.Lap), Mathf.Repeat(corner.e + site.After, c.Lap));
        }

        private CornerData Corner(StandSite site)
        {
            foreach (CornerData corner in c.Data.corners) if (corner.number == site.Corner) return corner;
            throw new ArgumentException("No corner " + site.Corner);
        }

        /// <summary>Track samples from one lap distance to another, going forwards.</summary>
        private List<int> Samples(float from, float to)
        {
            var ks = new List<int>();
            int k = c.IndexOf(from), end = c.IndexOf(to);
            for (int guard = 0; guard <= c.Count; guard++)
            {
                ks.Add(k);
                if (k == end) break;
                k = c.Wrap(k + 1);
            }
            return ks;
        }

        /// <summary>A stand's front edge in plan, measured along its length, and the ground level its tiers follow.</summary>
        private sealed class Guide
        {
            public Vector3[] Point, Out;
            public float[] S, Base;
            /// <summary>How far the stand's front edge is from the track's centre line, m.</summary>
            public float Front;
            public float Length => S[S.Length - 1];

            public void At(float s, out Vector3 p, out Vector3 o, out float b)
            {
                int i = Array.BinarySearch(S, s);
                if (i < 0) i = ~i - 1;
                i = Mathf.Clamp(i, 0, S.Length - 2);
                float t = Mathf.Clamp01((s - S[i]) / Mathf.Max(1e-4f, S[i + 1] - S[i]));
                p = Vector3.Lerp(Point[i], Point[i + 1], t);
                o = Vector3.Lerp(Out[i], Out[i + 1], t).normalized;
                b = Mathf.Lerp(Base[i], Base[i + 1], t);
            }
        }

        private Guide Line(List<int> ks, int side, float front, Profile profile)
        {
            float sign = side == 0 ? 1f : -1f;
            var g = new Guide { Point = new Vector3[ks.Count], Out = new Vector3[ks.Count], S = new float[ks.Count], Base = new float[ks.Count], Front = front };
            var ground = new float[ks.Count];
            for (int i = 0; i < ks.Count; i++)
            {
                int k = ks[i];
                Vector3 o = c.Left[k] * sign;
                Vector3 p = c.Centre[k] + o * front;
                p.y = 0f;
                g.Point[i] = p;
                g.Out[i] = o;
                if (i > 0) g.S[i] = g.S[i - 1] + Flat(g.Point[i], g.Point[i - 1]);
                // Base level high enough that every tread clears the ground under it.
                ground[i] = c.TerrainHeight(p.x, p.z);
                for (int r = 0; r < profile.Rows; r++)
                    for (float u = profile.U(r); u <= profile.U(r + 1); u += Tread * 0.5f)
                    {
                        Vector3 q = p + o * u;
                        ground[i] = Mathf.Max(ground[i], c.TerrainHeight(q.x, q.z) - profile.V(r) + 0.05f);
                    }
            }
            // The tiers follow the lie of the land along the stand, smoothed over 20 m either way, never lower
            // than the ground needs.
            for (int i = 0; i < ks.Count; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = 0; j < ks.Count; j++)
                    if (Mathf.Abs(g.S[j] - g.S[i]) <= 20f) { sum += ground[j]; count++; }
                g.Base[i] = Mathf.Max(sum / count, ground[i]);
            }
            return g;
        }

        private static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        private void Stand(StandSite site, int index)
        {
            int side = SideOf(site);
            (float from, float to) = Span(site);
            List<int> ks = Samples(from, to);
            float barrier = 0f;
            foreach (int k in ks) barrier = Mathf.Max(barrier, c.HalfWidth(k) + track.RunoffReach(side, k));

            bool roofed;
            Shape shape;
            if (site.Main) { shape = MainShape == MainStandShape.Open ? Shape.Open : Shape.Solid; roofed = MainShape != MainStandShape.Open; }
            else
            {
                shape = CornerShape == CornerStandShape.Bleachers ? Shape.Bleachers : CornerShape == CornerStandShape.Terrace ? Shape.Terrace : Shape.Solid;
                roofed = CornerShape == CornerStandShape.Covered;
            }
            var p = new Profile(site.Rows, shape == Shape.Terrace ? 0.5f : FrontWall, shape == Shape.Terrace ? 0.3f : Rise);
            Guide g = Line(ks, side, barrier + Walkway, p);
            var stand = new Builder(this, g, p);
            stand.Tiers(shape);
            if (roofed)
            {
                if (site.Main && MainShape == MainStandShape.Barrel) stand.BarrelRoof();
                else stand.CantileverRoof();
            }
            stand.Seats(site, index);
            float far = barrier + Walkway + p.Depth + (shape == Shape.Terrace ? stand.BankReach : 1f);
            Footprints.Add(new StandFootprint(side, Mathf.Repeat(from - FenceOverhang, c.Lap), Mathf.Repeat(to + FenceOverhang, c.Lap), far));
            Fence(side, Samples(Mathf.Repeat(from - FenceOverhang, c.Lap), Mathf.Repeat(to + FenceOverhang, c.Lap)));
        }

        private enum Shape { Solid, Open, Bleachers, Terrace }

        /// <summary>
        /// A box with only the faces that can be seen: never its bottom, its top and back only when asked. With a UV
        /// every corner takes it, so the box is one texel of a palette texture.
        /// </summary>
        private static void Block(MeshKit kit, Vector3 centre, Vector3 right, Vector3 forward, Vector3 size, Vector2? uv, bool back, bool top = true, bool sides = true)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + right * (x * h.x) + Vector3.up * (y * h.y) + forward * (z * h.z);
            kit.Face(P(-1, -1, 1), P(1, -1, 1), P(-1, 1, 1), P(1, 1, 1), centre, uv);
            if (back) kit.Face(P(-1, -1, -1), P(1, -1, -1), P(-1, 1, -1), P(1, 1, -1), centre, uv);
            if (sides)
            {
                kit.Face(P(1, -1, -1), P(1, -1, 1), P(1, 1, -1), P(1, 1, 1), centre, uv);
                kit.Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, -1), P(-1, 1, 1), centre, uv);
            }
            if (top) kit.Face(P(-1, 1, -1), P(1, 1, -1), P(-1, 1, 1), P(1, 1, 1), centre, uv);
        }

        /// <summary>A stand's tiers in section: u outwards from the front wall, v up from the stand's base level.</summary>
        private readonly struct Profile
        {
            public readonly int Rows;
            public readonly float Front, Rise;
            public Profile(int rows, float front, float rise) { Rows = rows; Front = front; Rise = rise; }
            public float U(int row) => row * Tread;
            public float V(int row) => Front + row * Rise;
            public float Depth => Rows * Tread;
            public float Top => V(Rows - 1);
        }

        /// <summary>Lays one stand's pieces into the shared kits.</summary>
        private sealed class Builder
        {
            private readonly Grandstands s;
            private readonly Guide g;
            private readonly Profile p;
            private readonly float[] stations;
            public float BankReach;

            public Builder(Grandstands owner, Guide guide, Profile profile)
            {
                s = owner;
                g = guide;
                p = profile;
                int n = Mathf.Max(1, Mathf.CeilToInt(g.Length / Station));
                stations = new float[n + 1];
                for (int i = 0; i <= n; i++) stations[i] = g.Length * i / n;
            }

            /// <summary>A point at distance a along the stand, u out from its front wall and v above its base.</summary>
            private Vector3 P(float a, float u, float v)
            {
                g.At(a, out Vector3 q, out Vector3 o, out float b);
                return q + o * u + Vector3.up * (b + v);
            }

            /// <summary>Height of the ground under a point, above the stand's base there.</summary>
            private float Ground(float a, float u)
            {
                g.At(a, out Vector3 q, out Vector3 o, out float b);
                Vector3 w = q + o * u;
                return s.c.TerrainHeight(w.x, w.z) - b;
            }

            private Vector3 Along(float a)
            {
                Vector3 d = P(Mathf.Min(a + 1f, g.Length), 0, 0) - P(Mathf.Max(a - 1f, 0f), 0, 0);
                d.y = 0;
                return d.normalized;
            }

            private Vector3 Out(float a)
            {
                g.At(a, out _, out Vector3 o, out _);
                return o;
            }

            /// <summary>A strip along the stand between two section points, flat between stations, facing away from a third.</summary>
            private void Band(MeshKit kit, float u0, float v0, float u1, float v1, float ui, float vi, float from = 0f, float to = -1f)
            {
                if (to < 0f) to = g.Length;
                float prev = from;
                foreach (float a in Cuts(from, to))
                {
                    if (a <= prev) continue;
                    float mid = (prev + a) * 0.5f;
                    kit.Face(P(prev, u0, v0), P(a, u0, v0), P(prev, u1, v1), P(a, u1, v1), P(mid, ui, vi));
                    prev = a;
                }
            }

            /// <summary>A wall along the stand at u from the ground up to v, facing away from ui.</summary>
            private void Wall(MeshKit kit, float u, float v, float ui)
            {
                float prev = 0f;
                foreach (float a in stations)
                {
                    if (a <= prev) continue;
                    float mid = (prev + a) * 0.5f;
                    kit.Face(P(prev, u, Ground(prev, u) - Skirt), P(a, u, Ground(a, u) - Skirt), P(prev, u, v), P(a, u, v), P(mid, ui, v * 0.5f));
                    prev = a;
                }
            }

            private IEnumerable<float> Cuts(float from, float to)
            {
                foreach (float a in stations) if (a > from && a < to) yield return a;
                yield return to;
            }

            /// <summary>A quad across an end of the stand, at a, from the ground up to a section edge.</summary>
            private void EndColumn(MeshKit kit, float a, float u0, float u1, float v0, float v1, float inward)
            {
                kit.Face(P(a, u0, Ground(a, u0) - Skirt), P(a, u1, Ground(a, u1) - Skirt), P(a, u0, v0), P(a, u1, v1), P(a + inward, (u0 + u1) * 0.5f, (v0 + v1) * 0.25f));
            }

            public void Tiers(Shape shape)
            {
                if (shape == Shape.Bleachers) { Bleachers(); return; }
                MeshKit body = s.concrete;
                Wall(body, 0f, p.Front, 1f);
                for (int r = 0; r < p.Rows; r++)
                {
                    float u0 = p.U(r), u1 = p.U(r + 1), v = p.V(r);
                    Band(body, u0, v, u1, v, (u0 + u1) * 0.5f, v - 1f);
                    if (r > 0) Band(body, u0, p.V(r - 1), u0, v, u0 + 0.3f, v - 0.3f);
                    // Close both ends, a column of wall under each tread.
                    MeshKit ends = shape == Shape.Terrace ? s.bank : body;
                    EndColumn(ends, 0f, u0, u1, v, v, 1f);
                    EndColumn(ends, g.Length, u0, u1, v, v, -1f);
                }
                float top = p.Top;
                if (shape == Shape.Terrace)
                {
                    // An earth bank falls away behind the top tier at 1 in 1.5.
                    float u = p.Depth;
                    float worst = 0f;
                    foreach (float a in stations) worst = Mathf.Max(worst, top - Ground(a, u));
                    BankReach = worst * 1.5f + 1f;
                    float prev = 0f;
                    foreach (float a in stations)
                    {
                        if (a <= prev) continue;
                        float Foot(float x) => Ground(x, u + BankReach) - Skirt;
                        s.bank.Face(P(prev, u, top), P(a, u, top), P(prev, u + BankReach, Foot(prev)), P(a, u + BankReach, Foot(a)), P((prev + a) * 0.5f, u, top - 2f));
                        prev = a;
                    }
                    s.bank.Face(P(0, u, Ground(0, u) - Skirt), P(0, u + BankReach, Ground(0, u + BankReach) - Skirt), P(0, u, top), P(0, u + BankReach, Ground(0, u + BankReach) - Skirt), P(1f, u, 0f));
                    s.bank.Face(P(g.Length, u, Ground(g.Length, u) - Skirt), P(g.Length, u + BankReach, Ground(g.Length, u + BankReach) - Skirt), P(g.Length, u, top), P(g.Length, u + BankReach, Ground(g.Length, u + BankReach) - Skirt), P(g.Length - 1f, u, 0f));
                    return;
                }
                // Back wall, with a parapet on an open stand.
                float back = p.Depth;
                Wall(body, back, top + (shape == Shape.Open ? 1.1f : 0f), back - 1f);
                if (shape == Shape.Open)
                {
                    Band(body, back - 0.2f, top, back - 0.2f, top + 1.1f, back, top + 0.5f);
                    Band(body, back - 0.2f, top + 1.1f, back, top + 1.1f, back - 0.1f, top);
                    s.concrete.Face(P(0, back - 0.2f, top), P(0, back, top), P(0, back - 0.2f, top + 1.1f), P(0, back, top + 1.1f), P(1f, back - 0.1f, top));
                    s.concrete.Face(P(g.Length, back - 0.2f, top), P(g.Length, back, top), P(g.Length, back - 0.2f, top + 1.1f), P(g.Length, back, top + 1.1f), P(g.Length - 1f, back - 0.1f, top));
                }
            }

            /// <summary>Open steel bleachers: plank treads on a frame of posts and raked beams, a rail along the back.</summary>
            private void Bleachers()
            {
                for (int r = 0; r < p.Rows; r++)
                {
                    float u0 = p.U(r), u1 = p.U(r + 1), v = p.V(r);
                    Band(s.steel, u0, v, u1, v, (u0 + u1) * 0.5f, v - 1f);
                    Band(s.steel, u0, v - 0.06f, u1, v - 0.06f, (u0 + u1) * 0.5f, v + 1f);
                    Band(s.steel, u0, v - 0.06f, u0, v, u0 + 0.3f, v - 0.03f);
                }
                float top = p.Top;
                // A frame every 4 m: a post under the back of each tread, a raked beam under the treads and a brace.
                for (int i = 0; i < stations.Length; i += 2)
                {
                    float a = stations[i];
                    Vector3 along = Along(a), o = Out(a);
                    for (int r = 0; r < p.Rows; r++)
                    {
                        float u = p.U(r + 1) - 0.08f, v = p.V(r) - 0.06f, foot = Ground(a, u) - Skirt;
                        Vector3 bottom = P(a, u, foot), up = P(a, u, v);
                        Block(s.steel, (bottom + up) * 0.5f, along, o, new Vector3(0.1f, (up - bottom).magnitude, 0.1f), null, true, false);
                    }
                    Beam(s.steel, P(a, 0.1f, p.Front - 0.15f), P(a, p.Depth - 0.1f, top - 0.15f), along, 0.12f);
                    Beam(s.steel, P(a, 0.2f, Ground(a, 0.2f)), P(a, p.Depth - 0.2f, top - 0.3f), along, 0.08f);
                }
                // Back rail: a top rail on posts.
                float back = p.Depth - 0.05f;
                Band(s.steel, back, top + 1.0f, back, top + 1.08f, back + 0.5f, top + 1.04f);
                Band(s.steel, back, top + 1.0f, back, top + 1.08f, back - 0.5f, top + 1.04f);
                foreach (float a in stations)
                    s.steel.Box(P(a, back, top + 0.5f), Along(a), Out(a), new Vector3(0.06f, 1.08f, 0.06f));
            }

            /// <summary>A square beam from one point to another, its sides square to the stand.</summary>
            private static void Beam(MeshKit kit, Vector3 a, Vector3 b, Vector3 along, float size)
            {
                Vector3 d = b - a;
                Vector3 up = Vector3.Cross(along, d).normalized;
                Vector3 side = along * (size * 0.5f), lift = up * (size * 0.5f);
                Vector3 mid = (a + b) * 0.5f;
                kit.Face(a - side - lift, a + side - lift, b - side - lift, b + side - lift, mid + lift);
                kit.Face(a - side + lift, a + side + lift, b - side + lift, b + side + lift, mid - lift);
                kit.Face(a - side - lift, a - side + lift, b - side - lift, b - side + lift, mid + side);
                kit.Face(a + side - lift, a + side + lift, b + side - lift, b + side + lift, mid - side);
            }

            /// <summary>Roof heights at its back and its front edge, over the stand's base.</summary>
            private float RoofBack => p.Top + 3.2f;
            private float RoofFront => p.Top + 3.8f;
            private float RoofFrontU => p.U(1);

            /// <summary>
            /// A flat roof rising gently towards the track on steel columns along the back, held up by a beam over
            /// each column, with the back clad from the top tier up to the roof.
            /// </summary>
            public void CantileverRoof()
            {
                float back = p.Depth, rb = RoofBack, rf = RoofFront, uf = RoofFrontU;
                float ub = back + 0.25f;
                Band(s.roof, ub, rb + 0.25f, uf, rf + 0.25f, (ub + uf) * 0.5f, rb - 2f);   // top
                Band(s.roof, ub, rb, uf, rf, (ub + uf) * 0.5f, rb + 2f);                    // underside
                Band(s.steel, uf, rf - 0.6f, uf, rf + 0.25f, uf + 1f, rf);                   // fascia
                Band(s.steel, uf, rf - 0.6f, uf + 0.15f, rf - 0.6f, uf, rf + 1f);
                Band(s.cladding, back, p.Top, back, rb + 0.25f, back - 1f, rb);              // back cladding, inside
                Band(s.cladding, ub, p.Top, ub, rb + 0.25f, back - 1f, rb);                  // back cladding, outside
                Band(s.cladding, back, rb + 0.25f, ub, rb + 0.25f, back, rb - 1f);
                foreach (float a in new[] { 0f, g.Length })
                {
                    float inward = a == 0f ? 1f : -1f;
                    // Close the roof's ends and the cladding's.
                    s.roof.Face(P(a, ub, rb), P(a, uf, rf), P(a, ub, rb + 0.25f), P(a, uf, rf + 0.25f), P(a + inward, (ub + uf) * 0.5f, rb));
                    s.cladding.Face(P(a, back, p.Top), P(a, ub, p.Top), P(a, back, rb + 0.25f), P(a, ub, rb + 0.25f), P(a + inward, back, rb));
                    s.steel.Face(P(a, uf, rf - 0.6f), P(a, uf + 0.15f, rf - 0.6f), P(a, uf, rf), P(a, uf + 0.15f, rf), P(a + inward, uf, rf));
                }
                Columns(ub + 0.25f, rb, true);
                RoofLamps();
            }

            /// <summary>A floodlight in each bay, hung just in front of the fascia's foot, aimed at the track's centre line.</summary>
            private void RoofLamps()
            {
                int bays = Mathf.Max(1, Mathf.RoundToInt(g.Length / Bay));
                for (int i = 0; i < bays; i++)
                {
                    float a = g.Length * (i + 0.5f) / bays;
                    Vector3 anchor = P(a, RoofFrontU - 0.1f, RoofFront - 0.6f);
                    Vector3 head = P(a, RoofFrontU - 0.55f, RoofFront - 1.05f);
                    g.At(a, out Vector3 q, out Vector3 o, out _);
                    s.RoofLamps.Add(new LampMount(head, anchor, q - o * g.Front));
                }
            }

            /// <summary>Columns every bay along the back, each with a beam out under the roof and a tie.</summary>
            private void Columns(float u, float height, bool beams)
            {
                int bays = Mathf.Max(1, Mathf.RoundToInt(g.Length / Bay));
                for (int i = 0; i <= bays; i++)
                {
                    float a = g.Length * i / bays;
                    Vector3 along = Along(a), o = Out(a);
                    float foot = Ground(a, u) - Skirt;
                    Vector3 bottom = P(a, u, foot), top = P(a, u, height + 0.25f);
                    s.steel.Box((bottom + top) * 0.5f, along, o, new Vector3(0.35f, (top - bottom).magnitude, 0.35f));
                    if (!beams) continue;
                    Beam(s.steel, P(a, u, RoofBack - 0.25f), P(a, RoofFrontU + 0.2f, RoofFront - 0.3f), along, 0.3f);
                    Beam(s.steel, P(a, u - 0.2f, p.Top + 0.6f), P(a, (u + RoofFrontU) * 0.5f, (RoofBack + RoofFront) * 0.5f - 0.3f), along, 0.15f);
                }
            }

            /// <summary>
            /// A curved roof spanning the stand on columns at the back and slim columns along the front, the
            /// older look: an arc rising to its crown over the middle of the tiers.
            /// </summary>
            public void BarrelRoof()
            {
                float back = p.Depth, ub = back + 0.25f, uf = -0.4f;
                float vb = p.Top + 2.6f, vf = p.Front + 3.4f, crown = 2.2f;
                const int pieces = 10;
                var us = new float[pieces + 1];
                var vs = new float[pieces + 1];
                for (int i = 0; i <= pieces; i++)
                {
                    float t = i / (float)pieces;
                    us[i] = Mathf.Lerp(ub, uf, t);
                    vs[i] = Mathf.Lerp(vb, vf, t) + crown * 4f * t * (1f - t);
                }
                for (int i = 0; i < pieces; i++)
                {
                    float um = (us[i] + us[i + 1]) * 0.5f, vm = (vs[i] + vs[i + 1]) * 0.5f;
                    Band(s.roof, us[i], vs[i] + 0.2f, us[i + 1], vs[i + 1] + 0.2f, um, vm - 3f);
                    Band(s.roof, us[i], vs[i], us[i + 1], vs[i + 1], um, vm + 3f);
                    foreach (float a in new[] { 0f, g.Length })
                    {
                        float inward = a == 0f ? 1f : -1f;
                        s.roof.Face(P(a, us[i], vs[i]), P(a, us[i + 1], vs[i + 1]), P(a, us[i], vs[i] + 0.2f), P(a, us[i + 1], vs[i + 1] + 0.2f), P(a + inward, um, vm));
                    }
                }
                Band(s.steel, uf, vf - 0.5f, uf, vf + 0.2f, uf + 1f, vf);
                Band(s.steel, uf, vf - 0.5f, uf + 0.12f, vf - 0.5f, uf, vf + 1f);
                Band(s.cladding, back, p.Top, back, vb + 0.2f, back - 1f, vb);
                Band(s.cladding, ub, p.Top, ub, vb + 0.2f, back - 1f, vb);
                foreach (float a in new[] { 0f, g.Length })
                    s.cladding.Face(P(a, back, p.Top), P(a, ub, p.Top), P(a, back, vb + 0.2f), P(a, ub, vb + 0.2f), P(a + (a == 0f ? 1f : -1f), back, vb));
                Columns(ub + 0.25f, vb, false);
                // Slim front columns, standing on the walkway in front of the stand.
                int bays = Mathf.Max(1, Mathf.RoundToInt(g.Length / Bay));
                for (int i = 0; i <= bays; i++)
                {
                    float a = g.Length * i / bays;
                    Vector3 bottom = P(a, uf, Ground(a, uf) - Skirt), top = P(a, uf, vf);
                    s.steel.Box((bottom + top) * 0.5f, Along(a), Out(a), new Vector3(0.18f, (top - bottom).magnitude, 0.18f));
                }
            }

            /// <summary>
            /// A row of seats on every tread, broken by aisles, and the fans in them: fuller towards the front and in
            /// clumps along the stand.
            /// </summary>
            public void Seats(StandSite site, int index)
            {
                var rng = new System.Random(1000 + index * 7919);
                // Clumping: smooth noise along the stand, a value every 9 m.
                int knots = Mathf.CeilToInt(g.Length / 9f) + 2;
                var clump = new float[knots];
                for (int i = 0; i < knots; i++) clump[i] = (float)rng.NextDouble();
                float Clump(float a)
                {
                    float x = a / 9f;
                    int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, knots - 2);
                    float t = x - i;
                    t = t * t * (3f - 2f * t);
                    return Mathf.Lerp(clump[i], clump[i + 1], t);
                }

                int blocks = Mathf.Max(1, Mathf.RoundToInt(g.Length / BlockLength));
                float block = g.Length / blocks;
                for (int b = 0; b < blocks; b++)
                {
                    float from = b * block + Aisle * 0.5f, to = (b + 1) * block - Aisle * 0.5f;
                    for (int r = 0; r < p.Rows; r++)
                    {
                        float u = p.U(r), v = p.V(r);
                        float front = u + 0.3f, back = u + 0.72f, h = v + 0.42f;
                        Band(s.seats, front, h, back, h, (front + back) * 0.5f, v, from, to);
                        Band(s.seats, front, v, front, h, back, v + 0.2f, from, to);
                        Band(s.seats, back, v + 0.2f, back, h + 0.35f, back + 0.3f, v + 0.2f, from, to);
                        foreach (float a in new[] { from, to })
                            s.seats.Face(P(a, front, v), P(a, back, v), P(a, front, h), P(a, back, h), P(a == from ? a + 1f : a - 1f, (front + back) * 0.5f, v + 0.2f));
                        float rowShare = 1.25f - 0.7f * r / Mathf.Max(1, p.Rows - 1);
                        for (float a = from + SeatSpacing * 0.5f; a < to; a += SeatSpacing)
                        {
                            float chance = site.Fill * rowShare * (0.25f + 1.5f * Clump(a));
                            if (rng.NextDouble() >= chance) continue;
                            Fan(a + (float)(rng.NextDouble() - 0.5) * 0.06f, u, v, rng);
                        }
                    }
                }
            }

            /// <summary>One seated fan facing the track: a card standing on the tread, its look packed into its UVs.</summary>
            private void Fan(float a, float u, float v, System.Random rng)
            {
                Vector3 toTrack = -Out(a);
                int shirt = rng.Next(CrowdPalette.Shirts.Length);
                int skin = rng.Next(CrowdPalette.Skins.Length);
                int legs = rng.Next(CrowdPalette.Trousers.Length);
                float lean = (float)(rng.NextDouble() - 0.5) * 0.06f;
                float tall = (float)rng.NextDouble();
                CrowdCard.Add(s.crowd, P(a + lean, u + CrowdCard.Depth, v), toTrack, shirt, skin, legs, tall, s.Fans);
                s.Fans++;
            }

        }

        // ---------------------------------------------------------------- catch fence

        /// <summary>
        /// Mesh fencing just behind the armco: posts every 4 m, 4 m of mesh and a top section leaning in towards the
        /// track, following the armco wherever it bends.
        /// </summary>
        private void Fence(int side, List<int> ks)
        {
            float sign = side == 0 ? 1f : -1f;
            // The fence line, resampled to even steps along its own length.
            var line = new List<Vector3>();
            var outs = new List<Vector3>();
            foreach (int k in ks)
            {
                Vector3 o = c.Left[k] * sign;
                Vector3 p = c.Centre[k] + o * (c.HalfWidth(k) + track.RunoffReach(side, k) + FenceBack);
                p.y = c.TerrainHeight(p.x, p.z);
                line.Add(p);
                outs.Add(o);
            }
            var lengths = new float[line.Count];
            for (int i = 1; i < line.Count; i++) lengths[i] = lengths[i - 1] + Flat(line[i], line[i - 1]);
            float total = lengths[line.Count - 1];
            int spans = Mathf.Max(1, Mathf.RoundToInt(total / PostSpacing));
            var at = new Vector3[spans + 1];
            var outAt = new Vector3[spans + 1];
            int j = 0;
            for (int i = 0; i <= spans; i++)
            {
                float d = total * i / spans;
                while (j < line.Count - 2 && lengths[j + 1] < d) j++;
                float t = Mathf.Clamp01((d - lengths[j]) / Mathf.Max(1e-4f, lengths[j + 1] - lengths[j]));
                at[i] = Vector3.Lerp(line[j], line[j + 1], t);
                outAt[i] = Vector3.Lerp(outs[j], outs[j + 1], t).normalized;
            }
            Vector3 Top(int i) => at[i] + Vector3.up * FenceHeight;
            Vector3 Tip(int i) => Top(i) + Vector3.up * (FenceLean * 0.6f) - outAt[i] * FenceLean;
            for (int i = 0; i < spans; i++)
            {
                Vector3 a = at[i] + Vector3.down * Skirt, b = at[i + 1] + Vector3.down * Skirt;
                Vector3 mid = (at[i] + at[i + 1]) * 0.5f + Vector3.up * 2f;
                mesh.Face(a, b, Top(i), Top(i + 1), mid + outAt[i]);
                mesh.Face(a, b, Top(i), Top(i + 1), mid - outAt[i]);
                mesh.Face(Top(i), Top(i + 1), Tip(i), Tip(i + 1), mid + Vector3.up * 3f + outAt[i] * 2f);
                mesh.Face(Top(i), Top(i + 1), Tip(i), Tip(i + 1), mid + Vector3.up * 3f - outAt[i] * 2f);
            }
            for (int i = 0; i <= spans; i++)
            {
                Vector3 along = (at[Mathf.Min(i + 1, spans)] - at[Mathf.Max(i - 1, 0)]);
                along.y = 0;
                along.Normalize();
                Vector3 bottom = at[i] + Vector3.down * Skirt;
                Block(posts, (bottom + Top(i)) * 0.5f, along, outAt[i], new Vector3(0.09f, FenceHeight + Skirt, 0.09f), null, true, false);
                Vector3 tip = Tip(i);
                Vector3 d = tip - Top(i);
                Vector3 up = Vector3.Cross(along, d).normalized * 0.04f, across = along * 0.04f;
                Vector3 p0 = Top(i), p1 = tip, m0 = (p0 + p1) * 0.5f;
                posts.Face(p0 - across - up, p0 + across - up, p1 - across - up, p1 + across - up, m0 + up);
                posts.Face(p0 - across + up, p0 + across + up, p1 - across + up, p1 + across + up, m0 - up);
                posts.Face(p0 - across - up, p0 - across + up, p1 - across - up, p1 - across + up, m0 + across);
                posts.Face(p0 + across - up, p0 + across + up, p1 + across - up, p1 + across + up, m0 - across);
            }
        }
    }

    /// <summary>
    /// One fan as a card: a quad standing on the tread, facing the track, that the crowd shader (Resources/Crowd) turns
    /// towards the camera and draws a seated fan on. Each corner's UV packs the corner and the fan's look in whole numbers:
    /// x is the corner across (0 or 1) plus twice (shirt + 8 trousers), y the corner up plus twice (skin + 8 (height + 16 seed)).
    /// </summary>
    public static class CrowdCard
    {
        /// <summary>The card's size, m: wide enough for arms waved out, tall enough for arms up.</summary>
        public const float Width = 0.62f, Height = 1.55f;
        /// <summary>How far the card stands behind the front of its tread, m: through the fan's knees.</summary>
        public const float Depth = 0.45f;
        /// <summary>Steps of height a fan's sitting height is kept to.</summary>
        public const int Heights = 16;

        /// <summary>A fan's card. <paramref name="tall"/> is 0 for the shortest fan to 1 for the tallest.</summary>
        public static void Add(MeshKit kit, Vector3 foot, Vector3 toTrack, int shirt, int skin, int legs, float tall, int id)
        {
            Vector3 right = Vector3.Cross(Vector3.up, toTrack).normalized;
            int height = Mathf.Clamp(Mathf.FloorToInt(tall * Heights), 0, Heights - 1);
            int seed = (int)(((uint)id * 2654435761u) >> 28);
            float x = 2f * (shirt + 8 * legs), y = 2f * (skin + 8 * (height + Heights * seed));
            Vector3 Corner(int cx, int cy) => foot + right * ((cx - 0.5f) * Width) + Vector3.up * (cy * Height);
            Vector2 Uv(int cx, int cy) => new Vector2(x + cx, y + cy);
            kit.Triangle(Corner(0, 0), Corner(0, 1), Corner(1, 0), toTrack, toTrack, toTrack, Uv(0, 0), Uv(0, 1), Uv(1, 0));
            kit.Triangle(Corner(1, 0), Corner(0, 1), Corner(1, 1), toTrack, toTrack, toTrack, Uv(1, 0), Uv(0, 1), Uv(1, 1));
        }

        /// <summary>The shirt colour of the fan whose card has this UV, an index into <see cref="CrowdPalette.Shirts"/>.</summary>
        public static int Shirt(Vector2 uv) => Mathf.FloorToInt(uv.x * 0.5f + 0.01f) % 8;
    }

    /// <summary>
    /// The crowd's colours: one row of the palette texture each for shirts, skin, trousers and hair. Mostly plain
    /// summer clothes, with a few brighter shirts.
    /// </summary>
    public static class CrowdPalette
    {
        public const int Width = 8, Height = 4;
        public static readonly Color[] Shirts =
        {
            new Color(0.92f, 0.92f, 0.9f), new Color(0.16f, 0.2f, 0.34f), new Color(0.62f, 0.14f, 0.12f), new Color(0.1f, 0.1f, 0.11f),
            new Color(0.5f, 0.52f, 0.54f), new Color(0.48f, 0.66f, 0.82f), new Color(0.38f, 0.42f, 0.22f), new Color(0.9f, 0.68f, 0.16f),
        };
        public static readonly Color[] Skins =
        {
            new Color(0.93f, 0.76f, 0.64f), new Color(0.85f, 0.66f, 0.52f), new Color(0.66f, 0.47f, 0.34f), new Color(0.42f, 0.28f, 0.2f),
            new Color(0.9f, 0.72f, 0.6f), new Color(0.78f, 0.58f, 0.44f),
        };
        public static readonly Color[] Trousers =
        {
            new Color(0.2f, 0.27f, 0.4f), new Color(0.13f, 0.14f, 0.16f), new Color(0.62f, 0.56f, 0.42f), new Color(0.32f, 0.38f, 0.5f),
            new Color(0.42f, 0.42f, 0.4f),
        };
        public static readonly Color[] Hair =
        {
            new Color(0.08f, 0.06f, 0.05f), new Color(0.24f, 0.15f, 0.08f), new Color(0.45f, 0.3f, 0.16f), new Color(0.72f, 0.6f, 0.38f),
            new Color(0.55f, 0.55f, 0.55f), new Color(0.13f, 0.1f, 0.08f),
        };

        /// <summary>The middle of one texel: row 0 shirts, 1 skin, 2 trousers, 3 hair.</summary>
        public static Vector2 Uv(int row, int column) => new Vector2((column + 0.5f) / Width, (row + 0.5f) / Height);

        public static Color[] Row(int row) => row == 0 ? Shirts : row == 1 ? Skins : row == 2 ? Trousers : Hair;
    }
}
