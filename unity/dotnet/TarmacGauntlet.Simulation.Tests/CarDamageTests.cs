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
        /// <summary>A stint early in the race: most of the 24 hours, 8640 s at 10 times the clock, still to come.</summary>
        private static readonly StintOutlook EarlyStint = LongStint with { SecondsLeft = 8000 };
        private const double RaceMinute = 6;

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
            // A heavy hit (30 m/s) costs a lap about a fifth; a light one (8 m/s) a few per cent.
            Assert.That(CarDamage.LapSlowdown(CarDamage.FromImpact(30)), Is.InRange(0.15, 0.22));
            Assert.That(CarDamage.LapSlowdown(CarDamage.FromImpact(8)), Is.InRange(0.01, 0.04));
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
            double heavy = CarDamage.CrewTarget(30, EarlyStint);
            Assert.That(heavy, Is.LessThan(30));
            Assert.That(CarDamage.CrewTarget(30, ShortStint), Is.GreaterThan(heavy));
            Assert.That(CarDamage.CrewTarget(30, LongStint), Is.GreaterThan(heavy), "late in the race, with only the stint left, less is worth the hours");
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
        public void HitsThatPileUpPastTheLimitPutTheCarOut()
        {
            CarRaceLog log = PlayerLog();
            double impact = CarDamage.TotalImpact - 1;
            int hits = 0;
            while (!log.Hit(10 + hits, impact)) hits++;
            Assert.That(hits, Is.GreaterThan(0), "one hit under the total leaves the car running");
            Assert.That(log.Retired, Is.True);
            Assert.That(log.RetirementReason, Is.EqualTo(FailureType.Crash));
            Assert.That(log.Damage, Is.GreaterThanOrEqualTo(CarDamage.RetireDamage));
            Assert.That(log.Totalled, Is.False);
            Assert.That(log.ConditionAfterRace, Is.EqualTo(Math.Max(0, log.Condition - log.Damage)).Within(1e-9));
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
        public void RepairsTakeMinutesForAKnockAndHoursForAWreck()
        {
            Assert.That(CarDamage.RepairSeconds(0), Is.EqualTo(0));
            Assert.That(CarDamage.RepairSeconds(5), Is.EqualTo(5 * RaceMinute).Within(1e-9));
            Assert.That(CarDamage.RepairSeconds(2.5), Is.EqualTo(1.25 * RaceMinute).Within(1e-9));
            double last = 0;
            for (int p = 1; p <= 40; p++)
            {
                double point = CarDamage.RepairSeconds(p, p - 1);
                Assert.That(point, Is.GreaterThanOrEqualTo(last - 1e-9), $"point {p} costs less than the one below it");
                last = point;
            }
            Assert.That(CarDamage.RepairSeconds(CarDamage.FromImpact(30)), Is.EqualTo(60 * RaceMinute).Within(1e-9));
            Assert.That(CarDamage.RepairSeconds(CarDamage.RetireDamage), Is.EqualTo(180 * RaceMinute).Within(1e-9));
            Assert.That(CarDamage.RepairSeconds(60), Is.GreaterThan(180 * RaceMinute));
            Assert.That(CarDamage.RepairSeconds(30, 15) + CarDamage.RepairSeconds(15, 0), Is.EqualTo(CarDamage.RepairSeconds(30)).Within(1e-9));
            Assert.That(CarDamage.RepairSeconds(15, 20), Is.EqualTo(0));
        }

        [Test]
        public void AStopRepairingAHeavyHitTakesAnHourOfRaceTime()
        {
            CarRaceLog log = PlayerLog(spares: 10);
            log.SetDamage(15);
            double crew = PitStop.CrewMultiplier(log.Car.CrewSize, log.Car.EngineerSkill);
            double none = log.WorkSeconds(Array.Empty<string>(), damageTo: 15);
            Assert.That(log.WorkSeconds(Array.Empty<string>()) - none, Is.EqualTo(60 * RaceMinute * crew).Within(1e-6));
            Assert.That(log.Pit(false, Array.Empty<string>()).Repairs, Is.True);
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
        public void CarsWithNoPhysicsTakeAFewHitsAnHourAndAFewTotal()
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
            // A few hits in a hundred total the car: over a 24-hour race that's one car in seven or so.
            double share = totals / (double)hits;
            Assert.That(share, Is.InRange(0.01, 0.04));
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
