#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    // --- Driver Stats ---

    public sealed record DriverStats
    {
        public double Pace { get; init; }        // Lap time modifier (0 to 100, higher = faster)
        public double Consistency { get; init; } // Lap time variance (0 to 100, higher = more predictable)
        public double Stamina { get; init; }     // Fatigue buildup rate (0 to 100, higher = slower fatigue)
        public double Safety { get; init; }      // Crash/failure probability (0 to 100, higher = fewer incidents)
        public double Smoothness { get; init; }  // Tyre wear rate (0 to 100, higher = less wear)

        /// <summary>C# stand-in for TS <c>stats[key]</c>.</summary>
        public double Get(DriverStatKey key) => key switch
        {
            DriverStatKey.Pace => Pace,
            DriverStatKey.Consistency => Consistency,
            DriverStatKey.Stamina => Stamina,
            DriverStatKey.Safety => Safety,
            DriverStatKey.Smoothness => Smoothness,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        /// <summary>C# stand-in for TS <c>{ ...stats, [key]: value }</c>.</summary>
        public DriverStats With(DriverStatKey key, double value) => key switch
        {
            DriverStatKey.Pace => this with { Pace = value },
            DriverStatKey.Consistency => this with { Consistency = value },
            DriverStatKey.Stamina => this with { Stamina = value },
            DriverStatKey.Safety => this with { Safety = value },
            DriverStatKey.Smoothness => this with { Smoothness = value },
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    /// <summary>TS <c>keyof DriverStats</c>, in declaration order.</summary>
    public enum DriverStatKey
    {
        Pace,
        Consistency,
        Stamina,
        Safety,
        Smoothness,
    }

    // --- Sinusoidal Curve Parameters ---
    // Each driver's stats follow a sinusoidal curve peaking around age 30.
    // Individual stats can have small phase offsets so no two same-age drivers are identical.

    public sealed record DriverCurveParams
    {
        public double PeakAge { get; init; }                               // Typically ~30
        public DriverStats PeakStats { get; init; } = new DriverStats();    // Stats at peak
        public DriverStats PhaseOffsets { get; init; } = new DriverStats(); // Per-stat phase offset (years, small values)
    }

    // --- Driver ---

    public sealed record Driver
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Nationality { get; init; } = ""; // ISO 3166-1 alpha-2 country code (e.g. "br", "jp", "gb")
        public int Age { get; init; }
        public DriverCurveParams CurveParams { get; init; } = new DriverCurveParams();
        public double MarketValue { get; init; }
    }

    // --- Contract ---

    public sealed record Contract
    {
        public string DriverId { get; init; } = "";
        public string TeamId { get; init; } = "";
        /// <summary>TS <c>ContractLength</c>: 1, 2 or 3.</summary>
        public int Length { get; init; }
        public int RemainingYears { get; init; }
        public double AnnualSalary { get; init; }
    }
}
