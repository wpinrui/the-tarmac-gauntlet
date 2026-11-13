#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Changes to a career for the Team HQ's debug menu, so any part of the game can be tested without playing up to
    /// it: money, cars, drivers, stock, crew and skills, and a race run in an instant with the player's car finishing
    /// where it is asked to. Each is a pure function on <see cref="GameState"/>, as <see cref="GameActions"/> are.
    /// </summary>
    public static class DebugActions
    {
        /// <summary>Laps the leader does in a race run by <see cref="Race"/>, as the GDD's 24 hours.</summary>
        public const int RaceLaps = 48;
        /// <summary>Laps the last car does fewer than the leader in a race run by <see cref="Race"/>.</summary>
        public const int LapSpread = 12;
        /// <summary>Seconds of race clock a lap takes in a race run by <see cref="Race"/>.</summary>
        public const double LapSeconds = 180;

        private static PlayerTeam Player(GameState game) => game.Teams.OfType<PlayerTeam>().Single();

        private static GameState WithPlayer(GameState game, Func<PlayerTeam, PlayerTeam> change) =>
            game with { Teams = game.Teams.Select(t => t is PlayerTeam p ? change(p) : t).ToArray() };

        public static GameState AddMoney(GameState game, double amount) => WithPlayer(game, p => p with { Budget = p.Budget + amount });

        public static GameState SetMoney(GameState game, double amount) => WithPlayer(game, p => p with { Budget = amount });

        /// <summary>A new car of the model, free, entered for the race.</summary>
        public static GameState GiveCar(GameState game, string modelId)
        {
            CarModel model = CarModels.All.First(m => m.Id == modelId);
            var car = new CarInstance { Id = DealerData.NextCarId(), ModelId = model.Id, Condition = 100 };
            game = GameActions.BuyCar(game, car, 0, model.Name);
            return GameActions.EnterCar(game, car.Id);
        }

        /// <summary>Every one of the player's cars at this condition, 0 to 100.</summary>
        public static GameState SetCondition(GameState game, double condition) =>
            WithPlayer(game, p => p with { Cars = p.Cars.Select(c => c with { Condition = Math.Max(0, Math.Min(100, condition)) }).ToArray() });

        /// <summary>The free agents, fastest first.</summary>
        public static IReadOnlyList<(Driver Driver, DriverStats Stats)> FreeAgents(GameState game) =>
            game.Drivers
                .Where(d => DriverLifecycle.IsDriverFreeAgent(d.Id, game.Contracts))
                .Select(d => (d, DriverLifecycle.CalculateDriverStats(d)))
                .OrderByDescending(x => x.Item2.Pace)
                .ToArray();

        /// <summary>Hires the driver on a one-year contract with no salary.</summary>
        public static GameState Hire(GameState game, string driverId)
        {
            if (!DriverLifecycle.IsDriverFreeAgent(driverId, game.Contracts)) return game;
            Driver driver = game.Drivers.First(d => d.Id == driverId);
            return GameActions.HireDriver(game, driverId, driver.Name, 1, 0);
        }

        /// <summary>Ends every one of the player's driver contracts.</summary>
        public static GameState ReleaseAll(GameState game)
        {
            foreach (Contract c in game.Contracts.Where(c => c.TeamId == GameActions.PlayerTeamId).ToArray())
                game = GameActions.ReleaseDriver(game, c.DriverId);
            return game;
        }

        public static GameState AddTyres(GameState game, int sets) => GameActions.BuyTyres(game, sets, 0);

        public static GameState AddSpares(GameState game, int parts) => GameActions.BuySpares(game, parts, 0);

        public static GameState SetCrew(GameState game, int size) => GameActions.SetCrewSize(game, size);

        /// <summary>Every player skill at its top level.</summary>
        public static GameState MaxSkills(GameState game) =>
            WithPlayer(game, p => p with { Skills = new PlayerSkills { Driver = 20, Engineer = 20, Business = 20 } });

        /// <summary>Ends the year without a race, as putting the paper down after one does: drivers age, contracts run down, the market moves.</summary>
        public static GameState EndYear(GameState game, Func<double>? random = null) =>
            Season.AdvanceYear(game with { Phase = GamePhase.PostRace }, random);

        /// <summary>The career moved on to <paramref name="year"/>, a year at a time, each ended without a race.</summary>
        public static GameState ToYear(GameState game, int year, Func<double>? random = null)
        {
            while (game.CurrentYear < year) game = EndYear(game, random);
            return game;
        }

        /// <summary>
        /// A team built up for testing: the cheapest car of the class, entered; the fastest free agents hired; crew,
        /// stock, money and every skill at the levels given.
        /// </summary>
        public static GameState Equip(GameState game, CarClass carClass, int drivers, int crew, double money, int skills)
        {
            CarModel model = CarModels.All.Where(m => m.CarClass == carClass).OrderBy(m => m.Price).First();
            game = GiveCar(game, model.Id);
            foreach ((Driver d, DriverStats _) in FreeAgents(game).Take(drivers).ToArray()) game = Hire(game, d.Id);
            game = AddSpares(AddTyres(SetCrew(game, crew), 12), 12);
            game = SetMoney(game, money);
            return WithPlayer(game, p => p with { Skills = new PlayerSkills { Driver = skills, Engineer = skills, Business = skills } });
        }

        /// <summary>
        /// The race's field cut down for testing: the player's car and the cars nearest the front, <paramref name="size"/>
        /// in all, in grid order, or the whole field for 0. With <paramref name="modelId"/> the player's car is that model,
        /// for this race only.
        /// </summary>
        public static RaceSetup Field(RaceSetup setup, int size, string? modelId)
        {
            IReadOnlyList<GridCar> grid = setup.Grid;
            CarModel? model = modelId != null ? CarModels.All.FirstOrDefault(m => m.Id == modelId) : null;
            if (model != null)
                grid = grid.Select(g =>
                {
                    if (!g.Player) return g;
                    CarInstance car = g.Car with { ModelId = model.Id };
                    return g with { Car = car, Model = model, StartingFuel = EffectiveStats.CalculateEffectiveStats(car, model).FuelCapacity };
                }).ToArray();
            if (size > 0 && size < grid.Count)
            {
                GridCar? me = grid.FirstOrDefault(g => g.Player);
                var kept = new HashSet<GridCar>(grid.Where(g => !g.Player).Take(me != null ? size - 1 : size));
                if (me != null) kept.Add(me);
                grid = grid.Where(kept.Contains).ToArray();
            }
            return setup with { Grid = grid };
        }

        /// <summary>
        /// Runs this year's race in an instant: the field finishes in grid order, a few laps apart, with no issues or
        /// failures, and the player's car finishes in <paramref name="place"/> (1 for a win), or retires at half
        /// distance when <paramref name="retire"/> is set. Returns the race to settle with <see cref="RaceWeekend.Finish"/>;
        /// <see cref="RaceWeekend.Setup"/> is set as for a race about to start.
        /// </summary>
        public static RaceDay Race(GameState game, int place, bool retire = false)
        {
            RaceSetup setup = RaceWeekend.Begin(game);
            var day = new RaceDay(setup, () => 0.999);
            int n = day.Cars.Count;
            if (n == 0) return day;

            // The finishing order: the field in grid order, the player's car moved to its place.
            List<CarRaceLog> order = day.Cars.ToList();
            CarRaceLog? me = order.FirstOrDefault(c => c.Car.Player);
            if (me != null)
            {
                order.Remove(me);
                order.Insert(Math.Max(0, Math.Min(n - 1, place - 1)), me);
            }
            var laps = new int[n];
            for (int r = 0; r < n; r++) laps[order[r].Index] = RaceLaps - r * LapSpread / n;
            if (me != null && retire) laps[me.Index] = RaceLaps / 2;

            // Every lap but each car's last, then the last laps in finishing order: the leader takes the flag first.
            for (int lap = 1; lap <= RaceLaps; lap++)
                for (int r = 0; r < n; r++)
                {
                    CarRaceLog c = order[r];
                    if (lap >= laps[c.Index]) continue;
                    day.CompleteLap(c.Index, lap * LapSeconds + r * 0.01, LapSeconds + r * 0.001, InstructionMode.Normal, false);
                }
            for (int r = 0; r < n; r++)
            {
                CarRaceLog c = order[r];
                int lap = laps[c.Index];
                double time = lap * LapSeconds + r * 0.01;
                bool retiring = retire && c == me;
                day.CompleteLap(c.Index, time, LapSeconds + r * 0.001, InstructionMode.Normal, !retiring);
                if (retiring) c.Retire(time, FailureType.Mechanical);
            }
            return day;
        }
    }
}
