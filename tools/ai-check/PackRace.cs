// "perf": the AI's cost per step on a 100-car grid start (PERF_GAME=1 as the game runs it, PERF_SECONDS).
// "pack": the full field's crash count, lap one and the rest apart, for tuning the pack down.
//   PACK_FIELD   game (default, the season's ~100 cars) or class (Race A's 51)
//   PACK_SEEDS   default 11 12 13 14
//   PACK_MINUTES default 15
// Per seed and in total: contacts per car-hour, hard ones (over 3 m/s), multi-car incidents (three or
// more cars touching in one chain, each touch within 5 s of the last), pile-ups, offs (a car starting to
// get back into the race) and cars still stuck at the end.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

static partial class Check
{
    sealed class PackResult
    {
        public int Seed;
        public float Lap1Hours, RestHours;
        public int Lap1Contacts, RestContacts, Lap1Hard, RestHard, Lap1Multi, RestMulti, Lap1Offs, RestOffs, Piles, Stuck;
        public List<string> Multi = new List<string>();
    }

    static void PackRace()
    {
        bool game = (Environment.GetEnvironmentVariable("PACK_FIELD") ?? "game") == "game";
        int[] seeds = (Environment.GetEnvironmentVariable("PACK_SEEDS") ?? "11 12 13 14").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        float minutes = float.TryParse(Environment.GetEnvironmentVariable("PACK_MINUTES"), out float m) ? m : 15f;
        var results = new PackResult[seeds.Length];
        Parallel.For(0, seeds.Length, i => results[i] = RunPack(game ? GameField(seeds[i]) : ClassField(seeds[i]), minutes, seeds[i]));
        foreach (PackResult p in results)
        {
            Console.WriteLine($"seed {p.Seed}: lap 1 {Rate(p.Lap1Contacts, p.Lap1Hours)} contacts/car-hour ({p.Lap1Contacts}, {p.Lap1Hard} hard), {p.Lap1Multi} multi, {p.Lap1Offs} offs; "
                + $"rest {Rate(p.RestContacts, p.RestHours)} ({p.RestContacts}, {p.RestHard} hard), {p.RestMulti} multi, {p.RestOffs} offs; {p.Piles} pile-ups, {p.Stuck} stuck");
            if (Environment.GetEnvironmentVariable("PACK_LOG") == "1") foreach (string s in p.Multi) Console.WriteLine("  " + s);
        }
        float h1 = results.Sum(p => p.Lap1Hours), h2 = results.Sum(p => p.RestHours);
        Console.WriteLine($"TOTAL lap1 {Rate(results.Sum(p => p.Lap1Contacts), h1)} c/h, hard {Rate(results.Sum(p => p.Lap1Hard), h1)}, multi {results.Sum(p => p.Lap1Multi)}, offs {results.Sum(p => p.Lap1Offs)}"
            + $" | rest {Rate(results.Sum(p => p.RestContacts), h2)} c/h, hard {Rate(results.Sum(p => p.RestHard), h2)}, multi {results.Sum(p => p.RestMulti)}, offs {results.Sum(p => p.RestOffs)}"
            + $" | piles {results.Sum(p => p.Piles)}, stuck {results.Sum(p => p.Stuck)}");
    }

    static string Rate(int n, float hours) => (n / Math.Max(hours, 1e-3f)).ToString("0.0");

