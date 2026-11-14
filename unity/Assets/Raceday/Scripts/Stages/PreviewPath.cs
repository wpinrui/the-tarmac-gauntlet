using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Where the race preview's camera goes (<see cref="BroadcastIntro"/>), with no Unity objects so it can be checked
    /// outside Unity. The opening shot rises over the start line. The flyover runs one lap above the track in
    /// <see cref="Duration"/> at one even pace, <see cref="Speed"/>, slow corners included, looking down the road ahead. The track is smoothed under it, so
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
        /// <summary>The flyover's speed along the track, m/s: one even pace for the whole lap, slow corners included.</summary>
        private const float Speed = 220f;
        /// <summary>The flyover's heading is measured over this far either side of a point, m.</summary>
        private const float Bend = 20f;
        /// <summary>A corner is called out this far before it starts, m.</summary>
        public const float CallOut = 250f;
        /// <summary>A corner's call stays up this far past its end, m.</summary>
        public const float CallOff = 160f;

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
            clock = new float[n + 1];
            for (int k = 0; k < n; k++) clock[k + 1] = clock[k] + step / Speed;
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

        /// <summary>How fast the camera's heading eases towards where a shot looks, 1/s.</summary>
        public const float Aim = 3f;
        /// <summary>The fastest the camera's heading turns, degrees a second: through a hairpin it pans round rather than whips.</summary>
        public const float TurnRate = 90f;

        /// <summary>
        /// The camera's heading a frame on: eased from <paramref name="from"/> towards <paramref name="to"/>, both unit vectors,
        /// turning round no faster than <see cref="TurnRate"/>. The flyover keeps its pace; only the look lags.
        /// </summary>
        public static Vector3 Turn(Vector3 from, Vector3 to, float dt)
        {
            Vector3 eased = Vector3.Lerp(from, to, 1f - Mathf.Exp(-Aim * dt)).normalized;
            float fromYaw = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg, yaw = Mathf.Atan2(eased.x, eased.z) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(fromYaw, yaw), most = TurnRate * dt;
            if (eased.sqrMagnitude < 1e-6f) eased = from;
            if (Mathf.Abs(turn) <= most) return eased;
            // Too fast: turn the most allowed, keeping the eased pitch.
            float y = (fromYaw + Mathf.Sign(turn) * most) * Mathf.Deg2Rad;
            float flat = Mathf.Sqrt(Mathf.Max(0f, 1f - eased.y * eased.y));
            return new Vector3(Mathf.Sin(y) * flat, eased.y, Mathf.Cos(y) * flat);
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
