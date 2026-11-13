#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class RaceWeekendTests
    {
        private static readonly Func<double> Never = () => 0.999;

        private static Func<double> Seeded(int seed)
        {
            var rng = new Random(seed);
            return rng.NextDouble;
        }

        /// <summary>A new career with the player owning one car and one hired driver.</summary>
        private static GameState Career(bool playerCar = true)
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions
            {
                PlayerName = "Ann Owner",
                TeamName = "Us",
                Skills = new PlayerSkills { Driver = 10, Engineer = 5 },
            }, Seeded(1));
            if (!playerCar) return game;
            game = GameActions.BuyCar(game, new CarInstance { Id = "mine", ModelId = "d-12", Condition = 90 }, 0, "car");
            Driver free = game.Drivers.First(d => !game.Contracts.Any(c => c.DriverId == d.Id));
            game = GameActions.HireDriver(game, free.Id, free.Name, 1, 0);
            return game with
            {
                Teams = game.Teams.Select(t => t is PlayerTeam p ? p with { TyreSets = 2, SpareParts = 3, CrewSize = 4, Budget = 100_000 } : t).ToArray(),
            };
        }

        private static string Hired(GameState game) => game.Contracts.First(c => c.TeamId == GameActions.PlayerTeamId).DriverId;

        // --- Grid ---

        [Test]
        public void GridHasEveryTeamWithACarOrderedByPrestige()
        {
            RaceSetup setup = RaceWeekend.BuildSetup(Career());
            Assert.That(setup.Grid, Has.Count.EqualTo(100));
            for (int i = 1; i < setup.Grid.Count; i++)
                Assert.That(setup.Grid[i].Prestige, Is.LessThanOrEqualTo(setup.Grid[i - 1].Prestige));
            // The player starts on 0 prestige, so at the back.
            Assert.That(setup.Grid[setup.Grid.Count - 1].Player, Is.True);
        }

        [Test]
        public void PlayerWithoutACarDoesNotRace()
        {
            RaceSetup setup = RaceWeekend.BuildSetup(Career(playerCar: false));
            Assert.That(setup.Grid, Has.Count.EqualTo(99));
            Assert.That(setup.Grid.Any(g => g.Player), Is.False);
        }

        [Test]
        public void EveryCarStartsOnAFullTank()
        {
            GameState game = Career();
            foreach (GridCar car in RaceWeekend.BuildSetup(game).Grid)
                Assert.That(car.StartingFuel, Is.EqualTo(EffectiveStats.CalculateEffectiveStats(car.Car, car.Model).FuelCapacity));
        }

        [Test]
        public void PlayerLineupFollowsTheGivenOrder()
        {
            GameState game = Career();
            string hired = Hired(game);
            GridCar player = RaceWeekend.BuildSetup(game, new[] { hired, RaceEntry.PlayerCharacterDriverId }).Grid.First(g => g.Player);
            Assert.That(player.Lineup.Select(d => d.Id), Is.EqualTo(new[] { hired, RaceEntry.PlayerCharacterDriverId }));

            GridCar standard = RaceWeekend.BuildSetup(game).Grid.First(g => g.Player);
            Assert.That(standard.Lineup[0].Id, Is.EqualTo(RaceEntry.PlayerCharacterDriverId));
        }

        [Test]
        public void WithNoLineupGiven_TheCareersStoredOrderIsUsed()
        {
            GameState game = Career();
            string hired = Hired(game);
            game = GameActions.SetLineup(game, new[] { hired, RaceEntry.PlayerCharacterDriverId });
            GridCar player = RaceWeekend.BuildSetup(game).Grid.First(g => g.Player);
            Assert.That(player.Lineup.Select(d => d.Id), Is.EqualTo(new[] { hired, RaceEntry.PlayerCharacterDriverId }));
        }

        [Test]
        public void DriversLeftOutOfTheLineupFollowTheNamedOnes()
        {
            GameState game = Career();
            GridCar player = RaceWeekend.BuildSetup(game, new[] { Hired(game) }).Grid.First(g => g.Player);
            Assert.That(player.Lineup.Select(d => d.Id), Is.EqualTo(new[] { Hired(game), RaceEntry.PlayerCharacterDriverId }));
        }

        [Test]
        public void GridCarriesTheTeamsStock()
        {
            GridCar player = RaceWeekend.BuildSetup(Career()).Grid.First(g => g.Player);
            Assert.That(player.TyreSets, Is.EqualTo(2));
            Assert.That(player.SpareParts, Is.EqualTo(3));
            Assert.That(player.CrewSize, Is.EqualTo(4));
            Assert.That(player.EngineerSkill, Is.EqualTo(5));
            Assert.That(player.TeamName, Is.EqualTo("Us"));
        }

        // --- Laps ---

        private static (RaceDay Race, CarRaceLog Player) PlayerRace(Func<double> random)
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(Career()), random);
            return (race, race.Cars.First(c => c.Car.Player));
        }

        [Test]
        public void AStopFixesOnlyTheIssuesItIsGiven()
        {
            (RaceDay _, CarRaceLog me) = PlayerRace(Never);
            me.AddIssue("engine-misfire");
            me.AddIssue("radiator-leak");
            double both = me.WorkSeconds();
            double one = me.WorkSeconds(new[] { "radiator-leak" });
            Assert.That(one, Is.LessThan(both));
            Assert.That(me.WorkSeconds(System.Array.Empty<string>()), Is.LessThan(one));
            PitWork work = me.Pit(false, new[] { "radiator-leak" });
            Assert.That(work.ExtraSeconds, Is.EqualTo(one).Within(1e-9));
            Assert.That(me.ActiveIssues.Select(i => i.TemplateId), Is.EqualTo(new[] { "engine-misfire" }));
            Assert.That(me.SpareParts, Is.EqualTo(2));
        }

        [Test]
        public void AStopWithNoListFixesEveryIssue()
        {
            (RaceDay _, CarRaceLog me) = PlayerRace(Never);
            me.AddIssue("engine-misfire");
            me.AddIssue("radiator-leak");
            me.Pit(false);
            Assert.That(me.ActiveIssues, Is.Empty);
        }

        [Test]
        public void AnIssueGivenAndFixedFromTheDebugMenu()
        {
            (RaceDay _, CarRaceLog me) = PlayerRace(Never);
            me.AddIssue("engine-misfire");
            me.AddIssue("engine-misfire");
            Assert.That(me.ActiveIssues.Count, Is.EqualTo(1));
            Assert.That(me.Effects.Power, Is.EqualTo(0.9).Within(1e-9));
            me.ClearIssues();
            Assert.That(me.ActiveIssues, Is.Empty);
            Assert.That(me.Effects.Power, Is.EqualTo(1));
            Assert.That(me.Issues.Single().FixedLap, Is.EqualTo(0));
        }

        [Test]
        public void FatigueSetFromTheDebugMenuStaysInRange()
        {
            (RaceDay _, CarRaceLog me) = PlayerRace(Never);
            me.SetFatigue(150);
            Assert.That(me.Fatigue, Is.EqualTo(100));
            me.SetFatigue(-5);
            Assert.That(me.Fatigue, Is.EqualTo(0));
        }

        [Test]
        public void ALapAgesTheCar()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            Assert.That(me.Laps, Is.EqualTo(1));
            Assert.That(me.Condition, Is.LessThan(90));
            Assert.That(me.BestLap, Is.EqualTo(90));
            Assert.That(me.Retired, Is.False);
        }

        [Test]
        public void AFailureRetiresTheCar()
        {
            // A failure (first roll low) that isn't a crash (second roll high).
            var rolls = new Queue<double>(new[] { 0.0, 0.9 });
            (RaceDay race, CarRaceLog me) = PlayerRace(() => rolls.Count > 0 ? rolls.Dequeue() : 0.999);
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            Assert.That(me.Retired, Is.True);
            Assert.That(me.RetirementReason, Is.EqualTo(FailureType.Mechanical));
            race.CompleteLap(me.Index, 180, 90, InstructionMode.Normal, false);
            Assert.That(me.Laps, Is.EqualTo(1));
        }

        [Test]
        public void AnIssueDebuffsTheCarUntilFixed()
        {
            // No failure (first roll high), then an issue (second roll low), then pick the first template.
            var rolls = new Queue<double>(new[] { 0.999, 0.0, 0.0 });
            (RaceDay race, CarRaceLog me) = PlayerRace(() => rolls.Count > 0 ? rolls.Dequeue() : 0.999);
            IReadOnlyList<ActiveIssue> found = race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            Assert.That(found, Has.Count.EqualTo(1));
            IssueTemplate template = IssueTemplates.AllIssueTemplates.First(t => t.Id == found[0].TemplateId);
            IssueEffects e = me.Effects;
            double expected = template.StatDebuffs.TryGetValue(CarStatKey.Power, out double cut) ? 1 - cut : 1;
            Assert.That(e.Power, Is.EqualTo(expected).Within(1e-9));

            PitWork work = me.Pit(true);
            Assert.That(me.ActiveIssues, Is.Empty);
            Assert.That(me.SpareParts, Is.EqualTo(3 - template.SparePartsCost));
            Assert.That(me.Issues[0].FixedLap, Is.EqualTo(1));
            Assert.That(work.ExtraSeconds, Is.GreaterThan(0));
            Assert.That(me.Effects.Power, Is.EqualTo(1));
        }

        [Test]
        public void IssuesWaitWhenThereAreNoSpares()
        {
            var rolls = new Queue<double>(new[] { 0.999, 0.0, 0.0 });
            var setup = RaceWeekend.BuildSetup(Career());
            setup = setup with { Grid = setup.Grid.Select(g => g.Player ? g with { SpareParts = 0 } : g).ToArray() };
            var race = new RaceDay(setup, () => rolls.Count > 0 ? rolls.Dequeue() : 0.999);
            CarRaceLog me = race.Cars.First(c => c.Car.Player);
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            me.Pit(false);
            Assert.That(me.ActiveIssues, Has.Count.EqualTo(1));
            Assert.That(me.Issues[0].FixedLap, Is.Null);
        }

        // --- Fatigue ---

        [Test]
        public void FatigueBuildsWithTimeInTheCar()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            Assert.That(me.Fatigue, Is.EqualTo(0));
            double perLap = Degradation.UpdateDriverFatigue(0, me.Stats.Comfort, me.Driver.Stats.Stamina, InstructionMode.Normal);
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            double half = me.Fatigue;
            // Half the race maths' lap in the car: half its fatigue.
            Assert.That(half, Is.EqualTo(perLap / 2).Within(1e-9));
            race.CompleteLap(me.Index, 270, 180, InstructionMode.Normal, false);
            // A lap twice as long tires the driver about twice as much (the worn car is a touch less comfortable).
            Assert.That(me.Fatigue - half, Is.EqualTo(2 * half).Within(0.02 * half));
        }

        [Test]
        public void PushingTiresTheDriverFaster()
        {
            (RaceDay a, CarRaceLog normal) = PlayerRace(Never);
            (RaceDay b, CarRaceLog push) = PlayerRace(Never);
            a.CompleteLap(normal.Index, 180, 180, InstructionMode.Normal, false);
            b.CompleteLap(push.Index, 180, 180, InstructionMode.Push, false);
            Assert.That(push.Fatigue, Is.GreaterThan(normal.Fatigue));
        }

        [Test]
        public void FatigueStopsAtOneHundred()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            race.CompleteLap(me.Index, 1e6, 1e6, InstructionMode.Push, false);
            Assert.That(me.Fatigue, Is.EqualTo(100));
        }

        [Test]
        public void ADriverChangeBringsAFreshDriver()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            race.CompleteLap(me.Index, 1800, 1800, InstructionMode.Normal, false);
            Assert.That(me.Fatigue, Is.GreaterThan(0));
            me.Pit(true);
            Assert.That(me.Fatigue, Is.EqualTo(0));
        }

        [Test]
        public void StayingInTheCarKeepsTheFatigue()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            race.CompleteLap(me.Index, 1800, 1800, InstructionMode.Normal, false);
            double tired = me.Fatigue;
            me.NextDriverIndex = me.DriverIndex;
            me.Pit(true);
            Assert.That(me.Fatigue, Is.EqualTo(tired));
        }

        // --- Stops ---

        [Test]
        public void StopsRotateTheLineupAndUseTyreSets()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            string first = me.Driver.Id;
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, false);
            PitWork stop1 = me.Pit(true);
            Assert.That(stop1.ChangeTyres, Is.True);
            Assert.That(stop1.NewDriver, Is.Not.Null);
            Assert.That(me.Driver.Id, Is.Not.EqualTo(first));
            me.PitDone(30, 40);
            Assert.That(me.FuelAdded, Is.EqualTo(40));
            Assert.That(me.PitStops[0].Seconds, Is.EqualTo(30));

            me.Pit(true);
            Assert.That(me.Driver.Id, Is.EqualTo(first));
            Assert.That(me.TyreSets, Is.EqualTo(0));
            Assert.That(me.Pit(true).ChangeTyres, Is.False);
        }

        [Test]
        public void WhoIsNextCanBeChangedAtAStop()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            me.NextDriverIndex = 0;
            PitWork work = me.Pit(false);
            Assert.That(work.NewDriver, Is.Null);
            Assert.That(me.PitStops[0].DriverIn, Is.EqualTo(me.PitStops[0].DriverOut));
        }

        [Test]
        public void StintsCloseAtEachChange()
        {
            (RaceDay race, CarRaceLog me) = PlayerRace(Never);
            for (int lap = 1; lap <= 3; lap++) race.CompleteLap(me.Index, lap * 90, 90 - lap, InstructionMode.Normal, false);
            me.Pit(true);
            for (int lap = 4; lap <= 5; lap++) race.CompleteLap(me.Index, lap * 90, 90, InstructionMode.Normal, false);
            Assert.That(me.Stints, Has.Count.EqualTo(2));
            Assert.That(me.Stints[0].StartLap, Is.EqualTo(1));
            Assert.That(me.Stints[0].EndLap, Is.EqualTo(3));
            Assert.That(me.Stints[1].StartLap, Is.EqualTo(4));
            Assert.That(me.Stints[1].EndLap, Is.EqualTo(5));
        }

        // --- Classification ---

        [Test]
        public void ClassificationIsLapsThenTimeAcrossTheLine()
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(Career()), Never);
            race.CompleteLap(5, 100, 100, InstructionMode.Normal, false);
            race.CompleteLap(3, 90, 90, InstructionMode.Normal, false);
            race.CompleteLap(3, 180, 90, InstructionMode.Normal, false);
            IReadOnlyList<CarRaceLog> order = race.Classify();
            Assert.That(order[0].Index, Is.EqualTo(3));
            Assert.That(order[1].Index, Is.EqualTo(5));
            Assert.That(order[2].Index, Is.EqualTo(0));
        }

        [Test]
        public void PositionsAreRecordedEachLeaderLap()
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(Career()), Never);
            race.CompleteLap(2, 90, 90, InstructionMode.Normal, false);
            race.CompleteLap(1, 91, 91, InstructionMode.Normal, false);
            race.CompleteLap(1, 180, 89, InstructionMode.Normal, false);
            Assert.That(race.PositionHistory, Has.Count.EqualTo(2));
            Assert.That(race.PositionHistory[0][2], Is.EqualTo(1));
            Assert.That(race.PositionHistory[1][1], Is.EqualTo(1));
            Assert.That(race.PositionHistory[1][2], Is.EqualTo(2));
        }

        [Test]
        public void TheFlagEndsEachCarsRace()
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(Career()), Never);
            Assert.That(race.AllFlagged, Is.False);
            foreach (CarRaceLog c in race.Cars) race.CompleteLap(c.Index, 100 + c.Index, 100, InstructionMode.Normal, true);
            Assert.That(race.AllFlagged, Is.True);
            race.CompleteLap(0, 300, 100, InstructionMode.Normal, false);
            Assert.That(race.Cars[0].Laps, Is.EqualTo(1));
        }

        // --- Settling ---

        private static RaceDay RunRace(GameState game)
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(game), Never);
            // Every car does its grid position's worth fewer laps than pole, so the grid order holds.
            for (int lap = 1; lap <= 3; lap++)
                foreach (CarRaceLog c in race.Cars)
                    race.CompleteLap(c.Index, lap * 100 + c.Index, 100, InstructionMode.Normal, lap == 3);
            return race;
        }

        [Test]
        public void SettlingPaysPrizesBillsFuelAndMovesToPostRace()
        {
            GameState game = Career();
            RaceDay race = RunRace(game);
            CarRaceLog me = race.Cars.First(c => c.Car.Player);
            me.PitDone(0, 0);
            me.FuelLeft = me.Car.StartingFuel - 50;

            (GameState after, RaceSummary summary) = RaceWeekend.Settle(race);
            Assert.That(after.Phase, Is.EqualTo(GamePhase.PostRace));
            Assert.That(after.RaceHistory, Has.Count.EqualTo(1));
            Assert.That(after.RaceHistory[0].Results, Has.Count.EqualTo(100));
            Assert.That(after.RaceHistory[0].PositionHistory, Has.Count.EqualTo(3));

            BaseTeam pole = after.Teams.First(t => t.Id == race.Cars[0].Car.TeamId);
            BaseTeam poleBefore = game.Teams.First(t => t.Id == pole.Id);
            Assert.That(pole.Budget - poleBefore.Budget, Is.EqualTo(PostRace.PrizeTable[0]));

            PlayerTeam player = after.Teams.OfType<PlayerTeam>().Single();
            double prize = PostRace.PrizeTable[99];
            double fuel = 50 * game.EconomyConfig.FuelConfig.CostPerLitre;
            Assert.That(player.Budget, Is.EqualTo(100_000 + prize - fuel));
            Assert.That(player.Transactions.Any(t => t.Category == TransactionCategory.FuelCost && t.Amount == -fuel), Is.True);
            Assert.That(summary.FuelUsed, Is.EqualTo(50));
            Assert.That(summary.Player!.Position, Is.EqualTo(100));
            Assert.That(summary.PositionByLap, Is.EqualTo(new[] { 100, 100, 100 }));
        }

        [Test]
        public void SettlingWritesBackConditionAndStock()
        {
            GameState game = Career();
            var race = new RaceDay(RaceWeekend.BuildSetup(game), Never);
            CarRaceLog me = race.Cars.First(c => c.Car.Player);
            race.CompleteLap(me.Index, 90, 90, InstructionMode.Normal, true);
            me.Pit(true);

            (GameState after, RaceSummary summary) = RaceWeekend.Settle(race);
            PlayerTeam player = after.Teams.OfType<PlayerTeam>().Single();
            Assert.That(player.TyreSets, Is.EqualTo(1));
            Assert.That(player.Cars.First(c => c.Id == "mine").Condition, Is.EqualTo(me.Condition).And.LessThan(90));
            Assert.That(summary.TyreSetsUsed, Is.EqualTo(1));
            Assert.That(summary.PitStops, Has.Count.EqualTo(1));
        }

        [Test]
        public void SettlingRecalculatesPrestige()
        {
            GameState game = Career();
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(RunRace(game));
            foreach (BaseTeam t in after.Teams)
            {
                double expected = PostRace.CalculatePrestige(PostRace.ExtractTeamResults(t.Id, after.RaceHistory));
                Assert.That(t.Prestige, Is.EqualTo(expected));
                Assert.That(t.PrestigeHistory.Last(), Is.EqualTo(expected));
            }
            Assert.That(summary.PrestigeAfter, Is.EqualTo(0));
        }

        [Test]
        public void SummaryNamesTheDriversInEachStint()
        {
            GameState game = Career();
            RaceDay race = RunRace(game);
            RaceSummary summary = RaceWeekend.Settle(race).Summary;
            Assert.That(summary.Stints, Has.Count.EqualTo(1));
            Assert.That(summary.Stints[0].DriverName, Is.EqualTo("Ann Owner"));
            Assert.That(summary.Stints[0].BestLap, Is.EqualTo(100));
            Assert.That(summary.Classification.Select(c => c.Position), Is.EqualTo(Enumerable.Range(1, 100)));
        }

        [Test]
        public void BeginAndFinishHandTheRaceOver()
        {
            GameState game = Career();
            int fired = 0;
            Action count = () => fired++;
            RaceWeekend.Finished += count;
            try
            {
                RaceWeekend.CanSkipToEnd = true;
                RaceSetup setup = RaceWeekend.Begin(game);
                Assert.That(RaceWeekend.CanSkipToEnd, Is.False, "a new race can't skip to its end");
                Assert.That(RaceWeekend.Setup, Is.SameAs(setup));
                var race = new RaceDay(setup, Never);
                RaceWeekend.CanSkipToEnd = true;
                RaceWeekend.Finish(race);
                Assert.That(RaceWeekend.CanSkipToEnd, Is.False, "a settled race has nothing left to skip");
                Assert.That(fired, Is.EqualTo(1));
                Assert.That(RaceWeekend.Setup, Is.Null);
                Assert.That(RaceWeekend.Settled!.Phase, Is.EqualTo(GamePhase.PostRace));
                Assert.That(RaceWeekend.Summary, Is.Not.Null);
            }
            finally
            {
                RaceWeekend.Finished -= count;
            }
        }

        [Test]
        public void SkipToEndAsksTheRaceScene()
        {
            int fired = 0;
            Action count = () => fired++;
            RaceWeekend.SkippingToEnd += count;
            try
            {
                RaceWeekend.SkipToEnd();
                Assert.That(fired, Is.EqualTo(1));
            }
            finally
            {
                RaceWeekend.SkippingToEnd -= count;
            }
        }
    }
}
