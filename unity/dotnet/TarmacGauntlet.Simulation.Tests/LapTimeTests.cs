#nullable enable
using System;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/lapTime.test.ts.</summary>
    public class LapTimeTests
    {
        /// <summary>Reference car: mid-field performance, gives ~30s base lap time.</summary>
        private static readonly CarStats RefStats = new CarStats
        {
            Power = 75,
            Handling = 62.5,
            FuelEfficiency = 60,
            TyreDurability = 60,
            Comfort = 50,
            Reliability = 70,
            PitStopTime = 30,
            FuelCapacity = 100,
        };

        /// <summary>Reference driver: average stats, no fatigue.</summary>
        private static readonly DriverStats RefDriver = new DriverStats
        {
            Pace = 50,
            Consistency = 50,
            Stamina = 50,
            Safety = 50,
            Smoothness = 50,
        };

        /// <summary>Zero-variance random: produces exactly the midpoint (0 variance).</summary>
        private static readonly Func<double> ZeroVariance = () => 0.5;

        /// <summary>Helper: build a clean lap time call with zero-variance random.</summary>
        private static double Lap(
            Func<CarStats, CarStats>? stats = null,
            Func<DriverStats, DriverStats>? driver = null,
            double fatigue = 0,
            InstructionMode mode = InstructionMode.Normal,
            double tyreWear = 0,
            double fuelLoad = 0)
        {
            return LapTime.CalculateLapTime(
                stats != null ? stats(RefStats) : RefStats,
                driver != null ? driver(RefDriver) : RefDriver,
                fatigue,
                mode,
                tyreWear,
                fuelLoad,
                ZeroVariance);
        }

        private static double Calc(DriverStats driver, double fatigue, InstructionMode mode, double roll) =>
            LapTime.CalculateLapTime(RefStats, driver, fatigue, mode, 0, 0, () => roll);

        // --- Baseline ---

        [Test]
        public void ReturnsALapTimeInAPlausibleRangeForTheReferenceCar()
        {
            double t = Lap();
            // Reference car should be in the neighbourhood of 30 s (GDD target).
            Assert.That(t, Is.GreaterThan(20));
            Assert.That(t, Is.LessThan(50));
        }

        [Test]
        public void ReturnsAFinitePositiveNumber()
        {
            double t = Lap();
            Assert.That(double.IsFinite(t), Is.True);
            Assert.That(t, Is.GreaterThan(0));
        }

        // --- Driver pace modifier ---

        [Test]
        public void HigherDriverPaceProducesAFasterLapTime()
        {
            double fast = Lap(driver: d => d with { Pace = 100 });
            double slow = Lap(driver: d => d with { Pace = 0 });
            Assert.That(fast, Is.LessThan(slow));
        }

        [Test]
        public void ReferencePace50ProducesATimeBetweenHighAndLowPace()
        {
            double fast = Lap(driver: d => d with { Pace = 100 });
            double reference = Lap(driver: d => d with { Pace = 50 });
            double slow = Lap(driver: d => d with { Pace = 0 });
            Assert.That(fast, Is.LessThan(reference));
            Assert.That(reference, Is.LessThan(slow));
        }

        // --- Driver fatigue ---

        [Test]
        public void HigherDriverFatigueProducesASlowerLapTime()
        {
            double fresh = Lap(fatigue: 0, driver: d => d with { Pace = 80 });
            double tired = Lap(fatigue: 80, driver: d => d with { Pace = 80 });
            Assert.That(tired, Is.GreaterThan(fresh));
        }

        // --- Instruction mode ---

        [Test]
        public void PushModeIsFasterThanNormalNormalIsFasterThanConserve()
        {
            double push = Lap(mode: InstructionMode.Push);
            double normal = Lap(mode: InstructionMode.Normal);
            double conserve = Lap(mode: InstructionMode.Conserve);
            Assert.That(push, Is.LessThan(normal));
            Assert.That(normal, Is.LessThan(conserve));
        }

        [Test]
        public void NormalModeProducesTheReferenceBaseline()
        {
            // With zero variance and Normal mode, the result should match the base × pace × tyre × fuel.
            // We can verify that Push is faster and Conserve is slower relative to Normal.
            double normal = Lap(mode: InstructionMode.Normal);
            double push = Lap(mode: InstructionMode.Push);
            double conserve = Lap(mode: InstructionMode.Conserve);
            Assert.That(push, Is.LessThan(normal));
            Assert.That(conserve, Is.GreaterThan(normal));
        }

        // --- Tyre wear modifier ---

        [Test]
        public void FreshTyresAreFasterThanWornTyres()
        {
            double fresh = Lap(tyreWear: 0);
            double worn = Lap(tyreWear: 100);
            Assert.That(fresh, Is.LessThan(worn));
        }

        [Test]
        public void TyreWearIncreasesLapTimeProgressively()
        {
            double t25 = Lap(tyreWear: 25);
            double t50 = Lap(tyreWear: 50);
            double t100 = Lap(tyreWear: 100);
            Assert.That(t25, Is.LessThan(t50));
            Assert.That(t50, Is.LessThan(t100));
        }

        // --- Fuel load modifier ---

        [Test]
        public void HigherFuelLoadProducesASlowerLapTime()
        {
            double light = Lap(fuelLoad: 0);
            double heavy = Lap(fuelLoad: 100);
            Assert.That(light, Is.LessThan(heavy));
        }

        [Test]
        public void FuelLoadIncreasesLapTimeProportionallyToLoad()
        {
            double t20 = Lap(fuelLoad: 20);
            double t60 = Lap(fuelLoad: 60);
            double t100 = Lap(fuelLoad: 100);
            Assert.That(t20, Is.LessThan(t60));
            Assert.That(t60, Is.LessThan(t100));
        }

        // --- Car stats ---

        [Test]
        public void AFasterCarProducesAShorterBaseLapTime()
        {
            double fast = Lap(stats: s => s with { Power = 95, Handling = 90 });
            double slow = Lap(stats: s => s with { Power = 30, Handling = 25 });
            Assert.That(fast, Is.LessThan(slow));
        }

        // --- Random variance ---

        [Test]
        public void ZeroVarianceReturnsADeterministicResult()
        {
            double a = LapTime.CalculateLapTime(RefStats, RefDriver, 0, InstructionMode.Normal, 0, 0, () => 0.5);
            double b = LapTime.CalculateLapTime(RefStats, RefDriver, 0, InstructionMode.Normal, 0, 0, () => 0.5);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void LowConsistencyDriverShowsLargerSpreadThanHighConsistencyDriver()
        {
            var highConsistency = RefDriver with { Consistency = 100 };
            var lowConsistency = RefDriver with { Consistency = 0 };

            // At consistency=100, variance range = 0 regardless of random value.
            double highLow = Calc(highConsistency, 0, InstructionMode.Normal, 0);
            double highHigh = Calc(highConsistency, 0, InstructionMode.Normal, 1);
            Assert.That(highLow, Is.EqualTo(highHigh).Within(5e-6)); // No variance at perfect consistency

            double lowLow = Calc(lowConsistency, 0, InstructionMode.Normal, 0);
            double lowHigh = Calc(lowConsistency, 0, InstructionMode.Normal, 1);
            Assert.That(lowHigh - lowLow, Is.GreaterThan(highHigh - highLow)); // Larger spread
        }

        [Test]
        public void PushModeAmplifiesVarianceVsNormalConserveReducesIt()
        {
            var lowConsistency = RefDriver with { Consistency = 0 };

            double pushSpread = Calc(lowConsistency, 0, InstructionMode.Push, 1) - Calc(lowConsistency, 0, InstructionMode.Push, 0);
            double normalSpread = Calc(lowConsistency, 0, InstructionMode.Normal, 1) - Calc(lowConsistency, 0, InstructionMode.Normal, 0);
            double conserveSpread = Calc(lowConsistency, 0, InstructionMode.Conserve, 1) - Calc(lowConsistency, 0, InstructionMode.Conserve, 0);

            Assert.That(pushSpread, Is.GreaterThan(normalSpread));
            Assert.That(normalSpread, Is.GreaterThan(conserveSpread));
        }

        [Test]
        public void FatigueIncreasesVarianceByReducingEffectiveConsistency()
        {
            var driver = RefDriver with { Consistency = 80 };
            double freshLow = Calc(driver, 0, InstructionMode.Normal, 0);
            double freshHigh = Calc(driver, 0, InstructionMode.Normal, 1);
            double tiredLow = Calc(driver, 100, InstructionMode.Normal, 0);
            double tiredHigh = Calc(driver, 100, InstructionMode.Normal, 1);

            Assert.That(tiredHigh - tiredLow, Is.GreaterThan(freshHigh - freshLow));
        }

        // --- All modifiers combined ---

        [Test]
        public void AllDegradationFactorsCompoundCorrectly()
        {
            double degraded = Lap(
                stats: s => s with { Power = 50, Handling = 40 },
                driver: d => d with { Pace = 30, Consistency = 20 },
                fatigue: 60,
                mode: InstructionMode.Conserve,
                tyreWear: 80,
                fuelLoad: 80);
            double baseline = Lap(); // reference car, fresh everything
            Assert.That(degraded, Is.GreaterThan(baseline));
        }

        // --- Edge cases ---

        [Test]
        public void ZeroFuelLoadAppliesNoFuelPenalty()
        {
            double noFuel = Lap(fuelLoad: 0);
            double someFuel = Lap(fuelLoad: 50);
            Assert.That(noFuel, Is.LessThan(someFuel));
        }

        [Test]
        public void ZeroTyreWearAppliesNoTyrePenalty()
        {
            double fresh = Lap(tyreWear: 0);
            double worn = Lap(tyreWear: 1);
            Assert.That(fresh, Is.LessThan(worn));
        }

        [Test]
        public void FatigueLapFactorIsOneWhenFresh()
        {
            Assert.That(LapTime.FatigueLapFactor(80, 0), Is.EqualTo(1).Within(1e-12));
        }

        [Test]
        public void FatigueLapFactorMatchesTheLapTimeFormula()
        {
            var stats = new DriverStats { Pace = 80, Consistency = 100, Stamina = 50, Safety = 50, Smoothness = 50 };
            CarStats car = new CarStats { Power = 50, Handling = 50 };
            double fresh = LapTime.CalculateLapTime(car, stats, 0, InstructionMode.Normal, 0, 0, () => 0.5);
            double tired = LapTime.CalculateLapTime(car, stats, 100, InstructionMode.Normal, 0, 0, () => 0.5);
            Assert.That(tired / fresh, Is.EqualTo(LapTime.FatigueLapFactor(80, 100)).Within(1e-12));
            Assert.That(LapTime.FatigueLapFactor(80, 100), Is.GreaterThan(1.04));
        }
    }
}
