#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/raceLoop.test.ts.</summary>
    public static class RaceLoopTests
    {
        // --- Fixtures ---

        private static readonly CarStats BaseStats = new CarStats
        {
            Power = 50,
            Handling = 50,
            FuelEfficiency = 50,
            TyreDurability = 50,
            Comfort = 50,
            Reliability = 80,
            PitStopTime = 30,
            FuelCapacity = 200,
        };

        private static readonly CarModel TestModel = new CarModel
        {
            Id = "test-model",
            Name = "Test Car",
            CarClass = CarClass.C,
            Tier = CarTier.TrackCar,
            Price = 50_000,
            BaseStats = BaseStats,
            PotentialStats = BaseStats,
            UpgradePacks = new[]
            {
                new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = 5_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 5_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = 5_000 },
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

        private static readonly RaceDriver SafeDriver = new RaceDriver
        {
            Id = "driver-1",
            Stats = new DriverStats { Pace = 50, Consistency = 50, Stamina = 80, Safety = 100, Smoothness = 50 }, // never crashes
        };

        private static CarEntry MakeEntry() => new CarEntry
        {
            CarId = "car-1",
            TeamId = "team-1",
            Instance = BaseInstance,
            Model = TestModel,
            Drivers = new[] { SafeDriver },
            StartingDriverId = "driver-1",
            StartingFuel = 200, // full tank: never runs out during a short test race
            TyreSetsAvailable = 10,
            SparePartsAvailable = 50,
            CrewSize = 8,
            EngineerSkill = 10,
        };

        /// <summary>Random that never triggers any probability check (rolls always = 1).</summary>
        private static readonly Func<double> NeverFire = () => 1;
        /// <summary>Random that always triggers every probability check (rolls always = 0).</summary>
        private static readonly Func<double> AlwaysFire = () => 0;

        private static RaceResultFull Run(IReadOnlyList<CarEntry> cars, int? totalLaps, Func<double> random, double? raceDurationSec = null) =>
            RaceLoop.SimulateRace(cars, new RaceOptions { TotalLaps = totalLaps, Random = random, RaceDurationSec = raceDurationSec });

        private static RaceResultFull Run(CarEntry car, int? totalLaps, Func<double> random) => Run(new[] { car }, totalLaps, random);

        private static readonly PitStopConfig NoopPit = new PitStopConfig
        {
            FuelToAdd = 0,
            ChangeTyres = false,
            NextDriverId = null,
            IssueIdsToFix = new string[0],
        };

        public class SingleCarCompletesARace
        {
            [Test]
            public void CarWithNoFailuresRunsAllLapsAndFinishes()
            {
                RaceResultFull result = Run(MakeEntry(), 48, NeverFire);
                Assert.That(result.Results, Has.Count.EqualTo(1));
                CarRaceResult car = result.Results[0];
                Assert.That(car.LapsCompleted, Is.EqualTo(48));
                Assert.That(car.Retired, Is.False);
                Assert.That(car.RetirementLap, Is.Null);
                Assert.That(car.RetirementReason, Is.Null);
                Assert.That(car.FinalPosition, Is.EqualTo(1));
            }

            [Test]
            public void TotalTimeIsGreaterThanMinimumPossible()
            {
                RaceResultFull result = Run(MakeEntry(), 48, NeverFire);
                Assert.That(result.Results[0].TotalTime, Is.GreaterThan(48 * 26));
            }

            [Test]
            public void FastestLapIsRecorded()
            {
                RaceResultFull result = Run(MakeEntry(), 5, NeverFire);
                Assert.That(result.FastestLap, Is.Not.Null);
                Assert.That(result.FastestLap!.CarId, Is.EqualTo("car-1"));
                Assert.That(result.FastestLap.Lap, Is.GreaterThanOrEqualTo(1));
                Assert.That(result.FastestLap.Time, Is.GreaterThan(0));
            }
        }

        public class PitStops
        {
            [Test]
            public void PlayerPitDeciderIsCalledAndPitDurationIsAddedToTotalTime()
            {
                bool pitCalled = false;
                CarEntry entry = MakeEntry() with
                {
                    PitDecider = (lap, _) =>
                    {
                        if (lap == 3)
                        {
                            pitCalled = true;
                            return NoopPit with { FuelToAdd = 50 };
                        }
                        return null;
                    },
                };

                RaceResultFull noPitResult = Run(MakeEntry(), 5, NeverFire);
                RaceResultFull withPitResult = Run(entry, 5, NeverFire);

                Assert.That(pitCalled, Is.True);
                Assert.That(withPitResult.Results[0].TotalTime, Is.GreaterThan(noPitResult.Results[0].TotalTime));
            }

            [Test]
            public void ForcedPitTriggersWhenFuelRunsOut()
            {
                CarEntry entry = MakeEntry() with
                {
                    StartingFuel = 5, // well below one lap's consumption
                    PitDecider = (_, _) => null, // player never pits intentionally
                };

                RaceResultFull result = Run(entry, 5, NeverFire);

                Assert.That(result.Results[0].LapsCompleted, Is.EqualTo(5));
                Assert.That(result.Results[0].Retired, Is.False);
            }

            [Test]
            public void TyreWearResetsAfterATyreChangePitStop()
            {
                double? tyreWearAtPit = null;
                double? tyreWearAfterPit = null;
                bool lapAfterPit = false;

                CarEntry entry = MakeEntry() with
                {
                    PitDecider = (lap, snapshot) =>
                    {
                        if (lap == 2)
                        {
                            tyreWearAtPit = snapshot.TyreWear;
                            return NoopPit with { ChangeTyres = true };
                        }
                        if (lap == 3 && tyreWearAtPit != null && !lapAfterPit)
                        {
                            tyreWearAfterPit = snapshot.TyreWear;
                            lapAfterPit = true;
                        }
                        return null;
                    },
                };

                Run(entry, 5, NeverFire);

                Assert.That(tyreWearAtPit, Is.Not.Null);
                Assert.That(tyreWearAtPit, Is.GreaterThan(0));
                Assert.That(tyreWearAfterPit, Is.Not.Null);
                Assert.That(tyreWearAfterPit!.Value, Is.LessThan(tyreWearAtPit!.Value));
            }
        }

        public class Failure
        {
            [Test]
            public void CarRetiresWhenFailureRollFiresOnLap1()
            {
                CarRaceResult car = Run(MakeEntry(), 48, AlwaysFire).Results[0];
                Assert.That(car.Retired, Is.True);
                Assert.That(car.RetirementLap, Is.EqualTo(1));
                Assert.That(car.RetirementReason, Is.Not.Null);
            }

            [Test]
            public void RetiredCarStillCountsLapsCompletedUpToRetirement()
            {
                // Fire everything after ~10 calls (laps 1-2 each use a few calls).
                int callCount = 0;
                Func<double> random = () =>
                {
                    callCount++;
                    return callCount > 10 ? 0 : 1;
                };

                CarRaceResult car = Run(MakeEntry(), 48, random).Results[0];
                Assert.That(car.Retired, Is.True);
                Assert.That(car.LapsCompleted, Is.GreaterThanOrEqualTo(1));
                Assert.That(car.LapsCompleted, Is.LessThan(48));
            }

            [Test]
            public void FailureTypeIsMechanicalOrCrash()
            {
                RaceResultFull result = Run(MakeEntry(), 48, AlwaysFire);
                Assert.That(new FailureType?[] { FailureType.Mechanical, FailureType.Crash }, Does.Contain(result.Results[0].RetirementReason));
            }

            [Test]
            public void RetiredCarHasNoMoreLapsAddedAfterRetirement()
            {
                CarRaceResult car = Run(MakeEntry(), 48, AlwaysFire).Results[0];
                Assert.That(car.LapsCompleted, Is.EqualTo(car.RetirementLap));
            }
        }

        public class Standings
        {
            [Test]
            public void FasterCarFinishesInPosition1()
            {
                CarStats fastStats = BaseStats with { Power = 90, Handling = 90 };
                CarStats slowStats = BaseStats with { Power = 10, Handling = 10 };
                CarEntry fastCar = MakeEntry() with
                {
                    CarId = "fast",
                    Model = TestModel with { BaseStats = fastStats, PotentialStats = fastStats },
                };
                CarEntry slowCar = MakeEntry() with
                {
                    CarId = "slow",
                    Model = TestModel with { BaseStats = slowStats, PotentialStats = slowStats },
                };

                RaceResultFull result = Run(new[] { slowCar, fastCar }, 5, NeverFire);

                CarRaceResult fastResult = result.Results.First(r => r.CarId == "fast");
                CarRaceResult slowResult = result.Results.First(r => r.CarId == "slow");

                Assert.That(fastResult.FinalPosition, Is.EqualTo(1));
                Assert.That(slowResult.FinalPosition, Is.EqualTo(2));
                Assert.That(fastResult.TotalTime, Is.LessThan(slowResult.TotalTime));
            }

            [Test]
            public void CarWithMoreLapsBeatsCarThatRetiredEarly()
            {
                CarEntry finisher = MakeEntry() with { CarId = "finisher" };
                CarEntry retiree = MakeEntry() with { CarId = "retiree" };

                // Each car uses 3 random calls per lap with no issue templates:
                // lap time variance, failure roll, issue roll. Call 5 is the retiree's lap-1 failure roll.
                int n = 0;
                RaceResultFull result = Run(new[] { finisher, retiree }, 5, () =>
                {
                    n++;
                    return n == 5 ? 0 : 1;
                });

                CarRaceResult finisherResult = result.Results.First(r => r.CarId == "finisher");
                CarRaceResult retireeResult = result.Results.First(r => r.CarId == "retiree");

                Assert.That(finisherResult.LapsCompleted, Is.GreaterThan(retireeResult.LapsCompleted));
                Assert.That(finisherResult.FinalPosition, Is.LessThan(retireeResult.FinalPosition));
            }

            [Test]
            public void PositionsAreUniqueAndContiguousStartingFrom1()
            {
                var cars = new[]
                {
                    MakeEntry() with { CarId = "a" },
                    MakeEntry() with { CarId = "b" },
                    MakeEntry() with { CarId = "c" },
                };
                RaceResultFull result = Run(cars, 3, NeverFire);
                int[] positions = result.Results.Select(r => r.FinalPosition).OrderBy(p => p).ToArray();
                Assert.That(positions, Is.EqualTo(new[] { 1, 2, 3 }));
            }
        }

        public class AiPitStrategy
        {
            [Test]
            public void AiCarPitsWhenTyreWearExceedsThreshold()
            {
                // Start at 70 so one lap (about 15 wear) pushes past the threshold of 80.
                CarEntry entry = MakeEntry() with { CarId = "ai-car", StartingTyreWear = 70, TyreSetsAvailable = 3 };
                CarEntry noPitEntry = MakeEntry() with { CarId = "no-pit", StartingTyreWear = 0 };

                RaceResultFull aiResult = Run(entry, 3, NeverFire);
                RaceResultFull noPitResult = Run(noPitEntry, 3, NeverFire);

                Assert.That(aiResult.Results[0].TotalTime, Is.GreaterThan(noPitResult.Results[0].TotalTime));
            }

            [Test]
            public void AiCarPitsWhenFuelDropsBelowThresholdFractionOfCapacity()
            {
                // FuelCapacity = 200, threshold = 0.15, so pit when below 30 litres.
                CarEntry entry = MakeEntry() with
                {
                    CarId = "low-fuel",
                    StartingFuel = 25,
                    TyreSetsAvailable = 0, // only fuel triggers the pit
                    StartingTyreWear = 0,
                };
                CarEntry baseline = MakeEntry() with { CarId = "full-fuel", StartingFuel = 200 };

                RaceResultFull lowFuelResult = Run(entry, 3, NeverFire);
                RaceResultFull baselineResult = Run(baseline, 3, NeverFire);

                Assert.That(lowFuelResult.Results[0].TotalTime, Is.GreaterThan(baselineResult.Results[0].TotalTime));
                Assert.That(lowFuelResult.Results[0].LapsCompleted, Is.EqualTo(3));
            }

            [Test]
            public void AiCarRotatesDriversAtPitStop()
            {
                var driver1 = new RaceDriver
                {
                    Id = "d1",
                    Stats = new DriverStats { Pace = 50, Consistency = 50, Stamina = 80, Safety = 100, Smoothness = 50 },
                };
                var driver2 = new RaceDriver
                {
                    Id = "d2",
                    Stats = new DriverStats { Pace = 50, Consistency = 50, Stamina = 80, Safety = 100, Smoothness = 50 },
                };

                string? modeAfterPit = null;
                CarEntry entry = MakeEntry() with
                {
                    CarId = "rotating",
                    StartingTyreWear = 85, // immediately above threshold
                    TyreSetsAvailable = 2,
                    Drivers = new[] { driver1, driver2 },
                    StartingDriverId = "d1",
                    // Observe the current driver via the mode decider.
                    ModeDecider = (_, snapshot) =>
                    {
                        modeAfterPit = snapshot.CurrentDriverId;
                        return InstructionMode.Normal;
                    },
                };

                Run(entry, 2, NeverFire);

                Assert.That(modeAfterPit, Is.EqualTo("d2"));
            }
        }

        public class InstructionModes
        {
            [Test]
            public void PitStopResetsInstructionModeToNormal()
            {
                InstructionMode? modeAfterPit = null;
                CarEntry entry = MakeEntry() with
                {
                    ModeDecider = (_, _) => InstructionMode.Push,
                    PitDecider = (lap, snapshot) =>
                    {
                        if (lap == 2) return NoopPit with { FuelToAdd = 10 };
                        if (lap == 3) modeAfterPit = snapshot.InstructionMode;
                        return null;
                    },
                };

                Run(entry, 4, NeverFire);
                Assert.That(modeAfterPit, Is.EqualTo(InstructionMode.Normal));
            }

            [Test]
            public void ModeDeciderAppliesToNextLapNotCurrent()
            {
                var modesUsed = new List<InstructionMode>();
                CarEntry entry = MakeEntry() with
                {
                    ModeDecider = (lap, _) => lap == 1 ? InstructionMode.Push : InstructionMode.Normal,
                    PitDecider = (_, snapshot) =>
                    {
                        modesUsed.Add(snapshot.InstructionMode);
                        return null;
                    },
                };

                Run(entry, 3, NeverFire);

                // Lap 1 runs in normal, the decider returns push, so lap 2 runs in push.
                Assert.That(modesUsed[1], Is.EqualTo(InstructionMode.Push));
            }
        }

        public class TimeBoundedRace
        {
            // Crew sizes mirror the AI fixture in gameInit.ts.
            private static readonly Dictionary<CarClass, int> CrewByClass = new Dictionary<CarClass, int>
            {
                [CarClass.F] = 0,
                [CarClass.E] = 2,
                [CarClass.D] = 4,
                [CarClass.C] = 6,
                [CarClass.B] = 8,
                [CarClass.A] = 12,
                [CarClass.F1] = 14,
            };

            /// <summary>Top-stat car of each class with a neutral driver and never-failing rolls.</summary>
            private static CarEntry ClassFixture(CarClass classId)
            {
                // Stable sort, same as JS Array.prototype.sort.
                CarModel model = CarModels.All
                    .Where(m => m.CarClass == classId)
                    .OrderByDescending(m => m.BaseStats.Power + m.BaseStats.Handling)
                    .First();
                var instance = new CarInstance
                {
                    Id = $"car-{classId}",
                    ModelId = model.Id,
                    Age = 0,
                    Condition = 100,
                    InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
                };
                var driver = new RaceDriver
                {
                    Id = $"d-{classId}",
                    Stats = new DriverStats { Pace = 50, Consistency = 50, Stamina = 80, Safety = 100, Smoothness = 50 },
                };
                return new CarEntry
                {
                    CarId = $"car-{classId}",
                    TeamId = $"team-{classId}",
                    Instance = instance,
                    Model = model,
                    Drivers = new[] { driver },
                    StartingDriverId = driver.Id,
                    StartingFuel = model.BaseStats.FuelCapacity,
                    TyreSetsAvailable = 12,
                    SparePartsAvailable = 50,
                    CrewSize = CrewByClass[classId],
                    EngineerSkill = 0,
                };
            }

            [Test]
            public void Default1440sBudgetProducesRealisticPerClassLapCounts()
            {
                CarClass[] classes = { CarClass.F1, CarClass.A, CarClass.B, CarClass.C, CarClass.D, CarClass.E, CarClass.F };
                // Uses the default 1440 s race budget.
                RaceResultFull result = RaceLoop.SimulateRace(classes.Select(ClassFixture).ToArray(), new RaceOptions { Random = NeverFire });

                var lapsByClass = new Dictionary<string, int>();
                foreach (CarRaceResult r in result.Results)
                {
                    lapsByClass[r.CarId.Replace("car-", "")] = r.LapsCompleted;
                }

                Assert.That(lapsByClass["A"], Is.GreaterThanOrEqualTo(40));
                Assert.That(lapsByClass["A"], Is.LessThanOrEqualTo(48));

                Assert.That(lapsByClass["F1"], Is.GreaterThanOrEqualTo(lapsByClass["A"]));
                Assert.That(lapsByClass["A"], Is.GreaterThan(lapsByClass["B"]));
                Assert.That(lapsByClass["B"], Is.GreaterThan(lapsByClass["C"]));
                Assert.That(lapsByClass["C"], Is.GreaterThan(lapsByClass["D"]));
                Assert.That(lapsByClass["D"], Is.GreaterThan(lapsByClass["E"]));
                Assert.That(lapsByClass["E"], Is.GreaterThan(lapsByClass["F"]));

                foreach (CarRaceResult r in result.Results)
                {
                    Assert.That(r.TotalTime, Is.LessThanOrEqualTo(1440 + 60)); // pit overhang allowed
                }
                Assert.That(result.Results[0].TotalTime, Is.GreaterThan(1440 - 60));
            }

            [Test]
            public void FrozenOutOfTimeCarsAreNotRetiredInTheResult()
            {
                RaceResultFull result = RaceLoop.SimulateRace(
                    new[] { ClassFixture(CarClass.A), ClassFixture(CarClass.F) },
                    new RaceOptions { Random = NeverFire });
                CarRaceResult a = result.Results.First(r => r.CarId == "car-A");
                CarRaceResult f = result.Results.First(r => r.CarId == "car-F");
                Assert.That(a.LapsCompleted, Is.GreaterThan(f.LapsCompleted));
                Assert.That(f.Retired, Is.False);
                Assert.That(f.RetirementReason, Is.Null);
            }

            [Test]
            public void ExplicitRaceDurationSecHalvesTheLapCountProportionally()
            {
                RaceResultFull full = RaceLoop.SimulateRace(new[] { ClassFixture(CarClass.A) }, new RaceOptions { Random = NeverFire });
                RaceResultFull half = RaceLoop.SimulateRace(
                    new[] { ClassFixture(CarClass.A) },
                    new RaceOptions { RaceDurationSec = 720, Random = NeverFire });
                double fullLaps = full.Results[0].LapsCompleted;
                Assert.That(half.Results[0].LapsCompleted, Is.GreaterThan(fullLaps / 2 - 2));
                Assert.That(half.Results[0].LapsCompleted, Is.LessThan(fullLaps / 2 + 2));
            }
        }
    }
}
