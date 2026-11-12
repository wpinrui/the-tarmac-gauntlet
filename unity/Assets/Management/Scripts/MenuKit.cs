using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The pieces every menu screen is built from, so each kind of thing looks and behaves the same on every screen:
    /// text, list heads and rows, tabs, steppers, tiles, buttons, stat bars, team names and the game data they show.
    /// Their looks are the matching classes in Menus.uss.
    /// </summary>
    internal static class MenuKit
    {
        // ------------------------------------------------------------------ type

        private static Font bold, semiBold;

        /// <summary>Titillium Web Bold, for names, numbers and buttons.</summary>
        internal static Font Bold => bold != null ? bold : bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");

        /// <summary>Titillium Web SemiBold, the menus' default.</summary>
        internal static Font SemiBold => semiBold != null ? semiBold : semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");

        internal static Label Text(string text, string cls, bool isBold = false)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            if (isBold) label.style.unityFontDefinition = FontDefinition.FromFont(Bold);
            return label;
        }

        internal static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ menu items

        /// <summary>A group of menu items. Every group after the first sits a gap below the one before.</summary>
        internal static VisualElement MenuGroup(int index)
        {
            var group = new VisualElement();
            group.AddToClassList("group");
            if (index > 0) group.AddToClassList("spaced");
            return group;
        }

        /// <summary>One item of a menu, in capitals.</summary>
        internal static VisualElement MenuItem(string text, Action onClick)
        {
            Label item = Text(text.ToUpperInvariant(), "item");
            item.RegisterCallback<ClickEvent>(_ => onClick());
            return item;
        }

        /// <summary>Whole dollars, such as $12,500 or -$800.</summary>
        internal static string Money(double amount)
        {
            string digits = Math.Round(Math.Abs(amount)).ToString("N0", CultureInfo.InvariantCulture);
            return (amount < 0 ? "-$" : "$") + digits;
        }

        /// <summary>Whole dollars with a + in front of income.</summary>
        internal static string Signed(double amount) => (amount > 0 ? "+" : "") + Money(amount);

        /// <summary>A car's age, such as 1 YR or 4 YRS.</summary>
        internal static string Years(int age) => Num(age) + (age == 1 ? " YR" : " YRS");

        /// <summary>A lap time as m:ss.fff.</summary>
        internal static string LapTime(double seconds)
        {
            int minutes = (int)(seconds / 60);
            return Num(minutes) + ":" + (seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture);
        }

        /// <summary>A 0 to 100 value's colour band: green at 60 or more, amber from 50, red below.</summary>
        internal static string Band(double value) => value >= 60 ? "hi" : value >= 50 ? "mid" : "lo";

        /// <summary>Colours text (v-) by its 0 to 100 value.</summary>
        internal static T Banded<T>(T e, double value) where T : VisualElement
        {
            foreach (string b in new[] { "hi", "mid", "lo" }) e.EnableInClassList("v-" + b, b == Band(value));
            return e;
        }

        // ------------------------------------------------------------------ boxes

        internal static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            foreach (string c in classes)
                if (!string.IsNullOrEmpty(c)) box.AddToClassList(c);
            return box;
        }

        /// <summary>A vertical list with its scroll bars hidden.</summary>
        internal static ScrollView Scroll(string cls = "list")
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(cls);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            return scroll;
        }

        /// <summary>A Tabler icon from Resources/MenuIcons, sized and tinted by its class.</summary>
        internal static VisualElement Icon(string file, string cls)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(cls);
            icon.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + file);
            return icon;
        }

        /// <summary>A small caps label over a part of a panel, such as DRIVING ORDER.</summary>
        internal static Label Section(string text) => Text(text.ToUpperInvariant(), "section-label");

        // ------------------------------------------------------------------ lists

        /// <summary>A column head. Its column class gives it the width and alignment of the cells under it.</summary>
        internal static Label Head(string text, string column = null)
        {
            Label head = Text(text, "head");
            if (column != null) head.AddToClassList(column);
            return head;
        }

        /// <summary>A row of column heads, lined up with the list rows under it.</summary>
        internal static VisualElement HeadRow(params VisualElement[] heads)
        {
            VisualElement row = Box("head-row");
            foreach (VisualElement h in heads) row.Add(h);
            return row;
        }

        /// <summary>An empty cell that takes the free width, as the name column does.</summary>
        internal static VisualElement Fill() => Box("fill");

        /// <summary>A list row. <paramref name="onClick"/> makes it clickable, with a hover.</summary>
        internal static VisualElement Row(Action onClick = null, params string[] classes)
        {
            VisualElement row = Box(classes);
            row.AddToClassList("row");
            if (onClick != null)
            {
                row.AddToClassList("click");
                row.RegisterCallback<ClickEvent>(_ => onClick());
            }
            return row;
        }

        /// <summary>A cell of text in a column.</summary>
        internal static Label Cell(string text, string column, bool isBold = false)
        {
            Label cell = Text(text, "cell", isBold);
            cell.AddToClassList(column);
            return cell;
        }

        /// <summary>A finishing place: gold, silver and bronze for the first three.</summary>
        internal static Label Place(string text, int place, string column)
        {
            Label cell = Cell(text, column, true);
            if (place >= 1 && place <= 3) cell.AddToClassList("p" + Num(place));
            return cell;
        }

        // ------------------------------------------------------------------ tabs and steppers

        internal static Label Tab(string text, bool on, Action onClick)
        {
            Label tab = Text(text, "tab");
            tab.EnableInClassList("on", on);
            tab.RegisterCallback<ClickEvent>(_ => onClick());
            return tab;
        }

        /// <summary>A tab for one class: the class letter in its colour.</summary>
        internal static Label ClassTab(CarClass c, bool on, Action onClick)
        {
            Label tab = Tab(ClassLabels.Name(c), on, onClick);
            tab.AddToClassList("tab-class");
            tab.style.color = ClassLabels.Colour(c);
            return tab;
        }

        /// <summary>A tab with a count after its name, such as STOPS 3.</summary>
        internal static Label CountTab(string text, int count, bool on, Action onClick)
        {
            Label tab = Tab(text, on, onClick);
            tab.Add(Text(Num(count), "tab-count", true));
            return tab;
        }

        /// <summary>A tab with only an icon, pushed to the right end of its row.</summary>
        internal static Label IconTab(string icon, bool on, Action onClick)
        {
            Label tab = Tab("", on, onClick);
            tab.AddToClassList("tab-end");
            tab.Add(Icon(icon, "tab-icon"));
            return tab;
        }

        internal static VisualElement Chevron(string icon, Action onClick)
        {
            var chevron = new VisualElement();
            chevron.AddToClassList("chevron");
            chevron.Add(Icon(icon, "chevron-image"));
            chevron.RegisterCallback<ClickEvent>(_ => onClick());
            return chevron;
        }

        /// <summary>
        /// A value between a back and an on chevron. A chevron hides where the value can't go further.
        /// <paramref name="step"/> gets -1 or +1.
        /// </summary>
        internal static VisualElement Stepper(string value, string cls, bool canBack, bool canOn, Action<int> step)
        {
            VisualElement stepper = Box("stepper", cls);
            VisualElement back = Chevron("chevron-left", () => { if (canBack) step(-1); });
            back.style.visibility = canBack ? Visibility.Visible : Visibility.Hidden;
            stepper.Add(back);
            stepper.Add(Text(value, "stepper-value", true));
            VisualElement on = Chevron("chevron-right", () => { if (canOn) step(1); });
            on.style.visibility = canOn ? Visibility.Visible : Visibility.Hidden;
            stepper.Add(on);
            return stepper;
        }

        /// <summary>YEAR N between two chevrons, at the top of a panel.</summary>
        internal static VisualElement YearStepper(int year, bool canBack, bool canOn, Action<int> step) =>
            Stepper("YEAR " + Num(year), "year-stepper", canBack, canOn, step);

        // ------------------------------------------------------------------ buttons and tiles

        /// <summary>The red button: the one action a panel is for.</summary>
        internal static VisualElement RedButton(string text, string cls, Action onClick)
        {
            VisualElement button = Box("red", cls);
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

        /// <summary>The outlined button beside or instead of a red one, such as Cancel or Release.</summary>
        internal static VisualElement GhostButton(string text, string cls, Action onClick)
        {
            VisualElement button = Box("ghost-button", cls);
            Label label = Text(text.ToUpperInvariant(), "ghost-label", true);
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        /// <summary>A small button inside a tile or row: a verb, then a price in green.</summary>
        internal static VisualElement SmallButton(string verb, double? price, bool enabled, Action onClick)
        {
            VisualElement b = Box("small-button");
            b.EnableInClassList("disabled", !enabled);
            Label label = Text(verb.ToUpperInvariant(), "small-label", true);
            label.pickingMode = PickingMode.Ignore;
            b.Add(label);
            if (price.HasValue)
            {
                Label m = Text(Money(price.Value), "small-money", true);
                m.pickingMode = PickingMode.Ignore;
                b.Add(m);
            }
            b.RegisterCallback<ClickEvent>(_ => { if (enabled) onClick(); });
            return b;
        }

        /// <summary>
        /// A tile: a small caps label over its content, with <paramref name="corner"/>, such as a count or a tick, at
        /// the label's right. Tiles in a row share its width.
        /// </summary>
        internal static VisualElement Tile(string label, VisualElement corner = null, params string[] classes)
        {
            VisualElement tile = Box(classes);
            tile.AddToClassList("tile");
            VisualElement head = Box("tile-head");
            head.Add(Text(label.ToUpperInvariant(), "tile-label"));
            if (corner != null) head.Add(corner);
            tile.Add(head);
            return tile;
        }

        /// <summary>Lays tiles out in a row of equal widths.</summary>
        internal static VisualElement Tiles(IList<VisualElement> tiles)
        {
            VisualElement row = Box("tiles");
            for (int i = 0; i < tiles.Count; i++)
            {
                if (i == tiles.Count - 1) tiles[i].AddToClassList("last");
                row.Add(tiles[i]);
            }
            return row;
        }

        /// <summary>A green tick, for something already done or owned.</summary>
        internal static VisualElement Tick() => Icon("check", "tick");

        /// <summary>Filled dots, such as a contract's years left.</summary>
        internal static VisualElement Pips(int count)
        {
            VisualElement pips = Box("pips");
            for (int i = 0; i < count; i++) pips.Add(Box("pip"));
            return pips;
        }

        private static Texture2D redGradient;

        /// <summary>#ff2b22 at the top to #e10600 at the bottom.</summary>
        internal static Texture2D RedGradient
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

        // ------------------------------------------------------------------ stats

        /// <summary>A 0 to 100 bar, filled in its colour band.</summary>
        internal static VisualElement Bar(double value)
        {
            VisualElement bar = Box("stat-bar");
            VisualElement fill = Box("stat-fill", "f-" + Band(value));
            fill.pickingMode = PickingMode.Ignore;
            fill.style.width = Length.Percent((float)Math.Max(0, Math.Min(100, value)));
            bar.Add(fill);
            return bar;
        }

        /// <summary>A stat: its name, which explains itself on hover, a bar and the value. Callers add more after.</summary>
        internal static VisualElement StatRow(Tooltips tips, string name, string tip, VisualElement bar, string value)
        {
            VisualElement row = Box("stat-row");
            Label label = Text(name.ToUpperInvariant(), "stat-label");
            tips.Attach(label, tip);
            row.Add(label);
            row.Add(bar);
            row.Add(Text(value, "stat-value", true));
            return row;
        }

        // ------------------------------------------------------------------ skills

        /// <summary>The highest a skill goes.</summary>
        internal const int MaxSkill = 20;

        internal static readonly (PlayerSkillType Type, string Name, string Tip)[] Skills =
        {
            (PlayerSkillType.Driver, "Driver", "Your own driving. All five of your driver stats are this level times 5."),
            (PlayerSkillType.Engineer, "Engineer", "Shorter pit stops, and the car loses condition more slowly in a race."),
            (PlayerSkillType.Business, "Business", "Discounts on purchases, salaries and repairs, and better sale prices."),
        };

        internal static int SkillLevel(PlayerSkills skills, PlayerSkillType type) => skills == null ? 0 : type switch
        {
            PlayerSkillType.Driver => skills.Driver,
            PlayerSkillType.Engineer => skills.Engineer,
            _ => skills.Business,
        };

        /// <summary>
        /// Skill points to spend: a pip for each, filled while unspent, then each skill's level between a minus and a
        /// plus. A level above where it started shows in green. <paramref name="spend"/> gets the skill and -1 or +1.
        /// </summary>
        internal static VisualElement SkillPicker(Tooltips tips, int points, int left, Func<PlayerSkillType, int> level,
            Func<PlayerSkillType, int> start, Action<PlayerSkillType, int> spend)
        {
            VisualElement picker = Box("skills");
            VisualElement pips = Box("skill-pips");
            for (int i = 0; i < points; i++)
            {
                VisualElement pip = Box("skill-pip");
                pip.EnableInClassList("used", i >= left);
                pips.Add(pip);
            }
            picker.Add(pips);
            VisualElement line = Box("skill-line");
            foreach ((PlayerSkillType type, string name, string tip) in Skills)
            {
                int at = level(type);
                VisualElement skill = Box("skill");
                Label label = Text(name.ToUpperInvariant(), "skill-name");
                tips.Attach(label, tip);
                skill.Add(label);
                VisualElement steps = Box("skill-level");
                steps.Add(SkillStep("minus", at > start(type), () => spend(type, -1)));
                Label value = Text(Num(at), "skill-value", true);
                value.EnableInClassList("v-hi", at > start(type));
                steps.Add(value);
                steps.Add(SkillStep("plus", left > 0 && at < MaxSkill, () => spend(type, 1)));
                skill.Add(steps);
                line.Add(skill);
            }
            picker.Add(line);
            return picker;
        }

        private static VisualElement SkillStep(string icon, bool on, Action onClick)
        {
            VisualElement button = Box("skill-step");
            button.Add(Icon(icon, "skill-step-icon"));
            button.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
            button.RegisterCallback<ClickEvent>(_ => { if (on) onClick(); });
            return button;
        }

        // ------------------------------------------------------------------ teams and drivers

        /// <summary>The team's nationality flag, sized by the flag class.</summary>
        internal static VisualElement Flag(BaseTeam team, bool large = false)
        {
            var flag = new VisualElement { pickingMode = PickingMode.Ignore };
            flag.AddToClassList("flag");
            if (large) flag.AddToClassList("flag-lg");
            flag.style.backgroundImage = Resources.Load<Texture2D>("Flags/" + TeamNationality.Of(team));
            return flag;
        }

        /// <summary>A team in one line: its flag and name.</summary>
        internal static VisualElement TeamName(BaseTeam team, string name)
        {
            VisualElement line = Box("team-name");
            if (team != null) line.Add(Flag(team));
            line.Add(Text(name, "team-name-text", true));
            return line;
        }

        /// <summary>
        /// A team in two lines: flag and name, then the car model, starting where the name does. Its row takes the
        /// two-line class, so the row's numbers line up with the name.
        /// </summary>
        internal static VisualElement TeamLines(BaseTeam team, string name, CarModel model)
        {
            VisualElement info = Box("team-lines");
            info.Add(TeamName(team, name));
            if (model != null) info.Add(Text(model.Name, "car-name"));
            return info;
        }

        /// <summary>The team's name heading a side pane: a large flag and the name in capitals.</summary>
        internal static VisualElement TeamTitle(BaseTeam team)
        {
            VisualElement title = Box("team-title");
            title.Add(Flag(team, true));
            title.Add(Text(team.Name.ToUpperInvariant(), "detail-name", true));
            return title;
        }

        /// <summary>A classified car's team in one line, by its team id.</summary>
        internal static VisualElement ResultTeam(ClassifiedCar r) => TeamName(Team(r.TeamId), r.TeamName);

        /// <summary>A podium team: its flag and name, and under it the car model.</summary>
        internal static VisualElement PodiumTeam(ClassifiedCar r) => TeamLines(Team(r.TeamId), r.TeamName, Model(r.ModelId));

        /// <summary>
        /// A driver's name that opens the Drivers screen on them: the market, or the roster for one of the player's own.
        /// </summary>
        internal static Label DriverLink(Menus menus, string name, string driverId, string cls)
        {
            Label link = Text(name, cls, true);
            link.AddToClassList("link");
            link.RegisterCallback<PointerEnterEvent>(_ => link.text = "<u>" + name + "</u>");
            link.RegisterCallback<PointerLeaveEvent>(_ => link.text = name);
            link.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                DriversPanel.Show(menus, driverId);
            });
            return link;
        }

        /// <summary>A coloured square for a driver, as the race summary marks stints.</summary>
        internal static VisualElement Swatch(Color colour)
        {
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("swatch");
            swatch.style.backgroundColor = colour;
            return swatch;
        }

        // ------------------------------------------------------------------ game data

        internal static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

        internal static BaseTeam Team(string id) => GameSession.State?.Teams.FirstOrDefault(t => t.Id == id);

        /// <summary>A car model by id: the career's own list first, else the built-in one.</summary>
        internal static CarModel Model(string id) =>
            GameSession.State?.CarModels.FirstOrDefault(m => m.Id == id) ?? CarModels.All.FirstOrDefault(m => m.Id == id);

        /// <summary>The car a team has entered for the race, or null with none.</summary>
        internal static CarInstance RaceCar(BaseTeam team) => team?.EnteredCarId == null ? null : team.Cars.FirstOrDefault(c => c.Id == team.EnteredCarId);

        /// <summary>A driver's overall: the average of their five stats.</summary>
        internal static int Ovr(Driver d) => (int)JsMath.Round(DriverLifecycle.TotalDriverStats(DriverLifecycle.CalculateDriverStats(d)) / 5);

        /// <summary>
        /// Leader laps per lap of the player's: positions are kept on the leader's laps, while stints, stops and issues
        /// count the player's own. A car that retired is taken to have run at its class winner's pace.
        /// </summary>
        internal static float LapScale(RaceSummary race)
        {
            int leaderLaps = race.PositionByLap.Count;
            ClassifiedCar me = race.Player;
            int pace = me.Retired ? race.Classification.FirstOrDefault(c => c.CarClass == me.CarClass && !c.Retired)?.Laps ?? me.Laps : me.Laps;
            return pace > 0 && leaderLaps > 0 ? leaderLaps / (float)pace : 1f;
        }

        // ------------------------------------------------------------------ top bar

        /// <summary>The team's badge and name on the left, the year and money in glass on the right.</summary>
        internal static (VisualElement Bar, VisualElement Badge, Label Name, Label Year, Label Money) TopBar(MenuGlass glass)
        {
            VisualElement top = Box("top");
            VisualElement team = Box("top-team");
            VisualElement badge = Box("badge");
            team.Add(badge);
            Label name = Text("", "top-name", true);
            team.Add(name);
            top.Add(team);
            VisualElement meta = glass.Make("meta");
            Label year = Text("", "year");
            Label money = Text("", "money", true);
            meta.Add(year);
            meta.Add(money);
            top.Add(meta);
            return (top, badge, name, year, money);
        }

        /// <summary>Fills the top bar from the player's team.</summary>
        internal static void FillTopBar(VisualElement badge, Label name, Label year, Label money)
        {
            PlayerTeam p = Player;
            if (p == null) return;
            name.text = p.Name.ToUpperInvariant();
            Badges.Paint(badge, p.Logo, p.TeamColour);
            year.text = "YEAR " + Num(GameSession.State.CurrentYear);
            money.text = Money(p.Budget);
        }
    }

    /// <summary>
    /// Tooltips, only on stats and skills, where a number needs explaining. One shows under its target after a short
    /// hover, or over it when the target is low on the screen.
    /// </summary>
    internal sealed class Tooltips
    {
        private readonly VisualElement layer;
        private readonly Label tip;
        private IVisualElementScheduledItem timer;

        internal Tooltips(VisualElement layer)
        {
            this.layer = layer;
            tip = MenuKit.Text("", "tip");
            tip.pickingMode = PickingMode.Ignore;
            tip.style.unityFontDefinition = FontDefinition.FromFont(Resources.Load<Font>("Fonts/HostGrotesk-Regular"));
            tip.style.display = DisplayStyle.None;
            layer.Add(tip);
        }

        internal void Attach(VisualElement target, string text)
        {
            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                timer?.Pause();
                timer = target.schedule.Execute(() =>
                {
                    if (target.panel == null) return;
                    tip.text = text;
                    Rect r = target.worldBound;
                    Vector2 top = layer.WorldToLocal(new Vector2(r.xMin, r.yMin)), bottom = layer.WorldToLocal(new Vector2(r.xMin, r.yMax));
                    tip.style.left = Mathf.Round(top.x);
                    bool above = bottom.y > layer.layout.height * 0.75f;
                    if (above)
                    {
                        tip.style.top = StyleKeyword.Auto;
                        tip.style.bottom = Mathf.Round(layer.layout.height - top.y + 8);
                    }
                    else
                    {
                        tip.style.top = Mathf.Round(bottom.y + 8);
                        tip.style.bottom = StyleKeyword.Auto;
                    }
                    tip.style.display = DisplayStyle.Flex;
                }).StartingIn(350);
            });
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        internal void Hide()
        {
            timer?.Pause();
            timer = null;
            tip.style.display = DisplayStyle.None;
        }
    }
}
