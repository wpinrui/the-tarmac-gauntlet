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
    /// It also switches the pit lane limiter on and off, and serves the player in their box: stop
    /// in it and the crew fills the tank and fits new tyres. A retired car that stops in its box stays there.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before the AI drivers think
    [RequireComponent(typeof(CarController))]
    public sealed class RaceCar : MonoBehaviour
    {
        private const float G = 9.81f;

        /// <summary>Every car on the track, as of this physics step, in lap order: cars in the pit lane are left out.</summary>
        public static readonly TrafficField Field = new TrafficField();
        private static readonly List<TrafficCar> onTrack = new List<TrafficCar>();
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
        /// <summary>The lap count the car starts on, instead of guessing it from the grid: a set-up's lapped car.</summary>
        [System.NonSerialized] public int? FirstLap;
        /// <summary>The player's stop in progress, or null.</summary>
        public PitService Service { get; private set; }

        private CarController car;
        private AiDriver driver;
        private int id;
        private int index = -1;
        private Vector3 velocity;
        private Collider body;
        /// <summary>Whether this car's collisions with the others are switched off as a ghost.</summary>
        private bool ghosted;

        private void Awake()
        {
            car = GetComponent<CarController>();
            driver = GetComponent<AiDriver>();
            body = GetComponent<Collider>();
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
            onTrack.Clear();
            All.Clear();
            foreach (RaceCar r in cars) r.Measure();
            foreach (RaceCar r in cars)
            {
                All.Add(new CarState { Id = r.id, Position = r.transform.position, Velocity = r.velocity });
                if (!r.InPitLane) onTrack.Add(r.Traffic);
            }
            if (cars.Count > 0) Field.Set(onTrack, cars[0].car.Circuit.Lap);
            foreach (RaceCar r in cars) r.Haunt();
            foreach (RaceCar r in cars) r.ServePlayer();
            foreach (RaceCar r in cars)
                if (!r.car.Asleep) r.car.DragScale = Racecraft.Slipstream(r.car.Circuit, r.Traffic, onTrack);
        }

        /// <summary>Finds the car on the lap, counts the laps, and fills in <see cref="Traffic"/>.</summary>
        private void Measure()
        {
            Circuit c = car.Circuit;
            if (driver == null) driver = GetComponent<AiDriver>();
            AiPilot pilot = driver != null ? driver.Pilot : null;
            if (driver != null && driver.Gliding)
            {
                // Gliding: racing, or in a start, which may be in the pit lane. Never making a pit stop.
                AiPilot active = driver.Racer.Active;
                Count(c, active.Index);
                InPitLane = Pits != null && Pits.OffTrackInLane(active.Index, active.GlideLateral);
                car.SpeedLimit = float.PositiveInfinity;
                velocity = active.Line.Tangent[active.Index] * active.GlideSpeed;
                active.GlidePose(0f, out _, out Vector3 heading, out _);
                Traffic = Describe(pilot, Laps * c.Lap + active.GlideDistance, active.GlideLateral, active.GlideSpeed, 0f, Mathf.Atan2(heading.z, heading.x));
                return;
            }
            Vector3 p = transform.position;
            int k = Nearest(c, p);
            Count(c, k);

            Vector3 d = p - c.Centre[k];
            Vector3 f = c.Forward[k];
            Vector3 v = car.Velocity;
            velocity = v;
            float lateral = d.x * c.Left[k].x + d.z * c.Left[k].z;
            InPitLane = Pits != null && Pits.OffTrackInLane(k, lateral);
            car.SpeedLimit = Pits != null && Pits.InLimitZone(p, k) ? Pits.LimitSpeed : float.PositiveInfinity;
            Vector3 ahead = transform.forward;
            Traffic = Describe(pilot, Laps * c.Lap + c.DistanceAt(k, p), lateral, v.x * f.x + v.z * f.z,
                v.x * c.Left[k].x + v.z * c.Left[k].z, Mathf.Atan2(ahead.z, ahead.x));
        }

        /// <summary>A ghost passes through every other car: its collisions with them are off while it is one.</summary>
        private void Haunt()
        {
            bool ghost = Traffic.Ghost;
            if (ghost == ghosted || body == null) return;
            ghosted = ghost;
            foreach (RaceCar o in cars)
                if (o != this && o.body != null) Physics.IgnoreCollision(body, o.body, ghost || o.ghosted);
        }

        /// <summary>Counts the laps as the car moves to sample k.</summary>
        private void Count(Circuit c, int k)
        {
            if (index < 0) Laps = FirstLap ?? (k * c.Step > c.Lap * 0.5f ? -1 : 0);   // on the grid, behind the line
            else if (index > c.Count - 50 && k < 50) Laps++;
            else if (index < 50 && k > c.Count - 50) Laps--;
            index = k;
        }

        private TrafficCar Describe(AiPilot pilot, float distance, float lateral, float speed, float across, float yaw) => new TrafficCar
        {
            Id = id,
            Distance = distance,
            Lateral = lateral,
            Speed = speed,
            LapPace = pilot != null ? pilot.Profile.LapTime : 0f,
            Braking = car.Spec.BrakeDecel * G * AiPilot.LimitFor(pilot != null ? pilot.Pace : 100f),
            Across = across,
            Yaw = yaw,
            Ghost = driver != null && driver.isActiveAndEnabled && driver.Racer != null && driver.Racer.Ghost,
            Profile = pilot?.Profile,
            Planned = pilot != null && car.AiDriving,
            Aim = pilot != null && car.AiDriving ? driver.Racer.Craft.Target : 0f,
            Cap = pilot != null && car.AiDriving ? pilot.SpeedCap : float.PositiveInfinity,
        };

        /// <summary>Carries on a saved race: the car is on its lap <paramref name="laps"/> from the start.</summary>
        public void Resume(int laps)
        {
            FirstLap = laps;
            Laps = laps;
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
                bool stopped = box.sqrMagnitude < 2.5f * 2.5f && car.Speed < 0.5f;
                // Race over: once in the box, the car stays there.
                if (car.Supplies.RaceOver)
                {
                    if (stopped) car.Hold = true;
                    return;
                }
                if (stopped) Service = new PitService(car.Supplies, car.Spec);
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
