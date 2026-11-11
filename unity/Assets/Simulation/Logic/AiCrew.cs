#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// AI pit crew sizes: each AI team's crew follows its prestige percentile across the
    /// AI teams, from <see cref="MinCrew"/> at the bottom to <see cref="MaxCrew"/> at the top.
    /// The player's crew is whatever they hire and is never touched here.
    /// </summary>
    public static class AiCrew
    {
        public const int MinCrew = 4;
        public const int MaxCrew = 16;

        /// <summary>Crew for a prestige percentile from 0 (bottom) to 1 (top), in whole people.</summary>
        public static int ForPercentile(double percentile) =>
            (int)JsMath.Round(MinCrew + percentile * (MaxCrew - MinCrew));

        /// <summary>
        /// Each AI team's prestige percentile among the AI teams: 0 for the lowest, 1 for the highest.
        /// Teams on equal prestige share the average of their places. A lone team counts as the top.
        /// </summary>
        public static Dictionary<string, double> Percentiles(IEnumerable<AITeam> aiTeams)
        {
            var sorted = aiTeams.OrderBy(t => t.Prestige).ToList();
            var result = new Dictionary<string, double>();
            if (sorted.Count == 1) result[sorted[0].Id] = 1;
            if (sorted.Count <= 1) return result;

            int last = sorted.Count - 1;
            for (int i = 0; i < sorted.Count;)
            {
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1].Prestige == sorted[i].Prestige) j++;
                double place = (i + j) / 2.0;
                for (int k = i; k <= j; k++) result[sorted[k].Id] = place / last;
                i = j + 1;
            }
            return result;
        }

        /// <summary>Sets every AI team's crew from its prestige percentile; other teams pass through.</summary>
        public static IReadOnlyList<BaseTeam> Assign(IEnumerable<BaseTeam> teams)
        {
            var list = teams.ToList();
            var percentiles = Percentiles(list.OfType<AITeam>());
            return list.Select(t => t is AITeam ai ? ai with { CrewSize = ForPercentile(percentiles[ai.Id]) } : t).ToArray();
        }
    }
}
