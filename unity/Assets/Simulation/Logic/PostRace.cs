#nullable enable
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A single past race result for one team, used as input to prestige calculation.
    /// Pass results ordered newest-first (index 0 = most recent race).
    /// </summary>
    public sealed record PastRaceResult
    {
        /// <summary>1-indexed finishing position.</summary>
        public int Position { get; init; }
        /// <summary>Total number of cars in the race (used to normalise the position score).</summary>
        public int TotalCars { get; init; }
    }

    /// <summary>TS inline type <c>{ teamId; position; lapsCompleted }</c> taken by <c>distributePrizeMoney</c>.</summary>
    public sealed record PrizeEligibility
    {
        public string TeamId { get; init; } = "";
        public int Position { get; init; }
        public int LapsCompleted { get; init; }
        /// <summary>False for a car that retired or was unclassified: it earns nothing.</summary>
        public bool Finished { get; init; } = true;
    }

    /// <summary>Port of simulation/postRace.ts.</summary>
    public static class PostRace
    {
        // ---------------------------------------------------------------------
        // Tunable constants: balance values, adjust freely without touching logic
        // ---------------------------------------------------------------------

        // --- Prize money (hardcoded table, total = $15,000,000) ---
        /// <summary>Prize amounts indexed by position (0 = P1, 99 = P100).</summary>
        public static readonly IReadOnlyList<double> PrizeTable = new double[]
        {
            750000,700000,675000,650000,620000,595000,570000,545000,520000,500000,
            480000,460000,440000,420000,400000,380000,365000,345000,330000,315000,
            300000,285000,270000,260000,245000,230000,220000,210000,195000,180000,
            175000,165000,155000,145000,135000,130000,120000,115000,105000,100000,
            95000,90000,83000,77000,71000,66000,62000,59000,54000,50000,
            46000,42000,39000,35000,32000,30000,27000,25000,22000,19500,
            19000,17000,16000,14000,13000,11000,10000,9000,8000,7000,
            6500,5500,5000,4000,3500,3000,3000,2500,2000,2000,
            1500,1500,1000,1000,1000,800,750,650,600,600,
            550,550,500,500,500,500,500,500,500,500,
        };

        // --- Prestige ---
        /// <summary>
        /// Number of most-recent races that receive a boosted weight.
        /// GDD §8: "recency bias toward the past 5 races."
        /// </summary>
        private const int RecentRaceCount = 5;
        /// <summary>Weight applied to each of the most-recent races.</summary>
        private const double RecentWeight = 2.0;
        /// <summary>Weight applied to all older races (beyond RecentRaceCount).</summary>
        private const double BaseWeight = 1.0;

        // ---------------------------------------------------------------------
        // Prize money
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the prize schedule from the hardcoded PrizeTable.
        /// The table has exactly 100 entries totalling $15,000,000.
        /// </summary>
        /// <param name="numCars">Total number of cars in the race (typically 100). Entries beyond the table length get $0.</param>
        public static List<PrizeScheduleEntry> BuildPrizeSchedule(int numCars)
        {
            var schedule = new List<PrizeScheduleEntry>();
            for (int pos = 1; pos <= numCars; pos++)
            {
                double amount = pos <= PrizeTable.Count ? PrizeTable[pos - 1] : 0;
                schedule.Add(new PrizeScheduleEntry { Position = pos, Amount = amount });
            }
            return schedule;
        }

        /// <summary>
        /// Distributes prize money to all teams based on their finishing position (GDD §7).
        ///
        /// Rules:
        ///   - Cars that completed 0 laps receive $0 (GDD §7: "prize money eligibility: all cars with ≥1 lap").
        ///   - Cars that did not finish (retired, or unclassified) receive $0.
        ///   - All other cars receive the prize for their finishing position from the schedule.
        ///   - If a position has no matching schedule entry (e.g., fewer cars than schedule entries),
        ///     the car receives $0.
        /// </summary>
        /// <param name="results">Per-car race outcomes. Each entry must have a unique position.</param>
        /// <param name="schedule">Prize schedule (from BuildPrizeSchedule or EconomyConfig.PrizeSchedule).</param>
        /// <returns>Map of teamId to prize money awarded (in dollars).</returns>
        public static Dictionary<string, double> DistributePrizeMoney(
            IEnumerable<PrizeEligibility> results,
            IEnumerable<PrizeScheduleEntry> schedule)
        {
            var scheduleMap = new Dictionary<int, double>();
            foreach (var e in schedule) scheduleMap[e.Position] = e.Amount;
            var prizes = new Dictionary<string, double>();

            foreach (var r in results)
            {
                if (r.LapsCompleted == 0 || !r.Finished)
                {
                    prizes[r.TeamId] = 0;
                }
                else
                {
                    prizes[r.TeamId] = scheduleMap.TryGetValue(r.Position, out var amount) ? amount : 0;
                }
            }

            return prizes;
        }

        // ---------------------------------------------------------------------
        // Team prestige
        // ---------------------------------------------------------------------

        /// <summary>
        /// Calculates a team's prestige score from its full race history (GDD §8).
        ///
        /// Prestige is a normalised 0 to 100 score (0 = always last, 100 = always first).
        /// It is a weighted average of per-race position scores, where the most recent
        /// RecentRaceCount races carry extra weight (recency bias).
        ///
        /// Position score per race:
        ///   score = (totalCars - position) / (totalCars - 1) × 100
        ///   so 100 for the winner, 0 for last place, linear between.
        ///
        /// Weight per race (results[0] = most recent):
        ///   i &lt; RecentRaceCount  gives RecentWeight
        ///   i ≥ RecentRaceCount  gives BaseWeight
        ///
        /// An empty history (year-1 team, no races yet) returns 0.
        /// </summary>
        /// <param name="results">All past race results for this team, newest-first.</param>
        public static double CalculatePrestige(IReadOnlyList<PastRaceResult> results)
        {
            if (results.Count == 0) return 0;

            double weightedSum = 0;
            double totalWeight = 0;

            for (int i = 0; i < results.Count; i++)
            {
                int position = results[i].Position;
                int totalCars = results[i].TotalCars;
                double positionScore =
                    totalCars > 1 ? ((double)(totalCars - position) / (totalCars - 1)) * 100 : 100;
                double weight = i < RecentRaceCount ? RecentWeight : BaseWeight;
                weightedSum += positionScore * weight;
                totalWeight += weight;
            }

            return weightedSum / totalWeight;
        }

        /// <summary>Extracts PastRaceResult list for a given team from race history (newest-first).</summary>
        public static List<PastRaceResult> ExtractTeamResults(
            string teamId,
            IReadOnlyList<RaceHistoryEntry> raceHistory)
        {
            var results = new List<PastRaceResult>();
            for (int i = raceHistory.Count - 1; i >= 0; i--)
            {
                var entry = raceHistory[i];
                RaceResult? teamResult = null;
                foreach (var r in entry.Results)
                {
                    if (r.TeamId == teamId) { teamResult = r; break; }
                }
                if (teamResult != null)
                {
                    results.Add(new PastRaceResult { Position = teamResult.Position, TotalCars = entry.Results.Count });
                }
            }
            return results;
        }

        /// <summary>Recalculates prestige for all teams from race history.</summary>
        public static Dictionary<string, double> RecalculateAllPrestige(
            IEnumerable<string> teamIds,
            IReadOnlyList<RaceHistoryEntry> raceHistory)
        {
            var result = new Dictionary<string, double>();
            foreach (var teamId in teamIds)
            {
                result[teamId] = CalculatePrestige(ExtractTeamResults(teamId, raceHistory));
            }
            return result;
        }
    }
}
