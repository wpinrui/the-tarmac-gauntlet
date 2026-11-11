#nullable enable
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class DriverLineupTests
    {
        private const string You = RaceEntry.PlayerCharacterDriverId;

        private static GameState Game(string[] lineup, params Contract[] contracts) => new GameState
        {
            Teams = new BaseTeam[]
            {
                new PlayerTeam { Id = GameActions.PlayerTeamId, Lineup = lineup },
                new AITeam { Id = "ai1" },
            },
            Contracts = contracts,
        };

        private static Contract Mine(string driverId, int remaining = 2) =>
            new Contract { DriverId = driverId, TeamId = GameActions.PlayerTeamId, Length = 2, RemainingYears = remaining, AnnualSalary = 1_000 };

        [Test]
        public void WithNothingStored_OwnerFirstThenHiresInContractOrder()
        {
            var game = Game(new string[0], Mine("d1"), Mine("d2"));
            Assert.That(DriverLineup.Of(game), Is.EqualTo(new[] { You, "d1", "d2" }));
        }

        [Test]
        public void StoredOrderWins()
        {
            var game = Game(new[] { "d2", You, "d1" }, Mine("d1"), Mine("d2"));
            Assert.That(DriverLineup.Of(game), Is.EqualTo(new[] { "d2", You, "d1" }));
        }

        [Test]
        public void NewHiresJoinTheEnd_ReleasedAndExpiredDriversDropOut()
        {
            var aiContract = new Contract { DriverId = "d4", TeamId = "ai1", Length = 1, RemainingYears = 1 };
            var game = Game(new[] { "d1", "gone", You, "d3" }, Mine("d1"), Mine("d2"), Mine("d3", remaining: 0), aiContract);
            Assert.That(DriverLineup.Of(game), Is.EqualTo(new[] { "d1", You, "d2" }));
        }

        [Test]
        public void SetLineup_StoresTheOrder()
        {
            var game = GameActions.SetLineup(Game(new string[0], Mine("d1")), new[] { "d1", You });
            Assert.That(game.Teams.OfType<PlayerTeam>().Single().Lineup, Is.EqualTo(new[] { "d1", You }));
            Assert.That(DriverLineup.Of(game), Is.EqualTo(new[] { "d1", You }));
        }
    }
}
