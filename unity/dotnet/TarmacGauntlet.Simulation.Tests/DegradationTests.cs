#nullable enable
using NUnit.Framework;
using static TarmacGauntlet.Simulation.Degradation;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/degradation.test.ts.</summary>
    public static class DegradationTests
    {
        private const InstructionMode Push = InstructionMode.Push;
        private const InstructionMode Normal = InstructionMode.Normal;
        private const InstructionMode Conserve = InstructionMode.Conserve;

        public class UpdateTyreWearTests
        {
            [Test]
            public void IncreasesTyreWearEachLapOnNormalMode()
            {
                Assert.That(UpdateTyreWear(0, 50, 50, Normal), Is.GreaterThan(0));
            }

            [Test]
            public void PushModeWearsTyresFasterThanNormalConserveWearsThemSlower()
            {
                double push = UpdateTyreWear(0, 50, 50, Push);
                double normal = UpdateTyreWear(0, 50, 50, Normal);
                double conserve = UpdateTyreWear(0, 50, 50, Conserve);
                Assert.That(push, Is.GreaterThan(normal));
                Assert.That(normal, Is.GreaterThan(conserve));
            }

            [Test]
            public void HigherTyreDurabilityReducesWearRate()
            {
                Assert.That(UpdateTyreWear(0, 100, 50, Normal), Is.LessThan(UpdateTyreWear(0, 0, 50, Normal)));
            }

            [Test]
            public void HigherDriverSmoothnessReducesWearRate()
            {
                Assert.That(UpdateTyreWear(0, 50, 100, Normal), Is.LessThan(UpdateTyreWear(0, 50, 0, Normal)));
            }

            [Test]
            public void ClampsTo100WhenWearWouldExceedMaximum()
            {
                Assert.That(UpdateTyreWear(95, 0, 0, Push), Is.EqualTo(100));
            }

            [Test]
            public void ReturnsCurrentValueWhenAlreadyAt100()
            {
                Assert.That(UpdateTyreWear(100, 50, 50, Normal), Is.EqualTo(100));
            }

            [Test]
            public void WearRateIsPositiveEvenWithMaximumStats()
            {
                // Tyres should always wear, even the best car+driver combination degrades them.
                Assert.That(UpdateTyreWear(0, 100, 100, Conserve), Is.GreaterThan(0));
            }
        }

        public class UpdateFuelLevelTests
        {
            [Test]
            public void DecreasesFuelLevelEachLapOnNormalMode()
            {
                Assert.That(UpdateFuelLevel(100, 50, Normal), Is.LessThan(100));
            }

            [Test]
            public void PushModeConsumesMoreFuelThanNormalConserveConsumesLess()
            {
                const double startFuel = 100;
                double push = UpdateFuelLevel(startFuel, 50, Push);
                double normal = UpdateFuelLevel(startFuel, 50, Normal);
                double conserve = UpdateFuelLevel(startFuel, 50, Conserve);
                Assert.That(push, Is.LessThan(normal));
                Assert.That(normal, Is.LessThan(conserve));
            }

            [Test]
            public void HigherFuelEfficiencyReducesConsumptionPerLap()
            {
                Assert.That(UpdateFuelLevel(100, 100, Normal), Is.GreaterThan(UpdateFuelLevel(100, 0, Normal)));
            }

            [Test]
            public void ClampsTo0WhenFuelWouldGoBelowEmpty()
            {
                Assert.That(UpdateFuelLevel(5, 0, Push), Is.EqualTo(0));
            }

            [Test]
            public void StaysAt0WhenAlreadyEmpty()
            {
                Assert.That(UpdateFuelLevel(0, 50, Normal), Is.EqualTo(0));
            }

            [Test]
            public void FuelIsAlwaysConsumedEvenAtMaximumEfficiency()
            {
                // The car always burns some fuel.
                Assert.That(UpdateFuelLevel(100, 100, Conserve), Is.LessThan(100));
            }
        }

        public class UpdateDriverFatigueTests
        {
            [Test]
            public void IncreasesFatigueEachLapOnNormalMode()
            {
                Assert.That(UpdateDriverFatigue(0, 50, 50, Normal), Is.GreaterThan(0));
            }

            [Test]
            public void PushModeBuildsFatigueFasterThanNormalConserveSlower()
            {
                double push = UpdateDriverFatigue(0, 50, 50, Push);
                double normal = UpdateDriverFatigue(0, 50, 50, Normal);
                double conserve = UpdateDriverFatigue(0, 50, 50, Conserve);
                Assert.That(push, Is.GreaterThan(normal));
                Assert.That(normal, Is.GreaterThan(conserve));
            }

            [Test]
            public void HigherCarComfortReducesFatigueBuildup()
            {
                Assert.That(UpdateDriverFatigue(0, 100, 50, Normal), Is.LessThan(UpdateDriverFatigue(0, 0, 50, Normal)));
            }

            [Test]
            public void HigherDriverStaminaReducesFatigueBuildup()
            {
                Assert.That(UpdateDriverFatigue(0, 50, 100, Normal), Is.LessThan(UpdateDriverFatigue(0, 50, 0, Normal)));
            }

            [Test]
            public void ClampsTo100WhenFatigueWouldExceedMaximum()
            {
                Assert.That(UpdateDriverFatigue(98, 0, 0, Push), Is.EqualTo(100));
            }

            [Test]
            public void StaysAt100WhenAlreadyFullyFatigued()
            {
                Assert.That(UpdateDriverFatigue(100, 50, 50, Normal), Is.EqualTo(100));
            }

            [Test]
            public void FatigueBuildsEvenWithMaximumComfortAndStamina()
            {
                Assert.That(UpdateDriverFatigue(0, 100, 100, Conserve), Is.GreaterThan(0));
            }
        }

        public class UpdateCarConditionTests
        {
            [Test]
            public void DecreasesConditionEachLapOnNormalMode()
            {
                Assert.That(UpdateCarCondition(100, 0, 0, Normal), Is.LessThan(100));
            }

            [Test]
            public void PushModeDegradesConditionFasterThanNormalConserveSlower()
            {
                double push = UpdateCarCondition(100, 0, 0, Push);
                double normal = UpdateCarCondition(100, 0, 0, Normal);
                double conserve = UpdateCarCondition(100, 0, 0, Conserve);
                Assert.That(push, Is.LessThan(normal));
                Assert.That(normal, Is.LessThan(conserve));
            }

            [Test]
            public void OlderCarsDegradeFaster()
            {
                Assert.That(UpdateCarCondition(100, 20, 0, Normal), Is.LessThan(UpdateCarCondition(100, 0, 0, Normal)));
            }

            [Test]
            public void HigherEngineerSkillSlowsConditionDecay()
            {
                Assert.That(UpdateCarCondition(100, 5, 20, Normal), Is.GreaterThan(UpdateCarCondition(100, 5, 0, Normal)));
            }

            [Test]
            public void ClampsTo0WhenConditionWouldGoBelowZero()
            {
                Assert.That(UpdateCarCondition(0.5, 20, 0, Push), Is.EqualTo(0));
            }

            [Test]
            public void StaysAt0WhenAlreadyFullyDegraded()
            {
                Assert.That(UpdateCarCondition(0, 0, 0, Normal), Is.EqualTo(0));
            }

            [Test]
            public void ConditionAlwaysDecaysEvenAtAge0WithMaxEngineerSkill()
            {
                Assert.That(UpdateCarCondition(100, 0, 20, Conserve), Is.LessThan(100));
            }

            [Test]
            public void EdgeCaseAge0NoEngineerSkillNormalModeStaysInValidRange()
            {
                double result = UpdateCarCondition(100, 0, 0, Normal);
                Assert.That(result, Is.GreaterThanOrEqualTo(0));
                Assert.That(result, Is.LessThanOrEqualTo(100));
            }

            [Test]
            public void AgeFactorIsCappedAge30AndAge20ProduceIdenticalDecay()
            {
                // MaxAgeFactor is reached at age 20; any older car should degrade at the same rate.
                double at20 = UpdateCarCondition(100, 20, 0, Normal);
                double at30 = UpdateCarCondition(100, 30, 0, Normal);
                Assert.That(at20, Is.EqualTo(at30).Within(5e-11));
            }
        }
    }
}
