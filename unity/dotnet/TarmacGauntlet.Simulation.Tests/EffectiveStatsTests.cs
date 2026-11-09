#nullable enable
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/effectiveStats.test.ts.</summary>
    public class EffectiveStatsTests
    {
        // TS toBeCloseTo default precision (2 digits).
        private const double Close = 0.005;

        // A model whose baseStats and potentialStats are easy to reason about.
        private static readonly CarModel Model = new CarModel
        {
            Id = "test-model",
            Name = "Test Car",
            CarClass = CarClass.C,
            Tier = CarTier.TrackCar,
            Price = 10000,
            BaseStats = new CarStats
            {
                Power = 100,
                Handling = 80,
                FuelEfficiency = 60,
                TyreDurability = 70,
                Comfort = 50,
                Reliability = 90,
                PitStopTime = 40, // lower is better
                FuelCapacity = 120,
            },
            PotentialStats = new CarStats
            {
                Power = 120,
                Handling = 100,
                FuelEfficiency = 75,
                TyreDurability = 90,
                Comfort = 70,
                Reliability = 90,  // no pack, equals base
                PitStopTime = 40,  // no pack, equals base
                FuelCapacity = 120, // no pack, equals base
            },
            UpgradePacks = new[]
            {
                new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = 5000 },
                new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 4000 },
                new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = 3000 },
            },
        };

        private static readonly CarInstance BaseInstance = new CarInstance
        {
            Id = "car-1",
            ModelId = "test-model",
            Age = 0,
            Condition = 100,
            InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
        };

        private static CarStats Calc(CarInstance instance) => EffectiveStats.CalculateEffectiveStats(instance, Model);

        private static InstalledUpgrades Upgrades(bool power, bool handling, bool comfort) =>
            new InstalledUpgrades { Power = power, Handling = handling, Comfort = comfort };

        // --- Base stats only ---

        [Test]
        public void ReturnsBaseStatsAtAge0FullConditionNoUpgrades()
        {
            var result = Calc(BaseInstance);
            Assert.That(result.Power, Is.EqualTo(100));
            Assert.That(result.Handling, Is.EqualTo(80));
            Assert.That(result.FuelEfficiency, Is.EqualTo(60));
            Assert.That(result.TyreDurability, Is.EqualTo(70));
            Assert.That(result.Comfort, Is.EqualTo(50));
            Assert.That(result.Reliability, Is.EqualTo(90));
            Assert.That(result.PitStopTime, Is.EqualTo(40)); // 40 / 1.0
            Assert.That(result.FuelCapacity, Is.EqualTo(120));
        }

        // --- Upgrades ---

        [Test]
        public void AppliesPowerPackUpgradesPowerAndFuelEfficiency()
        {
            var result = Calc(BaseInstance with { InstalledUpgrades = Upgrades(true, false, false) });
            Assert.That(result.Power, Is.EqualTo(120));
            Assert.That(result.FuelEfficiency, Is.EqualTo(75));
            // Unaffected stats remain at base
            Assert.That(result.Handling, Is.EqualTo(80));
            Assert.That(result.TyreDurability, Is.EqualTo(70));
            Assert.That(result.Comfort, Is.EqualTo(50));
        }

        [Test]
        public void AppliesHandlingPackUpgradesHandlingAndTyreDurability()
        {
            var result = Calc(BaseInstance with { InstalledUpgrades = Upgrades(false, true, false) });
            Assert.That(result.Handling, Is.EqualTo(100));
            Assert.That(result.TyreDurability, Is.EqualTo(90));
            Assert.That(result.Power, Is.EqualTo(100)); // unchanged
        }

        [Test]
        public void AppliesComfortPackUpgradesComfortOnly()
        {
            var result = Calc(BaseInstance with { InstalledUpgrades = Upgrades(false, false, true) });
            Assert.That(result.Comfort, Is.EqualTo(70));
            Assert.That(result.Power, Is.EqualTo(100)); // unchanged
        }

        [Test]
        public void AllUpgradesInstalledAllUpgradeableStatsUsePotentialValues()
        {
            var result = Calc(BaseInstance with { InstalledUpgrades = Upgrades(true, true, true) });
            Assert.That(result.Power, Is.EqualTo(120));
            Assert.That(result.FuelEfficiency, Is.EqualTo(75));
            Assert.That(result.Handling, Is.EqualTo(100));
            Assert.That(result.TyreDurability, Is.EqualTo(90));
            Assert.That(result.Comfort, Is.EqualTo(70));
            // No-pack stats unchanged
            Assert.That(result.Reliability, Is.EqualTo(90));
            Assert.That(result.PitStopTime, Is.EqualTo(40));
            Assert.That(result.FuelCapacity, Is.EqualTo(120));
        }

        // --- Age ---

        [Test]
        public void AppliesA2PercentPenaltyPerYearOfAge()
        {
            // age 5 → agePenalty = 0.10 → ageFraction = 0.90; condition = 100% → combined = 0.90
            var result = Calc(BaseInstance with { Age = 5 });
            Assert.That(result.Power, Is.EqualTo(100 * 0.9).Within(Close));
            Assert.That(result.Handling, Is.EqualTo(80 * 0.9).Within(Close));
            // pitStopTime: 40 / 0.90
            Assert.That(result.PitStopTime, Is.EqualTo(40 / 0.9).Within(Close));
        }

        [Test]
        public void CapsAgePenaltyAt50PercentAge25Plus()
        {
            var result25 = Calc(BaseInstance with { Age = 25 });
            var result50 = Calc(BaseInstance with { Age = 50 });
            // At age 25, penalty is exactly 50%; any older should give the same result.
            Assert.That(result25.Power, Is.EqualTo(result50.Power).Within(Close));
            Assert.That(result25.Power, Is.EqualTo(100 * 0.5).Within(Close));
        }

        [Test]
        public void Age0ProducesNoAgePenalty()
        {
            var result = Calc(BaseInstance with { Age = 0 });
            Assert.That(result.Power, Is.EqualTo(100));
        }

        // --- Condition ---

        [Test]
        public void ConditionActsAsADirectMultiplierOnNormalStats()
        {
            // condition 50% → multiplier = 0.50
            var result = Calc(BaseInstance with { Condition = 50 });
            Assert.That(result.Power, Is.EqualTo(100 * 0.5).Within(Close));
            Assert.That(result.Reliability, Is.EqualTo(90 * 0.5).Within(Close));
        }

        [Test]
        public void LowerConditionIncreasesPitStopTime()
        {
            var full = Calc(BaseInstance with { Condition = 100 });
            var degraded = Calc(BaseInstance with { Condition = 50 });
            Assert.That(degraded.PitStopTime, Is.GreaterThan(full.PitStopTime));
            Assert.That(degraded.PitStopTime, Is.EqualTo(40 / 0.5).Within(Close));
        }

        // --- Edge cases ---

        [Test]
        public void ZeroConditionAllNormalStatsAre0()
        {
            var result = Calc(BaseInstance with { Condition = 0 });
            Assert.That(result.Power, Is.EqualTo(0));
            Assert.That(result.Handling, Is.EqualTo(0));
            Assert.That(result.FuelEfficiency, Is.EqualTo(0));
            Assert.That(result.TyreDurability, Is.EqualTo(0));
            Assert.That(result.Comfort, Is.EqualTo(0));
            Assert.That(result.Reliability, Is.EqualTo(0));
            Assert.That(result.FuelCapacity, Is.EqualTo(0));
        }

        [Test]
        public void ZeroConditionPitStopTimeIsCappedDoesNotReachInfinity()
        {
            var result = Calc(BaseInstance with { Condition = 0 });
            // combined = 0, clamped to MinCombinedMultiplier = 0.1 → pitStopTime = 40 / 0.1 = 400
            Assert.That(result.PitStopTime, Is.EqualTo(40 / 0.1).Within(Close));
            Assert.That(double.IsFinite(result.PitStopTime), Is.True);
        }

        // --- All factors combined ---

        [Test]
        public void CombinesUpgradesAgeAndConditionCorrectly()
        {
            // age 10 → agePenalty = 0.20 → ageFraction = 0.80
            // condition 75% → conditionFraction = 0.75
            // combined = 0.80 * 0.75 = 0.60
            var instance = BaseInstance with
            {
                Age = 10,
                Condition = 75,
                InstalledUpgrades = Upgrades(true, true, true),
            };
            var result = Calc(instance);
            const double combined = 0.8 * 0.75;

            Assert.That(result.Power, Is.EqualTo(120 * combined).Within(Close));
            Assert.That(result.FuelEfficiency, Is.EqualTo(75 * combined).Within(Close));
            Assert.That(result.Handling, Is.EqualTo(100 * combined).Within(Close));
            Assert.That(result.TyreDurability, Is.EqualTo(90 * combined).Within(Close));
            Assert.That(result.Comfort, Is.EqualTo(70 * combined).Within(Close));
            Assert.That(result.Reliability, Is.EqualTo(90 * combined).Within(Close));
            Assert.That(result.PitStopTime, Is.EqualTo(40 / combined).Within(Close));
            Assert.That(result.FuelCapacity, Is.EqualTo(120 * combined).Within(Close));
        }
    }
}
