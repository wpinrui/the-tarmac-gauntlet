#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class RaceNumbersTests
    {
        private static AITeam Ai(string id, double prestige) => new AITeam { Id = id, Name = id, Prestige = prestige };

        private static Func<double> Seeded(int seed)
        {
            var rng = new Random(seed);
            return rng.NextDouble;
        }

        [Test]
        public void WithNoWinnerTheMostPrestigiousTeamIsNumberOne()
        {
            var teams = new BaseTeam[] { Ai("a", 10), Ai("b", 90), Ai("c", 50) };
            var numbers = RaceNumbers.Assign(teams, null).ToDictionary(t => t.Id, t => t.RaceNumber);
            Assert.That(numbers["b"], Is.EqualTo(1));
            Assert.That(numbers["c"], Is.EqualTo(2));
            Assert.That(numbers["a"], Is.EqualTo(3));
        }

        [Test]
        public void TheLastWinnerIsNumberOneAndTheRestFollowPrestige()
        {
            var teams = new BaseTeam[] { Ai("a", 10), Ai("b", 90), Ai("c", 50) };
            var numbers = RaceNumbers.Assign(teams, "a").ToDictionary(t => t.Id, t => t.RaceNumber);
            Assert.That(numbers["a"], Is.EqualTo(1));
            Assert.That(numbers["b"], Is.EqualTo(2));
            Assert.That(numbers["c"], Is.EqualTo(3));
        }

        [Test]
        public void EqualPrestigeIsOrderedByIdNotListOrder()
        {
            var one = RaceNumbers.Assign(new BaseTeam[] { Ai("y", 5), Ai("x", 5) }, null);
            var two = RaceNumbers.Assign(new BaseTeam[] { Ai("x", 5), Ai("y", 5) }, null);
            Assert.That(one.Single(t => t.Id == "x").RaceNumber, Is.EqualTo(1));
            Assert.That(two.Single(t => t.Id == "x").RaceNumber, Is.EqualTo(1));
        }

        [Test]
        public void WinnerOfReadsThatYearsOverallWinner()
        {
            var history = new[]
            {
                new RaceHistoryEntry { Year = 1, Results = new[] { new RaceResult { TeamId = "old", Position = 1 } } },
                new RaceHistoryEntry { Year = 2, Results = new[] { new RaceResult { TeamId = "p2", Position = 2 }, new RaceResult { TeamId = "new", Position = 1 } } },
            };
            Assert.That(RaceNumbers.WinnerOf(history, 2), Is.EqualTo("new"));
            Assert.That(RaceNumbers.WinnerOf(history, 3), Is.Null);
        }

        [Test]
        public void ANewGameNumbersAllHundredTeamsOneToHundred()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us" }, Seeded(1));
            Assert.That(game.Teams.Select(t => t.RaceNumber).OrderBy(n => n), Is.EqualTo(Enumerable.Range(1, 100)));
            Assert.That(game.Teams.Single(t => t.RaceNumber == 1).Prestige, Is.EqualTo(game.Teams.Max(t => t.Prestige)));
        }

        [Test]
        public void TheNextSeasonGivesLastYearsWinnerNumberOne()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us" }, Seeded(1));
            game = game with
            {
                Phase = GamePhase.PostRace,
                RaceHistory = new[] { new RaceHistoryEntry { Year = 1, Results = new[] { new RaceResult { TeamId = GameActions.PlayerTeamId, Position = 1 } } } },
            };
            GameState next = Season.AdvanceYear(game, Seeded(2));
            Assert.That(next.Teams.Single(t => t.RaceNumber == 1).Id, Is.EqualTo(GameActions.PlayerTeamId));
            Assert.That(next.Teams.Select(t => t.RaceNumber).OrderBy(n => n), Is.EqualTo(Enumerable.Range(1, 100)));
        }
    }
}
