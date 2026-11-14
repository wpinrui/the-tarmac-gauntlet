#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.DriverLifecycle;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/driverLifecycle.test.ts.</summary>
    public class DriverLifecycleTests
    {
        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        /// <summary>A driver with a symmetric curve (no phase offsets) peaking at age 30 with all stats = 80.</summary>
        private static Driver MakeDriver(string id, int age, double peakStats = 80, double peakAge = 30)
        {
            var peak = Uniform(peakStats);
            var zero = Uniform(0);
            return new Driver
            {
                Id = id,
                Name = $"Driver {id}",
                Nationality = "gb",
                Age = age,
                CurveParams = new DriverCurveParams { PeakAge = peakAge, PeakStats = peak, PhaseOffsets = zero },
                MarketValue = 0,
            };
        }

        private static DriverStats Uniform(double v) =>
            new DriverStats { Pace = v, Consistency = v, Stamina = v, Safety = v, Smoothness = v };

        private static DriverStats Stats(double pace, double consistency, double stamina, double safety, double smoothness) =>
            new DriverStats { Pace = pace, Consistency = consistency, Stamina = stamina, Safety = safety, Smoothness = smoothness };

        private static double NeverFire() => 0;

        private static List<RookieSpec> FifteenRookieSpecs() =>
            Enumerable.Range(0, 15).Select(i => new RookieSpec { Id = $"r{i}", Name = $"R{i}", Nationality = "gb" }).ToList();

        // ---------------------------------------------------------------------------
        // calculateDriverStats: stat curve
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateDriverStats_StatEqualsPeakStatAtPeakAge_NoPhaseOffset()
        {
            var driver = MakeDriver("d1", 30, 80);
            var stats = CalculateDriverStats(driver);
            Assert.That(stats.Pace, Is.EqualTo(80));
            Assert.That(stats.Consistency, Is.EqualTo(80));
        }

        [Test]
        public void CalculateDriverStats_StatCurvePeaksAroundAge30()
        {
            var atPeak = CalculateDriverStats(MakeDriver("d", 30));
            var before = CalculateDriverStats(MakeDriver("d", 25));
            var after = CalculateDriverStats(MakeDriver("d", 35));
            Assert.That(atPeak.Pace, Is.GreaterThan(before.Pace));
            Assert.That(atPeak.Pace, Is.GreaterThan(after.Pace));
        }

        [Test]
        public void CalculateDriverStats_StatsAreLowerAtAge18ThanAtPeak()
        {
            var young = CalculateDriverStats(MakeDriver("d", 18));
            var peak = CalculateDriverStats(MakeDriver("d", 30));
            Assert.That(young.Pace, Is.LessThan(peak.Pace));
        }

        [Test]
        public void CalculateDriverStats_StatsAreLowerAtAge45ThanAtPeak()
        {
            var old = CalculateDriverStats(MakeDriver("d", 45));
            var peak = CalculateDriverStats(MakeDriver("d", 30));
            Assert.That(old.Pace, Is.LessThan(peak.Pace));
        }

        [Test]
        public void CalculateDriverStats_StatsNeverGoBelowZeroOrAbove100()
        {
            foreach (var age in new[] { 10, 18, 25, 30, 40, 50, 60 })
            {
                var stats = CalculateDriverStats(MakeDriver("d", age));
                foreach (var key in DriverData.DriverStatKeys)
                {
                    Assert.That(stats.Get(key), Is.GreaterThanOrEqualTo(0));
                    Assert.That(stats.Get(key), Is.LessThanOrEqualTo(100));
                }
            }
        }

        [Test]
        public void CalculateDriverStats_PhaseOffsetsShiftTheIndividualStatPeak()
        {
            var zeroPeak = Uniform(80);
            var noOffset = new Driver
            {
                Id = "a",
                Name = "A",
                Nationality = "gb",
                Age = 30,
                CurveParams = new DriverCurveParams
                {
                    PeakAge = 30,
                    PeakStats = zeroPeak,
                    PhaseOffsets = Stats(0, 0, 0, 0, 0),
                },
                MarketValue = 0,
            };
            var withOffset = noOffset with
            {
                Id = "b",
                // pace offset of +3: pace peaks at age 33 instead of 30
                CurveParams = noOffset.CurveParams with { PhaseOffsets = Stats(3, 0, 0, 0, 0) },
            };
            var statsA = CalculateDriverStats(noOffset);
            var statsB = CalculateDriverStats(withOffset);
            // At age 30: driver A is at peak pace, driver B is slightly before peak (age 30 < 33)
            Assert.That(statsA.Pace, Is.GreaterThan(statsB.Pace));
            // consistency is unchanged (both have 0 offset)
            Assert.That(statsA.Consistency, Is.EqualTo(statsB.Consistency));
        }

        [Test]
        public void CalculateDriverStats_TwoSameAgeDriversWithDifferentPhaseOffsetsHaveDifferentStats()
        {
            var b = Uniform(80);
            var driverA = new Driver
            {
                Id = "a", Name = "A", Nationality = "gb", Age = 28,
                CurveParams = new DriverCurveParams { PeakAge = 30, PeakStats = b, PhaseOffsets = Stats(-1, 2, 0, -2, 1) },
                MarketValue = 0,
            };
            var driverB = new Driver
            {
                Id = "b", Name = "B", Nationality = "gb", Age = 28,
                CurveParams = new DriverCurveParams { PeakAge = 30, PeakStats = b, PhaseOffsets = Stats(2, -1, 1, 1, -2) },
                MarketValue = 0,
            };
            var statsA = CalculateDriverStats(driverA);
            var statsB = CalculateDriverStats(driverB);
            // At least one stat should differ between the two drivers at the same age
            var anyDiff = DriverData.DriverStatKeys.Any(k => statsA.Get(k) != statsB.Get(k));
            Assert.That(anyDiff, Is.True);
        }

        // ---------------------------------------------------------------------------
        // calculateMarketValue
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateMarketValue_MarketValueCorrelatesWithTotalStats()
        {
            Assert.That(CalculateMarketValue(Uniform(90)), Is.GreaterThan(CalculateMarketValue(Uniform(20))));
        }

        [Test]
        public void CalculateMarketValue_MarketValueIsPositiveEvenForVeryLowStats()
        {
            Assert.That(CalculateMarketValue(Uniform(0)), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------------------
        // calculateAnnualSalary
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateAnnualSalary_SalaryIsWithinTheGddSpecifiedRange()
        {
            Assert.That(CalculateAnnualSalary(Uniform(0)), Is.GreaterThanOrEqualTo(500));
            Assert.That(CalculateAnnualSalary(Uniform(100)), Is.LessThanOrEqualTo(150_000));
        }

        [Test]
        public void CalculateAnnualSalary_HigherStatsEarnHigherSalary()
        {
            Assert.That(CalculateAnnualSalary(Uniform(90)), Is.GreaterThan(CalculateAnnualSalary(Uniform(20))));
        }

        // ---------------------------------------------------------------------------
        // calculateContractSalary
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateContractSalary_OneYearContractWithNoBusinessSkillAppliesNoDiscount()
        {
            Assert.That(CalculateContractSalary(10_000, 1, 0), Is.EqualTo(10_000));
        }

        [Test]
        public void CalculateContractSalary_TwoYearContractApplies10PercentDiscount()
        {
            Assert.That(CalculateContractSalary(10_000, 2, 0), Is.EqualTo(9_000));
        }

        [Test]
        public void CalculateContractSalary_ThreeYearContractApplies20PercentDiscount()
        {
            Assert.That(CalculateContractSalary(10_000, 3, 0), Is.EqualTo(8_000));
        }

        [Test]
        public void CalculateContractSalary_BusinessSkillProvidesAdditionalDiscount()
        {
            var withBusiness = CalculateContractSalary(10_000, 1, 20); // max business = 10% extra
            var withoutBusiness = CalculateContractSalary(10_000, 1, 0);
            Assert.That(withBusiness, Is.LessThan(withoutBusiness));
        }

        [Test]
        public void CalculateContractSalary_ThreeYearPlusMaxBusinessSkillGivesTheLargestDiscount()
        {
            var best = CalculateContractSalary(10_000, 3, 20);
            var worst = CalculateContractSalary(10_000, 1, 0);
            Assert.That(best, Is.LessThan(worst));
        }

        [Test]
        public void CalculateContractSalary_DiscountedSalaryIsAlwaysPositive()
        {
            Assert.That(CalculateContractSalary(500, 3, 20), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------------------
        // calculateBuyoutCost
        // ---------------------------------------------------------------------------

        [Test]
        public void CalculateBuyoutCost_EqualsAnnualSalaryTimesRemainingYearsTimes1Point5()
        {
            var contract = new Contract { DriverId = "d1", TeamId = "t1", Length = 3, RemainingYears = 2, AnnualSalary = 10_000 };
            Assert.That(CalculateBuyoutCost(contract), Is.EqualTo(30_000)); // 10_000 × 2 × 1.5
        }

        [Test]
        public void CalculateBuyoutCost_With1RemainingYear()
        {
            var contract = new Contract { DriverId = "d1", TeamId = "t1", Length = 2, RemainingYears = 1, AnnualSalary = 20_000 };
            Assert.That(CalculateBuyoutCost(contract), Is.EqualTo(30_000)); // 20_000 × 1 × 1.5
        }

        // ---------------------------------------------------------------------------
        // isDriverFreeAgent
        // ---------------------------------------------------------------------------

        [Test]
        public void IsDriverFreeAgent_DriverWithNoContractIsAFreeAgent()
        {
            Assert.That(IsDriverFreeAgent("d1", new Contract[0]), Is.True);
        }

        [Test]
        public void IsDriverFreeAgent_DriverWithActiveContractIsNotAFreeAgent()
        {
            var contracts = new[] { new Contract { DriverId = "d1", TeamId = "t1", Length = 2, RemainingYears = 1, AnnualSalary = 5_000 } };
            Assert.That(IsDriverFreeAgent("d1", contracts), Is.False);
        }

        [Test]
        public void IsDriverFreeAgent_DriverWithExpiredContractIsAFreeAgent()
        {
            var contracts = new[] { new Contract { DriverId = "d1", TeamId = "t1", Length = 1, RemainingYears = 0, AnnualSalary = 5_000 } };
            Assert.That(IsDriverFreeAgent("d1", contracts), Is.True);
        }

        [Test]
        public void IsDriverFreeAgent_OtherDriversContractsDoNotAffectFreeAgentStatus()
        {
            var contracts = new[] { new Contract { DriverId = "d2", TeamId = "t1", Length = 2, RemainingYears = 2, AnnualSalary = 5_000 } };
            Assert.That(IsDriverFreeAgent("d1", contracts), Is.True);
        }

        // ---------------------------------------------------------------------------
        // generateRookie
        // ---------------------------------------------------------------------------

        [Test]
        public void GenerateRookie_RookieIsAlwaysAge18()
        {
            var r = GenerateRookie(new RookieSpec { Id = "r1", Name = "Rex", Nationality = "gb" }, NeverFire);
            Assert.That(r.Age, Is.EqualTo(18));
        }

        [Test]
        public void GenerateRookie_RookieHasLowStats()
        {
            var r = GenerateRookie(new RookieSpec { Id = "r1", Name = "Rex", Nationality = "gb" }, () => 0.5);
            var stats = CalculateDriverStats(r);
            // At age 18 the stat should be substantially below peak
            var total = TotalDriverStats(stats);
            var peakTotal = TotalDriverStats(r.CurveParams.PeakStats);
            Assert.That(total, Is.LessThan(peakTotal * 0.6)); // at 18, under 60% of peak
        }

        [Test]
        public void GenerateRookie_RookieIdAndNameAreSetFromSpec()
        {
            var r = GenerateRookie(new RookieSpec { Id = "rookie-99", Name = "Sam Speed", Nationality = "us" }, NeverFire);
            Assert.That(r.Id, Is.EqualTo("rookie-99"));
            Assert.That(r.Name, Is.EqualTo("Sam Speed"));
        }

        [Test]
        public void GenerateRookie_RookieMarketValueIsPositive()
        {
            var r = GenerateRookie(new RookieSpec { Id = "r1", Name = "Rex", Nationality = "gb" }, () => 0.5);
            Assert.That(r.MarketValue, Is.GreaterThan(0));
        }

        [Test]
        public void GenerateRookie_DifferentRandomValuesProduceDifferentRookies()
        {
            var r1 = GenerateRookie(new RookieSpec { Id = "r1", Name = "A", Nationality = "gb" }, () => 0.1);
            var r2 = GenerateRookie(new RookieSpec { Id = "r2", Name = "B", Nationality = "jp" }, () => 0.9);
            var stats1 = CalculateDriverStats(r1);
            var stats2 = CalculateDriverStats(r2);
            Assert.That(TotalDriverStats(stats1), Is.Not.EqualTo(TotalDriverStats(stats2)));
        }

        // ---------------------------------------------------------------------------
        // advanceDriverYear
        // ---------------------------------------------------------------------------

        [Test]
        public void AdvanceDriverYear_AllDriversAgeByExactly1Year()
        {
            var drivers = new[] { MakeDriver("d1", 25), MakeDriver("d2", 30), MakeDriver("d3", 40) };
            var result = AdvanceDriverYear(drivers, FifteenRookieSpecs(), () => 0.5);
            var survivors = result.Drivers.Where(d => !result.Rookies.Any(r => ReferenceEquals(r, d)));
            foreach (var d in survivors)
            {
                var original = drivers.First(o => o.Id == d.Id);
                Assert.That(d.Age, Is.EqualTo(original.Age + 1));
            }
        }

        [Test]
        public void AdvanceDriverYear_Retires15Drivers_TheLowestStatOnes()
        {
            // Create 20 drivers: 15 with very low peak stats and 5 with high peak stats
            var weak = Enumerable.Range(0, 15).Select(i => MakeDriver($"weak-{i}", 30, 10)); // total stats ≈ 10×5 = 50 each
            var strong = Enumerable.Range(0, 5).Select(i => MakeDriver($"strong-{i}", 30, 90)).ToList(); // total stats ≈ 90×5 = 450 each
            var drivers = weak.Concat(strong).ToList();

            var result = AdvanceDriverYear(drivers, FifteenRookieSpecs(), () => 0.5);

            Assert.That(result.RetiredIds, Has.Count.EqualTo(15));
            // All retired should be the weak drivers
            foreach (var id in result.RetiredIds)
            {
                Assert.That(id, Does.Match("^weak-"));
            }
            // All strong drivers should survive
            foreach (var s in strong)
            {
                Assert.That(result.RetiredIds, Does.Not.Contain(s.Id));
            }
        }

        [Test]
        public void AdvanceDriverYear_Adds15NewRookies()
        {
            var drivers = Enumerable.Range(0, 20).Select(i => MakeDriver($"d{i}", 30)).ToList();
            var result = AdvanceDriverYear(drivers, FifteenRookieSpecs(), () => 0.5);
            Assert.That(result.Rookies, Has.Count.EqualTo(15));
        }

        [Test]
        public void AdvanceDriverYear_NewPoolSizeEqualsOriginalSize()
        {
            var drivers = Enumerable.Range(0, 310).Select(i => MakeDriver($"d{i}", 30)).ToList();
            var result = AdvanceDriverYear(drivers, FifteenRookieSpecs(), () => 0.5);
            Assert.That(result.Drivers, Has.Count.EqualTo(310));
        }

        [Test]
        public void AdvanceDriverYear_RookiesAreAlwaysAge18()
        {
            var drivers = Enumerable.Range(0, 20).Select(i => MakeDriver($"d{i}", 30)).ToList();
            var result = AdvanceDriverYear(drivers, FifteenRookieSpecs(), () => 0.5);
            foreach (var r in result.Rookies)
            {
                Assert.That(r.Age, Is.EqualTo(18));
            }
        }

        [Test]
        public void AdvanceDriverYear_MarketValueIsUpdatedOnAgingDrivers()
        {
            // A driver at age 30 (peak) should have higher market value than the same driver at 45
            var atPeak = MakeDriver("d1", 29); // will be 30 after advancement
            var old = MakeDriver("d2", 44);    // will be 45 after advancement
            // Give both high peakStats so the difference is visible
            var highPeak = Uniform(90);
            var withPeak = atPeak with { CurveParams = atPeak.CurveParams with { PeakStats = highPeak } };
            var withOld = old with { CurveParams = old.CurveParams with { PeakStats = highPeak } };

            // Need 15 low-stat fillers so d1 and d2 (high stats) survive retirement
            var fillers = Enumerable.Range(0, 15).Select(i => MakeDriver($"filler{i}", 50, 10));
            var pool = new[] { withPeak, withOld }.Concat(fillers).ToList();
            var result = AdvanceDriverYear(pool, FifteenRookieSpecs(), () => 0.5);

            var peakResult = result.Drivers.First(d => d.Id == "d1");
            var oldResult = result.Drivers.First(d => d.Id == "d2");

            // d1 is now at peak (30), d2 is post-peak (45): peak should be worth more
            Assert.That(peakResult.MarketValue, Is.GreaterThan(oldResult.MarketValue));
        }

        [Test]
        public void TeamAppeal_StatusLiftsTheTopOfAClassPastTheBottomOfTheNext()
        {
            var topFactoryB = new AITeam { Prestige = 80, Status = TeamStatus.Factory };
            var worstA = new AITeam { Prestige = 80 };
            Assert.That(DriverLifecycle.TeamAppeal(topFactoryB), Is.GreaterThan(DriverLifecycle.TeamAppeal(worstA)));
        }

        [Test]
        public void TeamAppeal_StatusDoesNotCarryAMidfieldTeamUpAClass()
        {
            var midFactoryB = new AITeam { Prestige = 72, Status = TeamStatus.Factory };
            var worstA = new AITeam { Prestige = 80 };
            Assert.That(DriverLifecycle.TeamAppeal(midFactoryB), Is.LessThan(DriverLifecycle.TeamAppeal(worstA)));
        }

        [Test]
        public void TeamAppeal_FactoryBeatsSatelliteBeatsPrivateerAtEqualPrestige()
        {
            double Appeal(TeamStatus s) => DriverLifecycle.TeamAppeal(new AITeam { Prestige = 50, Status = s });
            Assert.That(Appeal(TeamStatus.Factory), Is.GreaterThan(Appeal(TeamStatus.Satellite)));
            Assert.That(Appeal(TeamStatus.Satellite), Is.GreaterThan(Appeal(TeamStatus.Privateer)));
        }
    }
}
