#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/effectiveStats.ts.</summary>
    public static class EffectiveStats
    {
        // Age reduces all stats gradually. 2% per year, capped at 50% (reached at age 25).
        private const double AgePenaltyPerYear = 0.02;
        private const double MaxAgePenalty = 0.5;

        // Prevents pitStopTime from reaching infinity when condition approaches 0.
        private const double MinCombinedMultiplier = 0.1;

        /// <summary>
        /// Returns the effective stats for a car at a given age and condition.
        ///
        /// Calculation chain (per stat):
        ///   1. Start with base value; use potential if the relevant upgrade pack is installed.
        ///   2. Multiply by the age fraction (1 - agePenalty), capped.
        ///   3. Multiply by condition fraction (condition / 100).
        ///
        /// pitStopTime is inverted (lower = better), so the combined multiplier is applied as a
        /// divisor instead: lower condition and greater age both increase the pit stop duration.
        ///
        /// Upgrade pack to stat mapping (GDD §3):
        ///   Power Pack    : power, fuelEfficiency
        ///   Handling Pack : handling, tyreDurability
        ///   Comfort Pack  : comfort
        ///   (reliability, pitStopTime, fuelCapacity have no upgrade pack; potential equals base)
        /// </summary>
        public static CarStats CalculateEffectiveStats(CarInstance instance, CarModel model)
        {
            var b = model.BaseStats;
            var p = model.PotentialStats;
            var u = instance.InstalledUpgrades;

            // Step 1: resolve upgrade-adjusted values
            double power = u.Power ? p.Power : b.Power;
            double fuelEfficiency = u.Power ? p.FuelEfficiency : b.FuelEfficiency;
            double handling = u.Handling ? p.Handling : b.Handling;
            double tyreDurability = u.Handling ? p.TyreDurability : b.TyreDurability;
            double comfort = u.Comfort ? p.Comfort : b.Comfort;
            double reliability = b.Reliability;
            double pitStopTime = b.PitStopTime;
            double fuelCapacity = b.FuelCapacity;

            // Step 2: combined multiplier from age and condition
            double agePenalty = Math.Min(MaxAgePenalty, instance.Age * AgePenaltyPerYear);
            double ageFraction = 1 - agePenalty;
            double conditionFraction = instance.Condition / 100;
            double combined = conditionFraction * ageFraction;

            // Step 3: apply to each stat
            return new CarStats
            {
                Power = power * combined,
                FuelEfficiency = fuelEfficiency * combined,
                Handling = handling * combined,
                TyreDurability = tyreDurability * combined,
                Comfort = comfort * combined,
                Reliability = reliability * combined,
                // pitStopTime is inverted: degraded cars take longer in the pits.
                PitStopTime = pitStopTime / Math.Max(MinCombinedMultiplier, combined),
                FuelCapacity = fuelCapacity * combined,
            };
        }
    }
}
