using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit lane as the cars use it, with no Unity objects so it can be tested outside Unity: the
    /// limiter zone, each garage's box, and the route a car drives from the racing line into its box
    /// and back out. Lane offsets are across the lane, positive towards the garages. The fast lane runs
    /// next to the pit wall and the boxes sit in the working lane in front of the garages.
    /// </summary>
    public sealed class PitLane
    {
        /// <summary>A pitting car starts easing over to the pit side this far before the entry, m.</summary>
        public const float Approach = 300f;
        /// <summary>A car leaving the pits eases back onto its racing line over this far past the exit, m.</summary>
        public const float Rejoin = 300f;
        /// <summary>Track offset a pitting car keeps on the pit side until the entry road takes it further over.</summary>
        private const float EntrySide = 3f;
        private const int FastLaneEase = 20;   // lane samples (40 m) to move into or out of the fast lane
        // Into the box, in metres along the lane from its middle: straight for BoxHold either side, out by
        // BoxSwing towards the fast lane by BoxClear away, then on into the fast lane by BoxEase away. Out by
        // BoxSwing, a car passes the crew working round a car standing in the next box (14 m on at the bend,
        // 16 m on the straight) with over half a metre to spare.
        private const float BoxHold = 2f;
        private const float BoxClear = 10.5f;
        private const float BoxEase = 40f;
        private const float BoxSwing = 3.6f;

        public readonly Circuit Circuit;
        public readonly PitData Data;
        /// <summary>The limiter's speed, m/s.</summary>
        public readonly float LimitSpeed;
        /// <summary>Lane offset of the middle of the fast lane.</summary>
        public readonly float FastLane;
        /// <summary>Lane offset of the middle of each box.</summary>
        public readonly float BoxLateral;
        /// <summary>Lane offset a car keeps passing a box on its way to or from another, clear of the crew there.</summary>
        public float PassLateral => BoxLateral - BoxSwing;
        /// <summary>Where each garage's box is along the lane, as a fractional lane sample.</summary>
        public readonly float[] BoxLane;
        public readonly int LimitStartLane, LimitEndLane;

        private readonly float[] laneAt;   // fractional lane sample beside each track sample, or -1
        private readonly float[] laneMetres;   // distance along the lane to each lane sample, m
        private readonly float[] boxMetres;    // distance along the lane to each box's middle, m
        private readonly float laneHalf;
        private readonly float side;       // +1 when the pits are on the left of the track

        public PitLane(Circuit circuit)
        {
            Circuit = circuit;
            Data = circuit.Data.pit;
            laneHalf = Data.laneWidth * 0.5f;
            side = Data.side == "right" ? -1f : 1f;
            LimitSpeed = Data.limiterKph / 3.6f;
            FastLane = -laneHalf + Data.fastLane * 0.5f;
            BoxLateral = 0.5f * ((-laneHalf + Data.fastLane + 0.3f) + (laneHalf - 0.3f));

            laneAt = new float[circuit.Count];
            for (int k = 0; k < laneAt.Length; k++) laneAt[k] = -1f;
            float[] along = Data.laneTrackDistance;
            int lastK = -1;
            for (int i = 0; i < along.Length; i++)
            {
                int k = circuit.IndexOf(along[i]);
                if (lastK >= 0)
                {
                    // Fill any track sample the lane skipped, between its neighbours.
                    int gap = circuit.Wrap(k - lastK);
                    if (gap > 1 && gap < 10)
                        for (int j = 1; j < gap; j++) laneAt[circuit.Wrap(lastK + j)] = i - 1 + j / (float)gap;
                }
                if (laneAt[k] < 0f) laneAt[k] = i;
                lastK = k;
            }
            LimitStartLane = NearestLane(Data.limitStart);
            LimitEndLane = NearestLane(Data.limitEnd);

            laneMetres = new float[circuit.Lane.Length];
            for (int i = 1; i < laneMetres.Length; i++) laneMetres[i] = laneMetres[i - 1] + Flat(circuit.Lane[i] - circuit.Lane[i - 1]).magnitude;

            BoxLane = new float[Data.garages.Length];
            for (int g = 0; g < BoxLane.Length; g++)
            {
                GarageData gd = Data.garages[g];
                var floor = new Vector3(gd.x, gd.y, gd.z);
                int best = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < circuit.Lane.Length; i++)
                {
                    float d = Flat(circuit.Lane[i] - floor).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                int next = Mathf.Min(best + 1, circuit.Lane.Length - 1);
                Vector3 step = Flat(circuit.Lane[next] - circuit.Lane[best]);
                float t = step.sqrMagnitude > 1e-4f ? Vector3.Dot(Flat(floor - circuit.Lane[best]), step) / step.sqrMagnitude : 0f;
                BoxLane[g] = Mathf.Clamp(best + t, 0f, circuit.Lane.Length - 1);
            }
            boxMetres = new float[BoxLane.Length];
            for (int g = 0; g < BoxLane.Length; g++) boxMetres[g] = Metres(BoxLane[g]);
        }

        public int Garages => BoxLane.Length;

        /// <summary>True beside the pit lane, from the entry to the exit.</summary>
        public bool InRange(int k) => laneAt[k] >= 0f;

        /// <summary>The fractional lane sample beside a track sample, or -1 away from the pit lane.</summary>
        public float LaneAt(int k) => laneAt[k];

        /// <summary>True where the limiter applies, beside a track sample.</summary>
        public bool InLimitStretch(int k) => laneAt[k] >= LimitStartLane && laneAt[k] <= LimitEndLane;

        /// <summary>
        /// True when a car is in the limiter zone: past the limit line, off the track and on the pit lane.
        /// <c>k</c> is the car's track sample.
        /// </summary>
        public bool InLimitZone(Vector3 p, int k)
        {
            if (!InLimitStretch(k)) return false;
            Circuit c = Circuit;
            Vector3 d = p - c.Centre[k];
            float lateral = side * (d.x * c.Left[k].x + d.z * c.Left[k].z);
            if (lateral < c.HalfWidth(k) + 0.3f) return false;
            return OnLane(p, k);
        }

        /// <summary>True on the pit lane surface, entry and exit roads included. <c>k</c> is the nearest track sample.</summary>
        public bool OnLane(Vector3 p, int k)
        {
            if (!InRange(k)) return false;
            float i = NearestLane(p, k);
            float across = Vector3.Dot(Flat(p - LanePoint(i, 0f)), LaneLeft(i));
            return Mathf.Abs(across) < laneHalf + 1.5f;
        }

        /// <summary>
        /// The lane sample nearest a point beside track sample k. Where the pit road cuts across a bend
        /// of the track the two drift apart, so it searches around the one beside k.
        /// </summary>
        public float NearestLane(Vector3 p, int k)
        {
            Vector3[] lane = Circuit.Lane;
            int from = Mathf.Max((int)laneAt[k] - 80, 0), to = Mathf.Min((int)laneAt[k] + 80, lane.Length - 1);
            int best = from;
            float bestD = float.MaxValue;
            for (int i = from; i <= to; i++)
            {
                float d = Flat(lane[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>True when a car at this track offset is off the track on the pit lane side, beside the pit lane.</summary>
        public bool OffTrackInLane(int k, float lateral) => InRange(k) && side * lateral > Circuit.HalfWidth(k) + 2f;

        /// <summary>Middle of a garage's box, on the lane surface.</summary>
        public Vector3 BoxPoint(int garage) => LanePoint(BoxLane[garage], BoxLateral);

        /// <summary>A point on the pit lane at a fractional lane sample and an offset across it.</summary>
        public Vector3 LanePoint(float i, float across)
        {
            Vector3[] lane = Circuit.Lane;
            int a = Mathf.Clamp((int)i, 0, lane.Length - 2);
            float t = Mathf.Clamp01(i - a);
            return Vector3.Lerp(lane[a], lane[a + 1], t) + LaneLeft(i) * across;
        }

        /// <summary>Unit vector across the lane towards the garages, flat.</summary>
        public Vector3 LaneLeft(float i)
        {
            Vector3[] lane = Circuit.Lane;
            int a = Mathf.Clamp(Mathf.RoundToInt(i), 0, lane.Length - 1);
            Vector3 t = Flat(lane[Mathf.Min(a + 1, lane.Length - 1)] - lane[Mathf.Max(a - 1, 0)]).normalized;
            return new Vector3(-t.z, 0f, t.x);
        }

        /// <summary>
        /// The path a car drives into the pits: its racing line until <see cref="Approach"/> before the
        /// entry, over to the pit side, down the entry road, along the fast lane and into its garage's
        /// box, then back out along the fast lane, down the exit road and onto its racing line again.
        /// With no garage (-1) it stays in the fast lane: the pace car's way off the track.
        /// </summary>
        public RacingLine Route(RacingLine from, int garage)
        {
            Circuit c = Circuit;
            int n = c.Count;
            var points = new Vector3[n];
            System.Array.Copy(from.Point, points, n);
            var smooth = new bool[n];

            int steps = Mathf.RoundToInt(Approach / c.Step);
            int k0 = c.IndexOf(Data.entry - Approach);
            for (int j = 0; j < steps; j++)
            {
                int k = c.Wrap(k0 + j);
                float lat = Mathf.Lerp(from.Lateral[k], side * EntrySide, Ease(j / (float)steps));
                points[k] = c.Surface(k, lat);
                smooth[k] = true;
            }

            int kEntry = c.IndexOf(Data.entry);
            while (!InRange(kEntry)) kEntry = c.Wrap(kEntry + 1);
            int last = kEntry;
            for (int j = 0; j < n && InRange(c.Wrap(kEntry + j)); j++)
            {
                int k = c.Wrap(kEntry + j);
                float i = laneAt[k];
                Vector3 p = LanePoint(i, Across(i, garage));
                Vector3 d = p - c.Centre[k];
                float lat = side * (d.x * c.Left[k].x + d.z * c.Left[k].z);
                // Where the entry road still overlaps the track, keep to the pit side of it.
                if (i < LimitStartLane + FastLaneEase && lat < EntrySide) p = c.Surface(k, side * EntrySide);
                points[k] = p;
                // The lane is smooth already, and bends hard round the hairpin: only the entry road's join is rounded.
                smooth[k] = i < LimitStartLane + FastLaneEase + 10;
                last = k;
            }

            steps = Mathf.RoundToInt(Rejoin / c.Step);
            Vector3 e = points[last] - c.Centre[last];
            float exitLat = e.x * c.Left[last].x + e.z * c.Left[last].z;
            for (int j = 1; j <= steps; j++)
            {
                int k = c.Wrap(last + j);
                points[k] = c.Surface(k, Mathf.Lerp(exitLat, from.Lateral[k], Ease(j / (float)steps)));
                smooth[k] = true;
            }

            // Round off the joins: a few passes of averaging over 18 m.
            var x = new float[n];
            var z = new float[n];
            for (int pass = 0; pass < 6; pass++)
            {
                for (int k = 0; k < n; k++)
                {
                    x[k] = points[k].x;
                    z[k] = points[k].z;
                    if (!smooth[k]) continue;
                    float sx = 0f, sz = 0f;
                    for (int j = -4; j <= 4; j++) { Vector3 q = points[c.Wrap(k + j)]; sx += q.x; sz += q.z; }
                    x[k] = sx / 9f;
                    z[k] = sz / 9f;
                }
                for (int k = 0; k < n; k++) { points[k].x = x[k]; points[k].z = z[k]; }
            }
            return new RacingLine(c, points);
        }

        /// <summary>Most a car may go at each sample of a route: just under the limiter in the limiter zone.</summary>
        public float[] Limits()
        {
            var max = new float[Circuit.Count];
            for (int k = 0; k < max.Length; k++) max[k] = InLimitStretch(k) ? LimitSpeed * 0.95f : float.PositiveInfinity;
            return max;
        }

        /// <summary>The track sample beside a garage's box.</summary>
        public int BoxSample(int garage)
        {
            int best = 0;
            float bestGap = float.MaxValue;
            for (int k = 0; k < laneAt.Length; k++)
            {
                if (laneAt[k] < 0f) continue;
                float gap = Mathf.Abs(laneAt[k] - BoxLane[garage]);
                if (gap < bestGap) { bestGap = gap; best = k; }
            }
            return best;
        }

        /// <summary>Track sample a route has rejoined the racing line by.</summary>
        public int RejoinedSample()
        {
            int k = Circuit.IndexOf(Data.exit);
            while (InRange(k)) k = Circuit.Wrap(k + 1);
            return Circuit.Wrap(k + Mathf.RoundToInt(Rejoin / Circuit.Step));
        }

        /// <summary>Where across the lane a route runs at a lane sample.</summary>
        private float Across(float i, int garage)
        {
            float into = Ease((i - LimitStartLane) / FastLaneEase);
            float outOf = Ease((LimitEndLane - i) / FastLaneEase);
            float across = FastLane * Mathf.Min(into, outOf);
            if (garage >= 0)
            {
                float off = Mathf.Abs(Metres(i) - boxMetres[garage]);
                if (off <= BoxClear) across = Mathf.Lerp(BoxLateral, PassLateral, Swing((off - BoxHold) / (BoxClear - BoxHold)));
                else if (off < BoxEase) across = Mathf.Lerp(PassLateral, across, Ease((off - BoxClear) / (BoxEase - BoxClear)));
            }
            return across;
        }

        /// <summary>Distance along the lane to a fractional lane sample, m.</summary>
        private float Metres(float i)
        {
            int a = Mathf.Clamp((int)i, 0, laneMetres.Length - 2);
            return Mathf.Lerp(laneMetres[a], laneMetres[a + 1], i - a);
        }

        private int NearestLane(float trackDistance)
        {
            float[] d = Data.laneTrackDistance;
            int best = 0;
            float bestGap = float.MaxValue;
            for (int i = 0; i < d.Length; i++)
            {
                float g = Mathf.Abs(Mathf.DeltaAngle(d[i] / Circuit.Lap * 360f, trackDistance / Circuit.Lap * 360f));
                if (g < bestGap) { bestGap = g; best = i; }
            }
            return best;
        }

        /// <summary>A half wave from 0 to 1: the gentlest bend for a sideways step, flat at both ends.</summary>
        private static float Swing(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(t));

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
