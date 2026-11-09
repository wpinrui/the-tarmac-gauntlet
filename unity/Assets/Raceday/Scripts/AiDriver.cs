using System.Threading.Tasks;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Puts an <see cref="AiRacer"/> in a physics car. Each physics step it hands the racer the car's
    /// position and motion and passes the pedals and steering back. After a hard crash, or when the
    /// car is stuck, it puts the car back on track as a player would with R. Lap times go to the console.
    /// Changing the stats in the inspector during Play takes effect straight away.
    /// It starts on the minimum-curvature line and switches to the line tuned for this car's lap time
    /// once that is worked out in the background, a couple of seconds in.
    /// With a <see cref="RaceCar"/> on the car, <see cref="Racecraft"/> races the cars around it first.
    /// With <see cref="Pits"/> set, the car pits on its own when its fuel or tyres run low.
    /// </summary>
    [DefaultExecutionOrder(-10)]   // before the car's own physics step
    [RequireComponent(typeof(CarController))]
    public sealed class AiDriver : MonoBehaviour
    {
        private const float CrashWait = 2f;
        private const float StuckTime = 4f;

        [Range(0, 100)] public float Pace = 80f;
        [Range(0, 100)] public float Consistency = 80f;
        [Range(0, 100)] public float Safety = 80f;
        public int Seed = 1;

        /// <summary>The minimum-curvature line, shared by every car.</summary>
        public RacingLine Line;
        /// <summary>The pit lane and this car's garage in it, for pit stops.</summary>
        public PitLane Pits;
        public int Garage = -1;
        /// <summary>The rolling start this car takes part in, if any.</summary>
        public RaceStart Starter;

        public AiPilot Pilot => Racer?.Pilot;
        public Racecraft Craft => Racer?.Craft;
        public AiRacer Racer { get; private set; }
        public bool Pitting => Racer != null && Racer.Pitting;

        private CarController car;
        private float stuckTimer, crashTimer;
        private int lastIndex = -1;
        private float lapStart = -1f;
        private int lap;
        private RaceCar race;
        private Task<AiPilot> tuning;

        private void Awake() => car = GetComponent<CarController>();

        private void Begin()
        {
            race = GetComponent<RaceCar>();
            if (Line == null || car.Spec == null) return;
            AiPilot pilot = NewPilot(Line);
            PitPlan pit = Pits != null && Garage >= 0 && car.Supplies != null ? new PitPlan(Pits, Garage, car.Supplies) : null;
            Racer = new AiRacer(pilot, new Racecraft(Seed), car.Supplies, pit, Seed);
            Log(pilot);
            // The tuned line and the pilot's lanes beside it take a while: both are made off the main thread.
            Circuit circuit = Line.Circuit;
            CarSpec spec = car.Spec;
            float pace = Pace, consistency = Consistency, safety = Safety;
            int seed = Seed;
            tuning = Task.Run(() => new AiPilot(new RacingLine(circuit, spec), spec, pace, consistency, safety, seed));
        }

        private void OnEnable()
        {
            if (car != null) car.AiDriving = true;
            Racer?.Lost();
            lastIndex = -1;
            lapStart = -1f;
        }

        private void OnDisable()
        {
            if (car != null)
            {
                car.SetControls(0f, 0f, 0f);
                car.AiDriving = false;
                car.Hold = false;
            }
        }

        private void OnValidate()
        {
            if (Racer != null && (Pilot.Pace != Pace || Pilot.Consistency != Consistency || Pilot.Safety != Safety)) Use(NewPilot(Pilot.Line));
        }

        private AiPilot NewPilot(RacingLine line) => new AiPilot(line, car.Spec, Pace, Consistency, Safety, Seed);

        private void Use(AiPilot pilot)
        {
            Racer.UsePilot(pilot);
            Line = pilot.Line;
            Log(pilot);
        }

        private void Log(AiPilot pilot)
        {
            string which = pilot.Line.TunedLapTime > 0f ? "tuned line" : "minimum-curvature line";
            Debug.Log($"{car.Spec.Name}: AI predicted lap {FormatTime(pilot.Profile.LapTime)} at Pace {Pace:0} on the {which}");
        }

        private void FixedUpdate()
        {
            if (Racer == null) Begin();
            if (Racer == null) return;
            car.AiDriving = true;
            if (tuning != null && tuning.IsCompleted)
            {
                if (tuning.Status == TaskStatus.RanToCompletion)
                {
                    AiPilot tuned = tuning.Result;
                    // Stats changed in the inspector meanwhile: keep the line, take the new stats.
                    if (tuned.Pace == Pace && tuned.Consistency == Consistency && tuned.Safety == Safety) Use(tuned);
                    else Use(NewPilot(tuned.Line));
                }
                else Debug.LogException(tuning.Exception);
                tuning = null;
            }
            float dt = Time.fixedDeltaTime;
            Transform t = car.transform;

            if (car.Crashed)
            {
                crashTimer += dt;
                if (crashTimer > CrashWait) Recover();
                return;
            }
            crashTimer = 0f;

            if (Starter != null) Starter.Brief(this);
            Racer.Active.Towed = car.DragScale < 0.97f;
            TrafficCar me = race != null ? race.Traffic : new TrafficCar { Id = -1 - Seed };
            bool racing = Starter == null || Starter.Released(this);
            AiPilot.Controls c = Racer.Step(t.position, t.forward, car.Velocity, car.YawRate, me,
                RaceCar.Field, RaceCar.All, racing, car.Spec, dt);
            car.SetControls(c.Throttle, c.Brake, c.Steer);
            car.Hold = Racer.Hold;

            // Stuck: wants to go but isn't moving, or has spun and is facing the wrong way.
            AiPilot active = Racer.Active;
            bool facingBack = active.Index >= 0 && Vector3.Dot(t.forward, active.Line.Tangent[active.Index]) < -0.2f;
            bool stalled = car.Speed < 2f && c.Throttle > 0.3f;
            stuckTimer = stalled || facingBack ? stuckTimer + dt : 0f;
            if (stuckTimer > StuckTime) Recover();

            CountLaps();
        }

        private void Recover()
        {
            car.ResetToTrack();
            Racer.Lost();
            stuckTimer = crashTimer = 0f;
            lastIndex = -1;
        }

        private void CountLaps()
        {
            int k = Racer.Active.Index;
            int n = Racer.Active.Line.Count;
            // Crossing the start line: the index wraps from the end of the lap back to the start.
            if (lastIndex > n - 50 && k < 50)
            {
                if (lapStart >= 0f)
                {
                    lap++;
                    Debug.Log($"{car.Spec.Name}: lap {lap} {FormatTime(Time.time - lapStart)} (target {FormatTime(Pilot.Profile.LapTime)}, mistakes so far {Pilot.Mistakes}, pit stops {Racer.Pit?.Stops ?? 0})");
                }
                lapStart = Time.time;
            }
            lastIndex = k;
        }

        private static string FormatTime(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.000}";
    }
}
