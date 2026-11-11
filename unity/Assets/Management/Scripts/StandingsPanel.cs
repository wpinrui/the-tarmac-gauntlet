using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Standings screen: all 100 teams by prestige, each with its race car's class and the change since the last
    /// race. It opens on the player's row, and a copy of that row stays pinned to the edge while it is scrolled away.
    /// Clicking a team shows its result in every race so far in the side pane.
    /// </summary>
    public sealed class StandingsPanel : MenuPanel
    {
        public const string Slug = "standings";
        private const float RowHeight = 36;

        internal const string PrestigeTip = "Earned from finishing positions, with the last 5 races counting double. It sets the starting grid.";

        private ScrollView list;
        private VisualElement pin;
        private int playerIndex = -1;
        private string picked;
        private readonly List<VisualElement> rows = new List<VisualElement>();

        protected override bool Shows(string slug) => slug == Slug;

        protected override void Render()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            Body.Clear();
            rows.Clear();
            if (picked == null || game.Teams.All(t => t.Id != picked)) picked = GameActions.PlayerTeamId;

            Label prestige = Head("PRESTIGE", "c-prestige");
            Tips.Attach(prestige, PrestigeTip);
            Body.Add(HeadRow(Box("c-pos"), Box("c-class"), Fill(), prestige, Head("CHANGE", "c-change")));

            List<BaseTeam> ranked = game.Teams.OrderByDescending(t => t.Prestige).ToList();
            playerIndex = ranked.FindIndex(t => t is PlayerTeam);
            list = Scroll();
            for (int i = 0; i < ranked.Count; i++) list.Add(TeamRow(game, ranked[i], i + 1));
            Body.Add(list);

            pin = playerIndex >= 0 ? TeamRow(game, ranked[playerIndex], playerIndex + 1) : null;
            if (pin != null)
            {
                pin.AddToClassList("pin");
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

        private VisualElement TeamRow(GameState game, BaseTeam team, int rank)
        {
            VisualElement row = Row(() =>
            {
                picked = team.Id;
                foreach (VisualElement r in rows) r.EnableInClassList("on", r.userData as string == picked);
                History(game);
            }, team is PlayerTeam ? "me" : null);
            row.userData = team.Id;
            row.EnableInClassList("on", team.Id == picked);
            rows.Add(row);
            row.Add(Place(Num(rank), rank, "c-pos"));
            CarClass? cls = RaceClass(team);
            Label label = ClassLabels.Make(cls ?? CarClass.F, "cls-sm");
            label.style.visibility = cls.HasValue ? Visibility.Visible : Visibility.Hidden;
            row.Add(label);
            VisualElement name = TeamName(team, team.Name);
            name.AddToClassList("fill");
            row.Add(name);
            row.Add(Cell(team.Prestige.ToString("0.0", CultureInfo.InvariantCulture), "c-prestige", true));

            VisualElement trend = Box("c-change", "trend");
            IReadOnlyList<double> h = team.PrestigeHistory;
            double change = h.Count >= 2 ? Math.Round(h[h.Count - 1] - h[h.Count - 2], 1) : 0;
            if (change != 0)
            {
                string colour = change > 0 ? "v-hi" : "v-lo";
                VisualElement arrow = Icon(change > 0 ? "arrow-up" : "arrow-down", "trend-arrow");
                arrow.AddToClassList(colour);
                trend.Add(arrow);
                Label value = Text(Math.Abs(change).ToString("0.0", CultureInfo.InvariantCulture), "trend-value", true);
                value.AddToClassList(colour);
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
            side.Add(TeamTitle(team));

            var races = new List<(int Year, RaceResult Result, int ClassPos)>();
            foreach (RaceHistoryEntry race in game.RaceHistory)
            {
                RaceResult r = race.Results.FirstOrDefault(x => x.TeamId == team.Id);
                if (r == null) continue;
                int classPos = race.Results.Count(x => x.CarClass == r.CarClass && x.Position <= r.Position);
                races.Add((race.Year, r, classPos));
            }
            if (races.Count == 0) return;

            side.Add(HeadRow(Head("YEAR", "c-year"), Fill(), Head("POS", "c-pos"), Head("CLASS POS", "c-class-pos")));
            ScrollView list = Scroll();
            foreach ((int y, RaceResult r, int cp) in Enumerable.Reverse(races))
            {
                VisualElement row = Row();
                row.Add(Cell(Num(y), "c-year", true));
                VisualElement fill = Fill();
                fill.Add(ClassLabels.Make(r.CarClass, "cls-sm"));
                row.Add(fill);
                if (r.Retired)
                {
                    Label dnf = Cell("DNF", "c-pos", true);
                    dnf.AddToClassList("v-lo");
                    row.Add(dnf);
                    row.Add(Cell("", "c-class-pos"));
                }
                else
                {
                    row.Add(Place(Num(r.Position), r.Position, "c-pos"));
                    row.Add(Place(Num(cp), cp, "c-class-pos"));
                }
                list.Add(row);
            }
            side.Add(list);
        }

        /// <summary>The class of the car a team has entered, or null with none.</summary>
        private static CarClass? RaceClass(BaseTeam team)
        {
            CarInstance car = RaceCar(team);
            return car == null ? null : Model(car.ModelId)?.CarClass;
        }
    }
}
