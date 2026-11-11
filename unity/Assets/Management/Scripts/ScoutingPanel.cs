using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.OfficeKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Scouting report: one class at a time, opening on the player's, with every team's race car stats, its crew and
    /// its last finish. A column header sorts by it. Clicking a team shows its car and drivers in the side pane.
    /// </summary>
    public sealed class ScoutingPanel : OfficePanel
    {
        public const string Slug = "scouting-report";

        private enum Column { Pace, Reliability, Tyres, Fuel, Condition, Crew, Last }

        private static readonly (Column Key, string Name, string Tip)[] Columns =
        {
            (Column.Pace, "PACE", "The car's speed: the average of its power and handling, upgrades included."),
            (Column.Reliability, "REL", "Reliability. Higher means fewer issues and failures."),
            (Column.Tyres, "TYRE", "Tyre durability. Higher wears tyres more slowly."),
            (Column.Fuel, "FUEL", "Fuel efficiency. Higher uses less fuel per lap."),
            (Column.Condition, "COND", "Car condition. Issues come more often as it falls."),
            (Column.Crew, "CREW", "Pit crew headcount. More crew means quicker stops."),
            (Column.Last, "LAST", "Finishing position overall in the last race."),
        };

        /// <summary>One team as the report shows it.</summary>
        private sealed class Scout
        {
            public BaseTeam Team;
            public CarClass Class;
            public CarModel Model;
            public int Age;
            public int? Last;
            public int[] Values; // by Column, Last as 0 when unknown
            public List<(string Name, string Id, int Ovr)> Drivers;
            public bool Mine => Team is PlayerTeam;
        }

        // Kept while the game runs, so the screen opens as it was left.
        private CarClass? shownClass;
        private Column sort = Column.Pace;
        private string picked;

        protected override string ScreenSlug => Slug;

        protected override void OnOpened()
        {
            // Each visit opens on the player's own class and team.
            shownClass = null;
            picked = null;
        }

        protected override void Render()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            Body.Clear();
            Menus.SideBody.Clear();

            List<Scout> all = game.Teams.Select(t => Read(game, t)).Where(s => s != null).ToList();
            CarClass[] classes = ClassLabels.Order.Where(c => all.Any(s => s.Class == c)).ToArray();
            if (classes.Length == 0) return;
            Scout mine = all.FirstOrDefault(s => s.Mine);
            CarClass cls = shownClass.HasValue && classes.Contains(shownClass.Value) ? shownClass.Value : mine?.Class ?? classes[0];

            VisualElement tabs = Box("rs-tabs");
            foreach (CarClass c in classes)
            {
                CarClass pick = c;
                Label tab = Tab(Menus, ClassLabels.Name(c), c == cls, () =>
                {
                    shownClass = pick;
                    picked = null;
                    Render();
                });
                tab.AddToClassList("rs-tab-class");
                tab.style.color = ClassLabels.Colour(c);
                tabs.Add(tab);
            }
            Body.Add(tabs);

            VisualElement header = Box("of-head-row");
            header.Add(Box("sc-team"));
            foreach ((Column key, string name, string tip) in Columns)
            {
                Label head = Menus.Text(name, "of-head");
                head.AddToClassList("sc-cell");
                head.AddToClassList("sort");
                head.EnableInClassList("on", key == sort);
                Menus.Tooltip(head, tip);
                head.RegisterCallback<ClickEvent>(_ =>
                {
                    sort = key;
                    Render();
                });
                header.Add(head);
            }
            Body.Add(header);

            List<Scout> rows = all.Where(s => s.Class == cls).ToList();
            rows.Sort(Compare);
            Scout chosen = rows.FirstOrDefault(s => s.Team.Id == picked) ?? rows.FirstOrDefault(s => s.Mine) ?? rows[0];

            ScrollView list = Scroll("rs-scroll");
            foreach (Scout s in rows)
            {
                VisualElement row = Box("of-row", "of-click", "of-two-line");
                row.EnableInClassList("me", s.Mine);
                row.EnableInClassList("pick", s == chosen);
                VisualElement team = Box("sc-team");
                team.Add(TeamLines(s.Team, s.Team.Name, s.Model, Menus.Text));
                row.Add(team);
                foreach ((Column key, _, _) in Columns)
                {
                    int v = s.Values[(int)key];
                    string text = key == Column.Condition ? Num(v) + "%" : key == Column.Last ? (s.Last.HasValue ? "P" + Num(v) : "") : Num(v);
                    Label cell = Menus.Text(text, "sc-cell", true);
                    if (key == Column.Condition) cell.AddToClassList(v >= 60 ? "v-hi" : v >= 50 ? "v-mid" : "v-lo");
                    row.Add(cell);
                }
                string id = s.Team.Id;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    picked = id;
                    shownClass = cls;
                    Render();
                });
                list.Add(row);
            }
            Body.Add(list);

            Detail(chosen);
        }

        private int Compare(Scout a, Scout b)
        {
            if (sort == Column.Last) return (a.Last ?? int.MaxValue).CompareTo(b.Last ?? int.MaxValue);
            return b.Values[(int)sort].CompareTo(a.Values[(int)sort]);
        }

        private void Detail(Scout s)
        {
            VisualElement side = Menus.SideBody;
            VisualElement title = Box("of-named");
            VisualElement flag = Flag(s.Team);
            flag.AddToClassList("of-flag-lg");
            title.Add(flag);
            title.Add(Menus.Text(s.Team.Name.ToUpperInvariant(), "sc-det-name", true));
            side.Add(title);
            VisualElement car = Box("sc-det-car");
            car.Add(ClassLabels.Make(s.Class, "cls-sm", Menus.Bold));
            car.Add(Menus.Text(s.Model.Name, "sc-det-car-name", true));
            side.Add(car);
            VisualElement meta = Box("sc-det-meta");
            meta.Add(Menus.Text("CAR AGE", "sc-det-meta-label"));
            meta.Add(Menus.Text(Num(s.Age), "sc-det-meta-value", true));
            side.Add(meta);
            if (s.Drivers.Count > 0)
            {
                VisualElement head = Box("of-head-row", "sc-driver-head");
                head.Add(Box("of-fill"));
                Label ovrHead = Menus.Text("OVR", "of-head");
                ovrHead.AddToClassList("sc-ovr-head");
                head.Add(ovrHead);
                side.Add(head);
            }
            foreach ((string name, string id, int ovr) in s.Drivers)
            {
                VisualElement row = Box("of-row", "sc-driver");
                VisualElement fill = Box("of-fill");
                fill.Add(DriverLink(Menus, name, id, "sc-driver-name"));
                row.Add(fill);
                Label value = Menus.Text(Num(ovr), "sc-ovr", true);
                value.AddToClassList(ovr >= 60 ? "v-hi" : ovr >= 50 ? "v-mid" : "v-lo");
                row.Add(value);
                side.Add(row);
            }
        }

        /// <summary>A team with a race car entered, or null without one.</summary>
        private static Scout Read(GameState game, BaseTeam team)
        {
            CarInstance car = team.Cars.FirstOrDefault(c => c.Id == team.EnteredCarId);
            CarModel model = car == null ? null : game.CarModels.FirstOrDefault(m => m.Id == car.ModelId) ?? CarModels.All.FirstOrDefault(m => m.Id == car.ModelId);
            if (model == null) return null;
            CarStats stats = EffectiveStats.CalculateEffectiveStats(car, model);
            int? last = game.RaceHistory.LastOrDefault()?.Results.FirstOrDefault(r => r.TeamId == team.Id)?.Position;
            return new Scout
            {
                Team = team,
                Class = model.CarClass,
                Model = model,
                Age = car.Age,
                Last = last,
                Values = new[]
                {
                    Round((stats.Power + stats.Handling) / 2),
                    Round(stats.Reliability),
                    Round(stats.TyreDurability),
                    Round(stats.FuelEfficiency),
                    Round(car.Condition),
                    team.CrewSize,
                    last ?? 0,
                },
                Drivers = Drivers(game, team),
            };
        }

        /// <summary>The team's drivers with their OVR, best first. The player's lineup includes the player.</summary>
        private static List<(string, string, int)> Drivers(GameState game, BaseTeam team)
        {
            var drivers = new List<(string Name, string Id, int Ovr)>();
            if (team is PlayerTeam p)
            {
                foreach (string id in p.Lineup)
                {
                    if (id == RaceEntry.PlayerCharacterDriverId) drivers.Add((p.PlayerName, id, p.Skills.Driver * 5));
                    else if (game.Drivers.FirstOrDefault(d => d.Id == id) is Driver d) drivers.Add((d.Name, d.Id, Ovr(d)));
                }
                return drivers;
            }
            foreach (Contract c in game.Contracts.Where(c => c.TeamId == team.Id && c.RemainingYears > 0))
                if (game.Drivers.FirstOrDefault(d => d.Id == c.DriverId) is Driver d) drivers.Add((d.Name, d.Id, Ovr(d)));
            return drivers.OrderByDescending(d => d.Ovr).ToList();
        }

        private static int Ovr(Driver d) => Round(DriverLifecycle.TotalDriverStats(DriverLifecycle.CalculateDriverStats(d)) / 5);

        private static int Round(double v) => (int)JsMath.Round(v);
    }
}
