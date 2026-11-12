#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/effectiveStats.ts.</summary>
    public static class EffectiveStats
    {
        // Age wears stats gradually: 0.4% per year, capped at 10% (reached at age 25).
        private const double AgePenaltyPerYear = 0.004;
        private const double MaxAgePenalty = 0.1;

        // Condition wears stats linearly: a car at 0% condition loses 20%, one at 30% loses 14%.
        private const double MaxConditionPenalty = 0.2;

        /// <summary>
        /// Returns the effective stats for a car at a given age and condition.
        ///
        /// Calculation chain (per stat):
        ///   1. Start with base value; use potential if the relevant upgrade pack is installed.
        ///   2. Subtract the age penalty and the condition penalty from 1. Together they take at most 30%,
        ///      so an old wreck is clearly slower but can still race.
        ///   3. Multiply each stat by that.
        ///
        /// pitStopTime is inverted (lower = better), so the combined multiplier is applied as a
        /// divisor instead: lower condition and greater age both increase the pit stop duration.
        /// fuelCapacity is the size of the tank, which wear does not change.
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
            double conditionPenalty = MaxConditionPenalty * (1 - Math.Max(0, Math.Min(100, instance.Condition)) / 100);
            double combined = 1 - agePenalty - conditionPenalty;

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
                PitStopTime = pitStopTime / combined,
                FuelCapacity = fuelCapacity,
            };
        }
    }
}
