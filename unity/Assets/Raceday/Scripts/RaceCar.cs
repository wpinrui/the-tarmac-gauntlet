using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car in the race as the others see it: how far it has gone, where it sits across the track
    /// and how fast it is going. Every car on track has one, the player's too, so AI drivers race
    /// all of them. Once per physics step the cars are brought up to date together, into
    /// <see cref="Field"/>, and each gets the slipstream of the cars ahead of it.
    /// It also switches the pit lane limiter on and off, and serves the player in their box: stop
    /// in it and the crew fills the tank and fits new tyres.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before the AI drivers think
    [RequireComponent(typeof(CarController))]
    public sealed class RaceCar : MonoBehaviour
    {
        private const float G = 9.81f;

        /// <summary>Every car on the track, as of this physics step: cars in the pit lane are left out.</summary>
        public static readonly List<TrafficCar> Field = new List<TrafficCar>();
        /// <summary>Where every car is, the pit lane included, as of this physics step.</summary>
        public static readonly List<CarState> All = new List<CarState>();
        private static readonly List<RaceCar> cars = new List<RaceCar>();
        private static float updated = -1f;
        private static int nextId;

        public TrafficCar Traffic { get; private set; }
        public int Laps { get; private set; }
        public int Id => id;
        /// <summary>True while the car is off the track in the pit lane.</summary>
        public bool InPitLane { get; private set; }
        /// <summary>The pit lane, and this car's garage in it, or -1 for none.</summary>
        public PitLane Pits;
        public int Garage = -1;
        /// <summary>The player's stop in progress, or null.</summary>
        public PitService Service { get; private set; }

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
            All.Clear();
            foreach (RaceCar r in cars) r.Measure();
            foreach (RaceCar r in cars)
            {
                All.Add(new CarState { Id = r.id, Position = r.transform.position, Velocity = r.car.Velocity });
                if (!r.InPitLane) Field.Add(r.Traffic);
            }
            foreach (RaceCar r in cars) r.ServePlayer();
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
            float lateral = d.x * c.Left[k].x + d.z * c.Left[k].z;
            InPitLane = Pits != null && Pits.OffTrackInLane(k, lateral);
            car.SpeedLimit = Pits != null && Pits.InLimitZone(p, k) ? Pits.LimitSpeed : float.PositiveInfinity;
            Traffic = new TrafficCar
            {
                Id = id,
                Distance = Laps * c.Lap + c.DistanceAt(k, p),
                Lateral = lateral,
                Speed = v.x * f.x + v.z * f.z,
                LapPace = pilot != null ? pilot.Profile.LapTime : 0f,
                Braking = car.Spec.BrakeDecel * G * AiPilot.LimitFor(pilot != null ? pilot.Pace : 100f),
                GivingWay = driver != null && driver.enabled && driver.Craft != null && !driver.Pitting && driver.Craft.State == Racecraft.Mode.GivingWay,
                // A player's car has no plan: the AI expects it to carry on as it is going.
                Craft = driver != null && driver.enabled ? driver.Craft : null,
            };
        }

        /// <summary>Takes one lap off every car's count, at the green: the formation lap doesn't count.</summary>
        public static void StartRace()
        {
            foreach (RaceCar r in cars) r.Laps--;
        }

        /// <summary>
        /// A player who stops in their own box is served: held on the brakes while the crew refuels
        /// and changes the tyres, then let go. AI cars are served by their own driver.
        /// </summary>
        private void ServePlayer()
        {
            if (car.AiDriving || Pits == null || Garage < 0 || car.Supplies == null)
            {
                if (Service != null && car.AiDriving) { Service = null; car.Hold = false; }
                return;
            }
            if (Service == null)
            {
                Vector3 box = Pits.BoxPoint(Garage) - transform.position;
                box.y = 0f;
                if (box.sqrMagnitude < 2.5f * 2.5f && car.Speed < 0.5f) Service = new PitService(car.Supplies, car.Spec);
                else return;
            }
            Service.Step(Time.fixedDeltaTime);
            car.Hold = !Service.Done;
            if (Service.Done)
            {
                // Stay put until the car has left the box, so the stop isn't started again.
                Vector3 box = Pits.BoxPoint(Garage) - transform.position;
                box.y = 0f;
                if (box.sqrMagnitude > 6f * 6f) Service = null;
            }
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
