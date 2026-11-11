using System;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Crew screen: the headcount as 16 figures, its yearly cost and the pit stop time it gives the race car.
    /// Clicking a figure sets the headcount at once; hovering one previews it first.
    /// </summary>
    public sealed class CrewPanel : MonoBehaviour
    {
        public const string Slug = "crew";
        private const int MaxCrew = 16, PerRow = 8;

        private Menus menus;
        private VisualElement body, head, pitBox;
        private Label count, cost, pitTime;
        private readonly VisualElement[] members = new VisualElement[MaxCrew];
        private int? hover;

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
            hover = null;
            Build();
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (body != null && body.panel != null) Render();
        }

        private static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

        private void Build()
        {

            head = new VisualElement();
            head.AddToClassList("crew-head");
            count = menus.Text("", "crew-count", true);
            cost = menus.Text("", "crew-cost", true);
            head.Add(count);
            head.Add(cost);
            body.Add(head);

            var grid = new VisualElement();
            grid.AddToClassList("crew-grid");
            Texture2D icon = Resources.Load<Texture2D>("MenuIcons/crew-member");
            for (int r = 0; r < MaxCrew / PerRow; r++)
            {
                var row = new VisualElement();
                row.AddToClassList("crew-row");
                for (int c = 0; c < PerRow; c++)
                {
                    int n = r * PerRow + c + 1;
                    var member = new VisualElement();
                    member.AddToClassList("member");
                    if (c == PerRow - 1) member.AddToClassList("last");
                    var image = new VisualElement { pickingMode = PickingMode.Ignore };
                    image.AddToClassList("member-image");
                    image.style.backgroundImage = icon;
                    member.Add(image);
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
            body.Add(grid);

            pitBox = new VisualElement();
            pitBox.AddToClassList("pit");
            Label label = menus.Text("PIT STOP", "pit-label");
            menus.Tooltip(label, "A full stop: refuel, four tyres and a driver swap, with this crew, your Engineer skill and the race car.");
            pitBox.Add(label);
            pitTime = menus.Text("", "pit-time", true);
            pitBox.Add(pitTime);
            body.Add(pitBox);
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

        private void Render()
        {
            PlayerTeam p = Player;
            if (p == null) return;
            int current = p.CrewSize, shown = hover ?? current;
            bool preview = shown != current;

            count.text = shown.ToString(CultureInfo.InvariantCulture);
            cost.text = Menus.Money(shown * DealerData.CrewCostPerMember);
            head.EnableInClassList("preview", preview);

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
            CarInstance car = p.Cars.FirstOrDefault(c => c.Id == p.EnteredCarId);
            CarModel model = car == null ? null : (GameSession.State.CarModels.FirstOrDefault(m => m.Id == car.ModelId) ?? CarModels.All.FirstOrDefault(m => m.Id == car.ModelId));
            if (model == null) return null;
            CarStats stats = EffectiveStats.CalculateEffectiveStats(car, model);
            return PitStop.EstimateFullServicePitDuration(stats.PitStopTime, stats.FuelCapacity, crew, p.Skills.Engineer);
        }

        private static string Clock(double seconds)
        {
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
