#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // --- Player Skills ---

    public sealed record PlayerSkills
    {
        public int Driver { get; init; }   // 0 to 20, sets player character driver stats (level x 5)
        public int Engineer { get; init; } // 0 to 20, reduces pit duration + slows condition degradation
        public int Business { get; init; } // 0 to 20, discounts on purchases, improves sale prices
    }

    // --- Base Team (shared between player and AI) ---

    /// <summary>
    /// TS <c>BaseTeam</c>. TS <c>Team = PlayerTeam | AITeam</c> is this abstract record;
    /// TS <c>team.kind === "player"</c> becomes <c>team is PlayerTeam</c>.
    /// </summary>
    public abstract record BaseTeam
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public double Budget { get; init; }
        public double Prestige { get; init; }
        public IReadOnlyList<double> PrestigeHistory { get; init; } = Array.Empty<double>(); // prestige snapshot per year (index 0 = year 1)
        public int CrewSize { get; init; }                                                     // 0 to 16
        public IReadOnlyList<CarInstance> Cars { get; init; } = Array.Empty<CarInstance>();
        public IReadOnlyList<Contract> Contracts { get; init; } = Array.Empty<Contract>();
        public string? EnteredCarId { get; init; } // Which car is entered for the next/current race
        /// <summary>1 to 100, redone each season by <see cref="RaceNumbers"/>. Every car the team runs carries it.</summary>
        public int RaceNumber { get; init; }
    }

    // --- Player Team ---

    public sealed record PlayerTeam : BaseTeam
    {
        public string PlayerName { get; init; } = "";
        public string Nationality { get; init; } = ""; // ISO 3166-1 alpha-2 country code
        public string? Logo { get; init; }             // Data URL or asset path; a badge shape id from new game
        public string TeamColour { get; init; } = "";  // #rrggbb, picked at new game
        public PlayerSkills Skills { get; init; } = new PlayerSkills();
        /// <summary>Starting skill points left unspent at new game. They are gone for good.</summary>
        public int ForfeitedSkillPoints { get; init; }
        public int SpareParts { get; init; }           // Consumable units carried over
        public int TyreSets { get; init; }             // Tyre sets carried over
        public IReadOnlyList<TransactionRecord> Transactions { get; init; } = Array.Empty<TransactionRecord>();
        /// <summary>The player's driver ids in race order, starter first. Read it through <see cref="DriverLineup.Of"/>, which keeps it in step with the roster.</summary>
        public IReadOnlyList<string> Lineup { get; init; } = Array.Empty<string>();
    }

    // --- AI Team ---

    public sealed record AITeam : BaseTeam
    {
        /// <summary>The one car model this team ever runs, so its livery always fits. Empty means any model.</summary>
        public string ModelId { get; init; } = "";
        public TeamStatus Status { get; init; }
        public int SpareParts { get; init; }
        public int TyreSets { get; init; }
    }
}
