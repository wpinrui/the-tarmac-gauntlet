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

        protected override string ScreenSlug => Slug;

        protected override void Render()
        {
            GameState game = GameSession.State;
            if (game == null) return;
            Body.Clear();

            var header = Box("of-head-row");
            header.Add(Box("st-rank"));
            header.Add(Box("of-fill"));
            Label prestige = Menus.Text("PRESTIGE", "of-head");
            prestige.AddToClassList("st-prestige");
            Menus.Tooltip(prestige, PrestigeTip);
            header.Add(prestige);
            header.Add(Box("st-trend"));
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
            VisualElement row = Box("of-row", "st-row");
            row.EnableInClassList("me", team is PlayerTeam);
            Label place = Menus.Text(Num(rank), "st-rank", true);
            if (rank <= 3) place.AddToClassList("p" + rank);
            row.Add(place);
            CarClass? cls = RaceClass(game, team);
            Label label = ClassLabels.Make(cls ?? CarClass.F, "cls-sm", Menus.Bold);
            label.style.visibility = cls.HasValue ? Visibility.Visible : Visibility.Hidden;
            row.Add(label);
            row.Add(Menus.Text(team.Name, "of-fill", true));
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

        /// <summary>The class of the car a team has entered, or null with none.</summary>
        internal static CarClass? RaceClass(GameState game, BaseTeam team)
        {
            CarInstance car = team.Cars.FirstOrDefault(c => c.Id == team.EnteredCarId);
            if (car == null) return null;
            return (game.CarModels.FirstOrDefault(m => m.Id == car.ModelId) ?? CarModels.All.FirstOrDefault(m => m.Id == car.ModelId))?.CarClass;
        }
    }
}
