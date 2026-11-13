using System;
using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What a stop does: the litres that go in, new tyres or not, the issues fixed (null for every one), and the
    /// damage left once the crew have repaired what they will (null to repair all of it).
    /// </summary>
    public readonly struct StopPlan
    {
        public readonly float Litres;
        public readonly bool Tyres;
        public readonly IReadOnlyList<string> Fix;
        public readonly double? DamageTo;

        public StopPlan(float litres, bool tyres, IReadOnlyList<string> fix, double? damageTo = null)
        {
            Litres = litres;
            Tyres = tyres;
            Fix = fix;
            DamageTo = damageTo;
        }

        /// <summary>A stop as the AI teams make it: a full tank, new tyres while a set is left, and every issue and all the damage the spares allow.</summary>
        public static StopPlan Default(Consumables s) => new StopPlan(Mathf.Max(s.Capacity - s.Fuel, 0f), s.TyresLeft, null);
    }

    /// <summary>
    /// The player's next stop, from the car's <see cref="PitOrders"/>, with the crew's call wherever the team has
    /// left a choice open. The crew fuel the car to the flag, or fill it when the flag is further than a tank,
    /// fit new tyres if the old ones won't last that far, and fix what <see cref="PitRepairs"/> says is worth it.
    /// Fuel is counted in laps at the limit on the engine map in use, with a fifth of a lap spare.
    /// </summary>
    public sealed class StopPlanner
    {
        private const float Spare = 0.2f;

        private readonly Consumables s;
        private readonly CarSpec spec;
        /// <summary>Seconds of racing left before the flag.</summary>
        private readonly Func<float> secondsLeft;
        /// <summary>Seconds a lap takes.</summary>
        private readonly Func<float> lapSeconds;

        public StopPlanner(Consumables supplies, CarSpec spec, Func<float> secondsLeft, Func<float> lapSeconds)
        {
            s = supplies;
            this.spec = spec;
            this.secondsLeft = secondsLeft;
            this.lapSeconds = lapSeconds;
        }

        private PitOrders Orders => s.Orders;

        /// <summary>Laps a full tank lasts.</summary>
        public int FullLaps => Mathf.Max(1, Mathf.FloorToInt(s.Capacity / Mathf.Max(s.LitresPerLap, 0.01f) - Spare));

        /// <summary>The crew's call on fuel: laps to the flag, the lap the clock runs out in included, at most a full tank.</summary>
        public int CrewLaps => Mathf.Clamp(Mathf.CeilToInt(secondsLeft() / Mathf.Max(lapSeconds(), 1f)) + 1, 1, FullLaps);

        /// <summary>Laps of fuel the car leaves with.</summary>
        public int Laps => Mathf.Clamp(Orders.Laps ?? CrewLaps, 1, FullLaps);

        /// <summary>True when the car leaves on a full tank.</summary>
        public bool Full => Laps >= FullLaps;

        /// <summary>Litres that go in.</summary>
        public float Litres
        {
            get
            {
                float room = Mathf.Max(s.Capacity - s.Fuel, 0f);
                return Full ? room : Mathf.Clamp((Laps + Spare) * s.LitresPerLap - s.Fuel, 0f, room);
            }
        }

        /// <summary>New tyres: the team's call, or the crew's if the old ones won't last the stint. Never with no set left.</summary>
        public bool Tyres => s.TyresLeft && (Orders.Tyres ?? s.Wear + s.WearPerLapNow * Laps > PitPlan.WearLimit);

        /// <summary>The issues the stop fixes, as <see cref="PitRepairs"/> picks them.</summary>
        public IReadOnlyList<string> Fix
        {
            get
            {
                CarRaceLog log = s.Log;
                if (log == null) return Array.Empty<string>();
                return PitRepairs.Choose(Orders.Repairs, log.ActiveIssues, log.Stats, Stint(log));
            }
        }

        /// <summary>The damage the stop leaves: none, all of it, or what the crew judge not worth the time (<see cref="CarDamage.Target"/>).</summary>
        public double DamageTo
        {
            get
            {
                CarRaceLog log = s.Log;
                if (log == null) return 0;
                return CarDamage.Target(Orders.Repairs, log.Damage, Stint(log));
            }
        }

        private StintOutlook Stint(CarRaceLog log)
        {
            double crew = PitStop.CrewMultiplier(log.Car.CrewSize, log.Car.EngineerSkill);
            return new StintOutlook
            {
                Laps = Laps,
                LapSeconds = lapSeconds(),
                LitresPerLap = s.LitresPerLap,
                SecondsPerLitre = PitService.SecondsPerLitre(spec) * crew,
                CrewMultiplier = crew,
            };
        }

        public StopPlan Plan() => new StopPlan(Litres, Tyres, Fix, DamageTo);

        /// <summary>Seconds the car would stand in its box for the plan as it stands.</summary>
        public float Seconds()
        {
            IReadOnlyList<string> fix = Fix;
            double extra = s.Log != null ? s.Log.WorkSeconds(fix, DamageTo) : 0.0;
            return PitService.Seconds(spec, Litres, Tyres, s.Log, extra);
        }
    }
}
