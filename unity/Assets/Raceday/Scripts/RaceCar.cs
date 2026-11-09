using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car in the race as the others see it: how far it has gone, where it sits across the track
    /// and how fast it is going. Every car on track has one, the player's too, so AI drivers race
    /// all of them. Once per physics step the cars are brought up to date together, into
    /// <see cref="Field"/>, and each gets the slipstream of the cars ahead of it.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before the AI drivers think
    [RequireComponent(typeof(CarController))]
    public sealed class RaceCar : MonoBehaviour
    {
        private const float G = 9.81f;

        /// <summary>Every car, as of this physics step.</summary>
        public static readonly List<TrafficCar> Field = new List<TrafficCar>();
        private static readonly List<RaceCar> cars = new List<RaceCar>();
        private static float updated = -1f;
        private static int nextId;

        public TrafficCar Traffic { get; private set; }
        public int Laps { get; private set; }

        private CarController car;
        private AiDriver driver;
        private int id;
        private int index = -1;

        private void Awake()
        {
            car = GetComponent<CarController>();
            driver = GetComponent<AiDriver>();
            id = nextId++;
        }

        private void OnEnable() => cars.Add(this);

        private void OnDisable()
        {
            cars.Remove(this);
            updated = -1f;
        }

        private void FixedUpdate()
        {
            if (updated == Time.fixedTime) return;
            updated = Time.fixedTime;
            Field.Clear();
            foreach (RaceCar r in cars) r.Measure();
            foreach (RaceCar r in cars) Field.Add(r.Traffic);
            foreach (RaceCar r in cars) r.car.DragScale = Racecraft.Slipstream(r.car.Circuit, r.Traffic, Field);
        }

        /// <summary>Finds the car on the lap, counts the laps, and fills in <see cref="Traffic"/>.</summary>
        private void Measure()
        {
            Circuit c = car.Circuit;
            if (driver == null) driver = GetComponent<AiDriver>();
            Vector3 p = transform.position;
            int k = Nearest(c, p);
            if (index < 0) Laps = k * c.Step > c.Lap * 0.5f ? -1 : 0;   // on the grid, behind the line
            else if (index > c.Count - 50 && k < 50) Laps++;
            else if (index < 50 && k > c.Count - 50) Laps--;
            index = k;

            Vector3 d = p - c.Centre[k];
            Vector3 f = c.Forward[k];
            Vector3 v = car.Velocity;
            AiPilot pilot = driver != null ? driver.Pilot : null;
            Traffic = new TrafficCar
            {
                Id = id,
                Distance = Laps * c.Lap + k * c.Step,
                Lateral = d.x * c.Left[k].x + d.z * c.Left[k].z,
                Speed = v.x * f.x + v.z * f.z,
                LapPace = pilot != null ? pilot.Profile.LapTime : 0f,
                Braking = car.Spec.BrakeDecel * G * AiPilot.LimitFor(pilot != null ? pilot.Pace : 100f),
                GivingWay = driver != null && driver.enabled && driver.Craft != null && driver.Craft.State == Racecraft.Mode.GivingWay,
            };
        }

        /// <summary>The centre line sample nearest the car, searching near the last one, or the whole lap after a reset.</summary>
        private int Nearest(Circuit c, Vector3 p)
        {
            bool all = index < 0 || (p - c.Centre[index]).sqrMagnitude > 900f;
            int from = all ? 0 : index - 10, to = all ? c.Count : index + 40;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = from; i < to; i++)
            {
                int k = c.Wrap(i);
                Vector3 d = c.Centre[k] - p;
                d.y *= 0.25f;
                if (d.sqrMagnitude < bestDistance) { bestDistance = d.sqrMagnitude; best = k; }
            }
            return best;
        }
    }
}
