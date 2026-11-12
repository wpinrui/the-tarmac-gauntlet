using System;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Team HQ's debug menu, on Tab, in the editor and in every build: it changes the career so any part
    /// of the game can be tested without playing up to it (<see cref="DebugActions"/>). Money, any car for free, every
    /// car mint or wrecked, any free agent hired for nothing, every contract ended, tyres, spares, crew and skills.
    /// It skips the race too: the race runs in an instant with your car where you pick, and the race summary opens over
    /// the HQ, then the paper, as after a race. It ends the year without a race, and opens the latest paper again. Anywhere in the HQ, even on the main menu, it starts a
    /// ready-made career in one click: year 1, or years 5, 10 and 15 with a team built up to match.
    /// </summary>
    public sealed class HqDebugMenu : MonoBehaviour
    {
        private enum Page { Main, Cars, Drivers }

        private static readonly Color Chosen = new Color(0.3f, 1f, 0.4f);
        /// <summary>Free agents the drivers page lists, fastest first.</summary>
        private const int Agents = 40;

        private Menus menus;
        private bool open;
        private Page page;
        private Vector2 scroll;
        private GUIStyle button, on;
        private GameState agentsOf;
        private (Driver Driver, DriverStats Stats)[] agents;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            Attach();
        }

        private static void Loaded(Scene scene, LoadSceneMode mode) => Attach();

        /// <summary>Puts the menu beside the HQ's menus, once per scene.</summary>
        private static void Attach()
        {
            Menus found = FindAnyObjectByType<Menus>();
            if (found != null && found.GetComponent<HqDebugMenu>() == null) found.gameObject.AddComponent<HqDebugMenu>();
        }

        private void Start()
        {
            menus = GetComponent<Menus>();
        }

        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Tab)
            {
                open = !open;
                page = Page.Main;
                ev.Use();
            }
            if (!open) return;
            GameState game = GameSession.State;
            bool career = game != null && game.Teams.OfType<PlayerTeam>().Any();

            float scale = Screen.height / 1080f;
            if (button == null)
            {
                button = new GUIStyle(GUI.skin.button);
                on = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                on.normal.textColor = on.hover.textColor = Chosen;
            }
            button.fontSize = on.fontSize = Mathf.RoundToInt(18 * scale);

            float w = 620f * scale, h = Screen.height * 0.8f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            Presets();
            if (career)
                switch (page)
                {
                    case Page.Main: Main(game, 14f * scale); break;
                    case Page.Cars: Cars(); break;
                    case Page.Drivers: Drivers(game); break;
                }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Ready-made careers: the year, the class of car, drivers hired, crew, money and every skill's level.</summary>
        private static readonly (int Year, CarClass Car, int Drivers, int Crew, double Money, int Skills)[] Careers =
        {
            (5, CarClass.D, 2, 6, 300_000, 5),
            (10, CarClass.B, 3, 12, 2_000_000, 12),
            (15, CarClass.A, 3, 16, 10_000_000, 20),
        };

        /// <summary>A new career in one click, replacing the current one as New game does.</summary>
        private void Presets()
        {
            if (page != Page.Main || menus == null) return;
            GUILayout.BeginHorizontal();
            if (Button("Year 1")) NewCareer(null);
            foreach (var c in Careers)
                if (Button($"Year {c.Year}"))
                {
                    var preset = c;
                    NewCareer(g => DebugActions.Equip(DebugActions.ToYear(g, preset.Year), preset.Car, preset.Drivers, preset.Crew, preset.Money, preset.Skills));
                }
            GUILayout.EndHorizontal();
            GUILayout.Space(14f * Screen.height / 1080f);
        }

        private void NewCareer(Func<GameState, GameState> build)
        {
            open = false;
            menus.StartCareer(new NewGameOptions { PlayerName = "Test Driver", TeamName = "Test Team", Nationality = "gb", TeamColour = "#2b4562" });
            if (build != null) Change(build);
            GameSession.Flush();
        }

        private static void Change(Func<GameState, GameState> change) => GameSession.Apply(change);

        private void Main(GameState game, float gap)
        {
            PlayerTeam p = game.Teams.OfType<PlayerTeam>().First();

            GUILayout.BeginHorizontal();
            if (Button("+100k")) Change(g => DebugActions.AddMoney(g, 100_000));
            if (Button("+1M")) Change(g => DebugActions.AddMoney(g, 1_000_000));
            if (Button("Broke")) Change(g => DebugActions.SetMoney(g, 0));
            GUILayout.EndHorizontal();

            GUILayout.Space(gap);
            GUILayout.BeginHorizontal();
            if (Button("Give car")) page = Page.Cars;
            if (Button("Mint")) Change(g => DebugActions.SetCondition(g, 100));
            if (Button("Wreck")) Change(g => DebugActions.SetCondition(g, 15));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Button("Hire")) page = Page.Drivers;
            if (Button("Release all")) Change(DebugActions.ReleaseAll);
            if (Button("Max skills")) Change(DebugActions.MaxSkills);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Button("+10 tyres")) Change(g => DebugActions.AddTyres(g, 10));
            if (Button("+10 spares")) Change(g => DebugActions.AddSpares(g, 10));
            foreach (int crew in new[] { 0, 8, 16 })
                if (Button($"Crew {crew}", p.CrewSize == crew)) Change(g => DebugActions.SetCrew(g, crew));
            GUILayout.EndHorizontal();

            GUILayout.Space(gap);
            if (game.Phase == GamePhase.PreRace)
            {
                GUILayout.BeginHorizontal();
                if (Button("Finish 1st")) Race(game, 1);
                if (Button("Finish mid")) Race(game, game.Teams.Count / 2);
                if (Button("Finish last")) Race(game, int.MaxValue);
                if (Button("Retire")) Race(game, 1, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (game.Phase == GamePhase.PreRace && Button("End year"))
            {
                Change(g => DebugActions.EndYear(g));
                GameSession.Flush();
            }
            if (game.Newspaper != null && menus != null && menus.InHub && Button("Paper"))
            {
                open = false;
                menus.ShowPaper();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Runs the race in an instant and settles it into the career: the race summary opens over the HQ.</summary>
        private void Race(GameState game, int place, bool retire = false)
        {
            RaceDay day = DebugActions.Race(game, place, retire);
            open = false;
            if (menus != null) menus.HideAll();
            RaceWeekend.Finish(day);
        }

        private void Cars()
        {
            if (Button("Back")) page = Page.Main;
            foreach (CarModel m in CarModels.All.OrderBy(m => m.CarClass).ThenBy(m => m.Price))
                if (Button($"{m.CarClass} {m.Name}"))
                {
                    string id = m.Id;
                    Change(g => DebugActions.GiveCar(g, id));
                }
        }

        private void Drivers(GameState game)
        {
            if (Button("Back")) page = Page.Main;
            if (!ReferenceEquals(agentsOf, game))
            {
                agentsOf = game;
                agents = DebugActions.FreeAgents(game).Take(Agents).ToArray();
            }
            foreach ((Driver d, DriverStats stats) in agents)
                if (Button($"{d.Name} {stats.Pace:0}"))
                {
                    string id = d.Id;
                    Change(g => DebugActions.Hire(g, id));
                }
        }

        private bool Button(string text, bool chosen = false) => GUILayout.Button(text, chosen ? on : button);
    }
}
