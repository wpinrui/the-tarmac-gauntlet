using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The shape of a lap in time: where on the lap a car is after a given share of its lap
    /// time. Cars brake for corners and accelerate out of them instead of moving at one speed.
    /// Only the shape is used; each car's own lap time sets how fast it plays.
    /// </summary>
    public sealed class SpeedProfile
    {
        private const float Step = 2f;       // world metres between samples
        private const float TopSpeed = 70f;  // m/s, shape only
        private const float Grip = 22f;      // m/s² lateral
        private const float Accel = 8f;      // m/s²
        private const float Brake = 14f;     // m/s²

        private readonly float[] timeAt;     // cumulative time at each sample, normalised to 0..1
        private readonly float length;

        public SpeedProfile(Polyline line)
        {
            length = line.Length;
            int n = Mathf.Max(2, Mathf.CeilToInt(length / Step));
            float ds = length / n;
            var v = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float k = Mathf.Abs(line.Curvature(i * ds));
                v[i] = k > 1e-5f ? Mathf.Min(TopSpeed, Mathf.Sqrt(Grip / k)) : TopSpeed;
            }
            // Two passes each way so the loop's join is smooth too.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i <= n; i++) v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i - 1] * v[i - 1] + 2 * Accel * ds));
                v[0] = Mathf.Min(v[0], v[n]);
                for (int i = n - 1; i >= 0; i--) v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i + 1] * v[i + 1] + 2 * Brake * ds));
                v[n] = Mathf.Min(v[n], v[0]);
            }

            timeAt = new float[n + 1];
            for (int i = 1; i <= n; i++) timeAt[i] = timeAt[i - 1] + 2 * ds / (v[i] + v[i - 1]);
            float total = timeAt[n];
            for (int i = 0; i <= n; i++) timeAt[i] /= total;
        }

        /// <summary>Distance along the lap after the given share (0 to 1) of the lap time.</summary>
        public float DistanceAt(float timeFraction)
        {
            timeFraction = Mathf.Clamp01(timeFraction);
            int lo = 0, hi = timeAt.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (timeAt[mid] <= timeFraction) lo = mid; else hi = mid;
            }
            float span = timeAt[hi] - timeAt[lo];
            float t = span > 0 ? (timeFraction - timeAt[lo]) / span : 0;
            return (lo + t) / (timeAt.Length - 1) * length;
        }
    }
}
