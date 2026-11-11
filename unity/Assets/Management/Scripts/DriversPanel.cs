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
    /// The Drivers screen: the driver market and the team roster in one panel. The four tiles on top are the
    /// lineup, starter first, and dragging one reorders it. Below them the market can be searched, filtered and
    /// sorted, and the box at the bottom shows the selected driver with Hire, Buy out or Release.
    /// </summary>
    public sealed class DriversPanel : MonoBehaviour
    {
        public const string Slug = "drivers";
        private const int MaxDrivers = 4;
        private const string You = RaceEntry.PlayerCharacterDriverId;

        private static readonly string[] StatNames = { "Pace", "Consistency", "Stamina", "Safety", "Smoothness" };
        private static readonly string[] StatShort = { "PAC", "CON", "STA", "SAF", "SMO" };
        private static readonly string[] StatTips =
        {
            "Lap time. Higher is faster.",
            "Lap-to-lap variation. Higher is more predictable.",
            "How quickly fatigue builds during a stint. Higher tires more slowly.",
            "Chance of crashes and mistakes. Higher means fewer incidents.",
            "Tyre wear. Higher wears tyres more slowly.",
        };

        private enum Sort { Age, Stat, Salary }

        /// <summary>One driver as the screen shows them.</summary>
        private sealed class Entry
        {
            public string Id, Name, TeamName;
            public int Age, Ovr;
            public int[] Stats;
            public double Salary;
            public Contract Contract; // their current contract with any team, or null
            public bool Mine => Contract?.TeamId == GameActions.PlayerTeamId;
            public int Stat(int index) => index < 0 ? Ovr : Stats[index];
        }

        private Menus menus;
        private VisualElement body, roster, detail, statHeader;
        private Label statLabel, ageHeader, salaryHeader;
        private ListView list;
        private TextField search;
        private VisualElement freeChip, cheapChip;

        // Screen state, kept while the game runs so the screen opens as it was left.
        private string selected = You;
        private int contractLength = 3, stat = -1;
        private Sort sort = Sort.Stat;
        private bool freeOnly, affordableOnly;

        private readonly List<Entry> market = new List<Entry>();
        private Dictionary<string, Entry> all = new Dictionary<string, Entry>();
        private string dragging;
        private Vector2 dragFrom;

        private void Awake()
        {
            menus = GetComponent<Menus>();
            menus.ScreenOpened += Opened;
            GameSession.Changed += OnChanged;
        }

        private void OnDestroy()
        {
            menus.ScreenOpened -= Opened;
            GameSession.Changed -= OnChanged;
        }

        private void Opened(string slug, VisualElement panelBody)
        {
            body = slug == Slug ? panelBody : null;
            if (body == null) return;
            Build();
            Refresh();
        }

        private void OnChanged(GameState _)
        {
            if (body != null && body.panel != null) Refresh();
        }

        private static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

        // ------------------------------------------------------------------ building

        private void Build()
        {

            roster = new VisualElement();
            roster.AddToClassList("roster");
            body.Add(roster);

            var tools = new VisualElement();
            tools.AddToClassList("tools");
            var searchBox = new VisualElement();
            searchBox.AddToClassList("search");
            var glass = new VisualElement { pickingMode = PickingMode.Ignore };
            glass.AddToClassList("search-icon");
            glass.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/search");
            searchBox.Add(glass);
            search = new TextField();
            search.AddToClassList("search-field");
            search.RegisterValueChangedCallback(_ => RebuildList(true));
            searchBox.Add(search);
            tools.Add(searchBox);
            freeChip = Chip("Free", () => { freeOnly = !freeOnly; RebuildList(true); });
            cheapChip = Chip("Affordable", () => { affordableOnly = !affordableOnly; RebuildList(true); });
            tools.Add(freeChip);
            tools.Add(cheapChip);
            body.Add(tools);

            var header = new VisualElement();
            header.AddToClassList("list-head");
            header.Add(Cell(new VisualElement(), "c-name"));
            header.Add(Cell(new VisualElement(), "c-team"));
            ageHeader = menus.Text("AGE", "head");
            ageHeader.RegisterCallback<ClickEvent>(_ => SortBy(Sort.Age));
            header.Add(Cell(ageHeader, "c-age"));
            statHeader = new VisualElement();
            statHeader.AddToClassList("stat-head");
            statHeader.Add(Menus.Chevron("chevron-left", () => StepStat(-1)));
            statLabel = menus.Text("", "head");
            statLabel.AddToClassList("stat-name");
            statLabel.RegisterCallback<ClickEvent>(_ => SortBy(Sort.Stat));
            statHeader.Add(statLabel);
            statHeader.Add(Menus.Chevron("chevron-right", () => StepStat(1)));
            header.Add(Cell(statHeader, "c-stat"));
            salaryHeader = menus.Text("SALARY", "head");
            salaryHeader.RegisterCallback<ClickEvent>(_ => SortBy(Sort.Salary));
            header.Add(Cell(salaryHeader, "c-salary"));
            body.Add(header);

            list = new ListView
            {
                fixedItemHeight = 35,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                itemsSource = market,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            list.AddToClassList("market");
            list.Q<ScrollView>().verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.Q<ScrollView>().horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(list);

            detail = new VisualElement();
            detail.AddToClassList("detail");
            body.Add(detail);
        }

        private static VisualElement Cell(VisualElement content, string cls)
        {
            content.AddToClassList("cell");
            content.AddToClassList(cls);
            return content;
        }

        private VisualElement Chip(string text, Action onClick)
        {
            Label chip = menus.Text(text.ToUpperInvariant(), "chip");
            chip.RegisterCallback<ClickEvent>(_ => onClick());
            return chip;
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("market-row");
            row.Add(Cell(menus.Text("", "r-name"), "c-name"));
            row.Add(Cell(menus.Text("", "r-team"), "c-team"));
            row.Add(Cell(menus.Text("", "r-age"), "c-age"));
            row.Add(Cell(menus.Text("", "r-stat", true), "c-stat"));
            row.Add(Cell(menus.Text("", "r-salary"), "c-salary"));
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (row.userData is string id) Select(id);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            Entry e = market[index];
            row.userData = e.Id;
            row.EnableInClassList("sel", e.Id == selected);
            row.EnableInClassList("contracted", e.Contract != null);
            row.Q<Label>(className: "r-name").text = e.Name;
            row.Q<Label>(className: "r-team").text = e.TeamName ?? "";
            row.Q<Label>(className: "r-age").text = e.Age.ToString(CultureInfo.InvariantCulture);
            Label value = row.Q<Label>(className: "r-stat");
            int v = e.Stat(stat);
            value.text = v.ToString(CultureInfo.InvariantCulture);
            Band(value, v, "v");
            row.Q<Label>(className: "r-salary").text = Menus.Money(e.Salary);
        }

        // ------------------------------------------------------------------ game data

        private static int Round(double v) => (int)JsMath.Round(v);

        private static Dictionary<string, Entry> Entries(GameState game)
        {
            var teams = game.Teams.ToDictionary(t => t.Id, t => t.Name);
            var contracts = new Dictionary<string, Contract>();
            foreach (Contract c in game.Contracts)
                if (c.RemainingYears > 0 && !contracts.ContainsKey(c.DriverId)) contracts[c.DriverId] = c;

            var entries = new Dictionary<string, Entry>();
            foreach (Driver d in game.Drivers)
            {
                DriverStats s = DriverLifecycle.CalculateDriverStats(d);
                contracts.TryGetValue(d.Id, out Contract contract);
                string team = contract == null || contract.TeamId == GameActions.PlayerTeamId ? null : teams.TryGetValue(contract.TeamId, out string n) ? n : contract.TeamId;
                entries[d.Id] = new Entry
                {
                    Id = d.Id,
                    Name = d.Name,
                    Age = d.Age,
                    Stats = new[] { Round(s.Pace), Round(s.Consistency), Round(s.Stamina), Round(s.Safety), Round(s.Smoothness) },
                    Ovr = Round(DriverLifecycle.TotalDriverStats(s) / 5),
                    Salary = DriverLifecycle.CalculateAnnualSalary(s),
                    Contract = contract,
                    TeamName = team,
                };
            }

            PlayerTeam p = game.Teams.OfType<PlayerTeam>().First();
            int own = p.Skills.Driver * 5;
            entries[You] = new Entry { Id = You, Name = p.PlayerName, Stats = Enumerable.Repeat(own, 5).ToArray(), Ovr = own };
            return entries;
        }

        /// <summary>What it takes to get a market driver: the 3-year salary of a free agent, or a buyout.</summary>
        private static double Price(Entry e, int business) => e.Contract != null
            ? DriverLifecycle.CalculateBuyoutCost(e.Contract)
            : DriverLifecycle.CalculateContractSalary(e.Salary, 3, business);

        // ------------------------------------------------------------------ rendering

        private void Refresh()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            all = Entries(game);
            if (!all.ContainsKey(selected)) selected = You;
            RenderRoster(game);
            RebuildList(false);
            RenderDetail();
        }

        private void RebuildList(bool toTop)
        {
            PlayerTeam p = Player;
            if (p == null) return;
            string q = search.value?.Trim() ?? "";
            IEnumerable<Entry> rows = all.Values.Where(e => e.Id != You && !e.Mine);
            if (q.Length > 0)
                rows = rows.Where(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (e.TeamName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (freeOnly) rows = rows.Where(e => e.Contract == null);
            if (affordableOnly) rows = rows.Where(e => Price(e, p.Skills.Business) <= p.Budget);
            IOrderedEnumerable<Entry> sorted = sort switch
            {
                Sort.Age => rows.OrderBy(e => e.Age).ThenByDescending(e => e.Ovr),
                Sort.Salary => rows.OrderByDescending(e => e.Salary),
                _ => rows.OrderByDescending(e => e.Stat(stat)).ThenByDescending(e => e.Ovr),
            };
            market.Clear();
            market.AddRange(sorted.ThenBy(e => e.Name, StringComparer.Ordinal));

            freeChip.EnableInClassList("on", freeOnly);
            cheapChip.EnableInClassList("on", affordableOnly);
            ageHeader.EnableInClassList("on", sort == Sort.Age);
            salaryHeader.EnableInClassList("on", sort == Sort.Salary);
            statLabel.EnableInClassList("on", sort == Sort.Stat);
            statLabel.text = stat < 0 ? "OVR" : StatShort[stat];
            list.RefreshItems();
            if (toTop && market.Count > 0) list.ScrollToItem(0);
        }

        private void RenderRoster(GameState game)
        {
            roster.Clear();
            IReadOnlyList<string> lineup = DriverLineup.Of(game);
            for (int i = 0; i < MaxDrivers; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("slot");
                if (i == MaxDrivers - 1) slot.AddToClassList("last");
                if (i >= lineup.Count || !all.TryGetValue(lineup[i], out Entry e))
                {
                    slot.AddToClassList("empty");
                    roster.Add(slot);
                    continue;
                }
                slot.userData = e.Id;
                slot.EnableInClassList("sel", e.Id == selected);
                slot.Add(menus.Text(e.Id == You ? "YOU" : e.Name.ToUpperInvariant(), "slot-name"));
                var line = new VisualElement();
                line.AddToClassList("slot-line");
                Label ovr = menus.Text(e.Ovr.ToString(CultureInfo.InvariantCulture), "slot-ovr", true);
                Band(ovr, e.Ovr, "v");
                line.Add(ovr);
                if (e.Contract != null) line.Add(Pips(e.Contract.RemainingYears));
                slot.Add(line);
                Draggable(slot);
                roster.Add(slot);
            }
        }

        private void RenderDetail()
        {
            detail.Clear();
            PlayerTeam p = Player;
            if (p == null || !all.TryGetValue(selected, out Entry e)) return;
            bool full = DriverLineup.Of(GameSession.State).Count >= MaxDrivers;

            var head = new VisualElement();
            head.AddToClassList("detail-head");
            var who = new VisualElement();
            who.Add(menus.Text(e.Name.ToUpperInvariant(), "detail-name", true));
            if (e.Id != You)
            {
                var meta = new VisualElement();
                meta.AddToClassList("detail-meta");
                meta.Add(menus.Text("AGE " + e.Age.ToString(CultureInfo.InvariantCulture), "meta-item"));
                if (e.TeamName != null) meta.Add(menus.Text(e.TeamName.ToUpperInvariant(), "meta-item"));
                if (e.Contract != null) meta.Add(Pips(e.Contract.RemainingYears));
                who.Add(meta);
            }
            head.Add(who);
            Label big = menus.Text(e.Ovr.ToString(CultureInfo.InvariantCulture), "detail-ovr", true);
            Band(big, e.Ovr, "v");
            head.Add(big);
            detail.Add(head);

            var stats = new VisualElement();
            stats.AddToClassList("stats");
            for (int i = 0; i < StatNames.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                Label name = menus.Text(StatNames[i].ToUpperInvariant(), "stat-label");
                menus.Tooltip(name, StatTips[i]);
                row.Add(name);
                row.Add(Bar(e.Stats[i]));
                row.Add(menus.Text(e.Stats[i].ToString(CultureInfo.InvariantCulture), "stat-value", true));
                stats.Add(row);
            }
            detail.Add(stats);

            if (e.Id == You) return;
            if (e.Mine)
            {
                detail.Add(GhostButton("Release", () => Act(g => GameActions.ReleaseDriver(g, e.Id))));
                return;
            }
            if (e.Contract != null)
            {
                double cost = DriverLifecycle.CalculateBuyoutCost(e.Contract);
                bool can = !full && p.Budget >= cost;
                VisualElement buy = menus.RedButton("Buy out " + Menus.Money(cost), "panel-button", () =>
                {
                    if (can) Act(g => GameActions.BuyoutDriver(g, e.Id, e.Name, cost));
                });
                buy.EnableInClassList("disabled", !can);
                detail.Add(buy);
                return;
            }

            var options = new VisualElement();
            options.AddToClassList("contract");
            for (int len = 1; len <= 3; len++)
            {
                int l = len;
                var opt = new VisualElement();
                opt.AddToClassList("option");
                if (len == 3) opt.AddToClassList("last");
                opt.EnableInClassList("sel", len == contractLength);
                opt.Add(menus.Text(len == 1 ? "1 YEAR" : len + " YEARS", "option-label"));
                opt.Add(menus.Text(Menus.Money(DriverLifecycle.CalculateContractSalary(e.Salary, len, p.Skills.Business)), "option-price", true));
                opt.RegisterCallback<ClickEvent>(_ =>
                {
                    contractLength = l;
                    RenderDetail();
                });
                options.Add(opt);
            }
            detail.Add(options);
            double salary = DriverLifecycle.CalculateContractSalary(e.Salary, contractLength, p.Skills.Business);
            bool canHire = !full && p.Budget >= salary;
            int length = contractLength;
            VisualElement hire = menus.RedButton("Hire", "panel-button", () =>
            {
                if (canHire) Act(g => GameActions.HireDriver(g, e.Id, e.Name, length, salary));
            });
            hire.EnableInClassList("disabled", !canHire);
            detail.Add(hire);
        }

        private VisualElement GhostButton(string text, Action onClick)
        {
            var button = new VisualElement();
            button.AddToClassList("ghost-button");
            Label label = menus.Text(text.ToUpperInvariant(), "ghost-label", true);
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        private static VisualElement Bar(int value)
        {
            var bar = new VisualElement();
            bar.AddToClassList("stat-bar");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("stat-fill");
            fill.style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
            Band(fill, value, "f");
            bar.Add(fill);
            return bar;
        }

        private static VisualElement Pips(int count)
        {
            var pips = new VisualElement();
            pips.AddToClassList("pips");
            for (int i = 0; i < count; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                pips.Add(pip);
            }
            return pips;
        }

        /// <summary>Green at 60 or more, amber from 50 to 59, red below 50; "v" colours text, "f" colours a fill.</summary>
        private static void Band(VisualElement e, int value, string kind)
        {
            e.EnableInClassList(kind + "-hi", value >= 60);
            e.EnableInClassList(kind + "-mid", value >= 50 && value < 60);
            e.EnableInClassList(kind + "-lo", value < 50);
        }

        // ------------------------------------------------------------------ actions

        private void Select(string id)
        {
            if (selected == id) return;
            selected = id;
            contractLength = 3;
            foreach (VisualElement slot in roster.Children())
                slot.EnableInClassList("sel", slot.userData as string == id);
            list.RefreshItems();
            RenderDetail();
        }

        private void SortBy(Sort next)
        {
            sort = next;
            RebuildList(true);
        }

        private void StepStat(int step)
        {
            stat = (stat + 1 + step + 6) % 6 - 1;
            sort = Sort.Stat;
            RebuildList(true);
        }

        private static void Act(Func<GameState, GameState> change)
        {
            if (GameSession.State != null) GameSession.Apply(change);
        }

        /// <summary>
        /// A press on a lineup tile selects it; a drag drops it on another tile, which moves it to that place.
        /// </summary>
        private void Draggable(VisualElement slot)
        {
            slot.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = null;
                dragFrom = e.position;
                slot.CapturePointer(e.pointerId);
            });
            slot.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!slot.HasPointerCapture(e.pointerId)) return;
                if (dragging == null && ((Vector2)e.position - dragFrom).sqrMagnitude > 36)
                {
                    dragging = slot.userData as string;
                    slot.AddToClassList("drag");
                }
                if (dragging == null) return;
                VisualElement over = SlotAt(e.position);
                foreach (VisualElement s in roster.Children()) s.EnableInClassList("over", s == over && s != slot);
            });
            slot.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!slot.HasPointerCapture(e.pointerId)) return;
                slot.ReleasePointer(e.pointerId);
                string id = slot.userData as string;
                if (dragging == null)
                {
                    Select(id);
                    return;
                }
                dragging = null;
                VisualElement over = SlotAt(e.position);
                if (over != null && over != slot && over.userData is string target)
                {
                    List<string> order = DriverLineup.Of(GameSession.State).ToList();
                    int to = order.IndexOf(target);
                    order.Remove(id);
                    order.Insert(to, id);
                    Act(g => GameActions.SetLineup(g, order));
                }
                else
                {
                    RenderRoster(GameSession.State);
                }
            });
        }

        private VisualElement SlotAt(Vector2 position) =>
            roster.Children().FirstOrDefault(s => s.userData is string && s.worldBound.Contains(position));
    }
}
