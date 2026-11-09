#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/raceEntry.ts.</summary>
    public static class RaceEntry
    {
        /// <summary>Stable ID for the player-character driver (the player owner-driver).</summary>
        public const string PlayerCharacterDriverId = "player-character";

        /// <summary>
        /// Builds a CarEntry for every team that has an entered car. AI teams need at least one
        /// active contracted driver; the player team always includes the player character.
        /// Teams that can't be raced are skipped. PitDecider/ModeDecider are left null so
        /// SimulateRace uses its defaults.
        /// </summary>
        public static List<CarEntry> BuildCarEntries(GameState game)
        {
            var entries = new List<CarEntry>();

            foreach (BaseTeam team in game.Teams)
            {
                CarEntry? entry = BuildEntryForTeam(team, game);
                if (entry != null) entries.Add(entry);
            }

            return entries;
        }

        private static CarEntry? BuildEntryForTeam(BaseTeam team, GameState game)
        {
            if (string.IsNullOrEmpty(team.EnteredCarId)) return null;

            CarInstance? car = team.Cars.FirstOrDefault(c => c.Id == team.EnteredCarId);
            if (car == null) return null;

            CarModel? model = game.CarModels.FirstOrDefault(m => m.Id == car.ModelId);
            if (model == null) return null;

            var drivers = new List<RaceDriver>();
            PlayerTeam? player = team as PlayerTeam;

            if (player != null)
            {
                drivers.Add(PlayerCharacterRaceDriver(player));
            }

            IEnumerable<Contract> teamContracts = game.Contracts.Where(c => c.TeamId == team.Id && c.RemainingYears > 0);
            foreach (Contract contract in teamContracts)
            {
                Driver? driver = game.Drivers.FirstOrDefault(d => d.Id == contract.DriverId);
                if (driver == null) continue;
                drivers.Add(new RaceDriver { Id = driver.Id, Stats = DriverLifecycle.CalculateDriverStats(driver) });
            }
            if (drivers.Count == 0) return null;

            CarStats effective = EffectiveStats.CalculateEffectiveStats(car, model);

            return new CarEntry
            {
                CarId = car.Id,
                TeamId = team.Id,
                Instance = car,
                Model = model,
                Drivers = drivers,
                StartingDriverId = drivers[0].Id,
                StartingFuel = effective.FuelCapacity,
                TyreSetsAvailable = TyreSets(team),
                SparePartsAvailable = SpareParts(team),
                CrewSize = team.CrewSize,
                EngineerSkill = player != null ? player.Skills.Engineer : 0,
            };
        }

        private static int TyreSets(BaseTeam team) => team switch
        {
            PlayerTeam p => p.TyreSets,
            AITeam a => a.TyreSets,
            _ => 0,
        };

        private static int SpareParts(BaseTeam team) => team switch
        {
            PlayerTeam p => p.SpareParts,
            AITeam a => a.SpareParts,
            _ => 0,
        };

        private static RaceDriver PlayerCharacterRaceDriver(PlayerTeam player)
        {
            // Player-character OVR = skills.driver x 5, applied to all five stats.
            double stat = player.Skills.Driver * 5;
            return new RaceDriver
            {
                Id = PlayerCharacterDriverId,
                Stats = new DriverStats { Pace = stat, Consistency = stat, Stamina = stat, Safety = stat, Smoothness = stat },
            };
        }
    }
}
