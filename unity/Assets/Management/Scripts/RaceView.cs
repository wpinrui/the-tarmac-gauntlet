using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// One race as the race summary and the race history both show it: the results overall, by class and as class
    /// podiums, the player's finish, the position chart with its legend, and the stints, stops and issues. A race just
    /// run (<paramref name="live"/>) also has stop times, fuel, best laps and the laps issues were fixed on. A car that
    /// retired has what put it out as its last issue, by name ("Gearbox seizure"), as the race history has it.
    /// </summary>
    internal sealed class RaceView
    {
        /// <summary>Driver colours in the order they first drove.</summary>
        private static readonly Color[] DriverColours =
        {
            Color.white, new Color32(0x5a, 0xb8, 0xff, 0xff), new Color32(0xff, 0xb0, 0x20, 0xff),
            new Color32(0xc5, 0x8c, 0xff, 0xff), new Color32(0x3d, 0xdc, 0x84, 0xff),
        };

        /// <summary>The tabs showing, kept by the screen between races.</summary>
        internal sealed class Views
        {
            public string Results = "all", Log = "stints";
        }

        private readonly RaceSummary race;
        private readonly bool live;
        private readonly Views views;
        private readonly Dictionary<string, Color> colours = new Dictionary<string, Color>();
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        /// <summary>The player's issues, and what put the car out if it retired.</summary>
        private readonly IReadOnlyList<IssueRecord> issues;

        internal RaceView(RaceSummary race, bool live, Views views)
        {
            this.race = race;
            this.live = live;
            this.views = views;
            foreach (StintRecord s in race.Stints)
            {
                names[s.DriverId] = s.DriverName;
                if (!colours.ContainsKey(s.DriverId)) colours[s.DriverId] = DriverColours[colours.Count % DriverColours.Length];
            }
            issues = race.Issues;
            // The race history already lists the retirement among the issues (RaceReplay); a race just run doesn't.
            if (live && race.Player is ClassifiedCar me && me.Retired)
                issues = race.Issues.Append(new IssueRecord
                {
                    Lap = me.Laps,
                    TemplateId = me.FailureId ?? (me.RetirementReason ?? FailureType.Mechanical).ToString(),
                    DriverId = race.Stints.Count > 0 ? race.Stints[race.Stints.Count - 1].DriverId : "",
                    FixedLap = null,
                }).ToArray();
        }

        private bool IsMe(ClassifiedCar r) => r.CarId == race.Player?.CarId;

        private Color Colour(string driverId) => colours.TryGetValue(driverId, out Color c) ? c : Color.white;

        private string DriverName(string id)
        {
            if (names.TryGetValue(id, out string n)) return n;
            return GameSession.State == null ? id : RaceReplay.DriverName(GameSession.State, id);
        }

        private static string IssueName(string templateId) =>
            IssueTemplates.AllIssueTemplates.FirstOrDefault(t => t.Id == templateId)?.Description ?? templateId;

        // ------------------------------------------------------------------ results

        private CarClass[] Classes => ClassLabels.Order.Where(c => race.Classification.Any(r => r.CarClass == c)).ToArray();

        /// <summary>Fills <paramref name="into"/> with the results tabs, their column heads and the list.</summary>
        internal void Results(VisualElement into)
        {
            VisualElement tabs = Box("tabs");
            VisualElement head = HeadRow(Head("POS", "c-pos"), Box("c-class"), Fill(), Head("LAPS", "c-laps"), Head("PRIZE", "c-prize"));
            ScrollView list = Scroll();
            into.Add(tabs);
            into.Add(head);
            into.Add(list);
            RenderResults(tabs, head, list);
        }

        private void RenderResults(VisualElement tabs, VisualElement head, ScrollView list)
        {
            CarClass[] classes = Classes;
            if (views.Results != "all" && views.Results != "podiums" && classes.All(c => c.ToString() != views.Results)) views.Results = "all";

            void Show(string view)
            {
                views.Results = view;
                RenderResults(tabs, head, list);
                list.scrollOffset = Vector2.zero;
            }

            tabs.Clear();
            tabs.Add(Tab("ALL", views.Results == "all", () => Show("all")));
            foreach (CarClass c in classes)
            {
                string key = c.ToString();
                tabs.Add(ClassTab(c, views.Results == key, () => Show(key)));
            }
            tabs.Add(IconTab("trophy", views.Results == "podiums", () => Show("podiums")));

            head.style.display = views.Results == "podiums" ? DisplayStyle.None : DisplayStyle.Flex;
            list.Clear();
            if (views.Results == "podiums")
            {
                foreach (CarClass c in classes) list.Add(Podium(c));
                return;
            }
            bool overall = views.Results == "all";
            foreach (ClassifiedCar r in overall ? race.Classification : race.Classification.Where(x => x.CarClass.ToString() == views.Results))
            {
                VisualElement row = Row(null, IsMe(r) ? "me" : null);
                row.EnableInClassList("out", r.Retired);
                row.Add(Place(Num(overall ? r.Position : r.ClassPosition), overall ? r.Position : r.ClassPosition, "c-pos"));
                row.Add(ClassLabels.Make(r.CarClass, "cls-sm"));
                VisualElement team = ResultTeam(r);
                team.AddToClassList("fill");
                row.Add(team);
                row.Add(Cell(Num(r.Laps), "c-laps"));
                Label prize = Cell(Money(r.PrizeMoney), "c-prize", true);
                prize.AddToClassList("v-hi");
                row.Add(prize);
                list.Add(row);
            }
            if (overall && race.Player != null) OpenOn(list, race.Player.Position - 1);
        }

        /// <summary>Scrolls the list to its row at <paramref name="index"/> once the list knows its height.</summary>
        private static void OpenOn(ScrollView list, int index)
        {
            list.RegisterCallback<GeometryChangedEvent>(Centre);
            void Centre(GeometryChangedEvent _)
            {
                list.UnregisterCallback<GeometryChangedEvent>(Centre);
                if (index < 0 || index >= list.contentContainer.childCount) return;
                VisualElement row = list.contentContainer[index];
                list.scrollOffset = new Vector2(0, Mathf.Max(0, row.layout.y - list.contentViewport.layout.height / 2 + row.layout.height / 2));
            }
        }

        private VisualElement Podium(CarClass c)
        {
            VisualElement block = Box("podium");
            Label letter = Text("", "podium-class", true);
            ClassLabels.Tint(letter, c);
            block.Add(letter);
            VisualElement rows = Box("podium-rows");
            int place = 0;
            foreach (ClassifiedCar r in race.Classification.Where(x => x.CarClass == c && !x.Retired).Take(3))
            {
                place++;
                VisualElement row = Row(null, "two-line", IsMe(r) ? "me" : null);
                row.Add(Place(Num(place), place, "c-place"));
                VisualElement team = PodiumTeam(r);
                team.AddToClassList("fill");
                row.Add(team);
                row.Add(Cell("P" + Num(r.Position), "c-overall"));
                rows.Add(row);
            }
            block.Add(rows);
            return block;
        }

        // ------------------------------------------------------------------ the player's race

        /// <summary>Fills <paramref name="into"/> with the player's finish: overall, in class, laps, best lap and prize.</summary>
        internal void Hero(VisualElement into)
        {
            ClassifiedCar me = race.Player;
            into.AddToClassList("hero");
            into.Add(Text("P" + Num(me.Position), "hero-pos", true));
            VisualElement inClass = Box("hero-class");
            inClass.Add(ClassLabels.Make(me.CarClass, "cls-md"));
            inClass.Add(Text("P" + Num(me.ClassPosition), "hero-class-pos", true));
            into.Add(inClass);
            into.Add(Figure(Num(me.Laps), "LAPS", null));
            if (me.BestLap is double best) into.Add(Figure(LapTime(best), "BEST LAP", "best"));
            Label end = Text(Money(me.PrizeMoney), "hero-prize", true);
            end.AddToClassList("v-hi");
            into.Add(end);
        }

        private static VisualElement Figure(string value, string label, string cls)
        {
            VisualElement figure = Box("hero-figure");
            Label v = Text(value, "hero-value", true);
            if (cls != null) v.AddToClassList(cls);
            figure.Add(v);
            figure.Add(Text(label, "hero-label"));
            return figure;
        }

        /// <summary>Whether the player raced.</summary>
        internal bool HasPlayer => race.Player != null;

        /// <summary>Whether the race kept its laps, so it has a chart and a log.</summary>
        internal bool HasLaps => race.Player != null && race.PositionByLap.Count > 0;

        /// <summary>Fills <paramref name="into"/> with the position chart, its lap and position readout, and its legend.</summary>
        internal void Chart(VisualElement into)
        {
            VisualElement box = Box("chart");
            var chart = new PositionChart(race, LapScale(race), Colour, SemiBold);
            box.Add(chart);
            Label readout = Text("", "chart-readout", true);
            readout.style.display = DisplayStyle.None;
            box.Add(readout);
            chart.Hovered += (lap, position) =>
            {
                readout.style.display = lap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                readout.text = "L" + Num(lap) + "   P" + Num(position);
            };
            into.Add(box);

            VisualElement legend = Box("legend");
            foreach (string id in colours.Keys)
            {
                VisualElement item = Box("legend-item");
                item.Add(Swatch(Colour(id)));
                item.Add(Text(DriverName(id), "legend-text"));
                legend.Add(item);
            }
            VisualElement issue = Box("legend-item");
            issue.Add(Box("legend-dot"));
            issue.Add(Text("ISSUE", "legend-text"));
            legend.Add(issue);
            VisualElement stop = Box("legend-item");
            stop.Add(Box("legend-tick"));
            stop.Add(Text("PIT STOP", "legend-text"));
            legend.Add(stop);
            into.Add(legend);
        }

        /// <summary>Fills <paramref name="into"/> with the stints, stops and issues tabs, their heads and the list.</summary>
        internal void Log(VisualElement into)
        {
            VisualElement tabs = Box("tabs");
            VisualElement head = Box("head-row");
            ScrollView list = Scroll();
            into.Add(tabs);
            into.Add(head);
            into.Add(list);
            RenderLog(tabs, head, list);
        }

        private void RenderLog(VisualElement tabs, VisualElement head, ScrollView list)
        {
            void Show(string view)
            {
                views.Log = view;
                RenderLog(tabs, head, list);
                list.scrollOffset = Vector2.zero;
            }

            tabs.Clear();
            foreach ((string view, string name, int count) in new[] { ("stints", "STINTS", race.Stints.Count), ("stops", "STOPS", race.PitStops.Count), ("issues", "ISSUES", issues.Count) })
                tabs.Add(CountTab(name, count, views.Log == view, () => Show(view)));

            head.Clear();
            list.Clear();
            switch (views.Log)
            {
                case "stints":
                    bool bests = race.Stints.Any(s => s.BestLap != null);
                    head.Add(Box("c-swatch"));
                    head.Add(Fill());
                    head.Add(Head("LAPS", "c-range"));
                    head.Add(Head("TOTAL", "c-count"));
                    if (bests) head.Add(Head("BEST", "c-lap"));
                    double? quickest = race.Player.BestLap;
                    foreach (StintRecord s in race.Stints)
                    {
                        VisualElement row = Row();
                        row.Add(Swatch(Colour(s.DriverId)));
                        row.Add(Cell(s.DriverName, "fill", true));
                        row.Add(Cell(Num(s.StartLap) + " to " + Num(s.EndLap), "c-range"));
                        row.Add(Cell(Num(s.EndLap - s.StartLap + 1), "c-count"));
                        if (bests)
                        {
                            Label best = Cell(s.BestLap is double b ? LapTime(b) : "", "c-lap");
                            best.EnableInClassList("best", s.BestLap != null && s.BestLap == quickest);
                            row.Add(best);
                        }
                        list.Add(row);
                    }
                    break;
                case "stops":
                    head.Add(Head("LAP", "c-at"));
                    if (live)
                    {
                        head.Add(Head("TIME", "c-secs"));
                        head.Add(Box("c-tyre"));
                        head.Add(Head("FUEL", "c-fuel"));
                    }
                    head.Add(Head("NEW DRIVER", "fill"));
                    foreach (PitStopRecord s in race.PitStops)
                    {
                        VisualElement row = Row();
                        row.Add(Cell(Num(s.Lap), "c-at"));
                        if (live)
                        {
                            row.Add(Cell(s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s", "c-secs"));
                            VisualElement tyre = Icon("tyre", "c-tyre");
                            tyre.style.visibility = s.TyresChanged ? Visibility.Visible : Visibility.Hidden;
                            row.Add(tyre);
                            row.Add(Cell(Num((int)Math.Round(s.FuelAdded)) + " L", "c-fuel"));
                        }
                        VisualElement who = Box("fill", "who");
                        if (s.DriverIn != s.DriverOut)
                        {
                            who.Add(Swatch(Colour(s.DriverIn)));
                            who.Add(Text(DriverName(s.DriverIn), "who-name", true));
                        }
                        if (s.FixedIssueIds.Count > 0) who.Add(Text(string.Join(", ", s.FixedIssueIds.Select(IssueName)), "who-fixed"));
                        row.Add(who);
                        list.Add(row);
                    }
                    break;
                case "issues":
                    head.Add(Head("LAP", "c-at"));
                    head.Add(Head("ISSUE", "fill"));
                    head.Add(Head(live ? "FIXED" : "", "c-fixed"));
                    foreach (IssueRecord i in issues)
                    {
                        VisualElement row = Row();
                        row.Add(Cell(Num(i.Lap), "c-at"));
                        VisualElement what = Box("fill", "who");
                        what.Add(Swatch(Colour(i.DriverId)));
                        what.Add(Text(IssueName(i.TemplateId), "who-name", true));
                        row.Add(what);
                        if (i.FixedLap is int f && live) row.Add(Cell(Num(f), "c-fixed"));
                        else row.Add(Cell(i.FixedLap == null ? "DNF" : "", "c-fixed", true));
                        row[row.childCount - 1].EnableInClassList("v-lo", i.FixedLap == null);
                        list.Add(row);
                    }
                    break;
            }
        }
    }
}
