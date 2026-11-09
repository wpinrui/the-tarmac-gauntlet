#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/pitStop.test.ts.</summary>
    public static class PitStopTests
    {
        private static readonly IssueTemplate[] NoIssueTemplates = new IssueTemplate[0];

        /// <summary>Reference context: mid-tier car, half-worn tyres, 40 L fuel, crew 8, skill 10.</summary>
        private static readonly PitStopContext Ctx = new PitStopContext
        {
            PitStopTime = 20,
            FuelCapacity = 100,
            CurrentFuel = 40,
            CurrentTyreWear = 50,
            TyreSetsAvailable = 5,
            SparePartsAvailable = 10,
            CurrentDriverId = "driver-A",
            DriverFatigue = new Dictionary<string, double> { ["driver-A"] = 60, ["driver-B"] = 0 },
            ActiveIssues = new ActiveIssue[0],
            IssueTemplates = NoIssueTemplates,
            CrewSize = 8,
            EngineerSkill = 10,
        };

        /// <summary>Minimal config: no refuel, no tyre change, keep current driver.</summary>
        private static readonly PitStopConfig EmptyConfig = new PitStopConfig
        {
            FuelToAdd = 0,
            ChangeTyres = false,
            NextDriverId = null,
        };

        private static PitStopResult Run(PitStopContext ctx, PitStopConfig config) => PitStop.ExecutePitStop(ctx, config);

        public class Duration
        {
            [Test]
            public void ReturnsAPositiveDurationForAnyStop()
            {
                Assert.That(Run(Ctx, EmptyConfig).Duration, Is.GreaterThan(0));
            }

            [Test]
            public void AddingTasksIncreasesDuration()
            {
                double baseDuration = Run(Ctx, EmptyConfig).Duration;
                var fullConfig = new PitStopConfig { FuelToAdd = 60, ChangeTyres = true, NextDriverId = "driver-B" };
                double fullDuration = Run(Ctx, fullConfig).Duration;
                Assert.That(fullDuration, Is.GreaterThan(baseDuration));
            }

            [Test]
            public void LargerRefuelAddsMoreTimeThanSmallRefuel()
            {
                double small = Run(Ctx, EmptyConfig with { FuelToAdd = 10 }).Duration;
                double large = Run(Ctx, EmptyConfig with { FuelToAdd = 60 }).Duration;
                Assert.That(large, Is.GreaterThan(small));
            }

            // --- Crew ---

            [Test]
            public void MoreCrewProducesAFasterStopThanLessCrew()
            {
                double bigCrew = Run(Ctx with { CrewSize = 16 }, EmptyConfig).Duration;
                double smallCrew = Run(Ctx with { CrewSize = 2 }, EmptyConfig).Duration;
                Assert.That(bigCrew, Is.LessThan(smallCrew));
            }

            [Test]
            public void ZeroCrewSoloIsSlowerThanAnyCrew()
            {
                double solo = Run(Ctx with { CrewSize = 0 }, EmptyConfig).Duration;
                double oneCrew = Run(Ctx with { CrewSize = 1 }, EmptyConfig).Duration;
                Assert.That(solo, Is.GreaterThan(oneCrew));
            }

            [Test]
            public void FullCrew16GivesTheFastestBaseStop()
            {
                double full = Run(Ctx with { CrewSize = 16 }, EmptyConfig).Duration;
                double half = Run(Ctx with { CrewSize = 8 }, EmptyConfig).Duration;
                Assert.That(full, Is.LessThan(half));
            }

            // --- Engineer skill ---

            [Test]
            public void HigherEngineerSkillReducesStopDuration()
            {
                double highSkill = Run(Ctx with { EngineerSkill = 20 }, EmptyConfig).Duration;
                double noSkill = Run(Ctx with { EngineerSkill = 0 }, EmptyConfig).Duration;
                Assert.That(highSkill, Is.LessThan(noSkill));
            }

            [Test]
            public void LowerCarPitStopTimeStatReducesStopDuration()
            {
                double fast = Run(Ctx with { PitStopTime = 10 }, EmptyConfig).Duration;
                double slow = Run(Ctx with { PitStopTime = 40 }, EmptyConfig).Duration;
                Assert.That(fast, Is.LessThan(slow));
            }
        }

        public class Fuel
        {
            [Test]
            public void FuelIsIncreasedByTheConfiguredAmount()
            {
                Assert.That(Run(Ctx, EmptyConfig with { FuelToAdd = 40 }).FuelLevel, Is.EqualTo(80)); // 40 current + 40 added
            }

            [Test]
            public void FuelIsCappedAtTankCapacity()
            {
                var result = Run(Ctx with { CurrentFuel = 90, FuelCapacity = 100 }, EmptyConfig with { FuelToAdd = 50 });
                Assert.That(result.FuelLevel, Is.EqualTo(100));
            }

            [Test]
            public void FuelToAdd0AddsNoFuelAndNoRefuelTime()
            {
                var without = Run(Ctx with { CurrentFuel = 40 }, EmptyConfig);
                Assert.That(without.FuelLevel, Is.EqualTo(40));
                double withRefuel = Run(Ctx with { CurrentFuel = 40 }, EmptyConfig with { FuelToAdd = 1 }).Duration;
                Assert.That(withRefuel, Is.GreaterThan(without.Duration));
            }

            [Test]
            public void AlreadyFullTankFuelToAddDoesNothing()
            {
                var full = Ctx with { CurrentFuel = 100, FuelCapacity = 100 };
                double withAttempt = Run(full, EmptyConfig with { FuelToAdd = 50 }).Duration;
                double without = Run(full, EmptyConfig).Duration;
                Assert.That(withAttempt, Is.EqualTo(without));
            }
        }

        public class Tyres
        {
            [Test]
            public void TyreChangeResetsWearTo0()
            {
                var result = Run(Ctx with { CurrentTyreWear = 80 }, EmptyConfig with { ChangeTyres = true });
                Assert.That(result.TyreWear, Is.EqualTo(0));
            }

            [Test]
            public void TyreChangeConsumesOneTyreSet()
            {
                var result = Run(Ctx with { TyreSetsAvailable = 3 }, EmptyConfig with { ChangeTyres = true });
                Assert.That(result.TyreSetsRemaining, Is.EqualTo(2));
            }

            [Test]
            public void NoTyreChangeLeavesWearAndSetsUnchanged()
            {
                var result = Run(Ctx with { CurrentTyreWear = 65, TyreSetsAvailable = 4 }, EmptyConfig);
                Assert.That(result.TyreWear, Is.EqualTo(65));
                Assert.That(result.TyreSetsRemaining, Is.EqualTo(4));
            }

            [Test]
            public void TyreChangeIsSkippedWhenNoSetsRemain()
            {
                var result = Run(Ctx with { TyreSetsAvailable = 0, CurrentTyreWear = 70 }, EmptyConfig with { ChangeTyres = true });
                Assert.That(result.TyreWear, Is.EqualTo(70)); // unchanged
                Assert.That(result.TyreSetsRemaining, Is.EqualTo(0));
            }
        }

        public class DriverSwap
        {
            [Test]
            public void DriverSwapChangesTheCurrentDriverId()
            {
                Assert.That(Run(Ctx, EmptyConfig with { NextDriverId = "driver-B" }).CurrentDriverId, Is.EqualTo("driver-B"));
            }

            [Test]
            public void IncomingDriversFatigueIsResetTo0()
            {
                var ctx = Ctx with { DriverFatigue = new Dictionary<string, double> { ["driver-A"] = 60, ["driver-B"] = 80 } };
                var result = Run(ctx, EmptyConfig with { NextDriverId = "driver-B" });
                Assert.That(result.DriverFatigue["driver-B"], Is.EqualTo(0));
            }

            [Test]
            public void OutgoingDriversFatigueIsPreservedInTheMap()
            {
                var ctx = Ctx with { DriverFatigue = new Dictionary<string, double> { ["driver-A"] = 60, ["driver-B"] = 0 } };
                var result = Run(ctx, EmptyConfig with { NextDriverId = "driver-B" });
                Assert.That(result.DriverFatigue["driver-A"], Is.EqualTo(60));
            }

            [Test]
            public void NextDriverIdNullKeepsTheCurrentDriver()
            {
                Assert.That(Run(Ctx, EmptyConfig).CurrentDriverId, Is.EqualTo("driver-A"));
            }

            [Test]
            public void NextDriverIdEqualToCurrentDriverIdDoesNotCountAsASwap()
            {
                double withSameDriver = Run(Ctx, EmptyConfig with { NextDriverId = "driver-A" }).Duration;
                double withNoSwap = Run(Ctx, EmptyConfig).Duration;
                Assert.That(withSameDriver, Is.EqualTo(withNoSwap));
            }
        }

        public class IssueFixes
        {
            private static readonly IssueTemplate[] Templates =
            {
                new IssueTemplate
                {
                    Id = "loose-wheel",
                    Description = "Loose wheel nut",
                    Severity = IssueSeverity.Minor,
                    Category = IssueCategory.Mechanical,
                    StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.08 },
                    Weight = 10,
                    SparePartsCost = 2,
                    WorkUnits = 10,
                },
                new IssueTemplate
                {
                    Id = "brake-fade",
                    Description = "Brake fade",
                    Severity = IssueSeverity.Medium,
                    Category = IssueCategory.Mechanical,
                    StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.18 },
                    Weight = 4,
                    SparePartsCost = 4,
                    WorkUnits = 15,
                },
            };

            private static readonly ActiveIssue[] Issues =
            {
                new ActiveIssue { TemplateId = "loose-wheel", LapOccurred = 3 },
                new ActiveIssue { TemplateId = "brake-fade", LapOccurred = 5 },
            };

            private static PitStopContext WithIssues(int spareParts) =>
                Ctx with { ActiveIssues = Issues, IssueTemplates = Templates, SparePartsAvailable = spareParts };

            private static readonly PitStopConfig FixLooseWheel = EmptyConfig with { IssueIdsToFix = new[] { "loose-wheel" } };

            [Test]
            public void FixedIssuesAreRemovedFromActiveIssues()
            {
                var result = Run(WithIssues(10), FixLooseWheel);
                Assert.That(result.ActiveIssues.FirstOrDefault(i => i.TemplateId == "loose-wheel"), Is.Null);
                Assert.That(result.ActiveIssues.FirstOrDefault(i => i.TemplateId == "brake-fade"), Is.Not.Null);
            }

            [Test]
            public void FixingAnIssueConsumesSpareParts()
            {
                Assert.That(Run(WithIssues(10), FixLooseWheel).SparePartsRemaining, Is.EqualTo(8)); // cost = 2
            }

            [Test]
            public void FixingAnIssueAddsItsWorkUnitsToTheStop()
            {
                double withFix = Run(WithIssues(10), FixLooseWheel).Duration;
                double withoutFix = Run(WithIssues(10), EmptyConfig).Duration;
                Assert.That(withFix, Is.GreaterThan(withoutFix));
            }

            [Test]
            public void IssueIsSkippedIfSparePartsAreInsufficient()
            {
                var result = Run(WithIssues(1), FixLooseWheel);
                Assert.That(result.ActiveIssues.FirstOrDefault(i => i.TemplateId == "loose-wheel"), Is.Not.Null); // not fixed
                Assert.That(result.SparePartsRemaining, Is.EqualTo(1)); // no parts consumed
            }

            [Test]
            public void UnfixedIssuesRemainActive()
            {
                var result = Run(WithIssues(10), EmptyConfig); // fix nothing (no IssueIdsToFix)
                Assert.That(result.ActiveIssues, Has.Count.EqualTo(2));
            }
        }

        public class InstructionModeReset
        {
            [Test]
            public void AlwaysReturnsNormalInstructionMode()
            {
                Assert.That(Run(Ctx, EmptyConfig).InstructionMode, Is.EqualTo(InstructionMode.Normal));
            }
        }

        public class CombinedStop
        {
            [Test]
            public void FullStopUpdatesAllResourcesCorrectly()
            {
                var templates = new[]
                {
                    new IssueTemplate
                    {
                        Id = "loose-wheel", Description = "test", Severity = IssueSeverity.Minor, Category = IssueCategory.Mechanical,
                        StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.08 },
                        Weight = 10, SparePartsCost = 3, WorkUnits = 8,
                    },
                };
                var ctx = Ctx with
                {
                    CurrentFuel = 20,
                    FuelCapacity = 100,
                    CurrentTyreWear = 80,
                    TyreSetsAvailable = 2,
                    SparePartsAvailable = 5,
                    DriverFatigue = new Dictionary<string, double> { ["driver-A"] = 70, ["driver-B"] = 0 },
                    ActiveIssues = new[] { new ActiveIssue { TemplateId = "loose-wheel", LapOccurred = 2 } },
                    IssueTemplates = templates,
                };
                var config = new PitStopConfig
                {
                    FuelToAdd = 60,
                    ChangeTyres = true,
                    NextDriverId = "driver-B",
                    IssueIdsToFix = new[] { "loose-wheel" },
                };

                var result = Run(ctx, config);

                Assert.That(result.FuelLevel, Is.EqualTo(80));         // 20 + 60
                Assert.That(result.TyreWear, Is.EqualTo(0));           // fresh tyres
                Assert.That(result.TyreSetsRemaining, Is.EqualTo(1));  // 2 → 1
                Assert.That(result.SparePartsRemaining, Is.EqualTo(2)); // 5 − 3
                Assert.That(result.CurrentDriverId, Is.EqualTo("driver-B"));
                Assert.That(result.DriverFatigue["driver-B"], Is.EqualTo(0));
                Assert.That(result.ActiveIssues, Has.Count.EqualTo(0)); // issue fixed
                Assert.That(result.InstructionMode, Is.EqualTo(InstructionMode.Normal));
                Assert.That(result.Duration, Is.GreaterThan(0));
            }
        }
    }
}
