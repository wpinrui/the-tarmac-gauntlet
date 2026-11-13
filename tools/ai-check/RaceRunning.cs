// Checks of the race running: the rolling start, fuel and tyre use per lap, and AI pit stops.
//
//   rolling start   100 cars, as in the raceday scene, follow the pace car round a formation lap two by
//                   two without touching, stay within 200 m of their places, and go green at the line
//                   once the pace car has gone into the pit lane; from the green until the last car
//                   crosses the line and starts racing, there are no hard hits. The racing after that,
//                   with the quickest cars carving through from the back, is the pack race's to check
//   supplies        one car of each class laps on a full tank: each lap's fuel and tyre wear land within
//                   40% of what the race maths says a lap uses
//   pit stops       in a 16-car race, the cars short of fuel pit on the first lap: each keeps to the
//                   limiter, stops within a metre of its own box, leaves full, rejoins, and nobody touches
//                   in the pit lane; the full cars don't stop
//   crowded pits    the same with every car short of fuel, so the whole field is in the pit lane at once
//   pit lane start  (run.sh pitstart) 100 cars, as in the raceday scene, queue in the fast lane quickest
//                   first with the Cheonma last, move up to the pit exit line and leave three seconds apart, gliding until just before
//                   the second-last car goes as the scene's fast forward, then with the nearest 24 to the
//                   Cheonma on physics: no car crosses the line before its turn, every car rejoins its racing line,
//                   keeps to the limiter and the track on its way out, nobody touches in the pit lane,
//                   there are no hard hits, and nobody pits until the whole field is out
//   gliding         (run.sh glide) the rolling start and pit stops again with only the nearest 24 cars
//                   within 300 m of the last car on physics, as PhysicsRange does: the far cars glide,
//                   are called in to pit while gliding, and the same holds
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static partial class Check
{
    /// <summary>The raceday scene's grid: 100 cars, reversed so the slowest is on pole and the Cheonma last.</summary>
    static List<string> SceneField()
    {
        (char Class, int Count)[] counts = { ('a', 17), ('b', 17), ('c', 17), ('d', 17), ('e', 16), ('f', 16) };
        var field = new List<string> { "a-29" };
        foreach ((char cls, int count) in counts)
        {
            List<string> models = CarModels.All.Where(m => m.Id[0] == cls && m.Id[1] == '-').Select(m => m.Id).OrderByDescending(id => id, StringComparer.Ordinal).ToList();
            int have = cls == 'a' ? 1 : 0;
            for (int j = 0; have < count; j++, have++) field.Add(models[j % models.Count]);
        }
        field.Reverse();
        return field;
    }

    /// <summary>
    /// The raceday test scene: 100 cars racing from the reversed grid at once, the camera on the
    /// quickest at the back. Traces that car every 0.1 s, and counts how often it swung its aim
    /// back towards the line and out again.
    /// </summary>
    static void SceneTrace(float seconds)
    {
        var s = new Session(circuit, line, SceneField().Select(m => (m, 1f)).ToList(), rolling: false, LineFor);
        SessionCar me = s.Cars[s.Cars.Count - 1];
        Watch(s, me);
        int tick = 0, swings = 0, dir = 0, flips = 0;
        float ext = 0f, sinceFlip = 99f;
        bool onLine = true;
        while (s.Time < seconds)
        {
            s.Step();
            Racecraft craft = me.Racer.Craft;
            float g = craft.Target;
            if (dir > 0 && g > ext || dir < 0 && g < ext) ext = g;
            else if (dir >= 0 && g < ext - 2f) { if (dir > 0) swings++; dir = -1; ext = g; }
            else if (dir <= 0 && g > ext + 2f) { if (dir < 0) swings++; dir = 1; ext = g; }
            sinceFlip += Session.Dt;
            bool line = craft.State == Racecraft.Mode.Racing;
            if (line != onLine) { if (sinceFlip < 1f) flips++; sinceFlip = 0f; onLine = line; }
            if (tick++ % 10 != 0) continue;
            SessionCar ahead = s.Cars.Where(o => o != me && o.Distance > me.Distance).OrderBy(o => o.Distance).FirstOrDefault();
            string a = ahead == null ? "" : $"ahead {ahead.Model} {ahead.Distance - me.Distance,5:0.0} m lat {ahead.Car.Lateral,5:0.0} {ahead.Car.Speed * 3.6f,4:0}";
            AiPilot p = me.Racer.Active;
            Console.WriteLine($"{Session.Clock(s.Time)} {me.Car.TrackIndex * circuit.Step,5:0} m {me.Car.Speed * 3.6f,4:0} {craft.State,-11} off {p.Offset,5:0.0} tgt {g,5:0.0} cap {Math.Min(p.SpeedCap, 999f) * 3.6f,4:0} lat {me.Car.Lateral,5:0.0} sw {swings} fl {flips}  {a}");
        }
        Console.WriteLine($"scene: {swings} swings, {flips} dithers in {seconds:0} s");
    }

    static readonly bool QuickestFirst = Environment.GetEnvironmentVariable("START_QUICKEST_FIRST") != null;

    /// <summary>
    /// The end of the formation lap (FinalFormation): the field cut to the leader just short of the last corner, or
    /// to the line when skipped, two by two behind it. It must go green with every car in its place and no contacts,
    /// and a player's car on its limiter, driven flat out on its column, must never get past the car before it in
    /// grid order nor out of its place until it crosses the line.
    /// </summary>
    static void FinalFormationCheck(int cars, int player, bool skipped, bool verbose)
    {
        List<string> order = SceneField();
        var entries = order.Take(cars).Select(m => (m, 1f)).ToList();
        var s = new Session(circuit, line, entries, rolling: true, LineFor, 11) { Player = player };
        Watch(s, s.Cars[Math.Max(player, 0)]);
        float leaderStart = skipped ? circuit.Lap - 3f : RollingStart.FinalStart(circuit);
        s.CutToFinal(leaderStart);
        float worstSlip = 0f, worstAhead = float.NegativeInfinity, worstOver = 0f;
        string worstCar = "";
        int contactsAtGreen = -1;
        float allRacing = -1f;
        SessionCar me = player >= 0 ? s.Cars[player] : null;
        // Off the track before crossing the line: the racing after it is the racing checks' business.
        var offInFormation = new Dictionary<SessionCar, float>();
        while (s.Time < 4f * 60f && allRacing < 0f)
        {
            if (s.GreenTime >= 0f && !s.Cars.Any(c => s.Start.Holds(c.Laps))) allRacing = s.Time;
            bool before = s.GreenTime < 0f;
            s.Step();
            if (me != null && s.Start.Holds(me.Laps))
            {
                if (player > 0) worstAhead = Math.Max(worstAhead, me.Distance - s.Cars[player - 1].Distance);
                worstOver = Math.Max(worstOver, me.Distance - RollingStart.PlaceOf(s.Cars[0].Distance, player));
            }
            foreach (SessionCar c in s.Cars)
                if (c.OffTime >= 0f && s.Start.Holds(c.Laps) && !offInFormation.ContainsKey(c)) offInFormation[c] = c.OffWhere;
            if (before && s.GreenTime >= 0f)
            {
                contactsAtGreen = s.Contacts.Count;
                float leader = s.Cars[0].Distance;
                foreach (SessionCar c in s.Cars)
                {
                    float slip = Math.Abs(c.Distance - RollingStart.PlaceOf(leader, c.Slot));
                    if (slip > worstSlip) { worstSlip = slip; worstCar = s.Name(c); }
                }
            }
        }
        var problems = new List<string>();
        if (s.GreenTime < 0f) problems.Add("never went green");
        if (allRacing < 0f) problems.Add("not every car crossed the line");
        int formationContacts = contactsAtGreen < 0 ? s.Contacts.Count : contactsAtGreen;
        if (formationContacts > 0) problems.Add($"{formationContacts} contacts before the green, first {Describe(s.Contacts[0])}");
        if (worstSlip > 30f) problems.Add($"{worstCar} was {worstSlip:0} m out of place at the green");
        if (player > 0 && worstAhead > 0f) problems.Add($"the player got {worstAhead:0.0} m past the car before it before the line");
        if (me != null && worstOver > 30f) problems.Add($"the player got {worstOver:0.0} m ahead of its place before the line");
        foreach (KeyValuePair<SessionCar, float> off in offInFormation) problems.Add($"{s.Name(off.Key)} left the track at {off.Value:0} m before crossing the line");
        string who = player < 0 ? "no player" : $"player in slot {player}, at most {worstAhead:0.0} m on the car before, {worstOver:0.0} m ahead of its place";
        Report($"{(skipped ? "skipped " : "")}final formation ({cars} cars, {who}: green at {Session.Clock(Math.Max(s.GreenTime, 0f))}, all racing at {Session.Clock(Math.Max(allRacing, 0f))}, worst place {worstSlip:0.0} m out)", problems);
        if (verbose) foreach (Contact c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
    }

    static void RollingStartCheck(int cars, bool verbose, bool glide = false)
    {
        List<string> order = SceneField();
        // As the game's scene: quickest first, the player's car (the quickest) at the back.
        if (QuickestFirst) { order.Reverse(); order.Add(order[0]); order.RemoveAt(0); }
        var entries = order.Take(cars).Select(m => (m, 1f)).ToList();
        var s = new Session(circuit, line, entries, rolling: true, LineFor, int.TryParse(Environment.GetEnvironmentVariable("START_SEED"), out int startSeed) ? startSeed : 11);
        if (glide) Watch(s, s.Cars[s.Cars.Count - 1]);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float worstSlip = 0f;
        string worstCar = "";
        int contactsAtGreen = -1;
        float allRacing = -1f;
        SessionCar back = s.Cars[s.Cars.Count - 1];
        float clearAt = -1f;
        float runOn = float.TryParse(Environment.GetEnvironmentVariable("START_RUN_ON"), out float ro) ? ro : 0f;
        while (s.Time < 12f * 60f && (allRacing < 0f || s.Time < allRacing + runOn))
        {
            if (allRacing < 0f && s.GreenTime >= 0f && !s.Cars.Any(c => s.Start.Holds(c.Laps))) allRacing = s.Time;
            bool before = s.GreenTime < 0f;
            s.Step();
            if (clearAt < 0f && s.GreenTime >= 0f && s.Cars.Where(c => c.Model[0] == 'e' || c.Model[0] == 'f').All(c => c.Distance < back.Distance)) clearAt = s.Time;
            if (QuickestFirst && clearAt >= 0f && runOn > 0f && s.Time > clearAt + 1f) break;
            if (before && s.GreenTime >= 0f)
            {
                contactsAtGreen = s.Contacts.Count;
                float leader = s.Cars[0].Distance;
                foreach (SessionCar c in s.Cars)
                {
                    float slip = Math.Abs(c.Distance - (leader - (c.Slot / 2) * RollingStart.RowGap));
                    if (slip > worstSlip) { worstSlip = slip; worstCar = s.Name(c); }
                }
            }
            if (verbose && (int)(s.Time / Session.Dt) % 1000 == 0)
            {
                SessionCar a = s.Cars[0], z = s.Cars[s.Cars.Count - 1];
                Console.WriteLine($"  {Session.Clock(s.Time)} {s.Start.State} leader {a.Distance:0} m {a.Car.Speed * 3.6f:0} km/h, last {a.Distance - z.Distance:0} m back {z.Car.Speed * 3.6f:0} km/h, contacts {s.Contacts.Count}");
            }
        }
        var problems = new List<string>();
        if (s.GreenTime < 0f) problems.Add("never went green");
        if (!s.PaceCarGone) problems.Add("the pace car never left the track");
        int formationContacts = contactsAtGreen < 0 ? s.Contacts.Count : contactsAtGreen;
        if (formationContacts > 0) problems.Add($"{formationContacts} contacts before the green, first {Describe(s.Contacts[0])}");
        if (worstSlip > 200f) problems.Add($"{worstCar} was {worstSlip:0} m out of place at the green");
        var hard = s.Contacts.Skip(Math.Max(contactsAtGreen, 0)).Where(c => c.Speed > HardHit).ToList();
        if (Environment.GetEnvironmentVariable("AI_LIST") != null) foreach (var c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
        if (hard.Count > 0) problems.Add($"{hard.Count} hard hits after the green, worst {hard.Max(c => c.Speed):0.0} m/s: {Describe(hard[0])}");
        var rear = s.Contacts.Skip(Math.Max(contactsAtGreen, 0)).Where(c => c.RearEnd && c.Speed > RearHit).ToList();
        if (rear.Count > 0) problems.Add($"{rear.Count} rear-end hits over {RearHit:0} m/s after the green, worst {rear.Max(c => c.Speed):0.0} m/s: {Describe(rear[0])}");
        foreach (SessionCar c in s.Cars)
        {
            if (c.Recoveries > 0) problems.Add($"{s.Name(c)} got stuck {c.Recoveries} times");
            if (c.OffTime >= 0f) problems.Add($"{s.Name(c)} left the track at {Session.Clock(c.OffTime)} at {c.OffWhere:0} m");
        }
        float lastBack = s.Cars[0].Distance - s.Cars.Min(c => c.Distance);
        Report($"{(glide ? "gliding rolling start" : "rolling start")} ({cars} cars{(glide ? $", {s.MostOnPhysics} on physics at most, {s.Cars.Sum(c => c.Handovers)} switches, {s.GhostOverlaps} overlaps while gliding" : "")}: green at {Session.Clock(Math.Max(s.GreenTime, 0f))}, worst place {worstSlip:0} m out, {s.Contacts.Count - Math.Max(contactsAtGreen, 0)} contacts from the green to {Session.Clock(Math.Max(s.Time, 0f))}, {s.Contacts.Count(c => c.Speed > 1f)} over 1 m/s, {(clearAt >= 0f ? $"{back.Model} at the back clear of E and F {clearAt - s.GreenTime:0} s after the green" : $"{back.Model} at the back not clear of E and F")}, {s.CrawlSteps * Session.Dt:0} s crawling on track, {s.CrawlBrakeSteps * Session.Dt:0} s of it braking for a car ahead, field {lastBack:0} m long, {watch.Elapsed.TotalSeconds:0} s to run)", problems);
        if (verbose) foreach (Contact c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
    }

    /// <summary>The raceday scene's field: quickest first, with the Cheonma moved from the front to the back.</summary>
    static List<string> QueueField()
    {
        List<string> field = SceneField();
        field.Reverse();
        field.Add(field[0]);
        field.RemoveAt(0);
        return field;
    }

    static void PitLaneStartCheck(bool verbose)
    {
        var s = new Session(circuit, line, QueueField().Select(m => (m, 1f)).ToList(), rolling: false, LineFor, pitLane: true);
        PitLaneStart start = s.PitStart;
        SessionCar me = s.Cars[s.Cars.Count - 1];
        Watch(s, me);
        Func<SessionCar, bool> near = s.Physics;
        // As the scene: everything glides through the fast forward, then PhysicsRange's rule.
        s.Physics = c => start.Time >= start.FastForwardTo && near(c);
        if (Environment.GetEnvironmentVariable("PIT_PHYSICS") != null) s.Physics = null;   // every car on physics throughout
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int lastLog = -1;
        while (s.Time < 25f * 60f && !(s.PitsOpen && s.Time > me.Out + 30f))
        {
            s.Step();
            int minute = (int)(s.Time / 60f);
            if (verbose && minute != lastLog)
            {
                lastLog = minute;
                int queued = s.Cars.Count(c => c.Moved < 0f), leaving = s.Cars.Count(c => c.Moved >= 0f && c.Out < 0f);
                Console.WriteLine($"  {Session.Clock(s.Time)} {queued} queued, {leaving} on the way out, {s.Cars.Count(c => c.Out >= 0f)} racing, {s.Contacts.Count} contacts, {s.Cars.Sum(c => c.Stops.Count)} stops");
            }
        }
        var problems = new List<string>();
        float limit = new PitLane(circuit).LimitSpeed;
        foreach (SessionCar c in s.Cars)
        {
            if (c.Moved >= 0f && c.Moved < PitLaneStart.LeavesAt(c.Slot)) problems.Add($"{s.Name(c)} crossed the pit exit line at {Session.Clock(c.Moved)}, before its turn at {Session.Clock(PitLaneStart.LeavesAt(c.Slot))}");
            if (c.Out < 0f) problems.Add($"{s.Name(c)} never got out of the pit lane");
            if (c.LimitZoneTop > limit + 0.3f) problems.Add($"{s.Name(c)} did {c.LimitZoneTop * 3.6f:0.0} km/h in the limiter zone");
            if (c.RanDry) problems.Add($"{s.Name(c)} ran out of fuel");
            if (c.Recoveries > 0) problems.Add($"{s.Name(c)} got stuck {c.Recoveries} times");
            // Leaving the track once racing is the racing's to check, not the start's.
            if (c.OffTime >= 0f && (c.Out < 0f || c.OffTime < c.Out)) problems.Add($"{s.Name(c)} left the track at {Session.Clock(c.OffTime)} at {c.OffWhere:0} m on its way out");
        }
        float worstGap = s.Cars.Max(c => c.GapAtTurn);
        if (worstGap > 6f) problems.Add($"{s.Name(s.Cars.First(c => c.GapAtTurn == worstGap))} was {worstGap:0.0} m back from the pit exit line at its turn");
        int offRacing = s.Cars.Count(c => c.OffTime >= 0f && c.Out >= 0f && c.OffTime >= c.Out);
        // Contacts between racing cars are the racing's to check; the start's are those with a car on its way out.
        var outContacts = s.Contacts.Where(c => c.A.Contains("(leaving)") || c.B.Contains("(leaving)")).ToList();
        var lane = outContacts.Where(c => c.A.Contains("(pits)") || c.B.Contains("(pits)")).ToList();
        if (s.LaneOverlaps > 0) problems.Add($"{s.LaneOverlaps} overlaps while gliding in the pit lane: woken in physics they would crash");
        if (lane.Count > 0) problems.Add($"{lane.Count} contacts in the pit lane: {Describe(lane[0])}");
        var hard = outContacts.Where(c => c.Speed > HardHit).ToList();
        if (hard.Count > 0) problems.Add($"{hard.Count} hard hits with a car on its way out, worst {hard.Max(c => c.Speed):0.0} m/s: {Describe(hard[0])}");
        int early = s.Cars.Sum(c => c.Stops.Count);
        if (!s.PitsOpen) problems.Add("pit stops never opened");
        var outTimes = s.Cars.Select(c => c.Out - PitLaneStart.LeavesAt(c.Slot)).Where(t => t > 0f).ToList();
        Report($"pit lane start ({s.Cars.Count} cars: middles {s.Cars.Min(c => c.GapAtTurn):0.0} to {worstGap:0.0} m from the line at their turn, fast forward to {Session.Clock(start.FastForwardTo)}, last car out at {Session.Clock(me.Out)}, {(outTimes.Count > 0 ? outTimes.Min() : 0f):0.0} to {(outTimes.Count > 0 ? outTimes.Max() : 0f):0.0} s from going to racing, {outContacts.Count} contacts with a car on its way out, {s.Contacts.Count - outContacts.Count} between racing cars ({s.Contacts.Count(c => c.Speed > 1f)} over 1 m/s in all), {s.MostOnPhysics} on physics at most, {s.GhostOverlaps} overlaps while gliding ({s.StartOverlaps} with a car on its way out, {s.LaneOverlaps} of them in the pit lane), {early} pit stops by {Session.Clock(s.Time)}, {offRacing} cars off the track while racing, {watch.Elapsed.TotalSeconds:0} s to run)", problems);
        if (!verbose) return;
        foreach (SessionCar c in s.Cars) Console.WriteLine($"  {s.Name(c)} went {Session.Clock(PitLaneStart.LeavesAt(c.Slot))}, over the line {Session.Clock(c.Moved)}, racing {Session.Clock(c.Out)} ({c.Out - PitLaneStart.LeavesAt(c.Slot):0.0} s)");
        foreach (Contact c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
    }

    /// <summary>PhysicsRange's rule with the camera on one car: the nearest 24 within 300 m run physics, kept to 375 m.</summary>
    static void Watch(Session s, SessionCar watched)
    {
        s.Apart = c =>
        {
            float dx = c.Car.Position.x - watched.Car.Position.x, dz = c.Car.Position.z - watched.Car.Position.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        };
        s.Physics = c => s.Apart(c) < (c.Racer.Active.Gliding ? 300f : 375f);
    }

    static string Describe(Contact c) => $"{Session.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s{(c.RearEnd ? " REAR" : "")}";

    static void SuppliesCheck(bool verbose)
    {
        string[] models = { "a-29", "b-22", "c-19", "d-13", "e-09", "f-01" };
        var problems = new List<string>();
        var lines = new List<string>();
        foreach (string m in models)
        {
            var s = new Session(circuit, line, new List<(string, float)> { (m, 1f) }, rolling: false, LineFor);
            SessionCar c = s.Cars[0];
            while (c.LapUse.Count < 3 && s.Time < 20f * 60f) s.Step();
            Consumables sup = c.Racer.Supplies;
            var use = c.LapUse.Skip(1).ToList();   // the first counted lap still had the start in it
            if (use.Count == 0) { problems.Add($"{m} did no full laps"); continue; }
            float fuel = use.Average(u => u.Fuel), wear = use.Average(u => u.Wear);
            float lap = c.LapTimes.Count > 0 ? c.LapTimes.Last() : 0f;
            lines.Add($"  {m}: lap {Time(lap)}, {fuel:0.00} L a lap (maths {sup.FuelPerLap:0.00}), tyres {wear * 100f:0.0}% a lap (maths {sup.WearPerLap * 100f:0.0}%): a tank lasts {sup.Capacity / fuel:0.0} laps, {sup.Capacity / fuel * lap / 60f:0} min");
            if (fuel < 0.6f * sup.FuelPerLap || fuel > 1.4f * sup.FuelPerLap) problems.Add($"{m} used {fuel:0.00} L a lap, the maths says {sup.FuelPerLap:0.00}");
            if (wear < 0.6f * sup.WearPerLap || wear > 1.4f * sup.WearPerLap) problems.Add($"{m} wore {wear * 100f:0.0}% a lap, the maths says {sup.WearPerLap * 100f:0.0}%");
        }
        Report("supplies", problems);
        if (verbose) foreach (string l in lines) Console.WriteLine(l);
    }

    static void PitStopsCheck(string name, Func<int, bool> shortOfFuel, bool verbose, bool glide = false)
    {
        string[] models = { "a-29", "a-27", "a-26", "b-24", "b-22", "b-21", "c-19", "c-18", "c-16", "d-15", "d-13", "e-09", "e-07", "f-06", "f-03", "f-01" };
        var entries = models.Select((m, i) => (m, shortOfFuel(i) ? (float.TryParse(Environment.GetEnvironmentVariable("AI_FUEL"), out float fu) ? fu : 0.13f) : 1f)).ToList();
        var s = new Session(circuit, line, entries, rolling: false, LineFor);
        if (glide) Watch(s, s.Cars[s.Cars.Count - 1]);
        var due = s.Cars.Where(c => shortOfFuel(c.Id)).ToList();
        // Until every car short of fuel has stopped, rejoined and done a lap after it.
        var lapsAtRejoin = new Dictionary<int, int>();
        while (s.Time < 20f * 60f)
        {
            s.Step();
            foreach (SessionCar c in due)
                if (c.Rejoins > 0 && !lapsAtRejoin.ContainsKey(c.Id)) lapsAtRejoin[c.Id] = c.Laps;
            if (due.All(c => lapsAtRejoin.TryGetValue(c.Id, out int l) && c.Laps > l)) break;
        }
        var problems = new List<string>();
        float limit = new PitLane(circuit).LimitSpeed;
        foreach (SessionCar c in s.Cars)
        {
            bool should = shortOfFuel(c.Id);
            if (should && c.Stops.Count == 0) problems.Add($"{s.Name(c)} never pitted");
            if (!should && c.Stops.Count > 0) problems.Add($"{s.Name(c)} pitted on a full tank");
            if (should && c.Rejoins == 0 && c.Stops.Count > 0) problems.Add($"{s.Name(c)} never rejoined");
            if (c.WorstBoxMiss > 1f) problems.Add($"{s.Name(c)} stopped {c.WorstBoxMiss:0.00} m from its box");
            if (c.LimitZoneTop > limit + 0.3f) problems.Add($"{s.Name(c)} did {c.LimitZoneTop * 3.6f:0.0} km/h in the limiter zone");
            if (c.RanDry) problems.Add($"{s.Name(c)} ran out of fuel");
            if (c.Recoveries > 0) problems.Add($"{s.Name(c)} got stuck {c.Recoveries} times");
            if (c.OffTime >= 0f) problems.Add($"{s.Name(c)} left the track at {Session.Clock(c.OffTime)} at {c.OffWhere:0} m");
            if (c.LeftWith >= 0f && c.LeftWith < 0.99f * c.Racer.Supplies.Capacity) problems.Add($"{s.Name(c)} left its stop with {c.LeftWith:0.0} of {c.Racer.Supplies.Capacity:0} L");
        }
        var pitContacts = s.Contacts.Where(c => c.A.Contains("(pits)") || c.B.Contains("(pits)")).ToList();
        if (pitContacts.Count > 0) problems.Add($"{pitContacts.Count} contacts with a pitting car: {Describe(pitContacts[0])}");
        if (!due.All(c => lapsAtRejoin.ContainsKey(c.Id))) problems.Add("ran out of time before every stop was done");
        float worstMiss = s.Cars.Max(c => c.WorstBoxMiss);
        Report($"{name} ({due.Count} of {s.Cars.Count} cars pit,{(glide ? $" {s.Cars.Sum(c => c.GlidingStops)} called in while gliding," : "")} {s.MostInLane} in the lane at once, worst box miss {worstMiss:0.00} m, top speed in the limiter zone {s.Cars.Max(c => c.LimitZoneTop) * 3.6f:0.0} km/h, {s.Contacts.Count} contacts in all ({s.Contacts.Count(c => c.A.Contains("(rejoining") || c.B.Contains("(rejoining"))} with a rejoining car), {Session.Clock(s.Time)} run)", problems);
        if (!verbose) return;
        foreach (SessionCar c in s.Cars)
            foreach (string stop in c.Stops) Console.WriteLine($"  {s.Name(c)} {stop}");
        foreach (Contact c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
    }

    static void RaceRunning(bool verbose)
    {
        SuppliesCheck(verbose);
        PitStopsCheck("pit stops", i => i % 2 == 1, verbose);
        PitStopsCheck("crowded pits", i => true, verbose);
        RollingStartCheck(100, verbose);
    }
}