    static PackResult RunPack(List<Entry> field, float minutes, int seed)
    {
        var p = new PackResult { Seed = seed };
        var piles = new PileWatch($"seed {seed} ");
        var busy = new Dictionary<Racer, bool>();
        var byName = new Dictionary<string, Racer>();
        int seen = 0;
        var lap1 = new List<bool>();
        Race race = RunRace(field, r =>
        {
            if (byName.Count == 0) foreach (Racer x in r.Racers) byName[r.Name(x)] = x;
            piles.Step(r);
            foreach (Racer x in r.Racers)
            {
                bool b = x.Rejoin.Busy;
                if (b && !(busy.TryGetValue(x, out bool was) && was)) { if (x.Laps == 0) p.Lap1Offs++; else p.RestOffs++; }
                busy[x] = b;
                if (x.Laps == 0) p.Lap1Hours += Race.Dt / 3600f; else p.RestHours += Race.Dt / 3600f;
            }
            for (; seen < r.Contacts.Count; seen++)
            {
                Contact c = r.Contacts[seen];
                lap1.Add(byName[c.A].Laps == 0 || byName[c.B].Laps == 0);
            }
            return false;
        }, minutes * 60f);
        for (int i = 0; i < race.Contacts.Count; i++)
        {
            bool hard = race.Contacts[i].Speed > 3f;
            if (lap1[i]) { p.Lap1Contacts++; if (hard) p.Lap1Hard++; }
            else { p.RestContacts++; if (hard) p.RestHard++; }
        }
        // Chains: touches sharing a car, each within 5 s of the chain's last.
        var cs = race.Contacts;
        var used = new bool[cs.Count];
        for (int i = 0; i < cs.Count; i++)
        {
            if (used[i]) continue;
            var cars = new HashSet<string> { cs[i].A, cs[i].B };
            float last = cs[i].Time;
            used[i] = true;
            for (int j = i + 1; j < cs.Count && cs[j].Time < last + 5f; j++)
            {
                if (used[j] || !(cars.Contains(cs[j].A) || cars.Contains(cs[j].B))) continue;
                used[j] = true; cars.Add(cs[j].A); cars.Add(cs[j].B); last = cs[j].Time;
            }
            if (cars.Count < 3) continue;
            if (lap1[i]) p.Lap1Multi++; else p.RestMulti++;
            p.Multi.Add($"{cars.Count} cars at {cs[i].Where:0} m at {Race.Clock(cs[i].Time)}{(lap1[i] ? " (lap 1)" : "")}: {string.Join(" ", cars)}");
        }
        p.Piles = piles.PileUps.Count;
        p.Stuck = race.Racers.Count(x => x.StuckTime > 10f || x.Rejoin.Busy && x.RejoinTime > 30f);
        return p;
    }
}

static partial class Check
{
    /// <summary>"perf": the AI's cost on a full-field grid start, ms. PERF_GAME runs it as the game does.</summary>
    static void PerfStart()
    {
        float seconds = float.TryParse(Environment.GetEnvironmentVariable("PERF_SECONDS"), out float s) ? s : 20f;
        var load = System.Diagnostics.Stopwatch.StartNew();
        var race = new Race(circuit, GameField(11), LineFor);
        Console.WriteLine($"field built in {load.ElapsedMilliseconds} ms");
        // As the game runs it: the 24 cars nearest the camera within 300 m on physics, the rest gliding,
        // every 0.05 s within 600 m of the camera and every 0.2 s past that. The camera is on the A car at the back.
        if (Environment.GetEnvironmentVariable("PERF_GAME") != null)
        {
            Racer cam = race.Racers[race.Racers.Count - 1];
            race.Camera = cam;
            var near = new HashSet<Racer>();
            float at = -1f;
            race.Physics = x =>
            {
                if (race.Time != at)
                {
                    at = race.Time;
                    near.Clear();
                    foreach (Racer y in race.Racers.OrderBy(y => Math.Abs(Gap(y.Distance - cam.Distance))).Take(24))
                        if (Math.Abs(Gap(y.Distance - cam.Distance)) < 300f) near.Add(y);
                }
                return near.Contains(x);
            };
            race.GlideStepFor = x => Math.Abs(Gap(x.Distance - cam.Distance)) < 600f ? 0.05f : 0.2f;
        }
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        long lastThink = 0;
        double worst = 0, sum = 0, secWorst = 0, secSum = 0;
        int steps = 0, secSteps = 0;
        while (race.Time < seconds)
        {
            race.Step();
            long think = Race.ThinkStepTicks + race.ThinkTicks;
            double ms = (think - lastThink) * f;
            lastThink = think;
            worst = Math.Max(worst, ms); sum += ms; steps++;
            if (ms > 40 && Environment.GetEnvironmentVariable("PERF_SPIKES") != null) Console.WriteLine($"  spike {ms:0} ms at {race.Time:0.00} s");
            secWorst = Math.Max(secWorst, ms); secSum += ms; secSteps++;
            if (secSteps == 100)
            {
                Console.WriteLine($"t {race.Time,5:0.0}  AI mean {secSum / secSteps,6:0.00} ms  worst {secWorst,7:0.00} ms per 10 ms step");
                secWorst = 0; secSum = 0; secSteps = 0;
            }
        }
        Console.WriteLine($"physics cars {Race.ThinkStepTicks * f / steps:0.00} ms per step, gliders {race.ThinkTicks * f / steps:0.00} ms per step");
        Console.WriteLine($"TOTAL AI mean {sum / steps:0.00} ms per 10 ms step, worst {worst:0.00} ms, {race.Racers.Count} cars");
    }
}
