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
        /// <summary>Race seconds when the car last crossed the line. Retiring doesn't move it: the car is classified on the laps it completed, when it completed them.</summary>
        public double LastLineTime { get; private set; }
        /// <summary>True once the car has crossed the line after the race time ran out.</summary>
        public bool Flagged { get; private set; }
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
        /// <summary>Damage from real hits, 0 untouched to 100 wrecked (<see cref="CarDamage"/>). It costs grip until the crew repair it.</summary>
        public double Damage { get; private set; }
        /// <summary>True once a hit has totalled the car: it is out, and goes back to the garage at 0% condition.</summary>
        public bool Totalled { get; private set; }

        /// <summary>The car's condition as it goes back to the garage: what the race left, less the damage still on it, or 0 if totalled.</summary>
        public double ConditionAfterRace => Totalled ? 0 : Math.Max(0, Condition - Damage);

        /// <summary>
        /// Driven seconds that count as one lap of the race maths: the GDD's 48 laps over the 24 hours,
        /// which the driven race runs in 8,640 s. Fatigue, condition wear and the chances of an issue or
        /// a failure all follow time on track, so a fast car is no worse off per hour than a slow one.
        /// </summary>
        public const double MathsLapSeconds = 180;

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
            double exposure = Math.Max(lapSeconds, 0) / MathsLapSeconds;
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
                Random = random,
                Exposure = exposure,
            });
            Condition = Degradation.UpdateCarCondition(Condition, Car.Car.Age, Car.EngineerSkill, mode, exposure);
            double fatiguePerLap = Degradation.UpdateDriverFatigue(0, stats.Comfort, Driver.Stats.Stamina, mode);
            Fatigue = Math.Min(100, Fatigue + fatiguePerLap * exposure);
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
            CloseStint();
        }

        /// <summary>
        /// The car hit a barrier or another car at <paramref name="impact"/> m/s into it. Adds the damage, and a hit
        /// hard enough totals the car and takes it out of the race. Returns true if it did.
        /// </summary>
        public bool Hit(double raceTime, double impact)
        {
            if (Retired || Flagged) return false;
            Damage = Math.Min(100, Damage + CarDamage.FromImpact(impact));
            if (!CarDamage.Totals(impact)) return false;
            Damage = 100;
            Totalled = true;
            Retire(raceTime, FailureType.Crash);
            return true;
        }

        /// <summary>Sets the damage, 0 to 100. For the debug menu.</summary>
        public void SetDamage(double damage) => Damage = Math.Max(0, Math.Min(100, damage));

        /// <summary>Gives the car an issue now, as if it had come up on this lap. For the debug menu.</summary>
        public void AddIssue(string templateId)
        {
            if (Retired || active.Any(i => i.TemplateId == templateId)) return;
            active.Add(new ActiveIssue { TemplateId = templateId, LapOccurred = Laps });
            issues.Add(new IssueRecord { Lap = Laps, TemplateId = templateId, DriverId = Driver.Id });
        }

        /// <summary>Fixes every active issue at once, without a stop. For the debug menu.</summary>
        public void ClearIssues()
        {
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].FixedLap == null) issues[i] = issues[i] with { FixedLap = Laps };
            active.Clear();
        }

        /// <summary>Sets how tired the driver at the wheel is, 0 to 100. For the debug menu.</summary>
        public void SetFatigue(double fatigue) => Fatigue = Math.Max(0, Math.Min(100, fatigue));

        /// <summary>How the active issues scale the car's stats, with the grip damage takes off folded into Handling.</summary>
        public IssueEffects Effects
        {
            get
            {
                double grip = CarDamage.Grip(Damage);
                if (active.Count == 0) return grip >= 1 ? IssueEffects.None : new IssueEffects(1, grip, 1, 1);
                var one = new CarStats { Power = 1, Handling = 1, FuelEfficiency = 1, TyreDurability = 1 };
                CarStats s = Debuffed(one);
                return new IssueEffects(s.Power, s.Handling * grip, s.FuelEfficiency, s.TyreDurability);
            }
        }

        /// <summary>
        /// The car stops in its box. The crew changes the tyres if the car wants them and a set is
        /// left, puts in the next driver, fixes the active issues in <paramref name="fix"/> the spares
        /// allow, or every one with no list, then repairs the damage down to <paramref name="damageTo"/>
        /// (all of it with none given) while spares last.
        /// </summary>
        public PitWork Pit(bool wantTyres, IReadOnlyList<string>? fix = null, double? damageTo = null)
        {
            bool tyres = wantTyres && TyreSets > 0;
            int next = NextDriverIndex;
            string? nextId = next != DriverIndex ? Car.Lineup[next].Id : null;
            PitStopResult work = Work(nextId, fix);
            (double repaired, int parts, double repairSeconds) = Repair(work.SparePartsRemaining, damageTo);

            var fixedIds = active.Select(i => i.TemplateId).Except(work.ActiveIssues.Select(i => i.TemplateId)).ToArray();
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].FixedLap == null && fixedIds.Contains(issues[i].TemplateId)) issues[i] = issues[i] with { FixedLap = Laps };
            active.Clear();
            active.AddRange(work.ActiveIssues);
            SpareParts = work.SparePartsRemaining - parts;
            Damage -= repaired;
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
                ExtraSeconds = work.Duration + repairSeconds,
                CrewMultiplier = PitStop.CrewMultiplier(Car.CrewSize, Car.EngineerSkill),
            };
        }

        /// <summary>Seconds of driver change and repairs a stop would add now, scaled by the crew, as <see cref="Pit"/> would work them.</summary>
        public double WorkSeconds(IReadOnlyList<string>? fix = null, double? damageTo = null)
        {
            PitStopResult work = Work(NextDriverIndex != DriverIndex ? Car.Lineup[NextDriverIndex].Id : null, fix);
            return work.Duration + Repair(work.SparePartsRemaining, damageTo).Seconds;
        }

        /// <summary>The damage a stop would repair down to <paramref name="damageTo"/> with <paramref name="parts"/> spares, the spares it takes and the crew's seconds.</summary>
        private (double Points, int Parts, double Seconds) Repair(int parts, double? damageTo)
        {
            double points = Math.Max(0, Damage - Math.Max(0, damageTo ?? 0));
            points = Math.Min(points, CarDamage.PointsFrom(parts));
            if (points <= 0) return (0, 0, 0);
            double seconds = points * CarDamage.RepairSecondsPerPoint * PitStop.CrewMultiplier(Car.CrewSize, Car.EngineerSkill);
            return (points, CarDamage.PartsFor(points), seconds);
        }

        private PitStopResult Work(string? nextId, IReadOnlyList<string>? fix) => PitStop.ExecutePitStop(
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
                IssueIdsToFix = fix ?? active.Select(i => i.TemplateId).ToArray(),
            });

        /// <summary>The stop just made is over: how long the car stood and how much fuel went in.</summary>
        public void PitDone(double seconds, double fuelAdded)
        {
            if (stops.Count == 0) return;
            stops[stops.Count - 1] = stops[stops.Count - 1] with { Seconds = seconds, FuelAdded = fuelAdded };
            FuelAdded += fuelAdded;
        }

        /// <summary>The log as it stands, for a race save.</summary>
        public CarRaceLogState Save() => new CarRaceLogState
        {
            Laps = Laps,
            Condition = Condition,
            TyreSets = TyreSets,
            SpareParts = SpareParts,
            Retired = Retired,
            RetirementReason = RetirementReason,
            LastLineTime = LastLineTime,
            Flagged = Flagged,
            FuelAdded = FuelAdded,
            DriverIndex = DriverIndex,
            NextDriverIndex = NextDriverIndex,
            BestLap = BestLap,
            FuelLeft = FuelLeft,
            Fatigue = Fatigue,
            Damage = Damage,
            Totalled = Totalled,
            Modes = Modes,
            ActiveIssues = active.ToArray(),
            Issues = issues.ToArray(),
            PitStops = stops.ToArray(),
            LapTimes = lapTimes.ToArray(),
            Stints = stints.ToArray(),
        };

        /// <summary>Puts the log back as a race save had it.</summary>
        public void Load(CarRaceLogState s)
        {
            Laps = s.Laps;
            Condition = s.Condition;
            TyreSets = s.TyreSets;
            SpareParts = s.SpareParts;
            Retired = s.Retired;
            RetirementReason = s.RetirementReason;
            LastLineTime = s.LastLineTime;
            Flagged = s.Flagged;
            FuelAdded = s.FuelAdded;
            int last = Car.Lineup.Count - 1;
            DriverIndex = Math.Max(0, Math.Min(last, s.DriverIndex));
            NextDriverIndex = Math.Max(0, Math.Min(last, s.NextDriverIndex));
            BestLap = s.BestLap;
            FuelLeft = s.FuelLeft;
            Fatigue = s.Fatigue;
            Damage = s.Damage;
            Totalled = s.Totalled;
            Modes = s.Modes;
            active.Clear();
            active.AddRange(s.ActiveIssues);
            issues.Clear();
            issues.AddRange(s.Issues);
            stops.Clear();
            stops.AddRange(s.PitStops);
            lapTimes.Clear();
            lapTimes.AddRange(s.LapTimes);
            if (s.Stints.Count == 0) return;
            stints.Clear();
            stints.AddRange(s.Stints);
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
    /// most laps first, then whoever completed their last lap soonest.
    /// </summary>
    public sealed class RaceDay
    {
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

        /// <summary>
        /// A car crossed the line. Returns the issues it picked up on the lap. Once the time is up
        /// (<paramref name="timeUp"/>) the chequered flag comes out for the leader: the first car still
        /// running to cross the line with no running car on more laps. Every car to cross after it takes the flag too.
        /// </summary>
        public IReadOnlyList<ActiveIssue> CompleteLap(int index, double raceTime, double lapSeconds, InstructionMode mode, bool timeUp)
        {
            CarRaceLog car = Cars[index];
            if (car.Retired || car.Flagged) return Array.Empty<ActiveIssue>();
            bool leader = Cars.All(c => c.Laps <= car.Laps);
            bool flag = timeUp && (FlagOut || Cars.All(c => c == car || c.Retired || c.Laps <= car.Laps));
            IReadOnlyList<ActiveIssue> found = car.CompleteLap(raceTime, lapSeconds, mode, flag, random);
            if (lapSeconds > 0 && (FastestLap == null || lapSeconds < FastestLap.Time))
                FastestLap = new FastestLap { TeamId = car.Car.TeamId, Time = lapSeconds };
            if (leader && car.Laps > positions.Count) RecordPositions();
            return found;
        }

        /// <summary>True once the leader has taken the chequered flag: every car crossing the line from now on takes it too.</summary>
        public bool FlagOut => Cars.Any(c => c.Flagged);

        /// <summary>True once every car still running has taken the flag.</summary>
        public bool AllFlagged => Cars.All(c => c.Retired || c.Flagged);

        /// <summary>
        /// The field in finishing order: most laps, then soonest to complete the last of them. A car that retired
        /// counts the laps it completed, so it finishes ahead of a car that completed as many laps later.
        /// </summary>
        public IReadOnlyList<CarRaceLog> Classify() =>
            Cars.OrderByDescending(c => c.Laps).ThenBy(c => c.LastLineTime).ThenBy(c => c.Index).ToArray();

        /// <summary>The race as it stands, for a race save.</summary>
        public RaceDayState Save() => new RaceDayState
        {
            Cars = Cars.Select(c => c.Save()).ToArray(),
            FastestLap = FastestLap,
            Positions = positions.ToArray(),
        };

        /// <summary>Puts the race back as a race save had it. The save's field must be this race's.</summary>
        public void Load(RaceDayState s)
        {
            if (s.Cars.Count != Cars.Count) throw new ArgumentException("The save is of another field.", nameof(s));
            for (int i = 0; i < Cars.Count; i++) Cars[i].Load(s.Cars[i]);
            FastestLap = s.FastestLap;
            positions.Clear();
            positions.AddRange(s.Positions);
        }

        /// <summary>Snapshots everyone's position, as the leader starts a new lap.</summary>
        private void RecordPositions()
        {
            IReadOnlyList<CarRaceLog> order = Classify();
            var row = new int[Cars.Count];
            for (int i = 0; i < order.Count; i++) row[order[i].Index] = i + 1;
            positions.Add(row);
        }
    }
}
