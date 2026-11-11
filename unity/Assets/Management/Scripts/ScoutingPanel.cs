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
    /// The Scouting report: one class at a time, opening on the player's, with every team's race car stats, its crew and
    /// its last finish. A column header sorts by it. Clicking a team shows its car and drivers in the side pane.
    /// </summary>
    public sealed class ScoutingPanel : MenuPanel
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

        protected override bool Shows(string slug) => slug == Slug;

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

            VisualElement tabs = Box("tabs");
            foreach (CarClass c in classes)
            {
                CarClass pick = c;
                tabs.Add(ClassTab(c, c == cls, () =>
                {
                    shownClass = pick;
                    picked = null;
                    Render();
                }));
            }
            Body.Add(tabs);

            VisualElement header = HeadRow(Fill());
            foreach ((Column key, string name, string tip) in Columns)
            {
                Label head = Head(name, "c-scout");
                head.AddToClassList("sort");
                head.EnableInClassList("on", key == sort);
                Tips.Attach(head, tip);
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

            ScrollView list = Scroll();
            foreach (Scout s in rows)
            {
                string id = s.Team.Id;
                VisualElement row = Row(() =>
                {
                    picked = id;
                    shownClass = cls;
                    Render();
                }, "two-line", s.Mine ? "me" : null);
                row.EnableInClassList("on", s == chosen);
                VisualElement team = TeamLines(s.Team, s.Team.Name, s.Model);
                team.AddToClassList("fill");
                row.Add(team);
                foreach ((Column key, _, _) in Columns)
                {
                    int v = s.Values[(int)key];
                    string text = key == Column.Condition ? Num(v) + "%" : key == Column.Last ? (s.Last.HasValue ? "P" + Num(v) : "") : Num(v);
                    Label cell = Cell(text, "c-scout", true);
                    if (key == Column.Condition) Banded(cell, v);
                    row.Add(cell);
                }
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
            side.Add(TeamTitle(s.Team));
            VisualElement car = Box("detail-meta");
            car.Add(ClassLabels.Make(s.Class, "cls-sm"));
            car.Add(Text(s.Model.Name, "detail-car", true));
            car.Add(Text(Years(s.Age), "meta-item"));
            side.Add(car);
            if (s.Drivers.Count > 0) side.Add(HeadRow(Fill(), Head("OVR", "c-ovr")));
            foreach ((string name, string id, int ovr) in s.Drivers)
            {
                VisualElement row = Row();
                VisualElement fill = Fill();
                fill.Add(DriverLink(Menus, name, id, "link-name"));
                row.Add(fill);
                row.Add(Banded(Cell(Num(ovr), "c-ovr", true), ovr));
                side.Add(row);
            }
        }

        /// <summary>A team with a race car entered, or null without one.</summary>
        private static Scout Read(GameState game, BaseTeam team)
        {
            CarInstance car = RaceCar(team);
            CarModel model = car == null ? null : Model(car.ModelId);
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

        private static int Round(double v) => (int)JsMath.Round(v);
    }
}
