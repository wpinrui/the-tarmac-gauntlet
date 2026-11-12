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
    /// The New car dealer and the Second-hand dealer. Cars are listed by class, cheapest first. A picked car rolls
    /// into the first empty bay, or the last bay when all four are full, and shows its stats against the race car.
    /// With four cars owned it can't be bought, and the panel says why.
    /// </summary>
    public sealed class DealerPanel : MenuPanel
    {
        public const string NewSlug = "new-car-dealer", UsedSlug = "second-hand-dealer";

        private static readonly CarClass[] Classes = { CarClass.F, CarClass.E, CarClass.D, CarClass.C, CarClass.B, CarClass.A };

        private GarageBays bays;
        private bool used;
        private GarageUi.StatRows statRows;

        // Kept while the game runs, so each dealer opens as it was left.
        private CarClass newClass, usedClass;
        private string newPick, usedPick;

        protected override bool Shows(string slug) => slug == NewSlug || slug == UsedSlug;

        protected override void OnOpened()
        {
            used = OpenSlug == UsedSlug;
            PlayerTeam p = Player;
            if (p == null) return;
            // With nothing picked, a dealer opens on the race car's class.
            CarClass race = Garage.RaceClass(p);
            if (used && usedPick == null) usedClass = race;
            if (!used && newPick == null) newClass = race == CarClass.F1 ? CarClass.A : race;
        }

        private static IReadOnlyList<UsedCarListing> Listings => GameSession.State?.CarMarket.UsedListings ?? Array.Empty<UsedCarListing>();

        protected override void Render()
        {
            if (bays == null) bays = GetComponent<GarageBays>();
            float scroll = GarageUi.ScrollOf(Body);
            Body.Clear();
            statRows = null;
            PlayerTeam p = Player;
            if (p == null) return;
            (VisualElement content, VisualElement foot) = GarageUi.Frame(Body, scroll);
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
            VisualElement rows = Box("block");
            foreach (CarModel m in models)
            {
                double price = Garage.Buy(p, m.Price);
                VisualElement row = CarRow(m, newPick == m.Id, () => Pick(m.Id, null));
                row.Add(Price(price, p.Budget >= price, "c-price"));
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

            content.Add(CarHead(pick, Sub(pick), cost, p.Budget >= cost, cost < pick.Price ? "LIST " + Money(pick.Price) : null));
            AddStats(p, content, car);
            AddUpgrades(p, content, car, pick);

            BuyFoot(foot, p, "Buy " + Money(cost), p.Budget >= cost, () =>
            {
                var bought = new CarInstance { Id = DealerData.NextCarId(), ModelId = pick.Id, Age = 0, Condition = 100 };
                GameSession.Apply(g => GameActions.BuyCar(g, bought, cost, pick.Name));
            });
        }

        // ------------------------------------------------------------------ used cars

        private void RenderUsed(PlayerTeam p, VisualElement content, VisualElement foot)
        {
            IReadOnlyList<UsedCarListing> all = Listings;
            bool hasF1 = all.Any(l => Model(l.ModelId)?.CarClass == CarClass.F1);
            CarClass[] tabs = hasF1 ? Classes.Append(CarClass.F1).ToArray() : Classes;
            content.Add(Tabs(tabs, usedClass, c =>
            {
                usedClass = c;
                usedPick = null;
                bays.Browse(null, null);
                Render();
            }));

            List<UsedCarListing> listings = all.Where(l => Model(l.ModelId)?.CarClass == usedClass).OrderBy(l => l.Price).ToList();
            VisualElement rows = Box("block");
            if (listings.Count > 0)
            {
                Label age = Head("AGE", "c-age");
                Tips.Attach(age, Garage.AgeTip);
                Label cond = Head("COND", "c-cond");
                Tips.Attach(cond, Garage.ConditionTip);
                rows.Add(HeadRow(Fill(), age, cond, Box("c-price")));
            }
            foreach (UsedCarListing l in listings)
            {
                CarModel m = Model(l.ModelId);
                double price = Garage.Buy(p, l.Price);
                VisualElement row = CarRow(m, usedPick == l.Id, () => Pick(null, l.Id));
                row.Add(Cell(Num(l.Age), "c-age", true));
                row.Add(Banded(Cell(Num((int)Math.Round(l.Condition)) + "%", "c-cond", true), l.Condition));
                row.Add(Price(price, p.Budget >= price || Garage.CanBeg(p, l.ModelId), "c-price"));
                rows.Add(row);
            }
            content.Add(rows);

            UsedCarListing pick = listings.FirstOrDefault(l => l.Id == usedPick);
            if (pick == null)
            {
                bays.Browse(null, null);
                return;
            }
            CarModel model = Model(pick.ModelId);
            bays.Browse(pick.Id, pick.ModelId);
            double cost = Garage.Buy(p, pick.Price);
            bool beg = Garage.CanBeg(p, pick.ModelId) && p.Budget < cost;
            var car = new CarInstance { Id = pick.Id, ModelId = pick.ModelId, Age = pick.Age, Condition = pick.Condition, InstalledUpgrades = pick.InstalledUpgrades };

            VisualElement sub = Sub(model);
            Label ageMeta = Text(Years(pick.Age), "meta-item");
            Tips.Attach(ageMeta, Garage.AgeTip);
            sub.Add(ageMeta);
            Label condMeta = Banded(Text(Num((int)Math.Round(pick.Condition)) + "%", "meta-item"), pick.Condition);
            Tips.Attach(condMeta, Garage.ConditionTip);
            sub.Add(condMeta);
            content.Add(CarHead(model, sub, cost, p.Budget >= cost, "NEW " + Money(Garage.Buy(p, model.Price))));
            AddStats(p, content, car);
            AddUpgrades(p, content, car, model);

            double paid = beg ? Math.Max(0, p.Budget) : cost;
            BuyFoot(foot, p, (beg ? "Beg " : "Buy ") + Money(paid), p.Budget >= cost || beg, () =>
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

        private static VisualElement Tabs(CarClass[] classes, CarClass on, Action<CarClass> choose)
        {
            VisualElement tabs = Box("tabs");
            foreach (CarClass c in classes) tabs.Add(ClassTab(c, c == on, () => { if (c != on) choose(c); }));
            return tabs;
        }

        /// <summary>A list row: the brand badge and name. The caller adds the other columns.</summary>
        private static VisualElement CarRow(CarModel m, bool on, Action pick)
        {
            VisualElement row = Row(pick, on ? "on" : null);
            VisualElement name = Box("fill", "brand-name");
            name.Add(GarageUi.Badge(m));
            name.Add(Text(m.Name, "brand-name-text", true));
            row.Add(name);
            return row;
        }

        /// <summary>A price, dimmed when the team can't pay it.</summary>
        private static Label Price(double price, bool affordable, string cls)
        {
            Label l = Cell(Money(price), cls, true);
            l.AddToClassList(affordable ? "v-hi" : "dim");
            return l;
        }

        private static VisualElement Sub(CarModel m)
        {
            VisualElement sub = Box("detail-meta");
            sub.Add(ClassLabels.Make(m.CarClass, "cls-sm"));
            return sub;
        }

        /// <summary>The picked car: name, class and details on the left, its price on the right.</summary>
        private VisualElement CarHead(CarModel m, VisualElement sub, double price, bool affordable, string under)
        {
            VisualElement head = Box("detail-head");
            VisualElement left = Box();
            left.Add(Text(m.Name.ToUpperInvariant(), "detail-name", true));
            left.Add(sub);
            head.Add(left);
            VisualElement right = Box("detail-right");
            right.Add(Price(price, affordable, "detail-price"));
            if (under != null) right.Add(Text(under, "meta-item"));
            head.Add(right);
            return head;
        }

        private void AddStats(PlayerTeam p, VisualElement content, CarInstance car)
        {
            CarInstance race = RaceCar(p);
            if (race != null) content.Add(Section("Vs your " + Model(race.ModelId)?.Name));
            statRows = GarageUi.Stats(Tips, Garage.Effective(car), Garage.Potential(car), race != null ? Garage.Effective(race) : null);
            content.Add(statRows.Root);
        }

        private void AddUpgrades(PlayerTeam p, VisualElement content, CarInstance car, CarModel m)
        {
            if (m.UpgradePacks.Count == 0) return;
            content.Add(Section("Upgrades"));
            var tiles = new List<VisualElement>();
            foreach (var pack in Garage.Packs)
            {
                UpgradePackDefinition def = m.UpgradePacks.FirstOrDefault(u => u.Type == pack.Type);
                if (def == null) continue;
                bool got = Garage.Has(car.InstalledUpgrades, pack.Type);
                VisualElement tile = Tile(pack.Name, got ? Tick() : Text(Money(Garage.Buy(p, def.Cost)), "tile-price", true), got ? "got" : null);
                if (!got) GarageUi.Previews(tile, pack.Type, () => statRows);
                tiles.Add(tile);
            }
            content.Add(Tiles(tiles));
        }

        private void BuyFoot(VisualElement foot, PlayerTeam p, string label, bool enabled, Action buy)
        {
            if (Garage.Full(p))
            {
                VisualElement full = Box("notice");
                full.Add(Text("GARAGE FULL", "notice-label", true));
                foot.Add(full);
                return;
            }
            VisualElement button = RedButton(label, "panel-button", () =>
            {
                if (!enabled || Garage.Full(Player)) return;
                MenuSounds.Play(UiCue.Buy);
                buy();
            });
            button.EnableInClassList("disabled", !enabled);
            foot.Add(button);
        }
    }
}
