#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.CarMarketLogic;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/carMarket.test.ts.</summary>
    public class CarMarketTests
    {
        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        private static CarModel MakeModel(string id, double price) => new CarModel
        {
            Id = id,
            Name = $"Car {id}",
            CarClass = CarClass.B,
            Tier = CarTier.GtRaceCar,
            Price = price,
            BaseStats = new CarStats
            {
                Power = 50, Handling = 50, FuelEfficiency = 50, TyreDurability = 50,
                Comfort = 50, Reliability = 50, PitStopTime = 50, FuelCapacity = 100,
            },
            PotentialStats = new CarStats
            {
                Power = 80, Handling = 80, FuelEfficiency = 80, TyreDurability = 80,
                Comfort = 80, Reliability = 80, PitStopTime = 30, FuelCapacity = 100,
            },
            UpgradePacks = new[]
            {
                new UpgradePackDefinition { Type = UpgradePackType.Power,    Cost = 10_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 8_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Comfort,  Cost = 5_000 },
            },
        };

        private static CarInstance MakeInstance(
            string modelId,
            int age = 0,
            double condition = 100,
            InstalledUpgrades? upgrades = null) => new CarInstance
        {
            Id = "c1",
            ModelId = modelId,
            Age = age,
            Condition = condition,
            InstalledUpgrades = upgrades ?? new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
        };

        private static readonly CarModel Model = MakeModel("m1", 100_000);

        /// <summary>Returns <paramref name="first"/> on call 1, then <paramref name="rest"/>.</summary>
        private static Func<double> FirstThen(double first, double rest)
        {
            var call = 0;
            return () =>
            {
                call++;
                return call == 1 ? first : rest;
            };
        }

        // ---------------------------------------------------------------------------
        // getNewCarListings
        // ---------------------------------------------------------------------------

        [Test]
        public void GetNewCarListings_ReturnsOneListingPerModel()
        {
            var models = new[] { MakeModel("a", 10_000), MakeModel("b", 20_000) };
            Assert.That(GetNewCarListings(models), Has.Count.EqualTo(2));
        }

        [Test]
        public void GetNewCarListings_EachListingHasTheModelsListPrice()
        {
            var models = new[] { MakeModel("a", 15_000) };
            Assert.That(GetNewCarListings(models)[0].Price, Is.EqualTo(15_000));
        }

        [Test]
        public void GetNewCarListings_ReturnsEmptyArrayForEmptyCatalogue()
        {
            Assert.That(GetNewCarListings(new CarModel[0]), Has.Count.EqualTo(0));
        }

        // ---------------------------------------------------------------------------
        // calculateCarMarketValue
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateCarMarketValue_NewCarIsCloseToListPrice()
        {
            var car = MakeInstance("m1", 0, 100);
            var value = CalculateCarMarketValue(car, Model);
            // ageFactor=1, condFactor = 0.5 + 0.5*1 = 1.0, so value = 100_000 * 1 * 1 = 100_000
            Assert.That(value, Is.EqualTo(100_000));
        }

        [Test]
        public void CalculateCarMarketValue_OlderCarIsWorthLessThanANewerCar()
        {
            var newer = MakeInstance("m1", 1, 100);
            var older = MakeInstance("m1", 5, 100);
            Assert.That(CalculateCarMarketValue(newer, Model), Is.GreaterThan(CalculateCarMarketValue(older, Model)));
        }

        [Test]
        public void CalculateCarMarketValue_LowerConditionReducesValue()
        {
            var good = MakeInstance("m1", 3, 90);
            var worn = MakeInstance("m1", 3, 30);
            Assert.That(CalculateCarMarketValue(good, Model), Is.GreaterThan(CalculateCarMarketValue(worn, Model)));
        }

        [Test]
        public void CalculateCarMarketValue_InstalledUpgradesAddValue()
        {
            var @base = MakeInstance("m1", 3, 80, new InstalledUpgrades { Power = false, Handling = false, Comfort = false });
            var upgraded = MakeInstance("m1", 3, 80, new InstalledUpgrades { Power = true, Handling = false, Comfort = false });
            Assert.That(CalculateCarMarketValue(upgraded, Model), Is.GreaterThan(CalculateCarMarketValue(@base, Model)));
        }

        [Test]
        public void CalculateCarMarketValue_ValueIsAlwaysPositive()
        {
            var extreme = MakeInstance("m1", 100, 0); // extremely old, 0 condition
            Assert.That(CalculateCarMarketValue(extreme, Model), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------------------
        // calculateSalePrice
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateSalePrice_SalePriceIsLessThanMarketValue()
        {
            var car = MakeInstance("m1", 2, 80);
            var market = CalculateCarMarketValue(car, Model);
            var sale = CalculateSalePrice(car, Model, 0);
            Assert.That(sale, Is.LessThan(market));
        }

        [Test]
        public void CalculateSalePrice_HigherBusinessSkillIncreasesSalePrice()
        {
            var car = MakeInstance("m1", 2, 80);
            var lowBusiness = CalculateSalePrice(car, Model, 0);
            var highBusiness = CalculateSalePrice(car, Model, 20);
            Assert.That(highBusiness, Is.GreaterThan(lowBusiness));
        }

        [Test]
        public void CalculateSalePrice_SalePriceScalesWithBusinessSkill()
        {
            var car = MakeInstance("m1", 0, 100);
            var noSkill = CalculateSalePrice(car, Model, 0);
            var maxSkill = CalculateSalePrice(car, Model, 20);
            // Max skill gives 15% bonus on top of base resale (60%)
            // vitest toBeCloseTo(1.15, 1): |diff| < 0.05
            Assert.That(maxSkill / noSkill, Is.EqualTo(1.15).Within(0.05));
        }

        [Test]
        public void CalculateSalePrice_SalePriceIsAlwaysPositive()
        {
            var car = MakeInstance("m1", 10, 10);
            Assert.That(CalculateSalePrice(car, Model, 0), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------------------
        // calculatePurchasePrice
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculatePurchasePrice_NoBusinessSkillReturnsListPriceUnchanged()
        {
            Assert.That(CalculatePurchasePrice(100_000, 0), Is.EqualTo(100_000));
        }

        [Test]
        public void CalculatePurchasePrice_MaxBusinessSkillGivesMaximumDiscount()
        {
            var discounted = CalculatePurchasePrice(100_000, 20);
            Assert.That(discounted, Is.LessThan(100_000));
            Assert.That(discounted, Is.GreaterThan(80_000)); // max 15% off
        }

        [Test]
        public void CalculatePurchasePrice_HigherBusinessSkillGivesALargerDiscount()
        {
            var low = CalculatePurchasePrice(100_000, 5);
            var high = CalculatePurchasePrice(100_000, 15);
            Assert.That(high, Is.LessThan(low));
        }

        // ---------------------------------------------------------------------------
        // generateUsedListing
        // ---------------------------------------------------------------------------

        [Test]
        public void GenerateUsedListing_AgeIsBetween1And10()
        {
            for (var i = 0; i < 20; i++)
            {
                var result = GenerateUsedListing(Model, FirstThen(0.0, 0.5)); // first call → age = 1
                Assert.That(result.Age, Is.GreaterThanOrEqualTo(1));
            }
            var maxAge = GenerateUsedListing(Model, () => 0.99);
            Assert.That(maxAge.Age, Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void GenerateUsedListing_ConditionIsBetween30And90()
        {
            // Force age (call 1), then condition (call 2)
            var low = GenerateUsedListing(Model, FirstThen(0.5, 0)); // condition call → 30
            Assert.That(low.Condition, Is.GreaterThanOrEqualTo(30));

            var high = GenerateUsedListing(Model, FirstThen(0.5, 1)); // condition call → 90
            Assert.That(high.Condition, Is.LessThanOrEqualTo(90));
        }

        [Test]
        public void GenerateUsedListing_PriceIsPositive()
        {
            var listing = GenerateUsedListing(Model, () => 0.5);
            Assert.That(listing.Price, Is.GreaterThan(0));
        }

        [Test]
        public void GenerateUsedListing_ListingPriceIsLessThanTheNewCarListPrice()
        {
            // Any used car (age ≥ 1) should be cheaper than new
            var listing = GenerateUsedListing(Model, () => 0.5);
            Assert.That(listing.Price, Is.LessThan(Model.Price));
        }

        [Test]
        public void GenerateUsedListing_ModelIdMatchesTheModel()
        {
            var listing = GenerateUsedListing(Model, () => 0.5);
            Assert.That(listing.ModelId, Is.EqualTo(Model.Id));
        }

        // ---------------------------------------------------------------------------
        // generateUsedInventory
        // ---------------------------------------------------------------------------

        private static readonly CarModel[] Models = { MakeModel("a", 50_000), MakeModel("b", 200_000) };

        [Test]
        public void GenerateUsedInventory_ReturnsTheRequestedCount()
        {
            var inventory = GenerateUsedInventory(Models, 5, () => 0.5);
            Assert.That(inventory, Has.Count.EqualTo(5));
        }

        [Test]
        public void GenerateUsedInventory_ReturnsDefaultUsedInventoryCountWhenCountIsOmitted()
        {
            var inventory = GenerateUsedInventory(Models, DefaultUsedInventoryCount, () => 0.5);
            Assert.That(inventory, Has.Count.EqualTo(DefaultUsedInventoryCount));
        }

        [Test]
        public void GenerateUsedInventory_AllListingsHaveAPositivePrice()
        {
            var inventory = GenerateUsedInventory(Models, 10, () => 0.5);
            foreach (var listing in inventory)
            {
                Assert.That(listing.Price, Is.GreaterThan(0));
            }
        }

        [Test]
        public void GenerateUsedInventory_ListingsComeFromTheProvidedModels()
        {
            var validIds = Models.Select(m => m.Id).ToHashSet();
            var rng = new Random();
            var inventory = GenerateUsedInventory(Models, 10, rng.NextDouble);
            foreach (var listing in inventory)
            {
                Assert.That(validIds.Contains(listing.ModelId), Is.True);
            }
        }
    }
}
