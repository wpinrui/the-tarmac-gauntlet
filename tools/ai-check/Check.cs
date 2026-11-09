// Drives the AI round the circuit in StandInCar, a flat copy of CarController's physics, and checks
// that it laps. Each check prints one line: ok, or FAIL with what went wrong. Exit code 1 if any fails.
//
// Checks:
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

static class Check
{
    const float Dt = 0.01f;
    const float KerbWidth = 1.5f;

    static Circuit circuit;
    static RacingLine line;
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

        if (args.Length > 1)
        {
            Detail(args[1]);
            return 0;
        }

        CleanLaps();
        PaceOrders();
        ConsistencySpreads();
        Mistakes();
        return failed ? 1 : 0;
    }

    static Run Drive(string modelId, float pace, float consistency, float safety, int laps, int seed, Action<StandInCar, AiPilot, AiPilot.Controls> watch = null)
    {
        CarSpec spec = CarSpec.For(modelId);
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

    static void CleanLaps()
    {
        var problems = new List<string>();
        var rows = new List<string>();
        foreach (CarModel model in CarModels.All)
        {
            Run r = Drive(model.Id, 100f, 100f, 100f, 2, 1);
            float flying = r.Laps.Count > 1 ? r.Laps[1] : float.NaN;
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
        Report($"mistakes ({r.Mistakes} overcooked corners in 6 laps cost {r.Laps.Sum() - careful.Laps.Sum():0.0} s, furthest {Math.Max(r.WorstOff, 0f):0.0} m past the edge)", problems);
    }

    static void Detail(string modelId)
    {
        int step = 0;
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
