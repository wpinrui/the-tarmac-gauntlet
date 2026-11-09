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
        private const float Skirt = 1.6f;          // hides the seams down to the ground
        private const float Paint = 0.012f;        // paint sits this far above the surface
        private const float PitWallGap = 6f;       // the pit wall starts once the lane is this far off the track
        private const float LineAcross = 0.3f;     // thickness of lines painted across a surface

        private readonly Circuit c;
        private readonly CircuitMaterials m;
        private readonly List<CircuitPart> parts = new List<CircuitPart>();
        private readonly int n;
        private readonly float laneHalf;

        // Per side (0 = left, 1 = right), per sample, measured outwards from the road edge.
        private readonly float[][] barrier = { null, null };
        private readonly float[][] gravel = { null, null };
        private readonly bool[][] pitWall = { null, null };
        private readonly bool[][] open = { null, null };   // no verge, runoff or barrier (pit road joins here)
        private readonly float[] laneGap;                  // gap between left road edge and pit lane edge, or NaN

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
            PlanSides();
        }

        public List<CircuitPart> Build()
        {
            Road();
            EdgeLines();
            Kerbs();
            Runoff();
            Barriers();
            PitLane();
            PitEntryAndExit();
            Garages();
            LinesAcross();
            Grid();
            Ground();
            return parts;
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
                var bar = new float[n];
                var wall = new bool[n];
                var shut = new bool[n];
                for (int k = 0; k < n; k++)
                {
                    float limit = room[k];
                    bar[k] = Mathf.Max(0f, Mathf.Min(want[k], limit));
                    if (s == 0 && !float.IsNaN(laneGap[k]))
                    {
                        if (laneGap[k] >= PitWallGap)
                        {
                            wall[k] = true;
                            bar[k] = laneGap[k] - 1f;
                        }
                        else shut[k] = true;
                    }
                    if (!wall[k] && bar[k] < Verge + 0.5f) shut[k] = true;
                    grav[k] = Mathf.Min(grav[k], bar[k] - 1.5f);
                }
                barrier[s] = bar;
                gravel[s] = grav;
                pitWall[s] = wall;
                open[s] = shut;
            }
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
            Emit("Road edges", skirts, m?.Asphalt);
        }

        private void EdgeLines()
        {
            var kit = new MeshKit();
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                // FIA: the edge line breaks only where the pit entry and exit roads join.
                bool Draw(int k) => s == 1 || float.IsNaN(laneGap[k]) || laneGap[k] >= PitWallGap;
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
            Emit("Kerbs", kit, m?.Kerb);
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
                Vector3 outer = c.Surface(k, sign * (e + KerbWidth)) + Vector3.up * 0.05f;
                rows.Add(sign > 0 ? new[] { outer, inner } : new[] { inner, outer });
            }
            kit.Strip(rows, KerbWidth, KerbStripe * 2f);
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
                    verge.Strip(Rows(run, k => Across(sign, c.HalfWidth(k), c.HalfWidth(k) + Verge), 0f, true), 2f, 2f);
                    grass.Strip(Rows(run, k => Across(sign, c.HalfWidth(k) + Verge, c.HalfWidth(k) + barrier[side][k]), 0f, true), 6f, 6f);
                }
                foreach (List<int> run in Runs(k => !open[side][k] && gravel[side][k] > Verge + 1f))
                    grav.Strip(Rows(run, k => Across(sign, c.HalfWidth(k) + Verge, c.HalfWidth(k) + gravel[side][k]), 0.03f, true), 3f, 3f);
            }
            Emit("Verges", verge, m?.Verge);
            Emit("Grass runoff", grass, m?.Grass);
            Emit("Gravel traps", grav, m?.Gravel);
        }

        private void Barriers()
        {
            var armco = new MeshKit();
            var tyres = new MeshKit();
            var wall = new MeshKit();
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;
                int side = s;
                foreach (List<int> run in Runs(k => !open[side][k] && !pitWall[side][k]))
                    armco.Strip(Vertical(run, k => c.HalfWidth(k) + barrier[side][k], sign, -Skirt, 0.8f), 4f, 4f, true);

                // Tyre walls in front of the armco where a gravel trap catches cars.
                foreach (List<int> run in Runs(k => !open[side][k] && !pitWall[side][k] && gravel[side][k] > Verge + 1f))
                {
                    tyres.Strip(Rows(run, k => Across(sign, c.HalfWidth(k) + barrier[side][k] - 1.2f, c.HalfWidth(k) + barrier[side][k] - 0.05f), 1.0f, true), 1f, 1f);
                    tyres.Strip(Vertical(run, k => c.HalfWidth(k) + barrier[side][k] - 1.2f, sign, -0.2f, 1.0f), 1f, 1f, true);
                }

                // Pit wall: concrete, between the start straight and the pit lane.
                foreach (List<int> run in Runs(k => pitWall[side][k]))
                {
                    wall.Strip(Rows(run, k => Across(sign, c.HalfWidth(k) + barrier[side][k] - 0.3f, c.HalfWidth(k) + barrier[side][k] + 0.3f), 1.2f, true), 1f, 1f);
                    wall.Strip(Vertical(run, k => c.HalfWidth(k) + barrier[side][k] - 0.3f, sign, -Skirt, 1.2f), 2f, 2f, true);
                    wall.Strip(Vertical(run, k => c.HalfWidth(k) + barrier[side][k] + 0.3f, sign, -Skirt, 1.2f), 2f, 2f, true);
                }
            }
            Emit("Armco", armco, m?.Armco, true);
            Emit("Tyre walls", tyres, m?.Tyres, true);
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
            float lat = Vector3.Dot(p - c.Centre[k], c.Left[k]);
            float edge = c.HalfWidth(k);
            if (lat < edge)
            {
                // Where the lane meets the track, stop it at the road edge instead of painting over the road.
                p += c.Left[k] * (edge - lat);
                lat = edge;
            }
            p.y = c.Surface(k, lat).y + 0.01f + lift;
            return p;
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
            var rows = new List<Vector3[]>();
            var skirtL = new List<Vector3[]>();
            var skirtR = new List<Vector3[]>();
            for (int i = 0; i < count; i++)
            {
                Vector3 l = LanePoint(i, laneHalf, 0), r = LanePoint(i, -laneHalf - 1.2f, 0);
                rows.Add(new[] { l, r });
                skirtL.Add(new[] { l, l + Vector3.down * Skirt });
                skirtR.Add(new[] { r, r + Vector3.down * Skirt });
            }
            surface.Strip(rows, 4f, 4f);
            surface.Strip(skirtL, 4f, 4f, true);
            surface.Strip(skirtR, 4f, 4f, true);
            Emit("Pit lane", surface, m?.PitAsphalt, true);

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

            bool Joining(int k) => !float.IsNaN(laneGap[k]) && laneGap[k] > 0f && laneGap[k] < PitWallGap;
            bool NearEntry(int k) => c.Ahead(pit.entry, k * c.Step) < 400f;
            bool NearExit(int k) => c.Ahead(k * c.Step, pit.exit) < 400f;

            // Tarmac between the track and the pit road where they join.
            var fill = new MeshKit();
            foreach (List<int> run in Runs(Joining))
                fill.Strip(Rows(run, k => new[] { c.HalfWidth(k) + laneGap[k] + 0.5f, c.HalfWidth(k) }, 0.004f), 4f, 4f);
            Emit("Pit road joins", fill, m?.PitAsphalt);

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
            Emit("Pit entry bollard", bollard, m?.Bollard);
        }

        private void Garages()
        {
            PitData pit = c.Data.pit;
            var walls = new MeshKit();
            var roof = new MeshKit();
            var paint = new MeshKit();
            float bay = pit.garageWidth, depth = pit.garageDepth, height = 7f;
            for (int g = 0; g < pit.garages.Length; g++)
            {
                GarageData gd = pit.garages[g];
                float rad = gd.heading * Mathf.Deg2Rad;
                var along = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                var toLane = new Vector3(along.z, 0, -along.x); // right of the row, towards the pit lane
                var floor = new Vector3(gd.x, gd.y, gd.z);
                int li = NearestLanePoint(floor);
                floor.y = LanePoint(li, laneHalf, 0).y;
                Vector3 side = -along; // local x, so that x, up and toLane form a left-handed frame
                Vector3 L(float x, float y, float z) => floor + side * x + Vector3.up * y + toLane * z;

                walls.Box(L(0, -1.02f, 0.5f), side, toLane, new Vector3(bay, 2f, depth + 1f));               // floor slab, up to the lane edge
                walls.Box(L(0, height * 0.5f, -depth * 0.5f + 0.15f), side, toLane, new Vector3(bay, height, 0.3f));
                walls.Box(L(-bay * 0.5f, height * 0.5f, 0), side, toLane, new Vector3(0.3f, height, depth));
                if (g == pit.garages.Length - 1 || g == 0) walls.Box(L(bay * 0.5f, height * 0.5f, 0), side, toLane, new Vector3(0.3f, height, depth));
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

        private void Ground()
        {
            GroundData t = c.Data.terrain;
            var rows = new List<Vector3[]>(t.countZ);
            for (int z = 0; z < t.countZ; z++)
            {
                var row = new Vector3[t.countX];
                for (int x = 0; x < t.countX; x++)
                    row[x] = new Vector3(t.originX + x * t.spacing, c.TerrainHeightAt(x, z), t.originZ + z * t.spacing);
                rows.Add(row);
            }
            var kit = new MeshKit();
            kit.Strip(rows, 8f, 8f);
            Emit("Ground", kit, m?.Terrain);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Lateral offsets for a band between two distances from the centre, ordered left to right.</summary>
        private static float[] Across(float sign, float a, float b) =>
            sign > 0 ? new[] { Mathf.Max(a, b), Mathf.Min(a, b) } : new[] { -Mathf.Min(a, b), -Mathf.Max(a, b) };

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

        /// <summary>The ground beside the road: level with the edge, then falling gently outwards.</summary>
        private Vector3 Point(int k, float lateral, float lift, bool runoff)
        {
            Vector3 p = c.Surface(k, lateral);
            if (runoff)
            {
                float beyond = Mathf.Abs(lateral) - c.HalfWidth(k);
                if (beyond > 0) p.y -= beyond * RunoffFall;
            }
            return p + Vector3.up * lift;
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

        private void Emit(string name, MeshKit kit, Material material, bool solid = false)
        {
            if (!kit.IsEmpty) parts.Add(new CircuitPart(name, kit, material, solid));
        }
    }
}
