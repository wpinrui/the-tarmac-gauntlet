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
    /// The Car workshop: one owned car at a time, stepped through in bay order with the camera on its bay. Repairs
    /// with spare parts, upgrades, spare parts and tyre sets, choosing the race car, and selling by holding the
    /// sell button.
    /// </summary>
    public sealed class WorkshopPanel : MonoBehaviour
    {
        public const string Slug = "car-workshop";
        private const float HoldSeconds = 0.8f;

        private Menus menus;
        private GarageUi ui;
        private GarageBays bays;
        private VisualElement body;
        private GarageUi.StatRows statRows;

        // Kept while the game runs, so the screen opens as it was left.
        private string carId;
        private int repairParts = -1, buyParts = 1, buyTyres = 1;

        private VisualElement sellFill;
        private float holdStart = -1;

        private void Awake()
        {
            menus = GetComponent<Menus>();
            bays = GetComponent<GarageBays>();
            ui = new GarageUi(menus);
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
            PlayerTeam p = Garage.Player;
            if (p != null && p.Cars.All(c => c.Id != carId)) carId = p.EnteredCarId ?? p.Cars.FirstOrDefault()?.Id;
            repairParts = -1;
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (body != null && body.panel != null) Render();
        }

        private void Render()
        {
            float scroll = GarageUi.ScrollOf(body);
            body.Clear();
            statRows = null;
            sellFill = null;
            holdStart = -1;
            PlayerTeam p = Garage.Player;
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
            CarModel model = Garage.Model(car.ModelId);
            if (model == null) return;
            (VisualElement content, VisualElement foot) = ui.Frame(body, scroll);

            content.Add(Head(p, cars, index, model));
            content.Add(Condition(p, car));
            statRows = ui.Stats(Garage.Effective(car), Garage.Potential(car), null);
            content.Add(statRows.Root);
            if (model.UpgradePacks.Count > 0)
            {
                content.Add(ui.Section("Upgrades"));
                content.Add(Upgrades(p, car, model));
            }
            content.Add(Stock(p));
            Foot(foot, p, car);
        }

        // ------------------------------------------------------------------ pieces

        private VisualElement Head(PlayerTeam p, List<CarInstance> cars, int index, CarModel model)
        {
            var head = new VisualElement();
            head.AddToClassList("g-head");
            bool many = cars.Count > 1;
            VisualElement prev = Menus.Chevron("chevron-left", () => Step(cars, index, -1));
            prev.EnableInClassList("no", !many);
            head.Add(prev);

            var mid = new VisualElement();
            mid.AddToClassList("g-head-mid");
            mid.Add(ui.Text(model.Name.ToUpperInvariant(), "g-name", true));
            var sub = new VisualElement();
            sub.AddToClassList("g-sub");
            sub.Add(ui.ClassLabel(model.CarClass, "cls-sm"));
            Label age = ui.Text(Garage.Years(cars[index].Age), "g-meta");
            ui.Tooltip(age, Garage.AgeTip);
            sub.Add(age);
            if (many)
            {
                var dots = new VisualElement();
                dots.AddToClassList("g-dots");
                for (int i = 0; i < cars.Count; i++)
                {
                    var dot = new VisualElement();
                    dot.AddToClassList("g-dot");
                    dot.EnableInClassList("on", i == index);
                    dot.EnableInClassList("rc", cars[i].Id == p.EnteredCarId);
                    dots.Add(dot);
                }
                sub.Add(dots);
            }
            mid.Add(sub);
            head.Add(mid);

            VisualElement next = Menus.Chevron("chevron-right", () => Step(cars, index, 1));
            next.EnableInClassList("no", !many);
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
            string band = Garage.Band(car.Condition);

            var row = new VisualElement();
            row.AddToClassList("g-cond");
            Label name = ui.Text("CONDITION", "g-lbl");
            ui.Tooltip(name, Garage.ConditionTip);
            row.Add(name);
            var bar = new VisualElement();
            bar.AddToClassList("g-cond-bar");
            var fill = new VisualElement();
            fill.AddToClassList("g-stat-fill");
            fill.AddToClassList("f-" + band);
            fill.style.width = Length.Percent((float)car.Condition);
            bar.Add(fill);
            if (after > car.Condition)
            {
                var ghost = new VisualElement();
                ghost.AddToClassList("g-cond-ghost");
                ghost.style.left = Length.Percent((float)car.Condition);
                ghost.style.width = Length.Percent((float)(after - car.Condition));
                bar.Add(ghost);
            }
            row.Add(bar);
            Label pc = ui.Text(Math.Round(car.Condition).ToString(CultureInfo.InvariantCulture) + "%", "g-cond-pc", true);
            pc.AddToClassList("v-" + band);
            row.Add(pc);

            if (deficit > 0)
            {
                var repair = new VisualElement();
                repair.AddToClassList("g-repair");
                repair.Add(ui.Text("PARTS", "g-lbl"));
                repair.Add(ui.Stepper(repairParts, repairParts > 0, repairParts < maxParts,
                    () => { repairParts--; Render(); }, () => { repairParts++; Render(); }));
                int parts = repairParts;
                double gain = after - car.Condition;
                repair.Add(ui.Button("Repair", null, parts > 0, () =>
                {
                    repairParts = -1;
                    GameSession.Apply(g => GameActions.RepairCar(g, car.Id, parts, gain));
                }));
                row.Add(repair);
            }
            return row;
        }

        private VisualElement Upgrades(PlayerTeam p, CarInstance car, CarModel model)
        {
            var tiles = new List<VisualElement>();
            foreach (var pack in Garage.Packs)
            {
                UpgradePackDefinition def = model.UpgradePacks.FirstOrDefault(u => u.Type == pack.Type);
                if (def == null) continue;
                bool got = Garage.Has(car.InstalledUpgrades, pack.Type);
                var tile = new VisualElement();
                tile.AddToClassList("g-tile");
                tile.AddToClassList("stack");
                tile.EnableInClassList("got", got);
                var head = new VisualElement();
                head.AddToClassList("g-tile-head");
                head.Add(ui.Text(pack.Name.ToUpperInvariant(), "g-tile-name", true));
                if (got) head.Add(ui.TickIcon());
                tile.Add(head);
                if (!got)
                {
                    double cost = Garage.Buy(p, def.Cost);
                    UpgradePackType type = pack.Type;
                    tile.Add(ui.Button("Install", cost, p.Budget >= cost,
                        () => GameSession.Apply(g => GameActions.InstallUpgrade(g, car.Id, type, cost, model.Name))));
                    GarageUi.Previews(tile, type, () => statRows);
                }
                tiles.Add(tile);
            }
            return GarageUi.Tiles(tiles);
        }

        private VisualElement Stock(PlayerTeam p)
        {
            return GarageUi.Tiles(new List<VisualElement>
            {
                StockTile("Spare parts", p.SpareParts, buyParts, Garage.SparePart(p), q => buyParts = q,
                    (g, q, cost) => GameActions.BuySpares(g, q, cost)),
                StockTile("Tyre sets", p.TyreSets, buyTyres, Garage.TyreSet(p), q => buyTyres = q,
                    (g, q, cost) => GameActions.BuyTyres(g, q, cost)),
            });
        }

        private VisualElement StockTile(string name, int count, int quantity, double unit, Action<int> setQuantity, Func<GameState, int, double, GameState> buy)
        {
            var tile = new VisualElement();
            tile.AddToClassList("g-tile");
            tile.AddToClassList("stack");
            var head = new VisualElement();
            head.AddToClassList("g-tile-head");
            head.Add(ui.Text(name.ToUpperInvariant(), "g-tile-name", true));
            head.Add(ui.Text(count.ToString(CultureInfo.InvariantCulture), "g-count", true));
            tile.Add(head);

            var row = new VisualElement();
            row.AddToClassList("g-buy-row");
            row.Add(ui.Stepper(quantity, quantity > 1, true,
                () => { setQuantity(quantity - 1); Render(); }, () => { setQuantity(quantity + 1); Render(); }));
            double cost = quantity * unit;
            PlayerTeam p = Garage.Player;
            row.Add(ui.Button("Buy", cost, p != null && p.Budget >= cost, () =>
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
                var selected = new VisualElement();
                selected.AddToClassList("g-selected");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("g-selected-icon");
                icon.style.backgroundImage = GarageUi.Tick;
                selected.Add(icon);
                selected.Add(ui.Text("SELECTED FOR RACE", "g-selected-label", true));
                foot.Add(selected);
            }
            else
            {
                foot.Add(menus.RedButton("Select for race", "g-race", () => GameSession.Apply(g => GameActions.EnterCar(g, car.Id))));
            }

            // Selling can't be undone, so the button is held down: it fills, then the car goes.
            var sell = new VisualElement();
            sell.AddToClassList("g-sell");
            sellFill = new VisualElement { pickingMode = PickingMode.Ignore };
            sellFill.AddToClassList("g-sell-fill");
            sellFill.style.backgroundImage = Menus.RedGradient;
            sell.Add(sellFill);
            Label label = ui.Text("HOLD TO SELL " + Menus.Money(Garage.Sale(p, car)), "g-sell-label", true);
            label.pickingMode = PickingMode.Ignore;
            sell.Add(label);
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
            PlayerTeam p = Garage.Player;
            CarInstance car = p?.Cars.FirstOrDefault(c => c.Id == carId);
            if (car == null) return;
            List<CarInstance> order = Garage.InBayOrder(p);
            int index = order.FindIndex(c => c.Id == car.Id);
            double price = Garage.Sale(p, car);
            string name = Garage.Model(car.ModelId)?.Name ?? car.ModelId;
            order.RemoveAt(index);
            carId = order.Count > 0 ? order[Math.Min(index, order.Count - 1)].Id : null;
            repairParts = -1;
            GameSession.Apply(g => GameActions.SellCar(g, car.Id, price, name));
            if (order.Count == 0) menus.CloseScreen();
        }
    }
}
