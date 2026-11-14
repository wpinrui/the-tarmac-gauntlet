#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    public sealed record YearAdvanceInput
    {
        /// <summary>Full driver pool before advancement.</summary>
        public IReadOnlyList<Driver> Drivers { get; init; } = Array.Empty<Driver>();
        /// <summary>All active contracts across all teams before advancement.</summary>
        public IReadOnlyList<Contract> Contracts { get; init; } = Array.Empty<Contract>();
        /// <summary>All 100 teams (player + AI).</summary>
        public IReadOnlyList<BaseTeam> Teams { get; init; } = Array.Empty<BaseTeam>();
        /// <summary>Full car model catalogue.</summary>
        public IReadOnlyList<CarModel> CarModels { get; init; } = Array.Empty<CarModel>();
        /// <summary>Exactly 15 specs for newly generated rookie drivers.</summary>
        public IReadOnlyList<RookieSpec> RookieSpecs { get; init; } = Array.Empty<RookieSpec>();
        /// <summary>Whether the player has won the event before (enables F1 in used market).</summary>
        public bool PlayerHasWon { get; init; }
        /// <summary>Current game year (for transaction logging).</summary>
        public int CurrentYear { get; init; } = 1;
        /// <summary>Race history to trim (rolling window).</summary>
        public IReadOnlyList<RaceHistoryEntry> RaceHistory { get; init; } = Array.Empty<RaceHistoryEntry>();
        /// <summary>Generates unique IDs for newly purchased car instances. Must produce distinct values.</summary>
        public Func<string> NewCarId { get; init; } = DealerData.NextCarId;
    }

    public sealed record YearAdvanceResult
    {
        /// <summary>Updated driver pool: aged survivors + 15 new rookies.</summary>
        public IReadOnlyList<Driver> Drivers { get; init; } = Array.Empty<Driver>();
        /// <summary>Updated contracts: remaining years decremented; expired (0) removed.</summary>
        public IReadOnlyList<Contract> Contracts { get; init; } = Array.Empty<Contract>();
        /// <summary>Updated teams: all cars aged +1, AI teams have completed spending.</summary>
        public IReadOnlyList<BaseTeam> Teams { get; init; } = Array.Empty<BaseTeam>();
        /// <summary>New used car inventory for the coming year.</summary>
        public IReadOnlyList<UsedCarListing> UsedListings { get; init; } = Array.Empty<UsedCarListing>();
        /// <summary>IDs of drivers retired this year.</summary>
        public IReadOnlyList<string> RetiredDriverIds { get; init; } = Array.Empty<string>();
        /// <summary>Trimmed race history (rich data stripped beyond window).</summary>
        public IReadOnlyList<RaceHistoryEntry> RaceHistory { get; init; } = Array.Empty<RaceHistoryEntry>();
    }

    /// <summary>Port of simulation/yearAdvance.ts.</summary>
    public static class YearAdvance
    {
        /// <summary>Number of years of rich race data to keep. Older entries lose per-lap detail.</summary>
        public const int RaceHistoryWindow = 3;

        /// <summary>
        /// Advances the world state by one year (GDD §8).
        ///
        /// Order of operations:
        ///   1. Advance driver pool: age all drivers +1, retire bottom 15, add 15 rookies.
        ///   2. Decrement contract remaining years by 1; remove contracts that hit 0.
        ///   3. Size each AI team's crew from its prestige percentile, then deduct annual
        ///      salaries and crew costs for all teams.
        ///   4. Age all car instances by 1 year (across all teams).
        ///   5. Generate new used car inventory.
        ///   6. Run AI spending for each AI team, most appealing team first (greedy algorithm, highest-value-first).
        ///   7. Trim race history to the rolling window.
        ///
        /// Player spending is NOT handled here: the player acts via the UI before this
        /// function is called each year.
        /// </summary>
        /// <param name="input">All world state needed for the advance.</param>
        /// <param name="random">Injectable random source (driver generation, used inventory); null uses <see cref="DefaultRandom"/>.</param>
        public static YearAdvanceResult AdvanceYear(YearAdvanceInput input, Func<double>? random = null)
        {
            random ??= DefaultRandom.Next;
            var currentYear = input.CurrentYear;

            // 1. Advance driver pool
            var driverResult = DriverLifecycle.AdvanceDriverYear(input.Drivers, input.RookieSpecs, random);

            // 2. Expire contracts: decrement RemainingYears; drop those that reach 0
            IReadOnlyList<Contract> updatedContracts = input.Contracts
                .Select(c => c with { RemainingYears = c.RemainingYears - 1 })
                .Where(c => c.RemainingYears > 0)
                .ToArray();

            // 3. Annual deductions: driver salaries + crew costs for ALL teams
            var deductedTeams = AiCrew.Assign(input.Teams).Select(team =>
            {
                var teamContracts = updatedContracts.Where(c => c.TeamId == team.Id).ToList();
                var totalSalary = teamContracts.Sum(c => c.AnnualSalary);
                var crewCost = team.CrewSize * DealerData.CrewCostPerMember;
                var updatedBudget = team.Budget - (totalSalary + crewCost);

                if (team is PlayerTeam player)
                {
                    // Log transactions for the player
                    var newTxs = new List<TransactionRecord>();
                    if (totalSalary > 0)
                    {
                        newTxs.Add(new TransactionRecord
                        {
                            Year = currentYear,
                            Category = TransactionCategory.DriverSalary,
                            Amount = -totalSalary,
                            Description = $"Annual driver salaries ({teamContracts.Count} contracts)",
                        });
                    }
                    if (crewCost > 0)
                    {
                        newTxs.Add(new TransactionRecord
                        {
                            Year = currentYear,
                            Category = TransactionCategory.CrewCost,
                            Amount = -crewCost,
                            Description = $"Crew wages ({player.CrewSize} members)",
                        });
                    }
                    return player with { Budget = updatedBudget, Transactions = player.Transactions.Concat(newTxs).ToArray() };
                }

                return team with { Budget = updatedBudget };
            }).ToList();

            // 4. Age all car instances by 1 year
            var agedTeams = deductedTeams
                .Select(team => team with { Cars = team.Cars.Select(car => car with { Age = car.Age + 1 }).ToArray() })
                .ToList();

            // 5. Generate new used car inventory
            var usedListings = CarMarketLogic.GenerateUsedInventoryByClass(input.CarModels, input.PlayerHasWon, random);

            // 6. Run AI spending for each AI team, most appealing first, so the best drivers go to the teams they most want
            var activeContracts = updatedContracts;
            var spent = new Dictionary<string, BaseTeam>();
            foreach (var ai in agedTeams.OfType<AITeam>().OrderByDescending(DriverLifecycle.TeamAppeal))
            {
                var result = AiSpending.RunAiSpending(new AiSpendingContext
                {
                    Team = ai,
                    AllContracts = activeContracts,
                    AllDrivers = driverResult.Drivers,
                    CarModels = input.CarModels,
                    UsedListings = usedListings,
                    NewCarId = input.NewCarId,
                });

                // Accumulate new contracts into the shared pool so subsequent AI teams see them
                activeContracts = activeContracts.Concat(result.NewContracts).ToArray();
                spent[ai.Id] = result.UpdatedTeam;
            }
            var finalTeams = agedTeams.Select(team => spent.TryGetValue(team.Id, out var updated) ? updated : team).ToList();

            // 7. Trim race history rolling window: strip rich data, keep results + fastest lap only
            var trimmedHistory = input.RaceHistory.Select(entry =>
                currentYear - entry.Year >= RaceHistoryWindow
                    ? entry with
                    {
                        LapSnapshots = null,
                        PositionHistory = null,
                        CarIndexById = null,
                        Events = null,
                        Stints = null,
                        ModeCounters = null,
                    }
                    : entry).ToArray();

            return new YearAdvanceResult
            {
                Drivers = driverResult.Drivers,
                Contracts = activeContracts,
                Teams = finalTeams,
                UsedListings = usedListings,
                RetiredDriverIds = driverResult.RetiredIds,
                RaceHistory = trimmedHistory,
            };
        }
    }
}
