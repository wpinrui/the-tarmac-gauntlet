using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The fastest-looking path round the circuit: one lateral offset per track sample, chosen to
    /// keep the line as straight as the track edges allow (outside, apex, outside). It depends only
    /// on the circuit, so every car shares it. Curvature is positive for a right-hander.
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
        public readonly float Length;

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

            Relax();
            for (int k = 0; k < Count; k++) Point[k] = circuit.Surface(k, Lateral[k]);
            for (int k = 0; k < Count; k++)
            {
                Segment[k] = (Point[circuit.Wrap(k + 1)] - Point[k]).magnitude;
                Length += Segment[k];
            }
            Shape();
        }

        private float Limit(int k) => Mathf.Max(Circuit.HalfWidth(k) - EdgeMargin, 0f);

        /// <summary>
        /// Pulls each point towards the middle of its neighbours, coarse spacing first, then finer,
        /// clamped to the track. This settles on the smoothest path between the edges.
        /// </summary>
        private void Relax()
        {
            Circuit c = Circuit;
            var flat = new Vector3[Count];
            for (int k = 0; k < Count; k++) flat[k] = new Vector3(c.Centre[k].x, 0f, c.Centre[k].z);
            int[] spans = { 96, 64, 40, 24, 16, 10, 6, 4, 2, 1 };
            foreach (int m in spans)
            {
                int passes = m >= 16 ? 60 : 120;
                for (int pass = 0; pass < passes; pass++)
                {
                    for (int k = 0; k < Count; k++)
                    {
                        int a = c.Wrap(k - m), b = c.Wrap(k + m);
                        Vector3 pa = flat[a] + c.Left[a] * Lateral[a];
                        Vector3 pb = flat[b] + c.Left[b] * Lateral[b];
                        Vector3 p = flat[k] + c.Left[k] * Lateral[k];
                        float move = Vector3.Dot((pa + pb) * 0.5f - p, c.Left[k]);
                        float lim = Limit(k);
                        Lateral[k] = Mathf.Clamp(Lateral[k] + 0.8f * move, -lim, lim);
                    }
                }
            }
        }

        private void Shape()
        {
            Circuit c = Circuit;
            const int m = 3;   // 6 m either side, enough to smooth the 2 m sampling
            for (int k = 0; k < Count; k++)
            {
                Vector3 a = Flat(Point[c.Wrap(k - m)]), b = Flat(Point[k]), d = Flat(Point[c.Wrap(k + m)]);
                Vector3 ab = b - a, bd = d - b, ad = d - a;
                float cross = ab.z * bd.x - ab.x * bd.z;   // y of Cross(ab, bd): positive turning right
                Curvature[k] = 2f * cross / Mathf.Max(ab.magnitude * bd.magnitude * ad.magnitude, 1e-4f);
                Tangent[k] = ad.normalized;
            }
            // Average out the sampling noise in the curvature.
            var smooth = new float[Count];
            for (int k = 0; k < Count; k++)
            {
                float sum = 0f;
                for (int j = -2; j <= 2; j++) sum += Curvature[c.Wrap(k + j)];
                smooth[k] = sum / 5f;
            }
            System.Array.Copy(smooth, Curvature, Count);

            const int v = 5;   // 10 m either side for heights
            for (int k = 0; k < Count; k++)
            {
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
