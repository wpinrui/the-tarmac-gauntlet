using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What the driver card shows of a driver: their initial and surname, their nationality and their stats,
    /// with the overall as the menus work it out, the average of the five. No Unity objects, so it can be
    /// checked outside Unity.
    /// </summary>
    public sealed class DriverFacts
    {
        public readonly string Id;
        /// <summary>"K." for Kenji Tanaka, or empty for a one-word name.</summary>
        public readonly string Initial;
        /// <summary>"TANAKA" for Kenji Tanaka.</summary>
        public readonly string Surname;
        /// <summary>ISO 3166-1 alpha-2 country code, lower case, as the flags are named. Empty if unknown.</summary>
        public readonly string Nation;
        public readonly int Ovr;
        /// <summary>Pace, consistency, stamina, safety and smoothness, rounded.</summary>
        public readonly int[] Stats;

        private DriverFacts(string id, string initial, string surname, string nation, int ovr, int[] stats)
        {
            Id = id;
            Initial = initial;
            Surname = surname;
            Nation = nation;
            Ovr = ovr;
            Stats = stats;
        }

        public static DriverFacts Of(RaceDriver driver, GameState game)
        {
            string full = game != null ? RaceWeekend.DriverName(driver.Id, game) : driver.Id;
            full = (full ?? "").Trim();
            int space = full.LastIndexOf(' ');
            string initial = space > 0 ? full.Substring(0, 1).ToUpperInvariant() + "." : "";
            string surname = (space > 0 ? full.Substring(space + 1) : full).ToUpperInvariant();
            DriverStats s = driver.Stats;
            int[] stats = { Stat(s.Pace), Stat(s.Consistency), Stat(s.Stamina), Stat(s.Safety), Stat(s.Smoothness) };
            int ovr = (int)JsMath.Round(DriverLifecycle.TotalDriverStats(s) / 5);
            return new DriverFacts(driver.Id, initial, surname, Nationality(driver.Id, game), ovr, stats);
        }

        private static int Stat(double v) => (int)JsMath.Round(v);

        /// <summary>The driver's country: the player's own, picked at new game, or the hired driver's.</summary>
        public static string Nationality(string id, GameState game)
        {
            if (game == null) return "";
            if (id == RaceEntry.PlayerCharacterDriverId) return game.Teams.OfType<PlayerTeam>().FirstOrDefault()?.Nationality ?? "";
            return game.Drivers.FirstOrDefault(d => d.Id == id)?.Nationality ?? "";
        }
    }

    /// <summary>
    /// Who takes over at a car's coming stop, while it rolls in: from the moment it enters the pit lane until
    /// the stop is made. Nobody while the stop keeps the same driver, or once the race is over for the car.
    /// Every car is followed, so the camera can move to any car already in the pit lane.
    /// </summary>
    public sealed class NextDriverWatch
    {
        /// <summary>Per car, the stops it had made as it entered the pit lane, or -1 while it is out of it.</summary>
        private readonly Dictionary<int, int> entered = new Dictionary<int, int>();

        /// <summary>Called every frame for every car.</summary>
        public void Step(int car, bool inPitLane, CarRaceLog log)
        {
            if (!inPitLane || log == null) entered.Remove(car);
            else if (!entered.ContainsKey(car)) entered[car] = log.PitStops.Count;
        }

        /// <summary>The driver who gets in at this car's stop, or null.</summary>
        public RaceDriver Next(int car, CarRaceLog log)
        {
            if (log == null || log.Retired || log.Flagged) return null;
            if (!entered.TryGetValue(car, out int stops) || log.PitStops.Count != stops) return null;
            if (log.NextDriverIndex == log.DriverIndex) return null;
            return log.Car.Lineup[log.NextDriverIndex];
        }
    }
}
