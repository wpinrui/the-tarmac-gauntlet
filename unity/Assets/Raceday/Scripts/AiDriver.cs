using System.Threading.Tasks;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Puts an <see cref="AiPilot"/> in a physics car. Each physics step it hands the pilot the car's
    /// position and motion and passes the pedals and steering back. After a hard crash, or when the
    /// car is stuck, it puts the car back on track as a player would with R. Lap times go to the console.
    /// Changing the stats in the inspector during Play takes effect straight away.
    /// It starts on the minimum-curvature line and switches to the line tuned for this car's lap time
    /// once that is worked out in the background, a couple of seconds in.
    /// With a <see cref="RaceCar"/> on the car, <see cref="Racecraft"/> races the cars around it first.
    ///
    /// Far from the camera the car glides instead (<see cref="Glide"/>): no physics, it moves along its
    /// path at its driver's pace, still raced by racecraft, and <see cref="PhysicsRange"/> wakes it
    /// back into physics as the camera or the player's car comes near.
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
        public AiPilot Pilot { get; private set; }
        public Racecraft Craft { get; private set; }
        public CarController Car => car;
        /// <summary>True while the car glides without physics.</summary>
        public bool Gliding => Pilot != null && Pilot.Gliding;
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

        private void Start()
        {
            car = GetComponent<CarController>();
            race = GetComponent<RaceCar>();
            Craft = new Racecraft(Seed);
            Build();
            if (Line != null && car.Spec != null)
            {
                // The tuned line and the pilot's lanes beside it take a while: both are made off the main thread.
                Circuit circuit = Line.Circuit;
                CarSpec spec = car.Spec;
                float pace = Pace, consistency = Consistency, safety = Safety;
                int seed = Seed;
                tuning = Task.Run(() => new AiPilot(new RacingLine(circuit, spec), spec, pace, consistency, safety, seed));
            }
        }

        private void OnEnable()
        {
            if (car != null) car.AiDriving = true;
            if (Pilot != null)
            {
                Pilot.Lost();
                Pilot.Offset = 0f;
            }
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
            }
        }

        private void OnValidate()
        {
            if (Pilot != null && (Pilot.Pace != Pace || Pilot.Consistency != Consistency || Pilot.Safety != Safety)) Build();
        }

        private void Build()
        {
            if (Line == null || car == null || car.Spec == null) return;
            Use(new AiPilot(Line, car.Spec, Pace, Consistency, Safety, Seed));
        }

        private void Use(AiPilot pilot)
        {
            if (Pilot != null) pilot.Offset = Pilot.Offset;   // carry on where it was across the track
            if (Gliding)
            {
                // Keep gliding on the new line from where the car is.
                Pilot.GlidePose(0f, out Vector3 at, out _, out _);
                pilot.StartGlide(at, Pilot.GlideSpeed);
            }
            Pilot = pilot;
            Line = pilot.Line;
            car.AiDriving = enabled;
            string which = Line.TunedLapTime > 0f ? "tuned line" : "minimum-curvature line";
            Debug.Log($"{car.Spec.Name}: AI predicted lap {FormatTime(Pilot.Profile.LapTime)} at Pace {Pace:0} on the {which}");
        }

        private void FixedUpdate()
        {
            if (Pilot == null) return;
            if (tuning != null && tuning.IsCompleted)
            {
                if (tuning.Status == TaskStatus.RanToCompletion)
                {
                    AiPilot tuned = tuning.Result;
                    // Stats changed in the inspector meanwhile: keep the line, take the new stats.
                    if (tuned.Pace == Pace && tuned.Consistency == Consistency && tuned.Safety == Safety) Use(tuned);
                    else { Line = tuned.Line; Build(); }
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
                if (race != null) Craft.Think(Pilot, race.Traffic, RaceCar.Field, glideClock);
                Pilot.Glide(glideClock);
                glideClock = 0f;
                car.Coast(Pilot.GlideSpeed, Pilot.GlideSteer);
                CountLaps();
                return;
            }

            if (car.Crashed)
            {
                crashTimer += dt;
                if (crashTimer > CrashWait) Recover();
                return;
            }
            crashTimer = 0f;

            if (race != null)
            {
                Craft.Think(Pilot, race.Traffic, RaceCar.Field, dt);
                Pilot.Towed = car.DragScale < 0.97f;
            }
            AiPilot.Controls c = Pilot.Drive(t.position, t.forward, car.Velocity, car.YawRate);
            car.SetControls(c.Throttle, c.Brake, c.Steer);

            // Stuck: wants to go but isn't moving, or has spun and is facing the wrong way.
            bool facingBack = Vector3.Dot(t.forward, Line.Tangent[Pilot.Index]) < -0.2f;
            bool stalled = car.Speed < 2f && c.Throttle > 0.3f;
            stuckTimer = stalled || facingBack ? stuckTimer + dt : 0f;
            if (stuckTimer > StuckTime) Recover();

            CountLaps();
        }

        /// <summary>Draws a gliding car where it is now, between its steps.</summary>
        private void Update()
        {
            if (!Gliding) return;
            float since = glideClock + Mathf.Max(Time.time - Time.fixedTime, 0f);
            Pilot.GlidePose(Pilot.GlideSpeed * since, out Vector3 at, out Vector3 forward, out Vector3 up);
            car.transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward, up));
        }

        /// <summary>Whether the car can leave physics now: driven by the AI and not waiting to be put back after a crash.</summary>
        public bool CanGlide => enabled && Pilot != null && !Gliding && !car.Crashed && !car.Asleep;

        /// <summary>Takes the car off physics. It glides on from where it is, at its speed along the track.</summary>
        public void Glide()
        {
            if (!CanGlide) return;
            Transform t = car.transform;
            int k = Line.Nearest(t.position, Pilot.Index);
            Pilot.StartGlide(t.position, Vector3.Dot(car.Velocity, Line.Tangent[k]));
            car.Sleep();
            car.Coast(Pilot.GlideSpeed, Pilot.GlideSteer);
            // Spread the gliding cars' steps over the physics steps, so they don't all land on one.
            glideClock = (Seed % 5) * Time.fixedDeltaTime;
            stuckTimer = crashTimer = 0f;
        }

        /// <summary>Back to physics where the gliding car is, moving along its path, so it carries on unbroken.</summary>
        public void Wake()
        {
            if (!Gliding) return;
            float since = glideClock + Mathf.Max(Time.time - Time.fixedTime, 0f);
            Pilot.GlidePose(Pilot.GlideSpeed * since, out Vector3 at, out Vector3 forward, out Vector3 up);
            car.Wake(at, Quaternion.LookRotation(forward, up), Pilot.GlideSpeed, Pilot.GlideSpeed * Pilot.GlideCurvature, Pilot.GlideSteer);
            Pilot.StopGlide();
            stuckTimer = crashTimer = 0f;
        }

        private void Recover()
        {
            car.ResetToTrack();
            Pilot.Lost();
            Pilot.Offset = 0f;
            stuckTimer = crashTimer = 0f;
            lastIndex = -1;
        }

        private void CountLaps()
        {
            int k = Pilot.Index;
            int n = Line.Count;
            // Crossing the start line: the index wraps from the end of the lap back to the start.
            if (lastIndex > n - 50 && k < 50)
            {
                if (lapStart >= 0f)
                {
                    lap++;
                    Debug.Log($"{car.Spec.Name}: lap {lap} {FormatTime(Time.time - lapStart)} (target {FormatTime(Pilot.Profile.LapTime)}, mistakes so far {Pilot.Mistakes})");
                }
                lapStart = Time.time;
            }
            lastIndex = k;
        }

        private static string FormatTime(float seconds) => $"{(int)(seconds / 60f)}:{seconds % 60f:00.000}";
    }
}
