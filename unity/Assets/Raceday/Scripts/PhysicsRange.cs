using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Decides which cars run full physics. Cars near the camera, and near the player's car while the
    /// player drives it, run physics, nearest first up to a limit. The rest glide along their racing
    /// lines with no physics (<see cref="AiDriver.Glide"/>). In fast forward the range shrinks: at 4x
    /// only cars close to the camera keep physics, and at 16x every AI car glides. A car the player
    /// drives always keeps its physics.
    /// </summary>
    public sealed class PhysicsRange : MonoBehaviour
    {
        /// <summary>Cars within this many metres run physics at normal speed.</summary>
        public float Range = 300f;
        /// <summary>Most cars on physics at once at normal speed.</summary>
        public int MostCars = 24;
        /// <summary>The same at 4x.</summary>
        public float FastRange = 120f;
        public int FastMostCars = 8;
        /// <summary>A car on physics keeps it until this much further out, so cars don't flick back and forth at the edge.</summary>
        private const float Leave = 1.25f;
        /// <summary>A car switches at most this often, seconds of race time.</summary>
        private const float Settle = 1f;
        /// <summary>
        /// Gliding cars nearer the camera than this look around and move on every 0.05 s (0.1 s at 16x),
        /// so they are drawn smoothly and race closely. The rest, out of sight, do it every 0.2 s. It
        /// reaches past what the camera shows, so a car has settled into the finer step before it is seen.
        /// </summary>
        private const float Watched = 600f;

        public AiDriver[] Drivers;
        /// <summary>The player's car.</summary>
        public CarController Player;

        private float[] lastSwitch;
        private readonly List<int> order = new List<int>();
        private float[] distance;

        private void Update()
        {
            if (Drivers == null) return;
            if (lastSwitch == null || lastSwitch.Length != Drivers.Length)
            {
                lastSwitch = new float[Drivers.Length];
                distance = new float[Drivers.Length];
                for (int i = 0; i < lastSwitch.Length; i++) lastSwitch[i] = float.NegativeInfinity;
            }
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            bool playerDriving = Player != null && !Player.AiDriving;
            Vector3 player = Player != null ? Player.transform.position : Vector3.zero;

            float speed = Time.timeScale;
            float range = speed <= 1.5f ? Range : speed <= 6f ? FastRange : 0f;
            int most = speed <= 1.5f ? MostCars : speed <= 6f ? FastMostCars : 0;

            order.Clear();
            for (int i = 0; i < Drivers.Length; i++)
            {
                AiDriver d = Drivers[i];
                if (d == null || d.Car == null || !d.gameObject.activeInHierarchy) continue;   // a retired car is off the track
                Vector3 p = d.Car.transform.position;
                float near = (p - eye).magnitude;
                if (playerDriving) near = Mathf.Min(near, (p - player).magnitude);
                distance[i] = near;
                d.GlideStep = (p - eye).sqrMagnitude > Watched * Watched ? 0.2f : speed > 6f ? 0.1f : 0.05f;
                order.Add(i);
            }
            order.Sort((a, b) => distance[a].CompareTo(distance[b]));

            int onPhysics = 0;
            foreach (int i in order)
            {
                AiDriver d = Drivers[i];
                bool human = !d.enabled;   // the player has the car, or it is out and stopped: it needs its physics
                float reach = d.Gliding ? range : range * Leave;
                bool want = human || (onPhysics < most && distance[i] < reach);
                if (want) onPhysics++;
                bool settled = Time.time - lastSwitch[i] > Settle;
                if (want && d.Gliding && (settled || human))
                {
                    d.Wake();
                    lastSwitch[i] = Time.time;
                }
                else if (!want && d.CanGlide && settled)
                {
                    d.Glide();
                    lastSwitch[i] = Time.time;
                }
            }
        }
    }
}
