#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Damage from real hits in the driven race: a barrier, or another car. It builds from 0 (untouched) to 100
    /// (wrecked) with the speed of each hit. It is not condition: it costs grip, more at the end of the car that was
    /// hit (a nose hit understeers, a tail hit oversteers), and adds drag from the broken bodywork. The crew can
    /// repair it at a stop, all of it or some, at a cost in spare parts for each point and in time that grows with the
    /// damage, from minutes of race time for a knock to hours for a wreck (<see cref="RepairSeconds(double)"/>). One hit
    /// hard enough totals the car: it is out of the race and goes back to the garage at 0% condition. Damage piling up to
    /// <see cref="RetireDamage"/> puts the car out too, and the damage comes off its condition as at the flag. Damage still on the car at
    /// the flag comes off its condition, one for one (<see cref="ConditionPerPoint"/>).
    /// </summary>
    public static class CarDamage
    {
        /// <summary>Hits slower than this, m/s into what was hit, do no damage: a touch, or brushing a barrier.</summary>
        public const double MinImpact = 5;
        /// <summary>Damage for each m/s of a hit above <see cref="MinImpact"/>: 18 m/s into a barrier is 7.8.</summary>
        public const double DamagePerImpact = 0.6;
        /// <summary>A hit at least this hard, m/s into what was hit (about 110 km/h), totals the car.</summary>
        public const double TotalImpact = 30;
        /// <summary>Damage on the car at which it is out of the race: hits that each leave it running add up to a wreck.</summary>
        public const double RetireDamage = 35;
        /// <summary>Share of its grip a car loses for each point of damage spread evenly: 15 damage, a heavy hit, costs 34%: a damaged car limps.</summary>
        public const double GripLossPerPoint = 0.0225;
        /// <summary>Grip an axle keeps however damaged: a wreck still crawls back to its box.</summary>
        public const double MinGrip = 0.3;
        /// <summary>Drag added for each point of damage, as a share of the car's own: 15 damage adds 45%.</summary>
        public const double DragPerPoint = 0.03;
        /// <summary>Condition a point of damage still on the car at the flag takes off: one for one.</summary>
        public const double ConditionPerPoint = 1;
        /// <summary>
        /// Crew seconds to repair damage down from each of these points to none, before the crew's size and the
        /// Engineer skill scale it: a race runs at 10 times the clock (1 race hour is 360 s), so a knock of 5 is 5
        /// race minutes, 10 is 15 race minutes, a heavy hit of 15 (one just short of totalling the car) is a race hour, and damage at the
        /// retirement limit (<see cref="RetireDamage"/>) is 3 race hours. Between the points it grows in a straight line, and past the last at
        /// the last slope.
        /// </summary>
        private static readonly (double Points, double Seconds)[] RepairCurve =
        {
            (0, 0),
            (5, 30),
            (10, 90),
            (15, 360),
            (RetireDamage, 1080),
        };
        /// <summary>Points of damage one spare part repairs.</summary>
        public const double PointsPerPart = 4;
        /// <summary>Share of a lap's time spent where grip sets the speed, for weighing a repair against the laps to come.</summary>
        private const double GripShare = 0.6;
        /// <summary>Share of a lap's time spent where drag sets the speed.</summary>
        private const double DragShare = 0.4;

        /// <summary>
        /// Damaging hits a car takes per hour of driving, for a car far from the camera, which runs without physics and
        /// can't hit anything: in a pack the AI makes about this many contacts over 5 m/s an hour.
        /// </summary>
        public const double HitsPerHour = 3;
        /// <summary>Mean m/s above <see cref="MinImpact"/> of a rolled hit: most are knocks, a very few total the car.</summary>
        public const double MeanRolledImpact = 6.3;

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

        /// <summary>True for damage enough to put the car out of the race (<see cref="RetireDamage"/>).</summary>
        public static bool Retires(double damage) => damage >= RetireDamage;

        private static double Points(double damage) => Math.Max(0, Math.Min(100, damage));

        /// <summary>
        /// How damage scales one axle's grip. <paramref name="front"/> is the share of the damage at the front, 0 all at
        /// the rear, 0.5 even: the damaged end loses up to one and a half times the even share, the other end half.
        /// </summary>
        public static double AxleGrip(double damage, double front, bool frontAxle)
        {
            double f = Math.Max(0, Math.Min(1, front));
            double weight = frontAxle ? 0.5 + f : 1.5 - f;
            return Math.Max(MinGrip, 1 - GripLossPerPoint * Points(damage) * weight);
        }

        /// <summary>How damage scales the grip of the car as a whole, spread evenly.</summary>
        public static double Grip(double damage) => Math.Max(MinGrip, 1 - GripLossPerPoint * Points(damage));

        /// <summary>How damage scales the car's drag: 1 untouched, more for each point.</summary>
        public static double Drag(double damage) => 1 + DragPerPoint * Points(damage);

        /// <summary>Share by which damage slows a lap: 0 untouched.</summary>
        public static double LapSlowdown(double damage) =>
            GripShare * (1 / Math.Sqrt(Grip(damage)) - 1) + DragShare * (Math.Pow(Drag(damage), 1.0 / 3) - 1);

        /// <summary>Share of a hit's damage at the front: 1 on the nose, 0 on the tail, 0.5 on the side.</summary>
        public static double FrontShare(double along, double halfLength) =>
            halfLength <= 0 ? 0.5 : Math.Max(0, Math.Min(1, 0.5 + 0.5 * along / halfLength));

        /// <summary>Most damage the crew leave on a car: one heavy hit (<see cref="FromImpact"/> of <see cref="TotalImpact"/>) short of <see cref="RetireDamage"/>.</summary>
        public static double SafeDamage => RetireDamage - FromImpact(TotalImpact);

        /// <summary>Crew seconds to repair all of <paramref name="damage"/>, before the crew's size and the Engineer skill scale it (<see cref="RepairCurve"/>).</summary>
        public static double RepairSeconds(double damage)
        {
            double d = Math.Max(0, damage);
            for (int i = 1; i < RepairCurve.Length; i++)
            {
                (double p0, double s0) = RepairCurve[i - 1];
                (double p1, double s1) = RepairCurve[i];
                if (d <= p1 || i == RepairCurve.Length - 1) return s0 + (d - p0) * (s1 - s0) / (p1 - p0);
            }
            return 0;
        }

        /// <summary>Crew seconds to repair damage from <paramref name="from"/> down to <paramref name="to"/>, before the crew's size and the Engineer skill scale it.</summary>
        public static double RepairSeconds(double from, double to) => Math.Max(0, RepairSeconds(from) - RepairSeconds(Math.Max(0, to)));

        /// <summary>Spare parts it takes to repair <paramref name="points"/> of damage.</summary>
        public static int PartsFor(double points) => points <= 0 ? 0 : (int)Math.Ceiling(points / PointsPerPart - 1e-9);

        /// <summary>Points of damage <paramref name="parts"/> spare parts repair.</summary>
        public static double PointsFrom(int parts) => Math.Max(0, parts) * PointsPerPart;

        /// <summary>
        /// The damage the crew leave on the car. They always repair it down to <see cref="SafeDamage"/>, as they
        /// always fix an issue that could end the race. Below that they repair point by point, worst first, while each point gains
        /// more over the rest of the race than it costs in the box. Damage stays on the car until it is repaired, and
        /// a repair costs the same later, so a point worth repairing at all is worth it now. Heavy damage early on is
        /// mostly repaired; light damage, or damage late in the race, may be left. Repair all of it, or none, is the
        /// team's call (<see cref="RepairMode"/>).
        /// </summary>
        public static double CrewTarget(double damage, StintOutlook stint)
        {
            double racing = Math.Max(stint.Laps * stint.LapSeconds, stint.SecondsLeft);
            // Past a heavy hit short of the retirement limit, whatever it costs: one more could end the race.
            double left = Math.Max(0, Math.Min(SafeDamage, damage));
            while (left > 0)
            {
                double next = Math.Max(0, left - 1);
                double gain = racing * (LapSlowdown(left) - LapSlowdown(next));
                if (gain <= RepairSeconds(left, next) * stint.CrewMultiplier) break;
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
