#nullable enable
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.GameInit;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/gameInit.test.ts.</summary>
    public class GameInitTests
    {
        private static double Stable() => 0.5;

        // ---------------------------------------------------------------------------
        // seedDriverPool
        // ---------------------------------------------------------------------------

        [Test]
        public void SeedDriverPool_GeneratesExactly310Drivers()
        {
            var drivers = SeedDriverPool(Stable);
            Assert.That(drivers, Has.Count.EqualTo(310));
        }

        [Test]
        public void SeedDriverPool_AllDriversHaveAgesBetween18And42()
        {
            var drivers = SeedDriverPool(DefaultRandom.Next);
            foreach (var d in drivers)
            {
                Assert.That(d.Age, Is.GreaterThanOrEqualTo(18));
                Assert.That(d.Age, Is.LessThanOrEqualTo(42));
            }
        }

        [Test]
        public void SeedDriverPool_AgeDistributionIsNotAllRookies()
        {
            var drivers = SeedDriverPool(DefaultRandom.Next);
            var ages = drivers.Select(d => d.Age).Distinct().Count();
            Assert.That(ages, Is.GreaterThan(5));
        }

        [Test]
        public void SeedDriverPool_AllDriversHaveUniqueIds()
        {
            var drivers = SeedDriverPool(Stable);
            Assert.That(drivers.Select(d => d.Id).Distinct().Count(), Is.EqualTo(310));
        }

        [Test]
        public void SeedDriverPool_AllDriversHaveNonEmptyNames()
        {
            var drivers = SeedDriverPool(Stable);
            foreach (var d in drivers) Assert.That(d.Name.Length, Is.GreaterThan(0));
        }

        [Test]
        public void SeedDriverPool_AllDriversHavePositiveMarketValues()
        {
            var drivers = SeedDriverPool(Stable);
            foreach (var d in drivers) Assert.That(d.MarketValue, Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------------------
        // initializeGame
        // ---------------------------------------------------------------------------

        private static readonly NewGameOptions Options = new NewGameOptions
        {
            PlayerName = "Ivan",
            Nationality = "sg",
            TeamName = "Irene Racing",
            Logo = null,
            Skills = new PlayerSkills { Driver = 5, Engineer = 5, Business = 5 },
        };

        private static PlayerTeam Player(GameState game) => game.Teams.OfType<PlayerTeam>().First();

        [Test]
        public void InitializeGame_CreatesAGameStateWith100Teams()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.Teams, Has.Count.EqualTo(100));
        }

        [Test]
        public void InitializeGame_HasExactly1PlayerTeamAnd99AiTeams()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.Teams.OfType<PlayerTeam>().Count(), Is.EqualTo(1));
            Assert.That(game.Teams.OfType<AITeam>().Count(), Is.EqualTo(99));
        }

        [Test]
        public void InitializeGame_PlayerTeamKeepsItsBadge()
        {
            var game = InitializeGame(Options with { Logo = "bolt", TeamColour = "#3aa0ff" }, Stable);
            Assert.That(Player(game).Logo, Is.EqualTo("bolt"));
            Assert.That(Player(game).TeamColour, Is.EqualTo("#3aa0ff"));
        }

        [Test]
        public void InitializeGame_UnspentStartingPointsAreForfeited()
        {
            Assert.That(Player(InitializeGame(Options, Stable)).ForfeitedSkillPoints, Is.EqualTo(0));
            var game = InitializeGame(Options with { Skills = new PlayerSkills { Driver = 6 } }, Stable);
            Assert.That(Player(game).ForfeitedSkillPoints, Is.EqualTo(9));
        }

        [Test]
        public void InitializeGame_PlayerTeamHasTheCorrectNameAndPlayerName()
        {
            var game = InitializeGame(Options, Stable);
            var player = Player(game);
            Assert.That(player.Name, Is.EqualTo("Irene Racing"));
            Assert.That(player.PlayerName, Is.EqualTo("Ivan"));
        }

        [Test]
        public void InitializeGame_PlayerTeamHas1000StartingBudget()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(Player(game).Budget, Is.EqualTo(1_000));
        }

        [Test]
        public void InitializeGame_PlayerTeamStartsWithNoCars()
        {
            var game = InitializeGame(Options, Stable);
            var player = Player(game);
            Assert.That(player.Cars, Is.Empty);
            Assert.That(player.EnteredCarId, Is.Null);
        }

        [Test]
        public void InitializeGame_AiCrewsRunFrom4To16ByPrestige()
        {
            var game = InitializeGame(Options, DefaultRandom.Next);
            var ai = game.Teams.OfType<AITeam>().OrderBy(t => t.Prestige).ToList();
            Assert.That(ai.Select(t => t.CrewSize), Is.All.InRange(4, 16));
            Assert.That(ai.First().CrewSize, Is.EqualTo(4));
            Assert.That(ai.Last().CrewSize, Is.EqualTo(16));
            for (int i = 1; i < ai.Count; i++)
                Assert.That(ai[i].CrewSize, Is.GreaterThanOrEqualTo(ai[i - 1].CrewSize));
        }

        [Test]
        public void InitializeGame_PlayerTeamHasNoCrewContractsSparePartsOrTyreSets()
        {
            var game = InitializeGame(Options, Stable);
            var player = Player(game);
            Assert.That(player.CrewSize, Is.EqualTo(0));
            Assert.That(player.Contracts, Is.Empty);
            Assert.That(player.SpareParts, Is.EqualTo(0));
            Assert.That(player.TyreSets, Is.EqualTo(0));
        }

        [Test]
        public void InitializeGame_PlayerSkillsAreSetCorrectly()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(Player(game).Skills, Is.EqualTo(new PlayerSkills { Driver = 5, Engineer = 5, Business = 5 }));
        }

        [Test]
        public void InitializeGame_AllAiTeamsHaveExactly1CarEach()
        {
            var game = InitializeGame(Options, Stable);
            foreach (var team in game.Teams.OfType<AITeam>()) Assert.That(team.Cars, Has.Count.EqualTo(1));
        }

        [Test]
        public void InitializeGame_EachAiTeamHasExactly3ContractedDrivers()
        {
            var game = InitializeGame(Options, Stable);
            foreach (var team in game.Teams.OfType<AITeam>())
            {
                Assert.That(game.Contracts.Count(c => c.TeamId == team.Id), Is.EqualTo(3));
            }
        }

        [Test]
        public void InitializeGame_TotalContractsIs297()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.Contracts, Has.Count.EqualTo(297));
        }

        [Test]
        public void InitializeGame_DriverPoolHas310Drivers()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.Drivers, Has.Count.EqualTo(310));
        }

        [Test]
        public void InitializeGame_GameYearIs1()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.CurrentYear, Is.EqualTo(1));
        }

        [Test]
        public void InitializeGame_PlayerStartsWithZeroPrestigeAiTeamsHaveSeededPrestige()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(Player(game).Prestige, Is.EqualTo(0));
            foreach (var team in game.Teams.OfType<AITeam>()) Assert.That(team.Prestige, Is.GreaterThan(0));
        }

        [Test]
        public void InitializeGame_CarMarketHasNewListingsForAllModels()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.CarMarket.NewListings.Count, Is.EqualTo(game.CarModels.Count));
        }

        [Test]
        public void InitializeGame_CarMarketHasUsedListings()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.CarMarket.UsedListings.Count, Is.GreaterThan(0));
        }

        [Test]
        public void InitializeGame_AiTeamsSpanTheBudgetSpectrum()
        {
            var game = InitializeGame(Options, Stable);
            var budgets = game.Teams.OfType<AITeam>().Select(t => t.Budget).ToList();
            Assert.That(budgets.Min(), Is.LessThan(5_000));
            Assert.That(budgets.Max(), Is.GreaterThan(50_000));
        }

        [Test]
        public void InitializeGame_RaceHistoryIsEmptyAtGameStart()
        {
            var game = InitializeGame(Options, Stable);
            Assert.That(game.RaceHistory, Is.Empty);
        }
    }
}
