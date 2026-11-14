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

    /// <summary>One floodlight's head: where its lens is and the spot on the ground it aims at.</summary>
    public readonly struct FloodHead
    {
        public readonly FloodKind Kind;
        public readonly Vector3 Position, Target;

        public FloodHead(FloodKind kind, Vector3 position, Vector3 target)
        {
            Kind = kind;
            Position = position;
            Target = target;
        }

        public Vector3 Aim => (Target - Position).normalized;
    }

    /// <summary>
    /// One real light standing for a few neighbouring heads (<see cref="FloodlightRig"/>): a URP spot light at their middle,
    /// aimed at the middle of their targets, wide enough to light their pools.
    /// </summary>
    public struct FloodSource
    {
        public FloodKind Kind;
        public Vector3 Position, Target;
        /// <summary>Full cone and its full-strength core, degrees; reach of the light, m.</summary>
        public float Cone, Core, Range;
        /// <summary>The light's intensity, set so the pools overlap to <see cref="NightBudget.Floodlit"/> on the track.</summary>
        public float Intensity;

        public Vector3 Aim => (Target - Position).normalized;
        public float Reach => (Target - Position).magnitude;
    }

    /// <summary>
    /// The floodlights round the pits, the stands and the start line, laid out from the circuit with no Unity objects so
    /// they can be checked outside Unity (tools/night-budget). Heads hang under each covered stand's roof edge
    /// (<see cref="Grandstands.RoofLamps"/>), off the pit building's roof edge over the pit lane, on masts along the pit
    /// wall and under the start gantry's beam. Neighbouring heads share one real light (<see cref="Sources"/>), and the
    /// lights' intensities are balanced so their pools add up to the same light along the lit track.
    /// </summary>
    public sealed class FloodlightLayout
    {
        /// <summary>Height of a pit wall mast's heads above the wall, m, and the distance between masts, m.</summary>
        public const float MastHeight = 15f, MastSpacing = 48f;
        /// <summary>The pit building's roof: its top, and how far out from the garages' middle line its front edge is, m.</summary>
        private const float RoofTop = 14.9f, RoofFront = 9.4f;
        /// <summary>The race offices' roof, the same way.</summary>
        private const float OfficeTop = 18.6f, OfficeFront = 9.5f;
        /// <summary>Where across the pit lane the pit building's heads aim, from the garages' middle line, m.</summary>
        private const float LaneAim = 15f;
        /// <summary>No pit wall mast stands this near either of the start gantry's feet, m.</summary>
        private const float StartClear = 32f;
        /// <summary>Lap distances the gantry's four heads aim at, down the grid and over the line.</summary>
        private static readonly float[] StartAims = { -75f, -45f, -18f, 10f };
        /// <summary>Where along the gantry's span each of those heads hangs, clear of the start light box in the middle.</summary>
        private static readonly float[] StartHooks = { 0.15f, 0.3f, 0.7f, 0.85f };
        private const float GantryDrop = 0.45f;
        /// <summary>Heads of a kind this near one another share a real light, m: the gantry's and the stands' nearest; a mast's and the pit roof's are each a light.</summary>
        private const float ShareStand = 8f, SharePitRoof = 1f, ShareMast = 1f, ShareStart = 6f;
        /// <summary>The radius each kind's light must cover round its target, m.</summary>
        private const float PoolStand = 32f, PoolPitRoof = 24f, PoolMast = 30f, PoolStart = 26f;
        /// <summary>A light reaches this many times as far as its target, plus a margin, m.</summary>
        private const float RangeTimes = 2.2f, RangeMargin = 10f;
        /// <summary>
        /// Rounds of balancing the lights against one another; the points balanced are this far apart along the track,
        /// m, wherever the lights as first set give at least this share of Floodlit.
        /// </summary>
        private const int Rounds = 300;
        private const float SampleStep = 6f, Covered = 0.25f;

        public readonly List<FloodHead> Heads = new List<FloodHead>();
        public readonly List<FloodSource> Sources = new List<FloodSource>();

        private readonly Circuit c;
        private readonly PitComplex pc;
        private readonly MeshKit steel = new MeshKit(), lenses = new MeshKit();

        /// <param name="standLamps">Under the stands' roofs, from <see cref="Grandstands.RoofLamps"/>.</param>
        /// <param name="pitWall">The pit wall's runs, from <see cref="CircuitBuilder.PitWallRuns"/>.</param>
        /// <param name="gantry">The start gantry, from <see cref="TrackFurniture"/>.</param>
        public FloodlightLayout(Circuit circuit, PitComplex complex, IReadOnlyList<LampMount> standLamps, IReadOnlyList<Vector3[]> pitWall,
            GantrySpan gantry)
        {
            c = circuit;
            pc = complex;
            foreach (LampMount m in standLamps) Stand(m);
            PitRoof();
            Gantry(gantry);
            foreach (Vector3[] run in pitWall) Masts(run, gantry);
            Share();
            Balance();
        }

        /// <summary>The masts and brackets, and the glowing lenses.</summary>
        public List<CircuitPart> Build(Material steelMaterial, Material lensMaterial)
        {
            var parts = new List<CircuitPart>();
            foreach (MeshKit chunk in steel.Chunks(150f)) parts.Add(new CircuitPart("Floodlight steel", chunk, steelMaterial, false, true));
            foreach (MeshKit chunk in lenses.Chunks(150f)) parts.Add(new CircuitPart("Floodlight lenses", chunk, lensMaterial, false, false));
            return parts;
        }

        /// <summary>
        /// The floodlights' light on a surface here with this normal, as the shaders give it: each light under the per-lamp
        /// cap, all of them under the cap together. 1 is <see cref="NightBudget.Floodlit"/> times the light's colour.
        /// </summary>
        public float Light(Vector3 at, Vector3 normal)
        {
            float sum = 0f;
            foreach (FloodSource s in Sources) sum += LampLight.Knee(Raw(s, at, normal), NightBudget.LampMost);
            return LampLight.Knee(sum, NightBudget.AllMost);
        }

        private static float Raw(in FloodSource s, Vector3 at, Vector3 normal) =>
            LampLight.Raw(s.Intensity, s.Position, s.Aim, s.Range, s.Cone, s.Core, at, normal);

        // ---------------------------------------------------------------- the heads

        private void Stand(LampMount m)
        {
            var f = new FloodHead(FloodKind.Stand, m.Position, OnGround(m.Target));
            Heads.Add(f);
            Bar(steel, m.Anchor, m.Position - f.Aim * 0.2f, 0.12f);
            Head(f, 0.8f, 0.5f);
        }

        /// <summary>Heads off the pit building's roof edge, and the race offices' higher roof, aimed down into the lane.</summary>
        private void PitRoof()
        {
            for (int j = 0; j < pc.Stations; j++)
            {
                bool office = j > pc.OfficeFrom && j < pc.OfficeTo;
                float top = office ? OfficeTop : RoofTop, front = office ? OfficeFront : RoofFront;
                Vector3 anchor = pc.RowPoint(j, top - 0.3f, front);
                Vector3 head = pc.RowPoint(j, top - 0.55f, front + 0.7f);
                var f = new FloodHead(FloodKind.PitRoof, head, OnGround(pc.RowPoint(j, 0f, LaneAim)));
                Heads.Add(f);
                Bar(steel, anchor, head - f.Aim * 0.2f, 0.12f);
                Head(f, 0.9f, 0.6f);
            }
        }

        /// <summary>Four heads under the gantry's beam, three aimed down the grid and one over the line.</summary>
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
                var f = new FloodHead(FloodKind.StartLine, lens, target);
                Heads.Add(f);
                Bar(steel, hook, lens - f.Aim * 0.2f, 0.1f);
                Head(f, 1.0f, 0.65f);
            }
        }

        /// <summary>Masts along one pit wall run, each with two heads aimed at the track either side of it.</summary>
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
                Mast(foot, new[]
                {
                    OnGround(c.Centre[c.IndexOf(Mathf.Repeat(d - 14f, c.Lap))]),
                    OnGround(c.Centre[c.IndexOf(Mathf.Repeat(d + 14f, c.Lap))]),
                });
            }
        }

        /// <summary>A mast on the pit wall with its heads on a bar across the top, the bar running along the track.</summary>
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
                var f = new FloodHead(FloodKind.PitWall, lens, targets[i]);
                Heads.Add(f);
                Bar(steel, lens - f.Aim * 0.3f + Vector3.up * 0.1f, lens - toTrack * 0.45f + Vector3.up * 0.65f, 0.1f);
                Head(f, w, h);
            }
        }

        /// <summary>A head: a housing behind a lens facing along its aim.</summary>
        private void Head(FloodHead f, float w, float h)
        {
            Vector3 z = f.Aim, x = Vector3.Cross(Vector3.up, z).normalized, y = Vector3.Cross(z, x);
            OrientedBox(steel, f.Position - z * 0.2f, x, y, z, new Vector3(w + 0.12f, h + 0.12f, 0.36f));
            Vector3 p = f.Position - z * 0.015f;
            Vector3 hx = x * (w * 0.5f), hy = y * (h * 0.5f);
            lenses.Face(p - hx - hy, p + hx - hy, p - hx + hy, p + hx + hy, p - z);
        }

        // ---------------------------------------------------------------- the lights

        /// <summary>Groups neighbouring heads of a kind, in the order they were laid out, each group one light.</summary>
        private void Share()
        {
            var group = new List<FloodHead>();
            for (int i = 0; i <= Heads.Count; i++)
            {
                bool end = i == Heads.Count;
                if (!end && group.Count > 0)
                {
                    FloodHead first = group[0], h = Heads[i];
                    if (h.Kind == first.Kind && (h.Position - first.Position).magnitude <= ShareOf(h.Kind)) { group.Add(h); continue; }
                }
                if (group.Count > 0) Sources.Add(SourceOf(group));
                group.Clear();
                if (!end) group.Add(Heads[i]);
            }
        }

        private static float ShareOf(FloodKind k) =>
            k == FloodKind.Stand ? ShareStand : k == FloodKind.PitRoof ? SharePitRoof : k == FloodKind.PitWall ? ShareMast : ShareStart;

        private static float PoolOf(FloodKind k) =>
            k == FloodKind.Stand ? PoolStand : k == FloodKind.PitRoof ? PoolPitRoof : k == FloodKind.PitWall ? PoolMast : PoolStart;

        private static FloodSource SourceOf(List<FloodHead> heads)
        {
            Vector3 p = Vector3.zero, t = Vector3.zero;
            foreach (FloodHead h in heads) { p += h.Position; t += h.Target; }
            p /= heads.Count;
            t /= heads.Count;
            // Wide enough for the pool round every head's target.
            float reach = (t - p).magnitude, pool = PoolOf(heads[0].Kind);
            Vector3 aim = (t - p).normalized;
            float half = Mathf.Atan2(pool, reach) * Mathf.Rad2Deg;
            foreach (FloodHead h in heads)
            {
                Vector3 v = h.Target - p;
                half = Mathf.Max(half, Vector3.Angle(aim, v) + Mathf.Atan2(pool * 0.5f, v.magnitude) * Mathf.Rad2Deg);
            }
            float cone = Mathf.Clamp(2f * half, 40f, 130f);
            return new FloodSource
            {
                Kind = heads[0].Kind, Position = p, Target = t, Cone = cone, Core = cone * 0.1f,
                Range = reach * RangeTimes + RangeMargin, Intensity = Mathf.Pow(reach, 2f) * NightBudget.Floodlit,
            };
        }

        /// <summary>
        /// Sets every light's intensity so that the floodlit track and pit lane take <see cref="NightBudget.Floodlit"/> as
        /// evenly as the lights can give it: a least-squares fit of every light's share over points across the road and the
        /// lane wherever the floodlights reach, no light below nothing (Lee and Seung's multiplicative updates).
        /// </summary>
        private void Balance()
        {
            var points = new List<Vector3>();
            for (int k = 0; k < c.Count; k += Mathf.Max(1, Mathf.RoundToInt(SampleStep / c.Step)))
                foreach (float lat in new[] { -0.7f, 0f, 0.7f })
                    points.Add(c.Surface(k, lat * c.HalfWidth(k)));
            for (int i = 0; i < c.Lane.Length; i++) points.Add(OnGround(c.Lane[i]));
            int n = Sources.Count;
            // Each light's share at each point, per unit of intensity, for the points the lights as first set light at all.
            var rows = new List<float[]>();
            foreach (Vector3 p in points)
            {
                var row = new float[n];
                float first = 0f;
                for (int i = 0; i < n; i++)
                {
                    FloodSource s = Sources[i];
                    row[i] = LampLight.Raw(1f, s.Position, s.Aim, s.Range, s.Cone, s.Core, p, Vector3.up);
                    first += row[i] * s.Intensity;
                }
                if (first >= Covered * NightBudget.Floodlit) rows.Add(row);
            }
            var x = new float[n];
            var top = new float[n];
            for (int i = 0; i < n; i++) x[i] = Sources[i].Intensity;
            foreach (float[] row in rows)
                for (int i = 0; i < n; i++) top[i] += row[i] * NightBudget.Floodlit;
            var sum = new float[rows.Count];
            var bottom = new float[n];
            for (int round = 0; round < Rounds; round++)
            {
                for (int j = 0; j < rows.Count; j++)
                {
                    float e = 0f;
                    float[] row = rows[j];
                    for (int i = 0; i < n; i++) e += row[i] * x[i];
                    sum[j] = e;
                }
                System.Array.Clear(bottom, 0, n);
                for (int j = 0; j < rows.Count; j++)
                {
                    float[] row = rows[j];
                    for (int i = 0; i < n; i++) bottom[i] += row[i] * sum[j];
                }
                for (int i = 0; i < n; i++) x[i] *= top[i] / Mathf.Max(bottom[i], 1e-12f);
            }
            for (int i = 0; i < n; i++)
            {
                FloodSource s = Sources[i];
                s.Intensity = x[i];
                Sources[i] = s;
            }
        }

        // ---------------------------------------------------------------- the ground

        private Vector3 OnGround(Vector3 p)
        {
            p.y = Ground(p);
            return p;
        }

        /// <summary>Height of whatever is underfoot at a point: the road, the pit lane, or the ground.</summary>
        public float Ground(Vector3 p)
        {
            float y = c.TerrainHeight(p.x, p.z);
            int k = NearestTrack(p, -1);
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
        private static void Bar(MeshKit kit, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 z = (b - a).normalized;
            Vector3 x = Mathf.Abs(z.y) > 0.95f ? Vector3.right : Vector3.Cross(Vector3.up, z).normalized;
            Vector3 y = Vector3.Cross(z, x);
            OrientedBox(kit, (a + b) * 0.5f, x, y, z, new Vector3(thickness, thickness, (b - a).magnitude));
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;
        private static float Flat2(Vector3 v) => v.x * v.x + v.z * v.z;
    }
}
