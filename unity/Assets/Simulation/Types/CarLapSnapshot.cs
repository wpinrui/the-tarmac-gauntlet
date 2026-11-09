#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// TS <c>CarLapSnapshot</c> from simulation/raceLoop.ts. Ported early because
    /// GameState's race history and the test fixtures need it.
    /// </summary>
    public sealed record CarLapSnapshot
    {
        public int LapsCompleted { get; init; }
        public double TotalTime { get; init; }
        public double TyreWear { get; init; }
        public double FuelRemaining { get; init; }
        /// <summary>Effective fuel capacity this lap (condition-adjusted).</summary>
        public double FuelCapacity { get; init; }
        public double Condition { get; init; }
        public string CurrentDriverId { get; init; } = "";
        public IReadOnlyDictionary<string, double> DriverFatigue { get; init; } = new Dictionary<string, double>();
        public InstructionMode InstructionMode { get; init; }
        public IReadOnlyList<ActiveIssue> ActiveIssues { get; init; } = Array.Empty<ActiveIssue>();
        public int TyreSetsAvailable { get; init; }
        public int SparePartsAvailable { get; init; }
    }
}
