using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TarmacGauntlet.Career;
using TarmacGauntlet.Management;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MenuPreview
{
    /// <summary>
    /// Runs the management menus outside Unity on a seeded two-race career and writes every screen's element tree
    /// as HTML, one file per screen, for build-viewer.py to lay out with the converted Menus.uss.
    /// Usage: MenuPreview &lt;unity Assets folder&gt; &lt;output folder&gt;
    /// </summary>
    internal static class Program
    {
        private static string outDir = "";
        private static readonly List<(string Name, string Shot)> shots = new List<(string, string)>();

        private static int Main(string[] args)
        {
            Resources.AssetsRoot = Html.AssetsRoot = Path.GetFullPath(args[0]);
            outDir = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(outDir);

            string saves = Path.Combine(Path.GetTempPath(), "menu-preview-" + Guid.NewGuid().ToString("N"));
            GameSession.Configure(new CareerStore(saves));
            var rng = new System.Random(7);
            GameSession.NewCareer(new NewGameOptions
            {
                PlayerName = "Ivan Reyes",
                Nationality = "sg",
                TeamName = "Tarmac Works",
                Logo = "bolt",
                TeamColour = "#e10600",
                Skills = new PlayerSkills { Driver = 9, Engineer = 4, Business = 2 },
            }, rng.NextDouble);
            Career(rng);

            // The race summary straight after the second race, over the circuit.
            Race(rng, 2);
            var summary = new GameObject("summary").AddComponent<RaceSummaryScreen>();
            summary.RunStart();
            Scheduler.Flush();
            VisualElement summaryRoot = UIDocument.All.Last().rootVisualElement;
            Snap("race-summary", "track", summaryRoot);

            // Continue opens the Skill points step; two points go on Driver.
            Click(summaryRoot.Q(className: "corner"));
            Snap("skill-points", "track", summaryRoot);
            for (int i = 0; i < 2; i++) Click(summaryRoot.Query<VisualElement>(className: "skill-step").ToList()[1]);
            Snap("skill-points-spent", "track", summaryRoot);

            // Then the paper is read, the year ends, and the Team HQ opens on the hub.
            GameSession.Apply(g => Season.AdvanceYear(g, rng.NextDouble));
            var cameraObject = new GameObject("camera");
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<HqCamera>();
            var go = new GameObject("menus");
            Menus menus = go.AddComponent<Menus>();
            menus.RunStart();
            Scheduler.Flush();
            VisualElement root = UIDocument.All.Last().rootVisualElement;
            Snap("hub", "hub", root);

            foreach (string slug in Menus.Groups.SelectMany(g => g.Select(s => s.Slug)))
            {
                if (slug == "newspaper") continue;
                menus.Open(slug);
                Scheduler.Flush();
                Snap(slug, slug, root);
                if (slug == "new-car-dealer" || slug == "second-hand-dealer")
                {
                    // Old dealer rows were g-row, shared rows are row with a click.
                    Click(root.Query<VisualElement>(className: "g-row").ToList().FirstOrDefault(r => !r.ClassListContains("g-hd"))
                        ?? root.Query<VisualElement>(className: "click").ToList().FirstOrDefault(r => r.ClassListContains("row")));
                    Snap(slug + "-picked", slug, root);
                }
            }

            // A market driver picked in Drivers, and the stat menu open.
            GameState game = GameSession.State;
            string free = game.Drivers.Where(d => !game.Contracts.Any(c => c.DriverId == d.Id && c.RemainingYears > 0)).Select(d => d.Id).First();
            DriversPanel.Show(menus, free);
            Scheduler.Flush();
            Snap("drivers-market", "drivers", root);

            Call(menus, "Go", Enum("Mode", "Hub"), null);
            Click(root.Q(className: "race") ?? root.Q(className: "corner"));
            Scheduler.Flush();
            Snap("lineup", "hub", root);

            Call(menus, "Go", Enum("Mode", "Hub"), null);
            Call(menus, "OpenSettings");
            Scheduler.Flush();
            Snap("settings", "hub", root);
            Call(menus, "SetOverlay", Enum("Overlay", "None"));

            Call(menus, "Go", Enum("Mode", "Main"), null);
            Scheduler.Flush();
            Snap("main-menu", "main-menu", root);
            Call(menus, "Go", Enum("Mode", "NewGame"), null);
            Scheduler.Flush();
            Snap("new-game", "main-menu", root);
            Call(menus, "NewGame");
            Scheduler.Flush();
            Snap("overwrite-dialog", "main-menu", root);

            File.WriteAllText(Path.Combine(outDir, "shots.txt"), string.Join("\n", shots.Select(s => s.Name + " " + s.Shot)));
            File.WriteAllText(Path.Combine(outDir, "images.txt"), string.Join("\n", Html.Images.OrderBy(i => i)));
            return 0;
        }

        /// <summary>Two seasons: a car, two hired drivers, crew and stock, then the first race and its year end.</summary>
        private static void Career(System.Random rng)
        {
            GameSession.Apply(g =>
            {
                g = GameActions.BuyCar(g, new CarInstance { Id = "player-car-1", ModelId = "e-10", Condition = 82, Age = 2 }, 0, "Kaiser Oboe");
                g = GameActions.BuyCar(g, new CarInstance { Id = "player-car-2", ModelId = "f-05", Condition = 54, Age = 5 }, 0, "Liberty Collier");
                g = GameActions.EnterCar(g, "player-car-1");
                foreach (Driver d in g.Drivers.Where(d => !g.Contracts.Any(c => c.DriverId == d.Id)).Take(2).ToList())
                    g = GameActions.HireDriver(g, d.Id, d.Name, 2, 0);
                g = GameActions.SetCrewSize(g, 6);
                return g with { Teams = g.Teams.Select(t => t is PlayerTeam p ? p with { TyreSets = 5, SpareParts = 3, Budget = 184_250 } : t).ToArray() };
            });
            Race(rng, 1);
            GameSession.Apply(g => Season.AdvanceYear(g, rng.NextDouble));
        }

        /// <summary>A 120 lap race where quicker classes lap faster and the player stops every 30 laps.</summary>
        private static void Race(System.Random rng, int seed)
        {
            var race = new RaceDay(RaceWeekend.Begin(GameSession.State), rng.NextDouble);
            CarRaceLog me = race.Cars.First(c => c.Car.Player);
            var pace = race.Cars.ToDictionary(c => c.Index, c => 100 - 6 * ClassSpeed(c.Car.Model.CarClass) + rng.NextDouble() * 3);
            var time = race.Cars.ToDictionary(c => c.Index, _ => 0.0);
            const int laps = 120;
            for (int lap = 1; lap <= laps; lap++)
            {
                foreach (CarRaceLog c in race.Cars)
                {
                    if (c.Retired) continue;
                    double t = pace[c.Index] + rng.NextDouble() * 2;
                    time[c.Index] += t;
                    race.CompleteLap(c.Index, time[c.Index], t, InstructionMode.Push, lap == laps);
                }
                if (lap % 30 == 0 && !me.Retired)
                {
                    me.Pit(lap % 60 == 0);
                    me.PitDone(31.5 + seed, 40);
                }
            }
            RaceWeekend.Finish(race);
        }

        private static int ClassSpeed(CarClass c) => c == CarClass.F1 ? 7 : (int)c + 1;

        private static void Snap(string name, string shot, VisualElement root)
        {
            File.WriteAllText(Path.Combine(outDir, name + ".html"), Html.Write(root));
            shots.Add((name, shot));
        }

        private static void Click(VisualElement e)
        {
            if (e == null) return;
            e.Send(new ClickEvent());
            Scheduler.Flush();
        }

        private static object Enum(string type, string value) =>
            System.Enum.Parse(typeof(Menus).GetNestedType(type, BindingFlags.NonPublic), value);

        private static void Call(object target, string method, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m.Invoke(target, args.Length == 0 ? null : args);
        }
    }
}
