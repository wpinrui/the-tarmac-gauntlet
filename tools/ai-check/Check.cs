// Drives the AI round the circuit in StandInCar, a flat copy of CarController's physics, and checks
// that it laps. Each check prints one line: ok, or FAIL with what went wrong. Exit code 1 if any fails.
//
// Checks:
//   line uses the width  the minimum-curvature line sits on the inside half of the track at the middle of
//                        every bend at least 20 m long, and, where no other bend ends in the 150 m before
//                        it, within 1 m of the outside limit somewhere in that run-up, so it is outside,
//                        apex, outside and never a shortest path
//   tuning helps         the line tuned for a car predicts a faster lap than the minimum-curvature line
//   clean laps       with Safety 100, every car model laps from a standing start without its centre
//                    ever leaving the track and kerbs, and its flying lap lands within 5% of the
//                    speed profile's prediction
//   pace orders      a Pace 100 driver is faster than a Pace 50 driver in the same car
//   consistency      lap times spread more for a Consistency 0 driver than a Consistency 100 driver
//   mistakes         a Safety 0 driver overcooks corners, and still finishes every lap
//
// Pass a car model id (e.g. a-29) to print six laps of that car at Pace 90 in detail instead: one line
// every half second, every step within 120 m of the lap distance in AI_TRACE, and every tenth step of an
// overcooked corner. AI_SAFETY sets the driver's Safety (default 100).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static partial class Check
{
    const float Dt = 0.01f;
    const float KerbWidth = 1.5f;

    static Circuit circuit;
    static RacingLine line;
    static readonly Dictionary<string, RacingLine> tuned = new Dictionary<string, RacingLine>();

    /// <summary>Each car's own line, as the game builds it: minimum curvature tuned to its lap time.</summary>
    static RacingLine LineFor(string modelId)
    {
        if (!tuned.TryGetValue(modelId, out RacingLine l)) tuned[modelId] = l = new RacingLine(circuit, CarSpec.For(modelId));
        return l;
    }
    static bool failed;

    sealed class Run
    {
        public List<float> Laps = new List<float>();
        public float Predicted;
        public int OffTrack;           // physics steps with the car's centre beyond the kerbs
        public float WorstOff;         // furthest the centre went past the track edge, m
        public float WorstError;       // furthest the front axle strayed from the line, m
        public int Mistakes;
        public bool Stuck;
        public string Name;
    }

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        circuit = new Circuit(data);
        line = new RacingLine(circuit);

        if (args.Length > 2 && args[1] == "line")
        {
            if (args.Length > 3)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                line = LineFor(args[3]);
                Console.WriteLine($"tuned for {args[3]} in {watch.ElapsedMilliseconds} ms: {Time(new SpeedProfile(new RacingLine(circuit), CarSpec.For(args[3]), 1f).LapTime)} -> {Time(line.TunedLapTime)}");
            }
            // The racing line as CSV, for drawing: centre x, z, left x, z, half width, line x, z, lateral.
            using var w = new StreamWriter(args[2]);
            for (int k = 0; k < circuit.Count; k++)
            {
                UnityEngine.Vector3 c = circuit.Centre[k], l = circuit.Left[k], p = line.Point[k];
                w.WriteLine($"{c.x},{c.z},{l.x},{l.z},{circuit.HalfWidth(k)},{p.x},{p.z},{line.Lateral[k]},{line.Curvature[k]}");
            }
            return 0;
        }
        if (args.Length > 1 && args[1] == "solo")
        {
            foreach (float pace in new[] { 75f, 100f })
            {
                Run r = Drive(args[2], pace, 100f, 100f, 3, 1);
                Console.WriteLine($"{args[2]} pace {pace}: {string.Join(" ", r.Laps.Select(Time))}");
            }
            return 0;
        }
        if (args.Length > 1 && args[1] == "racecraft")
        {
            HoldsGap();
            Defends();
            LapsAtHairpin();
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "glide")
        {
            string only = args.Length > 2 ? args[2] : null;
            if (only == null || only == "pace")
            {
                foreach (string id in new[] { "f-01", "c-19", "a-29" }) physicsLaps[id] = Drive(id, 100f, 100f, 100f, 2, 1).Laps[1];
                GlidePace(new[] { "f-01", "c-19", "a-29" });
            }
            if (only == null || only == "mistakes") { Mistakes(); GlideMistakes(); }
            if (only == null || only == "handovers") Handovers();
            if (only == null || only == "ff") FastForward();
            if (only == null || only == "pits") PitStopsCheck("gliding pit stops", i => i % 2 == 1, verbose: only != null, glide: true);
            if (only == null || only == "start") RollingStartCheck(100, verbose: false, glide: true);
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "tuner")
        {
            // The scene's start-up work: every car's tuned line and pilot, through the one tuner thread.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var pilots = new List<System.Threading.Tasks.Task<AiPilot>>();
            int seed = 0;
            foreach (string id in SceneField())
            {
                CarSpec spec = CarSpec.For(id);
                var tuned = LineTuner.Line(circuit, spec);
                int s = ++seed;
                pilots.Add(LineTuner.Run(() => new AiPilot(tuned.Result, spec, 85f, 80f, 90f, s)));
            }
            System.Threading.Tasks.Task.WaitAll(pilots.ToArray());
            Console.WriteLine($"tuner: {pilots.Count} pilots, {SceneField().Distinct().Count()} lines, all ready after {watch.Elapsed.TotalSeconds:0.0} s on one thread");
            return 0;
        }
        if (args.Length > 1 && args[1] == "running")
        {
            RaceRunning(verbose: true);
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "start")
        {
            RollingStartCheck(args.Length > 2 ? int.Parse(args[2]) : 100, verbose: true);
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "pits")
        {
            PitStopsCheck("pit stops", i => i % 2 == 1, verbose: true);
            PitStopsCheck("crowded pits", i => true, verbose: true);
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "supplies")
        {
            SuppliesCheck(verbose: true);
            return failed ? 1 : 0;
        }
        if (args.Length > 1 && args[1] == "race")
        {
            RacePack(args.Length > 2 ? int.Parse(args[2]) : 4, verbose: true, reversed: Environment.GetEnvironmentVariable("AI_REVERSED") != null);
            return 0;
        }
        if (args.Length > 1)
        {
            Detail(args[1]);
            return 0;
        }

        LineUsesWidth();
        TuningHelps();
        CleanLaps();
        PaceOrders();
        ConsistencySpreads();
        Mistakes();
        HoldsGap();
        Defends();
        LapsAtHairpin();
        RacePack(4, verbose: false);
        RacePack(4, verbose: false, reversed: true);
        RaceRunning(verbose: false);
        GlidePace();
        GlideMistakes();
        Handovers();
        FastForward();
        return failed ? 1 : 0;
    }

    /// <summary>The field, quickest first, on the grid in that order or, reversed, quickest at the back.</summary>
    static List<Entry> Pack(bool reversed)
    {
        string[] models = { "a-29", "a-27", "a-26", "b-24", "b-22", "b-21", "c-19", "c-18", "c-16", "d-15", "d-13", "e-09", "e-07", "f-06", "f-03", "f-01" };
        var rng = new Random(int.TryParse(Environment.GetEnvironmentVariable("AI_PACK_SEED"), out int packSeed) ? packSeed : 11);
        var entries = new List<Entry>();
        for (int i = 0; i < models.Length; i++)
        {
            GridSlot slot = circuit.Data.grid.slots[reversed ? models.Length - 1 - i : i];
            entries.Add(new Entry
            {
                Model = models[i],
                Start = slot.d - 2.6f,
                Lateral = slot.lateral,
                Pace = 70f + 30f * (float)rng.NextDouble(),
                Consistency = 60f + 40f * (float)rng.NextDouble(),
                Safety = 80f + 20f * (float)rng.NextDouble(),
                Seed = i + 1,
            });
        }
        return entries;
    }

    static Race RunRace(IEnumerable<Entry> entries, Func<Race, bool> done, float limit)
    {
        var race = new Race(circuit, entries, LineFor);
        while (!done(race) && race.Time < limit) race.Step();
        return race;
    }

    /// <summary>
    /// The pack races from a standing start until the leader has done some laps. It must pass, lap the
    /// slower classes, and stay off each other: no hard hits, no car stuck, no car off the track.
    /// </summary>
    static void RacePack(int laps, bool verbose, bool reversed = false)
    {
        int[] pair = Environment.GetEnvironmentVariable("AI_WATCH")?.Split(',').Select(int.Parse).ToArray();
        int tick = 0;
        int solo = int.TryParse(Environment.GetEnvironmentVariable("AI_SOLO"), out int so) ? so : -1;
        Race race = RunRace(Pack(reversed), r =>
        {
            if (pair != null && tick++ % 10 == 0)
            {
                Racer x = r.Racers[pair[0]], y = r.Racers[pair[1]];
                float road = x.Distance - y.Distance;
                road -= circuit.Lap * (float)Math.Round(road / circuit.Lap);
                if (Math.Abs(road) < 40f)
                    Console.WriteLine($"{Race.Clock(r.Time)} {x.Car.TrackIndex * circuit.Step,5:0} m gap {road,6:0.0}  #{x.Id} {x.Car.Speed * 3.6f,4:0} {x.Craft.State,-10} {x.Craft.Plan,-10} off {x.Pilot.Offset,5:0.0} tgt {x.Craft.Target,5:0.0} cap {Math.Min(x.Pilot.SpeedCap, 999f) * 3.6f,4:0} lat {x.Car.Lateral,5:0.0} yaw {x.Car.YawRate,5:0.00} err {x.Pilot.LateralError,5:0.0} tspd {x.Pilot.TargetSpeed * 3.6f,4:0}   #{y.Id} {y.Car.Speed * 3.6f,4:0} {y.Craft.State,-10} {y.Craft.Plan,-10} off {y.Pilot.Offset,5:0.0} tgt {y.Craft.Target,5:0.0} cap {Math.Min(y.Pilot.SpeedCap, 999f) * 3.6f,4:0} lat {y.Car.Lateral,5:0.0}");
            }
            if (solo >= 0 && tick % 20 == 0)
            {
                Racer x = r.Racers[solo];
                Racer ahead = r.Racers.Where(o => o != x && o.Distance > x.Distance).OrderBy(o => o.Distance).FirstOrDefault();
                string a = ahead == null ? "" : $"ahead #{ahead.Id} {ahead.Entry.Model} {ahead.Distance - x.Distance,5:0.0} m lat {ahead.Car.Lateral,5:0.0} {ahead.Car.Speed * 3.6f,4:0}";
                Console.WriteLine($"{Race.Clock(r.Time)} {x.Car.TrackIndex * circuit.Step,5:0} m {x.Car.Speed * 3.6f,4:0} {x.Craft.State,-10} {x.Craft.Plan,-10} off {x.Pilot.Offset,5:0.0} tgt {x.Craft.Target,5:0.0} cap {Math.Min(x.Pilot.SpeedCap, 999f) * 3.6f,4:0} lat {x.Car.Lateral,5:0.0} swings {x.Swings}  {a}");
            }
            if (pair == null) tick++;
            return r.Racers.Max(c => c.Laps) >= laps;
        }, 60f * 20f);
        var problems = new List<string>();
        int hard = race.Contacts.Count(c => c.Speed > 3f);
        int lapping = race.Passes.Count(p => p.EndsWith("(lapping)"));
        int classes = race.Passes.Count(p => !p.EndsWith("(lapping)") && p.Split(' ')[1][0] != p.Split(' ')[3][0]);
        foreach (Racer r in race.Racers)
        {
            if (r.Recoveries > 0) problems.Add($"{race.Name(r)} got stuck {r.Recoveries} times");
            if (r.OffSteps > 0) problems.Add($"{race.Name(r)} left the track for {r.OffSteps * Race.Dt:0.0} s, first at {Race.Clock(r.OffTime)} at {r.OffWhere:0} m");
        }
        if (hard > 0) problems.Add($"{hard} contacts harder than 3 m/s, worst {race.Contacts.Max(c => c.Speed):0.0} m/s");
        if (race.Contacts.Count > race.Racers.Count) problems.Add($"{race.Contacts.Count} contacts");
        if (lapping == 0) problems.Add("nobody was lapped");
        if (race.FastestStuck >= 3) problems.Add(race.FastestStuckLine);
        // Within a class the cars are close in pace, so on the normal grid a pass is only owed where
        // one is clearly quicker than a classmate it started behind. On the reversed grid the field
        // is all traffic, which decides who catches whom in four laps, but there are always passes.
        if (!reversed)
            foreach (Racer a in race.Racers)
                foreach (Racer b in race.Racers)
                    if (a.Entry.Model[0] == b.Entry.Model[0] && b.Entry.Start < a.Entry.Start
                        && b.Pilot.Profile.LapTime < a.Pilot.Profile.LapTime * 0.99f && b.Distance < a.Distance)
                        problems.Add($"{race.Name(b)} is {100f * (1f - b.Pilot.Profile.LapTime / a.Pilot.Profile.LapTime):0.0}% quicker than {race.Name(a)} but never got by");
        if (reversed && race.PassCount - lapping - classes < 1) problems.Add("no passes within a class");
        Report($"{(reversed ? "reversed " : "")}pack race ({race.Racers.Count} cars, {laps} laps: {race.PassCount - lapping - classes} passes within a class, {classes} between classes, {lapping} lappings, {race.Contacts.Count} contacts, closest following {race.ClosestFollow:0.0} m, {race.Racers.Sum(r => r.Pilot.Mistakes)} mistakes, {race.Racers.Sum(r => r.MistakeOffSteps) * Race.Dt:0} s off after them, fastest car {race.FastestStuck} corners behind the slowest, any car up to {race.WorstStuck} corners behind one two classes slower, {race.Racers.Sum(r => r.Swings)} swings, {race.Racers.Sum(r => r.Dithers)} dithers)", problems);
        if (!verbose) return;
        if (race.WorstStuck > 0) Console.WriteLine("  " + race.WorstStuckLine);
        foreach (string p in race.Passes) Console.WriteLine("  " + p);
        foreach (Contact c in race.Contacts) Console.WriteLine($"  contact {Race.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s");
        foreach (Racer r in race.Racers.OrderByDescending(r => r.Distance))
            Console.WriteLine($"  {race.Name(r),-8} lap {r.Laps} pace {r.Entry.Pace:0} solo {Time(r.Pilot.Profile.LapTime)} laps {string.Join(" ", r.LapTimes.Select(Time))}  passes {r.Craft.Passes} defences {r.Craft.Defences} swings {r.Swings} dithers {r.Dithers}  {string.Join(" ", r.ModeTime.Select(m => $"{m.Key} {m.Value:0}s"))}");
    }

    /// <summary>Two identical cars, the second close behind: it can't pass, so it must hold a gap without touching.</summary>
    static void HoldsGap()
    {
        var entries = new List<Entry>
        {
            new Entry { Model = "c-19", Start = 300f, Speed = 40f, Pace = 90f, Consistency = 100f, Seed = 1 },
            new Entry { Model = "c-19", Start = 285f, Speed = 40f, Pace = 90f, Consistency = 100f, Seed = 2 },
        };
        Race race = RunRace(entries, r => r.Time > 160f, 200f);
        var problems = new List<string>();
        if (race.Contacts.Count > 0) problems.Add($"{race.Contacts.Count} contacts");
        if (race.ClosestFollow < 1f) problems.Add($"followed {race.ClosestFollow:0.0} m from the gearbox ahead");
        Report($"holds gap (closest {race.ClosestFollow:0.0} m nose to tail)", problems);
    }

    /// <summary>A quicker driver behind an equal car: the leader defends the inside, and the pass still comes, cleanly.</summary>
    static void Defends()
    {
        var entries = new List<Entry>
        {
            new Entry { Model = "b-24", Start = 200f, Speed = 45f, Pace = 75f, Consistency = 100f, Seed = 1 },
            new Entry { Model = "b-24", Start = 180f, Speed = 45f, Pace = 100f, Consistency = 100f, Seed = 2 },
        };
        int tick = 0;
        bool trace = Environment.GetEnvironmentVariable("AI_RACE_TRACE") != null;
        Race race = RunRace(entries, r =>
        {
            if (trace && (tick++ % 50 == 0 || (r.Time > 71.0f && r.Time < 73.2f && tick % 5 == 0)))
            {
                Racer x = r.Racers[0], y = r.Racers[1];
                Console.WriteLine($"{Race.Clock(r.Time)} {x.Car.TrackIndex * circuit.Step,5:0} m gap {x.Distance - y.Distance,6:0.0}  lead {x.Car.Speed * 3.6f,4:0} {x.Craft.State,-10} {x.Craft.Plan,-10} off {x.Pilot.Offset,5:0.0}  chase {y.Car.Speed * 3.6f,4:0} {y.Craft.State,-10} {y.Craft.Plan,-10} off {y.Pilot.Offset,5:0.0} tgt {y.Craft.Target,5:0.0} cap {Math.Min(y.Pilot.SpeedCap, 999f) * 3.6f,4:0} sh {y.Pilot.Shift,5:0.0}/{x.Pilot.Shift,5:0.0} lat {y.Car.Lateral,5:0.0}/{x.Car.Lateral,5:0.0} brk {x.Car.Spec.Name.Length}");
            }
            return r.Racers[1].Distance > r.Racers[0].Distance + 60f;
        }, 400f);
        var problems = new List<string>();
        Racer leader = race.Racers[0], quick = race.Racers[1];
        if (leader.Craft.Defences == 0) problems.Add("the leader never defended");
        if (quick.Distance <= leader.Distance) problems.Add("the quicker driver never got by");
        if (race.Contacts.Any(c => c.Speed > 3f)) problems.Add($"hard contact {race.Contacts.Max(c => c.Speed):0.0} m/s");
        if (trace) foreach (Contact c in race.Contacts) Console.WriteLine($"  contact {Race.Clock(c.Time)} at {c.Where:0} m, {c.Speed:0.0} m/s");
        Report($"defends ({leader.Craft.Defences} defences, passed after {Race.Clock(race.Time)}, {race.Contacts.Count} contacts)", problems);
    }

    /// <summary>
    /// A prototype catches an F-class car just before the hairpin (turn 3): the slower car holds its
    /// line, the prototype goes by without touching it and without being held up for long.
    /// </summary>
    static void LapsAtHairpin()
    {
        var entries = new List<Entry>
        {
            new Entry { Model = "f-01", Start = 1150f, Speed = 25f, Pace = 80f, Consistency = 100f, Seed = 1 },
            new Entry { Model = "a-29", Start = 1000f, Speed = 40f, Pace = 95f, Consistency = 100f, Seed = 2 },
        };
        float where = -1f;
        Race race = RunRace(entries, r =>
        {
            if (where < 0f && r.Racers[1].Distance > r.Racers[0].Distance) where = Math.Max(r.Racers[1].Car.TrackIndex, 0) * circuit.Step;
            return r.Racers[1].Distance > r.Racers[0].Distance + 100f;
        }, 120f);
        var problems = new List<string>();
        if (race.Racers[1].Distance <= race.Racers[0].Distance) problems.Add("the prototype never got by");
        if (race.Contacts.Count > 0) problems.Add($"{race.Contacts.Count} contacts, worst {race.Contacts.Max(c => c.Speed):0.0} m/s");
        if (where > 2200f) problems.Add($"held up until {where:0} m");
        Report($"laps at the hairpin (by at {where:0} m, hairpin 1442 to 1489 m)", problems);
    }

    static Run Drive(string modelId, float pace, float consistency, float safety, int laps, int seed, Action<StandInCar, AiPilot, AiPilot.Controls> watch = null)
    {
        CarSpec spec = CarSpec.For(modelId);
        RacingLine line = LineFor(modelId);
        var pilot = new AiPilot(line, spec, pace, consistency, safety, seed);
        var car = new StandInCar(spec, line);
        GridSlot pole = circuit.Data.grid.slots[0];
        car.PlaceAt(circuit.IndexOf(pole.d - 2.6f), pole.lateral);
        var run = new Run { Predicted = pilot.Profile.LapTime, Name = spec.Name };

        float time = 0f, lapStart = -1f, stuck = 0f;
        int last = car.TrackIndex;
        float limit = pilot.Profile.LapTime * (laps + 2) * 2f;
        while (run.Laps.Count < laps && time < limit)
        {
            AiPilot.Controls c = pilot.Drive(car.Position, car.Forward, car.Velocity, car.YawRate);
            watch?.Invoke(car, pilot, c);
            car.Step(Dt, c.Throttle, c.Brake, c.Steer);
            time += Dt;

            float off = Math.Abs(car.Lateral) - circuit.HalfWidth(car.TrackIndex);
            run.WorstOff = Math.Max(run.WorstOff, off);
            if (off > KerbWidth) run.OffTrack++;
            if (run.Laps.Count > 0) run.WorstError = Math.Max(run.WorstError, Math.Abs(pilot.LateralError));
            stuck = car.Speed < 2f ? stuck + Dt : 0f;
            if (stuck > 4f) { run.Stuck = true; break; }

            int k = car.TrackIndex;
            if (last > circuit.Count - 50 && k < 50)
            {
                if (lapStart >= 0f) run.Laps.Add(time - lapStart);
                lapStart = time;
            }
            last = k;
        }
        run.Mistakes = pilot.Mistakes;
        if (run.Laps.Count < laps) run.Stuck = true;
        return run;
    }

    static void LineUsesWidth()
    {
        var problems = new List<string>();
        int bends = 0;
        foreach (CornerData corner in circuit.Data.corners)
            foreach (ArcData arc in corner.arcs)
            {
                if (arc.e - arc.s < 20f) continue;
                bends++;
                int mid = circuit.IndexOf((arc.s + arc.e) * 0.5f);
                // Inside of a right-hander (dir +1) is to the right, negative lateral.
                if (line.Lateral[mid] * arc.dir > 0f)
                    problems.Add($"turn {corner.number}: line is {Math.Abs(line.Lateral[mid]):0.0} m to the outside at the middle of the bend at {(arc.s + arc.e) * 0.5f:0} m");
                float widest = float.MinValue;
                for (float d = arc.s - 150f; d <= arc.s; d += circuit.Step)
                {
                    int k = circuit.IndexOf(d);
                    widest = Math.Max(widest, line.Lateral[k] * arc.dir - (circuit.HalfWidth(k) - RacingLine.EdgeMargin));
                }
                bool runUp = circuit.Data.corners.SelectMany(c => c.arcs).All(o => o == arc || circuit.Ahead(o.e, arc.s) > 150f);
                if (runUp && widest < -1f) problems.Add($"turn {corner.number}: line never reaches the outside before the bend at {arc.s:0} m ({-widest:0.0} m short)");
            }
        Report($"line uses the width ({bends} bends)", problems);
    }

    static void TuningHelps()
    {
        var problems = new List<string>();
        foreach (string id in new[] { "f-01", "c-20", "a-29" })
        {
            float plain = new SpeedProfile(line, CarSpec.For(id), 1f).LapTime;
            float tunedTime = LineFor(id).TunedLapTime;
            if (tunedTime >= plain) problems.Add($"{id}: tuned {Time(tunedTime)} is not faster than {Time(plain)}");
            else Console.WriteLine($"      {id}: {Time(plain)} on the minimum-curvature line, {Time(tunedTime)} tuned");
        }
        Report("tuning helps", problems);
    }

    static void CleanLaps()
    {
        var problems = new List<string>();
        var rows = new List<string>();
        foreach (CarModel model in CarModels.All)
        {
            Run r = Drive(model.Id, 100f, 100f, 100f, 2, 1);
            float flying = r.Laps.Count > 1 ? r.Laps[1] : float.NaN;
            physicsLaps[model.Id] = flying;
            float gap = (flying - r.Predicted) / r.Predicted;
            rows.Add($"    {model.Id,-6} {r.Name,-26} profile {Time(r.Predicted)}  driven {Time(flying)}  {gap * 100f,5:+0.0;-0.0}%  worst off line {r.WorstError:0.0} m, past edge {Math.Max(r.WorstOff, 0f):0.0} m");
            if (r.Stuck) problems.Add($"{model.Id} did not finish two laps");
            else if (r.OffTrack > 0) problems.Add($"{model.Id} left the track ({r.WorstOff:0.0} m past the edge)");
            else if (Math.Abs(gap) > 0.05f) problems.Add($"{model.Id} flying lap {gap * 100f:+0.0;-0.0}% off the profile");
        }
        Report("clean laps", problems);
        foreach (string row in rows) Console.WriteLine(row);
    }

    static void PaceOrders()
    {
        Run fast = Drive("a-29", 100f, 100f, 100f, 2, 1);
        Run slow = Drive("a-29", 50f, 100f, 100f, 2, 1);
        var problems = new List<string>();
        if (fast.Stuck || slow.Stuck) problems.Add("a run did not finish");
        else if (fast.Laps[1] >= slow.Laps[1]) problems.Add($"Pace 100 {Time(fast.Laps[1])} is not faster than Pace 50 {Time(slow.Laps[1])}");
        Report($"pace orders (Pace 100 {Time(fast.Laps.LastOrDefault())}, Pace 50 {Time(slow.Laps.LastOrDefault())})", problems);
    }

    static void ConsistencySpreads()
    {
        Run steady = Drive("a-29", 90f, 100f, 100f, 6, 3);
        Run erratic = Drive("a-29", 90f, 0f, 100f, 6, 3);
        var problems = new List<string>();
        float s1 = Spread(steady), s2 = Spread(erratic);
        if (steady.Stuck || erratic.Stuck) problems.Add("a run did not finish");
        else if (s2 <= s1) problems.Add($"Consistency 0 spread {s2:0.00} s is not wider than Consistency 100 spread {s1:0.00} s");
        Report($"consistency (flying lap spread {s1:0.00} s at 100, {s2:0.00} s at 0)", problems);
    }

    static void Mistakes()
    {
        Run r = Drive("a-29", 90f, 100f, 0f, 6, 7);
        var problems = new List<string>();
        if (r.Mistakes == 0) problems.Add("no corner was overcooked in 6 laps");
        if (r.Stuck) problems.Add("did not finish 6 laps");
        Run careful = Drive("a-29", 90f, 100f, 100f, 6, 7);
        if (!careful.Stuck && !r.Stuck && careful.Laps.Sum() >= r.Laps.Sum()) problems.Add("overcooking corners cost no time");
        physicsMistakeCost = r.Mistakes > 0 ? (r.Laps.Sum() - careful.Laps.Sum()) / r.Mistakes : float.NaN;
        Report($"mistakes ({r.Mistakes} overcooked corners in 6 laps cost {r.Laps.Sum() - careful.Laps.Sum():0.0} s, furthest {Math.Max(r.WorstOff, 0f):0.0} m past the edge)", problems);
    }

    static void Detail(string modelId)
    {
        int step = 0;
        RacingLine line = LineFor(modelId);
        float safety = Environment.GetEnvironmentVariable("AI_SAFETY") is string sv ? float.Parse(sv) : 100f;
        Run r = Drive(modelId, 90f, 80f, safety, 6, 7, (car, pilot, c) =>
        {
            float d = car.TrackIndex * circuit.Step;
            bool trace = Environment.GetEnvironmentVariable("AI_TRACE") is string w && Math.Abs(d - float.Parse(w)) < 120f;
            if (step++ % 50 != 0 && !trace && !(pilot.Overcooking && step % 10 == 0)) return;
            if (pilot.Overcooking) Console.Write($"OVER prof {pilot.Profile.SpeedAt(pilot.Index) * 3.6f:0} ");
            if (trace) Console.Write($"yaw {car.YawRate:+0.000;-0.000} need {car.Speed * line.Curvature[pilot.Index]:+0.000;-0.000} slip {Math.Atan2(car.Velocity.x * car.Right.x + car.Velocity.z * car.Right.z, car.Velocity.x * car.Forward.x + car.Velocity.z * car.Forward.z):+0.000;-0.000} ");
            Console.WriteLine($"{car.TrackIndex * circuit.Step,7:0} m  v {car.Speed * 3.6f,5:0} target {pilot.TargetSpeed * 3.6f,5:0} km/h  gear {car.Gear}  thr {c.Throttle:0.00} brk {c.Brake:0.00} steer {c.Steer:+0.000;-0.000}  line err {pilot.LateralError:+0.0;-0.0}  lat {car.Lateral:+0.0;-0.0}/{circuit.HalfWidth(car.TrackIndex):0.0}");
        });
        Console.WriteLine($"{r.Name}: profile {Time(r.Predicted)}, laps {string.Join(", ", r.Laps.Select(Time))}, off track steps {r.OffTrack}, stuck {r.Stuck}");
    }

    static float Spread(Run r)
    {
        var flying = r.Laps.Skip(1).ToList();
        if (flying.Count < 2) return 0f;
        float mean = flying.Average();
        return (float)Math.Sqrt(flying.Sum(t => (t - mean) * (t - mean)) / (flying.Count - 1));
    }

    static void Report(string rule, List<string> problems)
    {
        if (problems.Count == 0) { Console.WriteLine($"ok    {rule}"); return; }
        failed = true;
        Console.WriteLine($"FAIL  {rule}");
        foreach (string p in problems.Take(12)) Console.WriteLine($"      {p}");
    }

    static string Time(float s) => float.IsNaN(s) ? "--" : $"{(int)(s / 60f)}:{s % 60f:00.00}";
}
