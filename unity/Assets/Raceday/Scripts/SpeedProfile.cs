using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The target speed at every sample of a racing line for one car: as fast as its grip allows in
    /// the corners, then the fastest its engine can reach on the way out and the latest its brakes
    /// can stop it on the way in. <c>limit</c> is the share of the car's grip and braking the driver
    /// dares to use, so 1 is the car's absolute limit.
    /// </summary>
    public sealed class SpeedProfile
    {
        private const float G = 9.81f;
        /// <summary>The tyres hold a little less than their nominal grip once the car is loaded up and sliding.</summary>
        private const float GripUsable = 0.97f;

        public readonly RacingLine Line;
        public readonly float[] Speed;
        /// <summary>Acceleration the profile asks for at each sample, m/s², negative when braking.</summary>
        public readonly float[] Accel;
        /// <summary>Predicted lap time driving the profile exactly, seconds.</summary>
        public readonly float LapTime;

        private readonly CarSpec spec;
        private readonly float aero;     // downforce per unit mass per (m/s)²
        private readonly float drag;     // drag per unit mass per (m/s)²
        private readonly float mu;
        // Cornering limit of each axle, as a = μ·(weight·(g + dip·v²) + downforce·v²): the axle
        // that runs out of grip first sets the corner speed. Front carries more weight than downforce,
        // so fast corners end in understeer, as CarController's tyres do.
        private readonly float frontWeight, frontAero, rearWeight, rearAero;
        private readonly float frontShare;

        private const float TableStep = 0.5f;
        private float[] engineTable;   // full-throttle acceleration every half metre per second

        public SpeedProfile(RacingLine line, CarSpec spec, float limit)
        {
            Line = line;
            this.spec = spec;
            aero = 0.5f * CarSpec.AirDensity * spec.LiftArea / spec.Mass;
            drag = 0.5f * CarSpec.AirDensity * spec.DragArea / spec.Mass;
            int n = line.Count;
            mu = spec.Grip * GripUsable * limit;
            // Share of the cornering force each axle must carry is the other axle's distance from the centre of mass.
            float front = (spec.HalfWheelbase + spec.CentreOfMass.z) / (2f * spec.HalfWheelbase);
            frontShare = front;
            frontWeight = 0.97f;
            frontAero = 0.97f * 0.4f * aero / front;
            rearWeight = 1.06f;
            rearAero = 1.06f * 0.6f * aero / (1f - front);
            float brake = spec.BrakeDecel * G * limit;
            float top = spec.TopSpeed;

            // Corner speed: v²·κ ≤ μ·(w·(g + dip·v²) + d·v²) + banking, for each axle.
            var corner = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v2 = Mathf.Min(CornerSpeed2(k, frontWeight, frontAero), CornerSpeed2(k, rearWeight, rearAero));
                corner[k] = Mathf.Min(Mathf.Sqrt(Mathf.Max(v2, 25f)), top);
            }

            // Start from the slowest corner, so one pass each way settles the whole lap.
            int slowest = 0;
            for (int k = 1; k < n; k++) if (corner[k] < corner[slowest]) slowest = k;

            Speed = new float[n];
            System.Array.Copy(corner, Speed, n);
            for (int i = 0; i < n; i++)
            {
                int k = (slowest + i) % n, next = (k + 1) % n;
                float v = Speed[k];
                float pull = Mathf.Min(DriveAccel(v), RearGripLeft(k, v, v * v * line.Curvature[k]));
                float a = Mathf.Min(pull, Grip(k, v)) - Resist(k, v);
                float reach = Mathf.Sqrt(Mathf.Max(v * v + 2f * a * line.Segment[k], 0f));
                Speed[next] = Mathf.Min(Speed[next], reach);
            }
            for (int i = 0; i < n; i++)
            {
                int k = ((slowest - i) % n + n) % n, previous = (k - 1 + n) % n;
                float v = Speed[k];
                float d = Mathf.Min(brake, Grip(k, v)) + Resist(k, v);
                float reach = Mathf.Sqrt(v * v + 2f * d * line.Segment[previous]);
                Speed[previous] = Mathf.Min(Speed[previous], reach);
            }

            Accel = new float[n];
            float time = 0f;
            for (int k = 0; k < n; k++)
            {
                int next = (k + 1) % n;
                float v0 = Speed[k], v1 = Speed[next], s = line.Segment[k];
                Accel[k] = (v1 * v1 - v0 * v0) / (2f * Mathf.Max(s, 0.01f));
                time += 2f * s / Mathf.Max(v0 + v1, 0.1f);
            }
            LapTime = time;
        }

        /// <summary>Target speed at a fractional sample index, so the target moves smoothly between samples.</summary>
        public float SpeedAt(float index)
        {
            int n = Line.Count;
            index = Mathf.Repeat(index, n);
            int k = (int)index;
            float t = index - k;
            return Mathf.Lerp(Speed[k % n], Speed[(k + 1) % n], t);
        }

        /// <summary>Banking that helps the car round, as a slope: positive when the outside of the bend is higher.</summary>
        private float Banking(int k)
        {
            float kappa = Line.Curvature[k];
            if (Mathf.Abs(kappa) < 1e-4f) return 0f;
            // Crossfall is the rise of the left edge; a right-hander is helped when the left is higher.
            return Line.Circuit.Data.crossfall[k] * Mathf.Sign(kappa);
        }

        private float CornerSpeed2(int k, float weight, float down)
        {
            float kappa = Mathf.Abs(Line.Curvature[k]);
            float denominator = kappa - mu * (weight * Line.VerticalCurvature[k] + down);
            float top = spec.TopSpeed;
            return denominator > 1e-6f ? (mu * weight * G + Banking(k) * G) / denominator : top * top;
        }

        /// <summary>Most cornering acceleration the tyres hold at this speed, before the weaker axle lets go.</summary>
        private float LateralLimit(int k, float v)
        {
            float dip = Mathf.Max(G + Line.VerticalCurvature[k] * v * v, 0.2f * G);
            float frontLimit = mu * (frontWeight * dip + frontAero * v * v);
            float rearLimit = mu * (rearWeight * dip + rearAero * v * v);
            return Mathf.Min(frontLimit, rearLimit);
        }

        /// <summary>Grip left for speeding up or slowing down once the corner has taken its share (friction circle).</summary>
        private float Grip(int k, float v)
        {
            float total = LateralLimit(k, v);
            float lateral = Mathf.Max(v * v * Mathf.Abs(Line.Curvature[k]) - Banking(k) * G, 0f);
            float left = total * total - lateral * lateral;
            return left > 0f ? Mathf.Sqrt(left) : 0.05f * total;
        }

        /// <summary>
        /// What the driven rear tyres can still put down while carrying their share of a cornering
        /// acceleration. The drive is split evenly, and the inside wheel is lightened by the car
        /// leaning, so it is the one that spins first and lets go sideways.
        /// </summary>
        public float RearGripLeft(int k, float v, float lateral)
        {
            float dip = Mathf.Max(G + Line.VerticalCurvature[k] * v * v, 0.2f * G);
            float rearShare = 1f - frontShare;
            float axleLoad = rearWeight * dip * rearShare + rearAero * v * v * rearShare;   // already weighted by 1.06
            float axleCap = mu * axleLoad;
            float side = Mathf.Abs(lateral) * rearShare;
            float used = Mathf.Clamp01(side / Mathf.Max(axleCap, 0.01f));
            // Weight moved off the inside rear wheel, per unit mass.
            float lean = Mathf.Abs(lateral) * spec.CentreOfMass.y / (2f * spec.HalfTrack) * rearShare;
            float insideCap = mu * rearWeight * Mathf.Max(axleLoad / rearWeight * 0.5f - lean, 0f);
            return 2f * 0.9f * insideCap * Mathf.Sqrt(1f - used * used);
        }

        /// <summary>Drag, rolling resistance and the hill, as a deceleration.</summary>
        public float Resist(int k, float v) => drag * v * v + 0.012f * G + G * Line.Grade[k];

        /// <summary>Best acceleration from the engine, limited by what the driven rear tyres can put down.</summary>
        public float DriveAccel(float v) => Mathf.Min(EngineTable(v), Traction(v));

        /// <summary><see cref="EngineAccel"/> looked up from a table, for the passes over every sample.</summary>
        private float EngineTable(float v)
        {
            if (engineTable == null)
            {
                engineTable = new float[Mathf.CeilToInt(spec.TopSpeed * 1.3f / TableStep) + 2];
                for (int i = 0; i < engineTable.Length; i++) engineTable[i] = EngineAccel(i * TableStep);
            }
            float f = Mathf.Clamp(v / TableStep, 0f, engineTable.Length - 1.001f);
            int j = (int)f;
            return Mathf.Lerp(engineTable[j], engineTable[j + 1], f - j);
        }

        /// <summary>What the rear tyres can put down before they spin, as an acceleration.</summary>
        public float Traction(float v)
        {
            float rearLoad = 0.5f * spec.Mass * G + 0.6f * aero * spec.Mass * v * v;
            return 0.9f * 1.06f * spec.Grip * rearLoad / spec.Mass;
        }

        /// <summary>
        /// Acceleration from the engine at full throttle at this speed, in whichever gear pulls
        /// hardest below the automatic's upshift point.
        /// </summary>
        public float EngineAccel(float v)
        {
            float wheel = v / spec.WheelRadius;
            float best = 0f;
            foreach (float ratio in spec.Gears)
            {
                float x = wheel * ratio / spec.Redline;
                if (x >= 0.97f) continue;
                float xe = Mathf.Max(x, 0.2f);
                float torque = spec.PowerAt(xe) / (xe * spec.Redline);
                best = Mathf.Max(best, torque * ratio / spec.WheelRadius);
            }
            return best / spec.Mass;
        }
    }
}
