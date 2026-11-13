#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Damage from real hits in the driven race: a barrier, or another car. It builds from 0 (untouched) to 100
    /// (wrecked) with the speed of each hit. It is not condition: it costs grip, more at the end of the car that was
    /// hit (a nose hit understeers, a tail hit oversteers), and adds drag from the broken bodywork. The crew can
    /// repair it at a stop, all of it or some, at a cost in time and spare parts for each point. One hit hard enough
    /// totals the car: it is out of the race and goes back to the garage at 0% condition. A share of the damage still
    /// on the car at the flag comes off its condition (<see cref="ConditionPerPoint"/>).
    /// </summary>
    public static class CarDamage
    {
        /// <summary>Hits slower than this, m/s into what was hit, do no damage: a touch, or brushing a barrier.</summary>
        public const double MinImpact = 3;
        /// <summary>Damage for each m/s of a hit above <see cref="MinImpact"/>: 18 m/s into a barrier is about 38.</summary>
        public const double DamagePerImpact = 2.5;
        /// <summary>A hit at least this hard, m/s into what was hit (about 145 km/h), totals the car.</summary>
        public const double TotalImpact = 40;
        /// <summary>Share of its grip a car loses at 100 damage spread evenly, in proportion to the damage.</summary>
        public const double MaxGripLoss = 0.45;
        /// <summary>Drag added at 100 damage, as a share of the car's own: 0.6 is 60% more.</summary>
        public const double MaxDrag = 0.6;
        /// <summary>Condition a point of damage still on the car at the flag takes off: a fifth of it.</summary>
        public const double ConditionPerPoint = 0.2;
        /// <summary>Crew seconds to repair one point of damage, before the crew's size and the Engineer skill scale it.</summary>
        public const double RepairSecondsPerPoint = 1.2;
        /// <summary>Points of damage one spare part repairs.</summary>
        public const double PointsPerPart = 10;
        /// <summary>Share of a lap's time spent where grip sets the speed, for weighing a repair against the laps to come.</summary>
        private const double GripShare = 0.6;
        /// <summary>Share of a lap's time spent where drag sets the speed.</summary>
        private const double DragShare = 0.4;

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

        private static double Share(double damage) => Math.Max(0, Math.Min(100, damage)) / 100;

        /// <summary>
        /// How damage scales one axle's grip. <paramref name="front"/> is the share of the damage at the front, 0 all at
        /// the rear, 0.5 even: the damaged end loses up to one and a half times the even share, the other end half.
        /// </summary>
        public static double AxleGrip(double damage, double front, bool frontAxle)
        {
            double f = Math.Max(0, Math.Min(1, front));
            double weight = frontAxle ? 0.5 + f : 1.5 - f;
            return Math.Max(0.1, 1 - MaxGripLoss * Share(damage) * weight);
        }

        /// <summary>How damage scales the grip of the car as a whole, spread evenly.</summary>
        public static double Grip(double damage) => 1 - MaxGripLoss * Share(damage);

        /// <summary>How damage scales the car's drag: 1 untouched, 1 + <see cref="MaxDrag"/> at 100.</summary>
        public static double Drag(double damage) => 1 + MaxDrag * Share(damage);

        /// <summary>Share by which damage slows a lap: 0 untouched.</summary>
        public static double LapSlowdown(double damage) =>
            GripShare * (1 / Math.Sqrt(Grip(damage)) - 1) + DragShare * (Math.Pow(Drag(damage), 1.0 / 3) - 1);

        /// <summary>Share of a hit's damage at the front: 1 on the nose, 0 on the tail, 0.5 on the side.</summary>
        public static double FrontShare(double along, double halfLength) =>
            halfLength <= 0 ? 0.5 : Math.Max(0, Math.Min(1, 0.5 + 0.5 * along / halfLength));

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
