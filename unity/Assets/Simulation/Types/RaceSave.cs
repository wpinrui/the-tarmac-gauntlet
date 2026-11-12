#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A driven race saved part way, from its pause menu: the race's set-up, its clocks and weather, and every car where
    /// it was. Continuing the career races on from here. Saved before the green, it starts the race again.
    /// </summary>
    public sealed record RaceSave
    {
        public RaceSetup Setup { get; init; } = new RaceSetup();
        /// <summary>False when saved before the green: the race starts again from its start.</summary>
        public bool Started { get; init; }
        /// <summary>The race clock, race seconds.</summary>
        public float RaceTime { get; init; }
        /// <summary>Seconds since the green, as the race director counts them for lap times and the classification.</summary>
        public float Elapsed { get; init; }
        /// <summary>Seconds since the race time ran out, or null while it hasn't.</summary>
        public float? SinceTimeUp { get; init; }
        public float StartHour { get; init; }
        public int WeatherSeed { get; init; }
        /// <summary>The kind of weather the race was asked for, or null for the one its seed rolled.</summary>
        public int? WeatherKind { get; init; }
        /// <summary>True while the player drove their car rather than the AI.</summary>
        public bool PlayerDriving { get; init; }
        /// <summary>The race's own log, the field in grid order.</summary>
        public RaceDayState Day { get; init; } = new RaceDayState();
        /// <summary>Each car on track, in grid order.</summary>
        public IReadOnlyList<CarSave> Cars { get; init; } = Array.Empty<CarSave>();
    }

    /// <summary>One car on track in a <see cref="RaceSave"/>.</summary>
    public sealed record CarSave
    {
        /// <summary>Lap distance, m, and metres left of the track centre.</summary>
        public float Distance { get; init; }
        public float Lateral { get; init; }
        /// <summary>Speed along the track, m/s.</summary>
        public float Speed { get; init; }
        public int Laps { get; init; }
        /// <summary>True while the car was in the pit lane: it goes back on track at the pit exit.</summary>
        public bool InPitLane { get; init; }
        /// <summary>Seconds since the car last crossed the line, for its lap time.</summary>
        public float SinceLine { get; init; }
        public float Fuel { get; init; }
        public float Wear { get; init; }
        public int Map { get; init; }
        public bool PitRequested { get; init; }
        public float? PitLitres { get; init; }
        public bool PitTyres { get; init; } = true;
        /// <summary>Where a retired car stands and the way it faces, x y z.</summary>
        public float[]? Position { get; init; }
        public float[]? Forward { get; init; }
    }

    /// <summary>A <see cref="RaceDay"/> as it stood, to carry on from.</summary>
    public sealed record RaceDayState
    {
        public IReadOnlyList<CarRaceLogState> Cars { get; init; } = Array.Empty<CarRaceLogState>();
        public FastestLap? FastestLap { get; init; }
        public IReadOnlyList<IReadOnlyList<int>> Positions { get; init; } = Array.Empty<IReadOnlyList<int>>();
    }

    /// <summary>A <see cref="CarRaceLog"/> as it stood, to carry on from.</summary>
    public sealed record CarRaceLogState
    {
        public int Laps { get; init; }
        public double Condition { get; init; }
        public int TyreSets { get; init; }
        public int SpareParts { get; init; }
        public bool Retired { get; init; }
        public FailureType? RetirementReason { get; init; }
        public double LastLineTime { get; init; }
        public bool Flagged { get; init; }
        public double FuelAdded { get; init; }
        public int DriverIndex { get; init; }
        public int NextDriverIndex { get; init; }
        public double? BestLap { get; init; }
        public double FuelLeft { get; init; }
        public double Fatigue { get; init; }
        public ModeCounter Modes { get; init; } = new ModeCounter();
        public IReadOnlyList<ActiveIssue> ActiveIssues { get; init; } = Array.Empty<ActiveIssue>();
        public IReadOnlyList<IssueRecord> Issues { get; init; } = Array.Empty<IssueRecord>();
        public IReadOnlyList<PitStopRecord> PitStops { get; init; } = Array.Empty<PitStopRecord>();
        public IReadOnlyList<double> LapTimes { get; init; } = Array.Empty<double>();
        public IReadOnlyList<Stint> Stints { get; init; } = Array.Empty<Stint>();
    }
}
