#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>What the player wants done at their next pit stop.</summary>
    public sealed record PitPlan
    {
        /// <summary>Fill the tank to its current capacity.</summary>
        public bool Refuel { get; init; } = true;
        public bool ChangeTyres { get; init; } = true;
        /// <summary>Fix every active issue the spare parts allow.</summary>
        public bool FixIssues { get; init; } = true;
        /// <summary>Driver who takes over. Null keeps the current driver.</summary>
        public string? NextDriverId { get; init; }
    }

    /// <summary>The player's car as the dashboard shows it at one moment.</summary>
    public sealed record PlayerGauges
    {
        public int LapsCompleted { get; init; }
        /// <summary>0 fresh to 100 worn.</summary>
        public double TyreWear { get; init; }
        public double Fuel { get; init; }
        public double FuelCapacity { get; init; }
        /// <summary>0 fresh to 100 exhausted, for the driver at the wheel.</summary>
        public double Fatigue { get; init; }
        public double Condition { get; init; }
        public string DriverId { get; init; } = "";
        public int TyreSets { get; init; }
        public int SpareParts { get; init; }
        /// <summary>Issues known so far: only laps already completed.</summary>
        public IReadOnlyList<ActiveIssue> Issues { get; init; } = Array.Empty<ActiveIssue>();
        /// <summary>The snapshot the advisory works from (state at the start of the lap in progress).</summary>
        public CarLapSnapshot Basis { get; init; } = new CarLapSnapshot();
    }

    /// <summary>
    /// A race the player steers while it plays back in real time.
    ///
    /// RaceLoop.SimulateRace runs a whole race in one call, so live decisions work by
    /// re-simulating. Decisions for laps the playback has already passed are recorded and
    /// replayed; laps still ahead use the live choice. Each car draws from its own random
    /// stream, so a new player decision changes the player's future and leaves every other
    /// car's laps where they were.
    ///
    /// A hidden pacer car runs straight after the player; see <see cref="Pacer"/>.
    /// </summary>
    public sealed class LiveRace
    {
        private readonly IReadOnlyList<CarEntry> baseEntries;
        private readonly RaceOptions baseOptions;
        private readonly int seed;
        private readonly int playerIndex;

        // Decisions recorded at the end of each player lap (lap number to value).
        private readonly Dictionary<int, InstructionMode> lockedModes = new Dictionary<int, InstructionMode>();
        private readonly Dictionary<int, PitStopConfig?> lockedPits = new Dictionary<int, PitStopConfig?>();
        private Dictionary<int, InstructionMode> pendingModes = new Dictionary<int, InstructionMode>();
        private Dictionary<int, PitStopConfig?> pendingPits = new Dictionary<int, PitStopConfig?>();

        private InstructionMode mode = InstructionMode.Normal;
        private PitPlan? pitPlan;

        public LiveRace(IReadOnlyList<CarEntry> entries, string playerCarId, int seed, RaceOptions? options = null)
        {
            baseEntries = entries;
            baseOptions = options ?? new RaceOptions
            {
                IssueTemplates = IssueTemplates.MechanicalIssues,
                CrashTemplates = IssueTemplates.CrashIssueTemplates,
            };
            this.seed = seed;
            PlayerCarId = playerCarId;
            playerIndex = entries.ToList().FindIndex(e => e.CarId == playerCarId);
            if (playerIndex < 0) throw new ArgumentException("Player car is not entered.", nameof(playerCarId));
            Result = Simulate();
        }

        public string PlayerCarId { get; }
        public CarEntry PlayerEntry => baseEntries[playerIndex];
        public IReadOnlyList<CarEntry> Entries => baseEntries;

        /// <summary>The race as it will play out given every decision so far.</summary>
        public RaceResultFull Result { get; private set; }

        /// <summary>Pit stop durations from the latest simulation: car id to (lap to seconds).</summary>
        public IReadOnlyDictionary<string, Dictionary<int, double>> PitDurations { get; private set; } =
            new Dictionary<string, Dictionary<int, double>>();

        /// <summary>Player laps whose decisions are fixed, because playback has passed them.</summary>
        public int LockedLaps { get; private set; }

        /// <summary>Instruction for the player's car. Takes effect from the next lap.</summary>
        public InstructionMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                Result = Simulate();
            }
        }

        /// <summary>Pit at the end of the current lap with this plan. Null cancels the call.</summary>
        public PitPlan? PitPlan
        {
            get => pitPlan;
            set
            {
                if (pitPlan == value) return;
                pitPlan = value;
                Result = Simulate();
            }
        }

        /// <summary>
        /// Locks every player lap that ends at or before simTime. When a locked lap ended in a
        /// pit stop, the pit call is used up and the mode resets to Normal (GDD §2).
        /// </summary>
        public void AdvanceTo(double simTime)
        {
            IReadOnlyList<CarLapSnapshot> snaps = Result.LapSnapshots[PlayerCarId];
            int laps = RaceClock.LapsCompletedAtSim(snaps, simTime);
            if (laps <= LockedLaps) return;

            bool pitted = false;
            for (int lap = LockedLaps + 1; lap <= laps; lap++)
            {
                if (pendingModes.TryGetValue(lap, out InstructionMode m)) lockedModes[lap] = m;
                if (pendingPits.TryGetValue(lap, out PitStopConfig? p)) lockedPits[lap] = p;
                if (PlayerPittedOnLap(lap)) pitted = true;
            }
            LockedLaps = laps;

            if (pitted)
            {
                // The sim already reset the car to Normal; reset the live choice to match.
                mode = InstructionMode.Normal;
                pitPlan = null;
                Result = Simulate();
            }
        }

        /// <summary>
        /// The player's gauges at simTime. Wear, fuel, fatigue and condition move smoothly from
        /// the start of the lap in progress to its end; issues only show once a lap has found them.
        /// </summary>
        public PlayerGauges GaugesAt(double simTime)
        {
            IReadOnlyList<CarLapSnapshot> snaps = Result.LapSnapshots[PlayerCarId];
            int done = RaceClock.LapsCompletedAtSim(snaps, simTime);
            CarLapSnapshot start = done > 0 ? AfterPit(snaps[done - 1], done) : StartSnapshot();
            if (done >= snaps.Count) return Gauges(start, start, 0, done);

            CarLapSnapshot end = snaps[done];
            double startTime = done > 0 ? snaps[done - 1].TotalTime : 0;
            if (done > 0 && PitDurations[PlayerCarId].TryGetValue(done, out double stop)) startTime += stop;
            double u = end.TotalTime > startTime ? (simTime - startTime) / (end.TotalTime - startTime) : 0;
            return Gauges(start, end, Math.Max(0, Math.Min(1, u)), done);
        }

        private PlayerGauges Gauges(CarLapSnapshot a, CarLapSnapshot b, double u, int done)
        {
            double Lerp(double x, double y) => x + (y - x) * u;
            double fatigueA = a.DriverFatigue.TryGetValue(a.CurrentDriverId, out double fa) ? fa : 0;
            double fatigueB = b.DriverFatigue.TryGetValue(a.CurrentDriverId, out double fb) ? fb : fatigueA;
            return new PlayerGauges
            {
                LapsCompleted = done,
                TyreWear = Lerp(a.TyreWear, b.TyreWear),
                Fuel = Lerp(a.FuelRemaining, b.FuelRemaining),
                FuelCapacity = a.FuelCapacity,
                Fatigue = Lerp(fatigueA, fatigueB),
                Condition = Lerp(a.Condition, b.Condition),
                DriverId = a.CurrentDriverId,
                TyreSets = a.TyreSetsAvailable,
                SpareParts = a.SparePartsAvailable,
                Issues = a.ActiveIssues,
                Basis = a,
            };
        }

        private CarLapSnapshot StartSnapshot()
        {
            CarEntry e = PlayerEntry;
            CarStats stats = EffectiveStats.CalculateEffectiveStats(e.Instance, e.Model);
            return new CarLapSnapshot
            {
                TyreWear = e.StartingTyreWear ?? 0,
                FuelRemaining = e.StartingFuel,
                FuelCapacity = stats.FuelCapacity,
                Condition = e.Instance.Condition,
                CurrentDriverId = e.StartingDriverId,
                DriverFatigue = e.Drivers.ToDictionary(d => d.Id, d => 0.0),
                InstructionMode = InstructionMode.Normal,
                TyreSetsAvailable = e.TyreSetsAvailable,
                SparePartsAvailable = e.SparePartsAvailable,
            };
        }

        /// <summary>A lap's end snapshot with that lap's pit stop applied, as the next lap starts.</summary>
        private CarLapSnapshot AfterPit(CarLapSnapshot snap, int lap)
        {
            if (!PlayerPittedOnLap(lap)) return snap;
            IReadOnlyList<CarLapSnapshot> snaps = Result.LapSnapshots[PlayerCarId];
            PitStopConfig config = lockedPits.TryGetValue(lap, out PitStopConfig? p) && p != null
                ? p
                : new PitStopConfig { FuelToAdd = snap.FuelCapacity, ChangeTyres = snap.TyreSetsAvailable > 0 };
            bool tyres = config.ChangeTyres && snap.TyreSetsAvailable > 0;
            // The next snapshot (if any) carries the driver, stock and issues after the stop.
            CarLapSnapshot? next = lap < snaps.Count ? snaps[lap] : null;
            return snap with
            {
                TyreWear = tyres ? 0 : snap.TyreWear,
                FuelRemaining = Math.Min(snap.FuelCapacity, snap.FuelRemaining + config.FuelToAdd),
                CurrentDriverId = next?.CurrentDriverId ?? snap.CurrentDriverId,
                TyreSetsAvailable = next?.TyreSetsAvailable ?? snap.TyreSetsAvailable - (tyres ? 1 : 0),
                SparePartsAvailable = next?.SparePartsAvailable ?? snap.SparePartsAvailable,
                ActiveIssues = snap.ActiveIssues.Where(i => config.IssueIdsToFix?.Contains(i.TemplateId) != true).ToArray(),
                InstructionMode = InstructionMode.Normal,
            };
        }

        public bool PlayerPittedOnLap(int lap) =>
            Result.Events.Any(e => e.Type == RaceEventType.PitStop && e.CarId == PlayerCarId && e.Lap == lap);

        private RaceResultFull Simulate()
        {
            pendingModes = new Dictionary<int, InstructionMode>();
            pendingPits = new Dictionary<int, PitStopConfig?>();
            var streams = new CarRandomStreams(seed, baseEntries.Count);
            var pitDurations = new Dictionary<string, Dictionary<int, double>>();
            PitDurations = pitDurations;

            var entries = new List<CarEntry>(baseEntries.Count);
            for (int i = 0; i < baseEntries.Count; i++)
            {
                var stops = new Dictionary<int, double>();
                pitDurations[baseEntries[i].CarId] = stops;
                CarEntry entry = baseEntries[i] with { OnPitStop = (lap, duration) => stops[lap] = duration };
                int index = i;
                if (i == playerIndex)
                {
                    entries.Add(entry with
                    {
                        ModeDecider = (lap, snap) =>
                        {
                            streams.LapFinished(index);
                            return DecideMode(lap);
                        },
                        PitDecider = (lap, snap) => DecidePit(lap, snap),
                    });
                }
                else
                {
                    Func<int, CarLapSnapshot, InstructionMode>? inner = entry.ModeDecider;
                    entries.Add(entry with
                    {
                        ModeDecider = (lap, snap) =>
                        {
                            streams.LapFinished(index);
                            return inner != null ? inner(lap, snap) : InstructionMode.Normal;
                        },
                    });
                }
            }

            // The pacer goes right after the player, so it is the car that absorbs the player's
            // last-lap draws whenever the player retires or runs out of time.
            int pacerIndex = entries.Count;
            entries.Insert(playerIndex + 1, Pacer.Entry with
            {
                ModeDecider = (lap, snap) =>
                {
                    streams.LapFinished(pacerIndex);
                    return InstructionMode.Normal;
                },
            });
            streams.SetOrder(entries.Count, playerIndex + 1, pacerIndex);

            RaceResultFull raw = RaceLoop.SimulateRace(entries, baseOptions with { Random = streams.Next });
            return Pacer.Strip(raw);
        }

        private InstructionMode DecideMode(int lap)
        {
            if (lap <= LockedLaps) return lockedModes.TryGetValue(lap, out InstructionMode m) ? m : InstructionMode.Normal;
            pendingModes[lap] = mode;
            return mode;
        }

        private PitStopConfig? DecidePit(int lap, CarLapSnapshot snap)
        {
            if (lap <= LockedLaps) return lockedPits.TryGetValue(lap, out PitStopConfig? p) ? p : null;

            // A pit call is for the lap in progress; later laps run on until the player calls again.
            PitStopConfig? config = pitPlan != null && lap == LockedLaps + 1 ? BuildConfig(pitPlan, snap, PlayerEntry) : null;
            pendingPits[lap] = config;
            return config;
        }

        private static PitStopConfig BuildConfig(PitPlan plan, CarLapSnapshot snap, CarEntry entry) => new PitStopConfig
        {
            FuelToAdd = plan.Refuel ? Math.Max(0, snap.FuelCapacity - snap.FuelRemaining) : 0,
            ChangeTyres = plan.ChangeTyres && snap.TyreSetsAvailable > 0,
            NextDriverId = plan.NextDriverId != snap.CurrentDriverId && entry.Drivers.Any(d => d.Id == plan.NextDriverId)
                ? plan.NextDriverId
                : null,
            IssueIdsToFix = plan.FixIssues ? snap.ActiveIssues.Select(i => i.TemplateId).ToArray() : Array.Empty<string>(),
        };

        /// <summary>
        /// Gives each car its own random stream. RaceLoop calls the random source for one car at a
        /// time, in entry order, and calls ModeDecider once a car finishes its lap, so the car
        /// drawing is the next running car after the one that last finished. A car that retires
        /// or runs out of time never calls back; the next callback reveals the gap and those cars
        /// are skipped from then on.
        /// </summary>
        private sealed class CarRandomStreams
        {
            private ulong[] state;
            private bool[] stopped;
            /// <summary>Stream id for each slot in entry order.</summary>
            private int[] streamAt;
            /// <summary>Entry slot for each stream id.</summary>
            private int[] slotOf;
            /// <summary>Entry slot of the car drawing now.</summary>
            private int current;

            public CarRandomStreams(int seed, int count)
            {
                state = new ulong[count];
                stopped = new bool[count];
                streamAt = new int[count];
                slotOf = new int[count];
                for (int i = 0; i < count; i++)
                {
                    state[i] = Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)i * 0xD1B54A32D192ED03UL + 1);
                    streamAt[i] = i;
                    slotOf[i] = i;
                }
            }

            /// <summary>Adds one stream, whose car sits at entry slot <paramref name="slot"/>.</summary>
            public void SetOrder(int count, int slot, int stream)
            {
                int old = state.Length;
                Array.Resize(ref state, count);
                Array.Resize(ref stopped, count);
                state[stream] = Mix(0xA5A5A5A5A5A5A5A5UL);
                var order = new List<int>(Enumerable.Range(0, old));
                order.Insert(slot, stream);
                streamAt = order.ToArray();
                slotOf = new int[count];
                for (int i = 0; i < count; i++) slotOf[streamAt[i]] = i;
            }

            public double Next()
            {
                if (state.Length == 0) return 0;
                int s = streamAt[current];
                state[s] += 0x9E3779B97F4A7C15UL;
                return (Mix(state[s]) >> 11) * (1.0 / (1UL << 53));
            }

            /// <summary>Called when the car with stream id <paramref name="stream"/> finishes a lap.</summary>
            public void LapFinished(int stream)
            {
                int slot = slotOf[stream];
                int n = state.Length;
                // Cars between the expected one and this one dropped out of the race.
                for (int i = current; i != slot; i = (i + 1) % n) stopped[i] = true;
                current = slot;
                for (int step = 0; step < n; step++)
                {
                    current = (current + 1) % n;
                    if (!stopped[current]) return;
                }
            }

            private static ulong Mix(ulong z)
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
