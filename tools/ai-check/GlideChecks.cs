// Checks for gliding: far from the camera an AI car has no physics and moves along its path (AiPilot.Glide).
//
//   glide pace       gliding alone, every car model's flying lap is within 1.5% of the same driver's
//                    lap in the stand-in physics, so a car keeps its pace whichever way it is run
//   glide mistakes   an overcooked corner costs a gliding car between half and twice the time it costs
//                    a driven one
//   handovers        the 16-car pack races 4 laps with physics only within 300 m of the watched car,
//                    and the camera moves to another car every 40 s, as V does. Cars switch both ways
//                    many times without leaving the track, getting stuck or touching, and after waking
//                    no car strays more than 2 m from its path
//   fast forward     100 cars glide for 5 minutes, as at 16x: those within 600 m of the camera step
//                    every 0.1 s and the rest every 0.2 s. Every car laps, pace is within 1.5% of
//                    stepping every 0.01 s, and no car in sight runs through another faster than
//                    8 m/s (the same racecraft gives light touches in a pack this dense; harder means
//                    the glide itself is wrong). Prints what racecraft costs per second of 16x.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;
using UnityEngine;

static partial class Check
{
    static readonly Dictionary<string, float> physicsLaps = new Dictionary<string, float>();
    static float physicsMistakeCost = float.NaN;

    /// <summary>One driver alone on track, gliding all the way from the grid.</summary>
    static Racer GlideAlone(string model, float pace, float consistency, float safety, int seed, int laps)
    {
        GridSlot pole = circuit.Data.grid.slots[0];
        var entry = new Entry { Model = model, Start = pole.d - 2.6f, Lateral = pole.lateral, Pace = pace, Consistency = consistency, Safety = safety, Seed = seed };
        var race = new Race(circuit, new[] { entry }, LineFor) { Physics = _ => false };
        while (race.Racers[0].LapTimes.Count < laps && race.Time < 60f * 30f) race.Step();
        return race.Racers[0];
    }

    static void GlidePace() => GlidePace(CarModels.All.Select(m => m.Id).ToArray());

    static void GlidePace(string[] ids)
    {
        var problems = new List<string>();
        var rows = new List<string>();
        float worst = 0f;
        foreach (string id in ids)
        {
            Racer r = GlideAlone(id, 100f, 100f, 100f, 1, 2);
            float glide = r.LapTimes.Count > 1 ? r.LapTimes[1] : float.NaN;
            float driven = physicsLaps.TryGetValue(id, out float d) ? d : float.NaN;
            float gap = (glide - driven) / driven;
            worst = Math.Max(worst, Math.Abs(gap));
            rows.Add($"    {id,-6} driven {Time(driven)}  glided {Time(glide)}  {gap * 100f,5:+0.0;-0.0}%");
            if (float.IsNaN(gap) || Math.Abs(gap) > 0.015f) problems.Add($"{id} glides {gap * 100f:+0.0;-0.0}% off its driven lap");
        }
        Report($"glide pace (worst {worst * 100f:0.0}% off the driven lap)", problems);
        if (problems.Count > 0 || Environment.GetEnvironmentVariable("AI_GLIDE_ROWS") != null) foreach (string row in rows) Console.WriteLine(row);
    }

    static void GlideMistakes()
    {
        Racer risky = GlideAlone("a-29", 90f, 100f, 0f, 7, 6);
        Racer careful = GlideAlone("a-29", 90f, 100f, 100f, 7, 6);
        var problems = new List<string>();
        int mistakes = risky.Pilot.Mistakes;
        float cost = mistakes > 0 ? (risky.LapTimes.Sum() - careful.LapTimes.Sum()) / mistakes : float.NaN;
        if (mistakes == 0) problems.Add("no corner was overcooked in 6 laps");
        else if (!(cost > 0.5f * physicsMistakeCost && cost < 2f * physicsMistakeCost))
            problems.Add($"a mistake costs {cost:0.0} s gliding against {physicsMistakeCost:0.0} s driven");
        Report($"glide mistakes ({mistakes} overcooked corners, {cost:0.0} s each gliding, {physicsMistakeCost:0.0} s driven)", problems);
    }

