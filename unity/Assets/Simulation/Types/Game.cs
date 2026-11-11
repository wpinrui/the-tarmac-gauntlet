#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // --- Race History ---

    public sealed record RaceResult
    {
        public string TeamId { get; init; } = "";
        public string CarId { get; init; } = "";
        public CarClass CarClass { get; init; }
        public int Position { get; init; }
        public int LapsCompleted { get; init; }
        public double PrizeMoney { get; init; }
        public bool Retired { get; init; }
        /// <summary>Ran to the end but is no finisher: too few laps, or too slow a flag lap (<see cref="RaceDay.Finished"/>).</summary>
        public bool Unclassified { get; init; }

        /// <summary>A finisher: neither retired nor unclassified.</summary>
        public bool Finished() => !Retired && !Unclassified;
    }

    /// <summary>A driver stint within a single race.</summary>
    public sealed record Stint
    {
        public string DriverId { get; init; } = "";
        public int StartLap { get; init; }
        public int EndLap { get; init; }
    }

    /// <summary>Lap counts per instruction mode for a single car.</summary>
    public sealed record ModeCounter
    {
        public int Push { get; init; }
        public int Normal { get; init; }
        public int Conserve { get; init; }
    }

    /// <summary>TS inline union <c>RaceEvent["type"]</c>.</summary>
    public enum RaceEventType
    {
        Retirement,
        Issue,
        PitStop,
        Lapped,
        ClassLeadChange,
        FastestLap,
    }

    /// <summary>An endurance-relevant race event.</summary>
    public sealed record RaceEvent
    {
        public int Lap { get; init; }
        public RaceEventType Type { get; init; }
        public string Text { get; init; } = "";
        public string CarId { get; init; } = "";
        public string TeamId { get; init; } = "";
    }

    /// <summary>TS inline type <c>RaceHistoryEntry["fastestLap"]</c>.</summary>
    public sealed record FastestLap
    {
        public string TeamId { get; init; } = "";
        public double Time { get; init; }
    }

    public sealed record RaceHistoryEntry
    {
        public int Year { get; init; }
        public IReadOnlyList<RaceResult> Results { get; init; } = Array.Empty<RaceResult>();
        public FastestLap? FastestLap { get; init; }
        /// <summary>Optional rich data, stripped after the rolling window (default 3 years).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<CarLapSnapshot>>? LapSnapshots { get; init; }
        public IReadOnlyList<IReadOnlyList<int>>? PositionHistory { get; init; } // PositionHistory[lap][carIndex] = position
        /// <summary>Maps carId to its slot in PositionHistory[lap]. Required to read a specific car's positions.</summary>
        public IReadOnlyDictionary<string, int>? CarIndexById { get; init; }
        public IReadOnlyList<RaceEvent>? Events { get; init; }
        public IReadOnlyDictionary<string, IReadOnlyList<Stint>>? Stints { get; init; }
        public IReadOnlyDictionary<string, ModeCounter>? ModeCounters { get; init; }
    }

    // --- Save Metadata ---

    public sealed record SaveMetadata
    {
        public string CreatedAt { get; init; } = "";   // ISO 8601
        public string LastSavedAt { get; init; } = ""; // ISO 8601
        public int Version { get; init; }              // Save format version for migration
    }

    // --- Game Phase ---

    public enum GamePhase
    {
        NewGame,  // Character/team creation
        PreRace,  // Between-race management screens
        Race,     // Race in progress
        PostRace, // Race summary / newspaper
    }

    // --- Game State ---

    public sealed record GameState
    {
        public GamePhase Phase { get; init; }
        public int CurrentYear { get; init; } // Starts at 1
        public SaveMetadata Meta { get; init; } = new SaveMetadata();

        // Entities
        public IReadOnlyList<CarModel> CarModels { get; init; } = Array.Empty<CarModel>(); // ~20 templates (immutable reference data)
        public IReadOnlyList<Driver> Drivers { get; init; } = Array.Empty<Driver>();       // Full pool of 310
        public IReadOnlyList<Contract> Contracts { get; init; } = Array.Empty<Contract>(); // All active contracts across all teams
        public IReadOnlyList<BaseTeam> Teams { get; init; } = Array.Empty<BaseTeam>();     // All 100 teams (1 player + 99 AI)

        // Economy
        public EconomyConfig EconomyConfig { get; init; } = new EconomyConfig();
        public CarMarket CarMarket { get; init; } = new CarMarket();

        // Track
        public Track Track { get; init; } = new Track();

        // History
        public IReadOnlyList<RaceHistoryEntry> RaceHistory { get; init; } = Array.Empty<RaceHistoryEntry>();

        /// <summary>The paper printed after the latest race, or null before the first.</summary>
        public Newspaper? Newspaper { get; init; }
    }
}
