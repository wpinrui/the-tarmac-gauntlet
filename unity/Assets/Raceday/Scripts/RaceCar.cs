using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car in the race as the others see it: how far it has gone, where it sits across the track
    /// and how fast it is going. Every car on track has one, the player's too, so AI drivers race
    /// all of them. Once per physics step the cars are brought up to date together, into
    /// <see cref="Field"/>, and each car on physics gets the slipstream of the cars ahead of it.
    /// A gliding car is read from its pilot, which knows exactly where it is on its path.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before the AI drivers think
    [RequireComponent(typeof(CarController))]
    public sealed class RaceCar : MonoBehaviour
    {
        private const float G = 9.81f;

        /// <summary>Every car, as of this physics step, in lap order.</summary>
        public static readonly TrafficField Field = new TrafficField();
        private static readonly List<TrafficCar> all = new List<TrafficCar>();
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
            all.Clear();
            foreach (RaceCar r in cars) r.Measure();
            foreach (RaceCar r in cars) all.Add(r.Traffic);
            if (cars.Count > 0) Field.Set(all, cars[0].car.Circuit.Lap);
            foreach (RaceCar r in cars)
                if (!r.car.Asleep) r.car.DragScale = Racecraft.Slipstream(r.car.Circuit, r.Traffic, all);
        }

        /// <summary>Finds the car on the lap, counts the laps, and fills in <see cref="Traffic"/>.</summary>
        private void Measure()
        {
            Circuit c = car.Circuit;
            if (driver == null) driver = GetComponent<AiDriver>();
            AiPilot pilot = driver != null ? driver.Pilot : null;
            if (pilot != null && pilot.Gliding)
            {
                int g = pilot.Index;
                Count(c, g);
                Traffic = Describe(pilot, Laps * c.Lap + pilot.GlideDistance, pilot.GlideLateral, pilot.GlideSpeed);
                return;
            }
            Vector3 p = transform.position;
            int k = Nearest(c, p);
            Count(c, k);

            Vector3 d = p - c.Centre[k];
            Vector3 f = c.Forward[k];
            Vector3 v = car.Velocity;
            // Between samples too, so gaps don't jump in 2 m steps.
            Traffic = Describe(pilot, Laps * c.Lap + k * c.Step + d.x * f.x + d.z * f.z,
                d.x * c.Left[k].x + d.z * c.Left[k].z, v.x * f.x + v.z * f.z);
        }

        /// <summary>Counts the laps as the car moves to sample k.</summary>
        private void Count(Circuit c, int k)
        {
            if (index < 0) Laps = k * c.Step > c.Lap * 0.5f ? -1 : 0;   // on the grid, behind the line
            else if (index > c.Count - 50 && k < 50) Laps++;
            else if (index < 50 && k > c.Count - 50) Laps--;
            index = k;
        }

        private TrafficCar Describe(AiPilot pilot, float distance, float lateral, float speed) => new TrafficCar
        {
            Id = id,
            Distance = distance,
            Lateral = lateral,
            Speed = speed,
            LapPace = pilot != null ? pilot.Profile.LapTime : 0f,
            Braking = car.Spec.BrakeDecel * G * AiPilot.LimitFor(pilot != null ? pilot.Pace : 100f),
            GivingWay = driver != null && driver.enabled && driver.Craft != null && driver.Craft.State == Racecraft.Mode.GivingWay,
        };

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
