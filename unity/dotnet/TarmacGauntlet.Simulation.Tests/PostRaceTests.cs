#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    // Port of simulation/postRace.test.ts.

    // -------------------------------------------------------------------------
    // buildPrizeSchedule
    // -------------------------------------------------------------------------

    public class BuildPrizeScheduleTests
    {
        [Test]
        public void ReturnsExactlyNumCarsEntries()
        {
            Assert.That(PostRace.BuildPrizeSchedule(100), Has.Count.EqualTo(100));
            Assert.That(PostRace.BuildPrizeSchedule(10), Has.Count.EqualTo(10));
        }

        [Test]
        public void PositionsAre1IndexedAndContiguous()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            for (int idx = 0; idx < schedule.Count; idx++)
            {
                Assert.That(schedule[idx].Position, Is.EqualTo(idx + 1));
            }
        }

        [Test]
        public void WinnerP1Receives750000()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            Assert.That(schedule[0].Amount, Is.EqualTo(750_000));
        }

        [Test]
        public void LastPlaceP100ReceivesApproximatelyTheMinimumPrize()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            Assert.That(schedule[99].Amount, Is.GreaterThanOrEqualTo(400));
            Assert.That(schedule[99].Amount, Is.LessThanOrEqualTo(1_000));
        }

        [Test]
        public void PrizesAreNonIncreasingFromP1ToP100()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            for (int i = 1; i < schedule.Count; i++)
            {
                Assert.That(schedule[i - 1].Amount, Is.GreaterThanOrEqualTo(schedule[i].Amount));
            }
        }

        [Test]
        public void TotalPrizePoolIsExactly15000000()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            var total = schedule.Sum(e => e.Amount);
            Assert.That(total, Is.EqualTo(15_000_000));
        }

        [Test]
        public void AllAmountsArePositiveIntegers()
        {
            var schedule = PostRace.BuildPrizeSchedule(100);
            foreach (var entry in schedule)
            {
                Assert.That(entry.Amount, Is.GreaterThan(0));
                Assert.That(entry.Amount, Is.EqualTo(Math.Floor(entry.Amount)));
            }
        }

        [Test]
        public void SingleCarFieldReturnsOneEntryWithMaxPrizeNoDivisionByZero()
        {
            var schedule = PostRace.BuildPrizeSchedule(1);
            Assert.That(schedule, Has.Count.EqualTo(1));
            Assert.That(schedule[0].Position, Is.EqualTo(1));
            Assert.That(double.IsFinite(schedule[0].Amount), Is.True);
            Assert.That(schedule[0].Amount, Is.GreaterThan(0));
        }
    }

    // -------------------------------------------------------------------------
    // distributePrizeMoney
    // -------------------------------------------------------------------------

    public class DistributePrizeMoneyTests
    {
        private readonly List<PrizeScheduleEntry> schedule = PostRace.BuildPrizeSchedule(100);

        private static PrizeEligibility R(string teamId, int position, int lapsCompleted) =>
            new PrizeEligibility { TeamId = teamId, Position = position, LapsCompleted = lapsCompleted };

        [Test]
        public void WinnerReceivesTheMaximumPrize()
        {
            var results = new[] { R("winner", 1, 48), R("last", 100, 1) };
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            Assert.That(prizes["winner"], Is.EqualTo(schedule[0].Amount));
        }

        [Test]
        public void LastPlaceReceivesTheMinimumPrizeAtLeast1Lap()
        {
            var results = new[] { R("last", 100, 1) };
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            Assert.That(prizes["last"], Is.EqualTo(schedule[99].Amount));
        }

        [Test]
        public void CarWith0LapsCompletedReceivesNothing()
        {
            var results = new[] { R("dnf", 99, 0), R("other", 1, 48) };
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            Assert.That(prizes["dnf"], Is.EqualTo(0));
            Assert.That(prizes["other"], Is.GreaterThan(0));
        }

        [Test]
        public void All100CarsGetANonNegativePrize()
        {
            // 0-lap cars get $0, others get > $0
            var results = Enumerable.Range(0, 100)
                .Select(i => R($"team-{i + 1}", i + 1, i == 99 ? 0 : 48)) // last car completed 0 laps
                .ToList();
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            for (int i = 0; i < 99; i++)
            {
                Assert.That(prizes[$"team-{i + 1}"], Is.GreaterThan(0));
            }
            Assert.That(prizes["team-100"], Is.EqualTo(0));
        }

        [Test]
        public void HigherPositionAlwaysPaysAtLeastAsMuchAsLowerPosition()
        {
            // for non-zero lap cars
            var results = Enumerable.Range(0, 100)
                .Select(i => R($"team-{i + 1}", i + 1, 1))
                .ToList();
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            for (int pos = 1; pos < 100; pos++)
            {
                Assert.That(prizes[$"team-{pos}"], Is.GreaterThanOrEqualTo(prizes[$"team-{pos + 1}"]));
            }
        }

        [Test]
        public void ReturnsAnEntryForEveryInputTeam()
        {
            var results = new[] { R("a", 1, 48), R("b", 2, 48), R("c", 3, 0) };
            var prizes = PostRace.DistributePrizeMoney(results, schedule);
            Assert.That(prizes.Keys, Has.Count.EqualTo(3));
            Assert.That(prizes.ContainsKey("a"), Is.True);
            Assert.That(prizes.ContainsKey("b"), Is.True);
            Assert.That(prizes.ContainsKey("c"), Is.True);
        }
    }

    // -------------------------------------------------------------------------
    // calculatePrestige
    // -------------------------------------------------------------------------

    public class CalculatePrestigeTests
    {
        private static List<PastRaceResult> Repeat(int count, int position, int totalCars) =>
            Enumerable.Range(0, count)
                .Select(_ => new PastRaceResult { Position = position, TotalCars = totalCars })
                .ToList();

        [Test]
        public void Returns0ForATeamWithNoRaceHistoryYear1()
        {
            Assert.That(PostRace.CalculatePrestige(new List<PastRaceResult>()), Is.EqualTo(0));
        }

        [Test]
        public void AWinnerAlwaysGetsPrestigeCloseTo100()
        {
            var alwaysWin = Repeat(10, 1, 100);
            // toBeCloseTo(100, 5): |diff| < 0.5e-5
            Assert.That(PostRace.CalculatePrestige(alwaysWin), Is.EqualTo(100).Within(0.5e-5));
        }

        [Test]
        public void APermanentLastPlaceTeamHasPrestigeOf0()
        {
            var alwaysLast = Repeat(10, 100, 100);
            Assert.That(PostRace.CalculatePrestige(alwaysLast), Is.EqualTo(0));
        }

        [Test]
        public void PrestigeIncreasesAfterAGoodResult()
        {
            var baseline = Repeat(5, 50, 100);
            var withWin = new List<PastRaceResult> { new PastRaceResult { Position = 1, TotalCars = 100 } }; // newest: win
            withWin.AddRange(baseline);
            Assert.That(PostRace.CalculatePrestige(withWin), Is.GreaterThan(PostRace.CalculatePrestige(baseline)));
        }

        [Test]
        public void PrestigeDecreasesAfterAPoorResult()
        {
            var baseline = Repeat(5, 50, 100);
            var withLoss = new List<PastRaceResult> { new PastRaceResult { Position = 100, TotalCars = 100 } }; // newest: last place
            withLoss.AddRange(baseline);
            Assert.That(PostRace.CalculatePrestige(withLoss), Is.LessThan(PostRace.CalculatePrestige(baseline)));
        }

        [Test]
        public void RecencyBiasRecentWinsOutweighRecentLosses()
        {
            // Team A: won recent 5 races, finished last in older 5
            var teamA = Repeat(5, 1, 100).Concat(Repeat(5, 100, 100)).ToList();
            // Team B: finished last in recent 5, won older 5 (mirror of A)
            var teamB = Repeat(5, 100, 100).Concat(Repeat(5, 1, 100)).ToList();
            Assert.That(PostRace.CalculatePrestige(teamA), Is.GreaterThan(PostRace.CalculatePrestige(teamB)));
        }

        [Test]
        public void PrestigeIsBetween0And100InclusiveForAnyRealisticInput()
        {
            var mixed = new List<PastRaceResult>
            {
                new PastRaceResult { Position = 1, TotalCars = 100 },
                new PastRaceResult { Position = 50, TotalCars = 100 },
                new PastRaceResult { Position = 100, TotalCars = 100 },
                new PastRaceResult { Position = 25, TotalCars = 100 },
            };
            var p = PostRace.CalculatePrestige(mixed);
            Assert.That(p, Is.GreaterThanOrEqualTo(0));
            Assert.That(p, Is.LessThanOrEqualTo(100));
        }

        [Test]
        public void ASingleCarRaceWinnerGetsFullPrestige()
        {
            // Edge case: totalCars = 1, so the only possible position is 1
            var result = new List<PastRaceResult> { new PastRaceResult { Position = 1, TotalCars = 1 } };
            Assert.That(PostRace.CalculatePrestige(result), Is.EqualTo(100).Within(0.5e-5));
        }
    }
}
