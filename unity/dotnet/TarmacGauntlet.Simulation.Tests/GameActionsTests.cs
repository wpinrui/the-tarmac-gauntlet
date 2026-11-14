#nullable enable
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.GameActions;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>One test per game-logic action ported from state/store.ts.</summary>
    public class GameActionsTests
    {
        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        private static CarInstance Car(string id, double condition = 100) => new CarInstance
        {
            Id = id, ModelId = "m1", Age = 0, Condition = condition, InstalledUpgrades = new InstalledUpgrades(),
        };

        private static PlayerTeam MakePlayer(CarInstance[]? cars = null, string? enteredCarId = null) => new PlayerTeam
        {
            Id = PlayerTeamId, Name = "Player Team", Budget = 10_000, CrewSize = 2,
            Cars = cars ?? new CarInstance[0], EnteredCarId = enteredCarId,
            PlayerName = "Ivan", Nationality = "sg",
            Skills = new PlayerSkills { Driver = 5, Engineer = 5, Business = 20 },
            SpareParts = 10, TyreSets = 5,
        };

        private static AITeam MakeAi(string id) => new AITeam { Id = id, Name = $"Team {id}", Budget = 1_000 };

        private static GameState MakeGame(PlayerTeam player, params Contract[] contracts) => new GameState
        {
            CurrentYear = 4,
            Teams = new BaseTeam[] { player, MakeAi("ai1") },
            Contracts = contracts,
        };

        private static PlayerTeam Player(GameState g) => g.Teams.OfType<PlayerTeam>().Single();
        private static AITeam Ai(GameState g) => g.Teams.OfType<AITeam>().Single();

        private static Contract MakeContract(string driverId, string teamId) =>
            new Contract { DriverId = driverId, TeamId = teamId, Length = 2, RemainingYears = 2, AnnualSalary = 5_000 };

        // ---------------------------------------------------------------------------
        // Car actions
        // ---------------------------------------------------------------------------

        [Test]
        public void BuyCar_AddsCarDeductsCostEntersFirstCarAndLogs()
        {
            var g = BuyCar(MakeGame(MakePlayer()), Car("c1"), 3_000, "Nusa Carrier");
            var p = Player(g);
            Assert.That(p.Budget, Is.EqualTo(7_000));
            Assert.That(p.Cars.Select(c => c.Id), Is.EqualTo(new[] { "c1" }));
            Assert.That(p.EnteredCarId, Is.EqualTo("c1"));
            Assert.That(p.Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.CarPurchase, Amount = -3_000, Description = "Bought Nusa Carrier",
            }));

            // Entered car is kept when one is already set
            var g2 = BuyCar(g, Car("c2"), 1_000, "Liberty Omaha");
            Assert.That(Player(g2).EnteredCarId, Is.EqualTo("c1"));
            Assert.That(Ai(g2).Budget, Is.EqualTo(1_000));
        }

        [Test]
        public void SellCar_RemovesCarAddsPriceClearsEnteredAndLogs()
        {
            var g = SellCar(MakeGame(MakePlayer(new[] { Car("c1"), Car("c2") }, "c1")), "c1", 2_500, "Nusa Carrier");
            var p = Player(g);
            Assert.That(p.Budget, Is.EqualTo(12_500));
            Assert.That(p.Cars.Select(c => c.Id), Is.EqualTo(new[] { "c2" }));
            Assert.That(p.EnteredCarId, Is.Null);
            Assert.That(p.Transactions.Single().Category, Is.EqualTo(TransactionCategory.CarSale));
            Assert.That(p.Transactions.Single().Amount, Is.EqualTo(2_500));
            Assert.That(p.Transactions.Single().Description, Is.EqualTo("Sold Nusa Carrier"));

            var g2 = SellCar(MakeGame(MakePlayer(new[] { Car("c1"), Car("c2") }, "c1")), "c2", 100, "X");
            Assert.That(Player(g2).EnteredCarId, Is.EqualTo("c1"));
        }

        [Test]
        public void EnterCar_SetsEnteredCarId()
        {
            var g = EnterCar(MakeGame(MakePlayer(new[] { Car("c1"), Car("c2") }, "c1")), "c2");
            Assert.That(Player(g).EnteredCarId, Is.EqualTo("c2"));
        }

        [Test]
        public void InstallUpgrade_SetsPackDeductsCostAndLogsCapitalisedLabel()
        {
            var g = InstallUpgrade(MakeGame(MakePlayer(new[] { Car("c1"), Car("c2") })), "c1", UpgradePackType.Handling, 4_000, "Tora Giga");
            var p = Player(g);
            Assert.That(p.Budget, Is.EqualTo(6_000));
            Assert.That(p.Cars[0].InstalledUpgrades, Is.EqualTo(new InstalledUpgrades { Handling = true }));
            Assert.That(p.Cars[1].InstalledUpgrades, Is.EqualTo(new InstalledUpgrades()));
            Assert.That(p.Transactions.Single().Category, Is.EqualTo(TransactionCategory.Upgrade));
            Assert.That(p.Transactions.Single().Amount, Is.EqualTo(-4_000));
            Assert.That(p.Transactions.Single().Description, Is.EqualTo("Handling Pack on Tora Giga"));
        }

        [Test]
        public void RepairCar_UsesPartsAndCapsConditionAt100()
        {
            var g = RepairCar(MakeGame(MakePlayer(new[] { Car("c1", 90) })), "c1", 3, 15);
            var p = Player(g);
            Assert.That(p.SpareParts, Is.EqualTo(7));
            Assert.That(p.Cars[0].Condition, Is.EqualTo(100));
            Assert.That(p.Budget, Is.EqualTo(10_000));
            Assert.That(p.Transactions, Is.Empty);
        }

        // ---------------------------------------------------------------------------
        // Consumables
        // ---------------------------------------------------------------------------

        [Test]
        public void BuySpares_AddsPartsDeductsCostAndLogs()
        {
            var p = Player(BuySpares(MakeGame(MakePlayer()), 5, 500));
            Assert.That(p.SpareParts, Is.EqualTo(15));
            Assert.That(p.Budget, Is.EqualTo(9_500));
            Assert.That(p.Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.SpareParts, Amount = -500, Description = "Bought 5 spare parts",
            }));
        }

        [Test]
        public void BuyTyres_AddsSetsDeductsCostAndLogs()
        {
            var p = Player(BuyTyres(MakeGame(MakePlayer()), 3, 1_500));
            Assert.That(p.TyreSets, Is.EqualTo(8));
            Assert.That(p.Budget, Is.EqualTo(8_500));
            Assert.That(p.Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.Tyres, Amount = -1_500, Description = "Bought 3 tyre sets",
            }));
        }

        // ---------------------------------------------------------------------------
        // Driver actions
        // ---------------------------------------------------------------------------

        [Test]
        public void HireDriver_AddsContractToPlayerAndGameWithoutDeductingSalary()
        {
            var g = HireDriver(MakeGame(MakePlayer()), "d1", "Driver d1", 3, 8_000);
            var expected = new Contract { DriverId = "d1", TeamId = "player", Length = 3, RemainingYears = 3, AnnualSalary = 8_000 };
            Assert.That(Player(g).Contracts, Is.EqualTo(new[] { expected }));
            Assert.That(g.Contracts, Is.EqualTo(new[] { expected }));
            Assert.That(Player(g).Budget, Is.EqualTo(10_000));
            Assert.That(Player(g).Transactions, Is.Empty);
        }

        [Test]
        public void ReleaseDriver_RemovesOnlyThePlayersContract()
        {
            var mine = MakeContract("d1", "player");
            var theirs = MakeContract("d1", "ai1");
            var other = MakeContract("d2", "player");
            var player = MakePlayer() with { Contracts = new[] { mine, other } };
            var g = ReleaseDriver(MakeGame(player, mine, theirs, other), "d1");
            Assert.That(Player(g).Contracts, Is.EqualTo(new[] { other }));
            Assert.That(g.Contracts, Is.EqualTo(new[] { theirs, other }));
        }

        [Test]
        public void BuyoutDriver_RemovesDriverContractsFromGameChargesAndLogs()
        {
            var aiContract = MakeContract("d1", "ai1");
            var keep = MakeContract("d2", "ai1");
            var g = BuyoutDriver(MakeGame(MakePlayer(), aiContract, keep), "d1", "Driver d1", 7_500);
            var p = Player(g);
            Assert.That(g.Contracts, Is.EqualTo(new[] { keep }));
            Assert.That(p.Budget, Is.EqualTo(2_500));
            Assert.That(p.Contracts, Is.Empty);
            Assert.That(p.Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.DriverBuyout, Amount = -7_500, Description = "Bought out Driver d1", DriverId = "d1",
            }));
        }

        // ---------------------------------------------------------------------------
        // Crew
        // ---------------------------------------------------------------------------

        [Test]
        public void SetCrewSize_SetsSizeWithoutDeductingCost()
        {
            var p = Player(SetCrewSize(MakeGame(MakePlayer()), 9));
            Assert.That(p.CrewSize, Is.EqualTo(9));
            Assert.That(p.Budget, Is.EqualTo(10_000));
        }

        // ---------------------------------------------------------------------------
        // Post-race financial actions
        // ---------------------------------------------------------------------------

        [Test]
        public void AwardPrizeMoney_PaysTeamAndLogsOnlyForPlayer()
        {
            var g = AwardPrizeMoney(MakeGame(MakePlayer()), "player", 3, 20_000);
            Assert.That(Player(g).Budget, Is.EqualTo(30_000));
            Assert.That(Player(g).Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.PrizeMoney, Amount = 20_000, Description = "P3 prize money",
            }));
            Assert.That(Ai(g).Budget, Is.EqualTo(1_000));

            var g2 = AwardPrizeMoney(g, "ai1", 1, 500);
            Assert.That(Ai(g2).Budget, Is.EqualTo(1_500));
            Assert.That(Player(g2).Budget, Is.EqualTo(30_000));
            Assert.That(Player(g2).Transactions, Has.Count.EqualTo(1));
        }

        [Test]
        public void DeductFuelCost_ChargesFullCostOrWritesOffShortfall()
        {
            var paid = Player(DeductFuelCost(MakeGame(MakePlayer()), 4_000));
            Assert.That(paid.Budget, Is.EqualTo(6_000));
            Assert.That(paid.Transactions.Single(), Is.EqualTo(new TransactionRecord
            {
                Year = 4, Category = TransactionCategory.FuelCost, Amount = -4_000, Description = "Fuel cost",
            }));

            var shortfall = Player(DeductFuelCost(MakeGame(MakePlayer()), 1_250_000));
            Assert.That(shortfall.Budget, Is.EqualTo(0));
            Assert.That(shortfall.Transactions.Single().Amount, Is.EqualTo(-10_000));
            Assert.That(shortfall.Transactions.Single().Description, Is.EqualTo("Fuel cost (written off — $1,240,000 unpaid)"));
        }

        // ---------------------------------------------------------------------------
        // Skills
        // ---------------------------------------------------------------------------

        [Test]
        public void AllocateSkillPoint_AddsOneCappedAt20()
        {
            var g = AllocateSkillPoint(MakeGame(MakePlayer()), PlayerSkillType.Engineer);
            Assert.That(Player(g).Skills, Is.EqualTo(new PlayerSkills { Driver = 5, Engineer = 6, Business = 20 }));
            var capped = AllocateSkillPoint(g, PlayerSkillType.Business);
            Assert.That(Player(capped).Skills.Business, Is.EqualTo(20));
        }

        // ---------------------------------------------------------------------------
        // Race history
        // ---------------------------------------------------------------------------

        [Test]
        public void PushRaceHistory_AppendsEntry()
        {
            var first = new RaceHistoryEntry { Year = 1 };
            var second = new RaceHistoryEntry { Year = 2 };
            var g = PushRaceHistory(PushRaceHistory(MakeGame(MakePlayer()), first), second);
            Assert.That(g.RaceHistory, Is.EqualTo(new[] { first, second }));
        }
    }
}
