using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit's meshes from <see cref="Circuit"/>: road and FIA markings, kerbs,
    /// verges, runoff, barriers, pit lane, pit wall, garages, grid and the ground around it.
    /// </summary>
    public sealed class CircuitBuilder
    {
        // Lateral layout either side of the road edge, metres.
        private const float EdgeLine = 0.12f;      // FIA: continuous white line, at least 10 cm
        private const float KerbWidth = 1.5f;
        private const float KerbStripe = 1.2f;     // length of each red or white block
        private const float Verge = 2f;            // FIA: compact verge, 1 to 5 m
        private const float GrassRunoff = 30f;     // FIA: runoff usually 30 to 100 m deep
        private const float GravelRunoff = 45f;
        private const float GravelMaxRadius = 120f;
        private const float RunoffFall = 0.015f;   // FIA: no steeper than 3% downwards
        private const float ShoulderWidth = 5f;    // grass beside the pit lane where no garage stands
        private const float RunoffEase = 8f;       // runoff takes this far past the verge to settle onto the ground
        private const float Skirt = 1.6f;          // hides the seams down to the ground
        private const float Paint = 0.012f;        // paint sits this far above the surface
        private const float PitWallGap = 6f;       // the pit wall starts once the lane is this far off the track
        private const float LineAcross = 0.3f;     // thickness of lines painted across a surface
        private const float LaneWiden = 1.2f;      // pit lane surface runs this far past its track-side line
        private const float FenceTurn = 20f;       // most a fence turns from one track sample to the next, degrees
        private const float GarageTuck = 0.063f;   // the shoulder in front of a garage lies this far under the lane edge, about 4 cm under the floor
        private const float EdgeArmcoMin = 6f;     // runoff cut short still gets armco at its edge when at least this deep
        private const float EdgeArmcoStep = 1f;    // ...unless its edge moves less than this from one track sample to the next
        // Tyre wall texture: one tile spans the stack's whole height, top to buried foot, and two tyres along it.
        public const float TyreTop = 1.0f, TyreFoot = 0.5f, TyreTileAlong = 1.2f;

        private readonly Circuit c;
        private readonly CircuitMaterials m;
        private readonly List<CircuitPart> parts = new List<CircuitPart>();
        private readonly int n;
        private readonly float laneHalf;

        // Per side (0 = left, 1 = right), per sample, measured outwards from the road edge.
        private readonly float[][] barrier = { null, null };
        private readonly bool[][] fenced = { null, null };
        private readonly bool[][] edgeArmco = { null, null };  // armco at the edge of runoff cut short, standing on the ground
        private readonly float[][] rooms = { null, null };
        private readonly float[][] wants = { null, null };
        private readonly float[][] gravel = { null, null };
        private readonly bool[][] pitWall = { null, null };
        private readonly bool[][] open = { null, null };   // no verge, runoff or barrier (pit road joins here)
        private readonly float[][] endEase = { null, null };   // how far runoff takes to settle, shorter beside an opening
        private readonly float[][] level = { null, null };     // 1 where runoff stays level with the road, beside a paved opening
        private readonly float[] laneGap;                  // gap between left road edge and pit lane edge, or NaN
        private readonly bool[] joining;                   // the pit entry or exit road meets the track here
        private readonly float[] trackToLane;              // straight-line gap from the left road edge to the pit lane surface, or NaN
        private readonly float[] laneToTrack;              // per lane sample: gap from the lane surface's track side to the nearest road edge
        private readonly int[] laneNearest;                // per lane sample: the nearest track sample

        /// <summary>Each garage's floor height, as laid. Filled by <see cref="Build"/>.</summary>
        public float[] GarageFloors { get; private set; }
        /// <summary>The pit wall's runs, each the middle of its top from one lane sample to the next.</summary>
        public readonly List<Vector3[]> PitWallRuns = new List<Vector3[]>();
        /// <summary>The armco's runs, each along the line where the runoff meets it. Filled by <see cref="Build"/>.</summary>
        public readonly List<ArmcoRun> ArmcoRuns = new List<ArmcoRun>();

        /// <summary>How far out from the road edge the runoff reaches on a side (0 = left), or 0 where the side is open.</summary>
        public float RunoffReach(int side, int k) => open[side][k] ? 0f : barrier[side][k];

        /// <summary>True where armco or a pit wall stands on a side (0 = left) at <see cref="RunoffReach"/>.</summary>
        public bool Fenced(int side, int k) => !open[side][k] && (Armco(side, k) || pitWall[side][k]);

        /// <summary>True where armco stands on a side (0 = left) at sample k.</summary>
        private bool Armco(int side, int k) => (fenced[side][k] || edgeArmco[side][k]) && !pitWall[side][k] && !open[side][k];

        /// <summary>True where the barrier on a side (0 = left) is the pit wall.</summary>
        public bool PitWallAt(int side, int k) => !open[side][k] && pitWall[side][k];

        /// <summary>A point on the verge or runoff as laid, at a lateral offset past the road edge.</summary>
        public Vector3 RunoffPoint(int k, float lateral) => Point(k, lateral, 0f, true);

        /// <param name="materials">May be null outside Unity (tests and previews).</param>
        public CircuitBuilder(Circuit circuit, CircuitMaterials materials)
        {
            c = circuit;
            m = materials;
            n = c.Count;
            laneHalf = c.Data.pit.laneWidth * 0.5f;
            laneGap = new float[n];
            for (int k = 0; k < n; k++)
            {
                float lat = c.LaneLateral[k];
                if (float.IsNaN(lat))
                {
                    float a = c.LaneLateral[c.Wrap(k - 1)], b = c.LaneLateral[c.Wrap(k + 1)];
                    if (!float.IsNaN(a) && !float.IsNaN(b)) lat = (a + b) * 0.5f;
                }
                laneGap[k] = float.IsNaN(lat) ? float.NaN : lat - laneHalf - c.HalfWidth(k);
            }

            PitData pit = c.Data.pit;
            joining = new bool[n];
            for (int k = 0; k < n; k++)
            {
                float d = k * c.Step;
                bool nearEnds = c.Ahead(pit.entry, d) < 400f || c.Ahead(d, pit.exit) < 400f;
                // The lane starts and ends on a sample. Those two rows still get their runoff, which stops
                // there along the lane's own end; only the rows strictly between are the lane's.
                float half = c.Step * 0.5f;
                bool alongLane = c.Ahead(pit.entry + half, d) <= c.Ahead(pit.entry + half, pit.exit - half);
                if (!alongLane) laneGap[k] = float.NaN;
                joining[k] = !float.IsNaN(laneGap[k]) && laneGap[k] < PitWallGap && nearEnds;
            }

            // Measured as the crow flies, because lateral offsets swing about wherever the track
            // bends hard beside the pit lane (the chicane bypass).
            Vector3[] lane = c.Lane;
            trackToLane = new float[n];
            for (int k = 0; k < n; k++)
            {
                trackToLane[k] = float.NaN;
                if (float.IsNaN(laneGap[k])) continue;
                float best = float.MaxValue;
                foreach (Vector3 q in lane) best = Mathf.Min(best, Flat(c.Centre[k], q));
                trackToLane[k] = best - c.HalfWidth(k) - laneHalf - LaneWiden;
            }
            laneToTrack = new float[lane.Length];
            laneNearest = new int[lane.Length];
            for (int i = 0; i < lane.Length; i++)
            {
                float best = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    float g = Flat(c.Centre[k], lane[i]) - c.HalfWidth(k);
                    if (g < best) { best = g; laneNearest[i] = k; }
                }
                laneToTrack[i] = best - laneHalf - LaneWiden;
            }
            PlanSides();
        }

        public List<CircuitPart> Build()
        {
            // Lay everything and sink the ground wherever it pokes through, then lay it all again on the sunk
            // ground, until the ground stops moving. Draped surfaces follow the ground exactly, so only things
            // at a height of their own, such as the road through a cutting, ever sink it.
            for (int pass = 0; pass < 8; pass++)
            {
                parts.Clear();
                Lay();
                float moved = SinkUnderCover();
                if (moved < 0.01f) break;
            }
            Ground();
            return parts;
        }

        private void Lay()
        {
            Road();
            EdgeLines();
            Kerbs();
            Runoff();
            Barriers();
            PitLane();
            PitEntryAndExit();
            Garages();
            Paddock();
            LinesAcross();
            Grid();
        }

        // ---------------------------------------------------------------- planning

        private void PlanSides()
        {
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                float[] clear = s == 0 ? c.Data.clearLeft : c.Data.clearRight;
                var want = new float[n];
                var grav = new float[n];
                for (int k = 0; k < n; k++) want[k] = Verge + GrassRunoff;

                // Gravel on the outside of slow and medium corners, through the corner and past its exit.
                foreach (CornerData corner in c.Data.corners)
                    foreach (ArcData arc in corner.arcs)
                    {
                        if (arc.radius > GravelMaxRadius) continue;
                        float outside = arc.dir > 0 ? 1 : -1; // a right-hander runs wide to the left
                        if (outside != sign) continue;
                        foreach (int k in Span(arc.s - 15f, arc.e + 70f))
                        {
                            want[k] = Verge + GravelRunoff + 2f;
                            grav[k] = Verge + GravelRunoff;
                        }
                    }

                want = Smooth(MaxFilter(want, 15), 10);
                grav = Smooth(grav, 4);
                // On the inside of a bend, stop short of the bend's centre so the ground doesn't fold over.
                var room = new float[n];
                for (int k = 0; k < n; k++) room[k] = Mathf.Min(clear[k] * 0.5f - 0.5f, InsideRoom(k, sign));
                room = MinFilter(room, 12);
                var inside = new float[n];
                for (int k = 0; k < n; k++) inside[k] = InsideRoom(k, sign);
                inside = MinFilter(inside, 12);
                // The pit lane's closeness reaches a little past its ends; there the road still needs a
                // verge and some grass beside it rather than a bare edge.
                PitData pd = c.Data.pit;
                for (int k = 0; k < n; k++)
                    if (s == 0 && c.Ahead(pd.entry, k * c.Step) > c.Ahead(pd.entry, pd.exit)) room[k] = Mathf.Max(room[k], Verge + 1f);
                var bar = new float[n];
                var wall = new bool[n];
                var shut = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    float limit = room[k];
                    bar[k] = Mathf.Max(0f, Mathf.Min(want[k], limit));
                    if (s == 0 && !float.IsNaN(laneGap[k]))
                    {
                        // The pit wall runs along the lane, so the runoff just reaches over to it.
                        if (joining[k]) shut[k] = true;
                        else
                        {
                            // The lane itself is what the room was measured to, so only the bend limits it here.
                            wall[k] = true;
                            float toLane = Mathf.Min(trackToLane[k], laneGap[k] - LaneWiden);
                            bar[k] = Mathf.Max(0f, Mathf.Min(inside[k], toLane - 0.2f));
                        }
                    }
                    // Too little room even for a verge and some grass: leave the road edge bare, unless a pit
                    // wall stands there, in which case the verge narrows to meet it.
                    if (bar[k] < Verge + 0.5f && !wall[k]) shut[k] = true;
                }
                barrier[s] = bar;
                gravel[s] = grav;
                pitWall[s] = wall;
                open[s] = shut;
                rooms[s] = room;
                wants[s] = want;
            }

            ResolveOverlaps();
            for (int s = 0; s < 2; s++)
            {
                Unkink(barrier[s], rooms[s], pitWall[s], s == 0 ? 1 : -1);
                // No gravel between the pit wall and the track: the wall side is grass only.
                for (int k = 0; k < n; k++) gravel[s][k] = pitWall[s][k] ? 0f : Mathf.Min(gravel[s][k], barrier[s][k] - 1.5f);
                fenced[s] = Fences(barrier[s], wants[s], open[s]);
                edgeArmco[s] = EdgeFences(barrier[s], fenced[s], pitWall[s], open[s], s == 0 ? 1 : -1);
                // Where the runoff stops for an opening, such as a pit road join, its end row settles onto
                // the ground within a shoulder's width, as the pit lane's own shoulder does, so the two ends
                // meet and nothing stands proud of the ground across the end. It eases back over a few rows.
                // Where the opening is paved right beside the road, as where the pit lane runs alongside, the
                // runoff instead stays level with the road towards its end, so it meets the paving flush.
                endEase[s] = new float[n];
                level[s] = new float[n];
                for (int k = 0; k < n; k++)
                {
                    endEase[s][k] = RunoffEase;
                    for (int d = 1; d <= 4; d++)
                    {
                        int a = c.Wrap(k - d), b = c.Wrap(k + d);
                        if (!open[s][a] && !open[s][b]) continue;
                        if (s == 0 && (PavedBeside(a) || PavedBeside(b))) level[s][k] = 1f - (d - 1) / 4f;
                        else endEase[s][k] = Mathf.Lerp(ShoulderWidth - Verge, RunoffEase, (d - 1) / 4f);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Pulls runoff in wherever it would reach over another stretch of track's runoff, as two legs that
        /// converge at an angle can, so no two runoffs lie on top of each other.
        /// </summary>
        private void ResolveOverlaps()
        {
            const float cell = 20f;
            var cells = new Dictionary<(int, int), List<int>>();
            for (int k = 0; k < n; k++)
            {
                var key = ((int)Mathf.Floor(c.Centre[k].x / cell), (int)Mathf.Floor(c.Centre[k].z / cell));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(k);
            }
            int apart = Mathf.CeilToInt(60f / c.Step);   // far enough along the lap to be another leg, even round a hairpin
            for (int round = 0; round < 30; round++)
            {
                bool moved = false;
                for (int s = 0; s < 2; s++)
                    for (int k = 0; k < n; k++)
                    {
                        if (open[s][k] || barrier[s][k] <= 0f) continue;
                        float sign = s == 0 ? 1 : -1;
                        Vector3 o = c.Surface(k, sign * (c.HalfWidth(k) + barrier[s][k]));
                        int cx = (int)Mathf.Floor(o.x / cell), cz = (int)Mathf.Floor(o.z / cell);
                        int best = -1;
                        float bestD = float.MaxValue;
                        for (int i = -3; i <= 3; i++)
                            for (int j = -3; j <= 3; j++)
                                if (cells.TryGetValue((cx + i, cz + j), out var list))
                                    foreach (int q in list)
                                    {
                                        int gap = Math.Abs(q - k);
                                        if (Math.Min(gap, n - gap) < apart) continue;
                                        // Another leg lies beside this one, not further along the same bend.
                                        Vector3 across = c.Centre[q] - c.Centre[k];
                                        across.y = 0;
                                        if (Mathf.Abs(Vector3.Dot(across, c.Forward[k])) > 0.5f * across.magnitude) continue;
                                        float dx = c.Centre[q].x - o.x, dz = c.Centre[q].z - o.z;
                                        float d = dx * dx + dz * dz;
                                        if (d < bestD) { bestD = d; best = q; }
                                    }
                        if (best < 0) continue;
                        float lat = Vector3.Dot(o - c.Centre[best], c.Left[best]);
                        int os = lat >= 0 ? 0 : 1;
                        float reach = c.HalfWidth(best) + (open[os][best] ? 0f : barrier[os][best]);
                        float over = reach + 0.5f - Mathf.Abs(lat);
                        if (over <= 0f) continue;
                        barrier[s][k] = Mathf.Max(0f, barrier[s][k] - over * 0.5f - 0.1f);
                        if (!open[os][best]) barrier[os][best] = Mathf.Max(0f, barrier[os][best] - over * 0.5f - 0.1f);
                        moved = true;
                    }
                if (!moved) break;
            }
        }

        /// <summary>
        /// Rounds off the barrier line wherever it would turn more than <see cref="FenceTurn"/> degrees
        /// from one sample to the next. A fence far out on the inside of a bend moves only a little
        /// per sample, so the corner where the runoff starts to narrow shows up there as a sharp kink.
        /// </summary>
        private void Unkink(float[] bar, float[] room, bool[] wall, float sign)
        {
            float most = Mathf.Cos(FenceTurn * Mathf.PI / 180f);
            for (int round = 0; round < 200; round++)
            {
                bool moved = false;
                for (int k = 0; k < n; k++)
                {
                    int a = c.Wrap(k - 1), b = c.Wrap(k + 1);
                    if (wall[k] || wall[a] || wall[b]) continue;
                    Vector3 p = Fence(k, bar, sign), d1 = p - Fence(a, bar, sign), d2 = Fence(b, bar, sign) - p;
                    d1.y = 0; d2.y = 0;
                    if (Vector3.Dot(d1.normalized, d2.normalized) >= most) continue;
                    float mid = (bar[a] + bar[b]) * 0.5f;
                    bar[k] = Mathf.Max(0f, Mathf.Min(mid, room[k]));
                    moved = true;
                }
                if (!moved) break;
            }
        }

        private Vector3 Fence(int k, float[] bar, float sign) => c.Surface(k, sign * (c.HalfWidth(k) + bar[k]));

        /// <summary>
        /// Armco only where the runoff reached its full width. Where another leg of the track or the inside
        /// of a bend cut it short, the runoff just meets the ground, as in a hairpin's infield. Pieces
        /// shorter than 40 m are dropped so no stray bits of fence stand on their own.
        /// </summary>
        private bool[] Fences(float[] bar, float[] want, bool[] shut)
        {
            var f = new bool[n];
            for (int k = 0; k < n; k++) f[k] = !shut[k] && bar[k] >= want[k] - 0.5f;
            int minRun = Mathf.CeilToInt(40f / c.Step);
            var keep = new bool[n];
            foreach (List<int> run in Runs(k => f[k]))
                if (run.Count >= minRun) foreach (int k in run) keep[k] = true;
            return keep;
        }

        /// <summary>
        /// Armco along the rest of the lap too, where another leg of the track or the inside of a bend cut the
        /// runoff short: it stands at the runoff's edge, on the ground the runoff has settled onto. Only where
        /// there is room for the verge and some grass in front of it, and in pieces at least 40 m long.
        /// </summary>
        private bool[] EdgeFences(float[] bar, bool[] full, bool[] wall, bool[] shut, float sign)
        {
            var f = new bool[n];
            for (int k = 0; k < n; k++) f[k] = !shut[k] && !wall[k] && !full[k] && bar[k] >= EdgeArmcoMin;
            // Where the runoff's edge turns sharply or bunches up, as where its room runs out on the inside of a
            // bend, the armco leaves a gap.
            float most = Mathf.Cos(FenceTurn * Mathf.PI / 180f);
            var kink = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int a = c.Wrap(k - 1), b = c.Wrap(k + 1);
                Vector3 p = Fence(k, bar, sign), d1 = p - Fence(a, bar, sign), d2 = Fence(b, bar, sign) - p;
                d1.y = 0; d2.y = 0;
                kink[k] = Vector3.Dot(d1.normalized, d2.normalized) < most || d1.magnitude < EdgeArmcoStep || d2.magnitude < EdgeArmcoStep;
            }
            for (int k = 0; k < n; k++)
                if (kink[k])
                    for (int d = -1; d <= 1; d++) f[c.Wrap(k + d)] = false;
            int minRun = Mathf.CeilToInt(40f / c.Step);
            var keep = new bool[n];
            foreach (List<int> run in Runs(k => f[k]))
                if (run.Count >= minRun) foreach (int k in run) keep[k] = true;
            return keep;
        }

        /// <summary>
        /// Room beyond the road edge on one side before reaching the centre of the bend,
        /// or no limit if that side is the outside of the bend.
        /// </summary>
        private float InsideRoom(int k, float sign)
        {
            const int reach = 5;
            Vector3 a = c.Forward[c.Wrap(k - reach)], b = c.Forward[c.Wrap(k + reach)];
            float turn = Vector3.Cross(a, b).y; // positive when turning right
            float angle = Mathf.Asin(Mathf.Clamp(Mathf.Abs(turn), 0f, 1f));
            if (angle < 1e-3f) return float.MaxValue;
            float radius = 2 * reach * c.Step / angle;
            bool inside = turn > 0 ? sign < 0 : sign > 0;
            if (!inside) return float.MaxValue;
            return (radius - c.HalfWidth(k)) * 0.8f;
        }

        // ---------------------------------------------------------------- road and lines

        private void Road()
        {
            var kit = new MeshKit();
            var rows = new List<Vector3[]>();
            for (int k = 0; k <= n; k++)
            {
                int i = c.Wrap(k);
                float h = c.HalfWidth(i);
                rows.Add(new[] { c.Surface(i, h), c.Surface(i, -h) });
            }
            kit.Strip(rows, 4f, 4f);
            Emit("Road", kit, m?.Asphalt, true);

            var skirts = new MeshKit();
            foreach (float sign in new[] { 1f, -1f })
            {
                var side = new List<Vector3[]>();
                for (int k = 0; k <= n; k++)
                {
                    int i = c.Wrap(k);
                    Vector3 top = c.Surface(i, sign * c.HalfWidth(i));
                    side.Add(new[] { top, top + Vector3.down * Skirt });
                }
                skirts.Strip(side, 4f, 4f, true);
            }
            Emit("Road edges", skirts, m?.RoadEdge);
        }

        /// <summary>
        /// How far along the road's surface each track sample lies, once more back at the start: the road's uv along
        /// it, in metres, as <see cref="MeshKit.Strip"/> measures it between the middles of the road's rows.
        /// </summary>
        public static float[] RoadAlong(Circuit c)
        {
            int n = c.Count;
            var along = new float[n + 1];
            Vector3 last = Vector3.zero;
            for (int k = 0; k <= n; k++)
            {
                int i = c.Wrap(k);
                float h = c.HalfWidth(i);
                Vector3 mid = (c.Surface(i, h) + c.Surface(i, -h)) * 0.5f;
                if (k > 0) along[k] = along[k - 1] + Vector3.Distance(mid, last);
                last = mid;
            }
            return along;
        }

        private void EdgeLines()
        {
            var kit = new MeshKit();
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                // FIA: the edge line breaks only where the pit entry and exit roads join.
                bool Draw(int k) => s == 1 || !joining[k];
                foreach (List<int> run in Runs(Draw))
                    kit.Strip(Rows(run, k => new[] { sign > 0 ? c.HalfWidth(k) : -(c.HalfWidth(k) - EdgeLine),
                                                     sign > 0 ? c.HalfWidth(k) - EdgeLine : -c.HalfWidth(k) }, Paint), 1f, 1f);
            }
            Emit("Edge lines", kit, m?.White);
        }

        private void Kerbs()
        {
            var kit = new MeshKit();
            foreach (CornerData corner in c.Data.corners)
                foreach (ArcData arc in corner.arcs)
                {
                    float len = c.Ahead(arc.s, arc.e);
                    float mid = arc.s + len * 0.5f;
                    float half = Mathf.Max(12f, len * 0.35f);
                    float outside = arc.dir > 0 ? 1 : -1;
                    // Apex kerb on the inside, exit kerb on the outside.
                    KerbRun(kit, -outside, mid - half, mid + half);
                    KerbRun(kit, outside, arc.s + len * 0.6f, arc.e + 25f);
                }
            Emit("Kerbs", kit, m?.Kerb, true);
        }

        private void KerbRun(MeshKit kit, float sign, float from, float to)
        {
            int s = sign > 0 ? 0 : 1;
            var ks = new List<int>();
            foreach (int k in Span(from, to))
                if (!open[s][k] || s == 1) ks.Add(k);
            if (ks.Count < 2) return;
            var rows = new List<Vector3[]>();
            foreach (int k in ks)
            {
                float e = c.HalfWidth(k);
                Vector3 inner = c.Surface(k, sign * e) + Vector3.up * (Paint + 0.005f);
                Vector3 outer = Point(k, sign * (e + KerbWidth), 0.05f, true);   // 5 cm proud of the verge it sits on
                rows.Add(sign > 0 ? new[] { outer, inner } : new[] { inner, outer });
            }
            // u runs from the road's edge out, on either side, so wear lies along the edge the tyres ride over.
            kit.Strip(rows, KerbWidth, KerbStripe * 2f, flipAcross: sign > 0);
        }

        // ---------------------------------------------------------------- runoff and barriers

        private void Runoff()
        {
            var verge = new MeshKit();
            var grass = new MeshKit();
            var grav = new MeshKit();
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                int side = s;
                foreach (List<int> run in Runs(k => !open[side][k]))
                {
                    verge.Strip(Rows(run, k => Across(sign, c.HalfWidth(k), c.HalfWidth(k) + Mathf.Min(Verge, barrier[side][k])), 0f, true), 2f, 2f);
                    // Grass starts where the gravel ends, so the two never lie on top of each other.
                    Band(grass, run, sign, k => c.HalfWidth(k) + Mathf.Min(GravelEdge(side, k), barrier[side][k]), k => c.HalfWidth(k) + barrier[side][k], 0f, 6f);
                }
                // Each gravel trap reaches one row further at both ends, narrowing to nothing there, so its
                // edge runs along the grass's inner edge with no gap where the trap starts or stops.
                foreach (List<int> run in Runs(k => !open[side][k] && (HasGravel(side, k) || HasGravel(side, c.Wrap(k - 1)) || HasGravel(side, c.Wrap(k + 1)))))
                    Band(grav, run, sign, k => c.HalfWidth(k) + Verge, k => c.HalfWidth(k) + GravelEdge(side, k), 0.03f, 3f);
            }
            Emit("Verges", verge, m?.Verge, true);
            Emit("Grass runoff", grass, m?.Grass, true);
            Emit("Gravel traps", grav, m?.Gravel, true);
        }

        private bool HasGravel(int side, int k) => gravel[side][k] > Verge + 1f;

        /// <summary>True where a tyre wall stands in front of the armco: behind a gravel trap.</summary>
        private bool TyreWall(int side, int k) => fenced[side][k] && !pitWall[side][k] && HasGravel(side, k);

        /// <summary>How far from the road edge the gravel reaches, or just the verge where there is none.</summary>
        private float GravelEdge(int side, int k) => HasGravel(side, k) ? gravel[side][k] : Verge;

        private void Barriers()
        {
            var armco = new MeshKit();
            var tyres = new MeshKit();
            var wall = new MeshKit();
            ArmcoRuns.Clear();
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                int side = s;
                foreach (List<int> run in Runs(k => Armco(side, k)))
                {
                    armco.Strip(Vertical(run, k => c.HalfWidth(k) + barrier[side][k], sign, -Skirt, 0.8f), 4f, 4f, true);
                    var line = new Vector3[run.Count];
                    var stack = new bool[run.Count];
                    for (int i = 0; i < run.Count; i++)
                    {
                        line[i] = Point(run[i], sign * (c.HalfWidth(run[i]) + barrier[side][run[i]]), 0, true);
                        stack[i] = TyreWall(side, run[i]);
                    }
                    ArmcoRuns.Add(new ArmcoRun(side, run.ToArray(), line, stack));
                }

                // Tyre walls in front of the armco where a gravel trap catches cars. The texture's across runs down
                // the stack's faces from the top, and the top takes the plain rubber at the bottom of the tile.
                foreach (List<int> run in Runs(k => TyreWall(side, k)))
                {
                    float Front(int k) => c.HalfWidth(k) + barrier[side][k] - 1.2f;
                    float Back(int k) => c.HalfWidth(k) + barrier[side][k] - 0.05f;
                    Vector2 rubber = new Vector2(0.97f, 0.5f);
                    float height = TyreTop + TyreFoot;
                    tyres.Strip(Rows(run, k => Across(sign, Front(k), Back(k)), TyreTop, true), 1f, 1f, false, null, p => rubber);
                    tyres.Strip(Vertical(run, Front, sign, -TyreFoot, TyreTop), height, TyreTileAlong, true);
                    tyres.Strip(Vertical(run, Back, sign, -TyreFoot, TyreTop), height, TyreTileAlong, true);
                    // Close both ends of the stack.
                    foreach ((int end, int inward) in new[] { (run[0], run[1]), (run[run.Count - 1], run[run.Count - 2]) })
                    {
                        Vector3 f = Point(end, sign * Front(end), 0, true), b = Point(end, sign * Back(end), 0, true);
                        tyres.Face(f + Vector3.up * -TyreFoot, b + Vector3.up * -TyreFoot, f + Vector3.up * TyreTop, b + Vector3.up * TyreTop,
                            Point(inward, sign * Front(inward), 0.5f, true), rubber);
                    }
                }

            }
            Emit("Armco", armco, m?.Armco, true);
            Emit("Tyre walls", tyres, m?.Tyres, true);

            // Pit wall: concrete, following the pit lane on its track side wherever the lane is apart from the track.
            float inner = -laneHalf - LaneWiden;
            PitWallRuns.Clear();
            foreach (List<int> run in LaneRuns(HasPitWall))
            {
                var top = new List<Vector3[]>();
                var near = new List<Vector3[]>();
                var far = new List<Vector3[]>();
                foreach (int i in run)
                {
                    Vector3 a = Squared(i, LanePoint(i, inner, 1.2f)), b = Squared(i, LanePoint(i, inner - 0.6f, 1.2f));
                    top.Add(new[] { a, b });
                    near.Add(new[] { a, a + Vector3.down * (1.2f + Skirt) });
                    far.Add(new[] { b, b + Vector3.down * (1.2f + Skirt) });
                }
                var middle = new Vector3[top.Count];
                for (int t = 0; t < top.Count; t++) middle[t] = (top[t][0] + top[t][1]) * 0.5f;
                PitWallRuns.Add(middle);
                wall.Strip(top, 1f, 1f);
                wall.Strip(near, 2f, 2f, true);
                wall.Strip(far, 2f, 2f, true);
                // Close both ends of the wall.
                foreach ((int end, int inward) in new[] { (0, 1), (run.Count - 1, run.Count - 2) })
                {
                    Vector3 a = top[end][0], b = top[end][1];
                    Vector3 down = Vector3.down * (1.2f + Skirt);
                    wall.Face(a + down, b + down, a, b, (top[inward][0] + top[inward][1]) * 0.5f + Vector3.down * 0.5f);
                }
            }
            Emit("Pit wall", wall, m?.Concrete, true);
        }

        // ---------------------------------------------------------------- pit lane

        private Vector3 LaneLeft(int i)
        {
            Vector3[] lane = c.Lane;
            Vector3 t = lane[Mathf.Min(i + 1, lane.Length - 1)] - lane[Mathf.Max(i - 1, 0)];
            t.y = 0;
            t.Normalize();
            return new Vector3(-t.z, 0, t.x);
        }

        /// <summary>A point across the pit lane at sample i, on the road surface level.</summary>
        private Vector3 LanePoint(int i, float lateral, float lift)
        {
            Vector3 p = c.Lane[i] + LaneLeft(i) * lateral;
            int k = c.IndexOf(c.Data.pit.laneTrackDistance[i]);
            // Where the lane runs close to the road, take the track sample right beside the point, so the
            // two meet flush. Further off, the lane's own track sample is the steadier guide.
            int beside = TrackSampleBeside(p, k);
            if (Vector3.Dot(p - c.Centre[beside], c.Left[beside]) < c.HalfWidth(beside) + 3f) k = beside;
            float lat = Vector3.Dot(p - c.Centre[k], c.Left[k]);
            float edge = c.HalfWidth(k);
            if (lat < edge)
            {
                // Where the lane meets the track, stop it at the road edge instead of painting over the road.
                p += c.Left[k] * (edge - lat);
                lat = edge;
            }
            p.y = PlaneBeside(p, k) + 0.01f + lift;
            return p;
        }

        /// <summary>Height of the track's plane right beside p, from sample k carried along the track's own
        /// grade, so the lane meets the road flush wherever the two touch.</summary>
        private float PlaneBeside(Vector3 p, int k)
        {
            float lat = Mathf.Max(Vector3.Dot(p - c.Centre[k], c.Left[k]), c.HalfWidth(k));
            float along = Vector3.Dot(p - c.Centre[k], c.Forward[k]);
            int next = c.Wrap(along >= 0 ? k + 1 : k - 1);
            float t = Mathf.Abs(along) / c.Step;
            return Mathf.Lerp(c.Surface(k, lat).y, c.Surface(next, lat).y, t);
        }

        /// <summary>True where the pit wall stands at lane sample i: wherever the lane is apart from the track.</summary>
        private bool HasPitWall(int i) => !joining[LaneTrackIndex(i)] && laneToTrack[i] > 1f;

        /// <summary>True where the lane joins the track at lane sample i: the track's left side is open there.</summary>
        private bool Joined(int i) => open[0][LaneTrackIndex(i)];

        /// <summary>True where the lane between samples seg and seg + 1 widens out to the road edge.</summary>
        private bool Wide(int seg) => Joined(seg) || Joined(seg + 1);

        /// <summary>
        /// Where the lane widens to the road or narrows again at sample i, the runoff ends squarely on the
        /// track's row there. A point across the lane is slid along the lane onto that row, so the lane and
        /// its shoulder end on the same line as the runoff. Elsewhere it is left as it is.
        /// </summary>
        private Vector3 Squared(int i, Vector3 p)
        {
            if (i <= 0 || i >= c.Lane.Length - 1 || Wide(i - 1) == Wide(i)) return p;
            int k = LaneTrackIndex(i);
            Vector3 along = new Vector3(LaneLeft(i).z, 0, -LaneLeft(i).x);
            float off = Vector3.Dot(p - c.Centre[k], c.Forward[k]);
            float rate = Vector3.Dot(along, c.Forward[k]);
            if (Mathf.Abs(rate) < 0.5f) return p;
            Vector3 q = p - along * (off / rate);
            q.y += PlaneBeside(q, k) - PlaneBeside(p, k);   // the same height over the track's plane
            return q;
        }

        /// <summary>True where the pit lane's paving runs right beside the road's left edge at sample k.</summary>
        private bool PavedBeside(int k) => joining[k] && c.LaneLateral[k] + laneHalf - c.HalfWidth(k) > 1f;

        /// <summary>The track sample whose cross-section passes closest to p, searching near a first guess.</summary>
        private int TrackSampleBeside(Vector3 p, int guess)
        {
            int best = guess;
            float bestAlong = float.MaxValue;
            for (int d = -15; d <= 15; d++)
            {
                int k = c.Wrap(guess + d);
                float along = Mathf.Abs(Vector3.Dot(p - c.Centre[k], c.Forward[k]));
                if (along < bestAlong) { bestAlong = along; best = k; }
            }
            return best;
        }

        /// <summary>True where lane sample i runs past the front of a garage.</summary>
        private bool InFrontOfGarage(int i)
        {
            PitData pit = c.Data.pit;
            Vector3 p = c.Lane[i];
            foreach (GarageData g in pit.garages)
            {
                Vector3 d = p - new Vector3(g.x, g.y, g.z);
                d.y = 0;
                float rad = g.heading * Mathf.Deg2Rad;
                var along = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                if (Mathf.Abs(Vector3.Dot(d, along)) < pit.garageWidth * 0.5f + 1f && d.magnitude < pit.garageDepth + 20f) return true;
            }
            return false;
        }

        private bool InLimitZone(int i)
        {
            PitData pit = c.Data.pit;
            return c.Ahead(pit.limitStart, pit.laneTrackDistance[i]) <= c.Ahead(pit.limitStart, pit.limitEnd);
        }

        /// <summary>True where the pit lane is clear of the track (not overlapping the road).</summary>
        private bool LaneClear(int i)
        {
            int k = c.IndexOf(c.Data.pit.laneTrackDistance[i]);
            return float.IsNaN(laneGap[k]) || laneGap[k] > 0f;
        }

        private void PitLane()
        {
            PitData pit = c.Data.pit;
            int count = c.Lane.Length;
            var surface = new MeshKit();
            // Where the lane joins the track its surface carries on right to the road edge, so the two meet
            // flush. It does so from the last row of the track's runoff before the join to the first row
            // after, and changes width squarely on those rows, so it meets the runoff's end edge exactly.
            int from = 0;
            for (int seg = 1; seg <= count - 1; seg++)
            {
                if (seg < count - 1 && Wide(seg) == Wide(from)) continue;
                bool wide = Wide(from);
                var rows = new List<Vector3[]>();
                var skirtL = new List<Vector3[]>();
                var skirtR = new List<Vector3[]>();
                for (int i = from; i <= seg; i++)
                {
                    // Widened, the track side is the road's own edge on the track's own row, so it lines up
                    // with the runoff's end row instead of reaching past it along the lane's slant.
                    int k = LaneTrackIndex(i);
                    Vector3 l = Squared(i, LanePoint(i, laneHalf, 0));
                    Vector3 r = wide ? c.Surface(k, c.HalfWidth(k)) + Vector3.up * 0.01f : Squared(i, LanePoint(i, -laneHalf - LaneWiden, 0));
                    rows.Add(new[] { l, r });
                    skirtL.Add(new[] { l, l + Vector3.down * Skirt });
                    skirtR.Add(new[] { r, r + Vector3.down * Skirt });
                }
                surface.Strip(rows, 4f, 4f);
                surface.Strip(skirtL, 4f, 4f, true);
                surface.Strip(skirtR, 4f, 4f, true);
                from = seg;
            }
            Emit("Pit lane", surface, m?.PitAsphalt, true);

            // Grass along the lane's garage side, easing from the lane's edge down onto the ground, so a car
            // leaving the lane never drops off its edge. In front of a garage it runs on, flat and just under
            // the garage floor, so the floor and the grass meet with no notch of ground between them.
            var shoulder = new MeshKit();
            foreach (List<int> run in LaneRuns(i => true))
            {
                var band = new List<Vector3[]>();
                for (int r = 0; r < run.Count; r++)
                {
                    int i = run[r];
                    bool garage = InFrontOfGarage(i);
                    // Towards either end of the lane the shoulder settles onto the ground sooner, so its end
                    // meets the ground too.
                    float ease = ShoulderWidth * Mathf.Clamp(Mathf.Min(r, run.Count - 1 - r) / 3f, 0.1f, 1f);
                    var row = new Vector3[6];
                    for (int j = 0; j < row.Length; j++)
                    {
                        float out_ = ShoulderWidth * j / (row.Length - 1);
                        Vector3 q = Squared(i, LanePoint(i, laneHalf + out_, 0));
                        float t = Mathf.Clamp01(out_ / ease);
                        q.y = garage ? q.y - GarageTuck : Settle(new Vector3(q.x, q.y - RunoffFall * out_, q.z), t);
                        // At the lane's ends the shoulder takes over from the track's own runoff, so there
                        // it lies exactly as that runoff does and the two meet flush.
                        float own = 1f - Mathf.Clamp01(Mathf.Min(r, run.Count - 1 - r) / 3f);
                        if (own > 0f && !garage)
                        {
                            int k = TrackSampleBeside(q, LaneTrackIndex(i));
                            float lateral = Vector3.Dot(q - c.Centre[k], c.Left[k]);
                            if (Mathf.Abs(lateral) > c.HalfWidth(k)) q.y = Mathf.Lerp(q.y, Point(k, lateral, 0f, true).y, own);
                        }
                        row[row.Length - 1 - j] = q;   // left to right along the lane
                    }
                    band.Add(row);
                }
                shoulder.Strip(band, 6f, 6f);
            }
            Emit("Pit lane shoulders", shoulder, m?.Grass, true);

            var paint = new MeshKit();
            // Edge lines: garage side all along; track side wherever the lane is clear of the track.
            paint.Strip(LaneRows(0, count - 1, laneHalf, laneHalf - EdgeLine), 1f, 1f);
            foreach (List<int> run in LaneRuns(LaneClear))
                paint.Strip(LaneRows(run[0], run[run.Count - 1], -laneHalf + EdgeLine, -laneHalf), 1f, 1f);

            // Fast lane (max 3.5 m) next to the pit wall, working lane in front of the garages.
            float divider = -laneHalf + pit.fastLane;
            foreach (List<int> run in LaneRuns(InLimitZone))
                paint.Strip(LaneRows(run[0], run[run.Count - 1], divider + EdgeLine * 0.5f, divider - EdgeLine * 0.5f), 1f, 1f);

            // Speed limit lines across the lane where the limit starts and ends.
            AcrossLane(paint, NearestLane(pit.limitStart));
            AcrossLane(paint, NearestLane(pit.limitEnd));
            Emit("Pit lane lines", paint, m?.White);
        }

        private int LaneTrackIndex(int i) => c.IndexOf(c.Data.pit.laneTrackDistance[i]);

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private int NearestLane(float trackDistance)
        {
            int best = 0;
            float bestGap = float.MaxValue;
            float[] d = c.Data.pit.laneTrackDistance;
            for (int i = 0; i < d.Length; i++)
            {
                float g = Mathf.Abs(Mathf.DeltaAngle(d[i] / c.Lap * 360f, trackDistance / c.Lap * 360f));
                if (g < bestGap) { bestGap = g; best = i; }
            }
            return best;
        }

        private void AcrossLane(MeshKit kit, int i)
        {
            Vector3 fwd = Vector3.Cross(LaneLeft(i), Vector3.up) * -1f;
            Vector3 l = LanePoint(i, laneHalf, Paint), r = LanePoint(i, -laneHalf, Paint);
            Vector3 h = fwd * (LineAcross * 0.5f);
            kit.Strip(new[] { new[] { l - h, r - h }, new[] { l + h, r + h } }, 1f, 1f);
        }

        private List<Vector3[]> LaneRows(int from, int to, float leftLat, float rightLat)
        {
            var rows = new List<Vector3[]>();
            for (int i = from; i <= to; i++) rows.Add(new[] { LanePoint(i, leftLat, Paint), LanePoint(i, rightLat, Paint) });
            return rows;
        }

        private IEnumerable<List<int>> LaneRuns(Func<int, bool> keep)
        {
            var run = new List<int>();
            for (int i = 0; i < c.Lane.Length; i++)
            {
                if (keep(i)) run.Add(i);
                else
                {
                    if (run.Count > 1) yield return run;
                    run = new List<int>();
                }
            }
            if (run.Count > 1) yield return run;
        }

        private void PitEntryAndExit()
        {
            PitData pit = c.Data.pit;
            var hatch = new MeshKit();
            var white = new MeshKit();
            var bollard = new MeshKit();

            bool Joining(int k) => joining[k] && laneGap[k] > 0f;
            bool NearEntry(int k) => c.Ahead(pit.entry, k * c.Step) < 400f;
            bool NearExit(int k) => c.Ahead(k * c.Step, pit.exit) < 400f;

            // Entry: red and white painted area between the track and the pit road, bollard at its nose.
            foreach (List<int> run in Runs(k => Joining(k) && NearEntry(k)))
            {
                hatch.Strip(Rows(run, k => new[] { c.HalfWidth(k) + laneGap[k], c.HalfWidth(k) }, Paint), 1.5f, 1.5f);
                foreach (int k in run)
                {
                    if (laneGap[k] < 1.2f) continue;
                    Vector3 p = c.Surface(k, c.HalfWidth(k) + laneGap[k] * 0.5f);
                    bollard.Box(p + Vector3.up * 0.5f, -c.Left[k], c.Forward[k], new Vector3(0.35f, 1f, 0.35f));
                    break;
                }
            }

            // Exit: a solid white line along the exit road that cars leaving the pits must not cross.
            foreach (List<int> run in Runs(k => Joining(k) && NearExit(k)))
                white.Strip(Rows(run, k => new[] { c.HalfWidth(k) + laneGap[k], c.HalfWidth(k) + laneGap[k] - EdgeLine * 2f }, Paint * 1.5f), 1f, 1f);

            Emit("Pit entry hatching", hatch, m?.Hatch);
            Emit("Pit exit line", white, m?.White);
            Emit("Pit entry bollard", bollard, m?.Bollard, true);
        }

        private void Garages()
        {
            PitData pit = c.Data.pit;
            GarageFloors = new float[pit.garages.Length];
            var walls = new MeshKit();
            var roof = new MeshKit();
            var paint = new MeshKit();
            float bay = pit.garageWidth, depth = pit.garageDepth, height = 7f;
            // Where the row curves, neighbouring floors share the corner halfway between theirs, so no wedge of
            // ground shows between them.
            Vector3 Corner(int g, float x, float z)
            {
                GarageData gd = pit.garages[g];
                float r = gd.heading * Mathf.Deg2Rad;
                var al = new Vector3(Mathf.Sin(r), 0, Mathf.Cos(r));
                return new Vector3(gd.x, 0, gd.z) - al * x + new Vector3(al.z, 0, -al.x) * z;
            }
            Vector3 Shared(int g, float x, float z)
            {
                int other = x < 0 ? g + 1 : g - 1;
                Vector3 own = Corner(g, x, z);
                return other < 0 || other >= pit.garages.Length ? own : (own + Corner(other, -x, z)) * 0.5f;
            }
            for (int g = 0; g < pit.garages.Length; g++)
            {
                GarageData gd = pit.garages[g];
                float rad = gd.heading * Mathf.Deg2Rad;
                var along = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                var toLane = new Vector3(along.z, 0, -along.x); // right of the row, towards the pit lane
                var floor = new Vector3(gd.x, gd.y, gd.z);
                int li = NearestLanePoint(floor);
                floor.y = LanePoint(li, laneHalf, 0).y;
                GarageFloors[g] = floor.y;
                Vector3 side = -along; // local x, so that x, up and toLane form a left-handed frame
                Vector3 L(float x, float y, float z) => floor + side * x + Vector3.up * y + toLane * z;

                // Floor slab up to the lane edge, its top following the lane's fall along the row so the
                // two meet flush.
                Vector3 frontA = L(-bay * 0.5f, 0, depth * 0.5f + 1f), frontB = L(bay * 0.5f, 0, depth * 0.5f + 1f);
                float yA = LanePoint(NearestLanePoint(frontA), laneHalf, 0).y - floor.y;
                float yB = LanePoint(NearestLanePoint(frontB), laneHalf, 0).y - floor.y;
                // It tucks half a metre under the lane, 2.5 cm lower, so no sliver of ground shows where the lane curves.
                Vector3 S(float x, float z, bool top)
                {
                    Vector3 q = Shared(g, x, z);
                    q.y = floor.y + (top ? Mathf.Lerp(yA, yB, x / bay + 0.5f) - 0.025f : -2f);
                    return q;
                }
                Slab(walls, S, bay, -depth * 0.5f, depth * 0.5f + 1.5f, L(0, -1f, 0.5f));
                walls.Box(L(0, (height - 2f) * 0.5f, -depth * 0.5f + 0.15f), side, toLane, new Vector3(bay, height + 2f, 0.3f));
                walls.Box(L(-bay * 0.5f, (height - 2f) * 0.5f, 0), side, toLane, new Vector3(0.3f, height + 2f, depth));
                if (g == pit.garages.Length - 1 || g == 0) walls.Box(L(bay * 0.5f, (height - 2f) * 0.5f, 0), side, toLane, new Vector3(0.3f, height + 2f, depth));   // walls reach down past the slab so none stands off a slope
                roof.Box(L(0, height + 0.15f, 0.5f), side, toLane, new Vector3(bay, 0.3f, depth + 1f));
                roof.Box(L(0, height - 1f, depth * 0.5f - 0.15f), side, toLane, new Vector3(bay, 2f, 0.3f)); // fascia over the door

                // Pit box outline on the working lane in front of the garage.
                Vector3 laneLeft = LaneLeft(li);
                float garageLat = Vector3.Dot(floor - c.Lane[li], laneLeft);
                float front = -laneHalf + pit.fastLane + 0.3f, back = laneHalf - 0.3f;
                float half = bay * 0.5f - 0.6f;
                Vector3 P(float lat, float al)
                {
                    Vector3 p = floor + toLane * (garageLat - lat) + along * al;
                    p.y = LanePoint(li, lat, Paint).y;
                    return p;
                }
                OutlineBox(paint, P(back, -half), P(back, half), P(front, -half), P(front, half), along, toLane);
            }
            Emit("Garages", walls, m?.GarageInside, true);
            Emit("Garage roofs", roof, m?.Concrete);
            Emit("Pit box lines", paint, m?.White);
        }

        /// <summary>Paddock surface falls away from the garages' back wall at this grade, past the first few metres.</summary>
        private const float PaddockFall = 0.01f;

        /// <summary>
        /// The paddock: asphalt behind the garages' back wall, <see cref="PitComplex.PaddockDepth"/> deep along the
        /// whole row, level with the garage floors by the wall and falling gently away, with a parking bay painted
        /// behind each garage.
        /// </summary>
        private void Paddock()
        {
            var pc = new PitComplex(c, GarageFloors);
            float back = -pc.Depth * 0.5f;
            const int across = 11;
            const int perBay = 4;
            int last = (pc.Stations - 1) * perBay;
            var rows = new List<Vector3[]>();
            // Past its far edge the paddock eases down onto the ground over this far, m.
            const float ease = 4f;
            // Its two ends step down in a metre, beside the garages' end walls.
            const float endEase = 1f;
            for (int h = -1; h <= last + 1; h++)
            {
                float st = Mathf.Clamp(h, 0, last) / (float)perBay;
                float beyondEnd = h < 0 || h > last ? endEase : 0f;
                Vector3 endward = h < 0 ? -pc.RowAlong(0f) : pc.RowAlong(pc.Stations - 1);
                var row = new Vector3[across + 2];
                for (int a = -2; a < across; a++)
                {
                    // Left to right driving along the row: from the far edge in to the back wall.
                    float z = back - PitComplex.PaddockDepth * (1f - Mathf.Max(a, 0) / (float)(across - 1));
                    float beyondEdge = a < 0 ? -a * ease * 0.5f : 0f;
                    Vector3 p = pc.RowPoint(st, PaddockHeight(back - z), z) - pc.RowOut(st) * beyondEdge + endward * beyondEnd;
                    float t = Mathf.Max(beyondEnd / endEase, beyondEdge / ease);
                    if (t > 0f) p.y = Mathf.Lerp(p.y, c.TerrainHeight(p.x, p.z) + 0.03f, t);
                    row[a + 2] = p;
                }
                rows.Add(row);
            }
            var surface = new MeshKit();
            surface.Strip(rows, 4f, 4f);
            Emit("Paddock", surface, m?.PitAsphalt, true);

            // A bay behind each garage: a line at each station from the wall out, and one along their ends.
            var paint = new MeshKit();
            const float bayFrom = 2f, bayTo = 14f;
            for (int j = 0; j < pc.Stations; j++)
            {
                Vector3 a = pc.RowPoint(j, PaddockHeight(bayFrom) + Paint, back - bayFrom);
                Vector3 b = pc.RowPoint(j, PaddockHeight(bayTo) + Paint, back - bayTo);
                Line(paint, a, b, pc.RowAlong(j), EdgeLine * 0.5f);
            }
            for (int j = 0; j < pc.Stations - 1; j++)
                for (int h = 0; h < 2; h++)
                {
                    float s0 = j + h * 0.5f, s1 = s0 + 0.5f;
                    Vector3 a = pc.RowPoint(s0, PaddockHeight(bayTo) + Paint, back - bayTo);
                    Vector3 b = pc.RowPoint(s1, PaddockHeight(bayTo) + Paint, back - bayTo);
                    Line(paint, a, b, pc.RowOut(s0 + 0.25f), EdgeLine * 0.5f);
                }
            Emit("Paddock lines", paint, m?.White);
        }

        /// <summary>The paddock's height over the garage floor, this far out from the back wall.</summary>
        private static float PaddockHeight(float fromWall) => -0.05f - PaddockFall * Mathf.Max(0f, fromWall - 4f);

        /// <summary>A six-sided slab, x across from -width/2 to width/2 and z from back to front, with corners from <paramref name="corner"/>.</summary>
        private static void Slab(MeshKit kit, Func<float, float, bool, Vector3> corner, float width, float back, float front, Vector3 inside)
        {
            float h = width * 0.5f;
            Vector3 P(float x, float z, bool top) => corner(x, z, top);
            kit.Face(P(-h, back, true), P(h, back, true), P(-h, front, true), P(h, front, true), inside);
            kit.Face(P(-h, back, false), P(h, back, false), P(-h, front, false), P(h, front, false), inside);
            kit.Face(P(-h, front, false), P(h, front, false), P(-h, front, true), P(h, front, true), inside);
            kit.Face(P(-h, back, false), P(h, back, false), P(-h, back, true), P(h, back, true), inside);
            kit.Face(P(-h, back, false), P(-h, front, false), P(-h, back, true), P(-h, front, true), inside);
            kit.Face(P(h, back, false), P(h, front, false), P(h, back, true), P(h, front, true), inside);
        }

        private int NearestLanePoint(Vector3 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < c.Lane.Length; i++)
            {
                float d = (c.Lane[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The four sides of a painted rectangle given its corners.</summary>
        private void OutlineBox(MeshKit kit, Vector3 backA, Vector3 backB, Vector3 frontA, Vector3 frontB, Vector3 along, Vector3 across)
        {
            float w = EdgeLine * 0.5f;
            Line(kit, backA, backB, across, w);
            Line(kit, frontA, frontB, across, w);
            Line(kit, backA, frontA, along, w);
            Line(kit, backB, frontB, along, w);
        }

        /// <summary>A painted line from a to b, widened along <paramref name="widen"/>.</summary>
        private static void Line(MeshKit kit, Vector3 a, Vector3 b, Vector3 widen, float halfWidth)
        {
            Vector3 o = widen.normalized * halfWidth;
            kit.Face(a + o, a - o, b + o, b - o, a + Vector3.down);
        }

        // ---------------------------------------------------------------- start line, grid, safety car lines

        private void LinesAcross()
        {
            var white = new MeshKit();
            AcrossTrack(white, 0f, LineAcross);
            foreach (float d in c.Data.safetyCarLines) AcrossTrack(white, d, LineAcross * 0.7f);
            Emit("Start line and safety car lines", white, m?.White);
        }

        private void AcrossTrack(MeshKit kit, float d, float thickness)
        {
            Vector3 a0 = At(d - thickness * 0.5f, 1), a1 = At(d - thickness * 0.5f, -1);
            Vector3 b0 = At(d + thickness * 0.5f, 1), b1 = At(d + thickness * 0.5f, -1);
            kit.Face(a0, a1, b0, b1, (a0 + b1) * 0.5f + Vector3.down);
        }

        /// <summary>The road edge (side +1 left, -1 right) at any distance, interpolated, with paint lift.</summary>
        private Vector3 At(float d, float side) => SurfaceAt(d, side * 1000f);

        private Vector3 SurfaceAt(float d, float lateral)
        {
            d = Mathf.Repeat(d, c.Lap);
            float f = d / c.Step;
            int k0 = c.Wrap(Mathf.FloorToInt(f)), k1 = c.Wrap(k0 + 1);
            float t = f - Mathf.Floor(f);
            float h0 = c.HalfWidth(k0), h1 = c.HalfWidth(k1);
            Vector3 p0 = c.Surface(k0, Mathf.Clamp(lateral, -h0, h0));
            Vector3 p1 = c.Surface(k1, Mathf.Clamp(lateral, -h1, h1));
            return Vector3.Lerp(p0, p1, t) + Vector3.up * Paint;
        }

        private void Grid()
        {
            GridData grid = c.Data.grid;
            var white = new MeshKit();
            var yellow = new MeshKit();
            float w = 0.06f; // half width of the slot lines
            foreach (GridSlot slot in grid.slots)
            {
                float sign = Mathf.Sign(slot.lateral);
                float inner = slot.lateral - sign * grid.slotWidth * 0.5f;
                float outer = slot.lateral + sign * grid.slotWidth * 0.5f;
                // White outline open at the back: front line and two side lines.
                PaintAcross(white, slot.d, inner, outer, w);
                PaintAlong(white, slot.d - grid.slotLength, slot.d, inner, w);
                PaintAlong(white, slot.d - grid.slotLength, slot.d, outer, w);
                // Yellow front-wheel line, crossing the slot and sticking out past its outer side.
                PaintAcross(yellow, slot.d - 0.6f, inner, inner + sign * grid.yellowLength, w, Paint * 1.5f);
            }
            Emit("Grid slots", white, m?.White);
            Emit("Grid yellow lines", yellow, m?.Yellow);
        }

        private void PaintAcross(MeshKit kit, float d, float latA, float latB, float halfWidth, float extraLift = 0f)
        {
            Vector3 up = Vector3.up * extraLift;
            Vector3 a0 = SurfaceAt(d - halfWidth, latA) + up, a1 = SurfaceAt(d - halfWidth, latB) + up;
            Vector3 b0 = SurfaceAt(d + halfWidth, latA) + up, b1 = SurfaceAt(d + halfWidth, latB) + up;
            kit.Face(a0, a1, b0, b1, (a0 + b1) * 0.5f + Vector3.down);
        }

        private void PaintAlong(MeshKit kit, float from, float to, float lat, float halfWidth)
        {
            Vector3 a0 = SurfaceAt(from, lat + halfWidth), a1 = SurfaceAt(from, lat - halfWidth);
            Vector3 b0 = SurfaceAt(to, lat + halfWidth), b1 = SurfaceAt(to, lat - halfWidth);
            kit.Face(a0, a1, b0, b1, (a0 + b1) * 0.5f + Vector3.down);
        }

        // ---------------------------------------------------------------- ground

        /// <summary>Ground cells per side of a ground tile.</summary>
        private const int GroundTile = 16;

        /// <summary>
        /// The ground in square tiles. Tiles near anything laid on the ground keep every point. Farther out
        /// they keep every second, then every fourth. Points on a tile's edge sit on the straight line between
        /// the coarser neighbour's points, so tiles meet without cracks, and normals come from the full grid
        /// so the shading has no seams.
        /// </summary>
        private void Ground()
        {
            GroundData t = c.Data.terrain;
            int lastX = t.countX - 1, lastZ = t.countZ - 1;
            int tilesX = (lastX + GroundTile - 1) / GroundTile, tilesZ = (lastZ + GroundTile - 1) / GroundTile;

            // Tiles away from the nearest tile with something laid on it.
            var away = new int[tilesX, tilesZ];
            for (int i = 0; i < tilesX; i++) for (int j = 0; j < tilesZ; j++) away[i, j] = int.MaxValue;
            var queue = new Queue<(int, int)>();
            foreach (CircuitPart part in parts)
                foreach (Vector3 v in part.Kit.Vertices)
                {
                    int i = Mathf.Clamp(Mathf.FloorToInt((v.x - t.originX) / t.spacing / GroundTile), 0, tilesX - 1);
                    int j = Mathf.Clamp(Mathf.FloorToInt((v.z - t.originZ) / t.spacing / GroundTile), 0, tilesZ - 1);
                    if (away[i, j] == 0) continue;
                    away[i, j] = 0;
                    queue.Enqueue((i, j));
                }
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= tilesX || b >= tilesZ || away[a, b] <= away[i, j] + 1) continue;
                        away[a, b] = away[i, j] + 1;
                        queue.Enqueue((a, b));
                    }
            }
            int Stride(int i, int j) =>
                i < 0 || j < 0 || i >= tilesX || j >= tilesZ ? 1 : away[i, j] <= 1 ? 1 : away[i, j] <= 3 ? 2 : 4;

            float H(int x, int z) => c.TerrainHeightAt(x, z);
            // Height along a grid line, on the straight line between points a stride apart.
            float Along(int at, int stride, int last, Func<int, float> h)
            {
                int a = at / stride * stride, b = Mathf.Min(a + stride, last);
                return a == at || b == a ? h(at) : Mathf.Lerp(h(a), h(b), (at - a) / (float)(b - a));
            }
            float Height(int x, int z)
            {
                bool onX = x % GroundTile == 0, onZ = z % GroundTile == 0;
                if (onX && onZ) return H(x, z);
                int ti = x / GroundTile, tj = z / GroundTile;
                if (onX)
                {
                    int s = Mathf.Max(Stride(ti - 1, tj), Stride(ti, tj));
                    return Along(z, s, lastZ, zz => H(x, zz));
                }
                if (onZ)
                {
                    int s = Mathf.Max(Stride(ti, tj - 1), Stride(ti, tj));
                    return Along(x, s, lastX, xx => H(xx, z));
                }
                return H(x, z);
            }
            Vector3 NormalAt(Vector3 p)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((p.x - t.originX) / t.spacing), 0, lastX);
                int z = Mathf.Clamp(Mathf.RoundToInt((p.z - t.originZ) / t.spacing), 0, lastZ);
                int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, lastX), z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, lastZ);
                float dx = (H(x1, z) - H(x0, z)) / ((x1 - x0) * t.spacing);
                float dz = (H(x, z1) - H(x, z0)) / ((z1 - z0) * t.spacing);
                return new Vector3(-dx, 1f, -dz).normalized;
            }
            Vector2 UvAt(Vector3 p) => new Vector2(p.x / 8f, p.z / 8f);

            var kit = new MeshKit();
            for (int i = 0; i < tilesX; i++)
                for (int j = 0; j < tilesZ; j++)
                {
                    int s = Stride(i, j);
                    var xs = Steps(i * GroundTile, Mathf.Min((i + 1) * GroundTile, lastX), s);
                    var zs = Steps(j * GroundTile, Mathf.Min((j + 1) * GroundTile, lastZ), s);
                    var rows = new List<Vector3[]>(zs.Count);
                    foreach (int z in zs)
                        rows.Add(xs.ConvertAll(x => new Vector3(t.originX + x * t.spacing, Height(x, z), t.originZ + z * t.spacing)).ToArray());
                    kit.Strip(rows, 8f, 8f, false, NormalAt, UvAt);
                }
            Emit("Ground", kit, m?.Terrain, true);
        }

        /// <summary>Grid indices from one to another a stride apart, always ending on the last.</summary>
        private static List<int> Steps(int from, int to, int stride)
        {
            var list = new List<int>();
            for (int v = from; v < to; v += stride) list.Add(v);
            list.Add(to);
            return list;
        }

        private static readonly HashSet<string> Standing = new HashSet<string> { "Armco", "Tyre walls", "Pit wall", "Pit entry bollard" };

        /// <summary>
        /// Lowers the ground wherever it would poke through something laid on it. Draped runoff follows the
        /// ground only as finely as its own grid, so on a sharp fold the ground can rise between its points.
        /// </summary>
        private float SinkUnderCover()
        {
            float most = 0f;
            GroundData t = c.Data.terrain;
            const float clearance = 0.03f;
            var samples = new List<Vector3>();
            foreach (CircuitPart part in parts)
            {
                if (!part.Solid || Standing.Contains(part.Name)) continue;
                IReadOnlyList<Vector3> v = part.Kit.Vertices;
                IReadOnlyList<int> tri = part.Kit.Triangles;
                for (int i = 0; i < tri.Count; i += 3)
                {
                    Vector3 a = v[tri[i]], b = v[tri[i + 1]], d = v[tri[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, d - a);
                    if (normal.sqrMagnitude < 1e-10f || normal.y < 0.5f * normal.magnitude) continue;
                    samples.Add(a); samples.Add(b); samples.Add(d);
                    samples.Add((a + b) * 0.5f); samples.Add((b + d) * 0.5f); samples.Add((d + a) * 0.5f);
                    samples.Add((a + b + d) / 3f);
                }
            }
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                foreach (Vector3 p in samples)
                {
                    float fx = (p.x - t.originX) / t.spacing, fz = (p.z - t.originZ) / t.spacing;
                    if (fx < 0 || fz < 0 || fx >= t.countX - 1 || fz >= t.countZ - 1) continue;
                    int ix = (int)fx, iz = (int)fz;
                    float over = c.TerrainHeight(p.x, p.z) - (p.y - clearance);
                    if (over <= 0f) continue;
                    // The cell's two triangles share the (x+1, z) to (x, z+1) diagonal. Each corner goes
                    // down in proportion to its weight at p, so the corner nearest p moves most and the
                    // ground past the cover's edge moves least.
                    float u = fx - ix, v = fz - iz;
                    bool first = u + v <= 1f;
                    float wb = first ? u : 1f - v, wd = first ? v : 1f - u, wc = first ? 1f - u - v : u + v - 1f;
                    float k = over / (wb * wb + wd * wd + wc * wc);
                    most = Mathf.Max(most, k * Mathf.Max(wb, Mathf.Max(wd, wc)));
                    c.LowerTerrainAt(ix + 1, iz, k * wb);
                    c.LowerTerrainAt(ix, iz + 1, k * wd);
                    c.LowerTerrainAt(first ? ix : ix + 1, first ? iz : iz + 1, k * wc);
                    moved = true;
                }
                if (!moved) break;
            }
            return most;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Lateral offsets for a band between two distances from the centre, ordered left to right.</summary>
        private static float[] Across(float sign, float a, float b) =>
            sign > 0 ? new[] { Mathf.Max(a, b), Mathf.Min(a, b) } : new[] { -Mathf.Min(a, b), -Mathf.Max(a, b) };

        /// <summary>
        /// Runoff between two distances from the centre along a run. Where it eases off the road it has
        /// columns a metre apart on every row. Once settled on the ground, where <see cref="Conform"/> cuts it
        /// along the ground's folds anyway, its columns are a ground cell apart.
        /// </summary>
        private void Band(MeshKit kit, List<int> run, float sign, Func<int, float> inner, Func<int, float> outer, float lift, float tile)
        {
            int side = sign > 0 ? 0 : 1;
            float Lo(int k) => Mathf.Min(inner(k), outer(k));
            float Hi(int k) => Mathf.Max(inner(k), outer(k));
            // Where the runoff has settled onto the ground, as a distance from the centre.
            float Settled(int k) => level[side][k] > 0f ? Hi(k) : Mathf.Clamp(c.HalfWidth(k) + Verge + EaseLength(side, k), Lo(k), Hi(k));
            // A point at a distance from the centre, on the band's side.
            Vector3 At(int k, float d) => Point(k, sign * d, lift, true);
            // Rows run left to right: outermost first on the left side, innermost first on the right.
            Vector3[] LeftToRight(List<Vector3> innerFirst)
            {
                if (sign > 0) innerFirst.Reverse();
                return innerFirst.ToArray();
            }

            // Easing part: every row, a metre between columns.
            Vector3[] EaseRow(int k)
            {
                float lo = Lo(k), hi = Settled(k);
                int n = Mathf.Max(1, Mathf.CeilToInt(hi - lo - 1e-3f));
                var pts = new List<Vector3>();
                for (int j = 0; j <= n; j++) pts.Add(At(k, lo + (hi - lo) * j / n));
                return LeftToRight(pts);
            }
            int from = 0;
            for (int r = 1; r <= run.Count; r++)
            {
                if (r < run.Count && EaseRow(run[r]).Length == EaseRow(run[from]).Length) continue;
                if (r - from > 1) kit.Strip(run.GetRange(from, r - from).ConvertAll(EaseRow), tile, tile);
                if (r < run.Count) kit.Zip(EaseRow(run[r - 1]), EaseRow(run[r]), tile);
                from = r;
            }

            // Settled part, from the settled line out to the band's outer edge.
            float widest = 0f;
            foreach (int k in run) widest = Mathf.Max(widest, Hi(k) - Settled(k));
            if (widest < 0.05f) return;
            int cols = Mathf.Max(1, Mathf.CeilToInt(widest / c.Data.terrain.spacing));
            Vector3 Across(int k, float u) => At(k, Mathf.Lerp(Settled(k), Hi(k), u));
            kit.Strip(run.ConvertAll(k =>
            {
                var pts = new List<Vector3>();
                for (int j = 0; j <= cols; j++) pts.Add(Across(k, j / (float)cols));
                return LeftToRight(pts);
            }), tile, tile);
        }

        /// <summary>Rows of surface points at the given lateral offsets (left positive, ordered left to right).</summary>
        private List<Vector3[]> Rows(List<int> ks, Func<int, float[]> lats, float lift, bool runoff = false)
        {
            var rows = new List<Vector3[]>(ks.Count);
            foreach (int k in ks)
            {
                float[] l = lats(k);
                var row = new Vector3[l.Length];
                for (int j = 0; j < l.Length; j++) row[j] = Point(k, l[j], lift, runoff);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>A vertical band at a distance from the centre, from <paramref name="bottom"/> to <paramref name="top"/> above the surface.</summary>
        private List<Vector3[]> Vertical(List<int> ks, Func<int, float> distance, float sign, float bottom, float top)
        {
            var rows = new List<Vector3[]>(ks.Count);
            foreach (int k in ks)
            {
                Vector3 p = Point(k, sign * distance(k), 0, true);
                rows.Add(new[] { p + Vector3.up * top, p + Vector3.up * bottom });
            }
            return rows;
        }

        /// <summary>
        /// The ground beside the road: the verge carries on from the road edge, falling gently
        /// outwards; past the verge, runoff and barriers lie on the ground itself.
        /// </summary>
        private Vector3 Point(int k, float lateral, float lift, bool runoff)
        {
            Vector3 p = c.Surface(k, lateral);
            if (runoff)
            {
                float beyond = Mathf.Abs(lateral) - c.HalfWidth(k);
                if (beyond > 0) p.y -= beyond * RunoffFall;
                // Past the verge, ease from the road's own plane down onto the ground, so the runoff
                // meets the verge flush instead of dropping off it.
                if (beyond > Verge + 0.01f)
                {
                    // A narrow unfenced runoff eases over its own width, so its outer edge still lands on the
                    // ground. Against armco it needn't, and stays near the road's plane instead of dropping
                    // steeply over a strip too narrow to ease across.
                    float ease = EaseLength(lateral > 0 ? 0 : 1, k);
                    float t = Mathf.Clamp01((beyond - Verge) / ease + 1e-4f);   // the outer edge itself counts as settled
                    p.y = Settle(p, t);
                }
                float flat = level[lateral > 0 ? 0 : 1]?[k] ?? 0f;
                if (flat > 0f) p.y = Mathf.Lerp(p.y, c.Surface(k, lateral).y + 0.01f, flat);
            }
            return p + Vector3.up * lift;
        }

        /// <summary>How far past the verge the runoff on one side at sample k takes to settle onto the ground.</summary>
        private float EaseLength(int side, int k)
        {
            float reach = barrier[side]?[k] ?? 0f;
            // Only armco where the runoff reached its full width keeps it up; armco at a cut-short edge stands on the ground.
            bool armco = (fenced[side]?[k] ?? false) && !(pitWall[side]?[k] ?? false);
            if (armco) reach = float.MaxValue;
            return Mathf.Clamp(Mathf.Min(reach - Verge, endEase[side]?[k] ?? RunoffEase), 0.25f, RunoffEase);
        }

        /// <summary>Height to lay runoff at: just over the ground. <see cref="Conform"/> then makes it follow
        /// the ground's folds between its points.</summary>
        private float Drape(float x, float z) => c.TerrainHeight(x, z) + 0.03f;

        /// <summary>
        /// Eases from a laid height at <paramref name="t"/> = 0 down onto the ground at 1, smoothly. Once
        /// settled it lies on the ground as it now is. On the way it aims at the ground as generated, which
        /// never moves: aiming at ground that sinks under it would sink it in turn, and it would never settle.
        /// </summary>
        private float Settle(Vector3 p, float t)
        {
            if (t >= 1f) return Drape(p.x, p.z);
            return Mathf.Lerp(p.y, c.UntouchedHeight(p.x, p.z) + 0.03f, t * t * (3f - 2f * t));
        }

        /// <summary>Sample indices from one lap distance to another, going forwards.</summary>
        private IEnumerable<int> Span(float from, float to)
        {
            int a = c.IndexOf(from);
            int steps = Mathf.RoundToInt(c.Ahead(from, to) / c.Step);
            for (int i = 0; i <= steps; i++) yield return c.Wrap(a + i);
        }

        /// <summary>Contiguous runs of samples (wrapping round the lap) where <paramref name="keep"/> holds.</summary>
        private IEnumerable<List<int>> Runs(Func<int, bool> keep)
        {
            int start = -1;
            for (int k = 0; k < n; k++)
                if (!keep(k)) { start = k; break; }
            if (start < 0)
            {
                var all = new List<int>(n + 1);
                for (int k = 0; k <= n; k++) all.Add(c.Wrap(k));
                yield return all;
                yield break;
            }
            var run = new List<int>();
            for (int i = 1; i <= n; i++)
            {
                int k = c.Wrap(start + i);
                if (keep(k)) run.Add(k);
                else
                {
                    if (run.Count > 1) yield return run;
                    run = new List<int>();
                }
            }
            if (run.Count > 1) yield return run;
        }

        private float[] MaxFilter(float[] v, int radius)
        {
            var o = new float[n];
            for (int k = 0; k < n; k++)
            {
                float best = float.MinValue;
                for (int j = -radius; j <= radius; j++) best = Mathf.Max(best, v[c.Wrap(k + j)]);
                o[k] = best;
            }
            return o;
        }

        private float[] MinFilter(float[] v, int radius)
        {
            var o = new float[n];
            for (int k = 0; k < n; k++)
            {
                float best = float.MaxValue;
                for (int j = -radius; j <= radius; j++) best = Mathf.Min(best, v[c.Wrap(k + j)]);
                o[k] = best;
            }
            return o;
        }

        private float[] Smooth(float[] v, int radius)
        {
            var o = new float[n];
            for (int k = 0; k < n; k++)
            {
                float sum = 0;
                for (int j = -radius; j <= radius; j++) sum += v[c.Wrap(k + j)];
                o[k] = sum / (2 * radius + 1);
            }
            return o;
        }

        /// <summary>Cells the circuit's meshes are cut into, so the camera skips whatever is off screen.</summary>
        private const float ChunkSize = 200f;

        private void Emit(string name, MeshKit kit, Material material, bool solid = false)
        {
            if (Draped.Contains(name)) kit = Conform(kit).Welded();
            // Only things standing up cast a shadow worth drawing; flat cover just receives them.
            bool shadows = Standing.Contains(name) || name.StartsWith("Garage");
            foreach (MeshKit chunk in kit.Chunks(ChunkSize))
                if (!chunk.IsEmpty) parts.Add(new CircuitPart(name, chunk, material, solid, shadows));
        }

        /// <summary>A point on a draped triangle's edge follows the ground only when its ends follow it at least this much.</summary>
        private const float EdgeFollow = 0.7f;

        /// <summary>Surfaces laid over the ground rather than at a height of their own.</summary>
        private static readonly HashSet<string> Draped = new HashSet<string> { "Grass runoff", "Gravel traps", "Pit lane shoulders" };

        /// <summary>
        /// Cuts every triangle along the ground's own cell lines, so each piece lies over one flat piece of
        /// ground. Where a triangle lies on the ground, its new corners keep the same height over the ground,
        /// so it follows every fold exactly instead of cutting through crests or bridging hollows. Where it
        /// stands well clear of the ground, as where runoff leaves the road over a dip, it stays straight.
        /// </summary>
        private MeshKit Conform(MeshKit kit)
        {
            GroundData t = c.Data.terrain;
            IReadOnlyList<Vector3> v = kit.Vertices;
            IReadOnlyList<Vector3> nrm = kit.Normals;
            IReadOnlyList<Vector2> uv = kit.UVs;
            IReadOnlyList<int> tri = kit.Triangles;
            var result = new MeshKit();
            var lines = new System.Func<Vector3, float>[]
            {
                p => (p.x - t.originX) / t.spacing,
                p => (p.z - t.originZ) / t.spacing,
                p => (p.x - t.originX) / t.spacing + (p.z - t.originZ) / t.spacing,
            };
            for (int i = 0; i < tri.Count; i += 3)
            {
                int[] id = { tri[i], tri[i + 1], tri[i + 2] };
                var over = new float[3];
                for (int j = 0; j < 3; j++) over[j] = v[id[j]].y - c.TerrainHeight(v[id[j]].x, v[id[j]].z);
                // Each corner of a piece is a blend of the triangle's three corners.
                var pieces = new List<List<Vector3>> { new List<Vector3> { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) } };
                Vector3 At(Vector3 w) => v[id[0]] * w.x + v[id[1]] * w.y + v[id[2]] * w.z;
                foreach (var line in lines)
                {
                    var next = new List<List<Vector3>>();
                    foreach (List<Vector3> piece in pieces) Cut(piece, w => line(At(w)), next);
                    pieces = next;
                }
                foreach (List<Vector3> piece in pieces)
                {
                    var pos = new Vector3[piece.Count];
                    for (int j = 0; j < piece.Count; j++)
                    {
                        Vector3 w = piece[j];
                        Vector3 p = At(w);
                        // Follow the ground as much as the corners this point blends from lie on it.
                        float follow = 0f, groundLine = 0f;
                        for (int q = 0; q < 3; q++)
                        {
                            float wq = q == 0 ? w.x : q == 1 ? w.y : w.z;
                            groundLine += wq * (v[id[q]].y - over[q]);
                            follow += wq * Mathf.Clamp01(1.2f - Mathf.Abs(over[q] - 0.03f) / 0.25f);
                        }
                        // A point on one of the triangle's own edges stays on that straight edge unless its ends lie
                        // nearly on the ground: there the surface beside it (the verge) shares the straight edge, and
                        // following the ground partly would leave a step at the seam.
                        bool onEdge = w.x < 1e-5f || w.y < 1e-5f || w.z < 1e-5f;
                        if (onEdge && follow < EdgeFollow) follow = 0f;
                        p.y += follow * (c.TerrainHeight(p.x, p.z) - groundLine);
                        pos[j] = p;
                    }
                    for (int j = 1; j + 1 < piece.Count; j++)
                    {
                        Vector3 wa = piece[0], wb = piece[j], wc = piece[j + 1];
                        if (Vector3.Cross(pos[j] - pos[0], pos[j + 1] - pos[0]).sqrMagnitude < 1e-10f) continue;
                        Vector3 N(Vector3 w) => (nrm[id[0]] * w.x + nrm[id[1]] * w.y + nrm[id[2]] * w.z).normalized;
                        Vector2 U(Vector3 w) => uv[id[0]] * w.x + uv[id[1]] * w.y + uv[id[2]] * w.z;
                        result.Triangle(pos[0], pos[j], pos[j + 1], N(wa), N(wb), N(wc), U(wa), U(wb), U(wc));
                    }
                }
            }
            return result;
        }

        /// <summary>Splits a convex piece at every whole value of <paramref name="f"/> that crosses it.</summary>
        private static void Cut(List<Vector3> piece, System.Func<Vector3, float> f, List<List<Vector3>> into)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 w in piece) { float x = f(w); lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            var rest = piece;
            for (float m = Mathf.Floor(lo) + 1f; m < hi - 1e-4f; m += 1f)
            {
                if (m <= lo + 1e-4f) continue;
                var below = new List<Vector3>();
                var above = new List<Vector3>();
                for (int j = 0; j < rest.Count; j++)
                {
                    Vector3 a = rest[j], b = rest[(j + 1) % rest.Count];
                    float fa = Snap(f(a) - m), fb = Snap(f(b) - m);
                    if (fa <= 0) below.Add(a);
                    if (fa >= 0) above.Add(a);
                    if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0))
                    {
                        Vector3 x = a + (b - a) * (fa / (fa - fb));
                        below.Add(x);
                        above.Add(x);
                    }
                }
                if (below.Count >= 3) into.Add(below);
                rest = above;
                if (rest.Count < 3) return;
            }
            into.Add(rest);
        }

        /// <summary>
        /// A corner within a hair of a cut line counts as on it, so no cut leaves a sliver. The hair is in cells: 1e-3 of
        /// an 8 m cell is 8 mm. Wider, a corner a centimetre off the line went into the pieces either side of it,
        /// and the two overlapped in a sliver that flickered.
        /// </summary>
        private static float Snap(float f) => Mathf.Abs(f) < 1e-3f ? 0f : f;
    }

    /// <summary>One unbroken run of armco on one side of the track.</summary>
    public sealed class ArmcoRun
    {
        /// <summary>0 = left of the track, 1 = right.</summary>
        public readonly int Side;
        /// <summary>The track sample beside each point.</summary>
        public readonly int[] Samples;
        /// <summary>Where the runoff meets the armco, on the runoff's surface: the face cars hit.</summary>
        public readonly Vector3[] Line;
        /// <summary>True where a tyre wall stands in front of it.</summary>
        public readonly bool[] Tyres;

        public ArmcoRun(int side, int[] samples, Vector3[] line, bool[] tyres)
        {
            Side = side;
            Samples = samples;
            Line = line;
            Tyres = tyres;
        }
    }
}
