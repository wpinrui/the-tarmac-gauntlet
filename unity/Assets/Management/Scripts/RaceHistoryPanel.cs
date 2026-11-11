using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.OfficeKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Race history screen: one year's race at a time, laid out like the race summary. The panel holds the results
    /// overall, by class and as class podiums. The side pane holds the player's finish and, while the race is within
    /// the detail window (<see cref="YearAdvance.RaceHistoryWindow"/>), its position chart and the stints, stops and
    /// issues. Older races keep only their results.
    /// </summary>
    public sealed class RaceHistoryPanel : OfficePanel
    {
        public const string Slug = "race-history";

        // Kept while the game runs; null shows the latest race.
        private int? year;
        private string resultsView = "all", logView = "stints";
        private readonly Dictionary<string, Color> colours = new Dictionary<string, Color>();

        protected override string ScreenSlug => Slug;

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
            Body.Add(YearStepper(Menus, entry.Year, at > 0, at < history.Count - 1, d =>
            {
                year = history[at + d].Year;
                Render();
            }));

            RaceSummary race = RaceReplay.FromHistory(game, entry);
            VisualElement tabs = Box("rs-tabs");
            Body.Add(tabs);
            ScrollView results = Scroll("rs-scroll");
            Body.Add(results);
            RenderResults(race, tabs, results);

            if (race.Player != null) RenderPlayer(game, race, entry);
        }

        private static int IndexOf(IReadOnlyList<RaceHistoryEntry> history, int y)
        {
            for (int i = 0; i < history.Count; i++)
                if (history[i].Year == y) return i;
            return -1;
        }

        // ------------------------------------------------------------------ results

        private void RenderResults(RaceSummary race, VisualElement tabs, ScrollView list)
        {
            CarClass[] classes = ClassLabels.Order.Where(c => race.Classification.Any(r => r.CarClass == c)).ToArray();
            if (resultsView != "all" && resultsView != "podiums" && !classes.Any(c => c.ToString() == resultsView)) resultsView = "all";

            void Show(string view)
            {
                resultsView = view;
                RenderResults(race, tabs, list);
                list.scrollOffset = Vector2.zero;
            }

            tabs.Clear();
            tabs.Add(Tab(Menus, "ALL", resultsView == "all", () => Show("all")));
            foreach (CarClass c in classes)
            {
                string key = c.ToString();
                Label t = Tab(Menus, ClassLabels.Name(c), resultsView == key, () => Show(key));
                t.AddToClassList("rs-tab-class");
                t.style.color = ClassLabels.Colour(c);
                tabs.Add(t);
            }
            Label podiums = Tab(Menus, "", resultsView == "podiums", () => Show("podiums"));
            podiums.AddToClassList("rs-tab-end");
            podiums.Add(Icon("trophy", "rs-tab-icon"));
            tabs.Add(podiums);

            list.Clear();
            if (resultsView == "podiums")
            {
                foreach (CarClass c in classes) list.Add(Podium(race, c));
                return;
            }
            bool overall = resultsView == "all";
            foreach (ClassifiedCar r in overall ? race.Classification : race.Classification.Where(x => x.CarClass.ToString() == resultsView))
            {
                VisualElement row = Box("rs-row");
                row.EnableInClassList("me", r.TeamId == GameActions.PlayerTeamId);
                row.EnableInClassList("out", r.Retired);
                row.Add(Menus.Text(Num(overall ? r.Position : r.ClassPosition), "rs-pos", true));
                row.Add(ClassLabels.Make(r.CarClass, "cls-sm", Menus.Bold));
                VisualElement team = Box("rs-team");
                team.Add(Menus.Text(r.TeamName, "rs-team-name", true));
                row.Add(team);
                row.Add(Menus.Text(Num(r.Laps), "rs-laps"));
                row.Add(r.Retired ? Menus.Text("DNF", "rs-dnf", true) : Menus.Text(Menus.Money(r.PrizeMoney), "rs-prize", true));
                list.Add(row);
            }
            if (overall && race.Player != null)
            {
                // Open on the player's row.
                int index = race.Player.Position - 1;
                list.RegisterCallback<GeometryChangedEvent>(Centre);
                void Centre(GeometryChangedEvent _)
                {
                    list.UnregisterCallback<GeometryChangedEvent>(Centre);
                    if (index >= list.contentContainer.childCount) return;
                    VisualElement row = list.contentContainer[index];
                    list.scrollOffset = new Vector2(0, Mathf.Max(0, row.layout.y - list.contentViewport.layout.height / 2 + row.layout.height / 2));
                }
            }
        }

        private VisualElement Podium(RaceSummary race, CarClass c)
        {
            VisualElement block = Box("rs-podium");
            Label letter = Menus.Text("", "rs-podium-class", true);
            ClassLabels.Tint(letter, c);
            block.Add(letter);
            VisualElement rows = Box("rs-podium-rows");
            int place = 0;
            foreach (ClassifiedCar r in race.Classification.Where(x => x.CarClass == c && !x.Retired).Take(3))
            {
                place++;
                VisualElement row = Box("rs-row", "rs-podium-row");
                row.EnableInClassList("me", r.TeamId == GameActions.PlayerTeamId);
                row.Add(Menus.Text(Num(place), "rs-place p" + place, true));
                row.Add(Menus.Text(r.TeamName, "rs-team-name", true));
                row.Add(Menus.Text("P" + Num(r.Position), "rs-overall"));
                rows.Add(row);
            }
            block.Add(rows);
            return block;
        }

        // ------------------------------------------------------------------ the player's race

        private void RenderPlayer(GameState game, RaceSummary race, RaceHistoryEntry entry)
        {
            VisualElement side = Menus.SideBody;
            ClassifiedCar me = race.Player;

            VisualElement hero = Box("rh-hero");
            hero.Add(Menus.Text("P" + Num(me.Position), "rh-big", true));
            VisualElement inClass = Box("rs-inclass");
            inClass.Add(ClassLabels.Make(me.CarClass, "cls-md", Menus.Bold));
            inClass.Add(Menus.Text("P" + Num(me.ClassPosition), "rs-inclass-pos", true));
            hero.Add(inClass);
            VisualElement laps = Box("rs-stat");
            laps.Add(Menus.Text(Num(me.Laps), "rs-stat-value", true));
            laps.Add(Menus.Text("LAPS", "rs-stat-label"));
            hero.Add(laps);
            hero.Add(me.Retired ? Menus.Text("DNF", "rs-hero-dnf", true) : Menus.Text(Menus.Money(me.PrizeMoney), "rs-hero-prize", true));
            side.Add(hero);

            if (race.PositionByLap.Count == 0) return;

            colours.Clear();
            foreach (StintRecord s in race.Stints)
                if (!colours.ContainsKey(s.DriverId)) colours[s.DriverId] = RaceSummaryScreen.DriverColours[colours.Count % RaceSummaryScreen.DriverColours.Length];

            VisualElement chartBox = Box("rh-chart");
            var chart = new PositionChart(race, LapScale(race), Colour, Menus.SemiBold);
            chartBox.Add(chart);
            Label readout = Menus.Text("", "rs-readout", true);
            readout.style.display = DisplayStyle.None;
            chartBox.Add(readout);
            chart.Hovered += (lap, position) =>
            {
                readout.style.display = lap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                readout.text = "L" + Num(lap) + "   P" + Num(position);
            };
            side.Add(chartBox);

            VisualElement log = Box("rh-log");
            VisualElement tabs = Box("rs-tabs");
            log.Add(tabs);
            ScrollView list = Scroll("rs-scroll");
            log.Add(list);
            side.Add(log);
            RenderLog(game, race, tabs, list);
        }

        private Color Colour(string driverId) => colours.TryGetValue(driverId, out Color c) ? c : Color.white;

        /// <summary>Leader laps per lap of the player's, as the race summary places stints, stops and issues.</summary>
        private static float LapScale(RaceSummary race)
        {
            int leaderLaps = race.PositionByLap.Count;
            ClassifiedCar me = race.Player;
            int pace = me.Retired ? race.Classification.FirstOrDefault(c => c.CarClass == me.CarClass && !c.Retired)?.Laps ?? me.Laps : me.Laps;
            return pace > 0 && leaderLaps > 0 ? leaderLaps / (float)pace : 1f;
        }

        private void RenderLog(GameState game, RaceSummary race, VisualElement tabs, ScrollView list)
        {
            void Show(string view)
            {
                logView = view;
                RenderLog(game, race, tabs, list);
                list.scrollOffset = Vector2.zero;
            }

            tabs.Clear();
            foreach ((string view, string name, int count) in new[] { ("stints", "STINTS", race.Stints.Count), ("stops", "STOPS", race.PitStops.Count), ("issues", "ISSUES", race.Issues.Count) })
            {
                Label tab = Tab(Menus, name, logView == view, () => Show(view));
                tab.Add(Menus.Text(Num(count), "rs-tab-count", true));
                tabs.Add(tab);
            }

            list.Clear();
            switch (logView)
            {
                case "stints":
                    foreach (StintRecord s in race.Stints)
                    {
                        VisualElement row = Box("rs-row", "rs-stint");
                        row.Add(Swatch(Colour(s.DriverId)));
                        row.Add(Menus.Text(s.DriverName, "rs-driver", true));
                        row.Add(Menus.Text(Num(s.StartLap) + " to " + Num(s.EndLap), "rs-range"));
                        row.Add(Menus.Text(Num(s.EndLap - s.StartLap + 1), "rs-count"));
                        list.Add(row);
                    }
                    break;
                case "stops":
                    foreach (PitStopRecord s in race.PitStops)
                    {
                        VisualElement row = Box("rs-row");
                        row.Add(Menus.Text("L" + Num(s.Lap), "rs-at"));
                        if (s.DriverIn != s.DriverOut)
                        {
                            row.Add(Swatch(Colour(s.DriverIn)));
                            row.Add(Menus.Text(RaceReplay.DriverName(game, s.DriverIn), "rs-driver", true));
                        }
                        list.Add(row);
                    }
                    break;
                case "issues":
                    foreach (IssueRecord i in race.Issues)
                    {
                        VisualElement row = Box("rs-row");
                        row.Add(Menus.Text("L" + Num(i.Lap), "rs-at"));
                        row.Add(Menus.Text(i.TemplateId, "rh-issue-name", true));
                        row.Add(Swatch(Colour(i.DriverId)));
                        if (i.FixedLap == null) row.Add(Menus.Text("DNF", "rs-dnf", true));
                        list.Add(row);
                    }
                    break;
            }
        }
    }
}
