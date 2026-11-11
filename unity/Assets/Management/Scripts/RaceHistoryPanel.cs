using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Race history screen: one year's race at a time, laid out like the race summary. The panel holds the results
    /// overall, by class and as class podiums. The side pane holds the player's finish and, while the race is within
    /// the detail window (<see cref="YearAdvance.RaceHistoryWindow"/>), its position chart and the stints, stops and
    /// issues. Older races keep only their results.
    /// </summary>
    public sealed class RaceHistoryPanel : MenuPanel
    {
        public const string Slug = "race-history";

        // Kept while the game runs; null shows the latest race.
        private int? year;
        private readonly RaceView.Views views = new RaceView.Views();

        protected override bool Shows(string slug) => slug == Slug;

        protected override void Render()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            Body.Clear();
            Menus.SideBody.Clear();
            IReadOnlyList<RaceHistoryEntry> history = game.RaceHistory;
            if (history.Count == 0) return;

            int at = year.HasValue ? Math.Max(0, IndexOf(history, year.Value)) : history.Count - 1;
            RaceHistoryEntry entry = history[at];
            Body.Add(YearStepper(entry.Year, at > 0, at < history.Count - 1, d =>
            {
                year = history[at + d].Year;
                Render();
            }));

            var race = new RaceView(RaceReplay.FromHistory(game, entry), false, views);
            race.Results(Body);
            if (race.HasPlayer)
            {
                VisualElement hero = Box();
                race.Hero(hero);
                Menus.SideBody.Add(hero);
                if (race.HasLaps)
                {
                    race.Chart(Menus.SideBody);
                    VisualElement log = Box("history-log");
                    race.Log(log);
                    Menus.SideBody.Add(log);
                }
            }
        }

        private static int IndexOf(IReadOnlyList<RaceHistoryEntry> history, int y)
        {
            for (int i = 0; i < history.Count; i++)
                if (history[i].Year == y) return i;
            return -1;
        }
    }
}
