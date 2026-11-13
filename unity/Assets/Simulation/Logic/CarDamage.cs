#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Damage from real hits in the driven race: a barrier, or another car. It builds from 0 (untouched) to 100
    /// (wrecked) with the speed of each hit, and the more of it there is, the less grip the car has, and more than
    /// in proportion, so light damage costs little and heavy damage a lot. The crew can repair it at a stop, all
    /// of it or some, at a cost in time and spare parts for each point. One hit hard enough totals the car: it is
    /// out of the race and goes back to the garage at 0% condition. Damage still on the car at the flag comes off
    /// its condition, point for point.
    /// </summary>
    public static class CarDamage
    {
        /// <summary>Hits slower than this, m/s into what was hit, do no damage: a touch, or brushing a barrier.</summary>
        public const double MinImpact = 3;
        /// <summary>Damage for each m/s of a hit above <see cref="MinImpact"/>: 18 m/s into a barrier is about 38.</summary>
        public const double DamagePerImpact = 2.5;
        /// <summary>A hit at least this hard, m/s into what was hit (about 145 km/h), totals the car.</summary>
        public const double TotalImpact = 40;
        /// <summary>Share of its grip a car loses at 100 damage.</summary>
        public const double MaxGripLoss = 0.4;
        /// <summary>How the grip lost grows with damage: above 1, faster than in proportion.</summary>
        private const double GripCurve = 1.5;
        /// <summary>Crew seconds to repair one point of damage, before the crew's size and the Engineer skill scale it.</summary>
        public const double RepairSecondsPerPoint = 1.2;
        /// <summary>Points of damage one spare part repairs.</summary>
        public const double PointsPerPart = 10;
        /// <summary>Share of a lap's time spent where grip sets the speed, for weighing a repair against the laps to come.</summary>
        private const double GripShare = 0.6;

        /// <summary>
        /// Damaging hits a car takes per hour of driving, for a car far from the camera, which runs without physics and
        /// can't hit anything: in a pack the AI makes about this many contacts over 3 m/s an hour.
        /// </summary>
        public const double HitsPerHour = 3;
        /// <summary>Mean m/s above <see cref="MinImpact"/> of a rolled hit: most are knocks, a very few total the car.</summary>
        public const double MeanRolledImpact = 6;

        /// <summary>
        /// For a car with no physics: the speed of a hit it takes over <paramref name="seconds"/> of driving, or null
        /// for none. Hits come at <see cref="HitsPerHour"/>, their speed above <see cref="MinImpact"/> spread exponentially.
        /// </summary>
        public static double? RollHit(double seconds, Func<double> random)
        {
            if (seconds <= 0) return null;
            double chance = 1 - Math.Exp(-HitsPerHour * seconds / 3600);
            if (random() >= chance) return null;
            return MinImpact - MeanRolledImpact * Math.Log(1 - random());
        }

        /// <summary>Damage a hit at <paramref name="impact"/> m/s does.</summary>
        public static double FromImpact(double impact) => Math.Max(0, impact - MinImpact) * DamagePerImpact;

        /// <summary>True for a hit hard enough to total the car.</summary>
        public static bool Totals(double impact) => impact >= TotalImpact;

        /// <summary>How damage scales the car's grip: 1 untouched, 1 − <see cref="MaxGripLoss"/> at 100.</summary>
        public static double Grip(double damage) =>
            1 - MaxGripLoss * Math.Pow(Math.Max(0, Math.Min(100, damage)) / 100, GripCurve);

        /// <summary>Share by which damage slows a lap: 0 untouched.</summary>
        public static double LapSlowdown(double damage) => GripShare * (1 / Math.Sqrt(Grip(damage)) - 1);

        /// <summary>Spare parts it takes to repair <paramref name="points"/> of damage.</summary>
        public static int PartsFor(double points) => points <= 0 ? 0 : (int)Math.Ceiling(points / PointsPerPart - 1e-9);

        /// <summary>Points of damage <paramref name="parts"/> spare parts repair.</summary>
        public static double PointsFrom(int parts) => Math.Max(0, parts) * PointsPerPart;

        /// <summary>
        /// The damage the crew leave on the car: they repair point by point, worst first, while each point gains
        /// more over the stint to come than it costs in the box. Heavy damage is mostly repaired; light damage may
        /// be left. Repair all of it, or none, is the team's call (<see cref="RepairMode"/>).
        /// </summary>
        public static double CrewTarget(double damage, StintOutlook stint)
        {
            double cost = RepairSecondsPerPoint * stint.CrewMultiplier;
            double stintSeconds = stint.Laps * stint.LapSeconds;
            double left = Math.Max(0, Math.Min(100, damage));
            while (left > 0)
            {
                double next = Math.Max(0, left - 1);
                double gain = stintSeconds * (LapSlowdown(left) - LapSlowdown(next));
                if (gain <= cost * (left - next)) break;
                left = next;
            }
            return left;
        }

        /// <summary>The damage a stop leaves, for a team order: none of it repaired, all of it, or the crew's call.</summary>
        public static double Target(RepairMode mode, double damage, StintOutlook stint) => mode switch
        {
            RepairMode.None => damage,
            RepairMode.All => 0,
            _ => CrewTarget(damage, stint),
        };
    }
}
