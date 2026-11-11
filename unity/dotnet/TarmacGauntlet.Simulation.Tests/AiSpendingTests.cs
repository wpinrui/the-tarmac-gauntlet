#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static TarmacGauntlet.Simulation.AiSpending;

namespace TarmacGauntlet.Simulation.Tests
{
    /// <summary>Port of simulation/aiSpending.test.ts.</summary>
    public class AiSpendingTests
    {
        // ---------------------------------------------------------------------------
        // Fixtures
        // ---------------------------------------------------------------------------

        private static int idCounter = 0;
        private static string NextId() => $"gen-{++idCounter}";

        private static CarModel MakeModel(string id, double price, double power = 50, double handling = 50) => new CarModel
        {
            Id = id,
            Name = $"Car {id}",
            CarClass = CarClass.B,
            Tier = CarTier.GtRaceCar,
            Price = price,
            BaseStats = new CarStats
            {
                Power = power, Handling = handling, FuelEfficiency = 50, TyreDurability = 50,
                Comfort = 50, Reliability = 50, PitStopTime = 50, FuelCapacity = 100,
            },
            PotentialStats = new CarStats
            {
                Power = power + 20, Handling = handling + 20, FuelEfficiency = 70, TyreDurability = 70,
                Comfort = 70, Reliability = 50, PitStopTime = 30, FuelCapacity = 100,
            },
            UpgradePacks = new[]
            {
                new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = 5_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 4_000 },
                new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = 3_000 },
            },
        };

        private static CarInstance MakeInstance(string id, string modelId) => new CarInstance
        {
            Id = id,
            ModelId = modelId,
            Age = 0,
            Condition = 100,
            InstalledUpgrades = new InstalledUpgrades(),
        };

        private static AITeam MakeAiTeam(string id, double budget, CarInstance[]? cars = null, int crewSize = 0, int spareParts = 0, int tyreSets = 0)
        {
            cars ??= new CarInstance[0];
            return new AITeam
            {
                Id = id,
                Name = $"Team {id}",
                Budget = budget,
                Prestige = 0,
                PrestigeHistory = new double[] { 0 },
                CrewSize = crewSize,
                Cars = cars,
                Contracts = new Contract[0],
                EnteredCarId = cars.FirstOrDefault()?.Id,
                SpareParts = spareParts,
                TyreSets = tyreSets,
            };
        }

        internal static Driver MakeDriver(string id, double totalStatSum)
        {
            var perStat = totalStatSum / 5;
            return new Driver
            {
                Id = id,
                Name = $"Driver {id}",
                Nationality = "gb",
                Age = 30, // peak age: stats equal perStat
                CurveParams = new DriverCurveParams
                {
                    PeakAge = 30,
                    PeakStats = new DriverStats { Pace = perStat, Consistency = perStat, Stamina = perStat, Safety = perStat, Smoothness = perStat },
                    PhaseOffsets = new DriverStats(),
                },
                MarketValue = totalStatSum * 100,
            };
        }

        private static Contract MakeContract(string driverId, string teamId) =>
            new Contract { DriverId = driverId, TeamId = teamId, Length = 1, RemainingYears = 1, AnnualSalary = 5_000 };

        private static AiSpendingResult Run(AITeam team, IReadOnlyList<CarModel> models, IReadOnlyList<Driver>? drivers = null,
            IReadOnlyList<Contract>? contracts = null, IReadOnlyList<UsedCarListing>? used = null) =>
            RunAiSpending(new AiSpendingContext
            {
                Team = team,
                AllContracts = contracts ?? new Contract[0],
                AllDrivers = drivers ?? new Driver[0],
                CarModels = models,
                UsedListings = used ?? new UsedCarListing[0],
                NewCarId = NextId,
            });

        // ---------------------------------------------------------------------------
        // AI hires drivers to fill 3 slots
        // ---------------------------------------------------------------------------

        [Test]
        public void LockedTeam_BuysOnlyItsOwnModel()
        {
            var team = MakeAiTeam("t1", 1_000_000, new[] { MakeInstance("car1", "m1") }) with { ModelId = "m1" };
            var models = new[] { MakeModel("m1", 50_000, 40, 40), MakeModel("m2", 60_000, 90, 90) };
            var used = new[] { new UsedCarListing { Id = "u1", ModelId = "m2", Age = 1, Condition = 90, InstalledUpgrades = new InstalledUpgrades(), Price = 10_000 } };
            var result = Run(team, models, used: used);
            Assert.That(result.UpdatedTeam.Cars.Select(c => c.ModelId), Is.All.EqualTo("m1"));
        }

        [Test]
        public void Hiring_FillsEmptySlotsUpTo3()
        {
            var team = MakeAiTeam("t1", 1_000_000, new[] { MakeInstance("car1", "m1") });
            var drivers = new[] { MakeDriver("d1", 200), MakeDriver("d2", 150), MakeDriver("d3", 180) };
            var result = Run(team, new[] { MakeModel("m1", 50_000) }, drivers);
            Assert.That(result.NewContracts.Select(c => c.DriverId).ToList(), Has.Count.EqualTo(3));
        }

        [Test]
        public void Hiring_DoesNotHireMoreThan3()
        {
            var team = MakeAiTeam("t1", 10_000_000, new[] { MakeInstance("car1", "m1") });
            var drivers = Enumerable.Range(0, 10).Select(i => MakeDriver($"d{i}", 200)).ToArray();
            var result = Run(team, new[] { MakeModel("m1", 5_000) }, drivers);
            var count = result.NewContracts.Count(c => c.TeamId == "t1" && c.RemainingYears > 0);
            Assert.That(count, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void Hiring_NoneWhenTeamHas3DriversAndNoBudget()
        {
            var team = MakeAiTeam("t1", 0);
            var contracts = new[] { MakeContract("d1", "t1"), MakeContract("d2", "t1"), MakeContract("d3", "t1") };
            var result = Run(team, new CarModel[0], new[] { MakeDriver("d4", 300) }, contracts);
            Assert.That(result.NewContracts, Is.Empty);
        }

        [Test]
        public void Hiring_BudgetReducedBySalaries()
        {
            var team = MakeAiTeam("t1", 500_000, new[] { MakeInstance("car1", "m1") });
            var result = Run(team, new[] { MakeModel("m1", 5_000) }, new[] { MakeDriver("d1", 100) });
            Assert.That(result.UpdatedTeam.Budget, Is.LessThan(500_000));
        }

        // ---------------------------------------------------------------------------
        // AI buys a better car
        // ---------------------------------------------------------------------------

        [Test]
        public void Cars_BuysBetterNewCarWhenAffordable()
        {
            var weak = MakeModel("weak", 1_000, 30, 30);
            var strong = MakeModel("strong", 50_000, 80, 80);
            var team = MakeAiTeam("t1", 200_000, new[] { MakeInstance("car1", "weak") }) with { EnteredCarId = "car1" };
            var result = Run(team, new[] { weak, strong });
            var newModelIds = result.UpdatedTeam.Cars.Where(c => c.Id != "car1").Select(c => c.ModelId).ToList();
            Assert.That(newModelIds, Does.Contain("strong"));
        }

        [Test]
        public void Cars_NoPurchaseWhenBudgetInsufficient()
        {
            var cheap = MakeModel("cheap", 500, 50, 50);
            var expensive = MakeModel("expensive", 1_000_000, 90, 90);
            var team = MakeAiTeam("t1", 100, new[] { MakeInstance("car1", "cheap") });
            var result = Run(team, new[] { cheap, expensive });
            Assert.That(result.UpdatedTeam.Cars, Has.Count.EqualTo(1));
        }

        [Test]
        public void Cars_BuysUsedWhenUsedAffordableButNewIsNot()
        {
            var weak = MakeModel("weak", 1_000, 20, 20);
            var strong = MakeModel("strong", 500_000, 80, 80);
            var team = MakeAiTeam("t1", 30_000, new[] { MakeInstance("car1", "weak") });
            var used = new UsedCarListing
            {
                Id = "used1",
                ModelId = "strong",
                Age = 5,
                Condition = 60,
                InstalledUpgrades = new InstalledUpgrades(),
                Price = 25_000,
            };
            var result = Run(team, new[] { weak, strong }, used: new[] { used });
            Assert.That(result.UpdatedTeam.Cars.Any(c => c.ModelId == "strong"), Is.True);
        }

        // ---------------------------------------------------------------------------
        // AI applies upgrade packs
        // ---------------------------------------------------------------------------

        [Test]
        public void Upgrades_AppliesAffordablePackToCurrentCar()
        {
            var team = MakeAiTeam("t1", 20_000, new[] { MakeInstance("car1", "m1") });
            var result = Run(team, new[] { MakeModel("m1", 50_000, 50, 50) });
            var u = result.UpdatedTeam.Cars.First(c => c.Id == "car1").InstalledUpgrades;
            Assert.That(u.Power || u.Handling || u.Comfort, Is.True);
        }

        [Test]
        public void Crew_AiSpendingNeverChangesCrewSize()
        {
            var team = MakeAiTeam("t1", 500_000, new[] { MakeInstance("car1", "m1") }, crewSize: 6);
            var result = Run(team, new[] { MakeModel("m1", 50_000) });
            Assert.That(result.UpdatedTeam.CrewSize, Is.EqualTo(6));
        }

        [Test]
        public void Upgrades_DoesNotApplyUnaffordablePack()
        {
            var model = MakeModel("m1", 50_000) with
            {
                UpgradePacks = new[]
                {
                    new UpgradePackDefinition { Type = UpgradePackType.Power, Cost = 1_000_000 },
                    new UpgradePackDefinition { Type = UpgradePackType.Handling, Cost = 1_000_000 },
                    new UpgradePackDefinition { Type = UpgradePackType.Comfort, Cost = 1_000_000 },
                },
            };
            var team = MakeAiTeam("t1", 100, new[] { MakeInstance("car1", "m1") });
            var result = Run(team, new[] { model });
            var u = result.UpdatedTeam.Cars.First(c => c.Id == "car1").InstalledUpgrades;
            Assert.That(u.Power, Is.False);
            Assert.That(u.Handling, Is.False);
            Assert.That(u.Comfort, Is.False);
        }

        // ---------------------------------------------------------------------------
        // Budget constraint
        // ---------------------------------------------------------------------------

        [Test]
        public void Budget_NeverSpendsMoreThanBudget()
        {
            var team = MakeAiTeam("t1", 25_000, new[] { MakeInstance("car1", "m1") });
            var drivers = Enumerable.Range(0, 5).Select(i => MakeDriver($"d{i}", 250)).ToArray();
            var result = Run(team, new[] { MakeModel("m1", 10_000) }, drivers);
            Assert.That(result.UpdatedTeam.Budget, Is.GreaterThanOrEqualTo(0));
        }
    }
}
