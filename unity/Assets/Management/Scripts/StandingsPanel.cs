using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.OfficeKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Standings screen: all 100 teams by prestige, each with its race car's class and the change since the last
    /// race. It opens on the player's row, and a copy of that row stays pinned to the edge while it is scrolled away.
    /// Clicking a team shows its result in every race so far in the side pane.
    /// </summary>
    public sealed class StandingsPanel : OfficePanel
    {
        public const string Slug = "standings";
        private const float RowHeight = 36;

        internal const string PrestigeTip = "Earned from finishing positions, with the last 5 races counting double. It sets the starting grid.";

        private static readonly Color Up = new Color32(0x3d, 0xdc, 0x84, 0xff), Down = new Color32(0xff, 0x5a, 0x4f, 0xff);

        private ScrollView list;
        private VisualElement pin;
        private int playerIndex = -1;
        private string picked;
        private readonly List<VisualElement> rows = new List<VisualElement>();

        protected override string ScreenSlug => Slug;

        protected override void Render()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            Body.Clear();
            rows.Clear();
            if (picked == null || game.Teams.All(t => t.Id != picked)) picked = GameActions.PlayerTeamId;

            var header = Box("of-head-row");
            header.Add(Box("st-rank"));
            header.Add(Box("of-fill"));
            Label prestige = Menus.Text("PRESTIGE", "of-head");
            prestige.AddToClassList("st-prestige");
            Menus.Tooltip(prestige, PrestigeTip);
            header.Add(prestige);
            Label change = Menus.Text("CHANGE", "of-head");
            change.AddToClassList("st-trend");
            header.Add(change);
            Body.Add(header);

            List<BaseTeam> ranked = game.Teams.OrderByDescending(t => t.Prestige).ToList();
            playerIndex = ranked.FindIndex(t => t is PlayerTeam);
            list = Scroll("st-list");
            for (int i = 0; i < ranked.Count; i++) list.Add(Row(game, ranked[i], i + 1));
            Body.Add(list);

            pin = playerIndex >= 0 ? Row(game, ranked[playerIndex], playerIndex + 1) : null;
            if (pin != null)
            {
                pin.AddToClassList("st-pin");
                pin.style.display = DisplayStyle.None;
                Body.Add(pin);
            }
            History(game);

            list.verticalScroller.valueChanged += _ => Pin();
            list.RegisterCallback<GeometryChangedEvent>(Centre);
        }

        /// <summary>Opens on the player's row, once the list knows its height.</summary>
        private void Centre(GeometryChangedEvent e)
        {
            list.UnregisterCallback<GeometryChangedEvent>(Centre);
            if (playerIndex < 0) return;
            float y = playerIndex * RowHeight - list.contentViewport.layout.height / 2 + RowHeight / 2;
            list.scrollOffset = new Vector2(0, Mathf.Max(0, y));
            Pin();
        }

        /// <summary>Shows the pinned copy at the top or bottom edge while the player's own row is out of view.</summary>
        private void Pin()
        {
            if (pin == null) return;
            float top = playerIndex * RowHeight - list.scrollOffset.y, view = list.contentViewport.layout.height;
            bool above = top < 0, below = top + RowHeight > view;
            pin.style.display = above || below ? DisplayStyle.Flex : DisplayStyle.None;
            pin.EnableInClassList("top", above);
            pin.EnableInClassList("bottom", below);
        }

        private VisualElement Row(GameState game, BaseTeam team, int rank)
        {
            VisualElement row = Box("of-row", "of-click", "st-row");
            row.EnableInClassList("me", team is PlayerTeam);
            row.userData = team.Id;
            row.EnableInClassList("pick", team.Id == picked);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                picked = team.Id;
                foreach (VisualElement r in rows) r.EnableInClassList("pick", r.userData as string == picked);
                History(game);
            });
            rows.Add(row);
            Label place = Menus.Text(Num(rank), "st-rank", true);
            if (rank <= 3) place.AddToClassList("p" + rank);
            row.Add(place);
            CarClass? cls = RaceClass(game, team);
            Label label = ClassLabels.Make(cls ?? CarClass.F, "cls-sm", Menus.Bold);
            label.style.visibility = cls.HasValue ? Visibility.Visible : Visibility.Hidden;
            row.Add(label);
            VisualElement name = Box("of-named", "of-fill");
            name.Add(Flag(team));
            name.Add(Menus.Text(team.Name, "rs-team-name", true));
            row.Add(name);
            row.Add(Menus.Text(team.Prestige.ToString("0.0", CultureInfo.InvariantCulture), "st-prestige", true));

            VisualElement trend = Box("st-trend");
            IReadOnlyList<double> h = team.PrestigeHistory;
            double change = h.Count >= 2 ? Math.Round(h[h.Count - 1] - h[h.Count - 2], 1) : 0;
            if (change != 0)
            {
                VisualElement arrow = Icon(change > 0 ? "arrow-up" : "arrow-down", "st-arrow");
                arrow.style.unityBackgroundImageTintColor = change > 0 ? Up : Down;
                trend.Add(arrow);
                Label value = Menus.Text(Math.Abs(change).ToString("0.0", CultureInfo.InvariantCulture), "of-right", true);
                value.style.color = change > 0 ? Up : Down;
                trend.Add(value);
            }
            row.Add(trend);
            return row;
        }

        /// <summary>The picked team's finish in every race on record, newest first.</summary>
        private void History(GameState game)
        {
            VisualElement side = Menus.SideBody;
            side.Clear();
            BaseTeam team = game.Teams.FirstOrDefault(t => t.Id == picked);
            if (team == null) return;
            VisualElement title = Box("of-named");
            VisualElement flag = Flag(team);
            flag.AddToClassList("of-flag-lg");
            title.Add(flag);
            title.Add(Menus.Text(team.Name.ToUpperInvariant(), "sc-det-name", true));
            side.Add(title);

            var races = new List<(int Year, RaceResult Result, int ClassPos)>();
            foreach (RaceHistoryEntry race in game.RaceHistory)
            {
                RaceResult r = race.Results.FirstOrDefault(x => x.TeamId == team.Id);
                if (r == null) continue;
                int classPos = race.Results.Count(x => x.CarClass == r.CarClass && x.Position <= r.Position);
                races.Add((race.Year, r, classPos));
            }
            if (races.Count == 0) return;

            VisualElement head = Box("of-head-row", "st-hist-head");
            Label year = Menus.Text("YEAR", "of-head");
            year.AddToClassList("st-hist-year");
            head.Add(year);
            head.Add(Box("of-fill"));
            Label pos = Menus.Text("POS", "of-head");
            pos.AddToClassList("st-hist-pos");
            head.Add(pos);
            Label classPosHead = Menus.Text("CLASS POS", "of-head");
            classPosHead.AddToClassList("st-hist-cpos");
            head.Add(classPosHead);
            side.Add(head);

            ScrollView list = Scroll("st-hist");
            foreach ((int y, RaceResult r, int cp) in Enumerable.Reverse(races))
            {
                VisualElement row = Box("of-row", "st-hist-row");
                row.Add(Menus.Text(Num(y), "st-hist-year", true));
                VisualElement fill = Box("of-fill");
                fill.Add(ClassLabels.Make(r.CarClass, "cls-sm", Menus.Bold));
                row.Add(fill);
                Label at = Menus.Text(r.Retired ? "DNF" : "P" + Num(r.Position), "st-hist-pos", true);
                if (r.Retired) at.AddToClassList("of-red");
                row.Add(at);
                Label c = Menus.Text(r.Retired ? "" : Num(cp), "st-hist-cpos", true);
                if (!r.Retired && cp <= 3) c.AddToClassList("p" + cp);
                row.Add(c);
                list.Add(row);
            }
            side.Add(list);
        }

        /// <summary>The class of the car a team has entered, or null with none.</summary>
        internal static CarClass? RaceClass(GameState game, BaseTeam team)
        {
            CarInstance car = team.Cars.FirstOrDefault(c => c.Id == team.EnteredCarId);
            if (car == null) return null;
            return (game.CarModels.FirstOrDefault(m => m.Id == car.ModelId) ?? CarModels.All.FirstOrDefault(m => m.Id == car.ModelId))?.CarClass;
        }
    }
}
