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
//   clear of passing cars   a car driving to or from any other box, or along the fast lane, keeps at least 0.5 m
//                           from every place a crew works a job round a car standing in its box (0.25 m round them)
//
// And the two drivers of every box (DriverSwap), on the stops that change driver:
//   driver changed in time  by the end of the stop the new driver is in the seat, the other out and the door shut
//   drivers clear of the car neither driver comes within reach of the car, but for getting in and out by the door
//   drivers clear of the crew a standing driver keeps 0.35 m from every crew member's middle
//   drivers home after      25 s after the release the one who got out waits at the back of the garage
//
// And the kit every crew handles (KitWork), the hands a step in front of each person:
//   fuel hose in step       on every stop that fuels, the nozzle is on the filler from the fuel's start (0.35 s
//                           allowed) to its end, off within a frame of it, and never on outside the fuel time
//   wheels changed in time  on every stop that changes tyres, each wheel's gun is on its hub while it is worked, and
//                           each fresh tyre brought out is on the car, the car's wheels all back on, by the release
//   lance in for the tyres  the rear jack has the lance in its socket for most of the tyre time (70 %)
//   lines in reach          no hanging piece is taken further from the boom than its line reaches
//   kit home after          25 s after the release every piece is back on its rest and the car down off its jacks
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
    static readonly List<(float bad, string where)> hoseLate = new List<(float, string)>();
    static readonly List<(float bad, string where)> wheelsLate = new List<(float, string)>();
    static readonly List<(float bad, string where)> lanceLate = new List<(float, string)>();
    static readonly List<(float bad, string where)> notFetched = new List<(float, string)>();
    static readonly List<(float bad, string where)> overReach = new List<(float, string)>();
    static readonly List<(float bad, string where)> kitNotHome = new List<(float, string)>();
    static int fuelStops, wheelsChanged, fetches;
    static PitBoxKit boxKit;
    static int stops, people, jobs;
    const float PassGap = 0.5f;
    const float PassLength = 5.2f, PassWidth = 2.0f;   // the biggest car the stops are run with
    // Per garage, every place its crew work a job at a stop, world x and z on a 5 cm grid.
    static readonly Dictionary<int, HashSet<(int, int)>> workedAt = new Dictionary<int, HashSet<(int, int)>>();
    static readonly List<(float bad, string where)> passing = new List<(float, string)>();
    static float closestPass = float.MaxValue;

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
        boxKit = new PitBoxKit(complex);

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

        int only = int.TryParse(Environment.GetEnvironmentVariable("GARAGE"), out int og) ? og - 1 : -1;
        for (int g = 0; g < complex.Garages; g++)
            if (only < 0 || g == only)
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

        Passing(circuit, lane);

        Report("never through the car", throughCar, $"{stops} stops, {people} people", "m into the car");
        Report("at each job in its time", late, $"{jobs} jobs", "");
        Report("home after", notHome, $"{people} people home", "");
        Report("out of the fast lane", inFastLane, $"its edge {-fast:0.00} m out from the box's middle", "m into it");
        Report("clear of passing cars", passing, $"closest {closestPass:0.00} m", "m short");
        Report("driver changed in time", notChanged, $"{changes} changes", "");
        Report("drivers clear of the car", driverInCar, $"{stops} stops", "m into the car");
        Report("drivers clear of the crew", driverOnCrew, $"{Apart:0.00} m apart", "m too close");
        Report("drivers home after", driverNotHome, $"{stops} stops", "");
        Report("fuel hose in step", hoseLate, $"{fuelStops} stops fuelled", "");
        Report("wheels changed in time", wheelsChanged > 0 ? wheelsLate : wheelsLate, $"{wheelsChanged} wheels", "");
        Report("lance in for the tyres", lanceLate, "rear jacks", "");
        // Not a rule: a fetch is let go when the car comes in too soon for it, as it does from the check's short way in.
        Console.WriteLine($"info  kit fetched before the car stopped: {fetches - notFetched.Count} of {fetches} pieces");
        Report("lines in reach", overReach, "every line", "m over");
        Report("kit home after", kitNotHome, $"{stops} stops", "");
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
        var work = new KitWork(complex, boxKit, crew);
        float nozzleFirst = -1f, nozzleLast = -1f, nozzleOn = 0f, nozzleStray = 0f, lanceOn = 0f, reachOver = 0f;
        var gunOnHub = new bool[4];
        var carried = new bool[4];
        string reachWho = "";
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
        Vector3 Hand(int p, bool right)
        {
            CrewMember m = crew.People[p];
            Vector3 face = crew.WorldDirection(m.Facing).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, face);
            return crew.World(m.At) + Vector3.up * (m.Low ? 0.55f : 0.9f) + face * (m.Low ? 0.35f : 0.15f) + side * (right ? 0.22f : -0.22f);
        }
        bool dump = Environment.GetEnvironmentVariable("DUMP") == tag;
        var frames = new List<string>();
        void Kit(in StopClock c)
        {
            if (dump) frames.Add(Frame(crew, work, c, swap));
            Vector2 f = heading.normalized;
            Vector3 fwd = crew.WorldDirection(f).normalized;
            Vector3 mid = crew.Box + crew.Forward * c.Car.x + crew.ToGarage * c.Car.y;
            mid.y = crew.Box.y;
            work.Step(c, KitCar.Standing(mid, fwd, crew.ToGarage, c.CarLength, c.CarWidth), Hand, Dt);
            for (KitItem item = KitItem.Nozzle; item <= KitItem.Lance; item++)
            {
                if (!work.Out[(int)item]) continue;
                float over = (work.Pieces[(int)item].Position - work.Anchor(item)).magnitude - PitBoxKit.LineLength(item);
                if (over > reachOver) { reachOver = over; reachWho = $"{item} at {c.Stage} {c.Elapsed:0.0} s"; }
            }
        }
        worstWho = "";
        float stopAt = 0f + s.StopOff;   // where the car stops, m along the box from its middle

        // Into the box: braking to stop at its mark from the limiter, the crew out to meet it.
        // The crew see the car coming from the pit entry: the further down the lane the box, the sooner.
        float dist = 140f + 7f * g;
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
            Kit(clock);
            t += Dt;
            if (there) break;
        }

        // The stop.
        clock.Stage = CrewStage.Stop;
        clock.CarSpeed = 0f;
        // What was fetched before the car stopped.
        foreach (CrewMember m in crew.People)
        {
            if (m.FetchAt == null || m.FetchTyre && !s.Tyres) continue;
            fetches++;
            if (!m.Carrying) notFetched.Add((1f, $"{tag}: person {Array.IndexOf(crew.People, m) + 1} {m.Tasks[0].Job} {m.Tasks[0].Spot}"));
        }
        for (int w = 0; w < 4; w++) carried[w] = work.TyreHand(w) >= 0 && crew.People[work.TyreHand(w)].Carrying && s.Tyres;
        for (float e = 0f; e < duration; e += Dt)
        {
            clock.Elapsed = e;
            crew.Step(clock, Dt);
            Drivers(clock);
            Kit(clock);
            if (work.NozzleOn)
            {
                if (nozzleFirst < 0f) nozzleFirst = e;
                nozzleLast = e;
                if (e >= fuelFrom && e < fuelTo) nozzleOn += Dt;
                nozzleStray = Mathf.Max(nozzleStray, Mathf.Max(fuelFrom - Dt - e, e - fuelTo - Dt));
            }
            if (work.LanceIn) lanceOn += Dt;
            for (int w = 0; w < 4; w++) gunOnHub[w] |= work.OnHub[w];
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
        // The fuel hose on the filler in step with the fuel time.
        if (fuelTo - fuelFrom >= KitWork.MinFuel)
        {
            fuelStops++;
            if (nozzleFirst < 0f) hoseLate.Add((fuelTo - fuelFrom, $"{tag}: never on"));
            else if (nozzleFirst > fuelFrom + 0.35f) hoseLate.Add((nozzleFirst - fuelFrom, $"{tag}: on {nozzleFirst - fuelFrom:0.00} s late"));
            else if (nozzleLast < fuelTo - 2f * Dt) hoseLate.Add((fuelTo - nozzleLast, $"{tag}: off {fuelTo - nozzleLast:0.00} s early"));
            else if (nozzleOn < (fuelTo - Mathf.Max(fuelFrom, nozzleFirst)) - 2f * Dt) hoseLate.Add((1f, $"{tag}: on {nozzleOn:0.0} s of the fuel time"));
        }
        else if (nozzleFirst >= 0f) hoseLate.Add((1f, $"{tag}: on with no fuel"));
        if (nozzleStray > 0f) hoseLate.Add((nozzleStray, $"{tag}: on {nozzleStray:0.00} s outside the fuel time"));
        // Each wheel done by the release; the lance in for the tyre time.
        if (s.Tyres)
        {
            for (int w = 0; w < 4; w++)
            {
                wheelsChanged++;
                if (!gunOnHub[w]) wheelsLate.Add((1f, $"{tag}: wheel {w + 1} gun never on the hub"));
                if (work.WheelOff[w]) wheelsLate.Add((1f, $"{tag}: wheel {w + 1} still off at the release"));
                else if (carried[w] && !work.OldOut[w]) wheelsLate.Add((1f, $"{tag}: wheel {w + 1} fresh tyre brought out but not changed"));
            }
            int jack = Array.FindIndex(crew.People, m => m.Tasks.Any(k => k.Job == CrewTask.Kind.Jack && k.Spot == PitStopSpots.StopJob.RearJack));
            if (jack >= 0)
            {
                (float from, float to) jw = crew.TimeOf(crew.People[jack].Tasks[0], clock);
                if (jw.to - jw.from > 1f && lanceOn < (jw.to - jw.from) * 0.7f) lanceLate.Add((1f - lanceOn / (jw.to - jw.from), $"{tag}: in {lanceOn:0.0} s of {jw.to - jw.from:0.0} s"));
            }
        }
        // Where each job is worked, for the check on passing cars.
        if (!workedAt.TryGetValue(g, out HashSet<(int, int)> at)) workedAt[g] = at = new HashSet<(int, int)>();
        foreach (CrewMember m in crew.People)
            foreach (CrewTask task in m.Tasks)
            {
                Vector3 w = crew.World(crew.PlaceOf(task).at);
                at.Add((Mathf.RoundToInt(w.x * 20f), Mathf.RoundToInt(w.z * 20f)));
            }
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
            Kit(clock);
        }
        // Back to racing: the crew must already be home.
        clock.Stage = CrewStage.Home;
        clock.CarNear = false;
        crew.Step(clock, Dt);
        swap.Step(clock, Dt);
        Kit(clock);
        if (work.Busy || work.Lift > 0f)
            kitNotHome.Add((1f, $"{tag}: out {string.Join(", ", Enumerable.Range(0, PitBoxKit.ItemCount).Where(k => work.Out[k]).Select(k => (KitItem)k))}{(work.OldOut.Any(o => o) ? ", an old wheel" : "")}"));
        if (dump)
        {
            string anchors = string.Join(",", Enumerable.Range(0, 6).Select(k => V(work.Anchor((KitItem)k))));
            File.WriteAllText(Environment.GetEnvironmentVariable("DUMP_TO") ?? "stop.json",
                $"{{\"tag\":\"{tag}\",\"box\":{V(crew.Box)},\"fwd\":{V(crew.Forward)},\"toGarage\":{V(crew.ToGarage)},\"door\":{crew.Door:0.###},\"len\":{s.CarLength},\"wid\":{s.CarWidth},\"fuel\":[{fuelFrom:0.##},{fuelTo:0.##}],\"tyres\":[{fuelTo:0.##},{tyresTo:0.##}],\"duration\":{duration:0.##},\"anchors\":[{anchors}],\"frames\":[{string.Join(",", frames)}]}}");
        }
        if (reachOver > 0.05f) overReach.Add((reachOver, $"{tag}: {reachWho}"));
        DriverFigure waiting = swap.Figures[1 - swap.Driving];
        if (!swap.AtRest || waiting.Body.At != swap.Rest) driverNotHome.Add(((waiting.Body.At - swap.Rest).magnitude, $"{tag}: waiting driver at ({waiting.Body.At.x:0.0}, {waiting.Body.At.y:0.0}), {waiting.Body.Clip}"));
        if (worstCar > 0f) driverInCar.Add((worstCar, tag + ": " + carWho));
        if (worstCrew > 0f) driverOnCrew.Add((worstCrew, tag + ": " + crewWho));
        int away = crew.People.Count(m => (m.At - m.Home).magnitude > 0.01f || m.Clip != "Idle");
        if (away > 0 || !crew.AtRest) notHome.Add((away, $"{tag}: {away} not home and idle"));

        if (worstDepth > 0f) throughCar.Add((worstDepth, tag + ": " + worstWho));
        if (worstLane > 0f) inFastLane.Add((worstLane, tag + ": " + laneWho));
    }

    /// <summary>
    /// Drives the biggest car along the routes to and from the nearby boxes and along the fast lane, past each garage's
    /// box, and finds how close its outline comes to any place its crew work a job round the car standing there.
    /// </summary>
    static void Passing(Circuit circuit, PitLane lane)
    {
        var from = new RacingLine(circuit);
        var routes = new Dictionary<int, RacingLine>();
        RacingLine RouteOf(int h) => routes.TryGetValue(h, out RacingLine r) ? r : routes[h] = lane.Route(from, h);
        float hl = PassLength * 0.5f, hw = PassWidth * 0.5f;
        foreach (KeyValuePair<int, HashSet<(int, int)>> kv in workedAt)
        {
            int g = kv.Key;
            Vector2[] places = kv.Value.Select(q => new Vector2(q.Item1 / 20f, q.Item2 / 20f)).ToArray();
            Vector3 box = lane.BoxPoint(g), left = lane.LaneLeft(lane.BoxLane[g]);
            // A point in the box's frame: metres along the lane and across it, towards the garages.
            Vector2 Local(float x, float z) => new Vector2((x - box.x) * left.z - (z - box.z) * left.x, (x - box.x) * left.x + (z - box.z) * left.z);
            float worst = float.MaxValue;
            string who = "";
            var others = new List<int> { -1 };
            for (int h = Mathf.Max(g - 4, 0); h <= Mathf.Min(g + 4, lane.Garages - 1); h++) if (h != g) others.Add(h);
            foreach (int h in others)
            {
                RacingLine r = RouteOf(h);
                int n = r.Count;
                for (int k = 0; k < n; k++)
                {
                    Vector3 a = r.Point[k], b = r.Point[(k + 1) % n];
                    if (Flat(a - box).magnitude > 45f) continue;
                    // The car points the way the route runs here.
                    Vector3 f3 = Flat(b - a).normalized;
                    var fwd = new Vector2(f3.x, f3.z);
                    var side = new Vector2(-fwd.y, fwd.x);
                    for (int q = 0; q < 8; q++)
                    {
                        Vector3 c = Vector3.Lerp(a, b, q / 8f);
                        foreach (Vector2 p in places)
                        {
                            Vector2 d = p - new Vector2(c.x, c.z);
                            float da = Mathf.Abs(Vector2.Dot(d, fwd)) - hl, db = Mathf.Abs(Vector2.Dot(d, side)) - hw;
                            float gap = (da > 0f && db > 0f ? Mathf.Sqrt(da * da + db * db) : Mathf.Max(da, db)) - Reach;
                            if (gap >= worst) continue;
                            worst = gap;
                            Vector2 u = Local(p.x, p.y), v = Local(c.x, c.z);
                            who = $"{(h < 0 ? "the fast lane" : $"garage {h + 1}'s route")}, crew at ({u.x:0.0}, {u.y:0.0}), car at ({v.x:0.0}, {v.y:0.0}) along and across the lane";
                        }
                    }
                }
            }
            closestPass = Mathf.Min(closestPass, worst);
            if (worst < PassGap) passing.Add((PassGap - worst, $"garage {g + 1}'s crew, {who}, {worst:0.00} m away"));
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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

    static string V(Vector3 v) => $"[{v.x:0.###},{v.y:0.###},{v.z:0.###}]";

    /// <summary>One frame of a stop for DUMP: the clock, the car, the crew, the drivers and the kit, in the world.</summary>
    static string Frame(GarageCrew crew, KitWork work, in StopClock c, DriverSwap swap)
    {
        Vector3 mid = crew.Box + crew.Forward * c.Car.x + crew.ToGarage * c.Car.y;
        mid.y = crew.Box.y;
        Vector3 fwd = crew.WorldDirection(heading.normalized).normalized;
        var people = crew.People.Select(m => $"[{V(crew.World(m.At))},{V(crew.WorldDirection(m.Facing))},{(m.Low ? 1 : 0)}]");
        var drivers = swap.Figures.Where(d => !d.InCar).Select(d => V(crew.World(d.Body.At)));
        var pieces = Enumerable.Range(0, PitBoxKit.ItemCount).Select(k =>
            $"[{V(work.Pieces[k].Position)},{V(work.Pieces[k].Forward)},{V(work.Pieces[k].Up)},{(work.Hidden[k] ? 1 : 0)}]");
        var old = Enumerable.Range(0, 4).Select(w => work.OldOut[w] ? $"[{V(work.OldWheel[w].Position)},{V(work.OldWheel[w].Forward)}]" : "0");
        return $"{{\"t\":\"{c.Stage} {c.Elapsed:0.0}\",\"car\":[{V(mid)},{V(fwd)}],\"people\":[{string.Join(",", people)}],\"drivers\":[{string.Join(",", drivers)}],"
            + $"\"pieces\":[{string.Join(",", pieces)}],\"old\":[{string.Join(",", old)}],\"off\":[{string.Join(",", work.WheelOff.Select(o => o ? 1 : 0))}],"
            + $"\"lift\":{work.Lift:0.##},\"door\":{swap.Door:0.##},\"nozzle\":{(work.NozzleOn ? 1 : 0)}}}";
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
