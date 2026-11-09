using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Puts an <see cref="AiPilot"/> in a physics car. Each physics step it hands the pilot the car's
    /// position and motion and passes the pedals and steering back. After a hard crash, or when the
    /// car is stuck, it puts the car back on track as a player would with R. Lap times go to the console.
    /// Changing the stats in the inspector during Play takes effect straight away.
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

        public RacingLine Line;
        public AiPilot Pilot { get; private set; }

        private CarController car;
        private float stuckTimer, crashTimer;
        private int lastIndex = -1;
        private float lapStart = -1f;
        private int lap;

        private void Start()
        {
            car = GetComponent<CarController>();
            Build();
        }

        private void OnEnable()
        {
            if (car != null) car.AiDriving = true;
            Pilot?.Lost();
            lastIndex = -1;
            lapStart = -1f;
        }

        private void OnDisable()
        {
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
            Pilot = new AiPilot(Line, car.Spec, Pace, Consistency, Safety, Seed);
            car.AiDriving = enabled;
            Debug.Log($"{car.Spec.Name}: AI target lap {FormatTime(Pilot.Profile.LapTime)} at Pace {Pace:0}");
        }

        private void FixedUpdate()
        {
            if (Pilot == null) return;
            float dt = Time.fixedDeltaTime;
            Transform t = car.transform;

            if (car.Crashed)
            {
                crashTimer += dt;
                if (crashTimer > CrashWait) Recover();
                return;
            }
            crashTimer = 0f;

            AiPilot.Controls c = Pilot.Drive(t.position, t.forward, car.Velocity, car.YawRate);
            car.SetControls(c.Throttle, c.Brake, c.Steer);

            // Stuck: wants to go but isn't moving, or has spun and is facing the wrong way.
            bool facingBack = Vector3.Dot(t.forward, Line.Tangent[Pilot.Index]) < -0.2f;
            bool stalled = car.Speed < 2f && c.Throttle > 0.3f;
            stuckTimer = stalled || facingBack ? stuckTimer + dt : 0f;
            if (stuckTimer > StuckTime) Recover();

            CountLaps();
        }

        private void Recover()
        {
            car.ResetToTrack();
            Pilot.Lost();
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
