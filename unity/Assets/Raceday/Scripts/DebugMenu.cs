using System;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The raceday debug menu, on Tab, in the editor and in every build. It gets the race to any state
    /// in a click: the speed of the race, from paused to 64x; jumps of the race clock to dusk, night, dawn, the last
    /// hour and the flag, or the race finished and settled at once; weather held over the circuit, from dry to
    /// flooded; the watched car refuelled, on new tyres, fixed, given an issue, its driver fresh or spent, called
    /// in, retired or crashed out; and the race restarted with another start, kind of weather, start hour, field or car
    /// (<see cref="DebugSettings"/>), or thrown away for the Team HQ. Clean hides every on-screen graphic.
    /// </summary>
    public sealed class DebugMenu : MonoBehaviour
    {
        /// <summary>True while every on-screen graphic is hidden, for looking at the scene.</summary>
        public static bool Clean;

        /// <summary>True while the on-screen graphics are hidden: for a clean look, or under the pause menu.</summary>
        public static bool Bare => Clean || RaceWeekend.Paused;

        public KeyboardState Keys;
        public RaceCameras Cameras;
        public RaceTower Tower;
        public RaceDirector Director;
        /// <summary>The career the race was set up from, raced again on a restart.</summary>
        public GameState Game;

        /// <summary>The race speed the menu holds, or null to leave it to the race.</summary>
        public float? Speed { get; private set; }

        private enum Page { Main, Issues, Cars }

        private const string HubScene = "TeamHq";
        private static readonly Color Chosen = new Color(0.3f, 1f, 0.4f);
        private static readonly float[] Speeds = { 0f, 0.25f, 1f, 4f, 16f, 64f };
        private static readonly string[] SpeedNames = { "Pause", "0.25x", "1x", "4x", "16x", "64x" };
        private static readonly float[] StartHours = { 6f, 10f, 15f, 19f, 23f };
        /// <summary>Local times the clock jumps to: dusk, night and dawn, hours.</summary>
        private const float Dusk = 17.5f, Night = 23f, Dawn = 7f;
        private static readonly (string Name, WeatherState State)[] Skies =
        {
            ("Dry", new WeatherState { Cloud = 0.3f, Wind = 1f }),
            ("Drizzle", new WeatherState { Cloud = 0.95f, Rain = 0.5f, Water = 0.3f, Wind = 1f }),
            ("Rain", new WeatherState { Cloud = 1f, Rain = 8f, Water = 1.2f, Wind = 1.2f }),
            ("Flood", new WeatherState { Cloud = 1f, Rain = 25f, Water = WeatherPlan.Flooded, Wind = 1.4f }),
        };

        private bool open;
        private Page page;
        private int sky = -1;
        private Vector2 scroll;
        private GUIStyle button, on;

        private void Update()
        {
            if (Keys == null) return;
            if (Keys.Pressed(KeyCode.Tab))
            {
                open = !open;
                page = Page.Main;
            }
            if (Director != null && Director.Finished) Speed = null;   // the race summary runs at normal speed
            if (RaceWeekend.Paused) return;   // the pause menu holds the clock
            if (Speed is float s && Time.timeScale != s)
            {
                Time.timeScale = s;
                Time.maximumDeltaTime = Mathf.Max(1f / 3f, s / 30f);
            }
        }

        private void OnDestroy()
        {
            RaceWeather.Forced = null;
        }

        // ---------------------------------------------------------------- what the buttons do

        private CarController Watched => Cameras != null ? Cameras.Car : null;

        private void Jump(float raceSeconds)
        {
            if (Tower != null) Tower.SetRaceTime(raceSeconds);
        }

        private static float Hours(float local) => Mathf.Repeat(local - TimeOfDay.StartHour, 24f) * 3600f;

        private void Sky(int i)
        {
            sky = sky == i ? -1 : i;
            RaceWeather.Forced = sky >= 0 ? Skies[sky].State : (WeatherState?)null;
        }

        /// <summary>Races again from the career as it was set up, with the menu's start, weather, start hour, field and car.</summary>
        private void Restart()
        {
            if (Game != null) RaceWeekend.Begin(Game);
            Time.timeScale = 1f;
            Clean = false;
            SceneLoader.Load(SceneManager.GetActiveScene().name);
        }

        private void Home()
        {
            Time.timeScale = 1f;
            Clean = false;
            SceneLoader.Load(HubScene);
        }

        // ---------------------------------------------------------------- the menu

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (!open || RaceWeekend.Paused) return;
            float scale = Screen.height / 1080f;
            int size = Mathf.RoundToInt(18 * scale);
            if (button == null)
            {
                button = new GUIStyle(GUI.skin.button);
                on = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                on.normal.textColor = on.hover.textColor = Chosen;
            }
            button.fontSize = on.fontSize = size;

            float w = 620f * scale, h = Screen.height * 0.8f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            float gap = 14f * scale;
            switch (page)
            {
                case Page.Main: Main(gap); break;
                case Page.Issues: Issues(); break;
                case Page.Cars: Cars(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Main(float gap)
        {
            CarController car = Watched;
            Consumables supplies = car != null ? car.Supplies : null;
            CarRaceLog log = supplies != null ? supplies.Log : null;

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Speeds.Length; i++)
                if (Button(SpeedNames[i], Speed == Speeds[i])) Speed = Speed == Speeds[i] ? (float?)null : Speeds[i];
            GUILayout.EndHorizontal();

            if (Tower != null && Director != null && !Director.Finished)
            {
                GUILayout.Space(gap);
                GUILayout.BeginHorizontal();
                if (Button("Dusk")) Jump(Hours(Dusk));
                if (Button("Night")) Jump(Hours(Night));
                if (Button("Dawn")) Jump(Hours(Dawn));
                if (Button("Last hour")) Jump(RaceTower.RaceLength - 3600f);
                if (Button("Flag")) Jump(RaceTower.RaceLength);
                if (Button("Finish")) Director.FinishNow();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(gap);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Skies.Length; i++)
                if (Button(Skies[i].Name, sky == i)) Sky(i);
            GUILayout.EndHorizontal();

            {
                GUILayout.BeginHorizontal();
                foreach (WeatherKind k in (WeatherKind[])Enum.GetValues(typeof(WeatherKind)))
                    if (Button(k.ToString(), DebugSettings.Weather == k))
                    {
                        DebugSettings.Weather = DebugSettings.Weather == k ? (WeatherKind?)null : k;
                        Restart();
                    }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (float hour in StartHours)
                    if (Button($"{hour:00}:00", Mathf.Approximately(TimeOfDay.StartHour, hour)))
                    {
                        DebugSettings.StartHour = hour;
                        Restart();
                    }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach ((CircuitScene.StartMode mode, string name) in new[] { (CircuitScene.StartMode.Grid, "Grid"), (CircuitScene.StartMode.Rolling, "Rolling") })
                    if (Button(name, DebugSettings.Start == mode))
                    {
                        DebugSettings.Start = mode;
                        Restart();
                    }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach ((int size, string name) in new[] { (1, "Alone"), (10, "10 cars"), (0, "Full") })
                    if (Button(name, DebugSettings.Field == size))
                    {
                        DebugSettings.Field = size;
                        Restart();
                    }
                if (Button("Car", DebugSettings.Car != null)) page = Page.Cars;
                GUILayout.EndHorizontal();
            }

            if (supplies != null)
            {
                GUILayout.Space(gap);
                GUILayout.BeginHorizontal();
                if (Button("Refuel")) supplies.Fuel = supplies.Capacity;
                if (Button("Tyres")) supplies.Wear = 0f;
                if (log != null && Button("Pit", supplies.Orders.Requested)) supplies.Orders.Requested = true;
                GUILayout.EndHorizontal();
                if (log != null && !log.Retired && Director != null && !Director.Finished)
                {
                    GUILayout.BeginHorizontal();
                    if (Button("Fix", log.ActiveIssues.Count > 0))
                    {
                        log.ClearIssues();
                        supplies.Effects = log.Effects;
                    }
                    if (Button("Issue")) page = Page.Issues;
                    if (Button("Fresh")) log.SetFatigue(0);
                    if (Button("Tired")) log.SetFatigue(100);
                    if (Button("Retire")) Director.RetireNow(log.Index, FailureType.Mechanical);
                    if (Button("Crash out")) Director.RetireNow(log.Index, FailureType.Crash);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(gap);
            GUILayout.BeginHorizontal();
            if (Button("Clean", Clean)) Clean = !Clean;
            if (Button("Restart")) Restart();
            if (Button("HQ")) Home();
            GUILayout.EndHorizontal();
        }

        /// <summary>Your car for the next races: any model, or your own.</summary>
        private void Cars()
        {
            if (Button("Back")) page = Page.Main;
            if (Button("Own car", DebugSettings.Car == null)) Pick(null);
            foreach (CarModel m in CarModels.All)
                if (Button($"{m.CarClass} {m.Name}", DebugSettings.Car == m.Id)) Pick(m.Id);
        }

        private void Pick(string modelId)
        {
            DebugSettings.Car = modelId;
            Restart();
        }

        private void Issues()
        {
            if (Button("Back")) page = Page.Main;
            CarController car = Watched;
            CarRaceLog log = car != null && car.Supplies != null ? car.Supplies.Log : null;
            if (log == null || log.Retired) return;
            foreach (IssueTemplate t in IssueTemplates.AllIssueTemplates)
            {
                bool has = false;
                foreach (ActiveIssue a in log.ActiveIssues) has |= a.TemplateId == t.Id;
                if (Button(t.Description, has) && !has)
                {
                    log.AddIssue(t.Id);
                    car.Supplies.Effects = log.Effects;
                }
            }
        }

        private bool Button(string text, bool chosen = false) => GUILayout.Button(text, chosen ? on : button);
    }
}
