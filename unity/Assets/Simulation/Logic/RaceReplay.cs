#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Rebuilds a past race as the race summary shows it, from what <see cref="RaceHistoryEntry"/> keeps. Races past
    /// the detail window (<see cref="YearAdvance.RaceHistoryWindow"/>) come back with the classification and the
    /// player's finish only.
    /// </summary>
    public static class RaceReplay
    {
        private const string Separator = " — ";

        /// <summary>
        /// History keeps an issue's words rather than its template, so each issue's <see cref="IssueRecord.TemplateId"/>
        /// holds its description. It doesn't say which issues were fixed: every issue counts as fixed on its lap, and a
        /// retirement is the one unfixed issue. Stops carry no times or fuel.
        /// </summary>
        public static RaceSummary FromHistory(GameState game, RaceHistoryEntry entry)
        {
            var names = new Dictionary<string, string>();
            foreach (BaseTeam t in game.Teams) names[t.Id] = t.Name;
            var inClass = new Dictionary<CarClass, int>();
            ClassifiedCar[] classified = entry.Results.OrderBy(r => r.Position).Select(r =>
            {
                inClass.TryGetValue(r.CarClass, out int n);
                inClass[r.CarClass] = n + 1;
                return new ClassifiedCar
                {
                    Position = r.Position,
                    ClassPosition = n + 1,
                    TeamId = r.TeamId,
                    TeamName = names.TryGetValue(r.TeamId, out string? name) ? name : r.TeamId,
                    CarId = r.CarId,
                    CarClass = r.CarClass,
                    Laps = r.LapsCompleted,
                    Retired = r.Retired,
                    PrizeMoney = r.PrizeMoney,
                };
            }).ToArray();

            ClassifiedCar? me = classified.FirstOrDefault(c => c.TeamId == GameActions.PlayerTeamId);
            var bare = new RaceSummary { Year = entry.Year, Classification = classified, Player = me, FastestLap = entry.FastestLap };
            if (me == null || entry.PositionHistory == null || entry.CarIndexById == null || !entry.CarIndexById.TryGetValue(me.CarId, out int slot))
                return bare;

            IReadOnlyList<Stint> stints = entry.Stints != null && entry.Stints.TryGetValue(me.CarId, out IReadOnlyList<Stint>? s)
                ? s.Where(x => x.EndLap >= x.StartLap).ToArray()
                : Array.Empty<Stint>();
            // A new stint starts on the lap after the stop that put its driver in.
            string DriverAt(int lap) => stints.LastOrDefault(x => x.StartLap <= lap)?.DriverId ?? stints.FirstOrDefault()?.DriverId ?? "";
            RaceEvent[] events = (entry.Events ?? Array.Empty<RaceEvent>()).Where(e => e.CarId == me.CarId).ToArray();

            return bare with
            {
                Stints = stints.Select(x => new StintRecord
                {
                    DriverId = x.DriverId,
                    DriverName = DriverName(game, x.DriverId),
                    StartLap = x.StartLap,
                    EndLap = x.EndLap,
                }).ToArray(),
                PitStops = events.Where(e => e.Type == RaceEventType.PitStop).Select(e => new PitStopRecord
                {
                    Lap = e.Lap,
                    DriverOut = DriverAt(e.Lap),
                    DriverIn = DriverAt(e.Lap + 1),
                }).ToArray(),
                Issues = events.Where(e => e.Type == RaceEventType.Issue || e.Type == RaceEventType.Retirement).Select(e => new IssueRecord
                {
                    Lap = e.Lap,
                    TemplateId = Describe(e),
                    DriverId = DriverAt(e.Lap),
                    FixedLap = e.Type == RaceEventType.Retirement ? (int?)null : e.Lap,
                }).ToArray(),
                PositionByLap = entry.PositionHistory.Where(row => slot < row.Count).Select(row => row[slot]).ToArray(),
            };
        }

        /// <summary>The words after the car id in an event's text, with a capital first letter.</summary>
        private static string Describe(RaceEvent e)
        {
            int cut = e.Text.IndexOf(Separator, StringComparison.Ordinal);
            string what = cut >= 0 ? e.Text.Substring(cut + Separator.Length) : e.Text;
            return what.Length == 0 ? what : char.ToUpperInvariant(what[0]) + what.Substring(1);
        }

        /// <summary>A driver's name, the player's own for the player character.</summary>
        public static string DriverName(GameState game, string id)
        {
            if (id == RaceEntry.PlayerCharacterDriverId) return game.Teams.OfType<PlayerTeam>().FirstOrDefault()?.PlayerName ?? id;
            return game.Drivers.FirstOrDefault(d => d.Id == id)?.Name ?? id;
        }
    }
}
