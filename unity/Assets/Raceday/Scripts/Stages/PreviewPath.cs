using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Where the race preview's camera goes (<see cref="BroadcastIntro"/>), with no Unity objects so it can be checked
    /// outside Unity. The opening shot rises over the start line. The flyover runs one lap above the track in
    /// <see cref="Duration"/>, slowing for each bend so its heading never turns faster than <see cref="TurnRate"/>, looking down the road ahead. The track is smoothed under it, so
    /// it sweeps through a hairpin rather than turning on the spot. Each corner shot swings slowly round a corner from
    /// its outside, looking at its middle. Every shot keeps clear of the ground.
    /// </summary>
    public sealed class PreviewPath
    {
        public struct Pose
        {
            public Vector3 Position;
            /// <summary>The point the camera looks at.</summary>
            public Vector3 Target;
        }

        /// <summary>The flyover's height above the track, m.</summary>
        public const float Height = 55f;
        /// <summary>How far ahead of itself the flyover looks, m: it looks down at the road that far on.</summary>
        public const float Ahead = 220f;
        /// <summary>The flyover looks along the track's heading this far ahead of it, m.</summary>
        private const float Lead = 30f;
        /// <summary>The track is smoothed over this far either side of the camera, m.</summary>
        public const float Smoothing = 90f;
        /// <summary>The least height above the ground for any shot, m.</summary>
        public const float Clearance = 25f;
        /// <summary>The flyover's speed on the straights, m/s.</summary>
        private const float Fast = 320f;
        /// <summary>The fastest the flyover's heading turns, degrees a second: it slows for a bend to keep to it.</summary>
        private const float TurnRate = 60f;
        /// <summary>How hard the flyover speeds up and slows down, m/s².</summary>
        private const float Accel = 100f;
        /// <summary>The flyover's heading is measured over this far either side of a point, m.</summary>
        private const float Bend = 20f;
        /// <summary>A corner is called out this far before it starts, m.</summary>
        public const float CallOut = 250f;
        /// <summary>A corner's call stays up this far past its end, m.</summary>
        public const float CallOff = 100f;

        /// <summary>The corner shot's distance from the corner, its height, and how far it swings round, degrees.</summary>
        private const float OrbitRadius = 110f, OrbitHeight = 40f, OrbitSwing = 40f;
        /// <summary>The opening shot: how far behind the start line it starts and ends, and how high it rises, m.</summary>
        private const float OpenFrom = 260f, OpenTo = 180f, OpenLow = 30f, OpenHigh = 60f, OpenLook = 80f;

        /// <summary>How long the flyover's lap takes, real seconds.</summary>
        public readonly float Duration;
        private readonly Circuit circuit;
        private readonly Vector3[] smooth;
        /// <summary>Flyover seconds at each track sample, from 0 at the start line to <see cref="Duration"/> a lap on.</summary>
        private readonly float[] clock;
        private readonly CornerData[] corners;

        public PreviewPath(Circuit circuit)
        {
            this.circuit = circuit;
            corners = circuit.Data.corners ?? new CornerData[0];
            int n = circuit.Count;
            float step = circuit.Step;
            smooth = new Vector3[n];
            int half = Mathf.Max(1, Mathf.RoundToInt(Smoothing / step));
            Vector3 sum = Vector3.zero;
            for (int j = -half; j <= half; j++) sum += circuit.Centre[circuit.Wrap(j)];
            for (int k = 0; k < n; k++)
            {
                smooth[k] = sum / (2 * half + 1);
                sum += circuit.Centre[circuit.Wrap(k + half + 1)] - circuit.Centre[circuit.Wrap(k - half)];
            }
            // As fast as the bend of the smoothed track lets the heading keep to TurnRate, then eased in and out of each bend.
            var speed = new float[n];
            int m = Mathf.Max(1, Mathf.RoundToInt(Bend / step));
            float rate = TurnRate * Mathf.Deg2Rad;
            for (int k = 0; k < n; k++)
            {
                Vector3 a = smooth[k] - smooth[circuit.Wrap(k - m)], b = smooth[circuit.Wrap(k + m)] - smooth[k];
                float turn = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg, Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float length = (a.magnitude + b.magnitude) / 2f;
                speed[k] = turn > 1e-4f ? Mathf.Min(Fast, rate * length / turn) : Fast;
            }
            // The camera looks along the heading Lead ahead, so it slows for that bend too.
            int lead = Mathf.RoundToInt(Lead / step);
            var bend = (float[])speed.Clone();
            for (int k = 0; k < n; k++) speed[k] = Mathf.Min(bend[k], bend[circuit.Wrap(k + lead)]);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 1; k < 2 * n; k++)
                {
                    int i = k % n, prev = (k - 1) % n;
                    speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(speed[prev] * speed[prev] + 2f * Accel * step));
                }
                for (int k = 2 * n - 2; k >= 0; k--)
                {
                    int i = k % n, next = (k + 1) % n;
                    speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(speed[next] * speed[next] + 2f * Accel * step));
                }
            }
            clock = new float[n + 1];
            for (int k = 0; k < n; k++) clock[k + 1] = clock[k] + step / Mathf.Max(speed[k], 1f);
            Duration = clock[n];
        }

        /// <summary>The flyover's lap distance t seconds in, m.</summary>
        public float DistanceAt(float t)
        {
            int n = circuit.Count;
            t = Mathf.Clamp(t, 0f, Duration);
            int lo = 0, hi = n;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (clock[mid] <= t) lo = mid;
                else hi = mid;
            }
            float span = clock[hi] - clock[lo];
            float u = span > 0f ? (t - clock[lo]) / span : 0f;
            return Mathf.Min((lo + u) * circuit.Step, circuit.Lap);
        }

        /// <summary>The smoothed track at a lap distance, between samples.</summary>
        private Vector3 Smooth(float d)
        {
            float f = Mathf.Repeat(d, circuit.Lap) / circuit.Step;
            int k = Mathf.FloorToInt(f);
            return Vector3.Lerp(smooth[circuit.Wrap(k)], smooth[circuit.Wrap(k + 1)], f - k);
        }

        /// <summary>The flyover t seconds in.</summary>
        public Pose Fly(float t)
        {
            float d = DistanceAt(t);
            // Down the smoothed track's heading a little ahead, which turns no faster than the flyover does,
            // rather than at a point down the road, which swings across the infield of a hairpin.
            Vector3 heading = Smooth(d + Lead + Bend) - Smooth(d + Lead - Bend);
            heading.y = 0f;
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : circuit.Forward[circuit.IndexOf(d)];
            Vector3 at = Clear(Smooth(d) + Vector3.up * Height);
            Vector3 target = at + heading * Ahead;
            target.y = Smooth(d + Ahead).y;
            return new Pose { Position = at, Target = target };
        }

        /// <summary>The opening shot, u from 0 to 1 through it: rising and closing on the start line from behind.</summary>
        public Pose Open(float u)
        {
            u = Mathf.Clamp01(u);
            float d = -Mathf.Lerp(OpenFrom, OpenTo, u);
            Vector3 at = Smooth(d) + Vector3.up * Mathf.Lerp(OpenLow, OpenHigh, u);
            return new Pose { Position = Clear(at), Target = circuit.Centre[circuit.IndexOf(OpenLook)] };
        }

        /// <summary>The corner shot of corner i, u from 0 to 1 through it.</summary>
        public Pose Orbit(int i, float u)
        {
            CornerData c = corners[i];
            int mid = circuit.IndexOf((c.s + c.e) / 2f);
            Vector3 apex = circuit.Centre[mid];
            Vector3 outside = circuit.Left[mid] * Direction(c);
            float angle = Mathf.Lerp(-OrbitSwing / 2f, OrbitSwing / 2f, Mathf.Clamp01(u)) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var round = new Vector3(outside.x * cos - outside.z * sin, 0f, outside.x * sin + outside.z * cos);
            Vector3 at = apex + round * OrbitRadius + Vector3.up * OrbitHeight;
            return new Pose { Position = Clear(at), Target = apex };
        }

        /// <summary>+1 for a corner that turns right overall, -1 for one that turns left: its outside is to the left of a right-hander.</summary>
        public static float Direction(CornerData c)
        {
            float turn = 0f;
            if (c.arcs != null) foreach (ArcData a in c.arcs) turn += a.dir * (a.e - a.s) / Mathf.Max(a.radius, 1f);
            return turn >= 0f ? 1f : -1f;
        }

        /// <summary>
        /// The corner called out at lap distance d: from <see cref="CallOut"/> before it to <see cref="CallOff"/> past its end,
        /// or to its middle when the next corner is near enough to call. -1 for none.
        /// </summary>
        public int CornerAt(float d)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                if (d < corners[i].s - CallOut || d > corners[i].e + CallOff) continue;
                // Past a corner's middle, the next corner takes over if it is near enough to call.
                bool next = i + 1 < corners.Length && d >= corners[i + 1].s - CallOut && d > (corners[i].s + corners[i].e) / 2f;
                if (!next) return i;
            }
            return -1;
        }

        public int Corners => corners.Length;

        /// <summary>The corners for the corner shots, tightest first, as many as asked for and no more than there are.</summary>
        public List<int> Tightest(int count)
        {
            var order = new List<int>();
            for (int i = 0; i < corners.Length; i++) order.Add(i);
            order.Sort((a, b) => corners[a].radius != corners[b].radius ? corners[a].radius.CompareTo(corners[b].radius) : a.CompareTo(b));
            if (order.Count > count) order.RemoveRange(count, order.Count - count);
            return order;
        }

        /// <summary>A point lifted to at least <see cref="Clearance"/> over the ground under it.</summary>
        private Vector3 Clear(Vector3 p)
        {
            float ground = circuit.TerrainHeight(p.x, p.z);
            if (p.y < ground + Clearance) p.y = ground + Clearance;
            return p;
        }
    }
}
