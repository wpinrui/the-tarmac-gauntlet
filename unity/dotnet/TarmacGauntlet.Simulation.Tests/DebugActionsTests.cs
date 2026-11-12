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
