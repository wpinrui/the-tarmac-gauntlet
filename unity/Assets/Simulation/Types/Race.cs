#nullable enable
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // --- Instruction Mode ---

    public enum InstructionMode
    {
        Push,
        Normal,
        Conserve,
    }

    // --- Mechanical Issues ---

    public enum IssueSeverity
    {
        Minor,
        Medium,
        Major,
    }

    public enum IssueCategory
    {
        Mechanical,
        Crash,
    }

    public sealed record IssueTemplate
    {
        public string Id { get; init; } = "";
        public string Description { get; init; } = "";
        public IssueSeverity Severity { get; init; }
        public IssueCategory Category { get; init; }
        /// <summary>Fractional stat debuffs while active, e.g. { Power: 0.10 } = -10% power.</summary>
        public IReadOnlyDictionary<CarStatKey, double> StatDebuffs { get; init; } = new Dictionary<CarStatKey, double>();
        /// <summary>Relative probability weight for weighted pick when an issue triggers.</summary>
        public double Weight { get; init; }
        /// <summary>Spare parts consumed to fix.</summary>
        public int SparePartsCost { get; init; }
        /// <summary>Base work units to fix, scaled by crew size and engineer skill in pit stop.</summary>
        public double WorkUnits { get; init; }
    }

    public sealed record ActiveIssue
    {
        public string TemplateId { get; init; } = "";
        public int LapOccurred { get; init; }
    }

    // --- Pit Stop Configuration ---

    public sealed record PitStopConfig
    {
        public double FuelToAdd { get; init; }            // Litres to add (0 = no refuel)
        public bool ChangeTyres { get; init; }            // Whether to change tyres (consumes 1 tyre set)
        public string? NextDriverId { get; init; }        // Driver to swap to (null = keep current)
        public IReadOnlyList<string>? IssueIdsToFix { get; init; } // Template IDs of issues to fix this stop (null or empty = fix nothing)
    }
}
