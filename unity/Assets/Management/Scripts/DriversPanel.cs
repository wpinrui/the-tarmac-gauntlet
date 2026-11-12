using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Drivers screen: the driver market and the team roster in one panel. The four tiles on top are the
    /// lineup, starter first, and dragging one reorders it. Below them the market can be searched, filtered and
    /// sorted, and the box at the bottom shows the selected driver with Hire, Buy out or Release.
    /// </summary>
    public sealed class DriversPanel : MenuPanel
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

        private const float StatMenuWidth = 180;

        private VisualElement roster, detail, statHeader, statMenu;
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

        /// <summary>Opens the screen on one driver, clearing any filter that would hide them in the market.</summary>
        internal static void Show(Menus menus, string driverId)
        {
            DriversPanel panel = menus.GetComponent<DriversPanel>();
            panel.selected = driverId;
            panel.contractLength = 3;
            panel.freeOnly = panel.affordableOnly = false;
            panel.focus = true;
            menus.Open(Slug);
        }

        private bool focus;

        protected override bool Shows(string slug) => slug == Slug;

        protected override void OnOpened() => Build();

        protected override void Render()
        {
            Refresh();
            if (!focus) return;
            focus = false;
            int at = market.FindIndex(e => e.Id == selected);
            if (at >= 0) list.schedule.Execute(() => list.ScrollToItem(at));
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Body.Add(Section("Driving order"));
            roster = Box("roster");
            Body.Add(roster);

            VisualElement tools = Box("tools");
            VisualElement searchBox = Box("search");
            searchBox.Add(Icon("search", "search-icon"));
            search = new TextField();
            search.AddToClassList("search-field");
            search.RegisterValueChangedCallback(_ => RebuildList(true));
            searchBox.Add(search);
            tools.Add(searchBox);
            freeChip = Chip("Free", () => { freeOnly = !freeOnly; RebuildList(true); });
            cheapChip = Chip("Affordable", () => { affordableOnly = !affordableOnly; RebuildList(true); });
            tools.Add(freeChip);
            tools.Add(cheapChip);
            Body.Add(tools);

            ageHeader = Head("AGE", "c-age");
            ageHeader.AddToClassList("sort");
            ageHeader.RegisterCallback<ClickEvent>(_ => SortBy(Sort.Age));
            statHeader = Box("c-stat", "stat-head");
            statLabel = Head("", "sort");
            statLabel.pickingMode = PickingMode.Ignore;
            statHeader.Add(statLabel);
            statHeader.Add(Icon("chevron-down", "caret"));
            statHeader.RegisterCallback<ClickEvent>(_ => ToggleStatMenu());
            salaryHeader = Head("SALARY", "c-salary");
            salaryHeader.AddToClassList("sort");
            salaryHeader.RegisterCallback<ClickEvent>(_ => SortBy(Sort.Salary));
            Body.Add(HeadRow(Fill(), Box("c-team"), ageHeader, statHeader, salaryHeader));

            list = new ListView
            {
                fixedItemHeight = 36,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                itemsSource = market,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            list.AddToClassList("list");
            list.Q<ScrollView>().verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.Q<ScrollView>().horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.RegisterCallback<DetachFromPanelEvent>(_ => CloseStatMenu());
            Body.Add(list);

            detail = Box("detail");
            Menus.SideBody.Add(detail);

            statMenu?.RemoveFromHierarchy();
            statMenu = Box("popup");
            statMenu.style.width = StatMenuWidth;
            for (int i = -1; i < StatNames.Length; i++)
            {
                int pick = i;
                Label item = Text(i < 0 ? "OVR" : StatNames[i].ToUpperInvariant(), "popup-item");
                item.userData = pick;
                item.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    PickStat(pick);
                });
                statMenu.Add(item);
            }
            statMenu.style.display = DisplayStyle.None;
            Menus.Popups.Add(statMenu);
            // A press anywhere else closes the stat menu.
            Body.RegisterCallback<PointerDownEvent>(e =>
            {
                if (statMenu.style.display == DisplayStyle.None) return;
                var target = e.target as VisualElement;
                if (target != null && (statMenu.Contains(target) || statHeader.Contains(target))) return;
                CloseStatMenu();
            }, TrickleDown.TrickleDown);
        }

        private VisualElement Chip(string text, Action onClick)
        {
            Label chip = Text(text.ToUpperInvariant(), "chip");
            chip.RegisterCallback<ClickEvent>(_ => onClick());
            return chip;
        }

        private VisualElement MakeRow()
        {
            VisualElement row = null;
            row = Row(() =>
            {
                if (row.userData is string id) Select(id);
            });
            row.Add(Cell("", "fill", true));
            row.Add(Cell("", "c-team"));
            row.Add(Cell("", "c-age"));
            row.Add(Cell("", "c-stat", true));
            row.Add(Cell("", "c-salary"));
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            Entry e = market[index];
            row.userData = e.Id;
            row.EnableInClassList("on", e.Id == selected);
            row.EnableInClassList("out", e.Contract != null);
            row.Q<Label>(className: "fill").text = e.Name;
            row.Q<Label>(className: "c-team").text = e.TeamName ?? "";
            row.Q<Label>(className: "c-age").text = Num(e.Age);
            int v = e.Stat(stat);
            Banded(row.Q<Label>(className: "c-stat"), v).text = Num(v);
            row.Q<Label>(className: "c-salary").text = Money(e.Salary);
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

            PlayerTeam p = Player;
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
                VisualElement slot = Box("tile", "slot");
                if (i == MaxDrivers - 1) slot.AddToClassList("last");
                if (i >= lineup.Count || !all.TryGetValue(lineup[i], out Entry e))
                {
                    slot.AddToClassList("empty");
                    roster.Add(slot);
                    continue;
                }
                slot.userData = e.Id;
                slot.EnableInClassList("on", e.Id == selected);
                VisualElement top = Box("tile-head");
                top.Add(Text(e.Id == You ? "YOU" : e.Name.ToUpperInvariant(), "slot-name"));
                top.Add(Icon("grip", "grip"));
                slot.Add(top);
                VisualElement line = Box("tile-line");
                line.Add(Banded(Text(Num(e.Ovr), "tile-value", true), e.Ovr));
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

            VisualElement head = Box("detail-head");
            VisualElement who = Box();
            who.Add(Text(e.Name.ToUpperInvariant(), "detail-name", true));
            if (e.Id != You)
            {
                VisualElement meta = Box("detail-meta");
                meta.Add(Text("AGE " + Num(e.Age), "meta-item"));
                if (e.TeamName != null) meta.Add(Text(e.TeamName.ToUpperInvariant(), "meta-item"));
                if (e.Contract != null) meta.Add(Pips(e.Contract.RemainingYears));
                who.Add(meta);
            }
            head.Add(who);
            head.Add(Banded(Text(Num(e.Ovr), "detail-big", true), e.Ovr));
            detail.Add(head);

            VisualElement stats = Box("stats");
            for (int i = 0; i < StatNames.Length; i++)
                stats.Add(StatRow(Tips, StatNames[i], StatTips[i], Bar(e.Stats[i]), Num(e.Stats[i])));
            detail.Add(stats);

            if (e.Id == You) return;
            if (e.Mine)
            {
                detail.Add(GhostButton("Release", "panel-button", () => Act(g => GameActions.ReleaseDriver(g, e.Id))));
                return;
            }
            if (e.Contract != null)
            {
                double cost = DriverLifecycle.CalculateBuyoutCost(e.Contract);
                bool can = !full && p.Budget >= cost;
                VisualElement buy = RedButton("Buy out " + Money(cost), "panel-button", () =>
                {
                    if (!can) return;
                    MenuSounds.Play(UiCue.Hire);
                    Act(g => GameActions.BuyoutDriver(g, e.Id, e.Name, cost));
                });
                buy.EnableInClassList("disabled", !can);
                detail.Add(buy);
                return;
            }

            var options = new List<VisualElement>();
            for (int len = 1; len <= 3; len++)
            {
                int l = len;
                VisualElement opt = Tile(len == 1 ? "1 year" : Num(len) + " years", null, "click", len == contractLength ? "on" : null);
                opt.Add(Text(Money(DriverLifecycle.CalculateContractSalary(e.Salary, len, p.Skills.Business)), "tile-value", true));
                opt.RegisterCallback<ClickEvent>(_ =>
                {
                    contractLength = l;
                    RenderDetail();
                });
                options.Add(opt);
            }
            detail.Add(Tiles(options));
            double salary = DriverLifecycle.CalculateContractSalary(e.Salary, contractLength, p.Skills.Business);
            bool canHire = !full && p.Budget >= salary;
            int length = contractLength;
            VisualElement hire = RedButton("Hire", "panel-button", () =>
            {
                if (!canHire) return;
                MenuSounds.Play(UiCue.Hire);
                Act(g => GameActions.HireDriver(g, e.Id, e.Name, length, salary));
            });
            hire.EnableInClassList("disabled", !canHire);
            detail.Add(hire);
        }

        // ------------------------------------------------------------------ actions

        private void Select(string id)
        {
            if (selected == id) return;
            selected = id;
            contractLength = 3;
            foreach (VisualElement slot in roster.Children())
                slot.EnableInClassList("on", slot.userData as string == id);
            list.RefreshItems();
            RenderDetail();
        }

        private void SortBy(Sort next)
        {
            sort = next;
            RebuildList(true);
        }

        /// <summary>The dropdown under the stat column: OVR or one of the five stats, which the column shows and sorts by.</summary>
        private void ToggleStatMenu()
        {
            if (statMenu.style.display == DisplayStyle.Flex)
            {
                CloseStatMenu();
                return;
            }
            foreach (VisualElement item in statMenu.Children())
                item.EnableInClassList("on", item.userData is int i && i == stat);
            Rect r = statHeader.worldBound;
            Vector2 at = Menus.Popups.WorldToLocal(new Vector2(r.xMax, r.yMax));
            statMenu.style.left = Mathf.Round(at.x + 20 - StatMenuWidth);
            statMenu.style.top = Mathf.Round(at.y + 4);
            statMenu.style.display = DisplayStyle.Flex;
        }

        private void CloseStatMenu()
        {
            if (statMenu != null) statMenu.style.display = DisplayStyle.None;
        }

        private void PickStat(int pick)
        {
            stat = pick;
            sort = Sort.Stat;
            CloseStatMenu();
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
