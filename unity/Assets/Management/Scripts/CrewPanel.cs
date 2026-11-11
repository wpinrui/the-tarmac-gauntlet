using System;
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
    /// The Crew screen: the headcount as 16 figures, its yearly cost and the pit stop time it gives the race car.
    /// Clicking a figure sets the headcount at once; hovering one previews it first.
    /// </summary>
    public sealed class CrewPanel : MenuPanel
    {
        public const string Slug = "crew";
        private const int MaxCrew = 16, PerRow = 8;

        private const string PitTip = "A full stop: refuel, four tyres and a driver swap, with this crew, your Engineer skill and the race car.";

        private VisualElement head, pitBox;
        private Label count, action, cost, pitTime;
        private readonly VisualElement[] members = new VisualElement[MaxCrew];
        private int? hover;

        protected override bool Shows(string slug) => slug == Slug;

        protected override void OnOpened()
        {
            hover = null;
            head = Box("detail-head", "crew-head");
            count = Text("", "detail-big", true);
            action = Text("", "crew-action", true);
            cost = Text("", "detail-price", true);
            cost.AddToClassList("v-hi");
            head.Add(count);
            head.Add(action);
            head.Add(cost);
            Body.Add(head);

            VisualElement grid = Box("crew-grid");
            for (int r = 0; r < MaxCrew / PerRow; r++)
            {
                VisualElement row = Box("crew-row");
                for (int c = 0; c < PerRow; c++)
                {
                    int n = r * PerRow + c + 1;
                    VisualElement member = Box("member");
                    if (c == PerRow - 1) member.AddToClassList("last");
                    member.Add(Icon("crew-member", "member-image"));
                    member.RegisterCallback<PointerEnterEvent>(_ => Hover(n));
                    member.RegisterCallback<ClickEvent>(_ => Choose(n));
                    members[n - 1] = member;
                    row.Add(member);
                }
                grid.Add(row);
            }
            grid.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hover = null;
                Render();
            });
            Body.Add(grid);

            pitBox = Tile("Average pit stop timing");
            Tips.Attach(pitBox.Q<Label>(className: "tile-label"), PitTip);
            pitTime = Text("", "tile-value", true);
            pitBox.Add(pitTime);
            Body.Add(pitBox);
        }

        /// <summary>Clicking the last hired figure lets them go, so the crew can reach 0.</summary>
        private int Target(int n) => Player != null && n == Player.CrewSize ? n - 1 : n;

        /// <summary>A bigger crew must be paid for a whole year from the money in hand. Smaller is always allowed.</summary>
        private static bool Affordable(PlayerTeam p, int n) => n <= p.CrewSize || n * DealerData.CrewCostPerMember <= p.Budget;

        private void Hover(int n)
        {
            PlayerTeam p = Player;
            if (p == null || !Affordable(p, n)) return;
            hover = Target(n);
            Render();
        }

        private void Choose(int n)
        {
            PlayerTeam p = Player;
            if (p == null || !Affordable(p, n)) return;
            int size = Target(n);
            hover = null;
            GameSession.Apply(g => GameActions.SetCrewSize(g, size));
        }

        protected override void Render()
        {
            PlayerTeam p = Player;
            if (p == null) return;
            int current = p.CrewSize, shown = hover ?? current;
            bool preview = shown != current;

            count.text = Num(shown);
            cost.text = Money(shown * DealerData.CrewCostPerMember);
            head.EnableInClassList("preview", preview);
            int change = shown - current;
            action.text = change == 0 ? "" : (change > 0 ? "HIRE " : "RELEASE ") + Num(Math.Abs(change)) + " PIT CREW";

            for (int i = 1; i <= MaxCrew; i++)
            {
                VisualElement m = members[i - 1];
                m.EnableInClassList("in", i <= Math.Min(shown, current));
                m.EnableInClassList("adding", i > current && i <= shown);
                m.EnableInClassList("leaving", i > shown && i <= current);
                m.EnableInClassList("disabled", !Affordable(p, i));
            }

            double? seconds = PitSeconds(p, shown);
            pitBox.style.display = seconds.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            pitBox.EnableInClassList("preview", preview);
            if (seconds.HasValue) pitTime.text = Clock(seconds.Value);
        }

        /// <summary>A full-service stop for the race car with this crew, or null with no race car.</summary>
        private static double? PitSeconds(PlayerTeam p, int crew)
        {
            CarInstance car = RaceCar(p);
            CarModel model = car == null ? null : Model(car.ModelId);
            if (model == null) return null;
            CarStats stats = EffectiveStats.CalculateEffectiveStats(car, model);
            return PitStop.EstimateFullServicePitDuration(stats.PitStopTime, stats.FuelCapacity, crew, p.Skills.Engineer);
        }

        private static string Clock(double seconds)
        {
            int s = (int)Math.Round(seconds);
            return Num(s / 60) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
