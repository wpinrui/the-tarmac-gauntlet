#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of the logic data in shared/dealerData.ts. UI labels are not ported.</summary>
    public static class DealerData
    {
        /// <summary>Crew cost per member per race (GDD §5: $2,000).</summary>
        public const double CrewCostPerMember = 2_000;

        /// <summary>Condition restored per spare part used in repair.</summary>
        public const double ConditionPerPart = 5;

        /// <summary>Sale price multiplier: player receives this fraction of the calculated sale value.</summary>
        public const double SalePriceMultiplier = 0.5;

        /// <summary>Shitbox model ID: the designated plot armour car.</summary>
        public const string ShitboxModelId = "f-01";

        /// <summary>Generates unique car instance IDs.</summary>
        private static long carIdCounter = 0;

        public static string NextCarId()
        {
            if (carIdCounter == 0) carIdCounter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return $"car-{++carIdCounter}";
        }
    }
}
