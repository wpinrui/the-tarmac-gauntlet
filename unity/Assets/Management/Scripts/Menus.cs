using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The management menus over the Team HQ building: the main menu, the hub with its top bar, car tag and Race
    /// button, screens as one panel docked to the right with the menu collapsed to an icon strip, the Escape menu
    /// and settings. Every screen change moves the camera to that screen's shot.
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

        private const string MainShot = "main-menu", HubShot = "hub";

        /// <summary>A panel opened: its slug and the element to fill. The body is emptied each time.</summary>
        public event Action<string, VisualElement> ScreenOpened;

        private enum Mode { Main, NewGame, Hub, Screen, Lineup }
        private enum Overlay { None, Pause, Settings }

        private static readonly string[] Order = Groups.SelectMany(g => g.Select(s => s.Slug)).ToArray();
        private static bool booted;

        private HqCamera hqCamera;
        private Camera cam;
        private MenuGlass glass;
        private Font semiBold, bold, mainMedium, mainRegular;
        private readonly HashSet<VisualElement> shown = new HashSet<VisualElement>();

        private VisualElement root, mainColumn, continueItem, gameLayer, top, hubMenu, strip, tag, tagFill, race, panel, panelBody, panelButton;
        private VisualElement dimmer, pauseLayer, settingsLayer, settingsPanel;
        private Label continueSub, teamName, year, money, tagName, tagPercent;
        private readonly Dictionary<string, VisualElement> hubItems = new Dictionary<string, VisualElement>(), stripIcons = new Dictionary<string, VisualElement>();

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
                GameSettings.Load();
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

            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
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
            root.style.unityFontDefinition = FontDefinition.FromFont(semiBold);
            glass = new MenuGlass(root);
            Build();
            gameObject.AddComponent<DriversPanel>();
            gameObject.AddComponent<CrewPanel>();

            GameSession.Changed += OnGameChanged;
            if (GameSession.State != null) OnGameChanged(GameSession.State);
            Go(GameSession.State != null ? Mode.Hub : Mode.Main);
            hqCamera.Cut(hqCamera.Current == null ? MainShot : hqCamera.Current);
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
            continueItem = MainItem("Continue", () => { if (GameSession.ContinueLatest()) Go(Mode.Hub); });
            continueSub = new Label();
            continueSub.AddToClassList("sub");
            continueSub.style.unityFontDefinition = FontDefinition.FromFont(mainRegular);
            continueItem.Add(continueSub);
            mainColumn.Add(continueItem);
            mainColumn.Add(MainItem("New game", () => Go(Mode.NewGame)));
            mainColumn.Add(MainItem("Settings", OpenSettings));
            mainColumn.Add(MainItem("Quit game", Quit));
            mainLayer.Add(mainColumn);

            // The game: top bar, hub menu, icon strip, car tag, Race and the docked panel.
            gameLayer = Layer();
            top = new VisualElement();
            top.AddToClassList("top");
            top.AddToClassList("fade");
            var team = new VisualElement();
            team.AddToClassList("team");
            var badge = new VisualElement();
            badge.AddToClassList("badge");
            badge.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/badge");
            team.Add(badge);
            teamName = Text("", "team-name", true);
            team.Add(teamName);
            top.Add(team);
            VisualElement meta = glass.Make("meta");
            year = Text("", "year");
            money = Text("", "money", true);
            meta.Add(year);
            meta.Add(money);
            top.Add(meta);
            gameLayer.Add(top);

            hubMenu = glass.Make("menu", "fade");
            strip = glass.Make("strip", "fade");
            for (int g = 0; g < Groups.Length; g++)
            {
                VisualElement items = Group(g), icons = Group(g);
                foreach ((string slug, string name) in Groups[g])
                {
                    VisualElement item = Text(name.ToUpperInvariant(), "item");
                    item.RegisterCallback<ClickEvent>(_ => OpenScreen(slug));
                    hubItems[slug] = item;
                    items.Add(item);

                    var icon = new VisualElement();
                    icon.AddToClassList("icon");
                    var image = new VisualElement { pickingMode = PickingMode.Ignore };
                    image.AddToClassList("icon-image");
                    image.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + slug);
                    icon.Add(image);
                    icon.RegisterCallback<ClickEvent>(_ => OpenScreen(slug));
                    stripIcons[slug] = icon;
                    icons.Add(icon);
                }
                hubMenu.Add(items);
                strip.Add(icons);
            }
            gameLayer.Add(hubMenu);
            gameLayer.Add(strip);

            tag = glass.Make("tag", "fade");
            tagName = Text("", "tag-name", true);
            tag.Add(tagName);
            var condition = new VisualElement();
            condition.AddToClassList("condition");
            var bar = new VisualElement();
            bar.AddToClassList("bar");
            tagFill = new VisualElement();
            tagFill.AddToClassList("bar-fill");
            bar.Add(tagFill);
            condition.Add(bar);
            tagPercent = Text("", "percent", true);
            condition.Add(tagPercent);
            tag.Add(condition);
            gameLayer.Add(tag);

            race = RedButton("Race", "race", () => Go(Mode.Lineup));
            race.AddToClassList("fade");
            gameLayer.Add(race);

            panel = glass.Make("panel", "fade");
            panelBody = new VisualElement();
            panelBody.AddToClassList("panel-body");
            panel.Add(panelBody);
            panelButton = RedButton("Start", "panel-button", StartNewGame);
            panel.Add(panelButton);
            gameLayer.Add(panel);

            // Escape menu and settings, over a dimmed scene.
            dimmer = Layer("dimmer", "fade");
            pauseLayer = Layer("centre", "fade");
            VisualElement pause = glass.Make("menu");
            VisualElement pauseItems = Group(0);
            pauseItems.Add(MenuItem("Resume", () => SetOverlay(Overlay.None)));
            pauseItems.Add(MenuItem("Settings", OpenSettings));
            pauseItems.Add(MenuItem("Quit to main menu", QuitToMainMenu));
            pauseItems.Add(MenuItem("Quit game", Quit));
            pause.Add(pauseItems);
            pauseLayer.Add(pause);

            settingsLayer = Layer("centre", "fade");
            settingsPanel = glass.Make("settings");
            settingsLayer.Add(settingsPanel);

            foreach (VisualElement e in new[] { mainColumn, top, hubMenu, strip, tag, race, panel, dimmer, pauseLayer, settingsLayer })
                e.style.display = DisplayStyle.None;

            tipLayer = Layer();
            tip = Text("", "tip");
            tip.pickingMode = PickingMode.Ignore;
            tip.style.unityFontDefinition = FontDefinition.FromFont(mainRegular);
            tip.style.display = DisplayStyle.None;
            tipLayer.Add(tip);
        }

        // ------------------------------------------------------------------ tooltips

        private VisualElement tipLayer;
        private Label tip;
        private IVisualElementScheduledItem tipTimer;

        /// <summary>
        /// Shows <paramref name="text"/> under <paramref name="target"/> after a short hover. Only stats and skills
        /// get tooltips, where a number needs explaining.
        /// </summary>
        internal void Tooltip(VisualElement target, string text)
        {
            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                tipTimer?.Pause();
                tipTimer = target.schedule.Execute(() =>
                {
                    if (target.panel == null) return;
                    tip.text = text;
                    Rect r = target.worldBound;
                    Vector2 p = tipLayer.WorldToLocal(new Vector2(r.xMin, r.yMax));
                    tip.style.left = Mathf.Round(p.x);
                    tip.style.top = Mathf.Round(p.y + 8);
                    tip.style.display = DisplayStyle.Flex;
                }).StartingIn(350);
            });
            target.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
            target.RegisterCallback<DetachFromPanelEvent>(_ => HideTooltip());
        }

        private void HideTooltip()
        {
            tipTimer?.Pause();
            tipTimer = null;
            if (tip != null) tip.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ wordmark

        // In wordmark pixels on its 358 x 209.32 canvas, one to one with the reference screen (wordmark/README.md).
        private const float CarStartLeft = 364, CarFinalLeft = 195, CarTop = 19.7f, CarWidth = 169.61f, CarHeight = 43;
        private const float RimSize = 14.14f, RimY = 50.07f, TyreRadius = 12.627f;
        private static readonly float[] RimX = { 233.05f, 333.38f };
        private const float RollDelay = 0.35f, RollSeconds = 1.7f;

        private static bool rolledIn;
        private VisualElement car;
        private readonly List<VisualElement> rims = new List<VisualElement>();
        private float rollStart = -1;

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
            e.style.backgroundImage = Resources.Load<Texture2D>("Wordmark/" + file);
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
            if (rollStart < 0)
            {
                if (!shown.Contains(mainColumn)) return;
                rollStart = Time.unscaledTime + RollDelay;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - rollStart) / RollSeconds);
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

        private static VisualElement Group(int index)
        {
            var group = new VisualElement();
            group.AddToClassList("group");
            if (index > 0) group.AddToClassList("spaced");
            return group;
        }

        internal Label Text(string text, string cls, bool isBold = false)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            if (isBold) label.style.unityFontDefinition = FontDefinition.FromFont(bold);
            return label;
        }

        private VisualElement MenuItem(string text, Action onClick)
        {
            Label item = Text(text.ToUpperInvariant(), "item");
            item.RegisterCallback<ClickEvent>(_ => onClick());
            return item;
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

        internal VisualElement RedButton(string text, string cls, Action onClick)
        {
            var button = new VisualElement();
            button.AddToClassList("red");
            button.AddToClassList(cls);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("red-fill");
            fill.style.backgroundImage = RedGradient;
            button.Add(fill);
            Label label = Text(text.ToUpperInvariant(), "red-label", true);
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        private static Texture2D redGradient;

        /// <summary>#ff2b22 at the top to #e10600 at the bottom.</summary>
        private static Texture2D RedGradient
        {
            get
            {
                if (redGradient != null) return redGradient;
                redGradient = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                Color topColour = new Color32(0xff, 0x2b, 0x22, 0xff), bottomColour = new Color32(0xe1, 0x06, 0x00, 0xff);
                for (int y = 0; y < 32; y++) redGradient.SetPixel(0, y, Color.Lerp(bottomColour, topColour, y / 31f));
                redGradient.Apply();
                return redGradient;
            }
        }

        // ------------------------------------------------------------------ settings

        private void BuildSettings()
        {
            settingsPanel.Query<VisualElement>(className: "settings-group").ForEach(g => g.RemoveFromHierarchy());

            VisualElement graphics = SettingsGroup(0);
            IReadOnlyList<Vector2Int> sizes = GameSettings.Resolutions;
            graphics.Add(Stepper("Display mode", GameSettings.DisplayModes.Select(m => m.Name).ToArray(), GameSettings.DisplayMode, i => GameSettings.DisplayMode = i));
            graphics.Add(Stepper("Resolution", sizes.Select(r => r.x + " × " + r.y).ToArray(), sizes.ToList().IndexOf(GameSettings.Resolution), i => GameSettings.Resolution = sizes[i]));
            graphics.Add(Stepper("Quality", GameSettings.QualityNames, GameSettings.Quality, i => GameSettings.Quality = i));
            graphics.Add(Stepper("V-sync", new[] { "Off", "On" }, GameSettings.VSync ? 1 : 0, i => GameSettings.VSync = i == 1));
            settingsPanel.Add(graphics);

            VisualElement audio = SettingsGroup(1);
            audio.Add(Slider("Volume", GameSettings.Volume, v => GameSettings.Volume = v));
            settingsPanel.Add(audio);
        }

        private static VisualElement SettingsGroup(int index)
        {
            var group = new VisualElement();
            group.AddToClassList("settings-group");
            if (index > 0) group.AddToClassList("spaced");
            return group;
        }

        private VisualElement Row(string label)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            row.Add(Text(label.ToUpperInvariant(), "row-label"));
            return row;
        }

        private VisualElement Stepper(string label, string[] options, int index, Action<int> set)
        {
            VisualElement row = Row(label);
            var stepper = new VisualElement();
            stepper.AddToClassList("stepper");
            int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
            Label value = Text(options.Length > 0 ? options[current].ToUpperInvariant() : "", "stepper-value", true);
            void Step(int d)
            {
                int next = Mathf.Clamp(current + d, 0, options.Length - 1);
                if (next == current) return;
                current = next;
                value.text = options[current].ToUpperInvariant();
                set(current);
            }
            stepper.Add(Chevron("chevron-left", () => Step(-1)));
            stepper.Add(value);
            stepper.Add(Chevron("chevron-right", () => Step(1)));
            row.Add(stepper);
            return row;
        }

        internal static VisualElement Chevron(string icon, Action onClick)
        {
            var chevron = new VisualElement();
            chevron.AddToClassList("chevron");
            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("chevron-image");
            image.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + icon);
            chevron.Add(image);
            chevron.RegisterCallback<ClickEvent>(_ => onClick());
            return chevron;
        }

        private VisualElement Slider(string label, float value01, Action<float> set)
        {
            VisualElement row = Row(label);
            var slider = new VisualElement();
            slider.AddToClassList("slider");
            var track = new VisualElement();
            track.AddToClassList("track");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("track-line");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("track-fill");
            line.Add(fill);
            track.Add(line);
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("knob");
            track.Add(knob);
            Label number = Text("", "slider-value", true);
            slider.Add(track);
            slider.Add(number);
            row.Add(slider);

            void Show(float v)
            {
                fill.style.width = Length.Percent(v * 100);
                knob.style.left = Length.Percent(v * 100);
                number.text = Mathf.RoundToInt(v * 100).ToString(CultureInfo.InvariantCulture);
            }
            void SetFrom(Vector2 local)
            {
                float v = Mathf.Clamp01(local.x / Mathf.Max(1, track.layout.width));
                Show(v);
                set(v);
            }
            Show(value01);
            track.RegisterCallback<PointerDownEvent>(e => { track.CapturePointer(e.pointerId); SetFrom(e.localPosition); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (track.HasPointerCapture(e.pointerId)) SetFrom(e.localPosition); });
            track.RegisterCallback<PointerUpEvent>(e => track.ReleasePointer(e.pointerId));
            return row;
        }

        // ------------------------------------------------------------------ state

        private void Go(Mode next, string slug = null)
        {
            HideTooltip();
            mode = next;
            screen = next == Mode.Screen ? slug : null;
            if (next == Mode.Screen || next == Mode.Lineup || next == Mode.NewGame)
            {
                panelBody.Clear();
                panel.name = next == Mode.Screen ? slug : next == Mode.Lineup ? LineupSlug : NewGameSlug;
                ScreenOpened?.Invoke(panel.name, panelBody);
            }
            hqCamera.Show(next == Mode.Main || next == Mode.NewGame ? MainShot : next == Mode.Screen ? slug : HubShot);
            Render();
        }

        private void OpenScreen(string slug)
        {
            if (mode == Mode.Screen && screen == slug) Go(Mode.Hub);
            else Go(Mode.Screen, slug);
        }

        private void Cycle(int step)
        {
            int i = mode == Mode.Screen ? Array.IndexOf(Order, screen) : (step > 0 ? -1 : Order.Length);
            OpenScreen(Order[(i + step + Order.Length) % Order.Length]);
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

        private void StartNewGame()
        {
            // The new game panel's fields come later; until then a career starts with blank names.
            GameSession.NewCareer(new NewGameOptions());
            Go(Mode.Hub);
        }

        private void QuitToMainMenu()
        {
            GameSession.Close();
            overlay = Overlay.None;
            Go(Mode.Main);
        }

        private static void Quit()
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
            bool covered = overlay != Overlay.None;
            bool hasSave = GameSession.Store.Latest() != null;

            Show(mainColumn, mode == Mode.Main && !covered);
            continueItem.style.display = hasSave ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasSave && mode == Mode.Main)
            {
                CareerInfo latest = GameSession.Store.Latest();
                continueSub.text = string.IsNullOrEmpty(latest.TeamName) ? "Year " + latest.Year : latest.TeamName + ", year " + latest.Year;
            }
            Show(top, inGame);
            Show(hubMenu, mode == Mode.Hub && !covered);
            Show(strip, (mode == Mode.Screen || mode == Mode.Lineup) && !covered);
            Show(race, mode == Mode.Hub && !covered);
            Show(panel, (mode == Mode.Screen || mode == Mode.Lineup || mode == Mode.NewGame) && !covered);
            panelButton.style.display = mode == Mode.NewGame ? DisplayStyle.Flex : DisplayStyle.None;
            Show(dimmer, covered);
            Show(pauseLayer, overlay == Overlay.Pause);
            Show(settingsLayer, overlay == Overlay.Settings);
            foreach (KeyValuePair<string, VisualElement> icon in stripIcons)
                icon.Value.EnableInClassList("on", mode == Mode.Screen && icon.Key == screen);
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
            Show(tag, tagWasShown);
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

        private static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

        private static CarInstance RaceCar()
        {
            PlayerTeam p = Player;
            return p?.EnteredCarId == null ? null : p.Cars.FirstOrDefault(c => c.Id == p.EnteredCarId);
        }

        private void OnGameChanged(GameState game)
        {
            PlayerTeam p = Player;
            if (p == null) return;
            teamName.text = p.Name.ToUpperInvariant();
            year.text = "YEAR " + game.CurrentYear.ToString(CultureInfo.InvariantCulture);
            money.text = Money(p.Budget);

            CarInstance car = RaceCar();
            if (car != null)
            {
                CarModel model = CarModels.All.FirstOrDefault(m => m.Id == car.ModelId);
                tagName.text = (model?.Name ?? car.ModelId).ToUpperInvariant();
                double c = Math.Max(0, Math.Min(100, car.Condition));
                tagFill.style.width = Length.Percent((float)c);
                tagPercent.text = Math.Round(c).ToString(CultureInfo.InvariantCulture) + "%";
                tag.EnableInClassList("hi", c >= 60);
                tag.EnableInClassList("mid", c >= 50 && c < 60);
                tag.EnableInClassList("lo", c < 50);
            }
            if (root != null) RenderTag();
        }

        internal static string Money(double amount)
        {
            string digits = Math.Round(Math.Abs(amount)).ToString("N0", CultureInfo.InvariantCulture);
            return (amount < 0 ? "-$" : "$") + digits;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (root == null) return;
            RollWordmark();
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
            tag.style.left = Mathf.Round(p.x + 24);
            tag.style.top = Mathf.Round(p.y - 60);
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
            switch (ev.keyCode)
            {
                case KeyCode.Escape:
                    if (overlay == Overlay.Settings) SetOverlay(settingsFrom);
                    else if (overlay == Overlay.Pause) SetOverlay(Overlay.None);
                    else if (mode == Mode.Screen || mode == Mode.Lineup) Go(Mode.Hub);
                    else if (mode == Mode.NewGame) Go(Mode.Main);
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
