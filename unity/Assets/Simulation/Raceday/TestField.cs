#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A ready-to-race year-one game for play-testing raceday without the management screens.
    /// It runs the real new-game setup, then puts the player in any model with two hired
    /// co-drivers, a small crew and some tyres and parts.
    /// </summary>
    public static class TestField
    {
        public const string PlayerCarId = "player-car";

        private const int CoDrivers = 2;

        public static GameState BuildGame(string playerModelId, int seed)
        {
            var rng = new Random(seed);
            GameState game = GameInit.InitializeGame(
                new NewGameOptions
                {
                    PlayerName = "Player",
                    Nationality = "gb",
                    TeamName = "Player Racing",
                    Skills = new PlayerSkills { Driver = 10, Engineer = 5, Business = 0 },
                },
                rng.NextDouble);

            PlayerTeam player = game.Teams.OfType<PlayerTeam>().First();
            var hired = new HashSet<string>(game.Contracts.Select(c => c.DriverId));
            List<Contract> coDrivers = game.Drivers
                .Where(d => !hired.Contains(d.Id))
                .Take(CoDrivers)
                .Select(d => new Contract { DriverId = d.Id, TeamId = player.Id, Length = 1, RemainingYears = 1 })
                .ToList();

            var car = new CarInstance { Id = PlayerCarId, ModelId = playerModelId, Age = 0, Condition = 100 };
            PlayerTeam ready = player with
            {
                Cars = new[] { car },
                EnteredCarId = car.Id,
                Contracts = coDrivers,
                CrewSize = 2,
                TyreSets = 6,
                SpareParts = 6,
            };

            return game with
            {
                Teams = game.Teams.Select(t => t == player ? ready : t).ToArray(),
                Contracts = game.Contracts.Concat(coDrivers).ToArray(),
            };
        }

        /// <summary>Race entries in grid order: highest prestige at the front (GDD §2).</summary>
        public static List<CarEntry> Grid(GameState game)
        {
            var prestige = game.Teams.ToDictionary(t => t.Id, t => t.Prestige);
            return RaceEntry.BuildCarEntries(game)
                .OrderByDescending(e => prestige[e.TeamId])
                .ToList();
        }

        public static List<CarEntry> Build(string playerModelId, int seed) => Grid(BuildGame(playerModelId, seed));
    }
}
