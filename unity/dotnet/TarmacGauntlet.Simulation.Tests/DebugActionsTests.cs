#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class DebugActionsTests
    {
        private static GameState Career()
        {
            var random = new Random(1);
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann Owner", TeamName = "Us" }, random.NextDouble);
            game = DebugActions.GiveCar(game, "d-12");
            return DebugActions.Hire(game, DebugActions.FreeAgents(game)[0].Driver.Id);
        }

        private static PlayerTeam Player(GameState game) => game.Teams.OfType<PlayerTeam>().Single();

        [TestCase(1)]
        [TestCase(50)]
        [TestCase(1000)]
        public void ASkippedRaceFinishesThePlayerWhereAsked(int place)
        {
            RaceDay day = DebugActions.Race(Career(), place);
            var order = day.Classify();
            int expected = Math.Min(place, day.Cars.Count);
            Assert.That(order[expected - 1].Car.Player, Is.True);
            Assert.That(day.AllFlagged, Is.True);
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(day);
            Assert.That(after.Phase, Is.EqualTo(GamePhase.PostRace));
            Assert.That(summary.Player!.Position, Is.EqualTo(expected));
        }

        [Test]
        public void ASkippedRaceCanRetireThePlayer()
        {
            RaceDay day = DebugActions.Race(Career(), 1, retire: true);
            CarRaceLog me = day.Cars.Single(c => c.Car.Player);
            Assert.That(me.Retired, Is.True);
            Assert.That(me.Laps, Is.EqualTo(DebugActions.RaceLaps / 2));
            Assert.That(day.AllFlagged, Is.True);
        }

        [Test]
        public void AYearEndsWithoutARace()
        {
            GameState game = Career();
            GameState next = DebugActions.EndYear(game, new Random(2).NextDouble);
            Assert.That(next.CurrentYear, Is.EqualTo(game.CurrentYear + 1));
            Assert.That(next.Phase, Is.EqualTo(GamePhase.PreRace));
        }

        [Test]
        public void AReadyMadeCareer()
        {
            var random = new Random(3);
            GameState game = GameInit.InitializeGame(new NewGameOptions { TeamName = "Us" }, random.NextDouble);
            game = DebugActions.Equip(DebugActions.ToYear(game, 5, random.NextDouble), CarClass.B, 3, 12, 2_000_000, 10);
            PlayerTeam p = Player(game);
            Assert.That(game.CurrentYear, Is.EqualTo(5));
            Assert.That(CarModels.All.First(m => m.Id == p.Cars.Single().ModelId).CarClass, Is.EqualTo(CarClass.B));
            Assert.That(game.Contracts.Count(c => c.TeamId == GameActions.PlayerTeamId), Is.EqualTo(3));
            Assert.That(p.Budget, Is.EqualTo(2_000_000));
            Assert.That(RaceWeekend.BuildSetup(game).Grid.Any(g => g.Player), Is.True);
        }

        [Test]
        public void AFieldCutDownKeepsThePlayer()
        {
            RaceSetup full = RaceWeekend.BuildSetup(Career());
            RaceSetup ten = DebugActions.Field(full, 10, "a-29");
            Assert.That(ten.Grid, Has.Count.EqualTo(10));
            GridCar me = ten.Grid.Single(g => g.Player);
            Assert.That(me.Model.Id, Is.EqualTo("a-29"));
            Assert.That(me.Car.ModelId, Is.EqualTo("a-29"));
            Assert.That(DebugActions.Field(full, 1, null).Grid.Single().Player, Is.True);
            Assert.That(DebugActions.Field(full, 0, null).Grid, Has.Count.EqualTo(full.Grid.Count));
        }

        [Test]
        public void TheCareerChanges()
        {
            GameState game = Career();
            Assert.That(Player(game).Cars.Single().ModelId, Is.EqualTo("d-12"));
            Assert.That(Player(game).EnteredCarId, Is.EqualTo(Player(game).Cars.Single().Id));
            Assert.That(game.Contracts.Count(c => c.TeamId == GameActions.PlayerTeamId), Is.EqualTo(1));

            game = DebugActions.SetMoney(DebugActions.AddMoney(game, 5), 0);
            Assert.That(Player(game).Budget, Is.EqualTo(0));
            game = DebugActions.SetCondition(game, 140);
            Assert.That(Player(game).Cars.Single().Condition, Is.EqualTo(100));
            game = DebugActions.ReleaseAll(game);
            Assert.That(game.Contracts.Any(c => c.TeamId == GameActions.PlayerTeamId), Is.False);
            game = DebugActions.MaxSkills(DebugActions.SetCrew(DebugActions.AddSpares(DebugActions.AddTyres(game, 10), 10), 8));
            Assert.That(Player(game).TyreSets, Is.GreaterThanOrEqualTo(10));
            Assert.That(Player(game).CrewSize, Is.EqualTo(8));
            Assert.That(Player(game).Skills.Business, Is.EqualTo(20));
        }
    }
}
