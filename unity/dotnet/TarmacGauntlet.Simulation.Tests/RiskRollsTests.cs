#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.RiskRolls;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/riskRolls.test.ts.</summary>
    public static class RiskRollsTests
    {
        private const InstructionMode Push = InstructionMode.Push;
        private const InstructionMode Normal = InstructionMode.Normal;
        private const InstructionMode Conserve = InstructionMode.Conserve;

        private static readonly IssueTemplate LooseWheel = new IssueTemplate
        {
            Id = "loose-wheel",
            Description = "Loose wheel nut",
            Severity = IssueSeverity.Minor,
            Category = IssueCategory.Mechanical,
            StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.08, [CarStatKey.Reliability] = 0.10 },
            Weight = 10,
            SparePartsCost = 2,
            WorkUnits = 14,
        };

        private static readonly IssueTemplate BrakeFade = new IssueTemplate
        {
            Id = "brake-fade",
            Description = "Brake fade",
            Severity = IssueSeverity.Medium,
            Category = IssueCategory.Mechanical,
            StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.18, [CarStatKey.TyreDurability] = 0.10 },
            Weight = 4,
            SparePartsCost = 2,
            WorkUnits = 30,
        };

        private static readonly IssueTemplate FrontWingDamage = new IssueTemplate
        {
            Id = "front-wing-damage",
            Description = "Front wing damage",
            Severity = IssueSeverity.Major,
            Category = IssueCategory.Crash,
            StatDebuffs = new Dictionary<CarStatKey, double> { [CarStatKey.Handling] = 0.25, [CarStatKey.FuelEfficiency] = 0.15 },
            Weight = 3,
            SparePartsCost = 4,
            WorkUnits = 50,
        };

        /// <summary>Baseline "healthy car, safe driver, Normal mode" context.</summary>
        private static readonly LapRiskContext Ctx = new LapRiskContext
        {
            CurrentLap = 10,
            Condition = 100,
            CarAge = 0,
            Reliability = 80,
            DriverSafety = 80,
            InstructionMode = Normal,
            ActiveIssues = new ActiveIssue[0],
            IssueTemplates = new[] { LooseWheel, BrakeFade },
            CrashTemplates = new[] { FrontWingDamage },
            Random = () => 0.5, // mid-range default; overridden per test
        };

        /// <summary>Forces every probability roll to succeed (roll = 0 &lt; any positive prob).</summary>
        private static readonly Func<double> AlwaysFire = () => 0;
        /// <summary>Forces every probability roll to fail (roll = 1 ≥ any prob ≤ 1).</summary>
        private static readonly Func<double> NeverFire = () => 1;

        /// <summary>Returns the given values for the first calls, then the fallback.</summary>
        private static Func<double> Sequence(double fallback, params double[] values)
        {
            int callCount = 0;
            return () =>
            {
                callCount++;
                return callCount <= values.Length ? values[callCount - 1] : fallback;
            };
        }

        public class IssueEffectiveProbabilityTests
        {
            [Test]
            public void ReturnsBaseProbabilityAtPerfectConditionZeroReliabilityNormalMode()
            {
                Assert.That(IssueEffectiveProbability(100, 0, Normal), Is.EqualTo(0.05).Within(0.005));
            }

            [Test]
            public void LowerConditionIncreasesEffectiveProbability()
            {
                Assert.That(IssueEffectiveProbability(0, 50, Normal), Is.GreaterThan(IssueEffectiveProbability(100, 50, Normal)));
            }

            [Test]
            public void HigherReliabilityReducesEffectiveProbability()
            {
                Assert.That(IssueEffectiveProbability(80, 100, Normal), Is.LessThan(IssueEffectiveProbability(80, 0, Normal)));
            }

            [Test]
            public void PushModeRaisesProbabilityConserveModeLowersItVsNormal()
            {
                double push = IssueEffectiveProbability(80, 50, Push);
                double normal = IssueEffectiveProbability(80, 50, Normal);
                double conserve = IssueEffectiveProbability(80, 50, Conserve);
                Assert.That(push, Is.GreaterThan(normal));
                Assert.That(normal, Is.GreaterThan(conserve));
            }
        }

        public class FailureEffectiveProbabilityTests
        {
            [Test]
            public void ReturnsALowProbabilityUnderIdealConditions()
            {
                Assert.That(FailureEffectiveProbability(100, 0, 100, 100, Normal), Is.LessThan(0.005));
            }

            [Test]
            public void LowerConditionIncreasesFailureProbability()
            {
                Assert.That(FailureEffectiveProbability(0, 0, 50, 50, Normal), Is.GreaterThan(FailureEffectiveProbability(100, 0, 50, 50, Normal)));
            }

            [Test]
            public void OlderCarHasHigherFailureProbability()
            {
                Assert.That(FailureEffectiveProbability(80, 20, 50, 50, Normal), Is.GreaterThan(FailureEffectiveProbability(80, 0, 50, 50, Normal)));
            }

            [Test]
            public void AgeFactorIsCappedAge50AndAge38ProduceTheSameProbability()
            {
                double at38 = FailureEffectiveProbability(80, 38, 50, 50, Normal);
                double at50 = FailureEffectiveProbability(80, 50, 50, 50, Normal);
                Assert.That(at38, Is.EqualTo(at50).Within(5e-11));
            }

            [Test]
            public void HigherReliabilityReducesFailureProbability()
            {
                Assert.That(FailureEffectiveProbability(80, 5, 100, 50, Normal), Is.LessThan(FailureEffectiveProbability(80, 5, 0, 50, Normal)));
            }

            [Test]
            public void HigherDriverSafetyReducesFailureProbability()
            {
                Assert.That(FailureEffectiveProbability(80, 5, 50, 100, Normal), Is.LessThan(FailureEffectiveProbability(80, 5, 50, 0, Normal)));
            }

            [Test]
            public void PushModeNoticeablyRaisesFailureProbabilityVsNormalAndConserve()
            {
                double push = FailureEffectiveProbability(50, 10, 30, 30, Push);
                double normal = FailureEffectiveProbability(50, 10, 30, 30, Normal);
                double conserve = FailureEffectiveProbability(50, 10, 30, 30, Conserve);
                Assert.That(push, Is.GreaterThan(normal));
                Assert.That(normal, Is.GreaterThan(conserve));
                Assert.That(push, Is.GreaterThan(normal * 1.5));
            }
        }

        public class CrashSurvivalProbabilityTests
        {
            [Test]
            public void HigherSafetyMeansHigherSurvivalChance()
            {
                Assert.That(CrashSurvivalProbability(100), Is.GreaterThan(CrashSurvivalProbability(0)));
            }

            [Test]
            public void SurvivalProbabilityIsBetween0And1()
            {
                Assert.That(CrashSurvivalProbability(0), Is.GreaterThan(0));
                Assert.That(CrashSurvivalProbability(100), Is.LessThan(1));
            }
        }

        public class WeightedPickTests
        {
            [Test]
            public void ReturnsATemplateFromTheList()
            {
                var result = WeightedPick(new[] { LooseWheel, BrakeFade }, new ActiveIssue[0], () => 0.5);
                Assert.That(result, Is.Not.Null);
                Assert.That(new[] { "loose-wheel", "brake-fade" }, Does.Contain(result!.Id));
            }

            [Test]
            public void SkipsAlreadyActiveIssues()
            {
                var result = WeightedPick(
                    new[] { LooseWheel, BrakeFade },
                    new[] { new ActiveIssue { TemplateId = "loose-wheel", LapOccurred = 1 } },
                    () => 0);
                Assert.That(result!.Id, Is.EqualTo("brake-fade"));
            }

            [Test]
            public void ReturnsNullWhenAllTemplatesAreActive()
            {
                var result = WeightedPick(
                    new[] { LooseWheel },
                    new[] { new ActiveIssue { TemplateId = "loose-wheel", LapOccurred = 1 } },
                    () => 0);
                Assert.That(result, Is.Null);
            }

            [Test]
            public void HigherWeightTemplatesArePickedMoreOften()
            {
                // looseWheel weight=10, brakeFade weight=4. Total=14.
                // Roll at 0 should pick looseWheel (first in list, higher weight).
                var result = WeightedPick(new[] { LooseWheel, BrakeFade }, new ActiveIssue[0], () => 0);
                Assert.That(result!.Id, Is.EqualTo("loose-wheel"));
            }
        }

        public class RollLapRisksNoEvents
        {
            [Test]
            public void ReturnsNoIssuesAndNoFailureWhenRollsNeverFire()
            {
                var result = RollLapRisks(Ctx with { Random = NeverFire });
                Assert.That(result.NewIssues, Has.Count.EqualTo(0));
                Assert.That(result.Failure, Is.Null);
            }

            [Test]
            public void PerfectCarAtConserveModeProducesNoEventsWithNeverFireRandom()
            {
                var ctx = Ctx with
                {
                    Condition = 100,
                    CarAge = 0,
                    Reliability = 100,
                    DriverSafety = 100,
                    InstructionMode = Conserve,
                    Random = NeverFire,
                };
                var result = RollLapRisks(ctx);
                Assert.That(result.NewIssues, Has.Count.EqualTo(0));
                Assert.That(result.Failure, Is.Null);
            }
        }

        public class RollLapRisksIssues
        {
            [Test]
            public void TriggersOneIssueWhenIssueRollFires()
            {
                // Calls: failure roll (skip), issue roll (fire), weighted pick
                var result = RollLapRisks(Ctx with { Random = Sequence(0, 1) });
                Assert.That(result.Failure, Is.Null);
                Assert.That(result.NewIssues, Has.Count.EqualTo(1));
            }

            [Test]
            public void AlreadyActiveIssuesAreExcludedFromTheWeightedPick()
            {
                // skip failure, fire issue, pick first eligible
                var ctx = Ctx with
                {
                    ActiveIssues = new[] { new ActiveIssue { TemplateId = "loose-wheel", LapOccurred = 5 } },
                    Random = Sequence(0, 1),
                };
                var result = RollLapRisks(ctx);
                Assert.That(result.NewIssues.FirstOrDefault(i => i.TemplateId == "loose-wheel"), Is.Null);
                if (result.NewIssues.Count > 0)
                {
                    Assert.That(result.NewIssues[0].TemplateId, Is.EqualTo("brake-fade"));
                }
            }

            [Test]
            public void NewIssueRecordsTheCorrectLapNumber()
            {
                var result = RollLapRisks(Ctx with { CurrentLap = 17, Random = Sequence(0, 1) });
                Assert.That(result.Failure, Is.Null);
                foreach (var issue in result.NewIssues)
                {
                    Assert.That(issue.LapOccurred, Is.EqualTo(17));
                }
            }

            [Test]
            public void IssueProbabilityIsHigherAtLowCondition()
            {
                double goodCondition = IssueEffectiveProbability(100, 50, Normal);
                double poorCondition = IssueEffectiveProbability(10, 50, Normal);
                Assert.That(poorCondition, Is.GreaterThan(goodCondition));
            }
        }

        public class RollLapRisksFailures
        {
            [Test]
            public void FailureIsReturnedWhenFirstRollFires()
            {
                var result = RollLapRisks(Ctx with { Random = AlwaysFire });
                // AlwaysFire: failure fires, crash roll fires (< 0.25), survival fires → non-terminal
                // OR terminal depending on crash templates. Either way, something happens.
                Assert.That(result.Failure != null || result.NewIssues.Count > 0, Is.True);
            }

            [Test]
            public void TerminalMechanicalFailureWhenCrashRollDoesNotFire()
            {
                // Calls: failure (fire), crash type (>0.25 = mechanical)
                var result = RollLapRisks(Ctx with { Random = Sequence(0.5, 0, 0.5) });
                Assert.That(result.Failure, Is.EqualTo(FailureType.Mechanical));
                Assert.That(result.NewIssues, Has.Count.EqualTo(0));
            }

            [Test]
            public void NonTerminalCrashProducesACrashIssueWhenDriverSurvives()
            {
                // Calls: failure (fire), crash type (<0.25), survival (fire), weighted pick
                var result = RollLapRisks(Ctx with { Random = Sequence(0, 0, 0, 0) });
                Assert.That(result.Failure, Is.Null);
                Assert.That(result.NewIssues, Has.Count.EqualTo(1));
                Assert.That(result.NewIssues[0].TemplateId, Is.EqualTo("front-wing-damage"));
            }

            [Test]
            public void TerminalCrashWhenDriverDoesNotSurvive()
            {
                // Calls: failure (fire), crash type (<0.25), survival (fail)
                var result = RollLapRisks(Ctx with { Random = Sequence(0.5, 0, 0, 1) });
                Assert.That(result.Failure, Is.EqualTo(FailureType.Crash));
                Assert.That(result.NewIssues, Has.Count.EqualTo(0));
            }

            [Test]
            public void FailureProbabilityIsHigherWithPushLowConditionOldCar()
            {
                double dangerous = FailureEffectiveProbability(20, 15, 20, 20, Push);
                double safe = FailureEffectiveProbability(100, 0, 100, 100, Conserve);
                Assert.That(dangerous, Is.GreaterThan(safe * 10));
            }

            [Test]
            public void DriverSafetyStatReducesFailureProbability()
            {
                double unsafeProb = FailureEffectiveProbability(70, 5, 50, 0, Normal);
                double safeProb = FailureEffectiveProbability(70, 5, 50, 100, Normal);
                Assert.That(safeProb, Is.LessThan(unsafeProb));
            }
        }
    }
}
