// "situations": runs the set-ups of the AI racecraft spec (Situations.cs, shared with the raceday debug
// menu) one by one. With an id ("situations T2") it prints the cars every half second: where they are,
// across the track, speed and what racecraft is doing. Without one it prints a line per set-up.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;

static partial class Check
{
    /// <summary>Builds the race for a set-up, with every car where and as fast as the set-up says.</summary>
    static Race SituationRace(Situation s)
    {
        var entries = new List<Entry>();
        RacingLine first = LineFor(s.Cars[0].Model);
        foreach (SituationCar c in s.Cars)
        {
            CarSpec spec = CarSpec.For(c.Model);
            RacingLine own = LineFor(c.Model);
            c.Spot(s, circuit, first, own, new SpeedProfile(own, spec, AiPilot.LimitFor(c.Pace)), out int k, out float lateral, out float speed);
            entries.Add(new Entry
            {
                Model = c.Model, Start = k * circuit.Step, Lateral = lateral, Speed = Math.Max(speed, 0.01f),
                Pace = c.Pace, Consistency = c.Consistency, Safety = c.Safety, Seed = c.Seed,
            });
        }
        var race = new Race(circuit, entries, LineFor);
        for (int i = 0; i < s.Cars.Length; i++)
        {
            SituationCar c = s.Cars[i];
            Racer r = race.Racers[i];
            if (c.Speed == 0f) r.Car.Launch(0f);
            if (c.Turned != 0f) r.Car.Heading += c.Turned * (float)Math.PI / 180f;
            r.Laps = -c.LapsDown;   // not the grid's guess from where on the lap the car starts
            if (c.Spun) r.Rejoin.Spin();
            if (c.Wear > 0f)
            {
                r.Car.Supplies = Consumables.For(r.Car.Spec, r.Pilot.Profile, 60f);
                r.Car.Supplies.Wear = c.Wear;
            }
        }
        return race;
    }

