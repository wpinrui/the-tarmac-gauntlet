#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Port of simulation/raceClock.ts. GDD §2 / Appendix A: 24 real-time minutes per race.
    /// Wall-clock time maps linearly onto sim time via wallElapsed / totalRaceSec * raceSimDuration,
    /// so every class plays back across the same 24-minute window. Tests can pass a smaller
    /// totalRaceSec to compress playback.
    /// </summary>
    public static class RaceClock
    {
        public const double TotalRaceSeconds = 1440;

        /// <summary>
        /// Sim-time at which P1 completes their final lap. Returns 0 if the race had no laps.
        /// </summary>
        public static double RaceSimDuration(RaceResultFull result)
        {
            if (result.Results.Count == 0) return 0;
            CarRaceResult leader = result.Results[0];
            if (!result.LapSnapshots.TryGetValue(leader.CarId, out IReadOnlyList<CarLapSnapshot>? snaps)) return 0;
            if (snaps.Count == 0) return 0;
            return snaps[snaps.Count - 1].TotalTime;
        }

        /// <summary>Maps wall-clock elapsed to sim-time elapsed. Clamped to [0, raceSimDuration].</summary>
        public static double WallToSim(double wallElapsedSec, RaceResultFull result, double totalRaceSec = TotalRaceSeconds)
        {
            double sim = RaceSimDuration(result);
            if (totalRaceSec <= 0 || sim <= 0) return 0;
            double ratio = Math.Min(1, Math.Max(0, wallElapsedSec / totalRaceSec));
            return ratio * sim;
        }

        /// <summary>Largest lap count for a car whose end-of-lap TotalTime is at most simElapsedSec.</summary>
        public static int LapsCompletedAtSim(IReadOnlyList<CarLapSnapshot> snapshots, double simElapsedSec)
        {
            int laps = 0;
            foreach (CarLapSnapshot s in snapshots)
            {
                if (s.TotalTime <= simElapsedSec) laps = s.LapsCompleted;
                else break;
            }
            return laps;
        }

        /// <summary>
        /// The leader's lap count at wall-clock elapsed time: whichever car has completed the
        /// most laps at that sim moment, not necessarily P1. Retired cars still count.
        /// Converges to LeaderTotalLaps at the end of the race when there is no tie.
        /// </summary>
        public static int LeaderLapAt(RaceResultFull result, double wallElapsedSec, double totalRaceSec = TotalRaceSeconds)
        {
            double sim = WallToSim(wallElapsedSec, result, totalRaceSec);
            int max = 0;
            foreach (KeyValuePair<string, IReadOnlyList<CarLapSnapshot>> kv in result.LapSnapshots)
            {
                int laps = LapsCompletedAtSim(kv.Value, sim);
                if (laps > max) max = laps;
            }
            return max;
        }

        /// <summary>Final lap count of the standings winner. 0 if results are empty.</summary>
        public static int LeaderTotalLaps(RaceResultFull result) =>
            result.Results.Count > 0 ? result.Results[0].LapsCompleted : 0;
    }
}
