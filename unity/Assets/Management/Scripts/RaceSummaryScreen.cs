using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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

        /// <summary>Skill points a new career starts with, and what each race adds.</summary>
        internal const int PointsPerRace = 3, MaxSkill = 20;

        /// <summary>Driver colours in the order they first drove. The race history uses them too.</summary>
        internal static readonly Color[] DriverColours =
        {
            Color.white, new Color32(0x5a, 0xb8, 0xff, 0xff), new Color32(0xff, 0xb0, 0x20, 0xff),
            new Color32(0xc5, 0x8c, 0xff, 0xff), new Color32(0x3d, 0xdc, 0x84, 0xff),
        };

        internal static readonly (PlayerSkillType Type, string Name, string Tip)[] Skills =
        {
            (PlayerSkillType.Driver, "Driver", "Your own driving. All five of your driver stats are this level times 5."),
            (PlayerSkillType.Engineer, "Engineer", "Shorter pit stops, and the car loses condition more slowly in a race."),
            (PlayerSkillType.Business, "Business", "Discounts on purchases, salaries and repairs, and better sale prices."),
        };

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
        private Font semiBold, bold;
        private MenuGlass glass;
        private VisualElement root, resultsTabs, logTabs, skillsPanel, cont;
        private ScrollView resultsList, logList;
        private Label tip, readout;
        private IVisualElementScheduledItem tipTimer;
        private string resultsView = "all", logView = "stints";

        private readonly Dictionary<string, string> driverNames = new Dictionary<string, string>();
        private readonly Dictionary<string, Color> driverColours = new Dictionary<string, Color>();
        private readonly Dictionary<PlayerSkillType, int> pending = new Dictionary<PlayerSkillType, int>();
        private int toSpend;

        // ------------------------------------------------------------------ start-up

        private void Start()
        {
            summary = RaceWeekend.Summary;
            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<GlassBackdrop>() == null) cam.gameObject.AddComponent<GlassBackdrop>();

            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
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
            root.style.unityFontDefinition = FontDefinition.FromFont(semiBold);
            glass = new MenuGlass(root);

            NameDrivers();
            foreach ((PlayerSkillType type, _, _) in Skills) pending[type] = 0;
            toSpend = PointsToSpend();
            Build();
        }

        private void LateUpdate()
        {
            glass?.Refresh();
        }

        private static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

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

        /// <summary>The player's drivers by id, coloured in the order they first drove.</summary>
        private void NameDrivers()
        {
            foreach (StintRecord s in summary.Stints)
            {
                driverNames[s.DriverId] = s.DriverName;
                if (!driverColours.ContainsKey(s.DriverId)) driverColours[s.DriverId] = DriverColours[driverColours.Count % DriverColours.Length];
            }
        }

        private string DriverName(string id)
        {
            if (driverNames.TryGetValue(id, out string n)) return n;
            return GameSession.State?.Drivers.FirstOrDefault(d => d.Id == id)?.Name ?? id;
        }

        private Color DriverColour(string id) => driverColours.TryGetValue(id, out Color c) ? c : Color.white;

        // ------------------------------------------------------------------ building

        private void Build()
        {
            BuildTopBar();

            VisualElement results = glass.Make("rs-results");
            resultsTabs = Row("rs-tabs");
            results.Add(resultsTabs);
            resultsList = Scroll();
            results.Add(resultsList);
            root.Add(results);
            RenderResults();

            if (summary.Player != null)
            {
                var side = new VisualElement();
                side.AddToClassList("rs-side");
                side.Add(Hero());
                side.Add(Chart());
                VisualElement log = glass.Make("rs-log");
                logTabs = Row("rs-tabs");
                log.Add(logTabs);
                logList = Scroll();
                log.Add(logList);
                side.Add(log);
                root.Add(side);
                RenderLog();
            }

            if (toSpend > 0)
            {
                skillsPanel = glass.Make("rs-skills");
                root.Add(skillsPanel);
            }
            cont = RedButton("Continue", "rs-continue", Continue);
            root.Add(cont);
            RenderSkills();

            tip = Text("", "tip");
            tip.pickingMode = PickingMode.Ignore;
            tip.style.display = DisplayStyle.None;
            root.Add(tip);
        }

        private void BuildTopBar()
        {
            PlayerTeam p = Player;
            if (p == null) return;
            var top = new VisualElement();
            top.AddToClassList("top");
            var team = new VisualElement();
            team.AddToClassList("team");
            var badge = new VisualElement();
            badge.AddToClassList("badge");
            badge.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/badge");
            team.Add(badge);
            team.Add(Text(p.Name.ToUpperInvariant(), "team-name", true));
            top.Add(team);
            VisualElement meta = glass.Make("meta");
            meta.Add(Text("YEAR " + GameSession.State.CurrentYear.ToString(CultureInfo.InvariantCulture), "year"));
            meta.Add(Text(Menus.Money(p.Budget), "money", true));
            top.Add(meta);
            root.Add(top);
        }

        // ------------------------------------------------------------------ results

        private IEnumerable<CarClass> ClassesRaced => ClassLabels.Order.Where(c => summary.Classification.Any(r => r.CarClass == c));

        private void RenderResults()
        {
            resultsTabs.Clear();
            resultsTabs.Add(Tab("ALL", resultsView == "all", () => ShowResults("all")));
            foreach (CarClass c in ClassesRaced)
            {
                string key = c.ToString();
                Label t = Tab(ClassLabels.Name(c), resultsView == key, () => ShowResults(key));
                t.AddToClassList("rs-tab-class");
                t.style.color = ClassLabels.Colour(c);
                resultsTabs.Add(t);
            }
            Label podiums = Tab("", resultsView == "podiums", () => ShowResults("podiums"));
            podiums.AddToClassList("rs-tab-end");
            podiums.Add(Icon("trophy", "rs-tab-icon"));
            resultsTabs.Add(podiums);

            resultsList.Clear();
            if (resultsView == "podiums")
            {
                foreach (CarClass c in ClassesRaced) resultsList.Add(Podium(c));
                return;
            }
            IEnumerable<ClassifiedCar> rows = resultsView == "all" ? summary.Classification : summary.Classification.Where(r => r.CarClass.ToString() == resultsView);
            foreach (ClassifiedCar r in rows) resultsList.Add(ResultRow(r, resultsView == "all" ? r.Position : r.ClassPosition));
        }

        private void ShowResults(string view)
        {
            resultsView = view;
            RenderResults();
            resultsList.scrollOffset = Vector2.zero;
        }

        private VisualElement ResultRow(ClassifiedCar r, int position)
        {
            VisualElement row = Row("rs-row", "rs-result");
            row.EnableInClassList("me", r.CarId == summary.Player?.CarId);
            row.EnableInClassList("out", r.Retired);
            row.Add(Text(Num(position), "rs-pos", true));
            row.Add(ClassLabels.Make(r.CarClass, "cls-sm", bold));
            var team = new VisualElement();
            team.AddToClassList("rs-team");
            team.Add(Text(r.TeamName, "rs-team-name", true));
            team.Add(Text(CarModels.All.FirstOrDefault(m => m.Id == r.ModelId)?.Name ?? r.ModelId, "rs-car"));
            row.Add(team);
            row.Add(Text(Num(r.Laps), "rs-laps"));
            row.Add(r.Retired ? Text("DNF", "rs-dnf", true) : Text(Menus.Money(r.PrizeMoney), "rs-prize", true));
            return row;
        }

        private VisualElement Podium(CarClass c)
        {
            VisualElement block = Row("rs-podium");
            Label letter = Text("", "rs-podium-class", true);
            ClassLabels.Tint(letter, c);
            block.Add(letter);
            var rows = new VisualElement();
            rows.AddToClassList("rs-podium-rows");
            int place = 0;
            foreach (ClassifiedCar r in summary.Classification.Where(x => x.CarClass == c && !x.Retired).Take(3))
            {
                place++;
                VisualElement row = Row("rs-row", "rs-podium-row");
                row.EnableInClassList("me", r.CarId == summary.Player?.CarId);
                row.Add(Text(Num(place), "rs-place p" + place, true));
                row.Add(Text(r.TeamName, "rs-team-name", true));
                row.Add(Text("P" + Num(r.Position), "rs-overall"));
                rows.Add(row);
            }
            block.Add(rows);
            return block;
        }

        // ------------------------------------------------------------------ the player's race

        private VisualElement Hero()
        {
            ClassifiedCar me = summary.Player;
            VisualElement hero = glass.Make("rs-hero");
            hero.Add(Text("P" + Num(me.Position), "rs-big", true));
            VisualElement inClass = Row("rs-inclass");
            inClass.Add(ClassLabels.Make(me.CarClass, "cls-md", bold));
            inClass.Add(Text("P" + Num(me.ClassPosition), "rs-inclass-pos", true));
            hero.Add(inClass);
            hero.Add(Stat(Num(me.Laps), "LAPS", null));
            if (me.BestLap is double best) hero.Add(Stat(LapTime(best), "BEST LAP", "rs-best"));
            hero.Add(me.Retired ? Text("DNF", "rs-hero-dnf", true) : Text(Menus.Money(me.PrizeMoney), "rs-hero-prize", true));
            return hero;
        }

        private VisualElement Stat(string value, string label, string cls)
        {
            var stat = new VisualElement();
            stat.AddToClassList("rs-stat");
            Label v = Text(value, "rs-stat-value", true);
            if (cls != null) v.AddToClassList(cls);
            stat.Add(v);
            stat.Add(Text(label, "rs-stat-label"));
            return stat;
        }

        private VisualElement Chart()
        {
            VisualElement panel = glass.Make("rs-chart");
            var chart = new PositionChart(summary, LapScale(), DriverColour, semiBold);
            panel.Add(chart);
            readout = Text("", "rs-readout", true);
            readout.style.display = DisplayStyle.None;
            panel.Add(readout);
            chart.Hovered += (lap, position) =>
            {
                readout.style.display = lap > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                readout.text = "L" + Num(lap) + "   P" + Num(position);
            };
            return panel;
        }

        /// <summary>
        /// Leader laps per lap of the player's: positions are kept on the leader's laps, while stints, stops and issues
        /// count the player's own. A car that retired is taken to have run at its class winner's pace.
        /// </summary>
        private float LapScale()
        {
            int leaderLaps = summary.PositionByLap.Count;
            ClassifiedCar me = summary.Player;
            int pace = me.Retired ? summary.Classification.FirstOrDefault(c => c.CarClass == me.CarClass && !c.Retired)?.Laps ?? me.Laps : me.Laps;
            return pace > 0 && leaderLaps > 0 ? leaderLaps / (float)pace : 1f;
        }

        private void RenderLog()
        {
            logTabs.Clear();
            logTabs.Add(CountTab("STINTS", summary.Stints.Count, "stints"));
            logTabs.Add(CountTab("STOPS", summary.PitStops.Count, "stops"));
            logTabs.Add(CountTab("ISSUES", summary.Issues.Count, "issues"));
            logList.Clear();
            switch (logView)
            {
                case "stints":
                    double? quickest = summary.Player.BestLap;
                    foreach (StintRecord s in summary.Stints)
                    {
                        VisualElement row = Row("rs-row", "rs-stint");
                        row.Add(Swatch(DriverColour(s.DriverId)));
                        row.Add(Text(s.DriverName, "rs-driver", true));
                        row.Add(Text(Num(s.StartLap) + " to " + Num(s.EndLap), "rs-range"));
                        row.Add(Text(Num(s.EndLap - s.StartLap + 1), "rs-count"));
                        Label best = Text(s.BestLap is double b ? LapTime(b) : "", "rs-lap");
                        best.EnableInClassList("rs-best", s.BestLap != null && s.BestLap == quickest);
                        row.Add(best);
                        logList.Add(row);
                    }
                    break;
                case "stops":
                    foreach (PitStopRecord s in summary.PitStops)
                    {
                        VisualElement row = Row("rs-row", "rs-stop");
                        row.Add(Text("L" + Num(s.Lap), "rs-at"));
                        row.Add(Text(s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s", "rs-secs"));
                        VisualElement tyre = Icon("tyre", "rs-tyre");
                        tyre.style.visibility = s.TyresChanged ? Visibility.Visible : Visibility.Hidden;
                        row.Add(tyre);
                        row.Add(Text(Num((int)Math.Round(s.FuelAdded)) + " L", "rs-fuel"));
                        VisualElement who = Row("rs-who");
                        if (s.DriverIn != s.DriverOut)
                        {
                            who.Add(Swatch(DriverColour(s.DriverIn)));
                            who.Add(Text(DriverName(s.DriverIn), "rs-driver", true));
                        }
                        if (s.FixedIssueIds.Count > 0) who.Add(Text(string.Join(", ", s.FixedIssueIds.Select(IssueName)), "rs-fixed"));
                        row.Add(who);
                        logList.Add(row);
                    }
                    break;
                case "issues":
                    foreach (IssueRecord i in summary.Issues)
                    {
                        VisualElement row = Row("rs-row", "rs-issue");
                        row.Add(Text("L" + Num(i.Lap), "rs-at"));
                        row.Add(Text(IssueName(i.TemplateId), "rs-issue-name", true));
                        VisualElement who = Row("rs-who");
                        who.Add(Swatch(DriverColour(i.DriverId)));
                        who.Add(Text(DriverName(i.DriverId), "rs-range"));
                        row.Add(who);
                        row.Add(i.FixedLap is int f ? Text("L" + Num(f), "rs-ok", true) : Text("DNF", "rs-dnf", true));
                        logList.Add(row);
                    }
                    break;
            }
        }

        private Label CountTab(string name, int count, string view)
        {
            Label tab = Tab(name, logView == view, () =>
            {
                logView = view;
                RenderLog();
                logList.scrollOffset = Vector2.zero;
            });
            tab.Add(Text(Num(count), "rs-tab-count", true));
            return tab;
        }

        private static string IssueName(string templateId) =>
            IssueTemplates.AllIssueTemplates.FirstOrDefault(t => t.Id == templateId)?.Description ?? templateId;

        // ------------------------------------------------------------------ skill points and Continue

        private int Left => toSpend - pending.Values.Sum();

        private void RenderSkills()
        {
            cont.EnableInClassList("disabled", Left > 0);
            if (skillsPanel == null) return;
            PlayerTeam p = Player;
            skillsPanel.Clear();
            skillsPanel.Add(Text("SKILL POINTS", "rs-skills-label"));
            var line = Row("rs-skills-line");
            var pips = new VisualElement();
            pips.AddToClassList("rs-pips");
            for (int i = 0; i < toSpend; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("rs-pip");
                pip.EnableInClassList("used", i >= Left);
                pips.Add(pip);
            }
            line.Add(pips);
            foreach ((PlayerSkillType type, string name, string tipText) in Skills)
            {
                int level = Level(p, type) + pending[type];
                var skill = new VisualElement();
                skill.AddToClassList("rs-skill");
                Label label = Text(name.ToUpperInvariant(), "rs-skill-name");
                Tooltip(label, tipText);
                skill.Add(label);
                VisualElement levelRow = Row("rs-level");
                levelRow.Add(Step("minus", pending[type] > 0, () => Spend(type, -1)));
                Label value = Text(Num(level), "rs-skill-value", true);
                value.EnableInClassList("up", pending[type] > 0);
                levelRow.Add(value);
                levelRow.Add(Step("plus", Left > 0 && level < MaxSkill, () => Spend(type, 1)));
                skill.Add(levelRow);
                line.Add(skill);
            }
            skillsPanel.Add(line);
        }

        private static int Level(PlayerTeam p, PlayerSkillType type) => p == null ? 0 : type switch
        {
            PlayerSkillType.Driver => p.Skills.Driver,
            PlayerSkillType.Engineer => p.Skills.Engineer,
            _ => p.Skills.Business,
        };

        private VisualElement Step(string icon, bool on, Action onClick)
        {
            var button = new VisualElement();
            button.AddToClassList("rs-step");
            button.Add(Icon(icon, "rs-step-icon"));
            button.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        private void Spend(PlayerSkillType type, int points)
        {
            HideTooltip();
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
            SceneManager.LoadScene(HubScene);
        }

        // ------------------------------------------------------------------ tooltips

        /// <summary>Shows a skill's explanation under it after a short hover.</summary>
        private void Tooltip(VisualElement target, string text)
        {
            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                tipTimer?.Pause();
                tipTimer = target.schedule.Execute(() =>
                {
                    if (target.panel == null) return;
                    tip.text = text;
                    Rect r = target.worldBound;
                    Vector2 at = root.WorldToLocal(new Vector2(r.xMin, r.yMin));
                    tip.style.left = Mathf.Round(at.x);
                    tip.style.top = StyleKeyword.Auto;
                    tip.style.bottom = Mathf.Round(root.layout.height - at.y + 8);
                    tip.style.display = DisplayStyle.Flex;
                }).StartingIn(350);
            });
            target.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        }

        private void HideTooltip()
        {
            tipTimer?.Pause();
            tipTimer = null;
            if (tip != null) tip.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ helpers

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        /// <summary>A lap time as m:ss.fff.</summary>
        internal static string LapTime(double seconds)
        {
            int minutes = (int)(seconds / 60);
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + (seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture);
        }

        private Label Text(string text, string cls, bool isBold = false)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            if (isBold) label.style.unityFontDefinition = FontDefinition.FromFont(bold);
            return label;
        }

        private static VisualElement Row(params string[] classes)
        {
            var row = new VisualElement();
            foreach (string c in classes) row.AddToClassList(c);
            return row;
        }

        private static ScrollView Scroll()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("rs-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            return scroll;
        }

        private Label Tab(string text, bool on, Action onClick)
        {
            Label tab = Text(text, "rs-tab");
            tab.EnableInClassList("on", on);
            tab.RegisterCallback<ClickEvent>(_ => onClick());
            return tab;
        }

        private static VisualElement Icon(string file, string cls)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(cls);
            icon.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + file);
            return icon;
        }

        private static VisualElement Swatch(Color colour)
        {
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("rs-swatch");
            swatch.style.backgroundColor = colour;
            return swatch;
        }

        private VisualElement RedButton(string text, string cls, Action onClick)
        {
            var button = new VisualElement();
            button.AddToClassList("red");
            button.AddToClassList(cls);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("red-fill");
            fill.style.backgroundImage = Menus.RedGradient;
            button.Add(fill);
            Label label = Text(text.ToUpperInvariant(), "red-label", true);
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }
    }
}