    static void RunSituations(string only)
    {
        if (only == "speeds")
        {
            // Each model's racing speed along the back straight, km/h, to place the set-ups by.
            foreach (string m in new[] { "a-29", "a-28", "b-25", "b-24", "b-23", "b-22", "b-21", "c-19", "c-18", "d-15", "e-11", "f-01", "f-05" })
            {
                var p = new SpeedProfile(LineFor(m), CarSpec.For(m), AiPilot.LimitFor(90f));
                Console.WriteLine($"{m} " + string.Join(" ", new[] { 2960f, 3200f, 3500f, 3700f, 3800f, 3850f, 3900f, 3950f, 3990f }.Select(d => $"{d:0}:{p.Speed[circuit.IndexOf(d)] * 3.6f:0}")) + $"  lap {p.LapTime:0.0}");
            }
            return;
        }
        if (only != null && only.StartsWith("race"))
        {
            // A Race A field with one car traced while it runs well under its line's speed: "race:51:120" traces car 50 (the A car) for 120 s.
            string[] parts = only.Split(':');
            int id = parts.Length > 1 ? int.Parse(parts[1]) : 50;
            float until = parts.Length > 2 ? float.Parse(parts[2]) : 120f;
            Race r = new Race(circuit, ClassField(11), LineFor);
            int tick = 0;
            while (r.Time < until)
            {
                r.Step();
                if (tick++ % 25 != 0) continue;
                Racer x = r.Racers[id];
                int k = Math.Max(x.Car.TrackIndex, 0);
                float free = x.Pilot.Profile.Speed[k];
                if (x.Car.Speed > 0.92f * free || r.Time < 20f) continue;
                PathChooser pc = x.Craft.Chooser;
                Racer ahead = r.Racers.Where(o => o != x && Gap(o.Distance - x.Distance) > 0f).OrderBy(o => Gap(o.Distance - x.Distance)).FirstOrDefault();
                string a = ahead == null ? "" : $"ahead {r.Name(ahead)} {Gap(ahead.Distance - x.Distance):0.0} m lat {ahead.Car.Lateral:0.0} {ahead.Car.Speed * 3.6f:0}";
                Console.WriteLine($"{r.Time,6:0.0} {k * circuit.Step,5:0} m {x.Car.Speed * 3.6f:0}/{free * 3.6f:0} lat {x.Car.Lateral,5:0.0} {(x.Craft.Braking ? "BRK" : "   ")} {a} [{pc?.Reason} -> {pc?.Chosen?.Shift:0.0}{(pc?.Chosen?.Delay > 0 ? "d" + pc.Chosen.Delay : "")} {pc?.Chosen?.Pedal} | "
                    + (pc == null ? "" : string.Join(" ", pc.Candidates.Where(c => c.Pedal == PathChooser.Pedal.Go && c.Delay == 0).Select(c => $"{c.Shift:0.#}:{c.Rejected ?? c.Score.ToString("0.00")}"))) + "]");
            }
            return;
        }
        foreach (Situation s in Situations.All)
        {
            if (only != null && !string.Equals(s.Id, only, StringComparison.OrdinalIgnoreCase)) continue;
            if (!s.Ready) { Console.WriteLine($"{s.Name,-48} waits on {s.Needs}"); continue; }
            Race race = SituationRace(s);
            int contacts = 0;
            int tick = 0;
            while (race.Time < s.Seconds)
            {
                if (only != null && tick++ % (Environment.GetEnvironmentVariable("TRACE_EVERY") is string te ? int.Parse(te) : 50) == 0)
                {
                    var line = new System.Text.StringBuilder($"{race.Time,5:0.0}");
                    Racer lead = race.Racers[0];
                    foreach (Racer r in race.Racers)
                    {
                        float gap = r.Distance - lead.Distance;
                        line.Append($" | {r.Entry.Model} {(r == lead ? r.Car.TrackIndex * circuit.Step : gap),6:0.0} lat {r.Car.Lateral,5:0.0} {r.Car.Speed * 3.6f,3:0} {r.Craft.State,-11}{(r.Craft.Braking ? " BRK" : "    ")}{(r.Ghost ? " G" : "  ")}");
                        PathChooser pc = r.Craft.Chooser;
                        if (pc?.Chosen != null && r == race.Racers[Environment.GetEnvironmentVariable("TRACE_CAR") is string tc ? int.Parse(tc) : 0])
                            line.Append($" [{pc.Reason} -> {pc.Chosen.Shift:0.0}{(pc.Chosen.Delay > 0 ? "d" + pc.Chosen.Delay : "")} {pc.Chosen.Pedal} {pc.Chosen.Score:0.00} | " + string.Join(" ", pc.Candidates.Where(x => x.Pedal == PathChooser.Pedal.Go).Select(x => $"{x.Shift:0.#}{(x.Delay > 0 ? "d" + x.Delay : "")}:{x.Rejected ?? x.Score.ToString("0.00")}")) + "]");
                    }
                    Console.WriteLine(line);
                }
                race.Step();
            }
            contacts = race.Contacts.Count;
            Racer me = race.Racers[0];
            string order = string.Join(" ", race.Racers.OrderByDescending(r => r.Distance).Select(r => r.Entry.Model));
            Console.WriteLine($"{s.Name,-48} contacts {contacts,2}  order {order}  first car at {Math.Max(me.Car.TrackIndex, 0) * circuit.Step:0} m");
            foreach (Contact x in race.Contacts) Console.WriteLine($"  contact {x.Time:0.00} s {x.A} {x.B} {x.Speed:0.0} m/s{(x.RearEnd ? " rear-end" : "")} at {x.Where:0} m");
            if (only != null)
            {
                // The suite's verdict on this set-up.
                SetupRun run = RunSetup(s);
                if (s.Id.StartsWith("H")) { var j = Verdict(run); Console.WriteLine($"{(j.Pass ? "pass" : "FAIL")}  {j.Why}"); }
                else { var sc = Mark(run); Console.WriteLine($"score {sc.Score:0}  {sc.Why}"); }
            }
        }
    }
}
