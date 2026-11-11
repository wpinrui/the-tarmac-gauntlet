using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public enum FloodKind { Stand, PitRoof, PitWall, StartLine }

    /// <summary>The start gantry's span: its two feet and the height of the underside of its beam.</summary>
    public readonly struct GantrySpan
    {
        public readonly Vector3 Left, Right;
        public readonly float Bottom;
        /// <summary>Half the depth of the beam along the track, from its middle to a face.</summary>
        public readonly float HalfDepth;

        public GantrySpan(Vector3 left, Vector3 right, float bottom, float halfDepth)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            HalfDepth = halfDepth;
        }

        /// <summary>The gantry where the trackside furniture placed it.</summary>
        public static GantrySpan Of(Circuit circuit, TrackFurniture placed) =>
            new GantrySpan(placed.GantryLeft, placed.GantryRight, circuit.Centre[placed.GantrySample].y + TrackFurniture.GantryClearance,
                FurnitureBuilder.LegHalf + 0.07f);
    }

    /// <summary>One floodlight: where its lens is, where it aims, and the pool of light it throws on the ground.</summary>
    public readonly struct Floodlight
    {
        public readonly FloodKind Kind;
        /// <summary>Middle of the lens.</summary>
        public readonly Vector3 Position;
        /// <summary>Unit vector from the lens to <see cref="Target"/>.</summary>
        public readonly Vector3 Aim;
        /// <summary>The spot on the ground the beam centres on.</summary>
        public readonly Vector3 Target;
        /// <summary>Flat unit vector of the pool's first radius.</summary>
        public readonly Vector3 PoolAxis;
        /// <summary>The pool's radii along <see cref="PoolAxis"/> and across it, m.</summary>
        public readonly float Long, Wide;

        public Floodlight(FloodKind kind, Vector3 position, Vector3 target, Vector3 poolAxis, float longRadius, float wideRadius)
        {
            Kind = kind;
            Position = position;
            Target = target;
            Aim = (target - position).normalized;
            PoolAxis = poolAxis;
            Long = longRadius;
            Wide = wideRadius;
        }

        /// <summary>Distance from the lens to the target, m.</summary>
        public float Reach => (Target - Position).magnitude;

        /// <summary>Full cone angle that covers the pool, degrees.</summary>
        public float Cone => Mathf.Clamp(2f * Mathf.Atan(Mathf.Max(Long, Wide) * 1.1f / Reach) * Mathf.Rad2Deg, 30f, 120f);
    }

    /// <summary>
    /// The floodlights round the pits, the stands and the start line, laid out from the circuit with no Unity objects
    /// so they can be checked outside Unity. Lamps hang under each covered stand's roof edge (<see cref="Grandstands.RoofLamps"/>),
    /// off the pit building's roof edge over the pit lane, on masts along the pit wall, and under the start gantry's
    /// beam, three aimed down the grid and one over the line. Uplights at the gantry's feet light the gantry itself. Each lamp has a glowing lens and throws a pool of light on the ground below its aim:
    /// a mesh drawn over the ground that brightens what is already there, so the whole lit area costs a few draw
    /// calls and no lights. <see cref="NightLights"/> adds real lights, for the cars only, near the camera.
    /// </summary>
    public sealed class Floodlights
    {
        /// <summary>Height of a pit wall mast's lamps above the wall, m.</summary>
        public const float MastHeight = 15f;
        /// <summary>Distance between masts along the pit wall, m.</summary>
        public const float MastSpacing = 48f;
        /// <summary>Pit building lamps are hung at every this many stations along the garage row.</summary>
        public const int PitRoofEvery = 2;
        /// <summary>Pools sit this far above the ground they lie on, m.</summary>
        public const float PoolLift = 0.07f;
        /// <summary>Squares across a pool's grid, each way.</summary>
        public const int PoolCells = 6;

        /// <summary>The circuit's parts a pool can lie on.</summary>
        public static readonly HashSet<string> GroundParts = new HashSet<string>
        {
            "Road", "Road edges", "Kerbs", "Verges", "Grass runoff", "Gravel traps", "Ground", "Pit lane", "Pit lane shoulders",
            "Paddock", "Edge lines", "Start line and safety car lines", "Grid slots", "Grid yellow lines", "Pit lane lines",
            "Pit box lines", "Pit exit line", "Pit entry hatching",
        };

        /// <summary>The pit building's roof: its top, and how far out from the garages' middle line its front edge is, m.</summary>
        private const float RoofTop = 14.9f, RoofFront = 9.4f;
        /// <summary>The race offices' roof, the same way.</summary>
        private const float OfficeTop = 18.6f, OfficeFront = 9.5f;
        /// <summary>Where across the pit lane the pit building's lamps aim, from the garages' middle line, m.</summary>
        private const float LaneAim = 15f;
        /// <summary>No pit wall mast stands this near either of the start gantry's feet, m.</summary>
        private const float StartClear = 32f;
        /// <summary>Lap distances the gantry's four lamps aim at, down the grid and over the line.</summary>
        private static readonly float[] StartAims = { -75f, -45f, -18f, 10f };
        /// <summary>Where along the gantry's span each of those lamps hangs, clear of the start light box in the middle.</summary>
        private static readonly float[] StartHooks = { 0.15f, 0.3f, 0.7f, 0.85f };
        /// <summary>The gantry's lamps hang this far under its beam, m.</summary>
        private const float GantryDrop = 0.45f;

        public readonly List<Floodlight> Lights = new List<Floodlight>();
        /// <summary>Lights at the gantry's feet aimed up at its beam: where they stand and where they aim.</summary>
        public readonly List<(Vector3 position, Vector3 aim)> Uplights = new List<(Vector3, Vector3)>();

        private readonly Circuit c;
        private readonly PitComplex pc;
        private readonly MeshKit steel = new MeshKit(), lenses = new MeshKit(), pools = new MeshKit();

        /// <param name="standLamps">Under the stands' roofs, from <see cref="Grandstands.RoofLamps"/>.</param>
        /// <param name="pitWall">The pit wall's runs, from <see cref="CircuitBuilder.PitWallRuns"/>.</param>
        /// <param name="built">The circuit's parts as built: the pools lie on those in <see cref="GroundParts"/>.</param>
        /// <param name="gantry">The start gantry, from <see cref="TrackFurniture"/>.</param>
        public Floodlights(Circuit circuit, PitComplex complex, IReadOnlyList<LampMount> standLamps, IReadOnlyList<Vector3[]> pitWall,
            IEnumerable<CircuitPart> built, GantrySpan gantry)
        {
            c = circuit;
            pc = complex;
            foreach (LampMount m in standLamps) Stand(m);
            PitRoof();
            Gantry(gantry);
            foreach (Vector3[] run in pitWall) Masts(run, gantry);
            var under = new GroundGrid();
            foreach (Floodlight f in Lights) under.Cover(f.Target, Mathf.Sqrt(f.Long * f.Long + f.Wide * f.Wide) + 1f);
            foreach (CircuitPart part in built)
                if (GroundParts.Contains(part.Name)) under.Add(part.Kit);
            foreach (Floodlight f in Lights) Pool(f, under);
        }

        /// <summary>The masts and brackets, the glowing lenses and the pools of light on the ground.</summary>
        public List<CircuitPart> Build(Material steelMaterial, Material lensMaterial, Material poolMaterial)
        {
            var parts = new List<CircuitPart>();
            foreach (MeshKit chunk in steel.Chunks(150f)) parts.Add(new CircuitPart("Floodlight steel", chunk, steelMaterial, false, true));
            foreach (MeshKit chunk in lenses.Chunks(150f)) parts.Add(new CircuitPart("Floodlight lenses", chunk, lensMaterial, false, false));
            foreach (MeshKit chunk in pools.Chunks(150f)) parts.Add(new CircuitPart("Floodlight pools", chunk, poolMaterial, false, false));
            return parts;
        }

        // ---------------------------------------------------------------- the lamps

        private void Stand(LampMount m)
        {
            Vector3 target = OnGround(m.Target);
            Vector3 axis = Flat(target - m.Position);
            var f = new Floodlight(FloodKind.Stand, m.Position, target, axis, 11f, 9f);
            Lights.Add(f);
            Beam(steel, m.Anchor, m.Position - f.Aim * 0.2f, 0.12f);
            Head(f, 0.8f, 0.5f);
        }

        /// <summary>Lamps off the pit building's roof edge, and the race offices' higher roof, aimed down into the lane.</summary>
        private void PitRoof()
        {
            for (int j = 1; j < pc.Stations - 1; j += PitRoofEvery)
            {
                bool office = j > pc.OfficeFrom && j < pc.OfficeTo;
                float top = office ? OfficeTop : RoofTop, front = office ? OfficeFront : RoofFront;
                Vector3 anchor = pc.RowPoint(j, top - 0.3f, front);
                Vector3 head = pc.RowPoint(j, top - 0.55f, front + 0.7f);
                Vector3 target = OnGround(pc.RowPoint(j, 0f, LaneAim));
                var f = new Floodlight(FloodKind.PitRoof, head, target, pc.RowOut(j), 12f, 19f);
                Lights.Add(f);
                Beam(steel, anchor, head - f.Aim * 0.2f, 0.12f);
                Head(f, 0.9f, 0.6f);
            }
        }

        /// <summary>
        /// Four lamps hung under the gantry's beam over the road, three on its grid side aimed down the grid and one on
        /// the far side aimed over the line, and an uplight at each foot aimed at the beam's middle.
        /// </summary>
        private void Gantry(GantrySpan g)
        {
            Vector3 span = Flat(g.Right - g.Left), along = Vector3.Cross(span, Vector3.up);
            Vector3 forward = c.Forward[c.IndexOf(0f)];
            if (Vector3.Dot(along, forward) < 0f) along = -along;
            Vector3 l = new Vector3(g.Left.x, g.Bottom, g.Left.z), r = new Vector3(g.Right.x, g.Bottom, g.Right.z);
            for (int i = 0; i < StartAims.Length; i++)
            {
                bool ahead = StartAims[i] > 0f;
                Vector3 hook = Vector3.Lerp(l, r, StartHooks[i]) + along * ((ahead ? 1f : -1f) * g.HalfDepth);
                Vector3 target = OnGround(c.Centre[c.IndexOf(Mathf.Repeat(StartAims[i], c.Lap))]);
                Vector3 lens = hook + Vector3.down * GantryDrop + along * (ahead ? 0.3f : -0.3f);
                var f = new Floodlight(FloodKind.StartLine, lens, target, Flat(target - lens), 18f, 16f);
                Lights.Add(f);
                Beam(steel, hook, lens - f.Aim * 0.2f, 0.1f);
                Head(f, 1.0f, 0.65f);
            }
            Vector3 middle = (l + r) * 0.5f;
            foreach (Vector3 foot in new[] { g.Left, g.Right })
            {
                Vector3 p = foot + Flat(middle - foot) * 1.5f + along * (g.HalfDepth + 1.2f) + Vector3.up * 0.3f;
                Uplights.Add((p, (middle - p).normalized));
            }
        }

        /// <summary>Masts along one pit wall run, each with two lamps aimed at the track either side of it.</summary>
        private void Masts(Vector3[] run, GantrySpan gantry)
        {
            float along = 0f, next = MastSpacing * 0.5f;
            for (int i = 1; i < run.Length; i++)
            {
                along += Mathf.Sqrt(Flat2(run[i] - run[i - 1]));
                if (along < next) continue;
                next += MastSpacing;
                Vector3 foot = run[i];
                if (Flat2(foot - gantry.Left) < StartClear * StartClear || Flat2(foot - gantry.Right) < StartClear * StartClear) continue;
                int k = NearestTrack(foot, -1);
                float d = c.DistanceAt(k, foot);
                var targets = new[]
                {
                    OnGround(c.Centre[c.IndexOf(Mathf.Repeat(d - 14f, c.Lap))]),
                    OnGround(c.Centre[c.IndexOf(Mathf.Repeat(d + 14f, c.Lap))]),
                };
                Mast(foot, targets);
            }
        }

        /// <summary>
        /// A mast standing on the pit wall with its lamps on a bar across the top, the bar running along the track. The
        /// lamps are spread along the bar in the order their targets are given.
        /// </summary>
        private void Mast(Vector3 foot, Vector3[] targets)
        {
            const float height = MastHeight, w = 1.1f, h = 0.75f;
            int k = NearestTrack(foot, -1);
            Vector3 toTrack = Flat(c.Centre[k] - foot), bar = Flat(c.Forward[k]);
            Vector3 top = foot + Vector3.up * height;
            float span = (targets.Length - 1) * (w + 0.4f);
            OrientedBox(steel, foot + Vector3.up * (height * 0.5f), bar, Vector3.up, toTrack, new Vector3(0.4f, height + 0.6f, 0.4f));
            OrientedBox(steel, top + Vector3.up * 0.65f, bar, Vector3.up, toTrack, new Vector3(span + w + 0.4f, 0.18f, 0.18f));
            for (int i = 0; i < targets.Length; i++)
            {
                Vector3 lens = top + bar * (i * (w + 0.4f) - span * 0.5f) + toTrack * 0.45f;
                var f = new Floodlight(FloodKind.PitWall, lens, targets[i], toTrack, 15f, 22f);
                Lights.Add(f);
                Beam(steel, lens - f.Aim * 0.3f + Vector3.up * 0.1f, lens - toTrack * 0.45f + Vector3.up * 0.65f, 0.1f);
                Head(f, w, h);
            }
        }

        /// <summary>A lamp: a housing behind a lens facing along its aim.</summary>
        private void Head(Floodlight f, float w, float h)
        {
            Vector3 z = f.Aim, x = Vector3.Cross(Vector3.up, z).normalized, y = Vector3.Cross(z, x);
            OrientedBox(steel, f.Position - z * 0.2f, x, y, z, new Vector3(w + 0.12f, h + 0.12f, 0.36f));
            Vector3 p = f.Position - z * 0.015f;
            Vector3 hx = x * (w * 0.5f), hy = y * (h * 0.5f);
            lenses.Face(p - hx - hy, p + hx - hy, p - hx + hy, p + hx + hy, p - z);
        }

        // ---------------------------------------------------------------- pools of light

        /// <summary>
        /// The pool under a lamp's aim: a grid lying on the ground, its UVs running -1 to 1 across each radius, so the
        /// shader can fade it to nothing at the edge.
        /// </summary>
        private void Pool(Floodlight f, GroundGrid under)
        {
            Vector3 a = f.PoolAxis, b = new Vector3(-a.z, 0f, a.x);
            int hint = NearestTrack(f.Target, -1);
            var rows = new List<Vector3[]>();
            for (int r = 0; r <= PoolCells; r++)
            {
                float u = -1f + 2f * r / PoolCells;
                var row = new Vector3[PoolCells + 1];
                for (int q = 0; q <= PoolCells; q++)
                {
                    float v = -1f + 2f * q / PoolCells;
                    Vector3 p = f.Target + a * (u * f.Long) + b * (v * f.Wide);
                    float guess = Ground(p, hint);
                    p.y = (under.Height(p.x, p.z, guess + 1.5f) ?? guess) + PoolLift;
                    row[q] = p;
                }
                rows.Add(row);
            }
            Vector3 centre = f.Target;
            float lr = f.Long, wr = f.Wide;
            pools.Strip(rows, 1f, 1f, false, null, p => new Vector2(Vector3.Dot(Flat3(p - centre), a) / lr, Vector3.Dot(Flat3(p - centre), b) / wr));
        }

        /// <summary>A point dropped onto the ground under it.</summary>
        private Vector3 OnGround(Vector3 p)
        {
            p.y = Ground(p, -1);
            return p;
        }

        /// <summary>
        /// Height of whatever is underfoot at a point: the road where it is on the road or its kerbs, the pit lane where
        /// it is on the lane, otherwise the ground.
        /// </summary>
        public float Ground(Vector3 p, int hint)
        {
            float y = c.TerrainHeight(p.x, p.z);
            int k = NearestTrack(p, hint);
            float lat = Vector3.Dot(p - c.Centre[k], c.Left[k]);
            if (Mathf.Abs(lat) < c.HalfWidth(k) + 1.5f) y = Mathf.Max(y, c.Surface(k, lat).y);
            if (c.Lane != null && c.Lane.Length > 0)
            {
                int best = 0;
                float nearest = float.MaxValue;
                for (int i = 0; i < c.Lane.Length; i++)
                {
                    float d = Flat2(c.Lane[i] - p);
                    if (d < nearest) { nearest = d; best = i; }
                }
                if (Mathf.Sqrt(nearest) < c.Data.pit.laneWidth * 0.5f + 1.2f) y = Mathf.Max(y, c.Lane[best].y + 0.01f);
            }
            return y;
        }

        /// <summary>The track sample nearest a point in plan, searching near the hint when one is given.</summary>
        private int NearestTrack(Vector3 p, int hint)
        {
            int best = 0, from = 0, count = c.Count;
            if (hint >= 0) { from = hint - 80; count = 161; }
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int k = c.Wrap(from + i);
                float d = Flat2(c.Centre[k] - p);
                if (d < nearest) { nearest = d; best = k; }
            }
            return best;
        }

        /// <summary>
        /// The built ground's triangles in a grid of cells, kept only where a pool lies, to find the height of what is
        /// built under a point.
        /// </summary>
        public sealed class GroundGrid
        {
            private const float Cell = 4f;
            private readonly HashSet<(int, int)> covered = new HashSet<(int, int)>();
            private readonly Dictionary<(int, int), List<Vector3>> cells = new Dictionary<(int, int), List<Vector3>>();

            /// <summary>Keeps the triangles within this distance of a point in plan.</summary>
            public void Cover(Vector3 centre, float radius)
            {
                int x0 = Mathf.FloorToInt((centre.x - radius) / Cell), x1 = Mathf.FloorToInt((centre.x + radius) / Cell);
                int z0 = Mathf.FloorToInt((centre.z - radius) / Cell), z1 = Mathf.FloorToInt((centre.z + radius) / Cell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++) covered.Add((x, z));
            }

            public void Add(MeshKit kit)
            {
                IReadOnlyList<Vector3> v = kit.Vertices;
                IReadOnlyList<int> t = kit.Triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, d.x)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, d.x)) / Cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, d.z)) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, d.z)) / Cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            if (!covered.Contains((x, z))) continue;
                            if (!cells.TryGetValue((x, z), out List<Vector3> list)) cells[(x, z)] = list = new List<Vector3>();
                            list.Add(a);
                            list.Add(b);
                            list.Add(d);
                        }
                }
            }

            /// <summary>The highest built surface under a point that is below the ceiling, or null where there is none.</summary>
            public float? Height(float x, float z, float ceiling)
            {
                if (!cells.TryGetValue((Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out List<Vector3> list)) return null;
                float? best = null;
                for (int i = 0; i < list.Count; i += 3)
                {
                    Vector3 a = list[i], b = list[i + 1], c = list[i + 2];
                    float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                    float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                    float l3 = 1f - l1 - l2;
                    if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * c.y;
                    if (y < ceiling && (best == null || y > best)) best = y;
                }
                return best;
            }
        }

        // ---------------------------------------------------------------- shapes

        /// <summary>A box along any three axes at right angles.</summary>
        private static void OrientedBox(MeshKit kit, Vector3 centre, Vector3 x, Vector3 y, Vector3 z, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float a, float b, float d) => centre + x * (a * h.x) + y * (b * h.y) + z * (d * h.z);
            kit.Face(P(-1, -1, 1), P(1, -1, 1), P(-1, 1, 1), P(1, 1, 1), centre);
            kit.Face(P(-1, -1, -1), P(1, -1, -1), P(-1, 1, -1), P(1, 1, -1), centre);
            kit.Face(P(1, -1, -1), P(1, -1, 1), P(1, 1, -1), P(1, 1, 1), centre);
            kit.Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, -1), P(-1, 1, 1), centre);
            kit.Face(P(-1, 1, -1), P(1, 1, -1), P(-1, 1, 1), P(1, 1, 1), centre);
            kit.Face(P(-1, -1, -1), P(1, -1, -1), P(-1, -1, 1), P(1, -1, 1), centre);
        }

        /// <summary>A square bar from one point to another.</summary>
        private static void Beam(MeshKit kit, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 z = (b - a).normalized;
            Vector3 x = Mathf.Abs(z.y) > 0.95f ? Vector3.right : Vector3.Cross(Vector3.up, z).normalized;
            Vector3 y = Vector3.Cross(z, x);
            OrientedBox(kit, (a + b) * 0.5f, x, y, z, new Vector3(thickness, thickness, (b - a).magnitude));
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;
        private static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);
        private static float Flat2(Vector3 v) => v.x * v.x + v.z * v.z;
    }
}
