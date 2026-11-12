using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The race summary, shown over the circuit when the race is settled (<see cref="RaceWeekend.Finished"/>), with
    /// the player's car parked in its box. The results overall, by class and as class podiums sit on the left. The
    /// player's race sits on the right: the finish, a lap-by-lap position chart with each driver's stints shaded, and
    /// the stints, stops and issues. The race's 3 skill points are spent here, and Continue stays locked until they
    /// are. Continue goes back to the Team HQ, where the race's newspaper opens first.
    /// </summary>
    public sealed class RaceSummaryScreen : MonoBehaviour
    {
        /// <summary>The scene Continue goes to. It opens the newspaper, then the hub.</summary>
        public const string HubScene = "TeamHq";

        /// <summary>Skill points each race adds.</summary>
        internal const int PointsPerRace = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            RaceWeekend.Finished -= OnFinished;
            RaceWeekend.Finished += OnFinished;
        }

        private static void OnFinished()
        {
            if (RaceWeekend.Summary == null || FindAnyObjectByType<RaceSummaryScreen>() != null) return;
            new GameObject("Race summary").AddComponent<RaceSummaryScreen>();
        }

        private RaceSummary summary;
        private MenuGlass glass;
        private Tooltips tips;
        private VisualElement root, skillsPanel, cont;
        private static readonly RaceView.Views views = new RaceView.Views();

        private readonly Dictionary<PlayerSkillType, int> pending = new Dictionary<PlayerSkillType, int>();
        private int toSpend;

        // ------------------------------------------------------------------ start-up

        private void Start()
        {
            summary = RaceWeekend.Summary;
            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<GlassBackdrop>() == null) cam.gameObject.AddComponent<GlassBackdrop>();

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 10;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            root = doc.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("Menus"));
            root.AddToClassList("root");
            root.style.unityFontDefinition = FontDefinition.FromFont(SemiBold);
            glass = new MenuGlass(root);

            foreach ((PlayerSkillType type, _, _) in Skills) pending[type] = 0;
            toSpend = PointsToSpend();
            Build();
        }

        private void LateUpdate()
        {
            glass?.Refresh();
        }

        /// <summary>Unspent points, up to what the skills still have room for.</summary>
        private static int PointsToSpend()
        {
            GameState game = GameSession.State;
            PlayerTeam p = Player;
            if (game == null || p == null) return 0;
            int earned = GameInit.StartingSkillPoints - p.ForfeitedSkillPoints + PointsPerRace * game.RaceHistory.Count;
            int spent = p.Skills.Driver + p.Skills.Engineer + p.Skills.Business;
            int room = 3 * MaxSkill - spent;
            return Math.Max(0, Math.Min(earned - spent, room));
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            (VisualElement top, VisualElement badge, Label name, Label year, Label money) = TopBar(glass);
            FillTopBar(badge, name, year, money);
            root.Add(top);

            var race = new RaceView(summary, true, views);
            VisualElement results = glass.Make("summary-results");
            race.Results(results);
            root.Add(results);

            if (summary.Player != null)
            {
                VisualElement side = Box("summary-side");
                VisualElement hero = glass.Make("summary-box");
                race.Hero(hero);
                side.Add(hero);
                if (race.HasLaps)
                {
                    VisualElement chart = glass.Make("summary-box");
                    race.Chart(chart);
                    side.Add(chart);
                    VisualElement log = glass.Make("summary-log");
                    race.Log(log);
                    side.Add(log);
                }
                root.Add(side);
            }

            if (toSpend > 0)
            {
                skillsPanel = glass.Make("summary-skills");
                root.Add(skillsPanel);
            }
            cont = RedButton("Continue", "big-button", Continue);
            cont.AddToClassList("corner");
            root.Add(cont);
            tips = new Tooltips(root);
            RenderSkills();
        }

        // ------------------------------------------------------------------ skill points and Continue

        private int Left => toSpend - pending.Values.Sum();

        private void RenderSkills()
        {
            cont.EnableInClassList("disabled", Left > 0);
            if (skillsPanel == null) return;
            PlayerSkills skills = Player?.Skills;
            skillsPanel.Clear();
            skillsPanel.Add(SkillPicker(tips, toSpend, Left, t => SkillLevel(skills, t) + pending[t], t => SkillLevel(skills, t), Spend));
        }

        private void Spend(PlayerSkillType type, int points)
        {
            tips.Hide();
            pending[type] = Math.Max(0, pending[type] + points);
            RenderSkills();
        }

        private void Continue()
        {
            if (Left > 0) return;
            if (GameSession.State != null && pending.Values.Sum() > 0)
            {
                GameSession.Apply(g =>
                {
                    foreach (KeyValuePair<PlayerSkillType, int> p in pending)
                        for (int i = 0; i < p.Value; i++) g = GameActions.AllocateSkillPoint(g, p.Key);
                    return g;
                });
            }
            GameSession.Flush();
            Time.timeScale = 1f;
            SceneLoader.Load(HubScene);
        }
    }
}
