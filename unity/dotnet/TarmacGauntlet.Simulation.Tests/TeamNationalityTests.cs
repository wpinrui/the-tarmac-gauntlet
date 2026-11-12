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
        public void EveryRosterTeamHasACountryCode()
        {
            Assert.That(AiTeamRoster.All.All(e => e.Nationality.Length == 2 && e.Nationality == e.Nationality.ToLowerInvariant()), Is.True);
        }

        [Test]
        public void AiTeamsUseTheirRosterNationality()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us", Nationality = "sg" }, new Random(1).NextDouble);
            BaseTeam feretti = game.Teams.Single(t => t.Name == "Scuderia Feretti");
            Assert.That(TeamNationality.Of(feretti), Is.EqualTo("it"));
            foreach (BaseTeam team in game.Teams.Where(t => t is not PlayerTeam))
                Assert.That(TeamNationality.Of(team), Is.EqualTo(AiTeamRoster.All.Single(e => e.Name == team.Name).Nationality));
        }

        [Test]
        public void ThePlayerKeepsTheirOwnPick()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us", Nationality = "sg" }, new Random(1).NextDouble);
            Assert.That(TeamNationality.Of(game.Teams.OfType<PlayerTeam>().Single()), Is.EqualTo("sg"));
        }
    }
}
