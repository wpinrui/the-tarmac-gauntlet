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
    /// The Car workshop: one owned car at a time, stepped through in bay order with the camera on its bay. Repairs
    /// with spare parts, upgrades, spare parts and tyre sets, choosing the race car, and selling by holding the
    /// sell button.
    /// </summary>
    public sealed class WorkshopPanel : MenuPanel
    {
        public const string Slug = "car-workshop";
        private const float HoldSeconds = 0.8f;

        private GarageBays bays;
        private GarageUi.StatRows statRows;

        // Kept while the game runs, so the screen opens as it was left.
        private string carId;
        private int repairParts = -1, buyParts = 1, buyTyres = 1;

        private VisualElement sellFill;
        private float holdStart = -1;

        protected override bool Shows(string slug) => slug == Slug;

        protected override void OnOpened()
        {
            PlayerTeam p = Player;
            if (p != null && p.Cars.All(c => c.Id != carId)) carId = p.EnteredCarId ?? p.Cars.FirstOrDefault()?.Id;
            repairParts = -1;
        }

        protected override void Render()
        {
            if (bays == null) bays = GetComponent<GarageBays>();
            float scroll = GarageUi.ScrollOf(Body);
            Body.Clear();
            statRows = null;
            sellFill = null;
            holdStart = -1;
            PlayerTeam p = Player;
            if (p == null) return;
            List<CarInstance> cars = Garage.InBayOrder(p);
            int index = cars.FindIndex(c => c.Id == carId);
            if (index < 0 && cars.Count > 0)
            {
                index = 0;
                carId = cars[0].Id;
            }
            bays.ShowInWorkshop(carId);
            if (index < 0) return;

            CarInstance car = cars[index];
            CarModel model = Model(car.ModelId);
            if (model == null) return;
            (VisualElement content, VisualElement foot) = GarageUi.Frame(Body, scroll);

            content.Add(Head(p, cars, index, model));
            statRows = GarageUi.Stats(Tips, Garage.Effective(car), Garage.Potential(car), null);
            statRows.Root.Insert(0, Condition(p, car));
            content.Add(statRows.Root);
            if (model.UpgradePacks.Count > 0)
            {
                content.Add(Section("Upgrades"));
                content.Add(Upgrades(p, car, model));
            }
            content.Add(Stock(p));
            Foot(foot, p, car);
        }

        // ------------------------------------------------------------------ pieces

        private VisualElement Head(PlayerTeam p, List<CarInstance> cars, int index, CarModel model)
        {
            VisualElement head = Box("car-head");
            bool many = cars.Count > 1;
            VisualElement prev = Chevron("chevron-left", () => Step(cars, index, -1));
            prev.style.visibility = many ? Visibility.Visible : Visibility.Hidden;
            head.Add(prev);

            VisualElement mid = Box("car-head-mid");
            mid.Add(Text(model.Name.ToUpperInvariant(), "detail-name", true));
            VisualElement sub = Box("detail-meta");
            sub.Add(ClassLabels.Make(model.CarClass, "cls-sm"));
            Label age = Text(Years(cars[index].Age), "meta-item");
            Tips.Attach(age, Garage.AgeTip);
            sub.Add(age);
            if (many)
            {
                VisualElement dots = Box("dots");
                for (int i = 0; i < cars.Count; i++)
                {
                    VisualElement dot = Box("dot");
                    dot.EnableInClassList("on", i == index);
                    dot.EnableInClassList("rc", cars[i].Id == p.EnteredCarId);
                    dots.Add(dot);
                }
                sub.Add(dots);
            }
            mid.Add(sub);
            head.Add(mid);

            VisualElement next = Chevron("chevron-right", () => Step(cars, index, 1));
            next.style.visibility = many ? Visibility.Visible : Visibility.Hidden;
            head.Add(next);
            return head;
        }

        private void Step(List<CarInstance> cars, int index, int step)
        {
            if (cars.Count < 2) return;
            carId = cars[(index + step + cars.Count) % cars.Count].Id;
            repairParts = -1;
            Render();
        }

        private VisualElement Condition(PlayerTeam p, CarInstance car)
        {
            double deficit = 100 - car.Condition;
            int maxParts = Math.Min(p.SpareParts, (int)Math.Ceiling(deficit / DealerData.ConditionPerPart));
            if (repairParts < 0 || repairParts > maxParts) repairParts = maxParts;
            double after = Math.Min(100, car.Condition + repairParts * DealerData.ConditionPerPart);
            VisualElement bar = Bar(car.Condition);
            if (after > car.Condition)
            {
                VisualElement ghost = Box("stat-ghost", "lit");
                ghost.style.left = Length.Percent((float)car.Condition);
                ghost.style.width = Length.Percent((float)(after - car.Condition));
                bar.Add(ghost);
            }
            VisualElement condition = Box("condition");
            VisualElement row = StatRow(Tips, "Condition", Garage.ConditionTip, bar, Num((int)Math.Round(car.Condition)) + "%");
            Banded(row.Q<Label>(className: "stat-value"), car.Condition);
            row.Add(Box("stat-delta"));
            condition.Add(row);

            if (deficit > 0)
            {
                VisualElement repair = Box("repair");
                repair.Add(Text("PARTS", "meta-item"));
                repair.Add(Stepper(Num(repairParts), "qty-stepper", repairParts > 0, repairParts < maxParts, d =>
                {
                    repairParts += d;
                    Render();
                }));
                int parts = repairParts;
                double gain = after - car.Condition;
                repair.Add(SmallButton("Repair", null, parts > 0, () =>
                {
                    repairParts = -1;
                    GameSession.Apply(g => GameActions.RepairCar(g, car.Id, parts, gain));
                }));
                condition.Add(repair);
            }
            return condition;
        }

        private VisualElement Upgrades(PlayerTeam p, CarInstance car, CarModel model)
        {
            var tiles = new List<VisualElement>();
            foreach (var pack in Garage.Packs)
            {
                UpgradePackDefinition def = model.UpgradePacks.FirstOrDefault(u => u.Type == pack.Type);
                if (def == null) continue;
                bool got = Garage.Has(car.InstalledUpgrades, pack.Type);
                VisualElement tile = Tile(pack.Name, got ? Tick() : null, got ? "got" : null);
                if (!got)
                {
                    double cost = Garage.Buy(p, def.Cost);
                    UpgradePackType type = pack.Type;
                    tile.Add(SmallButton("Install", cost, p.Budget >= cost,
                        () => GameSession.Apply(g => GameActions.InstallUpgrade(g, car.Id, type, cost, model.Name))));
                    GarageUi.Previews(tile, type, () => statRows);
                }
                tiles.Add(tile);
            }
            return Tiles(tiles);
        }

        private VisualElement Stock(PlayerTeam p)
        {
            return Tiles(new List<VisualElement>
            {
                StockTile("Spare parts", p.SpareParts, buyParts, Garage.SparePart(p), q => buyParts = q,
                    (g, q, cost) => GameActions.BuySpares(g, q, cost)),
                StockTile("Tyre sets", p.TyreSets, buyTyres, Garage.TyreSet(p), q => buyTyres = q,
                    (g, q, cost) => GameActions.BuyTyres(g, q, cost)),
            });
        }

        private VisualElement StockTile(string name, int count, int quantity, double unit, Action<int> setQuantity, Func<GameState, int, double, GameState> buy)
        {
            VisualElement tile = Tile(name, Text(Num(count), "tile-count", true));
            VisualElement row = Box("tile-line");
            row.Add(Stepper(Num(quantity), "qty-stepper", quantity > 1, true, d =>
            {
                setQuantity(quantity + d);
                Render();
            }));
            double cost = quantity * unit;
            PlayerTeam p = Player;
            row.Add(SmallButton("Buy", cost, p != null && p.Budget >= cost, () =>
            {
                setQuantity(1);
                repairParts = -1;
                GameSession.Apply(g => buy(g, quantity, cost));
            }));
            tile.Add(row);
            return tile;
        }

        private void Foot(VisualElement foot, PlayerTeam p, CarInstance car)
        {
            if (car.Id == p.EnteredCarId)
            {
                VisualElement selected = Box("selected");
                selected.Add(Icon("check", "selected-icon"));
                selected.Add(Text("SELECTED FOR RACE", "selected-label", true));
                foot.Add(selected);
            }
            else
            {
                foot.Add(RedButton("Select for race", "panel-button", () => GameSession.Apply(g => GameActions.EnterCar(g, car.Id))));
            }

            // Selling can't be undone, so the button is held down: it fills, then the car goes.
            VisualElement sell = GhostButton("Hold to sell " + Money(Garage.Sale(p, car)), "sell", () => { });
            sellFill = Box("sell-fill");
            sellFill.pickingMode = PickingMode.Ignore;
            sellFill.style.backgroundImage = RedGradient;
            sell.Insert(0, sellFill);
            sell.RegisterCallback<PointerDownEvent>(e =>
            {
                sell.CapturePointer(e.pointerId);
                holdStart = Time.unscaledTime;
            });
            sell.RegisterCallback<PointerUpEvent>(e =>
            {
                sell.ReleasePointer(e.pointerId);
                CancelHold();
            });
            sell.RegisterCallback<PointerCaptureOutEvent>(_ => CancelHold());
            foot.Add(sell);
        }

        private void CancelHold()
        {
            holdStart = -1;
            if (sellFill != null) sellFill.style.width = 0;
        }

        private void Update()
        {
            if (holdStart < 0 || sellFill == null) return;
            float t = Mathf.Clamp01((Time.unscaledTime - holdStart) / HoldSeconds);
            sellFill.style.width = Length.Percent(t * 100);
            if (t < 1) return;
            holdStart = -1;
            Sell();
        }

        private void Sell()
        {
            PlayerTeam p = Player;
            CarInstance car = p?.Cars.FirstOrDefault(c => c.Id == carId);
            if (car == null) return;
            List<CarInstance> order = Garage.InBayOrder(p);
            int index = order.FindIndex(c => c.Id == car.Id);
            double price = Garage.Sale(p, car);
            string name = Model(car.ModelId)?.Name ?? car.ModelId;
            order.RemoveAt(index);
            carId = order.Count > 0 ? order[Math.Min(index, order.Count - 1)].Id : null;
            repairParts = -1;
            GameSession.Apply(g => GameActions.SellCar(g, car.Id, price, name));
            if (order.Count == 0) Menus.CloseScreen();
        }
    }
}
