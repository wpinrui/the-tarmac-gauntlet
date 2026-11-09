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

    static void RollingStartCheck(int cars, bool verbose)
    {
        var entries = SceneField().Take(cars).Select(m => (m, 1f)).ToList();
        var s = new Session(circuit, line, entries, rolling: true, LineFor);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float worstSlip = 0f;
        string worstCar = "";
        int contactsAtGreen = -1;
        float allRacing = -1f;
        while (s.Time < 12f * 60f && allRacing < 0f)
        {
            if (allRacing < 0f && s.GreenTime >= 0f && !s.Cars.Any(c => s.Start.Holds(c.Laps))) allRacing = s.Time;
            bool before = s.GreenTime < 0f;
            s.Step();
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
        var hard = s.Contacts.Skip(Math.Max(contactsAtGreen, 0)).Where(c => c.Speed > 3f).ToList();
        if (hard.Count > 0) problems.Add($"{hard.Count} hard hits after the green, worst {hard.Max(c => c.Speed):0.0} m/s: {Describe(hard[0])}");
        foreach (SessionCar c in s.Cars)
        {
            if (c.Recoveries > 0) problems.Add($"{s.Name(c)} got stuck {c.Recoveries} times");
            if (c.OffTime >= 0f) problems.Add($"{s.Name(c)} left the track at {Session.Clock(c.OffTime)} at {c.OffWhere:0} m");
        }
        float lastBack = s.Cars[0].Distance - s.Cars.Min(c => c.Distance);
        Report($"rolling start ({cars} cars: green at {Session.Clock(Math.Max(s.GreenTime, 0f))}, worst place {worstSlip:0} m out, {s.Contacts.Count - Math.Max(contactsAtGreen, 0)} contacts from the green to {Session.Clock(Math.Max(s.Time, 0f))}, {s.Contacts.Count(c => c.Speed > 1f)} over 1 m/s, field {lastBack:0} m long, {watch.Elapsed.TotalSeconds:0} s to run)", problems);
        if (verbose) foreach (Contact c in s.Contacts) Console.WriteLine("  contact " + Describe(c));
    }

    static string Describe(Contact c) => $"{Session.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s";

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

    static void PitStopsCheck(string name, Func<int, bool> shortOfFuel, bool verbose)
    {
        string[] models = { "a-29", "a-27", "a-26", "b-24", "b-22", "b-21", "c-19", "c-18", "c-16", "d-15", "d-13", "e-09", "e-07", "f-06", "f-03", "f-01" };
        var entries = models.Select((m, i) => (m, shortOfFuel(i) ? 0.13f : 1f)).ToList();
        var s = new Session(circuit, line, entries, rolling: false, LineFor);
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
        Report($"{name} ({due.Count} of {s.Cars.Count} cars pit, {s.MostInLane} in the lane at once, worst box miss {worstMiss:0.00} m, top speed in the limiter zone {s.Cars.Max(c => c.LimitZoneTop) * 3.6f:0.0} km/h, {s.Contacts.Count} contacts in all, {Session.Clock(s.Time)} run)", problems);
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
