#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>One car on the grid of the driven race, with everything the race needs from the career.</summary>
    public sealed record GridCar
    {
        public string TeamId { get; init; } = "";
        public string TeamName { get; init; } = "";
        public bool Player { get; init; }
        public double Prestige { get; init; }
        /// <summary>The team's race number this season, drawn on the car.</summary>
        public int RaceNumber { get; init; }
        public CarInstance Car { get; init; } = new CarInstance();
        public CarModel Model { get; init; } = new CarModel();
        /// <summary>Drivers in stint order, the starter first.</summary>
        public IReadOnlyList<RaceDriver> Lineup { get; init; } = Array.Empty<RaceDriver>();
        public int CrewSize { get; init; }
        /// <summary>The player's Engineer skill; 0 for AI teams.</summary>
        public int EngineerSkill { get; init; }
        public int TyreSets { get; init; }
        public int SpareParts { get; init; }
        /// <summary>A full tank of the car's effective capacity, litres.</summary>
        public double StartingFuel { get; init; }
    }

    /// <summary>The driven race's field, pole first, and the career it came from.</summary>
    public sealed record RaceSetup
    {
        public GameState Game { get; init; } = new GameState();
        public IReadOnlyList<GridCar> Grid { get; init; } = Array.Empty<GridCar>();
    }

    /// <summary>A stop made by a car in the driven race.</summary>
    public sealed record PitStopRecord
    {
        /// <summary>Laps completed when the car stopped.</summary>
        public int Lap { get; init; }
        /// <summary>Time stood in the box, race seconds.</summary>
        public double Seconds { get; init; }
        public double FuelAdded { get; init; }
        public bool TyresChanged { get; init; }
        public string DriverOut { get; init; } = "";
        public string DriverIn { get; init; } = "";
        public IReadOnlyList<string> FixedIssueIds { get; init; } = Array.Empty<string>();
    }

    /// <summary>An issue a car picked up in the driven race.</summary>
    public sealed record IssueRecord
    {
        public int Lap { get; init; }
        public string TemplateId { get; init; } = "";
        public string DriverId { get; init; } = "";
        /// <summary>The lap of the stop that fixed it, or null if it was never fixed.</summary>
        public int? FixedLap { get; init; }
    }

    /// <summary>A driver's stint, with its quickest lap.</summary>
    public sealed record StintRecord
    {
        public string DriverId { get; init; } = "";
        public string DriverName { get; init; } = "";
        public int StartLap { get; init; }
        public int EndLap { get; init; }
        /// <summary>Quickest lap of the stint, race seconds, or null if it ran none.</summary>
        public double? BestLap { get; init; }
    }

    /// <summary>One car's line in the final classification.</summary>
    public sealed record ClassifiedCar
    {
        public int Position { get; init; }
        public int ClassPosition { get; init; }
        public string TeamId { get; init; } = "";
        public string TeamName { get; init; } = "";
        public string CarId { get; init; } = "";
        public string ModelId { get; init; } = "";
        public CarClass CarClass { get; init; }
        public int Laps { get; init; }
        public bool Retired { get; init; }
        public FailureType? RetirementReason { get; init; }
        /// <summary>For a mechanical failure, the id of what broke (<see cref="IssueTemplates.Find"/>); else null.</summary>
        public string? FailureId { get; init; }
        public double? BestLap { get; init; }
        public double PrizeMoney { get; init; }
    }

    /// <summary>What the race summary shows: the results, and the player's own race.</summary>
    public sealed record RaceSummary
    {
        public int Year { get; init; }
        /// <summary>Every car, winner first.</summary>
        public IReadOnlyList<ClassifiedCar> Classification { get; init; } = Array.Empty<ClassifiedCar>();
        /// <summary>The player's car, or null if the player didn't race.</summary>
        public ClassifiedCar? Player { get; init; }
        public IReadOnlyList<StintRecord> Stints { get; init; } = Array.Empty<StintRecord>();
        public IReadOnlyList<PitStopRecord> PitStops { get; init; } = Array.Empty<PitStopRecord>();
        public IReadOnlyList<IssueRecord> Issues { get; init; } = Array.Empty<IssueRecord>();
        /// <summary>The player's overall position at the end of each of the leader's laps.</summary>
        public IReadOnlyList<int> PositionByLap { get; init; } = Array.Empty<int>();
        public double FuelUsed { get; init; }
        public double FuelCost { get; init; }
        public int TyreSetsUsed { get; init; }
        public int SparePartsUsed { get; init; }
        /// <summary>The car's condition after the race, 0 to 100.</summary>
        public double Condition { get; init; }
        public double PrestigeBefore { get; init; }
        public double PrestigeAfter { get; init; }
        /// <summary>Fastest lap of the race, or null if no laps ran.</summary>
        public FastestLap? FastestLap { get; init; }
    }
}
