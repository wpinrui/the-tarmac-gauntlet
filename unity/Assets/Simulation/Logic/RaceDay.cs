#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>How active issues scale a car's stats: 1 for none, 0.9 for a 10% debuff.</summary>
    public readonly struct IssueEffects
    {
        public readonly double Power, Handling, FuelEfficiency, TyreDurability;

        public IssueEffects(double power, double handling, double fuelEfficiency, double tyreDurability)
        {
            Power = power;
            Handling = handling;
            FuelEfficiency = fuelEfficiency;
            TyreDurability = tyreDurability;
        }

        public static readonly IssueEffects None = new IssueEffects(1, 1, 1, 1);
    }

    /// <summary>What a crew does in a stop, decided as the car arrives in its box.</summary>
    public sealed record PitWork
    {
        public bool ChangeTyres { get; init; }
        /// <summary>The driver who takes over, or null if the same driver goes back out.</summary>
        public RaceDriver? NewDriver { get; init; }
        /// <summary>Seconds of driver change and repairs, already scaled by the crew.</summary>
        public double ExtraSeconds { get; init; }
        /// <summary>How much the crew and the Engineer skill scale the refuel and tyre change.</summary>
        public double CrewMultiplier { get; init; } = 1;
    }

    /// <summary>
    /// One car's race in the driven race, kept as it happens: laps, condition, issues, stints,
    /// stops, tyres and spares. Each completed lap ages the car and rolls for issues and failures
    /// with the race maths, as the simulated race does.
    /// </summary>
    public sealed class CarRaceLog
    {
        public readonly GridCar Car;
        public readonly int Index;

        public int Laps { get; private set; }
        public double Condition { get; private set; }
        public int TyreSets { get; private set; }
        public int SpareParts { get; private set; }
        public bool Retired { get; private set; }
        public FailureType? RetirementReason { get; private set; }
        /// <summary>Race seconds when the car last crossed the line, or retired.</summary>
        public double LastLineTime { get; private set; }
        /// <summary>True once the car has crossed the line after the race time ran out.</summary>
        public bool Flagged { get; private set; }
        /// <summary>The lap on which the car took the flag, seconds, or null before the flag.</summary>
        public double? FlagLap => Flagged ? lapTimes[lapTimes.Count - 1] : (double?)null;
        public double FuelAdded { get; private set; }
        public int DriverIndex { get; private set; }
        /// <summary>
        /// The driver who takes over at the next stop, as an index into the lineup. Starts as the
        /// next in the lineup after each stop; set it to change who's next.
        /// </summary>
        public int NextDriverIndex { get; set; }
        public double? BestLap { get; private set; }
        /// <summary>Litres in the tank: kept up to date by the race, so the fuel bill can be worked out at the flag.</summary>
        public double FuelLeft { get; set; }
        /// <summary>
        /// How tired the driver at the wheel is, 0 fresh to 100 spent (GDD §2). It builds with time in
        /// the car, more slowly with the car's Comfort and the driver's Stamina, and goes back to 0
        /// when a stop puts in another driver.
        /// </summary>
        public double Fatigue { get; private set; }

        /// <summary>
        /// Driven seconds that build one lap's worth of the race maths' fatigue: the GDD's 48 laps over
        /// the 24 hours, which the driven race runs in 8,640 s. Fatigue follows time, so a fast car's
        /// driver tires no quicker per hour than a slow one's.
        /// </summary>
        public const double FatigueLapSeconds = 180;

        public RaceDriver Driver => Car.Lineup[DriverIndex];
        public IReadOnlyList<ActiveIssue> ActiveIssues => active;
        public IReadOnlyList<IssueRecord> Issues => issues;
        public IReadOnlyList<PitStopRecord> PitStops => stops;
        public IReadOnlyList<double> LapTimes => lapTimes;
        public IReadOnlyList<Stint> Stints => stints;
        public ModeCounter Modes { get; private set; } = new ModeCounter();

        private readonly List<ActiveIssue> active = new List<ActiveIssue>();
        private readonly List<IssueRecord> issues = new List<IssueRecord>();
        private readonly List<PitStopRecord> stops = new List<PitStopRecord>();
        private readonly List<double> lapTimes = new List<double>();
        private readonly List<Stint> stints = new List<Stint>();

        public CarRaceLog(GridCar car, int index)
        {
            if (car.Lineup.Count == 0) throw new ArgumentException("A car needs at least one driver.", nameof(car));
            Car = car;
            Index = index;
            Condition = car.Car.Condition;
            TyreSets = car.TyreSets;
            SpareParts = car.SpareParts;
            NextDriverIndex = car.Lineup.Count > 1 ? 1 : 0;
            FuelLeft = car.StartingFuel;
            stints.Add(new Stint { DriverId = car.Lineup[0].Id, StartLap = 1, EndLap = 0 });
        }

        /// <summary>The car's stats now, worn by its condition, before issues.</summary>
        public CarStats Stats => EffectiveStats.CalculateEffectiveStats(Car.Car with { Condition = Condition }, Car.Model);

        /// <summary>
        /// A lap completed at <paramref name="raceTime"/>: ages the car, then rolls for issues and a
        /// race-ending failure. Returns the issues it picked up; check <see cref="Retired"/> after.
        /// </summary>
        public IReadOnlyList<ActiveIssue> CompleteLap(double raceTime, double lapSeconds, InstructionMode mode, bool timeUp, Func<double> random)
        {
            if (Retired || Flagged) return Array.Empty<ActiveIssue>();
            Laps++;
            LastLineTime = raceTime;
            if (timeUp) Flagged = true;
            lapTimes.Add(lapSeconds);
            if (lapSeconds > 0 && (BestLap == null || lapSeconds < BestLap)) BestLap = lapSeconds;
            Modes = mode switch
            {
                InstructionMode.Push => Modes with { Push = Modes.Push + 1 },
                InstructionMode.Conserve => Modes with { Conserve = Modes.Conserve + 1 },
                _ => Modes with { Normal = Modes.Normal + 1 },
            };
            CloseStint();

            CarStats stats = Debuffed(Stats);
            LapRiskResult risk = RiskRolls.RollLapRisks(new LapRiskContext
            {
                CurrentLap = Laps,
                Condition = Condition,
                CarAge = Car.Car.Age,
                Reliability = stats.Reliability,
                DriverSafety = Driver.Stats.Safety,
                InstructionMode = mode,
                ActiveIssues = active,
                IssueTemplates = IssueTemplates.MechanicalIssues,
                CrashTemplates = IssueTemplates.CrashIssueTemplates,
                Random = random,
            });
            Condition = Degradation.UpdateCarCondition(Condition, Car.Car.Age, Car.EngineerSkill, mode);
            double fatiguePerLap = Degradation.UpdateDriverFatigue(0, stats.Comfort, Driver.Stats.Stamina, mode);
            Fatigue = Math.Min(100, Fatigue + fatiguePerLap * Math.Max(lapSeconds, 0) / FatigueLapSeconds);
            if (risk.Failure.HasValue)
            {
                Retire(raceTime, risk.Failure.Value);
                return Array.Empty<ActiveIssue>();
            }
            foreach (ActiveIssue issue in risk.NewIssues)
            {
                active.Add(issue);
                issues.Add(new IssueRecord { Lap = Laps, TemplateId = issue.TemplateId, DriverId = Driver.Id });
            }
            return risk.NewIssues;
        }

        /// <summary>Takes the car out of the race, as at a race-ending failure.</summary>
        public void Retire(double raceTime, FailureType reason)
        {
            if (Retired) return;
            Retired = true;
            RetirementReason = reason;
            LastLineTime = Math.Max(LastLineTime, raceTime);
            CloseStint();
        }

        /// <summary>How the active issues scale the car's stats.</summary>
        public IssueEffects Effects
        {
            get
            {
                if (active.Count == 0) return IssueEffects.None;
                var one = new CarStats { Power = 1, Handling = 1, FuelEfficiency = 1, TyreDurability = 1 };
                CarStats s = Debuffed(one);
                return new IssueEffects(s.Power, s.Handling, s.FuelEfficiency, s.TyreDurability);
            }
        }

        /// <summary>
        /// The car stops in its box. The crew changes the tyres if the car wants them and a set is
        /// left, puts in the next driver, and fixes every active issue the spares allow.
        /// </summary>
        public PitWork Pit(bool wantTyres)
        {
            bool tyres = wantTyres && TyreSets > 0;
            int next = NextDriverIndex;
            string? nextId = next != DriverIndex ? Car.Lineup[next].Id : null;
            PitStopResult work = PitStop.ExecutePitStop(
                new PitStopContext
                {
                    PitStopTime = 0,
                    FuelCapacity = 0,
                    CurrentFuel = 0,
                    CurrentTyreWear = 0,
                    TyreSetsAvailable = 0,
                    SparePartsAvailable = SpareParts,
                    CurrentDriverId = Driver.Id,
                    ActiveIssues = active,
                    IssueTemplates = IssueTemplates.AllIssueTemplates,
                    CrewSize = Car.CrewSize,
                    EngineerSkill = Car.EngineerSkill,
                },
                new PitStopConfig
                {
                    NextDriverId = nextId,
                    IssueIdsToFix = active.Select(i => i.TemplateId).ToArray(),
                });

            var fixedIds = active.Select(i => i.TemplateId).Except(work.ActiveIssues.Select(i => i.TemplateId)).ToArray();
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].FixedLap == null && fixedIds.Contains(issues[i].TemplateId)) issues[i] = issues[i] with { FixedLap = Laps };
            active.Clear();
            active.AddRange(work.ActiveIssues);
            SpareParts = work.SparePartsRemaining;
            if (tyres) TyreSets--;

            string driverOut = Driver.Id;
            if (nextId != null)
            {
                DriverIndex = next;
                Fatigue = 0;
                stints.Add(new Stint { DriverId = nextId, StartLap = Laps + 1, EndLap = Laps });
            }
            NextDriverIndex = (DriverIndex + 1) % Car.Lineup.Count;
            stops.Add(new PitStopRecord
            {
                Lap = Laps,
                TyresChanged = tyres,
                DriverOut = driverOut,
                DriverIn = Driver.Id,
                FixedIssueIds = fixedIds,
            });
            return new PitWork
            {
                ChangeTyres = tyres,
                NewDriver = nextId != null ? Driver : null,
                ExtraSeconds = work.Duration,
                CrewMultiplier = PitStop.CrewMultiplier(Car.CrewSize, Car.EngineerSkill),
            };
        }

        /// <summary>The stop just made is over: how long the car stood and how much fuel went in.</summary>
        public void PitDone(double seconds, double fuelAdded)
        {
            if (stops.Count == 0) return;
            stops[stops.Count - 1] = stops[stops.Count - 1] with { Seconds = seconds, FuelAdded = fuelAdded };
            FuelAdded += fuelAdded;
        }

        private void CloseStint() => stints[stints.Count - 1] = stints[stints.Count - 1] with { EndLap = Laps };

        private CarStats Debuffed(CarStats stats)
        {
            CarStats result = stats;
            foreach (ActiveIssue issue in active)
            {
                IssueTemplate? template = IssueTemplates.AllIssueTemplates.FirstOrDefault(t => t.Id == issue.TemplateId);
                if (template == null) continue;
                foreach (KeyValuePair<CarStatKey, double> kv in template.StatDebuffs)
                    result = result.With(kv.Key, result.Get(kv.Key) * (1 - kv.Value));
            }
            return result;
        }
    }

    /// <summary>
    /// The driven race as it happens, one <see cref="CarRaceLog"/> per car in grid order, plus the
    /// running order at the end of each of the leader's laps. Classifies the field at the flag:
    /// finishers first, then everyone else, each by most laps and then whoever completed their
    /// last lap soonest.
    /// </summary>
    public sealed class RaceDay
    {
        /// <summary>Share of its class leader's laps a car needs to be a finisher.</summary>
        public const double FinisherShare = 0.7;
        /// <summary>
        /// A finisher's flag lap may take up to this many times the class's fastest lap: room for
        /// an amateur at the wheel and a stop on the way, but not for a car parked until the end.
        /// </summary>
        public const double FlagLapAllowance = 2;

        public readonly RaceSetup Setup;
        public readonly IReadOnlyList<CarRaceLog> Cars;
        public FastestLap? FastestLap { get; private set; }
        /// <summary>PositionHistory[lap][grid index] = position, at the end of each of the leader's laps.</summary>
        public IReadOnlyList<IReadOnlyList<int>> PositionHistory => positions;

        private readonly List<IReadOnlyList<int>> positions = new List<IReadOnlyList<int>>();
        private readonly Func<double> random;

        public RaceDay(RaceSetup setup, Func<double>? random = null)
        {
            Setup = setup;
            this.random = random ?? DefaultRandom.Next;
            Cars = setup.Grid.Select((car, i) => new CarRaceLog(car, i)).ToArray();
        }

        /// <summary>A car crossed the line. Returns the issues it picked up on the lap.</summary>
        public IReadOnlyList<ActiveIssue> CompleteLap(int index, double raceTime, double lapSeconds, InstructionMode mode, bool timeUp)
        {
            CarRaceLog car = Cars[index];
            if (car.Retired || car.Flagged) return Array.Empty<ActiveIssue>();
            bool leader = Cars.All(c => c.Laps <= car.Laps);
            IReadOnlyList<ActiveIssue> found = car.CompleteLap(raceTime, lapSeconds, mode, timeUp, random);
            if (lapSeconds > 0 && (FastestLap == null || lapSeconds < FastestLap.Time))
                FastestLap = new FastestLap { TeamId = car.Car.TeamId, Time = lapSeconds };
            if (leader && car.Laps > positions.Count) RecordPositions();
            return found;
        }

        /// <summary>True once every car still running has taken the flag.</summary>
        public bool AllFlagged => Cars.All(c => c.Retired || c.Flagged);

        /// <summary>
        /// True once no car can still change the result after the time ran out at
        /// <paramref name="raceTime"/>: each has retired, taken the flag, or been out on its lap
        /// longer than its flag lap may take.
        /// </summary>
        public bool Over(double raceTime) => Cars.All(c =>
        {
            if (c.Retired || c.Flagged) return true;
            double? limit = FlagLapLimit(c.Car.Model.CarClass);
            return limit == null || raceTime - c.LastLineTime > limit;
        });

        /// <summary>The longest a finisher's flag lap may take in this class, s, or null before anyone in it has set a lap.</summary>
        public double? FlagLapLimit(CarClass carClass)
        {
            double? best = Cars.Where(c => c.Car.Model.CarClass == carClass && c.BestLap != null).Min(c => c.BestLap);
            return best * FlagLapAllowance;
        }

        /// <summary>
        /// A finisher took the flag under its own power, on a flag lap within
        /// <see cref="FlagLapLimit"/>, having run at least <see cref="FinisherShare"/> of the laps
        /// of the class's leader at the flag.
        /// </summary>
        public bool Finished(CarRaceLog car)
        {
            if (car.Retired || !car.Flagged) return false;
            CarClass cls = car.Car.Model.CarClass;
            int leader = Cars.Where(c => c.Car.Model.CarClass == cls && c.Flagged && !c.Retired).Max(c => c.Laps);
            double? limit = FlagLapLimit(cls);
            return car.Laps >= FinisherShare * leader && limit != null && car.FlagLap <= limit;
        }

        /// <summary>The field in finishing order: finishers first, then most laps, then soonest across the line.</summary>
        public IReadOnlyList<CarRaceLog> Classify() =>
            Cars.OrderByDescending(Finished).ThenByDescending(c => c.Laps).ThenBy(c => c.LastLineTime).ThenBy(c => c.Index).ToArray();

        /// <summary>The running order while racing: most laps, then soonest across the line.</summary>
        private IReadOnlyList<CarRaceLog> RunningOrder() =>
            Cars.OrderByDescending(c => c.Laps).ThenBy(c => c.LastLineTime).ThenBy(c => c.Index).ToArray();

        /// <summary>Snapshots everyone's position, as the leader starts a new lap.</summary>
        private void RecordPositions()
        {
            IReadOnlyList<CarRaceLog> order = RunningOrder();
            var row = new int[Cars.Count];
            for (int i = 0; i < order.Count; i++) row[order[i].Index] = i + 1;
            positions.Add(row);
        }
    }
}
