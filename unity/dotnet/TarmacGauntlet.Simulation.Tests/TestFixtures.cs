#nullable enable
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/testFixtures.ts.</summary>
    public static class TestFixtures
    {
        /// <summary>
        /// Minimal CarLapSnapshot for tests that only care about lap count and timing.
        /// All resource fields are zero-filled. Pass real values when a test exercises
        /// fuel/wear/condition logic.
        /// </summary>
        public static CarLapSnapshot Snap(int lapsCompleted, double totalTime) => new CarLapSnapshot
        {
            LapsCompleted = lapsCompleted,
            TotalTime = totalTime,
            TyreWear = 0,
            FuelRemaining = 0,
            FuelCapacity = 0,
            Condition = 100,
            CurrentDriverId = "d",
            DriverFatigue = new Dictionary<string, double>(),
            InstructionMode = InstructionMode.Normal,
            ActiveIssues = new ActiveIssue[0],
            TyreSetsAvailable = 0,
            SparePartsAvailable = 0,
        };
    }
}
