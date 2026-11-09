#nullable enable
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    // Checks that CarModels.All matches src/renderer/simulation/carModels.ts.
    public class CarModelsTests
    {
        private static CarModel Model(string id) => CarModels.All.Single(m => m.Id == id);

        [Test]
        public void Has31ModelsInClassOrder()
        {
            Assert.That(CarModels.All, Has.Count.EqualTo(31));
            var counts = CarModels.All.GroupBy(m => m.CarClass).Select(g => (g.Key, g.Count())).ToArray();
            Assert.That(counts, Is.EqualTo(new[]
            {
                (CarClass.F, 6), (CarClass.E, 5), (CarClass.D, 4), (CarClass.C, 5),
                (CarClass.B, 5), (CarClass.A, 5), (CarClass.F1, 1),
            }));
        }

        [Test]
        public void IdsAndNamesMatchTs()
        {
            var expected = new[]
            {
                ("f-01", "Nusa Carrier"), ("f-02", "Liberty Omaha"), ("f-03", "Tora Daruma"),
                ("f-04", "Kaiser Firefly"), ("f-05", "Liberty Collier"), ("f-06", "Nusa Fox"),
                ("e-07", "Liberty Fantasy"), ("e-08", "Seorak Hwaeum"), ("e-09", "Liberty Fortress"),
                ("e-10", "Kaiser Oboe"), ("e-11", "Liberty Mantle"),
                ("d-12", "Seorak Hoeori"), ("d-13", "Kaiser Cappuccino"), ("d-14", "Seorak Rally"),
                ("d-15", "Tora Hanabi"),
                ("c-16", "Archer Crescendo"), ("c-17", "Kaiser Elevate"), ("c-18", "Tora Zero"),
                ("c-19", "Kaiser Klassik"), ("c-20", "Tora Giga"),
                ("b-21", "Feretti Vortice"), ("b-22", "Archer Tachyon"), ("b-23", "Archer Jet Black"),
                ("b-24", "Kaiser Scream"), ("b-25", "Liberty Bounty"),
                ("a-26", "Kaiser Sceptre"), ("a-27", "Archer Hypernova"), ("a-28", "Kaiser K24 Dynamo"),
                ("a-29", "Seorak Cheonma CX-24Si"), ("a-30", "Liberty GE67"),
                ("f1-31", "Feretti F-256V8T"),
            };
            Assert.That(CarModels.All.Select(m => (m.Id, m.Name)).ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void ShitboxIsFirstModel()
        {
            Assert.That(CarModels.All[0].Id, Is.EqualTo(DealerData.ShitboxModelId));
        }

        [Test]
        public void ClassFStatsAndPacks()
        {
            var m = Model("f-01");
            Assert.That(m.Tier, Is.EqualTo(CarTier.Junkyard));
            Assert.That(m.Price, Is.EqualTo(4500));
            Assert.That(m.BaseStats, Is.EqualTo(new CarStats
            {
                Power = 5, Handling = 7, FuelEfficiency = 94, TyreDurability = 92,
                Comfort = 30, Reliability = 90, PitStopTime = 96, FuelCapacity = 42,
            }));
            Assert.That(m.PotentialStats, Is.EqualTo(new CarStats
            {
                Power = 18, Handling = 20, FuelEfficiency = 96, TyreDurability = 94,
                Comfort = 44, Reliability = 90, PitStopTime = 96, FuelCapacity = 42,
            }));
            Assert.That(m.UpgradePacks.Select(p => (p.Type, p.Cost)).ToArray(), Is.EqualTo(new[]
            {
                (UpgradePackType.Power, 800.0), (UpgradePackType.Handling, 700.0), (UpgradePackType.Comfort, 500.0),
            }));
        }

        [Test]
        public void ClassEAndDStats()
        {
            var e = Model("e-08");
            Assert.That(e.Tier, Is.EqualTo(CarTier.DailyDriver));
            Assert.That(e.Price, Is.EqualTo(22500));
            Assert.That(e.BaseStats.Comfort, Is.EqualTo(62));
            Assert.That(e.PotentialStats.Comfort, Is.EqualTo(74));
            Assert.That(e.UpgradePacks.Select(p => p.Cost), Is.EqualTo(new[] { 2000.0, 1800.0, 1200.0 }));

            var d = Model("d-15");
            Assert.That(d.Tier, Is.EqualTo(CarTier.UsedSports));
            Assert.That(d.Price, Is.EqualTo(85000));
            Assert.That(d.BaseStats.Handling, Is.EqualTo(48));
            Assert.That(d.PotentialStats.Handling, Is.EqualTo(60));
            Assert.That(d.BaseStats.PitStopTime, Is.EqualTo(74));
        }

        [Test]
        public void ClassCStats()
        {
            var c = Model("c-19");
            Assert.That(c.Tier, Is.EqualTo(CarTier.TrackCar));
            Assert.That(c.Price, Is.EqualTo(257500));
            Assert.That(c.BaseStats.Power, Is.EqualTo(64));
            Assert.That(c.PotentialStats.Power, Is.EqualTo(76));
            Assert.That(c.BaseStats.FuelCapacity, Is.EqualTo(64));
            Assert.That(c.UpgradePacks.Select(p => p.Cost), Is.EqualTo(new[] { 15000.0, 13000.0, 8000.0 }));
        }

        [Test]
        public void ClassBWithoutComfortPotentialHasNoComfortPack()
        {
            var b = Model("b-24");
            Assert.That(b.Tier, Is.EqualTo(CarTier.GtRaceCar));
            Assert.That(b.Price, Is.EqualTo(900000));
            Assert.That(b.PotentialStats.Comfort, Is.EqualTo(b.BaseStats.Comfort));
            Assert.That(b.PotentialStats.Power, Is.EqualTo(90));
            Assert.That(b.UpgradePacks.Select(p => (p.Type, p.Cost)).ToArray(), Is.EqualTo(new[]
            {
                (UpgradePackType.Power, 40000.0), (UpgradePackType.Handling, 35000.0),
            }));
        }

        [Test]
        public void ClassAHasOnlyComfortPack()
        {
            var a = Model("a-29");
            Assert.That(a.Tier, Is.EqualTo(CarTier.Supercar));
            Assert.That(a.Price, Is.EqualTo(2875000));
            Assert.That(a.BaseStats.Handling, Is.EqualTo(98));
            Assert.That(a.PotentialStats.Handling, Is.EqualTo(98));
            Assert.That(a.PotentialStats.Comfort, Is.EqualTo(16));
            Assert.That(a.UpgradePacks.Select(p => (p.Type, p.Cost)).ToArray(), Is.EqualTo(new[]
            {
                (UpgradePackType.Comfort, 50000.0),
            }));
        }

        [Test]
        public void ClassF1HasNoUpgrades()
        {
            var f1 = Model("f1-31");
            Assert.That(f1.Tier, Is.EqualTo(CarTier.Hypercar));
            Assert.That(f1.Price, Is.EqualTo(15000000));
            Assert.That(f1.PotentialStats, Is.EqualTo(f1.BaseStats));
            Assert.That(f1.BaseStats.Power, Is.EqualTo(99));
            Assert.That(f1.BaseStats.TyreDurability, Is.EqualTo(5));
            Assert.That(f1.UpgradePacks, Is.Empty);
        }

        [Test]
        public void ReliabilityPitStopAndFuelCapacityNeverChangeWithPotential()
        {
            foreach (var m in CarModels.All)
            {
                Assert.That(m.PotentialStats.Reliability, Is.EqualTo(m.BaseStats.Reliability), m.Id);
                Assert.That(m.PotentialStats.PitStopTime, Is.EqualTo(m.BaseStats.PitStopTime), m.Id);
                Assert.That(m.PotentialStats.FuelCapacity, Is.EqualTo(m.BaseStats.FuelCapacity), m.Id);
            }
        }
    }
}
