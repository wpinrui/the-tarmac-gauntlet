#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>All data an AI team needs to make spending decisions.</summary>
    public sealed record AiSpendingContext
    {
        /// <summary>The AI team making spending decisions.</summary>
        public AITeam Team { get; init; } = new AITeam();
        /// <summary>All active contracts across all teams (used to identify free agents).</summary>
        public IReadOnlyList<Contract> AllContracts { get; init; } = Array.Empty<Contract>();
        /// <summary>Full driver pool (used to look up free agents).</summary>
        public IReadOnlyList<Driver> AllDrivers { get; init; } = Array.Empty<Driver>();
        /// <summary>Full car model catalogue.</summary>
        public IReadOnlyList<CarModel> CarModels { get; init; } = Array.Empty<CarModel>();
        /// <summary>Current year's used car inventory.</summary>
        public IReadOnlyList<UsedCarListing> UsedListings { get; init; } = Array.Empty<UsedCarListing>();
        /// <summary>Generates a unique ID for a newly purchased car instance.</summary>
        public Func<string> NewCarId { get; init; } = DealerData.NextCarId;
    }

    public sealed record AiSpendingResult
    {
        /// <summary>Updated AI team after all spending decisions.</summary>
        public AITeam UpdatedTeam { get; init; } = new AITeam();
        /// <summary>New contracts created by this team during the spending round.</summary>
        public IReadOnlyList<Contract> NewContracts { get; init; } = Array.Empty<Contract>();
    }

    /// <summary>Port of simulation/aiSpending.ts.</summary>
    public static class AiSpending
    {
        // ---------------------------------------------------------------------------
        // Tunable constants
        // ---------------------------------------------------------------------------

        /// <summary>AI teams always aim to field exactly this many drivers.</summary>
        private const int AiTargetDriverCount = 3;
        /// <summary>Number of spare parts AI buys per spending iteration.</summary>
        private const int AiSparePartsPerBuy = 10;
        /// <summary>Number of tyre sets AI buys per spending iteration.</summary>
        private const int AiTyreSetsPerBuy = 5;
        /// <summary>Cost per spare part unit for AI teams.</summary>
        private const double AiSparePartCost = 100;
        /// <summary>Cost per tyre set for AI teams (average across tiers).</summary>
        private const double AiTyreSetCost = 500;
        /// <summary>Maximum spare parts an AI team will accumulate.</summary>
        private const int AiMaxSpareParts = 50;
        /// <summary>Maximum tyre sets an AI team will accumulate.</summary>
        private const int AiMaxTyreSets = 30;
        /// <summary>Score bonus for filling a mandatory empty driver slot (ensures it ranks above upgrades).</summary>
        private const double EmptySlotBonus = 10_000;

        // ---------------------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------------------

        /// <summary>Returns the IDs of drivers currently under contract with a given team.</summary>
        private static List<string> ContractedDriverIds(string teamId, IReadOnlyList<Contract> contracts) =>
            contracts.Where(c => c.TeamId == teamId && c.RemainingYears > 0).Select(c => c.DriverId).ToList();

        /// <summary>Returns drivers not under contract with any team (free agents).</summary>
        private static IEnumerable<Driver> FreeAgents(IReadOnlyList<Driver> allDrivers, IReadOnlyList<Contract> allContracts)
        {
            var contracted = new HashSet<string>(allContracts.Where(c => c.RemainingYears > 0).Select(c => c.DriverId));
            return allDrivers.Where(d => !contracted.Contains(d.Id));
        }

        /// <summary>
        /// Performance score for a car: sum of power + handling, using potential stats for
        /// upgraded packs and base stats otherwise. Not degraded by age or condition.
        /// </summary>
        private static double CarScore(CarModel model, InstalledUpgrades upgrades)
        {
            var power = upgrades.Power ? model.PotentialStats.Power : model.BaseStats.Power;
            var handling = upgrades.Handling ? model.PotentialStats.Handling : model.BaseStats.Handling;
            return power + handling;
        }

        /// <summary>A team locked to one model only buys that model.</summary>
        private static bool RunsModel(AITeam team, string modelId) =>
            team.ModelId == "" || team.ModelId == modelId;

        private sealed record OwnedCar(CarInstance Instance, CarModel Model, double Score);

        /// <summary>Returns the best car the AI team currently owns (highest CarScore), or null.</summary>
        private static OwnedCar? BestOwnedCar(AITeam team, IReadOnlyList<CarModel> carModels)
        {
            if (team.Cars.Count == 0) return null;
            var modelMap = ModelMap(carModels);
            OwnedCar? best = null;
            foreach (var instance in team.Cars)
            {
                if (!modelMap.TryGetValue(instance.ModelId, out var model)) continue;
                var score = CarScore(model, instance.InstalledUpgrades);
                if (best == null || score > best.Score) best = new OwnedCar(instance, model, score);
            }
            return best;
        }

        /// <summary>TS <c>new Map(carModels.map(m => [m.id, m]))</c>: later duplicates win.</summary>
        private static Dictionary<string, CarModel> ModelMap(IReadOnlyList<CarModel> carModels)
        {
            var map = new Dictionary<string, CarModel>();
            foreach (var m in carModels) map[m.Id] = m;
            return map;
        }

        private static InstalledUpgrades WithPack(InstalledUpgrades u, UpgradePackType pack) => pack switch
        {
            UpgradePackType.Power => u with { Power = true },
            UpgradePackType.Handling => u with { Handling = true },
            UpgradePackType.Comfort => u with { Comfort = true },
            _ => throw new ArgumentOutOfRangeException(nameof(pack)),
        };

        private static bool HasPack(InstalledUpgrades u, UpgradePackType pack) => pack switch
        {
            UpgradePackType.Power => u.Power,
            UpgradePackType.Handling => u.Handling,
            UpgradePackType.Comfort => u.Comfort,
            _ => throw new ArgumentOutOfRangeException(nameof(pack)),
        };

        // ---------------------------------------------------------------------------
        // Spending options (TS discriminated union)
        // ---------------------------------------------------------------------------

        private enum OptionKind
        {
            HireDriver,
            UpgradePack,
            BuyNewCar,
            BuyUsedCar,
            BuySpares,
            BuyTyres,
            RepairCar,
        }

        private sealed record SpendingOption
        {
            public OptionKind Kind { get; init; }
            public double Value { get; init; }
            public double Cost { get; init; }
            public Driver? Driver { get; init; }
            public int ContractLength { get; init; }
            public string CarId { get; init; } = "";
            public UpgradePackType PackType { get; init; }
            public string ModelId { get; init; } = "";
            public UsedCarListing? Listing { get; init; }
            public int PartsNeeded { get; init; }
            public double ConditionGain { get; init; }
        }

        // ---------------------------------------------------------------------------
        // Option enumeration
        // ---------------------------------------------------------------------------

        private static List<SpendingOption> EnumerateOptions(
            AITeam team,
            List<string> contractedIds,
            List<Driver> agents,
            IReadOnlyList<CarModel> carModels,
            IReadOnlyList<UsedCarListing> usedListings)
        {
            var options = new List<SpendingOption>();
            var modelMap = ModelMap(carModels);
            var current = BestOwnedCar(team, carModels);
            var currentScore = current?.Score ?? 0;
            var driverCount = contractedIds.Count;

            // AI only hires to fill empty slots; replacement happens via contract expiry next year
            if (driverCount < AiTargetDriverCount)
            {
                foreach (var agent in agents)
                {
                    var agentScore = DriverLifecycle.PerceivedRating(agent, team.Id);
                    var annualSalary = DriverLifecycle.CalculateAnnualSalary(DriverLifecycle.CalculateDriverStats(agent));
                    options.Add(new SpendingOption
                    {
                        Kind = OptionKind.HireDriver,
                        Value = EmptySlotBonus + agentScore,
                        Cost = annualSalary,
                        Driver = agent,
                        ContractLength = 1,
                    });
                }
            }

            // --- Upgrade packs on current car ---
            if (current != null)
            {
                var packTypes = new[] { UpgradePackType.Power, UpgradePackType.Handling, UpgradePackType.Comfort };
                foreach (var packType in packTypes)
                {
                    if (HasPack(current.Instance.InstalledUpgrades, packType)) continue;
                    var packDef = current.Model.UpgradePacks.FirstOrDefault(pk => pk.Type == packType);
                    if (packDef == null) continue;
                    var upgradedScore = CarScore(current.Model, WithPack(current.Instance.InstalledUpgrades, packType));
                    options.Add(new SpendingOption
                    {
                        Kind = OptionKind.UpgradePack,
                        Value = upgradedScore - currentScore,
                        Cost = packDef.Cost,
                        CarId = current.Instance.Id,
                        PackType = packType,
                    });
                }
            }

            // --- Buy new car ---
            var fullUpgrades = new InstalledUpgrades { Power = true, Handling = true, Comfort = true };
            foreach (var model in carModels)
            {
                if (!RunsModel(team, model.Id)) continue;
                var potentialScore = CarScore(model, fullUpgrades);
                if (potentialScore > currentScore)
                {
                    options.Add(new SpendingOption
                    {
                        Kind = OptionKind.BuyNewCar,
                        Value = potentialScore - currentScore,
                        Cost = model.Price,
                        ModelId = model.Id,
                    });
                }
            }

            // --- Buy used car ---
            foreach (var listing in usedListings)
            {
                if (!RunsModel(team, listing.ModelId)) continue;
                if (!modelMap.TryGetValue(listing.ModelId, out var model)) continue;
                var listingScore = CarScore(model, listing.InstalledUpgrades);
                if (listingScore > currentScore)
                {
                    options.Add(new SpendingOption
                    {
                        Kind = OptionKind.BuyUsedCar,
                        Value = listingScore - currentScore,
                        Cost = listing.Price,
                        Listing = listing,
                    });
                }
            }

            // --- Car repair ---
            if (current != null && current.Instance.Condition < 100)
            {
                var conditionDeficit = 100 - current.Instance.Condition;
                var partsNeeded = (int)Math.Ceiling(conditionDeficit / DealerData.ConditionPerPart);
                var partsToUse = Math.Min(partsNeeded, team.SpareParts);
                if (partsToUse > 0)
                {
                    var conditionGain = Math.Min(conditionDeficit, partsToUse * DealerData.ConditionPerPart);
                    // High value: keeping condition up is critical for race performance
                    options.Add(new SpendingOption
                    {
                        Kind = OptionKind.RepairCar,
                        Value = conditionGain * 2, // 2 value points per % condition restored
                        Cost = 0,                  // spare parts already owned, no money cost
                        CarId = current.Instance.Id,
                        PartsNeeded = partsToUse,
                        ConditionGain = conditionGain,
                    });
                }
            }

            // --- Consumables ---
            // Boost spare parts value if car needs repair but team lacks parts
            var needsRepair = current != null && current.Instance.Condition < 100;
            var sparesValue = needsRepair && team.SpareParts < 10 ? 50 : 3;
            if (team.SpareParts < AiMaxSpareParts)
            {
                options.Add(new SpendingOption
                {
                    Kind = OptionKind.BuySpares,
                    Value = sparesValue,
                    Cost = AiSparePartsPerBuy * AiSparePartCost,
                });
            }
            if (team.TyreSets < AiMaxTyreSets)
            {
                options.Add(new SpendingOption
                {
                    Kind = OptionKind.BuyTyres,
                    Value = 3,
                    Cost = AiTyreSetsPerBuy * AiTyreSetCost,
                });
            }

            return options;
        }

        // ---------------------------------------------------------------------------
        // Apply a spending option
        // ---------------------------------------------------------------------------

        private static AITeam ApplyOption(
            AITeam team,
            SpendingOption option,
            ref IReadOnlyList<Contract> allContracts,
            ref List<string> contractedIds,
            Func<string> newCarId,
            List<Contract> newContracts)
        {
            switch (option.Kind)
            {
                case OptionKind.HireDriver:
                {
                    var contract = new Contract
                    {
                        DriverId = option.Driver!.Id,
                        TeamId = team.Id,
                        Length = option.ContractLength,
                        RemainingYears = option.ContractLength,
                        AnnualSalary = option.Cost,
                    };
                    newContracts.Add(contract);
                    contractedIds = contractedIds.Append(option.Driver.Id).ToList();
                    allContracts = allContracts.Append(contract).ToArray();
                    return team with { Budget = team.Budget - option.Cost, Contracts = team.Contracts.Append(contract).ToArray() };
                }

                case OptionKind.UpgradePack:
                    return team with
                    {
                        Budget = team.Budget - option.Cost,
                        Cars = team.Cars
                            .Select(c => c.Id == option.CarId ? c with { InstalledUpgrades = WithPack(c.InstalledUpgrades, option.PackType) } : c)
                            .ToArray(),
                    };

                case OptionKind.BuyNewCar:
                {
                    var newInstance = new CarInstance
                    {
                        Id = newCarId(),
                        ModelId = option.ModelId,
                        Age = 0,
                        Condition = 100,
                        InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
                    };
                    return team with
                    {
                        Budget = team.Budget - option.Cost,
                        Cars = team.Cars.Append(newInstance).ToArray(),
                        EnteredCarId = team.EnteredCarId ?? newInstance.Id,
                    };
                }

                case OptionKind.BuyUsedCar:
                {
                    var listing = option.Listing!;
                    var newInstance = new CarInstance
                    {
                        Id = newCarId(),
                        ModelId = listing.ModelId,
                        Age = listing.Age,
                        Condition = listing.Condition,
                        InstalledUpgrades = listing.InstalledUpgrades,
                    };
                    return team with
                    {
                        Budget = team.Budget - option.Cost,
                        Cars = team.Cars.Append(newInstance).ToArray(),
                        EnteredCarId = team.EnteredCarId ?? newInstance.Id,
                    };
                }

                case OptionKind.BuySpares:
                    return team with { Budget = team.Budget - option.Cost, SpareParts = team.SpareParts + AiSparePartsPerBuy };

                case OptionKind.BuyTyres:
                    return team with { Budget = team.Budget - option.Cost, TyreSets = team.TyreSets + AiTyreSetsPerBuy };

                case OptionKind.RepairCar:
                    return team with
                    {
                        SpareParts = team.SpareParts - option.PartsNeeded,
                        Cars = team.Cars
                            .Select(c => c.Id == option.CarId ? c with { Condition = Math.Min(100, c.Condition + option.ConditionGain) } : c)
                            .ToArray(),
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(option));
            }
        }

        // ---------------------------------------------------------------------------
        // Main greedy loop
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Runs the AI team's annual spending decisions using a greedy algorithm (GDD §8).
        ///
        /// Each iteration: enumerate all affordable options, score them, apply the highest-value
        /// option, repeat until no affordable options remain or budget is exhausted.
        ///
        /// "Highest value" is intentionally simplistic: AI teams are reasonable but not optimal.
        /// </summary>
        public static AiSpendingResult RunAiSpending(AiSpendingContext ctx)
        {
            var team = ctx.Team;
            var allContracts = ctx.AllContracts;
            var newContracts = new List<Contract>();

            var contractedIds = ContractedDriverIds(team.Id, allContracts);
            // Track which drivers have already been hired this round to avoid duplicates
            var hiredThisRound = new HashSet<string>();

            const int MaxIterations = 100; // Safety limit to prevent infinite loops
            for (var i = 0; i < MaxIterations; i++)
            {
                // Free agents: exclude those already hired this round
                var agents = FreeAgents(ctx.AllDrivers, allContracts).Where(d => !hiredThisRound.Contains(d.Id)).ToList();

                var options = EnumerateOptions(team, contractedIds, agents, ctx.CarModels, ctx.UsedListings);

                var budget = team.Budget;
                // Pick highest-value option; break ties by lowest cost (stable, like JS sort)
                var best = options
                    .Where(o => o.Cost <= budget)
                    .OrderByDescending(o => o.Value)
                    .ThenBy(o => o.Cost)
                    .FirstOrDefault();
                if (best == null) break;

                if (best.Kind == OptionKind.HireDriver) hiredThisRound.Add(best.Driver!.Id);

                team = ApplyOption(team, best, ref allContracts, ref contractedIds, ctx.NewCarId, newContracts);
            }

            return new AiSpendingResult { UpdatedTeam = team, NewContracts = newContracts };
        }
    }
}
