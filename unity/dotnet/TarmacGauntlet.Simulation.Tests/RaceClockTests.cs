#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.Tests.TestFixtures;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/raceClock.test.ts.</summary>
    public static class RaceClockTests
    {
        private static CarRaceResult Finisher(string carId, int position, int laps, double totalTime) => new CarRaceResult
        {
            CarId = carId,
            FinalPosition = position,
            LapsCompleted = laps,
            TotalTime = totalTime,
            Retired = false,
            RetirementLap = null,
            RetirementReason = null,
        };

        /// <summary>Two cars, 4 laps each. A wins, B trails by 8 sim seconds at the line.</summary>
        private static RaceResultFull Fixture() => new RaceResultFull
        {
            Results = new[] { Finisher("A", 1, 4, 40), Finisher("B", 2, 4, 48) },
            FastestLap = new RaceFastestLap { CarId = "A", Lap = 1, Time = 10 },
            LapSnapshots = new Dictionary<string, IReadOnlyList<CarLapSnapshot>>
            {
                ["A"] = new[] { Snap(1, 10), Snap(2, 20), Snap(3, 30), Snap(4, 40) },
                ["B"] = new[] { Snap(1, 12), Snap(2, 24), Snap(3, 36), Snap(4, 48) },
            },
            PositionHistory = new IReadOnlyList<int>[0],
            CarIndexById = new Dictionary<string, int> { ["A"] = 0, ["B"] = 1 },
            Events = new RaceEvent[0],
            Stints = new Dictionary<string, IReadOnlyList<Stint>> { ["A"] = new Stint[0], ["B"] = new Stint[0] },
            ModeCounters = new Dictionary<string, ModeCounter>
            {
                ["A"] = new ModeCounter { Push = 0, Normal = 4, Conserve = 0 },
                ["B"] = new ModeCounter { Push = 0, Normal = 4, Conserve = 0 },
            },
        };

        private static RaceResultFull Empty() => new RaceResultFull();

        public class TotalRaceSecondsConstant
        {
            [Test]
            public void MatchesTheGddContractOf24RealTimeMinutes()
            {
                Assert.That(RaceClock.TotalRaceSeconds, Is.EqualTo(1440));
            }
        }

        public class RaceSimDuration
        {
            [Test]
            public void ReturnsP1sLastLapTotalTime()
            {
                Assert.That(RaceClock.RaceSimDuration(Fixture()), Is.EqualTo(40));
            }

            [Test]
            public void ReturnsZeroForAnEmptyResult()
            {
                Assert.That(RaceClock.RaceSimDuration(Empty()), Is.EqualTo(0));
            }
        }

        public class WallToSim
        {
            [Test]
            public void ScalesLinearlyBetweenZeroAndRaceSimDuration()
            {
                RaceResultFull r = Fixture();
                Assert.That(RaceClock.WallToSim(0, r, 60), Is.EqualTo(0));
                Assert.That(RaceClock.WallToSim(30, r, 60), Is.EqualTo(20));
                Assert.That(RaceClock.WallToSim(60, r, 60), Is.EqualTo(40));
            }

            [Test]
            public void ClampsBelowZeroAndAboveTotalRaceSec()
            {
                RaceResultFull r = Fixture();
                Assert.That(RaceClock.WallToSim(-5, r, 60), Is.EqualTo(0));
                Assert.That(RaceClock.WallToSim(120, r, 60), Is.EqualTo(40));
            }

            [Test]
            public void ReturnsZeroWhenSimDurationIsZero()
            {
                Assert.That(RaceClock.WallToSim(30, Empty(), 60), Is.EqualTo(0));
            }
        }

        public class LapsCompletedAtSim
        {
            [Test]
            public void ReturnsZeroBeforeAnyLapCompletes()
            {
                Assert.That(RaceClock.LapsCompletedAtSim(new[] { Snap(1, 10), Snap(2, 20) }, 5), Is.EqualTo(0));
            }

            [Test]
            public void ReturnsTheLargestLapWhoseTotalTimeIsAtMostSimElapsed()
            {
                var s = new[] { Snap(1, 10), Snap(2, 20), Snap(3, 30) };
                Assert.That(RaceClock.LapsCompletedAtSim(s, 10), Is.EqualTo(1));
                Assert.That(RaceClock.LapsCompletedAtSim(s, 19.999), Is.EqualTo(1));
                Assert.That(RaceClock.LapsCompletedAtSim(s, 20), Is.EqualTo(2));
                Assert.That(RaceClock.LapsCompletedAtSim(s, 1000), Is.EqualTo(3));
            }

            [Test]
            public void ReturnsZeroForEmptySnapshots()
            {
                Assert.That(RaceClock.LapsCompletedAtSim(new CarLapSnapshot[0], 100), Is.EqualTo(0));
            }
        }

        public class LeaderLapAt
        {
            [Test]
            public void StartsAtZeroAndEndsAtTheLeadersTotalLaps()
            {
                RaceResultFull r = Fixture();
                Assert.That(RaceClock.LeaderLapAt(r, 0, 60), Is.EqualTo(0));
                Assert.That(RaceClock.LeaderLapAt(r, 60, 60), Is.EqualTo(4));
            }

            [Test]
            public void IsMonotonicallyNonDecreasingAcrossTheRace()
            {
                RaceResultFull r = Fixture();
                int prev = -1;
                for (double t = 0; t <= 60; t += 0.5)
                {
                    int lap = RaceClock.LeaderLapAt(r, t, 60);
                    Assert.That(lap, Is.GreaterThanOrEqualTo(prev));
                    prev = lap;
                }
            }

            [Test]
            public void PicksTheCarWithTheMostLapsAtTheMomentNotP1()
            {
                // Mid-race at sim=20: A done 2, B done 1, so leader = 2.
                Assert.That(RaceClock.LeaderLapAt(Fixture(), 30, 60), Is.EqualTo(2));
            }

            [Test]
            public void ConvergesToLeaderTotalLapsAtTheEndWhenThereIsNoTie()
            {
                RaceResultFull r = Fixture();
                Assert.That(RaceClock.LeaderLapAt(r, 60, 60), Is.EqualTo(RaceClock.LeaderTotalLaps(r)));
            }
        }

        public class LeaderTotalLaps
        {
            [Test]
            public void ReturnsTheStandingsWinnersLapCount()
            {
                Assert.That(RaceClock.LeaderTotalLaps(Fixture()), Is.EqualTo(4));
            }

            [Test]
            public void ReturnsZeroWhenResultsAreEmpty()
            {
                Assert.That(RaceClock.LeaderTotalLaps(Empty()), Is.EqualTo(0));
            }
        }
    }
}
