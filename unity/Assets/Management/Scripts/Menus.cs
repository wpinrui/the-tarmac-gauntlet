using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The management menus over the Team HQ building: the main menu, the hub with its top bar, car tag and Race
    /// button, screens as one panel docked to the right with the hub menu kept beside it, the Escape menu and
    /// settings. Every panel has a back arrow at its top left. Every screen change moves the camera to that
    /// screen's shot.
    /// Screen panels fill their docked panel through <see cref="ScreenOpened"/>, such as <see cref="DriversPanel"/>.
    /// </summary>
    public sealed class Menus : MonoBehaviour
    {
        /// <summary>Hub screens, Garage then Office, by camera shot slug.</summary>
        public static readonly (string Slug, string Name)[][] Groups =
        {
            new[] { ("car-workshop", "Car workshop"), ("new-car-dealer", "New car dealer"), ("second-hand-dealer", "Second-hand dealer"), ("crew", "Crew") },
            new[] { ("drivers", "Drivers"), ("finances", "Finances"), ("newspaper", "Newspaper"), ("race-history", "Race history"), ("standings", "Standings"), ("scouting-report", "Scouting report") },
        };

        /// <summary>Panels that are not hub screens.</summary>
        public const string NewGameSlug = "new-game", LineupSlug = "lineup";

        private const string MainShot = "main-menu", HubShot = "hub", PaperSlug = "newspaper";

        /// <summary>A panel opened: its slug and the element to fill. The body is emptied each time.</summary>
        public event Action<string, VisualElement> ScreenOpened;

        /// <summary>A hub screen closed: its slug. Another screen may open straight after.</summary>
        public event Action<string> ScreenClosed;

        /// <summary>
        /// The side pane left of the docked panel, emptied with the panel each time a screen opens. It shows while a
        /// screen is open and the pane has something in it.
        /// </summary>
        internal VisualElement SideBody { get; private set; }

        private VisualElement side;

        private enum Mode { Main, NewGame, Hub, Screen, Lineup, Paper }
        private enum Overlay { None, Pause, Settings, Dialog }

        private static readonly string[] Order = Groups.SelectMany(g => g.Select(s => s.Slug)).ToArray();
        private static bool booted;

        private HqCamera hqCamera;
        private Camera cam;
        private NewspaperView paper;
        private MenuGlass glass;
        private Font mainMedium, mainRegular;
        private readonly HashSet<VisualElement> shown = new HashSet<VisualElement>();

        private VisualElement root, mainColumn, continueItem, gameLayer, top, hubMenu, carTag, tagCondition, race, panel, panelBody;
        private VisualElement catcher, lineup, lineupBody;
        private Label tagClass;
        private VisualElement dimmer, pauseLayer, settingsLayer, settingsPanel, dialogLayer, dialog, dialogBody, dialogButtons, badge;
        private Label continueSub, teamName, year, money, tagName;
        private VisualElement tipLayer;
        private readonly Dictionary<string, VisualElement> hubItems = new Dictionary<string, VisualElement>();

        private Mode mode = Mode.Main;
        private string screen;
        private Overlay overlay = Overlay.None, settingsFrom = Overlay.None;
        private bool tagWasShown;

        // ------------------------------------------------------------------ start-up

        private void Awake()
        {
            if (!booted)
            {
                booted = true;
                Application.quitting += GameSession.Flush;
                GameSession.SaveFailed += e => Debug.LogException(e);
            }
            if (!GameSession.IsConfigured)
                GameSession.Configure(new CareerStore(Path.Combine(Application.persistentDataPath, "careers")));
        }

        private void Start()
        {
            hqCamera = FindAnyObjectByType<HqCamera>();
            cam = hqCamera.GetComponent<Camera>();
            if (cam.GetComponent<GlassBackdrop>() == null) cam.gameObject.AddComponent<GlassBackdrop>();

            mainMedium = Resources.Load<Font>("Fonts/HostGrotesk-Medium");
            mainRegular = Resources.Load<Font>("Fonts/HostGrotesk-Regular");

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            root = doc.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("Menus"));
            root.AddToClassList("root");
            root.style.unityFontDefinition = FontDefinition.FromFont(SemiBold);
            glass = new MenuGlass(root);
            Build();
            gameObject.AddComponent<DriversPanel>();
            gameObject.AddComponent<CrewPanel>();
            gameObject.AddComponent<LineupPanel>();
            gameObject.AddComponent<FinancesPanel>();
            gameObject.AddComponent<RaceHistoryPanel>();
            gameObject.AddComponent<StandingsPanel>();
            gameObject.AddComponent<ScoutingPanel>();
            gameObject.AddComponent<NewGamePanel>();
            gameObject.AddComponent<GarageBays>();
            gameObject.AddComponent<WorkshopPanel>();
            gameObject.AddComponent<DealerPanel>();

            GameSession.Changed += OnGameChanged;
            if (GameSession.State != null) OnGameChanged(GameSession.State);
            if (GameSession.State == null)
            {
                Go(Mode.Main);
                hqCamera.Cut(MainShot);
            }
            else
            {
                // Back from a race the camera starts on the hub, so it can glide to the paper.
                hqCamera.Cut(HubShot);
                EnterGame();
            }
        }

        private void OnDestroy()
        {
            GameSession.Changed -= OnGameChanged;
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            // Main menu: a tall glass column on the left edge, the wordmark above four items.
            var mainLayer = Layer();
            mainColumn = glass.Make("main-column", "fade");
            mainColumn.Add(BuildWordmark());
            continueItem = MainItem("Continue", Continue);
            continueSub = Text("", "sub");
            continueSub.style.unityFontDefinition = FontDefinition.FromFont(mainRegular);
            continueItem.Add(continueSub);
            mainColumn.Add(continueItem);
            mainColumn.Add(MainItem("New game", NewGame));
            mainColumn.Add(MainItem("Settings", OpenSettings));
            mainColumn.Add(MainItem("Quit game", Quit));
            mainLayer.Add(mainColumn);

            // The game: top bar, hub menu, car tag, Race and the docked panel.
            gameLayer = Layer();
            // Under everything in the game layer: a click on the scene closes the lineup.
            catcher = new VisualElement();
            catcher.AddToClassList("layer");
            catcher.RegisterCallback<ClickEvent>(_ => { if (mode == Mode.Lineup) Go(Mode.Hub); });
            gameLayer.Add(catcher);
            (top, badge, teamName, year, money) = TopBar(glass);
            top.AddToClassList("fade");
            gameLayer.Add(top);

            hubMenu = glass.Make("menu", "fade");
            for (int g = 0; g < Groups.Length; g++)
            {
                VisualElement items = MenuGroup(g);
                foreach ((string slug, string name) in Groups[g])
                {
                    VisualElement item = Text(name.ToUpperInvariant(), "item");
                    item.AddToClassList("hub-item");
                    item.Add(Icon(slug, "item-icon"));
                    item.RegisterCallback<ClickEvent>(_ => OpenScreen(slug));
                    hubItems[slug] = item;
                    items.Add(item);
                }
                hubMenu.Add(items);
            }
            gameLayer.Add(hubMenu);

            carTag = glass.Make("tag", "fade");
            VisualElement tagTop = Box("tag-top");
            tagClass = ClassLabels.Make(CarClass.F, "cls-sm");
            tagTop.Add(tagClass);
            tagName = Text("", "tag-name", true);
            tagTop.Add(tagName);
            carTag.Add(tagTop);
            tagCondition = Box("tag-condition");
            carTag.Add(tagCondition);
            gameLayer.Add(carTag);

            race = RedButton("Race", "big-button", () => Go(Mode.Lineup));
            race.AddToClassList("corner");
            race.AddToClassList("fade");
            gameLayer.Add(race);

            panel = glass.Make("panel", "fade");
            panel.Add(BackButton(CloseScreen));
            panelBody = new VisualElement();
            panelBody.AddToClassList("panel-body");
            panel.Add(panelBody);
            gameLayer.Add(panel);

            // The lineup: a small panel where the Race button was.
            lineup = glass.Make("lineup", "fade");
            lineup.Add(BackButton(CloseScreen));
            lineupBody = new VisualElement();
            lineup.Add(lineupBody);
            gameLayer.Add(lineup);

            // A second pane left of the docked panel, for screens that show a selected item beside a long list.
            side = glass.Make("side", "fade");
            SideBody = new VisualElement();
            SideBody.AddToClassList("side-body");
            side.Add(SideBody);
            gameLayer.Add(side);

            // Escape menu and settings, over a dimmed scene.
            dimmer = Layer("dimmer", "fade");
            pauseLayer = Layer("centre", "fade");
            VisualElement pause = glass.Make("menu");
            VisualElement pauseItems = MenuGroup(0);
            pauseItems.Add(MenuItem("Resume", () => SetOverlay(Overlay.None)));
            pauseItems.Add(MenuItem("Settings", OpenSettings));
            pauseItems.Add(MenuItem("Quit to main menu", QuitToMainMenu));
            pauseItems.Add(MenuItem("Quit game", Quit));
            pause.Add(pauseItems);
            pauseLayer.Add(pause);

            settingsLayer = Layer("centre", "fade");
            settingsPanel = glass.Make("settings");
            settingsLayer.Add(settingsPanel);

            // The newspaper, over everything but the Escape menu.
            paper = new NewspaperView(Layer(), hqCamera, glass);

            // A dialog over whatever is showing: a body and two buttons.
            // It takes every click outside the dialog, so the screen behind can't be used while it shows.
            dialogLayer = Layer("centre", "fade");
            dialogLayer.pickingMode = PickingMode.Position;
            dialog = glass.Make("dialog");
            dialogBody = new VisualElement();
            dialogBody.AddToClassList("dialog-body");
            dialog.Add(dialogBody);
            dialogButtons = new VisualElement();
            dialogButtons.AddToClassList("dialog-buttons");
            dialog.Add(dialogButtons);
            dialogLayer.Add(dialog);

            foreach (VisualElement e in new[] { mainColumn, top, hubMenu, carTag, race, panel, side, catcher, lineup, dimmer, pauseLayer, settingsLayer, dialogLayer })
                e.style.display = DisplayStyle.None;

            Popups = Layer();
            tipLayer = Layer();
            Tips = new Tooltips(tipLayer);
        }

        /// <summary>Tooltips over every panel.</summary>
        internal Tooltips Tips { get; private set; }

        /// <summary>A layer over every panel for drop-down lists, emptied each time the menus change screen.</summary>
        internal VisualElement Popups { get; private set; }

        // ------------------------------------------------------------------ dialogs

        /// <summary>Shows a dialog over the current screen. Cancel and Escape close it; confirm closes it and runs <paramref name="onConfirm"/>.</summary>
        internal void Dialog(Action<VisualElement> fill, string cancel, string confirm, Action onConfirm)
        {
            dialogBody.Clear();
            fill(dialogBody);
            dialogButtons.Clear();
            dialogButtons.Add(GhostButton(cancel, "dialog-button", () => SetOverlay(Overlay.None)));
            VisualElement ok = RedButton(confirm, "dialog-button", () =>
            {
                SetOverlay(Overlay.None);
                onConfirm();
            });
            ok.AddToClassList("last");
            dialogButtons.Add(ok);
            SetOverlay(Overlay.Dialog);
        }

        /// <summary>A new game overwrites the current career, so the career on disk is shown for a yes first.</summary>
        private void NewGame()
        {
            CareerInfo latest = GameSession.Store.Latest();
            if (latest == null)
            {
                Go(Mode.NewGame);
                return;
            }
            Dialog(body =>
            {
                var career = new VisualElement();
                career.AddToClassList("dialog-career");
                var mark = new VisualElement();
                mark.AddToClassList("dialog-badge");
                Badges.Paint(mark, latest.Logo, latest.TeamColour);
                career.Add(mark);
                var words = new VisualElement();
                words.Add(Text(latest.TeamName.ToUpperInvariant(), "dialog-title", true));
                words.Add(Text("YEAR " + latest.Year.ToString(CultureInfo.InvariantCulture), "dialog-sub"));
                career.Add(words);
                body.Add(career);
            }, "Cancel", "Overwrite", () => Go(Mode.NewGame));
        }

        // ------------------------------------------------------------------ wordmark

        // In wordmark pixels on its 358 x 209.32 canvas, one to one with the reference screen (wordmark/README.md).
        private const float CarStartLeft = 364, CarFinalLeft = 195, CarTop = 19.7f, CarWidth = 169.61f, CarHeight = 43;
        private const float RimSize = 14.14f, RimY = 50.07f, TyreRadius = 12.627f;
        private static readonly float[] RimX = { 233.05f, 333.38f };
        private const float RollDelay = 0.35f, RollSeconds = 1.7f;

        /// <summary>A frame counts for at most this long in the roll, s, so a slow first frame can't skip it.</summary>
        private const float RollStep = 1 / 30f;

        /// <summary>True once the car has rolled in this run: back on the main menu from a game it stays parked.</summary>
        private static bool rolledIn;
        private VisualElement car;
        private readonly List<VisualElement> rims = new List<VisualElement>();
        /// <summary>Seconds the main menu has shown, counted frame by frame; -1 until it shows.</summary>
        private float rollTime = -1;

        /// <summary>Every play in the editor is a launch, even with domain reload off, which keeps statics.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRoll() => rolledIn = false;

        private VisualElement BuildWordmark()
        {
            var wordmark = new VisualElement { name = "wordmark", pickingMode = PickingMode.Ignore };
            wordmark.AddToClassList("wordmark");
            wordmark.Add(Picture("wordmark-text", 0, 0, 358, 209.32f));
            car = new VisualElement { pickingMode = PickingMode.Ignore };
            car.AddToClassList("wordmark-car");
            car.style.width = CarWidth;
            car.style.height = CarHeight;
            car.Add(Picture("car-body", 0, 0, CarWidth, CarHeight));
            foreach (float x in RimX)
            {
                VisualElement rim = Picture("car-rim", x - CarFinalLeft - RimSize / 2, RimY - CarTop - RimSize / 2, RimSize, RimSize);
                rims.Add(rim);
                car.Add(rim);
            }
            wordmark.Add(car);
            PlaceCar(CarFinalLeft);
            return wordmark;
        }

        private static VisualElement Picture(string file, float left, float top, float width, float height)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("wordmark-layer");
            var texture = Resources.Load<Texture2D>("Wordmark/" + file);
            if (texture == null) Debug.LogError("Main menu wordmark: Resources/Wordmark/" + file + ".png did not load, so it won't show.");
            e.style.backgroundImage = texture;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
            return e;
        }

        /// <summary>Puts the car's left edge at x and turns the rims by the distance rolled from the start.</summary>
        private void PlaceCar(float x)
        {
            car.style.left = x;
            car.style.top = CarTop;
            // Rolling left turns the wheels anticlockwise; UI Toolkit's positive angles turn clockwise.
            float degrees = -(CarStartLeft - x) / TyreRadius * Mathf.Rad2Deg;
            foreach (VisualElement rim in rims) rim.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));
        }

        /// <summary>On game launch the car rolls in from the right and brakes to a stop at its spot.</summary>
        private void RollWordmark()
        {
            if (rolledIn) return;
            if (rollTime < 0)
            {
                if (!shown.Contains(mainColumn)) return;
                rollTime = 0;
            }
            else rollTime += Mathf.Min(Time.unscaledDeltaTime, RollStep);
            float t = Mathf.Clamp01((rollTime - RollDelay) / RollSeconds);
            PlaceCar(CarStartLeft - (CarStartLeft - CarFinalLeft) * (1 - (1 - t) * (1 - t)));
            if (t >= 1) rolledIn = true;
        }

        private VisualElement Layer(params string[] classes)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            foreach (string c in classes) layer.AddToClassList(c);
            root.Add(layer);
            return layer;
        }

        private VisualElement MainItem(string text, Action onClick)
        {
            var item = new VisualElement();
            item.AddToClassList("item");
            item.AddToClassList("main-item");
            Label label = Text(text, "label");
            label.style.unityFontDefinition = FontDefinition.FromFont(mainMedium);
            item.Add(label);
            item.RegisterCallback<ClickEvent>(_ => onClick());
            return item;
        }

        // ------------------------------------------------------------------ settings

        private void BuildSettings() => SettingsView.Fill(settingsPanel);

        // ------------------------------------------------------------------ state

        private void Go(Mode next, string slug = null)
        {
            Tips.Hide();
            Popups.Clear();
            string closing = screen;
            mode = next;
            screen = next == Mode.Screen ? slug : null;
            if (closing != null) ScreenClosed?.Invoke(closing);
            if (next == Mode.Screen || next == Mode.NewGame)
            {
                panelBody.Clear();
                SideBody.Clear();
                panel.name = next == Mode.Screen ? slug : NewGameSlug;
                ScreenOpened?.Invoke(panel.name, panelBody);
            }
            else if (next == Mode.Lineup)
            {
                lineupBody.Clear();
                ScreenOpened?.Invoke(LineupSlug, lineupBody);
            }
            hqCamera.Show(next == Mode.Main || next == Mode.NewGame ? MainShot : next == Mode.Screen ? slug : next == Mode.Paper ? PaperSlug : HubShot);
            Render();
        }

        /// <summary>Opens a hub screen, even one already open.</summary>
        internal void Open(string slug) => Go(Mode.Screen, slug);

        /// <summary>
        /// Closes the open panel: a hub screen or the lineup back to the hub, the new game back to the main menu.
        /// The back arrow and Escape both come here.
        /// </summary>
        internal void CloseScreen()
        {
            if (mode == Mode.Screen || mode == Mode.Lineup) Go(Mode.Hub);
            else if (mode == Mode.NewGame) Go(Mode.Main);
        }

        private void OpenScreen(string slug)
        {
            if (slug == PaperSlug) OpenPaper();
            else if (mode == Mode.Screen && screen == slug) Go(Mode.Hub);
            else Go(Mode.Screen, slug);
        }

        private void Cycle(int step)
        {
            int i = mode == Mode.Screen ? Array.IndexOf(Order, screen) : (step > 0 ? -1 : Order.Length);
            string next;
            do
            {
                i = (i + step + Order.Length) % Order.Length;
                next = Order[i];
            } while (next == PaperSlug);
            OpenScreen(next);
        }

        /// <summary>Into the career: the paper first if the race just run hasn't been read yet, else the hub.</summary>
        private void EnterGame()
        {
            GameState game = GameSession.State;
            if (game?.Phase == GamePhase.PostRace)
            {
                if (game.Newspaper != null)
                {
                    OpenPaper();
                    return;
                }
                EndYear();
            }
            Go(Mode.Hub);
        }

        /// <summary>The camera glides to the coffee table and the latest paper fills the screen.</summary>
        private void OpenPaper()
        {
            Newspaper latest = GameSession.State?.Newspaper;
            if (latest == null) return;
            Go(Mode.Paper);
            paper.Open(latest, ClosedPaper);
        }

        /// <summary>Putting down the paper after a race ends the year.</summary>
        private void ClosedPaper()
        {
            if (GameSession.State?.Phase == GamePhase.PostRace) EndYear();
            Go(Mode.Hub);
        }

        private static void EndYear()
        {
            GameSession.Apply(g => Season.AdvanceYear(g));
            GameSession.Flush();
        }

        private void OpenSettings()
        {
            settingsFrom = overlay;
            BuildSettings();
            SetOverlay(Overlay.Settings);
        }

        private void SetOverlay(Overlay next)
        {
            overlay = next;
            Render();
        }

        /// <summary>True on the hub or one of its screens, with nothing over it.</summary>
        internal bool InHub => overlay == Overlay.None && (mode == Mode.Hub || mode == Mode.Screen);

        /// <summary>Opens the latest paper again, from the hub. For the debug menu.</summary>
        internal void ShowPaper()
        {
            if (InHub) OpenPaper();
        }

        /// <summary>Takes every menu off the screen, for the race summary over the HQ. For the debug menu.</summary>
        internal void HideAll()
        {
            if (root != null) root.style.display = DisplayStyle.None;
        }

        /// <summary>Starts a new career, replacing the current one, and opens the hub.</summary>
        internal void StartCareer(NewGameOptions options)
        {
            GameSession.NewCareer(options);
            Go(Mode.Hub);
        }

        /// <summary>Loads the latest career: into the race it was saved part way through, or else the hub.</summary>
        private void Continue()
        {
            if (!GameSession.ContinueLatest()) return;
            if (GameSession.SavedRace() is RaceSave race)
            {
                RaceWeekend.Resume(race);
                SceneManager.LoadScene(LineupPanel.RacedayScene);
                return;
            }
            EnterGame();
        }

        private void QuitToMainMenu()
        {
            paper.Hide();
            GameSession.Close();
            overlay = Overlay.None;
            Go(Mode.Main);
        }

        internal static void Quit()
        {
            GameSession.Flush();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Render()
        {
            bool inGame = mode == Mode.Hub || mode == Mode.Screen || mode == Mode.Lineup;
            // A dialog sits over the screen it was opened from; pause and settings replace it.
            bool covered = overlay == Overlay.Pause || overlay == Overlay.Settings;
            bool hasSave = GameSession.Store.Latest() != null;

            Show(mainColumn, mode == Mode.Main && !covered);
            continueItem.style.display = hasSave ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasSave && mode == Mode.Main)
            {
                CareerInfo latest = GameSession.Store.Latest();
                continueSub.text = string.IsNullOrEmpty(latest.TeamName) ? "Year " + latest.Year : latest.TeamName + ", year " + latest.Year;
            }
            Show(top, inGame);
            Show(hubMenu, (mode == Mode.Hub || mode == Mode.Screen || mode == Mode.Lineup) && !covered);
            Show(race, mode == Mode.Hub && !covered);
            Show(panel, (mode == Mode.Screen || mode == Mode.NewGame) && !covered);
            Show(lineup, mode == Mode.Lineup && !covered);
            Show(catcher, mode == Mode.Lineup && !covered);
            Show(side, mode == Mode.Screen && SideBody.childCount > 0 && !covered);
            Show(dimmer, overlay != Overlay.None);
            Show(pauseLayer, overlay == Overlay.Pause);
            Show(settingsLayer, overlay == Overlay.Settings);
            Show(dialogLayer, overlay == Overlay.Dialog);
            foreach (KeyValuePair<string, VisualElement> item in hubItems)
                item.Value.EnableInClassList("on", mode == Mode.Screen && item.Key == screen);
            hubItems[PaperSlug].EnableInClassList("dim", GameSession.State?.Newspaper == null);
            RenderTag();
        }

        private bool TagWanted()
        {
            if (mode != Mode.Hub || overlay != Overlay.None || hqCamera.Gliding) return false;
            return RaceCar() != null;
        }

        private void RenderTag()
        {
            tagWasShown = TagWanted();
            Show(carTag, tagWasShown);
        }

        /// <summary>Fades an element in, or hides it at once.</summary>
        private void Show(VisualElement e, bool on)
        {
            if (on == shown.Contains(e)) return;
            if (on)
            {
                shown.Add(e);
                e.style.display = DisplayStyle.Flex;
                e.AddToClassList("off");
                e.schedule.Execute(() => e.RemoveFromClassList("off"));
            }
            else
            {
                shown.Remove(e);
                e.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------ game data

        private static CarInstance RaceCar() => MenuKit.RaceCar(Player);

        private void OnGameChanged(GameState game)
        {
            PlayerTeam p = Player;
            if (p == null) return;
            FillTopBar(badge, teamName, year, money);

            CarInstance car = RaceCar();
            if (car != null)
            {
                CarModel model = Model(car.ModelId);
                tagName.text = (model?.Name ?? car.ModelId).ToUpperInvariant();
                if (model != null) ClassLabels.Paint(tagClass, model.CarClass);
                tagClass.style.display = model != null ? DisplayStyle.Flex : DisplayStyle.None;
                double c = Math.Max(0, Math.Min(100, car.Condition));
                tagCondition.Clear();
                tagCondition.Add(Bar(c));
                tagCondition.Add(Banded(Text(Num((int)Math.Round(c)) + "%", "stat-value", true), c));
            }
            if (root != null) RenderTag();
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (root == null) return;
            RollWordmark();
            paper.Tick();
            if (TagWanted() != tagWasShown) RenderTag();
            if (tagWasShown) PlaceTag();
            glass.Refresh();
        }

        /// <summary>Floats the tag beside the race car in its bay.</summary>
        private void PlaceTag()
        {
            // Bays are in the viewer's frame; Unity's Z is the negative.
            var anchor = new Vector3(HqGeometry.BayX[HqGeometry.RaceBay] + 1.2f, 1.7f, -(HqGeometry.CarZ + 0.6f));
            Vector3 s = cam.WorldToScreenPoint(anchor);
            if (s.z <= 0 || root.panel == null) return;
            Vector2 p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(s.x, Screen.height - s.y));
            carTag.style.left = Mathf.Round(p.x + 24);
            carTag.style.top = Mathf.Round(p.y - 60);
        }

        // Keys come through IMGUI events, so this works with either of Unity's input systems.
        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.type != EventType.KeyDown || root == null) return;
            // Typing in a text field is not a shortcut; Escape just leaves the field.
            if (root.panel?.focusController?.focusedElement is VisualElement focused && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null))
            {
                if (ev.keyCode == KeyCode.Escape)
                {
                    focused.Blur();
                    ev.Use();
                }
                return;
            }
            if (MenuDropdown.Key(ev.keyCode))
            {
                ev.Use();
                return;
            }
            switch (ev.keyCode)
            {
                case KeyCode.Escape:
                    if (overlay == Overlay.Dialog) SetOverlay(Overlay.None);
                    else if (overlay == Overlay.Settings) SetOverlay(settingsFrom);
                    else if (overlay == Overlay.Pause) SetOverlay(Overlay.None);
                    else if (mode == Mode.Paper) paper.Close();
                    else if (mode == Mode.Screen || mode == Mode.Lineup || mode == Mode.NewGame) CloseScreen();
                    else if (mode == Mode.Hub) SetOverlay(Overlay.Pause);
                    break;
                case KeyCode.Q when overlay == Overlay.None && (mode == Mode.Hub || mode == Mode.Screen):
                    Cycle(-1);
                    break;
                case KeyCode.E when overlay == Overlay.None && (mode == Mode.Hub || mode == Mode.Screen):
                    Cycle(1);
                    break;
                default:
                    return;
            }
            ev.Use();
        }
    }
}
