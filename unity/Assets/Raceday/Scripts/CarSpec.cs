using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The physical numbers for one car, worked out from its game stats: Power sets the engine,
    /// Handling sets tyre grip and downforce. Everything else comes from the car's class.
    /// </summary>
    public sealed class CarSpec
    {
        public const float AirDensity = 1.2f;

        public string Name;
        /// <summary>The car's game stats, for fuel, tyre wear and pit stops.</summary>
        public CarStats Stats;
        public float Mass;            // kg
        public float PeakPower;       // W, reached at the redline
        public float Redline;         // engine speed, rad/s
        public float Grip;            // tyre friction coefficient on dry tarmac
        public float PeakSlip;        // slip angle of peak lateral grip, rad
        public float LiftArea;        // downforce coefficient times area, m²
        public float DragArea;        // drag coefficient times area, m²
        public float BrakeDecel;      // brake capacity in g
        public float FrontBrakeShare;
        public float WheelRadius;
        public float HalfTrack;       // wheel centre to car centre line
        public float HalfWheelbase;
        public float SpringRate;      // N/m per wheel
        public float Damping;         // N·s/m per wheel
        public float StaticCompression;
        public float Travel;
        public float SteerLock;       // rad
        public float[] Gears;         // overall ratio, engine speed / wheel speed
        public Vector3 CentreOfMass;

        public float TopSpeed { get; private set; }

        public static CarSpec For(string modelId)
        {
            CarModel model = CarModels.All.First(c => c.Id == modelId);
            CarStats s = model.BaseStats;
            float power = (float)s.Power / 100f;
            float handling = (float)s.Handling / 100f;
            bool racer = model.CarClass == CarClass.A || model.CarClass == CarClass.B || model.CarClass == CarClass.F1;

            var spec = new CarSpec
            {
                Name = model.Name,
                Stats = s,
                Mass = model.CarClass switch
                {
                    CarClass.F => 950f,
                    CarClass.E => 1250f,
                    CarClass.D => 1300f,
                    CarClass.C => 1450f,
                    CarClass.B => 1350f,
                    CarClass.A => 1000f,
                    _ => 750f,
                },
                // A 1990s saloon makes about 50 kW and a hybrid prototype about 700.
                PeakPower = 45000f * Mathf.Pow(17f, power),
                Redline = (racer ? 9000f : 6800f) * Mathf.PI / 30f,
                Grip = 1.0f + 0.6f * handling,
                PeakSlip = racer ? 0.14f : 0.17f,
                LiftArea = 4.5f * handling * handling * handling,
                DragArea = 0.62f + 0.55f * handling * handling * handling,
                BrakeDecel = 1.1f + 0.6f * handling,
                FrontBrakeShare = 0.6f,
                WheelRadius = 0.38f,
                HalfTrack = 0.775f,
                HalfWheelbase = 1.505f,
                StaticCompression = racer ? 0.025f : model.CarClass == CarClass.C ? 0.04f : 0.06f,
                SteerLock = racer ? 0.3f : 0.55f,
                CentreOfMass = new Vector3(0f, racer ? 0.28f : 0.42f, 0.05f),
            };
            float corner = spec.Mass * 9.81f / 4f;
            spec.SpringRate = corner / spec.StaticCompression;
            spec.Damping = 2f * 0.35f * Mathf.Sqrt(spec.SpringRate * spec.Mass / 4f);
            spec.Travel = spec.StaticCompression * 2.5f + 0.05f;

            spec.TopSpeed = SolveTopSpeed(spec);
            int count = racer ? 7 : model.CarClass == CarClass.F || model.CarClass == CarClass.E ? 5 : 6;
            spec.Gears = new float[count];
            float top = spec.TopSpeed * 1.03f, first = spec.TopSpeed * 0.3f;
            for (int i = 0; i < count; i++)
            {
                float v = first * Mathf.Pow(top / first, i / (float)(count - 1));
                spec.Gears[i] = spec.Redline * spec.WheelRadius / v;
            }
            return spec;
        }

        /// <summary>Engine power at a fraction of the redline: rises to its peak at the redline.</summary>
        public float PowerAt(float x) => PeakPower * (1.5f * x - 0.5f * x * x * x);

        /// <summary>The gear the automatic holds at a speed: the lowest one below its upshift point.</summary>
        public int GearFor(float speed)
        {
            float wheel = speed / WheelRadius;
            int gear = 1;
            while (gear < Gears.Length && wheel * Gears[gear - 1] / Redline > 0.9f) gear++;
            return gear;
        }

        /// <summary>Speed where full power just balances drag and rolling resistance.</summary>
        private static float SolveTopSpeed(CarSpec s)
        {
            float v = 20f;
            for (int i = 0; i < 60; i++)
            {
                float resist = 0.5f * AirDensity * s.DragArea * v * v * v + 0.012f * s.Mass * 9.81f * v;
                v *= Mathf.Pow(s.PeakPower / resist, 0.25f);
            }
            return v;
        }
    }
}
