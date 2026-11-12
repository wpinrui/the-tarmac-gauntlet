#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/lapTime.ts.</summary>
    public static class LapTime
    {
        // ---------------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------------

        /// <summary>Lap time for a car with maximum possible effective stats (seconds).</summary>
        private const double FastestLapTime = 26.0;
        /// <summary>Additional seconds added as stats approach zero. Defines the spread across the field.</summary>
        private const double LapTimeSpread = 20.0;

        /// <summary>Power contributes 60% and handling 40% to the combined speed score.</summary>
        private const double PowerWeight = 0.6;
        private const double HandlingWeight = 0.4;

        /// <summary>Driver pace at which the pace modifier equals 1.0 (no adjustment).</summary>
        private const double ReferencePace = 50;
        /// <summary>
        /// Fractional lap time change per pace point above or below the reference.
        /// At 0.002: a 100-Pace driver is 10% faster than a 0-Pace driver.
        /// </summary>
        private const double PaceEffectPerPoint = 0.002;

        /// <summary>At 100% fatigue, driver loses this fraction of their effective pace.</summary>
        private const double FatiguePaceLoss = 0.3;
        /// <summary>At 100% fatigue, driver loses this fraction of their effective consistency.</summary>
        private const double FatigueConsistencyLoss = 0.5;

        /// <summary>Flat lap time multipliers per instruction mode.</summary>
        private static double ModeLapTimeModifier(InstructionMode mode) => mode switch
        {
            InstructionMode.Push => 0.97,
            InstructionMode.Normal => 1.0,
            InstructionMode.Conserve => 1.03,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        /// <summary>Maximum fractional lap time penalty from fully worn tyres (100% wear).</summary>
        private const double MaxTyreWearPenalty = 0.10;

        /// <summary>
        /// Fractional lap time penalty per litre of fuel on board. Calibrated for the
        /// time-bounded race: at full ~70 L tank a class-A car runs ~3.5 % slower than
        /// empty, which is meaningful but doesn't crowd out the 1440 s lap budget.
        /// </summary>
        private const double FuelPenaltyPerLitre = 0.0005;

        /// <summary>
        /// Maximum lap time variance (± seconds) at 0% consistency, Normal mode, 0% fatigue.
        /// Actual range scales down with higher consistency and Conserve mode.
        /// </summary>
        private const double MaxVarianceSeconds = 2.0;

        /// <summary>Variance multiplier per instruction mode (Push spreads variance; Conserve tightens it).</summary>
        private static double ModeVarianceScale(InstructionMode mode) => mode switch
        {
            InstructionMode.Push => 1.5,
            InstructionMode.Normal => 1.0,
            InstructionMode.Conserve => 0.5,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        /// <summary>A driver's pace once fatigue (0 to 100) has worn it down.</summary>
        public static double EffectivePace(double pace, double fatigue) => pace * (1 - (fatigue / 100) * FatiguePaceLoss);

        /// <summary>A driver's consistency once fatigue (0 to 100) has worn it down.</summary>
        public static double EffectiveConsistency(double consistency, double fatigue) =>
            consistency * (1 - (fatigue / 100) * FatigueConsistencyLoss);

        /// <summary>Lap time multiplier for a driver of this pace: 1 at the reference pace, below 1 above it.</summary>
        public static double PaceModifier(double pace) => 1.0 - (pace - ReferencePace) * PaceEffectPerPoint;

        /// <summary>
        /// How much longer a lap takes for a driver of this pace at this fatigue (0 to 100) than fresh:
        /// 1 when fresh, about 1.05 for a Pace 80 driver fully tired.
        /// </summary>
        public static double FatigueLapFactor(double pace, double fatigue) =>
            PaceModifier(EffectivePace(pace, fatigue)) / PaceModifier(pace);

        /// <summary>The car's lap time before the driver, mode, tyres, fuel and variance: set by its Power and Handling.</summary>
        public static double BaseLapTime(CarStats effectiveStats)
        {
            double speedScore =
                effectiveStats.Power * PowerWeight + effectiveStats.Handling * HandlingWeight;
            double speedFraction = Math.Max(0, Math.Min(1, speedScore / 100));
            return FastestLapTime + (1 - speedFraction) * LapTimeSpread;
        }

        /// <summary>
        /// Calculates a single lap time for a car (GDD §2).
        ///
        /// Formula:
        ///   base_lap_time (from effectiveStats power + handling)
        ///   × driver_pace_modifier (pace stat, adjusted for fatigue)
        ///   × instruction_mode_modifier (Push &lt; 1.0, Normal = 1.0, Conserve &gt; 1.0)
        ///   × tyre_wear_modifier (worn tyres = slower)
        ///   × fuel_load_modifier (heavier fuel load = slower)
        ///   + random_variance (scaled by consistency and instruction mode)
        ///
        /// Design note on car_condition_modifier:
        ///   The GDD formula includes a car_condition_modifier. Since effectiveStats is
        ///   the output of CalculateEffectiveStats, which already applies the condition
        ///   fraction to power and handling, the condition effect is captured implicitly
        ///   in base_lap_time. Applying it again here would double-count it.
        /// </summary>
        /// <param name="effectiveStats">Car stats after upgrades, age, and condition (from CalculateEffectiveStats).</param>
        /// <param name="driverStats">Driver's base stats (pace, consistency, etc., 0 to 100).</param>
        /// <param name="driverFatigue">Driver's current fatigue level (0 to 100).</param>
        /// <param name="instructionMode">Current driver instruction mode.</param>
        /// <param name="tyreWear">Current tyre wear (0 = fresh, 100 = fully worn).</param>
        /// <param name="fuelLoad">Fuel remaining in litres.</param>
        /// <param name="random">Injectable random source; null uses <see cref="DefaultRandom"/>.</param>
        /// <returns>Lap time in seconds.</returns>
        public static double CalculateLapTime(
            CarStats effectiveStats,
            DriverStats driverStats,
            double driverFatigue,
            InstructionMode instructionMode,
            double tyreWear,
            double fuelLoad,
            Func<double>? random = null)
        {
            random ??= DefaultRandom.Next;

            // 1. Base lap time: higher combined speed score produces a shorter lap time.
            double baseTime = BaseLapTime(effectiveStats);

            // 2. Driver pace modifier: fatigue reduces the effective pace before applying.
            double paceMod = PaceModifier(EffectivePace(driverStats.Pace, driverFatigue));

            // 3. Instruction mode multiplier.
            double modeMod = ModeLapTimeModifier(instructionMode);

            // 4. Tyre wear multiplier: increases linearly with wear.
            double tyreMod = 1.0 + (tyreWear / 100) * MaxTyreWearPenalty;

            // 5. Fuel load multiplier: each litre adds a small fraction.
            double fuelMod = 1.0 + fuelLoad * FuelPenaltyPerLitre;

            // 6. Random variance: fatigue reduces effective consistency, widening variance.
            double effectiveConsistency = EffectiveConsistency(driverStats.Consistency, driverFatigue);
            double varianceRange =
                MaxVarianceSeconds *
                (1 - effectiveConsistency / 100) *
                ModeVarianceScale(instructionMode);
            double variance = (random() - 0.5) * 2 * varianceRange;

            return baseTime * paceMod * modeMod * tyreMod * fuelMod + variance;
        }
    }
}
