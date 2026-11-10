using System.Threading.Tasks;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Puts an <see cref="AiRacer"/> in a physics car. Each physics step it hands the racer the car's
    /// position and motion and passes the pedals and steering back. After leaving the track, spinning
    /// or a hard crash, <see cref="Rejoin"/> brings the car back into the race. Only if that hasn't
    /// worked after <see cref="Rejoin.GiveUpAfter"/>, or the car is stuck or <see cref="Rejoin.Stranded"/>
    /// (it can't move either way, as a small car can't in deep gravel), is it put back on track as a
    /// player would with R, once the gap behind is clear. Lap times go to the console.
    /// Changing the stats in the inspector during Play takes effect straight away.
    /// It starts on the minimum-curvature line and switches to the line tuned for this car's lap time
    /// once <see cref="LineTuner"/> has worked it out in the background.
    /// With a <see cref="RaceCar"/> on the car, <see cref="Racecraft"/> races the cars around it first.
    /// With <see cref="Pits"/> set, the car pits on its own when its fuel or tyres run low.
    ///
    /// Far from the camera the car glides instead (<see cref="Glide"/>): no physics, it moves along its
    /// path at its driver's pace, still raced by racecraft, and <see cref="PhysicsRange"/> wakes it
    /// back into physics as the camera or the player's car comes near, or when it is due to pit.
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
        /// <summary>The start this car takes part in, if any.</summary>
        public Starter Starter;

        public AiPilot Pilot => Racer?.Pilot;
        public Racecraft Craft => Racer?.Craft;
        public AiRacer Racer { get; private set; }
        public bool Pitting => Racer != null && Racer.Pitting;
        public CarController Car => car;
        /// <summary>True while the car glides without physics.</summary>
        public bool Gliding => Racer != null && Racer.Active.Gliding;
        /// <summary>How often a gliding car looks around and moves on, seconds: finer near the camera.</summary>
        [System.NonSerialized] public float GlideStep = 0.05f;

        private float glideClock;

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
            Racer = new AiRacer(pilot, new Racecraft(), car.Supplies, pit, Seed);
            Log(pilot);
            // The tuned line and the pilot's lanes beside it take a while: both are made off the main thread.
            Circuit circuit = Line.Circuit;
            CarSpec spec = car.Spec;
            float pace = Pace, consistency = Consistency, safety = Safety;
            int seed = Seed;
            // The line is queued first, so it is ready by the time the pilot is built on the same thread.
            Task<RacingLine> line = LineTuner.Line(circuit, spec);
            tuning = LineTuner.Run(() => new AiPilot(line.Result, spec, pace, consistency, safety, seed));
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
            if (Gliding) Wake();   // someone else drives it now, so it needs its physics
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

            if (Gliding)
            {
                glideClock += dt;
                if (glideClock < GlideStep - 1e-5f) return;
                if (Starter != null) Starter.Brief(this);
                TrafficCar self = race != null ? race.Traffic : new TrafficCar { Id = -1 - Seed };
                bool released = Starter == null || Starter.Released(this);
                bool gliding = Racer.Glide(self, RaceCar.Field, released, car.Spec, glideClock);
                glideClock = 0f;
                if (!gliding) Wake();   // due to pit: the stop is driven in physics
                else car.Coast(Racer.Active.GlideSpeed, Racer.Active.GlideSteer);
                CountLaps();
                return;
            }

            // On its roof or side for a moment: back on its wheels where it is, to rejoin from there.
            if (Racer.Rejoin.Upturned(t.up.y, dt)) car.Upright();

            if (car.Crashed)
            {
                // After a moment the car drives on from where it stopped, as one that lost control.
                crashTimer += dt;
                if (crashTimer > CrashWait)
                {
                    car.ClearCrash();
                    Racer.Rejoin.LostControl();
                }
                return;
            }
            crashTimer = 0f;

            if (Starter != null) Starter.Brief(this);
            Racer.Active.Towed = car.DragScale < 0.97f;
            TrafficCar me = race != null ? race.Traffic : new TrafficCar { Id = -1 - Seed };
            bool racing = Starter == null || Starter.Released(this);
            AiPilot.Controls c = Racer.Step(t.position, t.forward, car.Velocity, car.YawRate, me,
                RaceCar.Field, RaceCar.All, racing, car.Spec, dt);
            car.SetControls(c.Throttle, c.Brake, c.Steer, c.Reverse);
            car.Hold = Racer.Hold;

            // Last resort: stuck wanting to go but not moving, stranded, or not back racing after a long while.
            // The car is put back on the track only once no car is coming up behind.
            bool rejoining = Racer.Rejoin.Busy;
            bool stalled = !rejoining && car.Speed < 2f && c.Throttle > 0.3f;
            if (stalled || Racer.Rejoin.Trying) stuckTimer += dt;
            else if (!rejoining) stuckTimer = 0f;
            if ((stuckTimer > (rejoining ? Rejoin.GiveUpAfter : StuckTime) || Racer.Rejoin.Stranded) && Racer.Rejoin.GapClear) Recover();

            CountLaps();
        }

        /// <summary>Draws a gliding car where it is now, between its steps.</summary>
        private void Update()
        {
            if (!Gliding) return;
            AiPilot p = Racer.Active;
            float since = glideClock + Mathf.Max(Time.time - Time.fixedTime, 0f);
            p.GlidePose(p.GlideSpeed * since, out Vector3 at, out Vector3 forward, out Vector3 up);
            car.transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward, up));
        }

        /// <summary>
        /// Whether the car can leave physics now: driven by the AI, racing or in the formation, and
        /// not making a pit stop or waiting to be put back after a crash.
        /// </summary>
        public bool CanGlide => enabled && Racer != null && Racer.CanGlide && !Gliding && !car.Crashed && !car.Asleep;

        /// <summary>Takes the car off physics. It glides on from where it is, at its speed along the track.</summary>
        public void Glide()
        {
            if (!CanGlide) return;
            AiPilot p = Racer.Active;
            Transform t = car.transform;
            int k = p.Line.Nearest(t.position, p.Index);
            p.StartGlide(t.position, Vector3.Dot(car.Velocity, p.Line.Tangent[k]));
            car.Sleep();
            car.Coast(p.GlideSpeed, p.GlideSteer);
            // Spread the gliding cars' steps over the physics steps, so they don't all land on one.
            glideClock = (Seed % 5) * Time.fixedDeltaTime;
            stuckTimer = crashTimer = 0f;
        }

        /// <summary>Back to physics where the gliding car is, moving along its path, so it carries on unbroken.</summary>
        public void Wake()
        {
            if (!Gliding) return;
            AiPilot p = Racer.Active;
            float since = glideClock + Mathf.Max(Time.time - Time.fixedTime, 0f);
            p.GlidePose(p.GlideSpeed * since, out Vector3 at, out Vector3 forward, out Vector3 up);
            car.Wake(at, Quaternion.LookRotation(forward, up), p.GlideSpeed, p.GlideSpeed * p.GlideCurvature, p.GlideSteer);
            p.StopGlide();
            stuckTimer = crashTimer = 0f;
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
