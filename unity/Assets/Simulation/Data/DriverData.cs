#nullable enable
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of the logic data in shared/driverData.ts. UI labels are not ported.</summary>
    public static class DriverData
    {
        /// <summary>The 5 driver stats.</summary>
        public static readonly IReadOnlyList<DriverStatKey> DriverStatKeys = new[]
        {
            DriverStatKey.Pace,
            DriverStatKey.Consistency,
            DriverStatKey.Stamina,
            DriverStatKey.Safety,
            DriverStatKey.Smoothness,
        };
    }
}
