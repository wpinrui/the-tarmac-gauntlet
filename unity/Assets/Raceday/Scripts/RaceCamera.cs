using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Chase, trackside TV and overhead views of one target car.</summary>
    public sealed class RaceCamera
    {
        public enum View { Chase, Trackside, Overhead }

        private readonly Camera camera;
        private readonly List<Vector3> spots = new List<Vector3>();
        private readonly Vector3 overheadPosition;
        private Transform lastTarget;

        public RaceCamera(Camera camera, TrackLayout track)
        {
            this.camera = camera;

            // Trackside cameras every 120 m, alternating sides of the road.
            int i = 0;
            for (float d = 0; d < track.LapLength; d += 120f, i++)
            {
                float side = (i % 2 == 0 ? 1 : -1) * (TrackLayout.RoadWidth / 2 + 18f);
                spots.Add(track.Main.Offset(d, side, out _) + Vector3.up * 6f);
            }

            Bounds b = new Bounds(track.Main.Points[0], Vector3.zero);
            foreach (Vector3 p in track.Main.Points) b.Encapsulate(p);
            float half = Mathf.Tan(30f * Mathf.Deg2Rad);
            float height = Mathf.Max(b.size.z, b.size.x / Mathf.Max(0.5f, camera.aspect)) * 0.6f / half;
            overheadPosition = b.center + Vector3.up * height;
        }

        public View Mode { get; private set; } = View.Chase;

        public void NextView() => Mode = (View)(((int)Mode + 1) % 3);

        public void Update(Transform target, float dt)
        {
            if (target == null) return;
            bool cut = target != lastTarget;
            lastTarget = target;
            Vector3 pos = target.position;
            Vector3 fwd = target.forward;

            switch (Mode)
            {
                case View.Chase:
                {
                    Vector3 desired = pos - fwd * 11f + Vector3.up * 4f;
                    if (cut || (camera.transform.position - desired).sqrMagnitude > 100f * 100f)
                    {
                        camera.transform.position = desired;
                    }
                    else
                    {
                        camera.transform.position = Vector3.Lerp(camera.transform.position, desired, 1 - Mathf.Exp(-dt * 6f));
                    }
                    camera.transform.rotation = Quaternion.LookRotation(pos + fwd * 6f + Vector3.up * 1f - camera.transform.position);
                    camera.fieldOfView = 60f;
                    break;
                }
                case View.Trackside:
                {
                    Vector3 spot = spots[0];
                    float best = float.MaxValue;
                    foreach (Vector3 s in spots)
                    {
                        float d = (s - pos).sqrMagnitude;
                        if (d < best) { best = d; spot = s; }
                    }
                    camera.transform.position = spot;
                    camera.transform.rotation = Quaternion.LookRotation(pos + Vector3.up * 0.8f - spot);
                    // Zoom so the car stays about the same size on screen.
                    float dist = Mathf.Sqrt(best);
                    camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(12f / Mathf.Max(1f, dist)) * Mathf.Rad2Deg, 8f, 60f);
                    break;
                }
                case View.Overhead:
                    camera.transform.position = overheadPosition;
                    camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    camera.fieldOfView = 60f;
                    break;
            }
        }
    }
}
