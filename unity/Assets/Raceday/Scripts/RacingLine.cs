using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The racing line: the path within the track edges that gives the shortest lap time, as one
    /// lateral offset per track sample. It starts as the minimum-curvature line, which uses the full
    /// width (outside, apex, outside) and touches the inside edge only at apexes. For a given car it
    /// is then nudged towards minimum lap time using that car's speed profile, which mostly moves
    /// apexes later before long straights. Curvature is positive for a right-hander.
    /// </summary>
    public sealed class RacingLine
    {
        /// <summary>Car centre stays this far inside the white line, so the wheels just touch it.</summary>
        public const float EdgeMargin = 1.25f;

        public readonly Circuit Circuit;
        public readonly int Count;
        /// <summary>Offset from the track centre, positive to the left.</summary>
        public readonly float[] Lateral;
        public readonly Vector3[] Point;
        /// <summary>Unit direction along the line, flat.</summary>
        public readonly Vector3[] Tangent;
        /// <summary>Plan curvature, 1/m, positive turning right.</summary>
        public readonly float[] Curvature;
        /// <summary>Second derivative of height along the line, 1/m: positive in a dip, negative over a crest.</summary>
        public readonly float[] VerticalCurvature;
        /// <summary>Rise per metre along the line.</summary>
        public readonly float[] Grade;
        /// <summary>Length of the piece from sample k to k + 1.</summary>
        public readonly float[] Segment;
        public float Length { get; private set; }
        private readonly float[] rawCurvature;
        /// <summary>Lap time of the car this line was tuned for, driving it at its limit, or 0 if untuned.</summary>
        public float TunedLapTime { get; private set; }

        /// <summary>The minimum-curvature line, which suits every car.</summary>
        public RacingLine(Circuit circuit)
        {
            Circuit = circuit;
            Count = circuit.Count;
            Lateral = new float[Count];
            Point = new Vector3[Count];
            Tangent = new Vector3[Count];
            Curvature = new float[Count];
            VerticalCurvature = new float[Count];
            Grade = new float[Count];
            Segment = new float[Count];
            rawCurvature = new float[Count];

            Relax();
            Rebuild();
        }

        /// <summary>The minimum-curvature line, then tuned towards minimum lap time for this car.</summary>
        public RacingLine(Circuit circuit, CarSpec spec) : this(circuit)
        {
            TuneFor(spec);
        }

        /// <summary>
        /// A lane beside another line: that line moved sideways by <c>shift</c> metres (positive
        /// left), kept within the track. Where the move would cross the edge the lane runs along the
        /// edge instead, and the bend where it meets the edge is smoothed out. Used for passing,
        /// defending and making room.
        /// </summary>
        public RacingLine(RacingLine from, float shift)
        {
            Circuit = from.Circuit;
            Count = from.Count;
            Lateral = new float[Count];
            Point = new Vector3[Count];
            Tangent = new Vector3[Count];
            Curvature = new float[Count];
            VerticalCurvature = new float[Count];
            Grade = new float[Count];
            Segment = new float[Count];
            rawCurvature = new float[Count];

            var lim = new float[Count];
            for (int k = 0; k < Count; k++)
            {
                lim[k] = Limit(k);
                Lateral[k] = Mathf.Clamp(from.Lateral[k] + shift, -lim[k], lim[k]);
            }
            // Ease the corners where the lane meets the edge: a few passes of averaging over 10 m.
            var next = new float[Count];
            for (int pass = 0; pass < 12; pass++)
            {
                for (int k = 0; k < Count; k++)
                {
                    float sum = 0f;
                    for (int j = -5; j <= 5; j++) sum += Lateral[Circuit.Wrap(k + j)];
                    next[k] = Mathf.Clamp(sum / 11f, -lim[k], lim[k]);
                }
                System.Array.Copy(next, Lateral, Count);
            }
            Rebuild();
            TunedLapTime = from.TunedLapTime;
        }

        /// <summary>
        /// A line at a fixed offset from the track centre (positive left), kept within the track:
        /// a column of the formation behind the pace car.
        /// </summary>
        public RacingLine(Circuit circuit, float lateral)
        {
            Circuit = circuit;
            Count = circuit.Count;
            Lateral = new float[Count];
            Point = new Vector3[Count];
            Tangent = new Vector3[Count];
            Curvature = new float[Count];
            VerticalCurvature = new float[Count];
            Grade = new float[Count];
            Segment = new float[Count];
            rawCurvature = new float[Count];
            for (int k = 0; k < Count; k++)
            {
                float lim = Limit(k);
                Lateral[k] = Mathf.Clamp(lateral, -lim, lim);
            }
            Rebuild();
        }

        /// <summary>
        /// A path given point by point, one per track sample, which may leave the track: a pit route.
        /// <see cref="Lateral"/> is each point's offset from the track centre.
        /// </summary>
        public RacingLine(Circuit circuit, Vector3[] points)
        {
            Circuit = circuit;
            Count = circuit.Count;
            Lateral = new float[Count];
            Point = new Vector3[Count];
            Tangent = new Vector3[Count];
            Curvature = new float[Count];
            VerticalCurvature = new float[Count];
            Grade = new float[Count];
            Segment = new float[Count];
            rawCurvature = new float[Count];
            for (int k = 0; k < Count; k++)
            {
                Point[k] = points[k];
                Vector3 d = points[k] - circuit.Centre[k];
                Lateral[k] = d.x * circuit.Left[k].x + d.z * circuit.Left[k].z;
            }
            Rebuild(0, Count - 1, false);
        }

        /// <summary>Works out the line's points and shape from <see cref="Lateral"/>.</summary>
        private void Rebuild() => Rebuild(0, Count - 1);

        /// <summary>
        /// Works out the points and shape after the offsets from sample <c>from</c> to <c>to</c>
        /// changed: only that stretch, plus the neighbours its shape reaches, is redone.
        /// </summary>
        private void Rebuild(int from, int to, bool place = true)
        {
            Circuit c = Circuit;
            bool all = to - from >= Count - 1;
            if (place) for (int i = from; i <= to; i++) { int k = c.Wrap(i); Point[k] = c.Surface(k, Lateral[k]); }
            int reach = all ? 0 : 8;
            for (int i = from - reach; i <= to + reach; i++)
            {
                int k = c.Wrap(i);
                float before = Segment[k];
                Segment[k] = (Point[c.Wrap(k + 1)] - Point[k]).magnitude;
                if (!all) Length += Segment[k] - before;
            }
            if (all)
            {
                float length = 0f;
                for (int k = 0; k < Count; k++) length += Segment[k];
                Length = length;
            }
            Shape(from - reach, to + reach);
        }

        /// <summary>
        /// Minimum lap time: tries pushing each 20 m stretch of the line a little left or right with a
        /// smooth bump, keeps any push that makes the car's predicted lap faster, and repeats with
        /// smaller pushes. Exit speed onto a long straight is worth most, so apexes move later there.
        /// </summary>
        private void TuneFor(CarSpec spec)
        {
            const int Spacing = 10;     // a bump every 20 m
            const int HalfWidth = 20;   // each bump spans 80 m
            float best = new SpeedProfile(this, spec, 1f).LapTime;
            var saved = new float[2 * HalfWidth + 1];
            foreach (float size in new[] { 1.2f, 0.6f, 0.3f })
            {
                for (int centre = 0; centre < Count; centre += Spacing)
                {
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        bool moved = false;
                        for (int j = -HalfWidth; j <= HalfWidth; j++)
                        {
                            int k = Circuit.Wrap(centre + j);
                            saved[j + HalfWidth] = Lateral[k];
                            float bump = 0.5f * (1f + Mathf.Cos(Mathf.PI * j / HalfWidth));
                            float lim = Limit(k);
                            float next = Mathf.Clamp(Lateral[k] + sign * size * bump, -lim, lim);
                            moved |= next != Lateral[k];
                            Lateral[k] = next;
                        }
                        if (!moved) continue;
                        Rebuild(centre - HalfWidth, centre + HalfWidth);
                        float time = new SpeedProfile(this, spec, 1f).LapTime;
                        if (time < best - 0.001f)
                        {
                            best = time;
                            break;   // keep it, and don't try the other way
                        }
                        for (int j = -HalfWidth; j <= HalfWidth; j++) Lateral[Circuit.Wrap(centre + j)] = saved[j + HalfWidth];
                        Rebuild(centre - HalfWidth, centre + HalfWidth);
                    }
                }
            }
            Rebuild();
            TunedLapTime = best;
        }

        private float Limit(int k) => Mathf.Max(Circuit.HalfWidth(k) - EdgeMargin, 0f);

        /// <summary>
        /// Minimum curvature: moves each point sideways to make the bend through it and its
        /// neighbours as gentle as possible, clamped to the track. Coarse spacing first shapes whole
        /// corners (outside, apex, outside), finer spacing then smooths the detail. Pulling towards the
        /// neighbours' midpoint instead would find the shortest path, which hugs every inside kerb.
        /// </summary>
        private void Relax()
        {
            Circuit c = Circuit;
            int n = Count;
            // Flat positions as plain arrays, kept in step with Lateral, so the many passes stay cheap.
            var cx = new float[n]; var cz = new float[n]; var lx = new float[n]; var lz = new float[n];
            var px = new float[n]; var pz = new float[n]; var lim = new float[n];
            for (int k = 0; k < n; k++)
            {
                cx[k] = c.Centre[k].x; cz[k] = c.Centre[k].z;
                lx[k] = c.Left[k].x; lz[k] = c.Left[k].z;
                px[k] = cx[k]; pz[k] = cz[k];
                lim[k] = Limit(k);
            }

            int[] spans = { 48, 32, 24, 16, 12, 8, 6, 4, 3, 2 };
            foreach (int m in spans)
            {
                for (int pass = 0; pass < 300; pass++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        // Newton step on the squared bends at k - m, k and k + m, along the sideways
                        // direction: the fourth difference P(k-2m) - 4P(k-m) + 6P(k) - 4P(k+m) + P(k+2m).
                        int a2 = (k - 2 * m + 2 * n) % n, a1 = (k - m + n) % n, b1 = (k + m) % n, b2 = (k + 2 * m) % n;
                        float gx = px[a2] - 4f * px[a1] + 6f * px[k] - 4f * px[b1] + px[b2];
                        float gz = pz[a2] - 4f * pz[a1] + 6f * pz[k] - 4f * pz[b1] + pz[b2];
                        float move = -(gx * lx[k] + gz * lz[k]) / 6f;
                        float lat = Lateral[k] + move;
                        lat = lat < -lim[k] ? -lim[k] : lat > lim[k] ? lim[k] : lat;
                        Lateral[k] = lat;
                        px[k] = cx[k] + lx[k] * lat;
                        pz[k] = cz[k] + lz[k] * lat;
                    }
                }
            }
        }

        private void Shape(int from, int to)
        {
            Circuit c = Circuit;
            const int m = 3;   // 6 m either side, enough to smooth the 2 m sampling
            for (int i = from - 2; i <= to + 2; i++)
            {
                int k = c.Wrap(i);
                Vector3 a = Flat(Point[c.Wrap(k - m)]), b = Flat(Point[k]), d = Flat(Point[c.Wrap(k + m)]);
                Vector3 ab = b - a, bd = d - b, ad = d - a;
                float cross = ab.z * bd.x - ab.x * bd.z;   // y of Cross(ab, bd): positive turning right
                rawCurvature[k] = 2f * cross / Mathf.Max(ab.magnitude * bd.magnitude * ad.magnitude, 1e-4f);
                Tangent[k] = ad.normalized;
            }
            // Average out the sampling noise in the curvature.
            for (int i = from; i <= to; i++)
            {
                int k = c.Wrap(i);
                float sum = 0f;
                for (int j = -2; j <= 2; j++) sum += rawCurvature[c.Wrap(k + j)];
                Curvature[k] = sum / 5f;
            }

            const int v = 5;   // 10 m either side for heights
            for (int i = from; i <= to; i++)
            {
                int k = c.Wrap(i);
                float y0 = Point[c.Wrap(k - v)].y, y1 = Point[k].y, y2 = Point[c.Wrap(k + v)].y;
                float h = v * c.Step;
                VerticalCurvature[k] = (y2 - 2f * y1 + y0) / (h * h);
                Grade[k] = (Point[c.Wrap(k + 1)].y - Point[c.Wrap(k - 1)].y) / (2f * c.Step);
            }
        }

        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        /// <summary>
        /// The line sample nearest a point, searching forwards from a previous one. Pass -1 to search the whole lap.
        /// </summary>
        public int Nearest(Vector3 p, int from, int back = 8, int ahead = 40)
        {
            int start = from < 0 ? 0 : from - back;
            int end = from < 0 ? Count : from + ahead;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = start; i < end; i++)
            {
                int k = Circuit.Wrap(i);
                Vector3 d = Point[k] - p;
                d.y *= 0.25f;   // height counts less than position across the ground
                float dist = d.sqrMagnitude;
                if (dist < bestDistance) { bestDistance = dist; best = k; }
            }
            return best;
        }
    }
}