    static void Handovers()
    {
        var race = new Race(circuit, Pack(reversed: false), LineFor);
        int watched = 0;
        float lastSwitch = 0f;
        race.Physics = r =>
        {
            if (race.Time - lastSwitch > 40f) { watched = (watched + 5) % race.Racers.Count; lastSwitch = race.Time; }
            Racer w = race.Racers[watched];
            float dx = r.Car.X - w.Car.X, dz = r.Car.Z - w.Car.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            return d < (r.Pilot.Gliding ? 300f : 375f);
        };
        while (race.Racers.Max(c => c.Laps) < 4 && race.Time < 60f * 20f) race.Step();

        var problems = new List<string>();
        int handovers = race.Racers.Sum(r => r.Handovers);
        foreach (Racer r in race.Racers)
        {
            if (r.Recoveries > 0) problems.Add($"{race.Name(r)} got stuck {r.Recoveries} times");
            if (r.OffSteps > 0) problems.Add($"{race.Name(r)} left the track for {r.OffSteps * Race.Dt:0.0} s, first at {Race.Clock(r.OffTime)} at {r.OffWhere:0} m");
            if (r.WorstAfterWake > 2f) problems.Add($"{race.Name(r)} strayed {r.WorstAfterWake:0.0} m from its path after waking");
        }
        int hard = race.Contacts.Count(c => c.Speed > HardHit) + race.GhostOverlaps.Count(c => c.Speed > HardHit);
        if (hard > 0) problems.Add($"{hard} contacts harder than 3 m/s");
        if (handovers < 40) problems.Add($"only {handovers} handovers");
        float worstStray = race.Racers.Max(r => r.WorstAfterWake);
        Report($"handovers ({handovers} switches in 4 laps, worst {worstStray:0.0} m off the path after waking, {race.Contacts.Count} contacts, {race.GhostOverlaps.Count} overlaps while gliding, {race.PassCount} changes of place)", problems);
        foreach (Contact c in race.Contacts.Concat(race.GhostOverlaps).Where(c => c.Speed > HardHit).Take(8))
            Console.WriteLine($"      {Race.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s");
    }

    /// <summary>100 cars, every class, in a queue behind the line: the whole field gliding.</summary>
    static List<Entry> Hundred()
    {
        string[] models = CarModels.All.Select(m => m.Id).Where(id => !id.StartsWith("f1")).OrderByDescending(id => id).ToArray();
        var rng = new System.Random(5);
        var entries = new List<Entry>();
        for (int i = 0; i < 100; i++)
            entries.Add(new Entry
            {
                Model = models[i * models.Length / 100],
                Start = circuit.Lap - 10f - i * 18f,
                Lateral = 0f,
                Speed = 30f,
                Pace = 70f + 30f * (float)rng.NextDouble(),
                Consistency = 60f + 40f * (float)rng.NextDouble(),
                Safety = 80f + 20f * (float)rng.NextDouble(),
                Seed = i + 1,
            });
        return entries;
    }

    /// <summary>
    /// The whole field gliding as at 16x: cars within 600 m of the camera (on car 50) step every
    /// 0.1 s, the rest every 0.2 s, as PhysicsRange sets them. Overlaps count as in sight within 500 m,
    /// so a car coming into view has already raced at the finer step for a while.
    /// </summary>
    static void FastForward()
    {
        const float Length = 300f;
        Race Glide(bool fine, out double ms)
        {
            var race = new Race(circuit, Hundred(), LineFor) { Physics = _ => false };
            race.Camera = race.Racers[50];
            if (!fine) race.GlideStepFor = r => Apart(r, race.Camera) < 600f ? 0.1f : 0.2f;
            while (race.Time < Length) race.Step();
            ms = race.ThinkTicks / (double)Stopwatch.Frequency * 1000.0;
            return race;
        }
        Race fast = Glide(false, out double fastMs);
        Race fine = Glide(true, out _);

        var problems = new List<string>();
        List<Contact> seen = fast.GhostOverlaps.Where(c => c.Seen).ToList();
        int hard = seen.Count(c => c.Speed > 8f);
        if (hard > 0) problems.Add($"{hard} cars in sight ran into another faster than 8 m/s, worst {seen.Max(c => c.Speed):0.0}");
        foreach (Racer r in fast.Racers) if (r.LapTimes.Count == 0) problems.Add($"{fast.Name(r)} did not complete a lap");
        float Distance(Race x) => x.Racers.Sum(r => r.Distance);
        float drift = (Distance(fast) - Distance(fine)) / Distance(fine);
        if (Math.Abs(drift) > 0.015f) problems.Add($"stepping as at 16x covers {drift * 100f:+0.0;-0.0}% more ground than every 0.01 s");
        double perSecond = fastMs / Length * 16.0;
        Report($"fast forward (100 cars gliding: {seen.Count} overlaps in sight, worst {(seen.Count > 0 ? seen.Max(c => c.Speed) : 0f):0.0} m/s, {fast.GhostOverlaps.Count - seen.Count} out of sight, {drift * 100f:+0.0;-0.0}% ground against fine steps, racecraft {perSecond:0} ms per second of 16x)", problems);
        foreach (Contact c in seen.Where(c => c.Speed > 8f).Take(8))
            Console.WriteLine($"      {Race.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s");
    }

    static float Apart(Racer a, Racer b) => (float)Math.Sqrt((a.Car.X - b.Car.X) * (a.Car.X - b.Car.X) + (a.Car.Z - b.Car.Z) * (a.Car.Z - b.Car.Z));
}
