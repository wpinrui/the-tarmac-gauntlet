#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // --- Car Stats ---

    public sealed record CarStats
    {
        public double Power { get; init; }          // Straight-line speed (higher = faster)
        public double Handling { get; init; }       // Cornering ability (higher = faster)
        public double FuelEfficiency { get; init; } // Fuel consumed per lap (higher = less fuel)
        public double TyreDurability { get; init; } // Tyre wear per lap (higher = less wear)
        public double Comfort { get; init; }        // Driver fatigue rate (higher = slower fatigue)
        public double Reliability { get; init; }    // Issue/failure resistance (higher = fewer problems)
        public double PitStopTime { get; init; }    // Base pit stop duration (LOWER = faster)
        public double FuelCapacity { get; init; }   // Max litres of fuel (higher = more fuel)

        /// <summary>C# stand-in for TS <c>stats[key]</c>.</summary>
        public double Get(CarStatKey key) => key switch
        {
            CarStatKey.Power => Power,
            CarStatKey.Handling => Handling,
            CarStatKey.FuelEfficiency => FuelEfficiency,
            CarStatKey.TyreDurability => TyreDurability,
            CarStatKey.Comfort => Comfort,
            CarStatKey.Reliability => Reliability,
            CarStatKey.PitStopTime => PitStopTime,
            CarStatKey.FuelCapacity => FuelCapacity,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        /// <summary>C# stand-in for TS <c>{ ...stats, [key]: value }</c>.</summary>
        public CarStats With(CarStatKey key, double value) => key switch
        {
            CarStatKey.Power => this with { Power = value },
            CarStatKey.Handling => this with { Handling = value },
            CarStatKey.FuelEfficiency => this with { FuelEfficiency = value },
            CarStatKey.TyreDurability => this with { TyreDurability = value },
            CarStatKey.Comfort => this with { Comfort = value },
            CarStatKey.Reliability => this with { Reliability = value },
            CarStatKey.PitStopTime => this with { PitStopTime = value },
            CarStatKey.FuelCapacity => this with { FuelCapacity = value },
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    /// <summary>TS <c>keyof CarStats</c>, in declaration order.</summary>
    public enum CarStatKey
    {
        Power,
        Handling,
        FuelEfficiency,
        TyreDurability,
        Comfort,
        Reliability,
        PitStopTime,
        FuelCapacity,
    }

    // --- Upgrade Packs ---

    public enum UpgradePackType
    {
        Power,
        Handling,
        Comfort,
    }

    public sealed record UpgradePackDefinition
    {
        public UpgradePackType Type { get; init; }
        public double Cost { get; init; }
    }

    // --- Car Classes ---

    public enum CarClass
    {
        F,
        E,
        D,
        C,
        B,
        A,
        F1,
    }

    /// <summary>Deprecated: use CarClass instead. Kept for economy config compatibility.</summary>
    public enum CarTier
    {
        Junkyard,
        DailyDriver,
        UsedSports,
        TrackCar,
        GtRaceCar,
        Supercar,
        Hypercar,
    }

    // --- Car Model (template) ---

    public sealed record CarModel
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public CarClass CarClass { get; init; }
        public CarTier Tier { get; init; }
        public double Price { get; init; }
        public CarStats BaseStats { get; init; } = new CarStats();
        public CarStats PotentialStats { get; init; } = new CarStats();
        public IReadOnlyList<UpgradePackDefinition> UpgradePacks { get; init; } = Array.Empty<UpgradePackDefinition>();
    }

    // --- Car Instance (owned by a team) ---

    public sealed record InstalledUpgrades
    {
        public bool Power { get; init; }
        public bool Handling { get; init; }
        public bool Comfort { get; init; }
    }

    public sealed record CarInstance
    {
        public string Id { get; init; } = "";
        public string ModelId { get; init; } = "";
        public int Age { get; init; }          // Years since purchase (0 = new)
        public double Condition { get; init; } // 0 to 100%
        public InstalledUpgrades InstalledUpgrades { get; init; } = new InstalledUpgrades();
    }
}
