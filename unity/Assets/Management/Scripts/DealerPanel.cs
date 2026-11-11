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
    /// The New car dealer and the Second-hand dealer. Cars are listed by class, cheapest first. A picked car rolls
    /// into the first empty bay, or the last bay when all four are full, and shows its stats against the race car.
    /// With four cars owned it can't be bought, and the panel says why.
    /// </summary>
    public sealed class DealerPanel : MonoBehaviour
    {
        public const string NewSlug = "new-car-dealer", UsedSlug = "second-hand-dealer";

        private static readonly CarClass[] Classes = { CarClass.F, CarClass.E, CarClass.D, CarClass.C, CarClass.B, CarClass.A };

        private Menus menus;
        private GarageUi ui;
        private GarageBays bays;
        private VisualElement body;
        private bool used;
        private GarageUi.StatRows statRows;

        // Kept while the game runs, so each dealer opens as it was left.
        private CarClass newClass, usedClass;
        private string newPick, usedPick;

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
            body = slug == NewSlug || slug == UsedSlug ? panelBody : null;
            if (body == null) return;
            used = slug == UsedSlug;
            PlayerTeam p = Garage.Player;
            if (p != null)
            {
                // With nothing picked, a dealer opens on the race car's class.
                CarClass race = Garage.RaceClass(p);
                if (used && usedPick == null) usedClass = race;
                if (!used && newPick == null) newClass = race == CarClass.F1 ? CarClass.A : race;
            }
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (body != null && body.panel != null) Render();
        }

        private static IReadOnlyList<UsedCarListing> Listings => GameSession.State?.CarMarket.UsedListings ?? Array.Empty<UsedCarListing>();

        private void Render()
        {
            float scroll = GarageUi.ScrollOf(body);
            body.Clear();
            statRows = null;
            PlayerTeam p = Garage.Player;
            if (p == null) return;
            (VisualElement content, VisualElement foot) = ui.Frame(body, scroll);
            if (used) RenderUsed(p, content, foot);
            else RenderNew(p, content, foot);
        }

        // ------------------------------------------------------------------ new cars

        private void RenderNew(PlayerTeam p, VisualElement content, VisualElement foot)
        {
            content.Add(Tabs(Classes, newClass, c =>
            {
                newClass = c;
                newPick = null;
                bays.Browse(null, null);
                Render();
            }));

            List<CarModel> models = (GameSession.State.CarModels.Count > 0 ? GameSession.State.CarModels : CarModels.All)
                .Where(m => m.CarClass == newClass).OrderBy(m => m.Price).ToList();
            var rows = new VisualElement();
            rows.AddToClassList("g-rows");
            rows.Add(HeaderRow(false));
            foreach (CarModel m in models)
            {
                double price = Garage.Buy(p, m.Price);
                VisualElement row = Row(m, newPick == m.Id, () => Pick(m.Id, null));
                row.Add(Cell(Price(price, p.Budget >= price), "g-c-price"));
                rows.Add(row);
            }
            content.Add(rows);

            CarModel pick = models.FirstOrDefault(m => m.Id == newPick);
            if (pick == null)
            {
                bays.Browse(null, null);
                return;
            }
            bays.Browse("new-" + pick.Id, pick.Id);
            double cost = Garage.Buy(p, pick.Price);
            var car = new CarInstance { Id = "", ModelId = pick.Id, Age = 0, Condition = 100 };

            VisualElement sub = Sub(pick);
            sub.Add(ui.Text("NEW", "g-meta"));
            content.Add(CarHead(pick, sub, cost, p.Budget >= cost, cost < pick.Price ? "LIST " + Menus.Money(pick.Price) : null));
            AddStats(p, content, car);
            AddUpgrades(p, content, car, pick);

            BuyFoot(foot, p, "Buy " + Menus.Money(cost), p.Budget >= cost, () =>
            {
                var bought = new CarInstance { Id = DealerData.NextCarId(), ModelId = pick.Id, Age = 0, Condition = 100 };
                GameSession.Apply(g => GameActions.BuyCar(g, bought, cost, pick.Name));
            });
        }

        // ------------------------------------------------------------------ used cars

        private void RenderUsed(PlayerTeam p, VisualElement content, VisualElement foot)
        {
            IReadOnlyList<UsedCarListing> all = Listings;
            bool hasF1 = all.Any(l => Garage.Model(l.ModelId)?.CarClass == CarClass.F1);
            CarClass[] tabs = hasF1 ? Classes.Append(CarClass.F1).ToArray() : Classes;
            content.Add(Tabs(tabs, usedClass, c =>
            {
                usedClass = c;
                usedPick = null;
                bays.Browse(null, null);
                Render();
            }));

            List<UsedCarListing> listings = all.Where(l => Garage.Model(l.ModelId)?.CarClass == usedClass).OrderBy(l => l.Price).ToList();
            var rows = new VisualElement();
            rows.AddToClassList("g-rows");
            if (listings.Count > 0) rows.Add(HeaderRow(true));
            foreach (UsedCarListing l in listings)
            {
                CarModel m = Garage.Model(l.ModelId);
                double price = Garage.Buy(p, l.Price);
                VisualElement row = Row(m, usedPick == l.Id, () => Pick(null, l.Id));
                Label age = ui.Text(Garage.Years(l.Age).ToLowerInvariant(), "g-row-meta");
                ui.Tooltip(age, Garage.AgeTip);
                row.Add(Cell(age, "g-c-age"));
                Label cond = ui.Text(Math.Round(l.Condition).ToString(CultureInfo.InvariantCulture) + "%", "g-row-meta");
                cond.AddToClassList("v-" + Garage.Band(l.Condition));
                ui.Tooltip(cond, Garage.ConditionTip);
                row.Add(Cell(cond, "g-c-cond"));
                row.Add(Cell(Price(price, p.Budget >= price || Garage.CanBeg(p, l.ModelId)), "g-c-price"));
                rows.Add(row);
            }
            content.Add(rows);
            if (listings.Count == 0) content.Add(ui.Text("Nothing in class " + ClassLabels.Name(usedClass) + " this year.", "g-none"));

            UsedCarListing pick = listings.FirstOrDefault(l => l.Id == usedPick);
            if (pick == null)
            {
                bays.Browse(null, null);
                return;
            }
            CarModel model = Garage.Model(pick.ModelId);
            bays.Browse(pick.Id, pick.ModelId);
            double cost = Garage.Buy(p, pick.Price);
            bool beg = Garage.CanBeg(p, pick.ModelId) && p.Budget < cost;
            var car = new CarInstance { Id = pick.Id, ModelId = pick.ModelId, Age = pick.Age, Condition = pick.Condition, InstalledUpgrades = pick.InstalledUpgrades };

            VisualElement sub = Sub(model);
            Label ageMeta = ui.Text(Garage.Years(pick.Age), "g-meta");
            ui.Tooltip(ageMeta, Garage.AgeTip);
            sub.Add(ageMeta);
            Label condMeta = ui.Text(Math.Round(pick.Condition).ToString(CultureInfo.InvariantCulture) + "%", "g-meta");
            condMeta.AddToClassList("v-" + Garage.Band(pick.Condition));
            ui.Tooltip(condMeta, Garage.ConditionTip);
            sub.Add(condMeta);
            content.Add(CarHead(model, sub, cost, p.Budget >= cost, "NEW " + Menus.Money(Garage.Buy(p, model.Price))));
            AddStats(p, content, car);
            AddUpgrades(p, content, car, model);

            double paid = beg ? Math.Max(0, p.Budget) : cost;
            BuyFoot(foot, p, (beg ? "Beg " : "Buy ") + Menus.Money(paid), p.Budget >= cost || beg, () =>
            {
                var bought = car with { Id = DealerData.NextCarId() };
                string listingId = pick.Id;
                usedPick = null;
                GameSession.Apply(g =>
                {
                    GameState next = GameActions.BuyCar(g, bought, paid, model.Name);
                    return next with { CarMarket = next.CarMarket with { UsedListings = next.CarMarket.UsedListings.Where(l => l.Id != listingId).ToArray() } };
                });
            });
        }

        // ------------------------------------------------------------------ pieces

        private void Pick(string modelId, string listingId)
        {
            if (used) usedPick = listingId;
            else newPick = modelId;
            Render();
        }

        private VisualElement Tabs(CarClass[] classes, CarClass on, Action<CarClass> choose)
        {
            var seg = new VisualElement();
            seg.AddToClassList("g-seg");
            for (int i = 0; i < classes.Length; i++)
            {
                CarClass c = classes[i];
                var tab = new VisualElement();
                tab.AddToClassList("g-tab");
                tab.EnableInClassList("on", c == on);
                if (i == classes.Length - 1) tab.AddToClassList("last");
                tab.Add(ui.ClassLabel(c, "cls-md"));
                tab.RegisterCallback<ClickEvent>(_ => { if (c != on) choose(c); });
                seg.Add(tab);
            }
            return seg;
        }

        private VisualElement HeaderRow(bool usedColumns)
        {
            var row = new VisualElement();
            row.AddToClassList("g-row");
            row.AddToClassList("g-hd");
            row.Add(Cell(ui.Text("CAR", "g-hd-cell"), "g-c-name"));
            if (usedColumns)
            {
                row.Add(Cell(ui.Text("AGE", "g-hd-cell"), "g-c-age"));
                row.Add(Cell(ui.Text("CONDITION", "g-hd-cell"), "g-c-cond"));
            }
            row.Add(Cell(ui.Text("PRICE", "g-hd-cell"), "g-c-price"));
            return row;
        }

        /// <summary>A list row: the brand badge and name. The caller adds the other columns.</summary>
        private VisualElement Row(CarModel m, bool on, Action pick)
        {
            var row = new VisualElement();
            row.AddToClassList("g-row");
            row.EnableInClassList("on", on);
            var name = new VisualElement();
            name.AddToClassList("g-c-name");
            name.Add(ui.Badge(m));
            name.Add(ui.Text(m.Name.ToUpperInvariant(), "g-row-name", true));
            row.Add(name);
            row.RegisterCallback<ClickEvent>(_ => pick());
            return row;
        }

        private static VisualElement Cell(VisualElement e, string cls)
        {
            e.AddToClassList(cls);
            return e;
        }

        private Label Price(double price, bool affordable)
        {
            Label l = ui.Text(Menus.Money(price), "g-row-price", true);
            l.EnableInClassList("no", !affordable);
            return l;
        }

        private VisualElement Sub(CarModel m)
        {
            var sub = new VisualElement();
            sub.AddToClassList("g-car-sub");
            sub.Add(ui.ClassLabel(m.CarClass, "cls-sm"));
            return sub;
        }

        /// <summary>The picked car: name, class and details on the left, its price on the right.</summary>
        private VisualElement CarHead(CarModel m, VisualElement sub, double price, bool affordable, string under)
        {
            var head = new VisualElement();
            head.AddToClassList("g-car");
            var left = new VisualElement();
            left.Add(ui.Text(m.Name.ToUpperInvariant(), "g-car-name", true));
            left.Add(sub);
            head.Add(left);
            var right = new VisualElement();
            Label p = ui.Text(Menus.Money(price), "g-car-price", true);
            p.EnableInClassList("no", !affordable);
            right.Add(p);
            if (under != null) right.Add(ui.Text(under, "g-car-list"));
            head.Add(right);
            return head;
        }

        private void AddStats(PlayerTeam p, VisualElement content, CarInstance car)
        {
            CarInstance race = Garage.RaceCar(p);
            if (race != null) content.Add(ui.Section("Vs your " + Garage.Model(race.ModelId)?.Name));
            statRows = ui.Stats(Garage.Effective(car), Garage.Potential(car), race != null ? Garage.Effective(race) : null);
            content.Add(statRows.Root);
        }

        private void AddUpgrades(PlayerTeam p, VisualElement content, CarInstance car, CarModel m)
        {
            if (m.UpgradePacks.Count == 0) return;
            content.Add(ui.Section("Upgrades"));
            var tiles = new List<VisualElement>();
            foreach (var pack in Garage.Packs)
            {
                UpgradePackDefinition def = m.UpgradePacks.FirstOrDefault(u => u.Type == pack.Type);
                if (def == null) continue;
                bool got = Garage.Has(car.InstalledUpgrades, pack.Type);
                var tile = new VisualElement();
                tile.AddToClassList("g-tile");
                tile.EnableInClassList("got", got);
                tile.Add(ui.Text(pack.Name.ToUpperInvariant(), "g-tile-name", true));
                if (got) tile.Add(ui.TickIcon());
                else
                {
                    tile.Add(ui.Text(Menus.Money(Garage.Buy(p, def.Cost)), "g-tile-price", true));
                    GarageUi.Previews(tile, pack.Type, () => statRows);
                }
                tiles.Add(tile);
            }
            content.Add(GarageUi.Tiles(tiles));
        }

        private void BuyFoot(VisualElement foot, PlayerTeam p, string label, bool enabled, Action buy)
        {
            if (Garage.Full(p))
            {
                var full = new VisualElement();
                full.AddToClassList("g-full");
                full.Add(ui.Text("GARAGE FULL", "g-full-title", true));
                full.Add(ui.Text("Sell a car in the workshop to make room.", "g-full-text"));
                foot.Add(full);
                return;
            }
            VisualElement button = menus.RedButton(label, "g-buy", () => { if (enabled && !Garage.Full(Garage.Player)) buy(); });
            button.EnableInClassList("disabled", !enabled);
            foot.Add(button);
        }
    }
}
