#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/degradation.ts.</summary>
    public static class Degradation
    {
        // ---------------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------------

        // --- Instruction mode scale (same across all four systems) ---
        // Push = 30% faster degradation; Conserve = 30% slower.
        private static double ModeDegradationScale(InstructionMode mode) => mode switch
        {
            InstructionMode.Push => 1.3,
            InstructionMode.Normal => 1.0,
            InstructionMode.Conserve => 0.7,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // --- Tyre wear ---
        // Set so a set of tyres lasts about two tanks of fuel on a durable car (Tyre Durability
        // 80 and up, Smoothness 50: 22 laps or more before the pit limit) but not on a car that
        // eats its tyres (Durability 30: about 19). Keeping the tyres on at a stop saves the set
        // and the change, for a lap time cost on old rubber.
        /// <summary>Wear added per lap (0 to 100 scale) with baseline stats and Normal mode.</summary>
        private const double BaseTyreWearPerLap = 6;
        /// <summary>
        /// Maximum fractional reduction in wear rate from combined Tyre Durability + Smoothness stats.
        /// At both stats = 100, wear rate is halved.
        /// </summary>
        private const double MaxTyreStatReduction = 0.5;

        // --- Fuel consumption ---
        // Baseline calibrated for time-bounded sim. At ~6 L/lap a class-A 70 L tank
        // covers ~12 laps, so refuelling is on a similar cadence to tyres rather
        // than driving extra stops. Aggressive reductions here also shorten the
        // refuel task time (fewer litres × per-litre seconds).
        /// <summary>Litres consumed per lap with baseline Fuel Efficiency and Normal mode.</summary>
        private const double BaseFuelPerLap = 7;
        /// <summary>Maximum fractional reduction in fuel consumption from Fuel Efficiency.</summary>
        private const double MaxFuelStatReduction = 0.5;

        // --- Driver fatigue ---
        // Comfort is the car's only lever on fatigue, so it gets the bigger share. The base is set so
        // a Comfort 50, Stamina 50 car tires at about the same rate as before Comfort got its own cut.
        /// <summary>Fatigue built up per lap (0 to 100 scale) at Comfort 0, Stamina 0 and Normal mode.</summary>
        private const double BaseFatiguePerLap = 3.7;
        /// <summary>Fractional reduction in fatigue rate at Comfort 100.</summary>
        private const double MaxComfortFatigueReduction = 0.6;
        /// <summary>Fractional reduction in fatigue rate at Stamina 100.</summary>
        private const double MaxStaminaFatigueReduction = 0.25;

        // --- Car condition ---
        // Condition takes up to 20% off power/handling via effectiveStats (0% condition).
        // At 0.25 %/lap a fresh car stays well above 80 % across one race, so a race costs
        // a few percent of pace; cars still deteriorate meaningfully across the season.
        /// <summary>Condition lost per lap (0 to 100 scale) at age 0, skill 0, Normal mode.</summary>
        private const double BaseConditionDecayPerLap = 0.25;
        /// <summary>Fractional increase in condition decay rate per year of car age.</summary>
        private const double AgeConditionRate = 0.05;
        /// <summary>Cap on the age multiplier; prevents unbounded decay on very old cars (mirrors EffectiveStats).</summary>
        private const double MaxAgeFactor = 2.0; // reached at age 20, unchanged beyond
        /// <summary>Maximum of the Engineer skill stat (matches PlayerSkills.Engineer).</summary>
        private const double MaxEngineerSkill = 20;
        /// <summary>Maximum fractional reduction in decay from Engineer skill.</summary>
        private const double MaxEngineerReduction = 0.4; // skill 20 → 40% slower condition decay

        /// <summary>
        /// Returns the tyre wear value after one lap (GDD §2).
        /// Rate is increased by low Tyre Durability and Smoothness; Push mode accelerates it.
        /// </summary>
        /// <param name="current">Current tyre wear (0 = fresh, 100 = fully worn).</param>
        /// <param name="tyreDurability">Car's Tyre Durability effective stat (0 to 100).</param>
        /// <param name="smoothness">Driver's Smoothness stat (0 to 100).</param>
        /// <param name="mode">Current driver instruction mode.</param>
        /// <returns>New tyre wear, clamped to a maximum of 100.</returns>
        public static double UpdateTyreWear(double current, double tyreDurability, double smoothness, InstructionMode mode)
        {
            double statFactor = 1 - ((tyreDurability + smoothness) / 200) * MaxTyreStatReduction;
            double wearPerLap = BaseTyreWearPerLap * statFactor * ModeDegradationScale(mode);
            return Math.Min(100, current + wearPerLap);
        }

        /// <summary>
        /// Returns the fuel level after one lap (GDD §2).
        /// Consumption is reduced by high Fuel Efficiency; Push mode burns more fuel.
        /// </summary>
        /// <param name="current">Current fuel level in litres.</param>
        /// <param name="fuelEfficiency">Car's Fuel Efficiency effective stat (0 to 100).</param>
        /// <param name="mode">Current driver instruction mode.</param>
        /// <returns>New fuel level in litres, clamped to a minimum of 0.</returns>
        public static double UpdateFuelLevel(double current, double fuelEfficiency, InstructionMode mode)
        {
            double statFactor = 1 - (fuelEfficiency / 100) * MaxFuelStatReduction;
            double consumed = BaseFuelPerLap * statFactor * ModeDegradationScale(mode);
            return Math.Max(0, current - consumed);
        }

        /// <summary>
        /// Returns the driver fatigue level after one lap (GDD §2).
        /// Buildup is reduced by high Comfort (car) and Stamina (driver); Push mode increases it.
        /// The fatigue counter resets on a driver swap; that reset belongs to PitStop.
        /// </summary>
        /// <param name="current">Current fatigue level (0 = fresh, 100 = fully fatigued).</param>
        /// <param name="comfort">Car's Comfort effective stat (0 to 100).</param>
        /// <param name="stamina">Driver's Stamina stat (0 to 100).</param>
        /// <param name="mode">Current driver instruction mode.</param>
        /// <returns>New fatigue level, clamped to a maximum of 100.</returns>
        public static double UpdateDriverFatigue(double current, double comfort, double stamina, InstructionMode mode)
        {
            double statFactor = (1 - (comfort / 100) * MaxComfortFatigueReduction)
                * (1 - (stamina / 100) * MaxStaminaFatigueReduction);
            double fatiguePerLap = BaseFatiguePerLap * statFactor * ModeDegradationScale(mode);
            return Math.Min(100, current + fatiguePerLap);
        }

        /// <summary>
        /// Returns the car condition after one lap (GDD §3).
        /// Decay increases with car age and is reduced by the Engineer skill; Push accelerates it.
        /// Condition cannot be restored mid-race; that is a between-races operation.
        /// </summary>
        /// <param name="current">Current condition (0 to 100).</param>
        /// <param name="carAge">Car's age in years (0 = new).</param>
        /// <param name="engineerSkill">Player's Engineer skill level (0 to 20; use 0 for AI teams).</param>
        /// <param name="mode">Current driver instruction mode.</param>
        /// <returns>New condition, clamped to a minimum of 0.</returns>
        public static double UpdateCarCondition(double current, int carAge, int engineerSkill, InstructionMode mode)
        {
            double ageFactor = Math.Min(MaxAgeFactor, 1 + carAge * AgeConditionRate);
            double engineerFactor = 1 - (engineerSkill / MaxEngineerSkill) * MaxEngineerReduction;
            double decayPerLap =
                BaseConditionDecayPerLap * ageFactor * engineerFactor * ModeDegradationScale(mode);
            return Math.Max(0, current - decayPerLap);
        }
    }
}
