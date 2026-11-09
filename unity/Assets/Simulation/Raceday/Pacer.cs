#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A hidden car that keeps LiveRace's random streams apart.
    ///
    /// When a car retires or runs out of time it stops calling back, so the next car's draws
    /// for that one lap still come from the stopped car's stream. Running the pacer straight
    /// after the player means that car is always the pacer, never a real one. The pacer cannot
    /// fail (driver Safety 200 makes the failure chance exactly 0) and laps far faster than
    /// anyone, so by the time it runs out of time every other car has stopped. It is removed
    /// from the result before anything sees it.
    /// </summary>
    internal static class Pacer
    {
        public const string CarId = "__pacer";
        /// <summary>A class of its own, so it never leads a real class.</summary>
        private const CarClass PacerClass = (CarClass)99;

        private static readonly CarModel Model = new CarModel
        {
            Id = CarId,
            Name = CarId,
            CarClass = PacerClass,
            BaseStats = new CarStats
            {
                Power = 100, Handling = 100, FuelEfficiency = 100, TyreDurability = 100,
                Comfort = 100, Reliability = 100, PitStopTime = 0, FuelCapacity = 1_000,
            },
        };

        public static readonly CarEntry Entry = new CarEntry
        {
            CarId = CarId,
            TeamId = CarId,
            Instance = new CarInstance { Id = CarId, ModelId = CarId, Condition = 100 },
            Model = Model,
            Drivers = new[]
            {
                new RaceDriver
                {
                    Id = CarId,
                    Stats = new DriverStats { Pace = 400, Consistency = 100, Stamina = 100, Safety = 200, Smoothness = 100 },
                },
            },
            StartingDriverId = CarId,
            StartingFuel = 1_000,
            TyreSetsAvailable = 1_000,
            SparePartsAvailable = 1_000,
            CrewSize = 20,
            // Never pit: a pit stop is time the pacer does not need.
            PitDecider = (lap, snap) => null,
        };

        /// <summary>The result as if the pacer had never raced.</summary>
        public static RaceResultFull Strip(RaceResultFull raw)
        {
            int slot = raw.CarIndexById[CarId];

            var history = raw.PositionHistory.Select(lap =>
            {
                int pacerPos = lap[slot];
                return (IReadOnlyList<int>)lap
                    .Where((_, i) => i != slot)
                    .Select(p => p > pacerPos ? p - 1 : p)
                    .ToArray();
            }).ToList();

            var index = raw.CarIndexById
                .Where(kv => kv.Key != CarId)
                .ToDictionary(kv => kv.Key, kv => kv.Value > slot ? kv.Value - 1 : kv.Value);

            CarRaceResult[] results = raw.Results
                .Where(r => r.CarId != CarId)
                .Select((r, i) => r with { FinalPosition = i + 1 })
                .ToArray();

            var snapshots = raw.LapSnapshots.Where(kv => kv.Key != CarId).ToDictionary(kv => kv.Key, kv => kv.Value);

            return raw with
            {
                Results = results,
                FastestLap = FastestLap(snapshots, raw.Events),
                LapSnapshots = snapshots,
                PositionHistory = history,
                CarIndexById = index,
                Events = raw.Events.Where(e => e.CarId != CarId && e.Type != RaceEventType.FastestLap).ToArray(),
                Stints = raw.Stints.Where(kv => kv.Key != CarId).ToDictionary(kv => kv.Key, kv => kv.Value),
                ModeCounters = raw.ModeCounters.Where(kv => kv.Key != CarId).ToDictionary(kv => kv.Key, kv => kv.Value),
            };
        }

        /// <summary>
        /// Fastest lap among real cars. A lap's time is the gap between snapshots unless the car
        /// pitted before it, when the gap also holds the stop; those laps are skipped.
        /// </summary>
        private static RaceFastestLap? FastestLap(
            IReadOnlyDictionary<string, IReadOnlyList<CarLapSnapshot>> snapshots,
            IReadOnlyList<RaceEvent> events)
        {
            var pits = new HashSet<(string, int)>(
                events.Where(e => e.Type == RaceEventType.PitStop && e.CarId != null).Select(e => (e.CarId!, e.Lap)));

            RaceFastestLap? best = null;
            foreach (KeyValuePair<string, IReadOnlyList<CarLapSnapshot>> kv in snapshots)
            {
                double prev = 0;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    double time = kv.Value[i].TotalTime - prev;
                    prev = kv.Value[i].TotalTime;
                    if (i > 0 && pits.Contains((kv.Key, i))) continue;
                    if (best == null || time < best.Time) best = new RaceFastestLap { CarId = kv.Key, Lap = i + 1, Time = time };
                }
            }
            return best;
        }
    }
}
