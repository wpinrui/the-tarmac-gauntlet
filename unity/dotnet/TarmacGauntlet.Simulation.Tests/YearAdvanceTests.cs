#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.YearAdvance;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/yearAdvance.test.ts.</summary>
    public class YearAdvanceTests
    {
        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        private static int idSeq = 0;
        private static string NextId() => $"gen-{++idSeq}";

        private static CarModel MakeModel(string id, double price) => new CarModel
        {
            Id = id,
            Name = $"Car {id}",
            CarClass = CarClass.B,
            Tier = CarTier.GtRaceCar,
            Price = price,
            BaseStats = new CarStats
            {
                Power = 50, Handling = 50, FuelEfficiency = 50, TyreDurability = 50,
                Comfort = 50, Reliability = 50, PitStopTime = 50, FuelCapacity = 100,
            },
            PotentialStats = new CarStats
            {
                Power = 70, Handling = 70, FuelEfficiency = 70, TyreDurability = 70,
                Comfort = 70, Reliability = 50, PitStopTime = 30, FuelCapacity = 100,
            },
            UpgradePacks = new[]
            {
                new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = 5_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 4_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = 3_000 },
            },
        };

        private static CarInstance MakeCarInstance(string id, string modelId, int age = 0) => new CarInstance
        {
            Id = id, ModelId = modelId, Age = age, Condition = 100, InstalledUpgrades = new InstalledUpgrades(),
        };

        private static Driver MakeDriver(string id, double totalStats) => AiSpendingTests.MakeDriver(id, totalStats);

        private static AITeam MakeAiTeam(string id, double budget, CarInstance? car = null) => new AITeam
        {
            Id = id, Name = $"Team {id}", Budget = budget,
            Prestige = 0, PrestigeHistory = new double[] { 0 }, CrewSize = 0,
            Cars = car != null ? new[] { car } : new CarInstance[0],
            Contracts = new Contract[0],
            EnteredCarId = car?.Id,
            SpareParts = 0, TyreSets = 0,
        };

        internal static PlayerTeam MakePlayerTeam(string id)
        {
            var car = MakeCarInstance("player-car", "m1", 0);
            return new PlayerTeam
            {
                Id = id, Name = "Player Team", Budget = 10_000,
                Prestige = 0, PrestigeHistory = new double[] { 0 }, CrewSize = 2,
                Cars = new[] { car }, Contracts = new Contract[0], EnteredCarId = car.Id,
                PlayerName = "Ivan", Nationality = "sg", Logo = null,
                Skills = new PlayerSkills { Driver = 5, Engineer = 5, Business = 5 },
                SpareParts = 10, TyreSets = 5,
                Transactions = new TransactionRecord[0],
            };
        }

        /// <summary>Returns 15 driver specs for rookie generation.</summary>
        private static RookieSpec[] RookieSpecs() =>
            Enumerable.Range(0, 15).Select(i => new RookieSpec { Id = $"rookie-{i}", Name = $"Rookie {i}", Nationality = "gb" }).ToArray();

        /// <summary>Deterministic random: always returns 0.5.</summary>
        private static double Stable() => 0.5;

        private static Driver[] Drivers(int n, System.Func<int, double> stats) =>
            Enumerable.Range(0, n).Select(i => MakeDriver($"d{i}", stats(i))).ToArray();

        private static YearAdvanceResult Advance(Driver[] drivers, IReadOnlyList<Contract>? contracts = null,
            IReadOnlyList<BaseTeam>? teams = null, IReadOnlyList<CarModel>? models = null) =>
            AdvanceYear(new YearAdvanceInput
            {
                Drivers = drivers,
                Contracts = contracts ?? new Contract[0],
                Teams = teams ?? new BaseTeam[0],
                CarModels = models ?? new CarModel[0],
                RookieSpecs = RookieSpecs(),
                NewCarId = NextId,
            }, Stable);

        // ---------------------------------------------------------------------------
        // Driver pool advancement
        // ---------------------------------------------------------------------------

        [Test]
        public void DriverPool_SurvivorsAreOneYearOlder()
        {
            var result = Advance(Drivers(30, _ => 200), models: new[] { MakeModel("m1", 10_000) });
            // New rookies are always 18; surviving drivers should all be 31
            foreach (var d in result.Drivers.Where(d => !d.Id.StartsWith("rookie-")))
                Assert.That(d.Age, Is.EqualTo(31));
        }

        [Test]
        public void DriverPool_Returns15RetiredIds()
        {
            var result = Advance(Drivers(30, i => 200 + i));
            Assert.That(result.RetiredDriverIds, Has.Count.EqualTo(15));
        }

        [Test]
        public void DriverPool_SizeStaysConstant()
        {
            var result = Advance(Drivers(40, i => 200 + i));
            Assert.That(result.Drivers, Has.Count.EqualTo(40));
        }

        // ---------------------------------------------------------------------------
        // Contract expiry
        // ---------------------------------------------------------------------------

        [Test]
        public void Contracts_OneRemainingYearIsRemoved()
        {
            var expiring = new Contract { DriverId = "d1", TeamId = "t1", Length = 1, RemainingYears = 1, AnnualSalary = 5_000 };
            var result = Advance(Drivers(20, i => 200 + i * 5), new[] { expiring });
            Assert.That(result.Contracts.FirstOrDefault(c => c.DriverId == "d1" && c.TeamId == "t1"), Is.Null);
        }

        [Test]
        public void Contracts_TwoRemainingYearsBecomesOne()
        {
            var longContract = new Contract { DriverId = "d2", TeamId = "t2", Length = 2, RemainingYears = 2, AnnualSalary = 10_000 };
            var result = Advance(Drivers(20, i => 200 + i * 5), new[] { longContract });
            Assert.That(result.Contracts.FirstOrDefault(c => c.DriverId == "d2")?.RemainingYears, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------------------
        // Car aging
        // ---------------------------------------------------------------------------

        [Test]
        public void Cars_AllInstancesOneYearOlder()
        {
            var team = MakeAiTeam("t1", 0, MakeCarInstance("car1", "m1", 2));
            var result = Advance(Drivers(20, i => 200 + i * 5), teams: new[] { team }, models: new[] { MakeModel("m1", 10_000) });
            Assert.That(result.Teams[0].Cars[0].Age, Is.EqualTo(3));
        }

        // ---------------------------------------------------------------------------
        // Used car inventory
        // ---------------------------------------------------------------------------

        [Test]
        public void UsedInventory_IsValid()
        {
            var models = new[] { MakeModel("m1", 20_000), MakeModel("m2", 100_000) };
            var result = Advance(Drivers(20, i => 200 + i * 5), models: models);
            // Both test models are class B, so only 1 listing (1 per class)
            Assert.That(result.UsedListings.Count, Is.GreaterThanOrEqualTo(1));
            foreach (var listing in result.UsedListings)
            {
                Assert.That(listing.Age, Is.GreaterThanOrEqualTo(1));
                Assert.That(listing.Condition, Is.GreaterThanOrEqualTo(30));
                Assert.That(listing.Price, Is.GreaterThan(0));
            }
        }

        // ---------------------------------------------------------------------------
        // AI spending integration
        // ---------------------------------------------------------------------------

        [Test]
        public void AiSpending_AiTeamHiresDriversWithBudget()
        {
            var aiTeam = MakeAiTeam("ai1", 200_000, MakeCarInstance("ai-car", "m1", 0));
            var result = Advance(Drivers(5, _ => 200), teams: new[] { aiTeam }, models: new[] { MakeModel("m1", 5_000) });
            Assert.That(result.Contracts.Count(c => c.TeamId == "ai1"), Is.GreaterThan(0));
        }

        [Test]
        public void AiSpending_PlayerTeamNotModified()
        {
            var playerTeam = MakePlayerTeam("player1");
            var result = Advance(Drivers(20, i => 200 + i * 5), teams: new[] { playerTeam }, models: new[] { MakeModel("m1", 5_000) });
            var resultPlayer = result.Teams.First(t => t.Id == "player1");
            // Budget reduced by annual crew cost (2 members x $2,000 = $4,000)
            Assert.That(resultPlayer.Budget, Is.EqualTo(playerTeam.Budget - playerTeam.CrewSize * 2_000));
        }

        [Test]
        public void AiCrew_ResizedFromPrestigeAndPaidForAtTheNewSize()
        {
            var low = MakeAiTeam("low", 0) with { Prestige = 10, CrewSize = 16 };
            var high = MakeAiTeam("high", 0) with { Prestige = 80, CrewSize = 4 };
            var player = MakePlayerTeam("player1") with { Prestige = 100 };
            var result = Advance(Drivers(20, _ => 200), teams: new BaseTeam[] { player, low, high });
            var byId = result.Teams.ToDictionary(t => t.Id);
            Assert.That(byId["low"].CrewSize, Is.EqualTo(4));
            Assert.That(byId["high"].CrewSize, Is.EqualTo(16));
            Assert.That(byId["low"].Budget, Is.EqualTo(-4 * 2_000));
            Assert.That(byId["high"].Budget, Is.EqualTo(-16 * 2_000));
            Assert.That(byId["player1"].CrewSize, Is.EqualTo(player.CrewSize));
        }

        [Test]
        public void AiSpending_TheMoreAppealingTeamSignsTheBestDriverFirst()
        {
            var drivers = Drivers(20, i => i == 0 ? 400 : 100);
            var plain = MakeAiTeam("plain", 1_000_000) with { Prestige = 50 };
            var factory = MakeAiTeam("factory", 1_000_000) with { Prestige = 50, Status = TeamStatus.Factory };
            var result = Advance(drivers, teams: new BaseTeam[] { plain, factory });
            Assert.That(result.Contracts.Single(c => c.DriverId == "d0").TeamId, Is.EqualTo("factory"));
            Assert.That(result.Teams.Select(t => t.Id), Is.EqualTo(new[] { "plain", "factory" }));
        }

        // ---------------------------------------------------------------------------
        // Full-field smoke test
        // ---------------------------------------------------------------------------

        [Test]
        public void FullField_RunsOn100Teams()
        {
            var models = Enumerable.Range(0, 5).Select(i => MakeModel($"m{i}", (i + 1) * 20_000)).ToArray();
            var drivers = Drivers(310, i => 100 + (i % 400));
            var teams = new List<BaseTeam> { MakePlayerTeam("player") };
            teams.AddRange(Enumerable.Range(0, 99).Select(i => MakeAiTeam($"ai-{i}", 50_000 + i * 1_000, MakeCarInstance($"car-ai-{i}", $"m{i % 5}", i % 5))));
            var specs = Enumerable.Range(0, 15).Select(i => new RookieSpec { Id = $"r{i}", Name = $"R{i}", Nationality = "gb" }).ToArray();

            Assert.DoesNotThrow(() => AdvanceYear(new YearAdvanceInput
            {
                Drivers = drivers, Contracts = new Contract[0], Teams = teams, CarModels = models, RookieSpecs = specs, NewCarId = NextId,
            }, Stable));
        }
    }
}
