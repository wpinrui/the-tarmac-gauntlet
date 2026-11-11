#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class TeamNationalityTests
    {
        [Test]
        public void TheSameNameAlwaysGivesTheSameCode()
        {
            Assert.That(TeamNationality.FromName("Vega Racing"), Is.EqualTo(TeamNationality.FromName("Vega Racing")));
        }

        [Test]
        public void AiTeamsSpreadAcrossCountries()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us", Nationality = "sg" }, new Random(1).NextDouble);
            var codes = game.Teams.Where(t => t is not PlayerTeam).Select(TeamNationality.Of).ToArray();
            Assert.That(codes.All(c => GameInit.Nationalities.Contains(c)), Is.True);
            Assert.That(codes.Distinct().Count(), Is.GreaterThan(15));
        }

        [Test]
        public void ThePlayerKeepsTheirOwnPick()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us", Nationality = "sg" }, new Random(1).NextDouble);
            Assert.That(TeamNationality.Of(game.Teams.OfType<PlayerTeam>().Single()), Is.EqualTo("sg"));
        }
    }
}
