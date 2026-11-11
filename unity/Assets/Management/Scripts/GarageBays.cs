using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using UnityEngine;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Keeps the garage bays in step with the team's cars: the race car in the centre bay, the others beside it,
    /// and a car browsed at a dealer in the first empty bay, or the last bay when all four are full. Also points the
    /// workshop and dealer shots at the bay they show, and sets how many crew work in the building.
    /// </summary>
    public sealed class GarageBays : MonoBehaviour
    {
        /// <summary>People working in the building on the main menu, before a game is loaded.</summary>
        private const int DemoCrew = 6;

        private Menus menus;
        private HqCars cars;
        private HqCamera hqCamera;
        private HqCrew crew;
        private GameState shownState;
        private bool hadGame;
        private string browseKey, browseModel, workshopCar;

        private void Awake()
        {
            menus = GetComponent<Menus>();
            menus.ScreenClosed += Closed;
        }

        private void OnDestroy()
        {
            menus.ScreenClosed -= Closed;
        }

        /// <summary>Shows a dealer's car in the browse bay, or none with a null key.</summary>
        internal void Browse(string key, string modelId)
        {
            browseKey = key;
            browseModel = modelId;
            Sync();
        }

        /// <summary>The car the workshop shows; its shot moves to that car's bay.</summary>
        internal void ShowInWorkshop(string carId)
        {
            workshopCar = carId;
            Sync();
        }

        private void Closed(string slug)
        {
            if (slug == DealerPanel.NewSlug || slug == DealerPanel.UsedSlug) Browse(null, null);
        }

        private void LateUpdate()
        {
            if (!ReferenceEquals(GameSession.State, shownState)) Sync();
        }

        private void Sync()
        {
            if (cars == null) cars = FindAnyObjectByType<HqCars>();
            if (hqCamera == null) hqCamera = FindAnyObjectByType<HqCamera>();
            if (cars == null) return;
            shownState = GameSession.State;

            PlayerTeam p = MenuKit.Player;
            if (crew == null) crew = FindAnyObjectByType<HqCrew>();
            if (crew != null) crew.Count = p?.CrewSize ?? DemoCrew;
            if (p == null)
            {
                if (hadGame) cars.ShowDemo();
                hadGame = false;
                return;
            }
            // A game just loaded: the cars are already parked.
            bool instant = !hadGame;
            hadGame = true;

            CarInstance[] layout = Garage.Layout(p);
            int browseBay = Garage.BrowseBay(p);
            for (int i = 0; i < cars.Count && i < layout.Length; i++)
            {
                HqCarLook? look = null;
                if (browseKey != null && i == browseBay) look = Garage.Look(browseKey, browseModel);
                else if (layout[i] != null) look = Garage.Look(layout[i].Id, layout[i].ModelId);
                cars.Set(i, look, instant);
            }

            if (hqCamera == null) return;
            float dealerShift = HqGeometry.BayX[browseBay] - HqGeometry.BayX[Garage.LastBay];
            hqCamera.Shift(DealerPanel.NewSlug, dealerShift);
            hqCamera.Shift(DealerPanel.UsedSlug, dealerShift);
            int workshopBay = workshopCar != null ? Garage.BayOf(p, workshopCar) : HqGeometry.RaceBay;
            hqCamera.Shift(WorkshopPanel.Slug, HqGeometry.BayX[workshopBay] - HqGeometry.BayX[HqGeometry.RaceBay]);
        }
    }
}
