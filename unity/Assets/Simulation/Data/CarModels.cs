#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of src/renderer/simulation/carModels.ts.</summary>
    public static class CarModels
    {
        /// <summary>Maps CarClass to the legacy CarTier for economy config compatibility.</summary>
        private static CarTier TierForClass(CarClass carClass) => carClass switch
        {
            CarClass.F => CarTier.Junkyard,
            CarClass.E => CarTier.DailyDriver,
            CarClass.D => CarTier.UsedSports,
            CarClass.C => CarTier.TrackCar,
            CarClass.B => CarTier.GtRaceCar,
            CarClass.A => CarTier.Supercar,
            CarClass.F1 => CarTier.Hypercar,
            _ => CarTier.Junkyard,
        };

        private sealed record PackCost(double Power, double Handling, double Comfort);

        /// <summary>Upgrade pack cost placeholders scaled by class.</summary>
        private static readonly IReadOnlyDictionary<CarClass, PackCost> PackCosts = new Dictionary<CarClass, PackCost>
        {
            [CarClass.F] = new PackCost(800, 700, 500),
            [CarClass.E] = new PackCost(2_000, 1_800, 1_200),
            [CarClass.D] = new PackCost(5_000, 4_500, 3_000),
            [CarClass.C] = new PackCost(15_000, 13_000, 8_000),
            [CarClass.B] = new PackCost(40_000, 35_000, 20_000),
            [CarClass.A] = new PackCost(100_000, 90_000, 50_000),
            [CarClass.F1] = new PackCost(0, 0, 0),
        };

        /// <summary>Potential stats; null means "same as base" (TS optional field).</summary>
        private sealed record RawPot(
            double? power = null,
            double? fuelEfficiency = null,
            double? handling = null,
            double? tyreDurability = null,
            double? comfort = null);

        /// <summary>base: pow, hnd, fe, td, cmf, rel, pst, fc</summary>
        private sealed record RawCar(string Id, string Name, CarClass CarClass, double Price, double[] Base, RawPot Pot);

        private static CarModel BuildModel(RawCar r)
        {
            double pow = r.Base[0], hnd = r.Base[1], fe = r.Base[2], td = r.Base[3],
                cmf = r.Base[4], rel = r.Base[5], pst = r.Base[6], fc = r.Base[7];
            var baseStats = new CarStats
            {
                Power = pow, Handling = hnd, FuelEfficiency = fe, TyreDurability = td,
                Comfort = cmf, Reliability = rel, PitStopTime = pst, FuelCapacity = fc,
            };
            var potentialStats = new CarStats
            {
                Power = r.Pot.power ?? pow,
                Handling = r.Pot.handling ?? hnd,
                FuelEfficiency = r.Pot.fuelEfficiency ?? fe,
                TyreDurability = r.Pot.tyreDurability ?? td,
                Comfort = r.Pot.comfort ?? cmf,
                Reliability = rel,
                PitStopTime = pst,
                FuelCapacity = fc,
            };

            var costs = PackCosts[r.CarClass];
            var packs = new List<UpgradePackDefinition>();
            if (r.Pot.power != null) packs.Add(new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = costs.Power });
            if (r.Pot.handling != null) packs.Add(new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = costs.Handling });
            if (r.Pot.comfort != null) packs.Add(new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = costs.Comfort });

            return new CarModel
            {
                Id = r.Id,
                Name = r.Name,
                CarClass = r.CarClass,
                Tier = TierForClass(r.CarClass),
                Price = r.Price,
                BaseStats = baseStats,
                PotentialStats = potentialStats,
                UpgradePacks = packs,
            };
        }

        private static readonly RawCar[] Raw =
        {
            // --- Class F ---
            new RawCar("f-01", "Nusa Carrier", CarClass.F, 4_500, new double[] { 5, 7, 94, 92, 30, 90, 96, 42 }, new RawPot(power: 18, fuelEfficiency: 96, handling: 20, tyreDurability: 94, comfort: 44)),
            new RawCar("f-02", "Liberty Omaha", CarClass.F, 5_100, new double[] { 8, 12, 92, 88, 22, 82, 95, 37 }, new RawPot(power: 20, fuelEfficiency: 94, handling: 24, tyreDurability: 92, comfort: 36)),
            new RawCar("f-03", "Tora Daruma", CarClass.F, 5_700, new double[] { 10, 10, 90, 90, 46, 92, 94, 46 }, new RawPot(power: 22, fuelEfficiency: 92, handling: 22, tyreDurability: 92, comfort: 58)),
            new RawCar("f-04", "Kaiser Firefly", CarClass.F, 6_300, new double[] { 9, 11, 88, 86, 52, 80, 93, 55 }, new RawPot(power: 20, fuelEfficiency: 90, handling: 22, tyreDurability: 90, comfort: 64)),
            new RawCar("f-05", "Liberty Collier", CarClass.F, 6_900, new double[] { 13, 14, 84, 82, 36, 78, 92, 50 }, new RawPot(power: 24, fuelEfficiency: 88, handling: 26, tyreDurability: 86, comfort: 48)),
            new RawCar("f-06", "Nusa Fox", CarClass.F, 7_500, new double[] { 15, 16, 82, 80, 28, 76, 91, 45 }, new RawPot(power: 26, fuelEfficiency: 86, handling: 28, tyreDurability: 84, comfort: 40)),

            // --- Class E ---
            new RawCar("e-07", "Liberty Fantasy", CarClass.E, 20_000, new double[] { 24, 26, 82, 78, 32, 86, 88, 42 }, new RawPot(power: 34, fuelEfficiency: 86, handling: 36, tyreDurability: 84, comfort: 46)),
            new RawCar("e-08", "Seorak Hwaeum", CarClass.E, 22_500, new double[] { 22, 24, 86, 82, 62, 94, 88, 50 }, new RawPot(power: 32, fuelEfficiency: 90, handling: 34, tyreDurability: 86, comfort: 74)),
            new RawCar("e-10", "Kaiser Oboe", CarClass.E, 27_500, new double[] { 24, 28, 78, 76, 56, 84, 86, 55 }, new RawPot(power: 34, fuelEfficiency: 82, handling: 40, tyreDurability: 82, comfort: 68)),
            new RawCar("e-11", "Liberty Mantle", CarClass.E, 30_000, new double[] { 22, 24, 74, 78, 72, 96, 84, 60 }, new RawPot(power: 32, fuelEfficiency: 78, handling: 34, tyreDurability: 82, comfort: 80)),

            // --- Class D ---
            new RawCar("d-12", "Seorak Hoeori", CarClass.D, 70_000, new double[] { 38, 46, 64, 60, 28, 86, 76, 50 }, new RawPot(power: 50, fuelEfficiency: 70, handling: 58, tyreDurability: 68, comfort: 42)),
            new RawCar("d-13", "Kaiser Cappuccino", CarClass.D, 75_000, new double[] { 40, 44, 60, 56, 42, 80, 78, 55 }, new RawPot(power: 52, fuelEfficiency: 66, handling: 56, tyreDurability: 64, comfort: 54)),
            new RawCar("d-14", "Seorak Rally", CarClass.D, 80_000, new double[] { 42, 42, 54, 62, 30, 84, 78, 60 }, new RawPot(power: 54, fuelEfficiency: 60, handling: 54, tyreDurability: 70, comfort: 44)),
            new RawCar("d-15", "Tora Hanabi", CarClass.D, 85_000, new double[] { 44, 48, 56, 52, 22, 82, 74, 50 }, new RawPot(power: 56, fuelEfficiency: 62, handling: 60, tyreDurability: 60, comfort: 36)),

            // --- Class C ---
            new RawCar("c-16", "Archer Crescendo", CarClass.C, 190_000, new double[] { 56, 58, 62, 64, 78, 82, 68, 59 }, new RawPot(power: 68, fuelEfficiency: 66, handling: 70, tyreDurability: 70, comfort: 86)),
            new RawCar("c-17", "Kaiser Elevate", CarClass.C, 212_500, new double[] { 58, 58, 58, 62, 82, 78, 66, 73 }, new RawPot(power: 70, fuelEfficiency: 64, handling: 70, tyreDurability: 68, comfort: 90)),
            new RawCar("c-19", "Kaiser Klassik", CarClass.C, 257_500, new double[] { 64, 68, 38, 44, 38, 76, 44, 64 }, new RawPot(power: 76, fuelEfficiency: 44, handling: 78, tyreDurability: 52, comfort: 50)),
            new RawCar("c-20", "Tora Giga", CarClass.C, 280_000, new double[] { 66, 64, 32, 38, 55, 62, 40, 74 }, new RawPot(power: 78, fuelEfficiency: 38, handling: 76, tyreDurability: 46, comfort: 65)),

            // --- Class B ---
            new RawCar("b-21", "Feretti Vortice", CarClass.B, 750_000, new double[] { 76, 82, 28, 36, 26, 58, 36, 86 }, new RawPot(power: 84, fuelEfficiency: 34, handling: 88, tyreDurability: 42, comfort: 36)),
            new RawCar("b-22", "Archer Tachyon", CarClass.B, 800_000, new double[] { 78, 80, 30, 34, 22, 54, 32, 72 }, new RawPot(power: 86, fuelEfficiency: 36, handling: 88, tyreDurability: 40, comfort: 30)),
            new RawCar("b-23", "Archer Jet Black", CarClass.B, 850_000, new double[] { 80, 78, 26, 32, 32, 52, 32, 95 }, new RawPot(power: 88, fuelEfficiency: 32, handling: 84, tyreDurability: 38, comfort: 42)),
            new RawCar("b-24", "Kaiser Scream", CarClass.B, 900_000, new double[] { 82, 84, 22, 28, 16, 46, 30, 92 }, new RawPot(power: 90, fuelEfficiency: 28, handling: 90, tyreDurability: 34)),
            new RawCar("b-25", "Liberty Bounty", CarClass.B, 950_000, new double[] { 84, 86, 26, 30, 18, 50, 24, 65 }, new RawPot(power: 90, fuelEfficiency: 32, handling: 92, tyreDurability: 36)),

            // --- Class A ---
            new RawCar("a-27", "Archer Hypernova", CarClass.A, 2_625_000, new double[] { 92, 92, 22, 22, 10, 36, 20, 72 }, new RawPot(comfort: 18)),
            new RawCar("a-28", "Kaiser K24 Dynamo", CarClass.A, 2_750_000, new double[] { 92, 96, 34, 30, 8, 40, 14, 68 }, new RawPot(comfort: 16)),
            new RawCar("a-29", "Seorak Cheonma CX-24Si", CarClass.A, 2_875_000, new double[] { 94, 98, 36, 32, 8, 44, 12, 68 }, new RawPot(comfort: 16)),
            new RawCar("a-30", "Liberty GE67", CarClass.A, 3_000_000, new double[] { 94, 96, 44, 26, 14, 34, 8, 68 }, new RawPot(comfort: 20)),

            // --- Class F1 ---
            new RawCar("f1-31", "Feretti F-256V8T", CarClass.F1, 15_000_000, new double[] { 99, 99, 8, 5, 2, 12, 6, 30 }, new RawPot()),
        };

        /// <summary>
        /// Full car model catalogue: 28 models across 7 classes (F, E, D, C, B, A, F1).
        /// All numeric stats are from car_roster.md.
        /// </summary>
        public static readonly IReadOnlyList<CarModel> All = Raw.Select(BuildModel).ToArray();
    }
}
