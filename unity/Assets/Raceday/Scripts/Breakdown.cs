using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car whose engine or running gear just gave out. Nobody drives it: with no power it coasts, its driver
    /// steering it off the track onto the verge on the nearer side and braking gently, and once it is at rest it holds
    /// there on its brakes. If it hasn't stopped within <see cref="GiveUp"/> it is held where it is.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class Breakdown : MonoBehaviour
    {
        /// <summary>How far past the track's edge the car stops, m: across the verge, its wheels on the grass.</summary>
        public const float PastEdge = 3.2f;
        /// <summary>Room the car keeps from whatever stands beyond the verge, m.</summary>
        private const float KeepClear = 1.6f;
        /// <summary>Seconds the car may take to stop before it is held where it is.</summary>
        public const float GiveUp = 18f;
        /// <summary>Brake while it still has speed to lose, and as it rolls the last few metres.</summary>
        private const float CoastBrake = 0.4f, StopBrake = 0.9f;
        /// <summary>Under this speed, m/s, the car is at rest.</summary>
        private const float Rest = 0.4f;

        /// <summary>The track the car is on.</summary>
        public RacingLine Line;
        /// <summary>The track sample the car was at as it broke down.</summary>
        public int From;

        /// <summary>True once the car is at rest off the track, or has been held where it is.</summary>
        public bool Stopped { get; private set; }

        private CarController car;
        private int k = -1;
        private float side, since;

        private void Start()
        {
            car = GetComponent<CarController>();
            car.InputEnabled = false;
            car.Hold = false;
            car.AiDriving = true;
            k = Line != null ? Line.Nearest(transform.position, From) : -1;
            side = k >= 0 ? Side(Line.Circuit, k, transform.position) : 1f;
        }

        /// <summary>Which side the car pulls off to, 1 left or -1 right: the side it is on, or with more room off the track.</summary>
        public static float Side(Circuit c, int k, Vector3 at)
        {
            float lateral = Vector3.Dot(at - c.Centre[k], c.Left[k]);
            if (Mathf.Abs(lateral) > 0.25f * c.HalfWidth(k)) return Mathf.Sign(lateral);
            return Clear(c.Data.clearLeft, k) >= Clear(c.Data.clearRight, k) ? 1f : -1f;
        }

        /// <summary>Where across the track the car comes to rest at sample k, m from the centre, positive left.</summary>
        public static float Verge(Circuit c, int k, float side)
        {
            float room = Clear(side > 0f ? c.Data.clearLeft : c.Data.clearRight, k) - KeepClear;
            return side * (c.HalfWidth(k) + Mathf.Clamp(room, 1.2f, PastEdge));
        }

        private static float Clear(float[] clear, int k) => clear != null && k < clear.Length ? clear[k] : 10f;

        private void FixedUpdate()
        {
            if (car == null || Stopped || car.Asleep) return;
            since += Time.fixedDeltaTime;
            float speed = car.Speed;
            if (since > 0.5f && speed < Rest || since > GiveUp || k < 0)
            {
                Stop();
                return;
            }
            Circuit c = Line.Circuit;
            Vector3 at = transform.position;
            k = Line.Nearest(at, k);

            // Steer for a point on the verge a little way ahead, closer in as the car slows.
            float ahead = Mathf.Clamp(speed * 0.9f, 8f, 40f);
            int ka = c.Wrap(k + Mathf.RoundToInt(ahead / c.Step));
            Vector3 target = c.Centre[ka] + c.Left[ka] * Verge(c, ka, side);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 to = target - at;
            to.y = 0f;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float alpha = Mathf.Atan2(Vector3.Dot(to, right), Vector3.Dot(to, forward));
            float steer = Mathf.Atan(4f * car.Spec.HalfWheelbase * Mathf.Sin(alpha) / Mathf.Max(to.magnitude, 1f));

            // Off the track and nearly at the spot: brake to a stop.
            float lateral = Vector3.Dot(at - c.Centre[k], c.Left[k]);
            bool off = side * lateral > c.HalfWidth(k) + 0.5f;
            float brake = off && speed < 12f ? StopBrake : CoastBrake;
            car.SetControls(0f, brake, steer);
        }

        private void Stop()
        {
            Stopped = true;
            car.SetControls(0f, 1f, 0f);
            car.Hold = true;
        }
    }
}
