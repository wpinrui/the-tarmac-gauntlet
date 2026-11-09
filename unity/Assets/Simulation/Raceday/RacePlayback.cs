#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    public enum CarPhase
    {
        /// <summary>On a racing lap.</summary>
        Running,
        /// <summary>Stationary in the pit box.</summary>
        Pitting,
        /// <summary>Stopped for good after a failure.</summary>
        Retired,
    }

    /// <summary>Where one car is at a moment of the race.</summary>
    public readonly struct CarPlaybackState
    {
        public CarPlaybackState(CarPhase phase, int lap, double lapFraction, bool pitsThisLap, bool pittedLastLap)
        {
            Phase = phase;
            Lap = lap;
            LapFraction = lapFraction;
            PitsThisLap = pitsThisLap;
            PittedLastLap = pittedLastLap;
        }

        public CarPhase Phase { get; }
        /// <summary>1-based lap in progress (or the lap the car stopped on).</summary>
        public int Lap { get; }
        /// <summary>Share of the lap covered, 0 at the start line to 1 back at it.</summary>
        public double LapFraction { get; }
        /// <summary>The car leaves the track for the pit lane at the end of this lap.</summary>
        public bool PitsThisLap { get; }
        /// <summary>The car started this lap from the pit box.</summary>
        public bool PittedLastLap { get; }
    }

    /// <summary>
    /// Turns a race result into positions over time. Lap end times come straight from the
    /// snapshots. Pit stop durations come from LiveRace; without them a stop takes whatever
    /// the out-lap ran over the car's usual lap time.
    /// </summary>
    public sealed class RacePlayback
    {
        private readonly Dictionary<string, CarTimeline> timelines = new Dictionary<string, CarTimeline>();

        public RacePlayback(RaceResultFull result, IReadOnlyDictionary<string, Dictionary<int, double>>? pitDurations = null)
        {
            Result = result;
            var pitLaps = new Dictionary<string, HashSet<int>>();
            foreach (RaceEvent e in result.Events)
            {
                if (e.Type != RaceEventType.PitStop || e.CarId == null) continue;
                if (!pitLaps.TryGetValue(e.CarId, out HashSet<int>? set)) pitLaps[e.CarId] = set = new HashSet<int>();
                set.Add(e.Lap);
            }

            foreach (CarRaceResult r in result.Results)
            {
                IReadOnlyList<CarLapSnapshot> snaps =
                    result.LapSnapshots.TryGetValue(r.CarId, out IReadOnlyList<CarLapSnapshot>? s) ? s : Array.Empty<CarLapSnapshot>();
                Dictionary<int, double>? stops = null;
                pitDurations?.TryGetValue(r.CarId, out stops);
                timelines[r.CarId] = new CarTimeline(
                    r, snaps, pitLaps.TryGetValue(r.CarId, out HashSet<int>? p) ? p : new HashSet<int>(), stops);
            }
        }

        public RaceResultFull Result { get; }

        public CarPlaybackState StateAt(string carId, double simTime) => timelines[carId].StateAt(simTime);

        /// <summary>
        /// Running order at simTime: more laps first, then whoever reached that lap count sooner.
        /// Retired cars drop below every running car.
        /// </summary>
        public IReadOnlyList<string> OrderAt(double simTime) =>
            timelines.Values
                .OrderBy(t => t.RetiredBy(simTime))
                .ThenByDescending(t => t.LapsAt(simTime))
                .ThenBy(t => t.LastLapEndAt(simTime))
                .Select(t => t.CarId)
                .ToList();

        public int LapsAt(string carId, double simTime) => timelines[carId].LapsAt(simTime);

        public bool RetiredBy(string carId, double simTime) => timelines[carId].RetiredBy(simTime);

        /// <summary>Latest snapshot at or before simTime, or null before the first lap ends.</summary>
        public CarLapSnapshot? SnapshotAt(string carId, double simTime) => timelines[carId].SnapshotAt(simTime);

        private sealed class CarTimeline
        {
            private readonly CarRaceResult result;
            private readonly IReadOnlyList<CarLapSnapshot> snaps;
            private readonly HashSet<int> pitLaps;
            /// <summary>Lap start times, with any pit stop before the lap already served.</summary>
            private readonly double[] lapStart;
            /// <summary>Lap end times, before any pit stop at the end of the lap.</summary>
            private readonly double[] lapEnd;
            private readonly double typicalLap;

            public CarTimeline(
                CarRaceResult result, IReadOnlyList<CarLapSnapshot> snaps, HashSet<int> pitLaps, Dictionary<int, double>? stops)
            {
                this.result = result;
                this.snaps = snaps;
                this.pitLaps = pitLaps;

                int laps = snaps.Count + (result.Retired ? 1 : 0);
                var ends = new double[laps];
                for (int i = 0; i < snaps.Count; i++) ends[i] = snaps[i].TotalTime;
                if (result.Retired) ends[laps - 1] = result.TotalTime;

                var clean = new List<double>();
                for (int i = 0; i < laps; i++)
                {
                    double prev = i == 0 ? 0 : ends[i - 1];
                    if (i == 0 || !pitLaps.Contains(i)) clean.Add(ends[i] - prev);
                }
                clean.Sort();
                typicalLap = clean.Count > 0 ? clean[clean.Count / 2] : 30;

                lapStart = new double[laps];
                lapEnd = ends;
                for (int i = 0; i < laps; i++)
                {
                    double prevEnd = i == 0 ? 0 : ends[i - 1];
                    bool afterPit = i > 0 && pitLaps.Contains(i);
                    double stop = !afterPit ? 0
                        : stops != null && stops.TryGetValue(i, out double known) ? known
                        : Math.Max(0, ends[i] - prevEnd - typicalLap);
                    lapStart[i] = prevEnd + stop;
                }
            }

            public string CarId => result.CarId;

            public int LapsAt(double t)
            {
                int laps = 0;
                for (int i = 0; i < snaps.Count && snaps[i].TotalTime <= t; i++) laps = i + 1;
                return laps;
            }

            public double LastLapEndAt(double t)
            {
                int laps = LapsAt(t);
                return laps == 0 ? 0 : snaps[laps - 1].TotalTime;
            }

            public bool RetiredBy(double t) => result.Retired && t >= RetireTime;

            public CarLapSnapshot? SnapshotAt(double t)
            {
                int laps = LapsAt(t);
                return laps == 0 ? null : snaps[laps - 1];
            }

            /// <summary>The car stops partway round its last lap, at a spot fixed by its id.</summary>
            private double RetireFraction => 0.15 + 0.7 * (StableHash(CarId) % 1000 / 1000.0);

            private static uint StableHash(string s)
            {
                uint h = 2166136261;
                foreach (char c in s) h = (h ^ c) * 16777619;
                return h;
            }

            private double RetireTime
            {
                get
                {
                    int i = lapEnd.Length - 1;
                    return lapStart[i] + (lapEnd[i] - lapStart[i]) * RetireFraction;
                }
            }

            public CarPlaybackState StateAt(double t)
            {
                int count = lapEnd.Length;
                if (count == 0) return new CarPlaybackState(CarPhase.Running, 1, Math.Max(0, t) / typicalLap % 1, false, false);

                if (RetiredBy(t)) return new CarPlaybackState(CarPhase.Retired, count, RetireFraction, false, pitLaps.Contains(count - 1));

                for (int i = 0; i < count; i++)
                {
                    int lap = i + 1;
                    bool pitsThisLap = pitLaps.Contains(lap);
                    bool pittedLast = i > 0 && pitLaps.Contains(i);
                    if (t < lapStart[i])
                    {
                        // Waiting in the pit box after the previous lap.
                        return new CarPlaybackState(CarPhase.Pitting, i, 1, true, i > 1 && pitLaps.Contains(i - 1));
                    }
                    if (t < lapEnd[i])
                    {
                        double frac = (t - lapStart[i]) / Math.Max(1e-6, lapEnd[i] - lapStart[i]);
                        return new CarPlaybackState(CarPhase.Running, lap, frac, pitsThisLap, pittedLast);
                    }
                }

                // Out of race time: keep circulating at the usual pace so the field does not freeze.
                double since = t - lapEnd[count - 1];
                int extra = (int)(since / typicalLap);
                return new CarPlaybackState(CarPhase.Running, count + 1 + extra, since / typicalLap - extra, false, false);
            }
        }
    }
}
