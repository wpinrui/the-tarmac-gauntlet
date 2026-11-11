using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The garage rules the workshop and dealers share: four bays, prices after the Business discount, and how each
    /// car looks in its bay.
    /// </summary>
    internal static class Garage
    {
        /// <summary>The team owns at most this many cars, one per bay.</summary>
        internal const int Capacity = 4;

        /// <summary>Bays the other owned cars fill, left to right, around the race car.</summary>
        private static readonly int[] OtherBays = { 0, 2, 3 };

        /// <summary>The bay a dealer's car stands in when every bay is taken.</summary>
        internal const int LastBay = 3;

        /// <summary>Spare parts and tyre sets are priced by the race car's class.</summary>
        private static readonly Dictionary<CarClass, double> PartCost = new Dictionary<CarClass, double>
        {
            { CarClass.F, 50 }, { CarClass.E, 100 }, { CarClass.D, 200 }, { CarClass.C, 400 }, { CarClass.B, 800 }, { CarClass.A, 1600 }, { CarClass.F1, 3200 },
        };
        private static readonly Dictionary<CarClass, double> TyreCost = new Dictionary<CarClass, double>
        {
            { CarClass.F, 150 }, { CarClass.E, 300 }, { CarClass.D, 600 }, { CarClass.C, 1200 }, { CarClass.B, 2500 }, { CarClass.A, 5000 }, { CarClass.F1, 10000 },
        };

        /// <summary>Models with a finished 3D car; the rest show as grey bodies.</summary>
        private static readonly Dictionary<string, string> Modelled = new Dictionary<string, string>
        {
            { "f-05", "Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj" },
            { "e-10", "Assets/Art/Vehicles/KaiserOboe/KaiserOboe.obj" },
            { "a-29", "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj" },
        };

        /// <summary>Grey bodies per class: length, width, height and wheel radius in metres, and the side profile.</summary>
        private static readonly Dictionary<CarClass, (float L, float W, float H, float R, HqBodyShape Shape)> Grey =
            new Dictionary<CarClass, (float, float, float, float, HqBodyShape)>
            {
                { CarClass.F, (3.75f, 1.62f, 1.45f, 0.30f, HqBodyShape.Hatch) },
                { CarClass.E, (4.25f, 1.78f, 1.45f, 0.32f, HqBodyShape.Hatch) },
                { CarClass.D, (4.30f, 1.78f, 1.30f, 0.33f, HqBodyShape.Coupe) },
                { CarClass.C, (4.65f, 1.86f, 1.35f, 0.34f, HqBodyShape.Sedan) },
                { CarClass.B, (4.55f, 1.95f, 1.18f, 0.35f, HqBodyShape.Wedge) },
                { CarClass.A, (4.75f, 2.00f, 1.05f, 0.35f, HqBodyShape.Proto) },
                { CarClass.F1, (5.20f, 1.80f, 0.95f, 0.33f, HqBodyShape.Proto) },
            };

        /// <summary>The race car's class, or F with no race car.</summary>
        internal static CarClass RaceClass(PlayerTeam p) => Model(RaceCar(p)?.ModelId ?? "")?.CarClass ?? CarClass.F;

        internal static bool Full(PlayerTeam p) => p.Cars.Count >= Capacity;

        /// <summary>Owned cars by bay: the race car in the centre bay, the others in the free bays left to right.</summary>
        internal static CarInstance[] Layout(PlayerTeam p)
        {
            var bays = new CarInstance[Capacity];
            if (p == null) return bays;
            CarInstance race = RaceCar(p);
            var others = p.Cars.Where(c => c != race).ToList();
            if (race == null && others.Count > 0)
            {
                race = others[0];
                others.RemoveAt(0);
            }
            bays[HqGeometry.RaceBay] = race;
            for (int i = 0; i < OtherBays.Length && i < others.Count; i++) bays[OtherBays[i]] = others[i];
            return bays;
        }

        /// <summary>The bay a car stands in.</summary>
        internal static int BayOf(PlayerTeam p, string carId)
        {
            int i = Array.FindIndex(Layout(p), c => c?.Id == carId);
            return i < 0 ? LastBay : i;
        }

        /// <summary>A dealer's car stands in the first empty bay, or the last bay when all four are full.</summary>
        internal static int BrowseBay(PlayerTeam p)
        {
            int i = Array.IndexOf(Layout(p), null);
            return i < 0 ? LastBay : i;
        }

        /// <summary>Owned cars in bay order, left to right as they stand.</summary>
        internal static List<CarInstance> InBayOrder(PlayerTeam p) => Layout(p).Where(c => c != null).ToList();

        // ------------------------------------------------------------------ prices

        internal static double Buy(PlayerTeam p, double listPrice) => CarMarketLogic.CalculatePurchasePrice(listPrice, p?.Skills.Business ?? 0);

        /// <summary>What selling a car brings in: the dealer's price, halved.</summary>
        internal static double Sale(PlayerTeam p, CarInstance car) =>
            Math.Round(CarMarketLogic.CalculateSalePrice(car, Model(car.ModelId), p.Skills.Business) * DealerData.SalePriceMultiplier);

        internal static double SparePart(PlayerTeam p) => Buy(p, PartCost[RaceClass(p)]);

        internal static double TyreSet(PlayerTeam p) => Buy(p, TyreCost[RaceClass(p)]);

        /// <summary>A team with no car can beg for the cheapest car with whatever money it has.</summary>
        internal static bool CanBeg(PlayerTeam p, string modelId) => modelId == DealerData.ShitboxModelId && p.Cars.Count == 0;

        // ------------------------------------------------------------------ looks

        internal static HqCarLook Look(string key, string modelId)
        {
            CarModel m = Model(modelId);
            var g = Grey[m?.CarClass ?? CarClass.F];
            return new HqCarLook
            {
                Key = key,
                AssetPath = Modelled.TryGetValue(modelId, out string path) ? path : null,
                Shape = g.Shape,
                Length = g.L,
                Width = g.W,
                Height = g.H,
                WheelRadius = g.R,
            };
        }

        /// <summary>The brand is the model name's first word, such as Liberty.</summary>
        internal static string Brand(CarModel m) => m.Name.Split(' ')[0].ToLowerInvariant();

        // ------------------------------------------------------------------ stats

        internal static readonly (CarStatKey Key, string Name, string Tip)[] Stats =
        {
            (CarStatKey.Power, "Power", "Straight-line speed."),
            (CarStatKey.Handling, "Handling", "Cornering speed."),
            (CarStatKey.FuelEfficiency, "Fuel efficiency", "Higher burns less fuel each lap."),
            (CarStatKey.TyreDurability, "Tyre durability", "Higher wears the tyres more slowly."),
            (CarStatKey.Comfort, "Comfort", "Higher tires the drivers more slowly."),
            (CarStatKey.Reliability, "Reliability", "Higher means fewer issues and failures."),
            (CarStatKey.FuelCapacity, "Fuel capacity", "Litres in a full tank."),
            (CarStatKey.PitStopTime, "Pit stop", "Base seconds for a stop. Crew and the Engineer skill cut it."),
        };

        internal const string ConditionTip = "Every stat scales with condition. Each spare part repairs 5%.";
        internal const string AgeTip = "Every stat drops 2% a year, down to half.";

        internal static readonly (UpgradePackType Type, string Name, CarStatKey[] Raises)[] Packs =
        {
            (UpgradePackType.Power, "Power", new[] { CarStatKey.Power, CarStatKey.FuelEfficiency }),
            (UpgradePackType.Handling, "Handling", new[] { CarStatKey.Handling, CarStatKey.TyreDurability }),
            (UpgradePackType.Comfort, "Comfort", new[] { CarStatKey.Comfort }),
        };

        internal static bool Has(InstalledUpgrades u, UpgradePackType t) =>
            t == UpgradePackType.Power ? u.Power : t == UpgradePackType.Handling ? u.Handling : u.Comfort;

        internal static CarStats Effective(CarInstance car) => EffectiveStats.CalculateEffectiveStats(car, Model(car.ModelId));

        /// <summary>The stats with every pack the car can still take, for the ghost bars.</summary>
        internal static CarStats Potential(CarInstance car)
        {
            CarModel m = Model(car.ModelId);
            var all = car with { InstalledUpgrades = new InstalledUpgrades { Power = true, Handling = true, Comfort = true } };
            CarStats pot = EffectiveStats.CalculateEffectiveStats(all, m), cur = Effective(car);
            foreach (var pack in Packs)
            {
                bool open = !Has(car.InstalledUpgrades, pack.Type) && m.UpgradePacks.Any(u => u.Type == pack.Type);
                if (!open)
                    foreach (CarStatKey k in pack.Raises) pot = pot.With(k, cur.Get(k));
            }
            return pot;
        }
    }
}
