#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>A driver with pre-resolved stats for a race entry.</summary>
    public sealed record RaceDriver
    {
        public string Id { get; init; } = "";
        public DriverStats Stats { get; init; } = new DriverStats();
    }

    /// <summary>All inputs needed to enter a car in the race.</summary>
    public sealed record CarEntry
    {
        public string CarId { get; init; } = "";
        public string TeamId { get; init; } = "";
        public CarInstance Instance { get; init; } = new CarInstance();
        public CarModel Model { get; init; } = new CarModel();
        /// <summary>All drivers available for this car; at least one required.</summary>
        public IReadOnlyList<RaceDriver> Drivers { get; init; } = Array.Empty<RaceDriver>();
        /// <summary>ID of the driver who starts the race. Must be in Drivers.</summary>
        public string StartingDriverId { get; init; } = "";
        /// <summary>Starting fuel load in litres.</summary>
        public double StartingFuel { get; init; }
        /// <summary>Tyre sets in team inventory at race start.</summary>
        public int TyreSetsAvailable { get; init; }
        /// <summary>Spare parts units at race start.</summary>
        public int SparePartsAvailable { get; init; }
        public int CrewSize { get; init; }
        public int EngineerSkill { get; init; }
        /// <summary>Starting tyre wear (0 = fresh). Null means 0.</summary>
        public double? StartingTyreWear { get; init; }
        /// <summary>
        /// Pit decision callback, called after every lap with the post-lap snapshot.
        /// Return a PitStopConfig to pit this stop, or null to continue.
        /// Cars without this callback use the default AI threshold strategy.
        /// </summary>
        public Func<int, CarLapSnapshot, PitStopConfig?>? PitDecider { get; init; }
        /// <summary>
        /// Instruction mode callback, called after every non-pitting lap to set the mode for the next lap.
        /// Cars without this callback always use Normal (GDD §8).
        /// Has no effect when the car pits: a pit stop always resets mode to Normal.
        /// </summary>
        public Func<int, CarLapSnapshot, InstructionMode>? ModeDecider { get; init; }
    }

    /// <summary>Options for the race simulation.</summary>
    public sealed record RaceOptions
    {
        /// <summary>
        /// Sim-time budget in seconds. Default 1440 (GDD §2). Each car runs laps until
        /// TotalTime + nextLapTime would exceed this. Pass PositiveInfinity (or set TotalLaps) to disable.
        /// </summary>
        public double? RaceDurationSec { get; init; }
        /// <summary>
        /// Hard lap cap. When set without RaceDurationSec, the time budget is disabled
        /// and every non-retired car runs exactly this many laps.
        /// </summary>
        public int? TotalLaps { get; init; }
        /// <summary>Injectable random source; null uses <see cref="DefaultRandom"/>.</summary>
        public Func<double>? Random { get; init; }
        /// <summary>Mechanical issue template catalogue. Null means none.</summary>
        public IReadOnlyList<IssueTemplate>? IssueTemplates { get; init; }
        /// <summary>Crash issue template catalogue. Null means none.</summary>
        public IReadOnlyList<IssueTemplate>? CrashTemplates { get; init; }
    }

    /// <summary>Final result for one car.</summary>
    public sealed record CarRaceResult
    {
        public string CarId { get; init; } = "";
        /// <summary>1-indexed finishing position.</summary>
        public int FinalPosition { get; init; }
        public int LapsCompleted { get; init; }
        /// <summary>Total accumulated time in seconds (includes pit stop durations).</summary>
        public double TotalTime { get; init; }
        public bool Retired { get; init; }
        /// <summary>Lap on which the car retired, or null if it finished.</summary>
        public int? RetirementLap { get; init; }
        public FailureType? RetirementReason { get; init; }
    }

    /// <summary>Fastest single lap of a race (TS <c>RaceResultFull["fastestLap"]</c>).</summary>
    public sealed record RaceFastestLap
    {
        public string CarId { get; init; } = "";
        public int Lap { get; init; }
        public double Time { get; init; }
    }

    /// <summary>Full race results including rich per-lap data.</summary>
    public sealed record RaceResultFull
    {
        /// <summary>All cars sorted by FinalPosition (1st = index 0).</summary>
        public IReadOnlyList<CarRaceResult> Results { get; init; } = Array.Empty<CarRaceResult>();
        /// <summary>Fastest single lap across all cars and all laps, or null if no laps ran.</summary>
        public RaceFastestLap? FastestLap { get; init; }
        /// <summary>Per-lap snapshots: carId to snapshots.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<CarLapSnapshot>> LapSnapshots { get; init; } =
            new Dictionary<string, IReadOnlyList<CarLapSnapshot>>();
        /// <summary>PositionHistory[lap][carIndex] = position (1-indexed).</summary>
        public IReadOnlyList<IReadOnlyList<int>> PositionHistory { get; init; } = Array.Empty<IReadOnlyList<int>>();
        /// <summary>
        /// Maps carId to its slot in PositionHistory[lap] (the original entry order).
        /// Results is sorted by FinalPosition, so its index is not the slot.
        /// </summary>
        public IReadOnlyDictionary<string, int> CarIndexById { get; init; } = new Dictionary<string, int>();
        /// <summary>Endurance events logged during the race.</summary>
        public IReadOnlyList<RaceEvent> Events { get; init; } = Array.Empty<RaceEvent>();
        /// <summary>Driver stints: carId to stints.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<Stint>> Stints { get; init; } =
            new Dictionary<string, IReadOnlyList<Stint>>();
        /// <summary>Instruction mode counters: carId to counter.</summary>
        public IReadOnlyDictionary<string, ModeCounter> ModeCounters { get; init; } = new Dictionary<string, ModeCounter>();
    }

    /// <summary>Port of simulation/raceLoop.ts.</summary>
    public static class RaceLoop
    {
        // --- Tunable constants ---

        /// <summary>Default sim-time budget per race in seconds (GDD §2).</summary>
        private const double DefaultRaceDurationSec = 1440;

        /// <summary>Hard cap on lap iterations to prevent runaway loops.</summary>
        private const int SafetyLapCap = 200;

        /// <summary>AI pits when tyre wear exceeds this threshold.</summary>
        private const double AiTyrePitThreshold = 80;

        /// <summary>AI pits when fuel is below this fraction of effective fuel capacity.</summary>
        private const double AiFuelPitThresholdFraction = 0.15;

        private sealed class CarWorkingState
        {
            public CarEntry Entry = new CarEntry();
            public int LapsCompleted;
            public double TotalTime;
            public double Condition;
            public double TyreWear;
            public double FuelRemaining;
            public string CurrentDriverId = "";
            public IReadOnlyDictionary<string, double> DriverFatigue = new Dictionary<string, double>();
            public InstructionMode InstructionMode;
            public IReadOnlyList<ActiveIssue> ActiveIssues = Array.Empty<ActiveIssue>();
            public int TyreSetsAvailable;
            public int SparePartsAvailable;
            public bool Retired;
            public int? RetirementLap;
            public FailureType? RetirementReason;
            /// <summary>
            /// Frozen because another lap would exceed the sim-time budget. Not a retirement:
            /// excluded from further laps but still ranked by its final lap count.
            /// </summary>
            public bool OutOfTime;
        }

        private static CarLapSnapshot MakeSnapshot(
            CarWorkingState state,
            double fuelCapacity,
            double postLapTyreWear,
            double postLapFuel) => new CarLapSnapshot
        {
            LapsCompleted = state.LapsCompleted,
            TotalTime = state.TotalTime,
            TyreWear = postLapTyreWear,
            FuelRemaining = postLapFuel,
            FuelCapacity = fuelCapacity,
            Condition = state.Condition,
            CurrentDriverId = state.CurrentDriverId,
            DriverFatigue = state.DriverFatigue.ToDictionary(kv => kv.Key, kv => kv.Value),
            InstructionMode = state.InstructionMode,
            ActiveIssues = state.ActiveIssues.ToArray(),
            TyreSetsAvailable = state.TyreSetsAvailable,
            SparePartsAvailable = state.SparePartsAvailable,
        };

        /// <summary>
        /// Applies fractional stat debuffs from all active issues to effective stats.
        /// Multiple debuffs on the same stat stack multiplicatively.
        /// </summary>
        private static CarStats ApplyIssueDebuffs(
            CarStats stats,
            IReadOnlyList<ActiveIssue> activeIssues,
            IReadOnlyList<IssueTemplate> allTemplates)
        {
            if (activeIssues.Count == 0) return stats;

            CarStats result = stats;
            foreach (ActiveIssue issue in activeIssues)
            {
                IssueTemplate? template = allTemplates.FirstOrDefault(t => t.Id == issue.TemplateId);
                if (template == null) continue;
                foreach (KeyValuePair<CarStatKey, double> kv in template.StatDebuffs)
                {
                    result = result.With(kv.Key, result.Get(kv.Key) * (1 - kv.Value));
                }
            }
            return result;
        }

        /// <summary>
        /// Default AI pit strategy (GDD §8). Pits when tyres are heavily worn or fuel is low,
        /// refuels to capacity, changes tyres if sets remain, rotates one driver per stop.
        /// </summary>
        private static PitStopConfig? AiPitDecider(int lap, CarLapSnapshot snapshot, CarEntry entry)
        {
            bool tyreLow = snapshot.TyreWear > AiTyrePitThreshold;
            bool fuelLow = snapshot.FuelRemaining < snapshot.FuelCapacity * AiFuelPitThresholdFraction;

            if (!tyreLow && !fuelLow) return null;

            int currentIdx = -1;
            for (int i = 0; i < entry.Drivers.Count; i++)
            {
                if (entry.Drivers[i].Id == snapshot.CurrentDriverId) { currentIdx = i; break; }
            }
            int nextIdx = (currentIdx + 1) % entry.Drivers.Count;
            RaceDriver nextDriver = entry.Drivers[nextIdx];
            string? nextDriverId = nextDriver.Id != snapshot.CurrentDriverId ? nextDriver.Id : null;

            return new PitStopConfig
            {
                FuelToAdd = snapshot.FuelCapacity - snapshot.FuelRemaining,
                ChangeTyres = snapshot.TyreSetsAvailable > 0,
                NextDriverId = nextDriverId,
                IssueIdsToFix = snapshot.ActiveIssues.Select(i => i.TemplateId).ToArray(),
            };
        }

        private static ModeCounter Increment(ModeCounter counter, InstructionMode mode) => mode switch
        {
            InstructionMode.Push => counter with { Push = counter.Push + 1 },
            InstructionMode.Normal => counter with { Normal = counter.Normal + 1 },
            InstructionMode.Conserve => counter with { Conserve = counter.Conserve + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        /// <summary>Sets EndLap on the last stint of a car.</summary>
        private static void CloseLastStint(List<Stint> carStints, int endLap)
        {
            carStints[carStints.Count - 1] = carStints[carStints.Count - 1] with { EndLap = endLap };
        }

        private static string TsName(FailureType failure) => failure.ToString().ToLowerInvariant();

        private static List<CarWorkingState> RankStandings(List<CarWorkingState> states) =>
            states.OrderByDescending(s => s.LapsCompleted).ThenBy(s => s.TotalTime).ToList();

        /// <summary>
        /// Simulates a complete race for all entered cars (GDD §2).
        /// Standings rank laps completed descending, then total time ascending.
        /// </summary>
        /// <param name="cars">One entry per car, in any order.</param>
        /// <param name="options">Global race settings; null uses defaults.</param>
        public static RaceResultFull SimulateRace(IReadOnlyList<CarEntry> cars, RaceOptions? options = null)
        {
            options ??= new RaceOptions();

            // A bare TotalLaps disables the time budget; otherwise it defaults to 1440 s.
            double raceDurationSec =
                options.RaceDurationSec ??
                (options.TotalLaps.HasValue ? double.PositiveInfinity : DefaultRaceDurationSec);
            int lapCap = options.TotalLaps ?? SafetyLapCap;
            Func<double> random = options.Random ?? DefaultRandom.Next;
            IReadOnlyList<IssueTemplate> issueTemplates = options.IssueTemplates ?? Array.Empty<IssueTemplate>();
            IReadOnlyList<IssueTemplate> crashTemplates = options.CrashTemplates ?? Array.Empty<IssueTemplate>();
            // All templates combined, for pit stop issue fix lookups.
            IReadOnlyList<IssueTemplate> allTemplates = issueTemplates.Concat(crashTemplates).ToArray();

            List<CarWorkingState> states = cars.Select(entry => new CarWorkingState
            {
                Entry = entry,
                LapsCompleted = 0,
                TotalTime = 0,
                Condition = entry.Instance.Condition,
                TyreWear = entry.StartingTyreWear ?? 0,
                FuelRemaining = entry.StartingFuel,
                CurrentDriverId = entry.StartingDriverId,
                DriverFatigue = entry.Drivers.ToDictionary(d => d.Id, d => 0.0),
                InstructionMode = InstructionMode.Normal,
                ActiveIssues = Array.Empty<ActiveIssue>(),
                TyreSetsAvailable = entry.TyreSetsAvailable,
                SparePartsAvailable = entry.SparePartsAvailable,
                Retired = false,
                RetirementLap = null,
                RetirementReason = null,
                OutOfTime = false,
            }).ToList();

            RaceFastestLap? fastestLap = null;

            var lapSnapshots = new Dictionary<string, List<CarLapSnapshot>>();
            var positionHistory = new List<IReadOnlyList<int>>();
            var carIndexById = new Dictionary<string, int>();
            var events = new List<RaceEvent>();
            var stints = new Dictionary<string, List<Stint>>();
            var modeCounters = new Dictionary<string, ModeCounter>();

            for (int i = 0; i < cars.Count; i++)
            {
                CarEntry entry = cars[i];
                lapSnapshots[entry.CarId] = new List<CarLapSnapshot>();
                stints[entry.CarId] = new List<Stint> { new Stint { DriverId = entry.StartingDriverId, StartLap = 1, EndLap = -1 } };
                modeCounters[entry.CarId] = new ModeCounter { Push = 0, Normal = 0, Conserve = 0 };
                carIndexById[entry.CarId] = i;
            }

            // Previous lap's class leaders: class to carId.
            var prevClassLeaders = new Dictionary<CarClass, string>();

            for (int lap = 1; lap <= lapCap; lap++)
            {
                // Stop once every car is retired or out of time.
                bool anyActive = states.Any(s => !s.Retired && !s.OutOfTime);
                if (!anyActive) break;

                foreach (CarWorkingState state in states)
                {
                    if (state.Retired || state.OutOfTime) continue;

                    CarEntry entry = state.Entry;

                    // 1. Effective stats, recalculated each lap since condition degrades.
                    CarInstance instanceNow = entry.Instance with { Condition = state.Condition };
                    CarStats effectiveStats = EffectiveStats.CalculateEffectiveStats(instanceNow, entry.Model);

                    // 2. Current driver stats + fatigue.
                    RaceDriver currentDriver = entry.Drivers.First(d => d.Id == state.CurrentDriverId);
                    double currentFatigue = state.DriverFatigue.TryGetValue(state.CurrentDriverId, out double f) ? f : 0;

                    // 3. Active issue stat debuffs.
                    CarStats debuffedStats = ApplyIssueDebuffs(effectiveStats, state.ActiveIssues, allTemplates);

                    // 4. Lap time using debuffed stats.
                    double lapTime = LapTime.CalculateLapTime(
                        debuffedStats,
                        currentDriver.Stats,
                        currentFatigue,
                        state.InstructionMode,
                        state.TyreWear,
                        state.FuelRemaining,
                        random);

                    // 4.5. Time budget gate: freeze the car and skip a lap it never ran.
                    if (state.TotalTime + lapTime > raceDurationSec)
                    {
                        state.OutOfTime = true;
                        List<Stint> openStints = stints[entry.CarId];
                        if (openStints.Count > 0 && openStints[openStints.Count - 1].EndLap == -1)
                        {
                            CloseLastStint(openStints, state.LapsCompleted);
                        }
                        continue;
                    }

                    if (fastestLap == null || lapTime < fastestLap.Time)
                    {
                        fastestLap = new RaceFastestLap { CarId = entry.CarId, Lap = lap, Time = lapTime };
                    }

                    // 5. Roll for issues and failures.
                    LapRiskResult riskResult = RiskRolls.RollLapRisks(new LapRiskContext
                    {
                        CurrentLap = lap,
                        Condition = state.Condition,
                        CarAge = entry.Instance.Age,
                        Reliability = debuffedStats.Reliability,
                        DriverSafety = currentDriver.Stats.Safety,
                        InstructionMode = state.InstructionMode,
                        ActiveIssues = state.ActiveIssues,
                        IssueTemplates = issueTemplates,
                        CrashTemplates = crashTemplates,
                        Random = random,
                    });

                    // Degradation, computed before applying so a failure can still record final state.
                    double newTyreWear = Degradation.UpdateTyreWear(
                        state.TyreWear,
                        effectiveStats.TyreDurability,
                        currentDriver.Stats.Smoothness,
                        state.InstructionMode);
                    double newFuel = Degradation.UpdateFuelLevel(
                        state.FuelRemaining,
                        effectiveStats.FuelEfficiency,
                        state.InstructionMode);
                    double newFatigue = Degradation.UpdateDriverFatigue(
                        currentFatigue,
                        effectiveStats.Comfort,
                        currentDriver.Stats.Stamina,
                        state.InstructionMode);
                    double newCondition = Degradation.UpdateCarCondition(
                        state.Condition,
                        entry.Instance.Age,
                        entry.EngineerSkill,
                        state.InstructionMode);

                    // 6. Complete the lap.
                    state.TotalTime += lapTime;
                    state.LapsCompleted += 1;

                    // 7. Terminal failure: retire, no pit stop this lap.
                    if (riskResult.Failure.HasValue)
                    {
                        FailureType failure = riskResult.Failure.Value;
                        state.Retired = true;
                        state.RetirementLap = lap;
                        state.RetirementReason = failure;
                        state.TyreWear = newTyreWear;
                        state.FuelRemaining = newFuel;
                        state.DriverFatigue = WithFatigue(state.DriverFatigue, state.CurrentDriverId, newFatigue);
                        state.Condition = newCondition;

                        events.Add(new RaceEvent
                        {
                            Lap = lap,
                            Type = RaceEventType.Retirement,
                            Text = $"{entry.CarId} retired — {TsName(failure)}",
                            CarId = entry.CarId,
                            TeamId = entry.TeamId,
                        });
                        List<Stint> retireStints = stints[entry.CarId];
                        if (retireStints.Count > 0) CloseLastStint(retireStints, lap);
                        modeCounters[entry.CarId] = Increment(modeCounters[entry.CarId], state.InstructionMode);

                        continue;
                    }

                    // 8. Apply degradation and new issues to working state.
                    state.ActiveIssues = state.ActiveIssues.Concat(riskResult.NewIssues).ToArray();
                    state.Condition = newCondition;
                    state.DriverFatigue = WithFatigue(state.DriverFatigue, state.CurrentDriverId, newFatigue);

                    // 9. Snapshot (post-degradation, pre-pit) used by both deciders.
                    CarLapSnapshot snapshot = MakeSnapshot(state, effectiveStats.FuelCapacity, newTyreWear, newFuel);

                    lapSnapshots[entry.CarId].Add(snapshot);
                    modeCounters[entry.CarId] = Increment(modeCounters[entry.CarId], state.InstructionMode);

                    foreach (ActiveIssue issue in riskResult.NewIssues)
                    {
                        IssueTemplate? template = allTemplates.FirstOrDefault(t => t.Id == issue.TemplateId);
                        events.Add(new RaceEvent
                        {
                            Lap = lap,
                            Type = RaceEventType.Issue,
                            Text = $"{entry.CarId} — {template?.Description ?? "mechanical issue"}",
                            CarId = entry.CarId,
                            TeamId = entry.TeamId,
                        });
                    }

                    // 10. Next instruction mode (overridden to Normal if pitting).
                    InstructionMode nextMode = entry.ModeDecider != null
                        ? entry.ModeDecider(lap, snapshot)
                        : InstructionMode.Normal;

                    // 11. Pit decision: forced if fuel exhausted, otherwise ask the decider.
                    bool forcedPit = newFuel == 0;
                    PitStopConfig? pitConfig;

                    if (forcedPit)
                    {
                        pitConfig = new PitStopConfig
                        {
                            FuelToAdd = effectiveStats.FuelCapacity,
                            ChangeTyres = state.TyreSetsAvailable > 0,
                            NextDriverId = null,
                            IssueIdsToFix = Array.Empty<string>(),
                        };
                    }
                    else
                    {
                        pitConfig = entry.PitDecider != null
                            ? entry.PitDecider(lap, snapshot)
                            : AiPitDecider(lap, snapshot, entry);
                    }

                    // 12. Execute pit stop or commit degraded tyre/fuel to state.
                    if (pitConfig != null)
                    {
                        PitStopResult pitResult = PitStop.ExecutePitStop(
                            new PitStopContext
                            {
                                PitStopTime = effectiveStats.PitStopTime,
                                FuelCapacity = effectiveStats.FuelCapacity,
                                CurrentFuel = newFuel,
                                CurrentTyreWear = newTyreWear,
                                TyreSetsAvailable = state.TyreSetsAvailable,
                                SparePartsAvailable = state.SparePartsAvailable,
                                CurrentDriverId = state.CurrentDriverId,
                                DriverFatigue = state.DriverFatigue,
                                ActiveIssues = state.ActiveIssues,
                                IssueTemplates = allTemplates,
                                CrewSize = entry.CrewSize,
                                EngineerSkill = entry.EngineerSkill,
                            },
                            pitConfig);
                        state.TotalTime += pitResult.Duration;
                        state.FuelRemaining = pitResult.FuelLevel;
                        state.TyreWear = pitResult.TyreWear;
                        state.TyreSetsAvailable = pitResult.TyreSetsRemaining;
                        state.SparePartsAvailable = pitResult.SparePartsRemaining;
                        state.ActiveIssues = pitResult.ActiveIssues;
                        state.InstructionMode = pitResult.InstructionMode; // always Normal

                        bool driverSwapped = pitResult.CurrentDriverId != state.CurrentDriverId;
                        events.Add(new RaceEvent
                        {
                            Lap = lap,
                            Type = RaceEventType.PitStop,
                            Text = $"{entry.CarId} pits{(driverSwapped ? $" — {pitResult.CurrentDriverId} takes over" : "")}",
                            CarId = entry.CarId,
                            TeamId = entry.TeamId,
                        });

                        if (driverSwapped)
                        {
                            List<Stint> swapStints = stints[entry.CarId];
                            if (swapStints.Count > 0) CloseLastStint(swapStints, lap);
                            swapStints.Add(new Stint { DriverId = pitResult.CurrentDriverId, StartLap = lap + 1, EndLap = -1 });
                        }

                        state.CurrentDriverId = pitResult.CurrentDriverId;
                        state.DriverFatigue = pitResult.DriverFatigue;
                    }
                    else
                    {
                        state.TyreWear = newTyreWear;
                        state.FuelRemaining = newFuel;
                        state.InstructionMode = nextMode;
                    }
                }

                // --- End of lap: standings and position history ---
                List<CarWorkingState> lapStandings = RankStandings(states);

                var lapPositions = new int[states.Count];
                for (int i = 0; i < lapStandings.Count; i++)
                {
                    int stateIdx = states.IndexOf(lapStandings[i]);
                    lapPositions[stateIdx] = i + 1;
                }
                positionHistory.Add(lapPositions);

                // Class lead changes.
                var classLeaders = new Dictionary<CarClass, string>();
                foreach (CarWorkingState s in lapStandings)
                {
                    CarClass cls = s.Entry.Model.CarClass;
                    if (!classLeaders.ContainsKey(cls)) classLeaders[cls] = s.Entry.CarId;
                }
                foreach (KeyValuePair<CarClass, string> kv in classLeaders)
                {
                    if (prevClassLeaders.TryGetValue(kv.Key, out string? prevLeader) && prevLeader != kv.Value)
                    {
                        string leadCarId = kv.Value;
                        events.Add(new RaceEvent
                        {
                            Lap = lap,
                            Type = RaceEventType.ClassLeadChange,
                            Text = $"{leadCarId} takes the Class {kv.Key} lead",
                            CarId = leadCarId,
                            TeamId = states.FirstOrDefault(s => s.Entry.CarId == leadCarId)?.Entry.TeamId ?? "",
                        });
                    }
                }
                prevClassLeaders = classLeaders;

                if (fastestLap != null && fastestLap.Lap == lap)
                {
                    RaceFastestLap fl = fastestLap;
                    events.Add(new RaceEvent
                    {
                        Lap = lap,
                        Type = RaceEventType.FastestLap,
                        Text = $"Fastest lap: {fl.CarId} — {fl.Time.ToString("F1", CultureInfo.InvariantCulture)}s",
                        CarId = fl.CarId,
                        TeamId = states.FirstOrDefault(s => s.Entry.CarId == fl.CarId)?.Entry.TeamId ?? "",
                    });
                }
            }

            // --- Final standings ---
            List<CarWorkingState> sorted = RankStandings(states);

            CarRaceResult[] results = sorted.Select((state, idx) => new CarRaceResult
            {
                CarId = state.Entry.CarId,
                FinalPosition = idx + 1,
                LapsCompleted = state.LapsCompleted,
                TotalTime = state.TotalTime,
                Retired = state.Retired,
                RetirementLap = state.RetirementLap,
                RetirementReason = state.RetirementReason,
            }).ToArray();

            // Close all open stints.
            foreach (CarWorkingState state in states)
            {
                List<Stint> carStints = stints[state.Entry.CarId];
                if (carStints.Count > 0 && carStints[carStints.Count - 1].EndLap == -1)
                {
                    CloseLastStint(carStints, state.LapsCompleted);
                }
            }

            return new RaceResultFull
            {
                Results = results,
                FastestLap = fastestLap,
                LapSnapshots = lapSnapshots.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<CarLapSnapshot>)kv.Value),
                PositionHistory = positionHistory,
                CarIndexById = carIndexById,
                Events = events,
                Stints = stints.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Stint>)kv.Value),
                ModeCounters = modeCounters,
            };
        }

        private static IReadOnlyDictionary<string, double> WithFatigue(
            IReadOnlyDictionary<string, double> fatigue, string driverId, double value)
        {
            var copy = fatigue.ToDictionary(kv => kv.Key, kv => kv.Value);
            copy[driverId] = value;
            return copy;
        }
    }
}
