#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // --- Prize Money ---

    public sealed record PrizeScheduleEntry
    {
        public int Position { get; init; }  // 1 to 100
        public double Amount { get; init; } // Prize money in dollars
    }

    // --- Transactions ---

    public enum TransactionCategory
    {
        CarPurchase,
        CarSale,
        Upgrade,
        SpareParts,
        Tyres,
        DriverSalary,
        DriverBuyout,
        CrewCost,
        PrizeMoney,
        FuelCost,
    }

    public sealed record TransactionRecord
    {
        public int Year { get; init; }
        public TransactionCategory Category { get; init; }
        public double Amount { get; init; } // positive = income, negative = expense
        public string Description { get; init; } = "";
        public string? DriverId { get; init; } // the driver the description names, or null
    }

    // --- Car Markets ---

    public sealed record NewCarListing
    {
        public string ModelId { get; init; } = "";
        public double Price { get; init; } // Always list price for new cars
    }

    public sealed record UsedCarListing
    {
        public string Id { get; init; } = "";
        public string ModelId { get; init; } = "";
        public int Age { get; init; }
        public double Condition { get; init; }
        public InstalledUpgrades InstalledUpgrades { get; init; } = new InstalledUpgrades();
        public double Price { get; init; } // Discounted based on age/condition/upgrades
    }

    public sealed record CarMarket
    {
        public IReadOnlyList<NewCarListing> NewListings { get; init; } = Array.Empty<NewCarListing>();
        public IReadOnlyList<UsedCarListing> UsedListings { get; init; } = Array.Empty<UsedCarListing>(); // Rotates each year
    }

    // --- Consumable Costs (scale by car tier) ---

    public sealed record TierCosts
    {
        public CarTier Tier { get; init; }
        public double SparePartCost { get; init; } // Cost per unit of spare parts
        public double TyreSetCost { get; init; }   // Cost per tyre set
    }

    // --- Fuel ---

    public sealed record FuelConfig
    {
        public double CostPerLitre { get; init; }
    }

    // --- Economy Config (all configurable rates) ---

    /// <summary>TS inline type <c>EconomyConfig["contractDiscounts"]</c>.</summary>
    public sealed record ContractDiscounts
    {
        public double TwoYear { get; init; }   // 0.10 (10%)
        public double ThreeYear { get; init; } // 0.20 (20%)
    }

    public sealed record EconomyConfig
    {
        public IReadOnlyList<TierCosts> TierCosts { get; init; } = Array.Empty<TierCosts>();
        public FuelConfig FuelConfig { get; init; } = new FuelConfig();
        public double CrewCostPerMember { get; init; } // $2,000 per GDD
        public ContractDiscounts ContractDiscounts { get; init; } = new ContractDiscounts();
        public double BuyoutMultiplier { get; init; } // 1.5 per GDD
    }
}
