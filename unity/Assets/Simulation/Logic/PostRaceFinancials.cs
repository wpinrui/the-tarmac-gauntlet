#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS inline type of <c>PostRaceFinancialInput["results"][number]</c>.</summary>
    public sealed record PostRaceCarResult
    {
        public string TeamId { get; init; } = "";
        public string CarId { get; init; } = "";
        public CarClass CarClass { get; init; }
        public int Position { get; init; }
        public int LapsCompleted { get; init; }
        public bool Retired { get; init; }
    }

    public sealed record PostRaceFinancialInput
    {
        /// <summary>Race results: team positions + laps completed.</summary>
        public IReadOnlyList<PostRaceCarResult> Results { get; init; } = Array.Empty<PostRaceCarResult>();
        /// <summary>Total fuel consumed during the race (litres).</summary>
        public double PlayerFuelConsumed { get; init; }
        /// <summary>Fuel cost per litre.</summary>
        public double FuelCostPerLitre { get; init; }
        /// <summary>Current game year.</summary>
        public int Year { get; init; }
    }

    public sealed record PostRaceFinancialOutput
    {
        /// <summary>Prize money per team: teamId to amount.</summary>
        public IReadOnlyDictionary<string, double> PrizeMoney { get; init; } = new Dictionary<string, double>();
        /// <summary>Player's fuel cost (may be written off if insufficient funds).</summary>
        public double PlayerFuelCost { get; init; }
        /// <summary>Race history entry to append to game state.</summary>
        public RaceHistoryEntry RaceHistoryEntry { get; init; } = new RaceHistoryEntry();
    }

    /// <summary>Port of simulation/postRaceFinancials.ts.</summary>
    public static class PostRaceFinancials
    {
        /// <summary>
        /// Processes all financial outcomes of a completed race.
        ///
        /// Call sequence after a race ends:
        ///   1. Call this function to compute prize money, fuel cost, and race history
        ///   2. Call store.awardPrizeMoney for each team
        ///   3. Call store.deductFuelCost for the player
        ///   4. Push the RaceHistoryEntry to GameState.RaceHistory
        ///
        /// This function is pure: it computes but does not mutate state.
        /// </summary>
        public static PostRaceFinancialOutput ProcessPostRaceFinancials(PostRaceFinancialInput input)
        {
            var results = input.Results;

            // Prize money
            var schedule = PostRace.BuildPrizeSchedule(results.Count);
            var prizeMoney = PostRace.DistributePrizeMoney(
                results.Select(r => new PrizeEligibility
                {
                    TeamId = r.TeamId,
                    Position = r.Position,
                    LapsCompleted = r.LapsCompleted,
                }),
                schedule);

            // Fuel cost
            double playerFuelCost = JsMath.Round(input.PlayerFuelConsumed * input.FuelCostPerLitre);

            // Race history entry
            var raceHistoryEntry = new RaceHistoryEntry
            {
                Year = input.Year,
                Results = results.Select(r => new RaceResult
                {
                    TeamId = r.TeamId,
                    CarId = r.CarId,
                    CarClass = r.CarClass,
                    Position = r.Position,
                    LapsCompleted = r.LapsCompleted,
                    PrizeMoney = prizeMoney.TryGetValue(r.TeamId, out var p) ? p : 0,
                    Retired = r.Retired,
                }).ToList(),
                FastestLap = null, // Populated by the race loop, not here
            };

            return new PostRaceFinancialOutput
            {
                PrizeMoney = prizeMoney,
                PlayerFuelCost = playerFuelCost,
                RaceHistoryEntry = raceHistoryEntry,
            };
        }
    }
}
