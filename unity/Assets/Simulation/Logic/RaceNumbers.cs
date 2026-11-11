#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Race numbers, redone every season. Number 1 goes to the overall winner of last year's race.
    /// Everyone else follows by prestige, highest first. With no winner yet, prestige alone decides.
    /// </summary>
    public static class RaceNumbers
    {
        /// <summary>The team that won <paramref name="year"/>'s race outright, or null if it has none.</summary>
        public static string? WinnerOf(IEnumerable<RaceHistoryEntry> raceHistory, int year) =>
            raceHistory.LastOrDefault(r => r.Year == year)?.Results.FirstOrDefault(x => x.Position == 1)?.TeamId;

        /// <summary>
        /// Numbers every team from 1. The winner comes first, then prestige from high to low.
        /// Equal prestige falls back to team id so the order never depends on list order.
        /// </summary>
        public static IReadOnlyList<BaseTeam> Assign(IEnumerable<BaseTeam> teams, string? lastWinnerId)
        {
            var list = teams.ToList();
            var numbers = list
                .OrderByDescending(t => t.Id == lastWinnerId)
                .ThenByDescending(t => t.Prestige)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .Select((t, i) => (t.Id, Number: i + 1))
                .ToDictionary(x => x.Id, x => x.Number);
            return list.Select(t => t with { RaceNumber = numbers[t.Id] }).ToArray();
        }
    }
}
