// Runs every garage's pit crew through stops with the game's own crew planner (GarageCrew), the car driving
// into its box and out along the game's own pit lane route, and checks them. Each rule prints one line: ok, or
// FAIL with the worst cases. Exit code 1 if any rule fails.
//
// Every garage is run with every crew size from 1 to 16, through stops that change the tyres or not, with a
// little fuel or a lot, a driver change or repairs on top, a crew from the quickest to the slowest, a car that
// stops off its mark and one that leaves at once and hard.
//
// Rules:
//   never through the car   no one comes within reach of the car (0.25 m round them) while it drives in, stands
//                           or drives off
//   at each job in its time everyone works each job inside its time: there by the time it starts, or 4 s after the
//                           car stops for jobs that start sooner (the walk round the car), and working most of the
//                           time from then (60 %). A job over too soon to walk to may be let go. Someone with several
//                           wheels works every one of them inside the tyre time
//   home after              25 s after the car is released everyone is back at their spot in the garage, idle,
//                           and the crew rests
//   out of the fast lane    no one steps into the fast lane, where cars pass the boxes at the limiter
//
// And the two drivers of every box (DriverSwap), on the stops that change driver:
//   driver changed in time  by the end of the stop the new driver is in the seat, the other out and the door shut
//   drivers clear of the car neither driver comes within reach of the car, but for getting in and out by the door
//   drivers clear of the crew a standing driver keeps 0.35 m from every crew member's middle
//   drivers home after      25 s after the release the one who got out waits at the back of the garage
//
// TRACE="<a stop as a failure names it>" WHO=<person, from 1> prints that person through that stop.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    const float Dt = 1f / 30f;
    const float Reach = 0.25f;
    const float WalkRound = 4f, Cover = 0.6f;
    const float HomeBy = 25f;

    static bool failed;
    static readonly List<(float depth, string where)> throughCar = new List<(float, string)>();
    static readonly List<(float bad, string where)> late = new List<(float, string)>();
    static readonly List<(float bad, string where)> notHome = new List<(float, string)>();
    static readonly List<(float bad, string where)> inFastLane = new List<(float, string)>();
    static readonly List<(float bad, string where)> notChanged = new List<(float, string)>();
    static readonly List<(float bad, string where)> driverInCar = new List<(float, string)>();
    static readonly List<(float bad, string where)> driverOnCrew = new List<(float, string)>();
    static readonly List<(float bad, string where)> driverNotHome = new List<(float, string)>();
    const float Apart = 0.35f;
    static int changes;
    static int stops, people, jobs;

    /// <summary>One stop to run: its work and the car's way in and out.</summary>
    struct Stop
    {
        public string Name;
        public float Litres, Extra, Scale;
        public bool Tyres, Driver;
        public float CarLength, CarWidth, StopOff, Wait, Accel;
    }

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var circuit = new Circuit(data);
        var builder = new CircuitBuilder(circuit, null);
        builder.Build();
        var complex = new PitComplex(circuit, builder.GarageFloors);
        var lane = new PitLane(circuit);
        var spots = new PitStopSpots(complex, lane);
        MethodInfo across = typeof(PitLane).GetMethod("Across", BindingFlags.NonPublic | BindingFlags.Instance);

        Stop[] kinds =
        {
            new Stop { Name = "fuel and tyres", Litres = 30f, Tyres = true, CarLength = 4.6f, CarWidth = 1.9f, Accel = 4f },
            new Stop { Name = "splash only", Litres = 4f, CarLength = 4.2f, CarWidth = 1.75f, Accel = 10f },
            new Stop { Name = "tyres, a driver change", Litres = 0f, Tyres = true, Driver = true, Extra = 2.5f, CarLength = 4.9f, CarWidth = 2.0f, Accel = 6f, Wait = 1.5f },
            new Stop { Name = "long fuel and tyres", Litres = 90f, Tyres = true, CarLength = 4.7f, CarWidth = 1.95f, Accel = 10f, StopOff = 0.5f },
            new Stop { Name = "nothing to do", Litres = 0f, CarLength = 4.4f, CarWidth = 1.8f, Accel = 8f },
            new Stop { Name = "fuel, tyres and repairs", Litres = 20f, Tyres = true, Extra = 6f, CarLength = 5.2f, CarWidth = 2.0f, Accel = 5f, StopOff = -0.6f, Wait = 3f },
        };

        // The fast lane's edge in each box's frame.
        float fast = -lane.Data.laneWidth * 0.5f + lane.Data.fastLane - lane.BoxLateral;

        for (int g = 0; g < complex.Garages; g++)
            for (int n = 1; n <= PitComplex.StandbySpots; n++)
            {
                // Crew size and the car's Pit Stop Time stat and the player's Engineer skill scale the work, as PitService does.
                float crewMultiplier = 1f + (1f - n / 16f) * 0.8f;
                for (int k = 0; k < kinds.Length; k++)
                {
                    if ((g + n + k) % 2 == 1 && g != complex.StartGarage) continue;
                    Stop s = kinds[k];
                    float stat = (g * 37 + n * 11 + k * 5) % 101;
                    float engineer = (k % 3 == 2) ? 0.75f : 1f;
                    s.Scale = (0.85f + 0.3f * stat / 100f) * crewMultiplier * engineer;
                    Run(complex, lane, spots, across, fast, g, n, s);
                }
            }

        Report("never through the car", throughCar, $"{stops} stops, {people} people", "m into the car");
        Report("at each job in its time", late, $"{jobs} jobs", "");
        Report("home after", notHome, $"{people} people home", "");
        Report("out of the fast lane", inFastLane, $"its edge {-fast:0.00} m out from the box's middle", "m into it");
        Report("driver changed in time", notChanged, $"{changes} changes", "");
        Report("drivers clear of the car", driverInCar, $"{stops} stops", "m into the car");
        Report("drivers clear of the crew", driverOnCrew, $"{Apart:0.00} m apart", "m too close");
        Report("drivers home after", driverNotHome, $"{stops} stops", "");
        return failed ? 1 : 0;
    }

    static void Report(string rule, List<(float bad, string where)> bad, string ok, string unit)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {ok}");
        else
        {
            failed = true;
            var worst = bad.OrderByDescending(b => b.bad).Take(8).Select(b => $"{b.where} ({b.bad:0.00}{(unit.Length > 0 ? " " + unit : "")})");
            Console.WriteLine($"FAIL  {rule}: {bad.Count} failures, worst {string.Join("; ", worst)}");
        }
    }

    /// <summary>The car's middle and heading in the world at a fractional lane sample on its route into the box.</summary>
    static (Vector3 at, Vector3 fwd) OnRoute(PitLane lane, MethodInfo across, int g, float i)
    {
        Vector3 P(float j) => lane.LanePoint(j, (float)across.Invoke(lane, new object[] { j, g }));
        Vector3 a = P(i - 0.25f), b = P(i + 0.25f);
        Vector3 d = b - a;
        d.y = 0f;
        return (P(i), d.normalized);
    }

    /// <summary>Metres per lane sample round a fractional sample.</summary>
    static float Spacing(PitLane lane, float i)
    {
        Vector3 d = lane.LanePoint(i + 0.5f, 0f) - lane.LanePoint(i - 0.5f, 0f);
        d.y = 0f;
        return Mathf.Max(d.magnitude, 0.1f);
    }

    static void Run(PitComplex complex, PitLane lane, PitStopSpots spots, MethodInfo across, float fastY, int g, int n, Stop s)
    {
        stops++;
        var crew = new GarageCrew(complex, spots, g, n, 1000 + g);
        var swap = new DriverSwap(complex, spots, g, true, 2000 + g);
        crew.DoorSide = swap.DoorSide;
        if (s.Driver) changes++;
        float worstCar = 0f, worstCrew = 0f;
        string carWho = "", crewWho = "";
        people += crew.People.Length;
        string tag = $"garage {g + 1}, {n} crew, {s.Name}";
        float box = lane.BoxLane[g];
        float limit = lane.LimitSpeed * 0.95f;

        // The stop's clock, as PitService lays it out.
        float fuelFrom = 2f * s.Scale, fuelTo = (2f + s.Litres / 3f) * s.Scale;
        float tyresTo = fuelTo + (s.Tyres ? 6f * s.Scale : 0f);
        float duration = tyresTo + s.Extra;
        var clock = new StopClock
        {
            FuelFrom = fuelFrom, FuelTo = fuelTo, TyresFrom = fuelTo, TyresTo = tyresTo, Duration = duration,
            NewTyres = s.Tyres, NewDriver = s.Driver, CarLength = s.CarLength, CarWidth = s.CarWidth,
        };

        // Per person: per task, the first time worked, the time worked, and its window.
        var firstAt = new Dictionary<(int, int), float>();
        var worked = new Dictionary<(int, int), float>();
        float worstDepth = 0f, worstLane = 0f;
        void Drivers(in StopClock c)
        {
            swap.Step(c, Dt);
            Vector2 f = heading.normalized;
            var side = new Vector2(-f.y, f.x);
            for (int d = 0; d < 2; d++)
            {
                DriverFigure fig = swap.Figures[d];
                if (fig.InCar || fig.Lift > 0f) continue;
                Vector2 at = fig.Body.At;
                if (c.CarNear)
                {
                    Vector2 r = at - c.Car;
                    float a = Mathf.Abs(Vector2.Dot(r, f)) - c.CarLength * 0.5f, b = Mathf.Abs(Vector2.Dot(r, side)) - c.CarWidth * 0.5f;
                    float outside = a > 0f && b > 0f ? Mathf.Sqrt(a * a + b * b) : Mathf.Max(a, b);
                    if (Reach - outside > worstCar) { worstCar = Reach - outside; carWho = $"driver {d + 1} at {c.Stage} {c.Elapsed:0.0} s, at ({at.x:0.0}, {at.y:0.0})"; }
                }
                for (int p = 0; p < crew.People.Length; p++)
                {
                    float gap = (crew.People[p].At - at).magnitude;
                    CrewTask? cur = crew.Current(crew.People[p]);
                    if (Apart - gap > worstCrew) { worstCrew = Apart - gap; crewWho = $"driver {d + 1} and person {p + 1} {cur?.Job} at {c.Stage} {c.Elapsed:0.0} s, at ({at.x:0.0}, {at.y:0.0})"; }
                }
                float into = fastY - (at.y - Reach);
                if (into > worstLane) { worstLane = into; laneWho = $"driver {d + 1} at {c.Stage} ({at.x:0.0}, {at.y:0.0})"; }
            }
        }
        worstWho = "";
        float stopAt = 0f + s.StopOff;   // where the car stops, m along the box from its middle

        // Into the box: braking to stop at its mark from the limiter, the crew out to meet it.
        float dist = 140f;
        float t = 0f;
        clock.Stage = CrewStage.Ready;
        clock.NewDriver = s.Driver;
        while (true)
        {
            float v = Mathf.Min(limit, Mathf.Sqrt(2f * 3.5f * Mathf.Max(dist, 0f)));
            dist -= Mathf.Max(v, 0.3f) * Dt;
            bool there = dist <= 0f;
            if (there) dist = 0f;
            float i = box + (stopAt - dist) / Spacing(lane, box);
            Car(crew, lane, across, g, i, there ? 0f : v, ref clock);
            crew.Step(clock, Dt);
            Watch(crew, clock, ref worstDepth, ref worstLane, fastY);
            Drivers(clock);
            t += Dt;
            if (there) break;
        }

        // The stop.
        clock.Stage = CrewStage.Stop;
        clock.CarSpeed = 0f;
        for (float e = 0f; e < duration; e += Dt)
        {
            clock.Elapsed = e;
            crew.Step(clock, Dt);
            Drivers(clock);
            if (Environment.GetEnvironmentVariable("TRACE") == tag && (int)(e / Dt) % 10 == 0) Trace(crew, clock, e);
            Watch(crew, clock, ref worstDepth, ref worstLane, fastY);
            for (int p = 0; p < crew.People.Length; p++)
            {
                CrewMember m = crew.People[p];
                CrewTask? cur = crew.Current(m);
                if (cur == null || !m.Working) continue;
                int idx = m.Tasks.IndexOf(cur.Value);
                var key = (p, idx);
                if (!firstAt.ContainsKey(key)) firstAt[key] = e;
                worked[key] = (worked.TryGetValue(key, out float w) ? w : 0f) + Dt;
            }
        }
        clock.Elapsed = duration;
        if (s.Driver)
        {
            DriverFigure now = swap.Figures[swap.Driving], other = swap.Figures[1 - swap.Driving];
            if (!now.InCar || now.Lift < 1f || other.InCar || other.Lift > 0f || swap.Door > 0.001f)
                notChanged.Add((1f, $"{tag}: in car {now.InCar}, out {!other.InCar}, door {swap.Door:0.00}, pace {swap.Pace:0.00}"));
        }

        // Each job: there on time and working most of it; each of several wheels worked inside the tyre time.
        for (int p = 0; p < crew.People.Length; p++)
        {
            CrewMember m = crew.People[p];
            for (int k = 0; k < m.Tasks.Count; k++)
            {
                CrewTask task = m.Tasks[k];
                (float from, float to) w = crew.TimeOf(task, clock);
                if (w.to <= w.from) continue;
                jobs++;
                string what = $"{tag}: person {p + 1} {task.Job}{(task.Job == CrewTask.Kind.Wheel || task.Job == CrewTask.Kind.Jack ? " " + task.Spot : "")}";
                bool did = firstAt.TryGetValue((p, k), out float first);
                if (task.Parts > 1)
                {
                    if (!did || worked[(p, k)] < 0.25f) late.Add((did ? 0.25f - worked[(p, k)] : 1f, what + $" wheel {task.Part + 1} of {task.Parts} not worked in the tyre time"));
                    continue;
                }
                // The time they can be there for: from its start, or once they have had time to walk round the car.
                float start = Mathf.Max(w.from, WalkRound), span = w.to - start;
                if (span < 0.5f) { jobs--; continue; }
                if (!did) { late.Add((span, what + " never worked")); continue; }
                if (first > Mathf.Max(w.from + 0.5f, WalkRound) + Dt) late.Add((first - w.from, what + $" there {first - w.from:0.0} s after it starts"));
                else if (worked[(p, k)] < span * Cover - Dt) late.Add((1f - worked[(p, k)] / span, what + $" worked {worked[(p, k)] / span:P0} of it"));
            }
        }

        // Released: the car waits for the fast lane, then drives off; the crew walk in.
        clock.Stage = CrewStage.Release;
        float speed = 0f, gone = 0f;
        for (float r = 0f; r < HomeBy; r += Dt)
        {
            if (r >= s.Wait)
            {
                speed = Mathf.Min(limit, speed + s.Accel * Dt);
                gone += speed * Dt;
            }
            float i = box + (stopAt + gone) / Spacing(lane, box);
            Car(crew, lane, across, g, i, speed, ref clock);
            crew.Step(clock, Dt);
            Watch(crew, clock, ref worstDepth, ref worstLane, fastY);
            Drivers(clock);
        }
        // Back to racing: the crew must already be home.
        clock.Stage = CrewStage.Home;
        clock.CarNear = false;
        crew.Step(clock, Dt);
        swap.Step(clock, Dt);
        DriverFigure waiting = swap.Figures[1 - swap.Driving];
        if (!swap.AtRest || waiting.Body.At != swap.Rest) driverNotHome.Add(((waiting.Body.At - swap.Rest).magnitude, $"{tag}: waiting driver at ({waiting.Body.At.x:0.0}, {waiting.Body.At.y:0.0}), {waiting.Body.Clip}"));
        if (worstCar > 0f) driverInCar.Add((worstCar, tag + ": " + carWho));
        if (worstCrew > 0f) driverOnCrew.Add((worstCrew, tag + ": " + crewWho));
        int away = crew.People.Count(m => (m.At - m.Home).magnitude > 0.01f || m.Clip != "Idle");
        if (away > 0 || !crew.AtRest) notHome.Add((away, $"{tag}: {away} not home and idle"));

        if (worstDepth > 0f) throughCar.Add((worstDepth, tag + ": " + worstWho));
        if (worstLane > 0f) inFastLane.Add((worstLane, tag + ": " + laneWho));
    }

    /// <summary>The car on its route at lane sample i, in the box's frame, into the clock.</summary>
    static void Car(GarageCrew crew, PitLane lane, MethodInfo across, int g, float i, float speed, ref StopClock clock)
    {
        (Vector3 at, Vector3 fwd) c = OnRoute(lane, across, g, i);
        clock.Car = crew.Local(c.at);
        clock.CarSpeed = speed;
        clock.CarNear = Mathf.Abs(clock.Car.x) < 40f && Mathf.Abs(clock.Car.y) < 12f;
        heading = crew.Local(crew.Box + c.fwd);
    }

    /// <summary>With TRACE set to a stop's name as the failures print it and WHO to a person (from 1), prints them through the stop.</summary>
    static void Trace(GarageCrew crew, in StopClock clock, float e)
    {
        CrewMember m = crew.People[int.Parse(Environment.GetEnvironmentVariable("WHO") ?? "1") - 1];
        CrewTask? cur = crew.Current(m);
        Console.WriteLine($"{e:0.0} s at ({m.At.x:0.00}, {m.At.y:0.00}) {m.Clip} {cur?.Job} {cur?.Spot} working {m.Working}, car ({clock.Car.x:0.00}, {clock.Car.y:0.00})");
    }

    static Vector2 heading;
    static string worstWho, laneWho;

    /// <summary>How far anyone reaches into the car, an oriented box round its middle, and into the fast lane.</summary>
    static void Watch(GarageCrew crew, in StopClock clock, ref float depth, ref float fast, float fastY)
    {
        Vector2 f = heading.normalized;
        var side = new Vector2(-f.y, f.x);
        float hl = clock.CarLength * 0.5f, hw = clock.CarWidth * 0.5f;
        foreach (CrewMember m in crew.People)
        {
            if (clock.CarNear)
            {
                Vector2 d = m.At - clock.Car;
                float a = Mathf.Abs(Vector2.Dot(d, f)) - hl, b = Mathf.Abs(Vector2.Dot(d, side)) - hw;
                // Signed distance from the car's outline; under Reach means within reach.
                float outside = a > 0f && b > 0f ? Mathf.Sqrt(a * a + b * b) : Mathf.Max(a, b);
                if (Reach - outside > depth)
                {
                    depth = Reach - outside;
                    int p = Array.IndexOf(crew.People, m);
                    CrewTask? cur = crew.Current(m);
                    worstWho = $"person {p + 1} {(cur?.Job.ToString() ?? "")} {(cur?.Spot.ToString() ?? "")} at {clock.Stage} {clock.Elapsed:0.0} s, at ({m.At.x:0.0}, {m.At.y:0.0}), car ({clock.Car.x:0.0}, {clock.Car.y:0.0})";
                }
            }
            float into = fastY - (m.At.y - Reach);
            if (into > fast)
            {
                fast = into;
                CrewTask? cur = crew.Current(m);
                laneWho = $"person {Array.IndexOf(crew.People, m) + 1} {cur?.Job} {cur?.Spot} at {clock.Stage} ({m.At.x:0.0}, {m.At.y:0.0})";
            }
        }
    }
}
