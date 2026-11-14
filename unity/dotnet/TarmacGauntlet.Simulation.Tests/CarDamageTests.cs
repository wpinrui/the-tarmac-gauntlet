#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class CarDamageTests
    {
        /// <summary>The player's car on a fresh grid: condition 90, 3 spare parts, 4 crew.</summary>
        private static CarRaceLog PlayerLog(int spares = 3)
        {
            var rng = new Random(1);
            GameState game = GameInit.InitializeGame(new NewGameOptions
            {
                PlayerName = "Ann Owner",
                TeamName = "Us",
                Skills = new PlayerSkills { Driver = 10, Engineer = 5 },
            }, rng.NextDouble);
            game = GameActions.BuyCar(game, new CarInstance { Id = "mine", ModelId = "d-12", Condition = 90 }, 0, "car");
            Driver free = game.Drivers.First(d => !game.Contracts.Any(c => c.DriverId == d.Id));
            game = GameActions.HireDriver(game, free.Id, free.Name, 1, 0);
            game = game with
            {
                Teams = game.Teams.Select(t => t is PlayerTeam p ? p with { TyreSets = 2, SpareParts = spares, CrewSize = 4, Budget = 100_000 } : t).ToArray(),
            };
            var day = new RaceDay(RaceWeekend.BuildSetup(game), () => 0.999);
            return day.Cars.Single(c => c.Car.Player);
        }

        private static readonly StintOutlook LongStint = new StintOutlook { Laps = 12, LapSeconds = 120, CrewMultiplier = 1 };
        private static readonly StintOutlook ShortStint = new StintOutlook { Laps = 1, LapSeconds = 120, CrewMultiplier = 1 };

        [Test]
        public void ATouchDoesNoDamage()
        {
            Assert.That(CarDamage.FromImpact(CarDamage.MinImpact), Is.EqualTo(0));
            Assert.That(CarDamage.FromImpact(4), Is.EqualTo(0));
            Assert.That(CarDamage.FromImpact(18), Is.EqualTo(7.8).Within(1e-9));
        }

        [Test]
        public void DamageCostsGripAndAddsDrag()
        {
            Assert.That(CarDamage.Grip(0), Is.EqualTo(1));
            Assert.That(CarDamage.Grip(20), Is.EqualTo(1 - 20 * CarDamage.GripLossPerPoint).Within(1e-12));
            Assert.That(CarDamage.Grip(100), Is.EqualTo(CarDamage.MinGrip));
            Assert.That(CarDamage.Drag(0), Is.EqualTo(1));
            Assert.That(CarDamage.Drag(20), Is.EqualTo(1 + 20 * CarDamage.DragPerPoint).Within(1e-12));
            // A heavy hit (30 m/s) costs a lap several per cent; a light one (8 m/s) about one.
            Assert.That(CarDamage.LapSlowdown(CarDamage.FromImpact(30)), Is.InRange(0.07, 0.11));
            Assert.That(CarDamage.LapSlowdown(CarDamage.FromImpact(8)), Is.InRange(0.005, 0.02));
        }

        [Test]
        public void TheEndThatWasHitLosesMostGrip()
        {
            Assert.That(CarDamage.AxleGrip(16, 1, true), Is.LessThan(CarDamage.AxleGrip(16, 1, false)));
            Assert.That(CarDamage.AxleGrip(16, 0, true), Is.GreaterThan(CarDamage.AxleGrip(16, 0, false)));
            Assert.That(CarDamage.AxleGrip(16, 0.5, true), Is.EqualTo(CarDamage.Grip(16)).Within(1e-12));
            Assert.That(CarDamage.FrontShare(2, 2), Is.EqualTo(1));
            Assert.That(CarDamage.FrontShare(-2, 2), Is.EqualTo(0));
            Assert.That(CarDamage.FrontShare(0, 2), Is.EqualTo(0.5));
        }

        [Test]
        public void HitsKeepTrackOfWhichEndWasHit()
        {
            CarRaceLog log = PlayerLog();
            log.Hit(10, 13, front: 1);
            log.Hit(20, 13, front: 0.5);
            Assert.That(log.DamageFront, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(log.AxleGrip(true), Is.LessThan(log.AxleGrip(false)));
        }

        [Test]
        public void TheCrewRepairHeavyDamageMoreThanLight()
        {
            double heavy = CarDamage.CrewTarget(30, LongStint);
            Assert.That(heavy, Is.LessThan(30));
            Assert.That(CarDamage.CrewTarget(30, ShortStint), Is.GreaterThan(heavy));
            Assert.That(CarDamage.Target(RepairMode.All, 80, ShortStint), Is.EqualTo(0));
            Assert.That(CarDamage.Target(RepairMode.None, 80, LongStint), Is.EqualTo(80));
        }

        [Test]
        public void HitsAddUpAndCostGrip()
        {
            CarRaceLog log = PlayerLog();
            Assert.That(log.Hit(10, 10), Is.False);
            Assert.That(log.Hit(20, 10), Is.False);
            Assert.That(log.Damage, Is.EqualTo(2 * CarDamage.FromImpact(10)).Within(1e-9));
            Assert.That(log.AxleGrip(true), Is.LessThan(1));
            Assert.That(log.Effects.Handling, Is.EqualTo(1));
            Assert.That(log.Retired, Is.False);
        }

        [Test]
        public void AHugeHitTotalsTheCarAndItGoesBackAtZero()
        {
            CarRaceLog log = PlayerLog();
            Assert.That(log.Hit(10, CarDamage.TotalImpact + 1), Is.True);
            Assert.That(log.Retired, Is.True);
            Assert.That(log.RetirementReason, Is.EqualTo(FailureType.Crash));
            Assert.That(log.ConditionAfterRace, Is.EqualTo(0));
        }

        [Test]
        public void DamageLeftAtTheFlagComesOffTheCondition()
        {
            CarRaceLog log = PlayerLog();
            log.SetDamage(30);
            Assert.That(log.ConditionAfterRace, Is.EqualTo(log.Condition - 30).Within(1e-9));
        }

        [Test]
        public void ARepairTakesLongerTheMoreDamageAndOnlyWhatTheSparesAllow()
        {
            CarRaceLog small = PlayerLog(spares: 10);
            small.SetDamage(20);
            CarRaceLog big = PlayerLog(spares: 10);
            big.SetDamage(60);
            Assert.That(big.WorkSeconds(Array.Empty<string>()), Is.GreaterThan(small.WorkSeconds(Array.Empty<string>())));

            CarRaceLog shortOfParts = PlayerLog(spares: 3);
            shortOfParts.SetDamage(60);
            shortOfParts.Pit(false, Array.Empty<string>());
            Assert.That(shortOfParts.Damage, Is.EqualTo(60 - CarDamage.PointsFrom(3)).Within(1e-9));
            Assert.That(shortOfParts.SpareParts, Is.EqualTo(0));
        }

        [Test]
        public void APartialRepairLeavesWhatWasAskedFor()
        {
            CarRaceLog log = PlayerLog(spares: 10);
            log.SetDamage(50);
            log.Pit(false, Array.Empty<string>(), damageTo: 25);
            Assert.That(log.Damage, Is.EqualTo(25).Within(1e-9));
            Assert.That(log.SpareParts, Is.EqualTo(10 - CarDamage.PartsFor(25)));
        }

        [Test]
        public void CarsWithNoPhysicsTakeAFewHitsAnHourAndRarelyATotal()
        {
            var rng = new Random(3);
            int hits = 0, totals = 0;
            const int hours = 2000;
            for (int s = 0; s < hours * 3600; s++)
                if (CarDamage.RollHit(1, rng.NextDouble) is double impact)
                {
                    hits++;
                    Assert.That(impact, Is.GreaterThanOrEqualTo(CarDamage.MinImpact));
                    if (CarDamage.Totals(impact)) totals++;
                }
            Assert.That(hits / (double)hours, Is.EqualTo(CarDamage.HitsPerHour).Within(0.2));
            Assert.That(totals / (double)hits, Is.LessThan(0.01));
            Assert.That(CarDamage.RollHit(0, () => 0), Is.Null);
        }

        [Test]
        public void DamageSurvivesASave()
        {
            CarRaceLog log = PlayerLog();
            log.Hit(5, 15);
            CarRaceLog again = PlayerLog();
            again.Load(log.Save());
            Assert.That(again.Damage, Is.EqualTo(log.Damage));
        }
    }
}
