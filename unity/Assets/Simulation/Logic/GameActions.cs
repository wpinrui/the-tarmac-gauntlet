#nullable enable
using System;
using System.Globalization;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS <c>"driver" | "engineer" | "business"</c> argument of <c>allocateSkillPoint</c>.</summary>
    public enum PlayerSkillType
    {
        Driver,
        Engineer,
        Business,
    }

    /// <summary>
    /// Port of the game-logic actions in state/store.ts as pure functions on GameState.
    /// UI-only store state (screen, race session, phase) is not ported.
    /// </summary>
    public static class GameActions
    {
        /// <summary>The player team's fixed ID, as used by store.ts for player contracts.</summary>
        public const string PlayerTeamId = "player";

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private static TransactionRecord Tx(int year, TransactionCategory category, double amount, string description) =>
            new TransactionRecord { Year = year, Category = category, Amount = amount, Description = description };

        private static GameState UpdatePlayer(GameState game, Func<PlayerTeam, PlayerTeam> updater) =>
            game with { Teams = game.Teams.Select(t => t is PlayerTeam p ? updater(p) : t).ToArray() };

        /// <summary>JS <c>Number.prototype.toLocaleString()</c> in en-US.</summary>
        private static string ToLocaleString(double value) =>
            value.ToString("#,##0.###", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------------------
        // Car actions
        // ---------------------------------------------------------------------------

        public static GameState BuyCar(GameState game, CarInstance car, double cost, string modelName)
        {
            var year = game.CurrentYear;
            return UpdatePlayer(game, p => p with
            {
                Budget = p.Budget - cost,
                Cars = p.Cars.Append(car).ToArray(),
                EnteredCarId = p.EnteredCarId ?? car.Id,
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.CarPurchase, -cost, $"Bought {modelName}")).ToArray(),
            });
        }

        public static GameState SellCar(GameState game, string carId, double salePrice, string modelName)
        {
            var year = game.CurrentYear;
            return UpdatePlayer(game, p => p with
            {
                Budget = p.Budget + salePrice,
                Cars = p.Cars.Where(c => c.Id != carId).ToArray(),
                EnteredCarId = p.EnteredCarId == carId ? null : p.EnteredCarId,
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.CarSale, salePrice, $"Sold {modelName}")).ToArray(),
            });
        }

        public static GameState EnterCar(GameState game, string carId) =>
            UpdatePlayer(game, p => p with { EnteredCarId = carId });

        public static GameState InstallUpgrade(GameState game, string carId, UpgradePackType packType, double cost, string modelName)
        {
            var year = game.CurrentYear;
            var packLabel = packType.ToString(); // TS capitalises the pack type: "Power", "Handling", "Comfort"
            return UpdatePlayer(game, p => p with
            {
                Budget = p.Budget - cost,
                Cars = p.Cars
                    .Select(c => c.Id == carId ? c with { InstalledUpgrades = WithPack(c.InstalledUpgrades, packType) } : c)
                    .ToArray(),
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.Upgrade, -cost, $"{packLabel} Pack on {modelName}")).ToArray(),
            });
        }

        public static GameState RepairCar(GameState game, string carId, int partsUsed, double conditionGain) =>
            UpdatePlayer(game, p => p with
            {
                SpareParts = p.SpareParts - partsUsed,
                Cars = p.Cars
                    .Select(c => c.Id == carId ? c with { Condition = Math.Min(100, c.Condition + conditionGain) } : c)
                    .ToArray(),
            });

        private static InstalledUpgrades WithPack(InstalledUpgrades u, UpgradePackType pack) => pack switch
        {
            UpgradePackType.Power => u with { Power = true },
            UpgradePackType.Handling => u with { Handling = true },
            UpgradePackType.Comfort => u with { Comfort = true },
            _ => throw new ArgumentOutOfRangeException(nameof(pack)),
        };

        // ---------------------------------------------------------------------------
        // Consumables
        // ---------------------------------------------------------------------------

        public static GameState BuySpares(GameState game, int quantity, double totalCost)
        {
            var year = game.CurrentYear;
            return UpdatePlayer(game, p => p with
            {
                Budget = p.Budget - totalCost,
                SpareParts = p.SpareParts + quantity,
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.SpareParts, -totalCost, $"Bought {quantity} spare parts")).ToArray(),
            });
        }

        public static GameState BuyTyres(GameState game, int quantity, double totalCost)
        {
            var year = game.CurrentYear;
            return UpdatePlayer(game, p => p with
            {
                Budget = p.Budget - totalCost,
                TyreSets = p.TyreSets + quantity,
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.Tyres, -totalCost, $"Bought {quantity} tyre sets")).ToArray(),
            });
        }

        // ---------------------------------------------------------------------------
        // Driver actions
        // ---------------------------------------------------------------------------

        /// <summary>Does NOT deduct annual salary; YearAdvance handles that.</summary>
        public static GameState HireDriver(GameState game, string driverId, string driverName, int length, double annualSalary)
        {
            var contract = new Contract
            {
                DriverId = driverId,
                TeamId = PlayerTeamId,
                Length = length,
                RemainingYears = length,
                AnnualSalary = annualSalary,
            };
            var result = UpdatePlayer(game, p => p with { Contracts = p.Contracts.Append(contract).ToArray() });
            return result with { Contracts = result.Contracts.Append(contract).ToArray() };
        }

        public static GameState ReleaseDriver(GameState game, string driverId)
        {
            var result = UpdatePlayer(game, p => p with { Contracts = p.Contracts.Where(c => c.DriverId != driverId).ToArray() });
            return result with
            {
                Contracts = result.Contracts.Where(c => !(c.DriverId == driverId && c.TeamId == PlayerTeamId)).ToArray(),
            };
        }

        /// <summary>Removes every contract the driver holds (any team) and charges the player the buyout.</summary>
        public static GameState BuyoutDriver(GameState game, string driverId, string driverName, double buyoutCost)
        {
            var year = game.CurrentYear;
            var updatedContracts = game.Contracts.Where(c => c.DriverId != driverId).ToArray();
            var result = UpdatePlayer(game, p => p with
            {
                Budget = p.Budget - buyoutCost,
                Transactions = p.Transactions.Append(Tx(year, TransactionCategory.DriverBuyout, -buyoutCost, $"Bought out {driverName}")).ToArray(),
            });
            return result with { Contracts = updatedContracts };
        }

        // ---------------------------------------------------------------------------
        // Crew
        // ---------------------------------------------------------------------------

        /// <summary>Does NOT deduct annual cost; YearAdvance handles that.</summary>
        public static GameState SetCrewSize(GameState game, int size) =>
            UpdatePlayer(game, p => p with { CrewSize = size });

        // ---------------------------------------------------------------------------
        // Post-race financial actions
        // ---------------------------------------------------------------------------

        public static GameState AwardPrizeMoney(GameState game, string teamId, int position, double amount)
        {
            var year = game.CurrentYear;
            var teams = game.Teams.Select(t =>
            {
                if (t.Id != teamId) return t;
                if (t is PlayerTeam p)
                {
                    return p with
                    {
                        Budget = p.Budget + amount,
                        Transactions = p.Transactions.Append(Tx(year, TransactionCategory.PrizeMoney, amount, $"P{position} prize money")).ToArray(),
                    };
                }
                return t with { Budget = t.Budget + amount };
            }).ToArray();
            return game with { Teams = teams };
        }

        /// <summary>Charges fuel up to the player's budget; any shortfall is written off.</summary>
        public static GameState DeductFuelCost(GameState game, double totalCost)
        {
            var year = game.CurrentYear;
            return UpdatePlayer(game, p =>
            {
                var actualCost = Math.Min(p.Budget, totalCost);
                var description = actualCost < totalCost
                    ? $"Fuel cost (written off — ${ToLocaleString(totalCost - actualCost)} unpaid)"
                    : "Fuel cost";
                return p with
                {
                    Budget = p.Budget - actualCost,
                    Transactions = p.Transactions.Append(Tx(year, TransactionCategory.FuelCost, -actualCost, description)).ToArray(),
                };
            });
        }

        // ---------------------------------------------------------------------------
        // Skills
        // ---------------------------------------------------------------------------

        public static GameState AllocateSkillPoint(GameState game, PlayerSkillType skill) =>
            UpdatePlayer(game, p => p with
            {
                Skills = skill switch
                {
                    PlayerSkillType.Driver => p.Skills with { Driver = Math.Min(20, p.Skills.Driver + 1) },
                    PlayerSkillType.Engineer => p.Skills with { Engineer = Math.Min(20, p.Skills.Engineer + 1) },
                    PlayerSkillType.Business => p.Skills with { Business = Math.Min(20, p.Skills.Business + 1) },
                    _ => throw new ArgumentOutOfRangeException(nameof(skill)),
                },
            });

        // ---------------------------------------------------------------------------
        // Race history
        // ---------------------------------------------------------------------------

        public static GameState PushRaceHistory(GameState game, RaceHistoryEntry entry) =>
            game with { RaceHistory = game.RaceHistory.Append(entry).ToArray() };
    }
}
