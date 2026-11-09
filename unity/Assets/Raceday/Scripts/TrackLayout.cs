using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The circuit as polylines in world space, built from the track builder's JSON.
    /// Distances along the racing line start at the start/finish line.
    /// </summary>
    public sealed class TrackLayout
    {
        /// <summary>
        /// World metres per track metre. The sim runs ~30 s laps on a 9.2 km circuit, so the
        /// circuit is drawn smaller to keep speeds believable next to real-size cars.
        /// </summary>
        public const float WorldScale = 0.2f;
        public const float RoadWidth = 14f;
        public const float PitLaneWidth = 8f;

        private const float SampleSpacing = 2f; // world metres

        public Polyline Main { get; private set; }
        public Polyline PitLane { get; private set; }
        /// <summary>Main-line distance where the pit lane leaves the track.</summary>
        public float PitEntry { get; private set; }
        /// <summary>Main-line distance where the pit lane rejoins the track.</summary>
        public float PitExit { get; private set; }
        /// <summary>Pit-lane distance of the pit box, level with the start/finish line.</summary>
        public float PitBox { get; private set; }
        /// <summary>+1 when the pit lane is to the right of the racing line, -1 when left.</summary>
        public float PitSide { get; private set; } = 1f;

        public float LapLength => Main.Length;

        public static TrackLayout Load(string json)
        {
            TrackJson data = JsonUtility.FromJson<TrackJson>(json);
            var layout = new TrackLayout();

            List<Vector3> main = Sample(data.track.curves);
            // A closed loop ends where it starts; drop the repeat.
            if (main.Count > 2 && (main[main.Count - 1] - main[0]).sqrMagnitude < 0.01f) main.RemoveAt(main.Count - 1);
            Vector3 sf = ToWorld(data.startFinish.position);
            int sfIndex = Nearest(main, sf);
            // Rotate so index 0 is the start/finish line.
            var rotated = new List<Vector3>(main.Count);
            for (int i = 0; i < main.Count; i++) rotated.Add(main[(sfIndex + i) % main.Count]);
            layout.Main = new Polyline(rotated, true);

            if (data.pitLane != null && data.pitLane.curves != null && data.pitLane.curves.Length > 0)
            {
                layout.PitLane = new Polyline(Sample(data.pitLane.curves), false);
                layout.PitEntry = layout.Main.DistanceOf(layout.PitLane.Points[0]);
                layout.PitExit = layout.Main.DistanceOf(layout.PitLane.Points[layout.PitLane.Points.Count - 1]);
                layout.PitBox = layout.PitLane.DistanceOf(layout.Main.Points[0]);
                layout.Main.Sample(0, out Vector3 sfPos, out Vector3 sfFwd);
                Vector3 right = Vector3.Cross(Vector3.up, sfFwd);
                layout.PitSide = Vector3.Dot(layout.PitLane.PointAt(layout.PitBox) - sfPos, right) >= 0 ? 1f : -1f;
            }
            return layout;
        }

        private static Vector3 ToWorld(PointJson p) =>
            // Builder y grows downwards on screen; flip it so the map reads the same from above.
            new Vector3(p.x * WorldScale, 0f, -p.y * WorldScale);

        private static List<Vector3> Sample(CurveJson[] curves)
        {
            var points = new List<Vector3>();
            foreach (CurveJson c in curves)
            {
                Vector3 a = ToWorld(c.p0), b = ToWorld(c.cp1), d = ToWorld(c.cp2), e = ToWorld(c.p3);
                float approx = Vector3.Distance(a, b) + Vector3.Distance(b, d) + Vector3.Distance(d, e);
                int steps = Mathf.Max(2, Mathf.CeilToInt(approx / SampleSpacing));
                for (int i = 0; i < steps; i++)
                {
                    float t = i / (float)steps;
                    float u = 1 - t;
                    points.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * d + t * t * t * e);
                }
            }
            if (curves.Length > 0) points.Add(ToWorld(curves[curves.Length - 1].p3));
            // Drop duplicates where curves meet.
            var clean = new List<Vector3> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if ((points[i] - clean[clean.Count - 1]).sqrMagnitude > 0.01f) clean.Add(points[i]);
            }
            return clean;
        }

        private static int Nearest(List<Vector3> points, Vector3 p)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = (points[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // --- JSON shape (track builder export) ---

#pragma warning disable 0649 // Filled in by JsonUtility.

        [Serializable] private sealed class PointJson { public float x; public float y; }
        [Serializable] private sealed class CurveJson { public PointJson p0; public PointJson cp1; public PointJson cp2; public PointJson p3; }
        [Serializable] private sealed class PathJson { public CurveJson[] curves; public bool closed; public float lengthMetres; }
        [Serializable] private sealed class StartFinishJson { public int segmentIndex; public float t; public PointJson position; }
        [Serializable] private sealed class TrackJson { public PathJson track; public PathJson pitLane; public StartFinishJson startFinish; }
#pragma warning restore 0649
    }

    /// <summary>A polyline with arc-length lookup.</summary>
    public sealed class Polyline
    {
        private readonly float[] cumulative;

        public Polyline(List<Vector3> points, bool closed)
        {
            Closed = closed;
            Points = points;
            int segments = closed ? points.Count : points.Count - 1;
            cumulative = new float[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % points.Count]);
            }
            Length = cumulative[segments];
        }

        public IReadOnlyList<Vector3> Points { get; }
        public bool Closed { get; }
        public float Length { get; }

        /// <summary>Position and unit direction at a distance along the line.</summary>
        public void Sample(float distance, out Vector3 position, out Vector3 forward)
        {
            if (Closed) distance = Mathf.Repeat(distance, Length);
            else distance = Mathf.Clamp(distance, 0, Length);

            int lo = 0, hi = cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= distance) lo = mid; else hi = mid;
            }
            Vector3 a = Points[lo];
            Vector3 b = Points[(lo + 1) % Points.Count];
            float seg = cumulative[lo + 1] - cumulative[lo];
            float t = seg > 0 ? (distance - cumulative[lo]) / seg : 0;
            position = Vector3.Lerp(a, b, t);

            // Smooth the heading over a few metres so cars don't snap at sample joints.
            Vector3 ahead = PointAt(distance + 3f);
            Vector3 behind = PointAt(distance - 3f);
            forward = (ahead - behind).sqrMagnitude > 1e-6f ? (ahead - behind).normalized : (b - a).normalized;
        }

        public Vector3 PointAt(float distance)
        {
            if (Closed) distance = Mathf.Repeat(distance, Length);
            else distance = Mathf.Clamp(distance, 0, Length);
            int lo = 0, hi = cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= distance) lo = mid; else hi = mid;
            }
            float seg = cumulative[lo + 1] - cumulative[lo];
            float t = seg > 0 ? (distance - cumulative[lo]) / seg : 0;
            return Vector3.Lerp(Points[lo], Points[(lo + 1) % Points.Count], t);
        }

        /// <summary>Position offset sideways; positive is to the driver's right.</summary>
        public Vector3 Offset(float distance, float lateral, out Vector3 forward)
        {
            Sample(distance, out Vector3 p, out forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return p + right * lateral;
        }

        /// <summary>Distance along the line of the point nearest to p.</summary>
        public float DistanceOf(Vector3 p)
        {
            int segments = cumulative.Length - 1;
            float best = 0, bestD = float.MaxValue;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Points[i], b = Points[(i + 1) % Points.Count];
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = cumulative[i] + t * (cumulative[i + 1] - cumulative[i]); }
            }
            return best;
        }

        /// <summary>Signed curvature (1/radius) around a distance, measured over a span.</summary>
        public float Curvature(float distance, float span = 8f)
        {
            Vector3 a = PointAt(distance - span), b = PointAt(distance), c = PointAt(distance + span);
            Vector3 ab = b - a, bc = c - b;
            float cross = ab.x * bc.z - ab.z * bc.x;
            float denom = ab.magnitude * bc.magnitude * (c - a).magnitude;
            return denom > 1e-6f ? 2f * cross / denom : 0f;
        }
    }
}
