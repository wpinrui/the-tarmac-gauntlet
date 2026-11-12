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
        public int Lap1Contacts, RestContacts, Lap1Hard, RestHard, Lap1Multi, RestMulti, Lap1Offs, RestOffs, Piles, Stuck, Crashes, CrashCars;
        public List<string> CrashList = new List<string>();
        public List<string> Multi = new List<string>(), PileList = new List<string>();
        /// <summary>Part-throttle seconds on the free stretch behind a slower car, with a lane beside open or not.</summary>
        public float HeldOpen, HeldShut, HeldFlat, Hours;
        public float[] HeldGap = new float[3];
        public List<(float Seconds, string Line)> Spells = new List<(float, string)>();
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
            if (Environment.GetEnvironmentVariable("PACK_LOG") == "1") foreach (string s in p.Multi.Concat(p.PileList).Concat(p.CrashList)) Console.WriteLine("  " + s);
        }
        float hrs = results.Sum(p => p.Hours);
        Console.WriteLine($"HELD part-throttle behind a slower car on the free stretch: {results.Sum(p => p.HeldOpen) / hrs:0.0} s/car-hour with a lane beside open, {results.Sum(p => p.HeldShut) / hrs:0.0} with none; {results.Sum(p => p.HeldFlat) / hrs:0.0} of it with a legal flat-out path");
        Console.WriteLine($"HELD with the widest free gap beside over 2 m {results.Sum(p => p.HeldGap[0]) / hrs:0.0}, over 4 m {results.Sum(p => p.HeldGap[1]) / hrs:0.0}, over 6 m {results.Sum(p => p.HeldGap[2]) / hrs:0.0} s/car-hour");
        if (Environment.GetEnvironmentVariable("PACK_HELD") != null)
            foreach (var sp in results.SelectMany(p => p.Spells).OrderByDescending(x => x.Seconds).Take(int.Parse(Environment.GetEnvironmentVariable("PACK_HELD"))))
                Console.WriteLine($"  {sp.Seconds,5:0.0} s {sp.Line}");
        float h1 = results.Sum(p => p.Lap1Hours), h2 = results.Sum(p => p.RestHours);
        Console.WriteLine($"TOTAL lap1 {Rate(results.Sum(p => p.Lap1Contacts), h1)} c/h, hard {Rate(results.Sum(p => p.Lap1Hard), h1)}, multi {results.Sum(p => p.Lap1Multi)}, offs {results.Sum(p => p.Lap1Offs)}"
            + $" | rest {Rate(results.Sum(p => p.RestContacts), h2)} c/h, hard {Rate(results.Sum(p => p.RestHard), h2)}, multi {results.Sum(p => p.RestMulti)}, offs {results.Sum(p => p.RestOffs)}"
            + $" | piles {results.Sum(p => p.Piles)}, stuck {results.Sum(p => p.Stuck)}");
        Console.WriteLine($"CRASHES {results.Sum(p => p.Crashes) / hrs * 100f:0.00} per 100 car-hours ({results.Sum(p => p.Crashes)}), {results.Sum(p => p.CrashCars) / (float)Math.Max(results.Sum(p => p.Crashes), 1):0.0} cars each");
    }

    static string Rate(int n, float hours) => (n / Math.Max(hours, 1e-3f)).ToString("0.0");

    static PackResult RunPack(List<Entry> field, float minutes, int seed)
    {
        var p = new PackResult { Seed = seed };
        var piles = new PileWatch($"seed {seed} ");
        var busy = new Dictionary<Racer, bool>();
        var byName = new Dictionary<string, Racer>();
        // Hard contacts and cars going off, for crashes: three or more cars at one spot within a few seconds.
        var events = new List<(float T, float D, string Car, bool Off)>();
        int seen = 0;
        var held = new HeldWatch();
        var lap1 = new List<bool>();
        Race race = RunRace(field, r =>
        {
            if (byName.Count == 0) foreach (Racer x in r.Racers) byName[r.Name(x)] = x;
            piles.Step(r);
            foreach (Racer x in r.Racers)
            {
                bool b = x.Rejoin.Busy;
                if (b && !(busy.TryGetValue(x, out bool was) && was)) { if (x.Laps == 0) p.Lap1Offs++; else p.RestOffs++; events.Add((r.Time, Mod(x.Distance), r.Name(x), true)); }
                busy[x] = b;
                if (x.Laps == 0) p.Lap1Hours += Race.Dt / 3600f; else p.RestHours += Race.Dt / 3600f;
            }
            held.Step(r, p);
            string tr = Environment.GetEnvironmentVariable("PACK_TRACE");
            if (tr != null && (int)Math.Round(r.Time / Race.Dt) % 50 == 0 && byName.TryGetValue(tr, out Racer tx) && tx.Rejoin.Busy)
                Console.WriteLine($"  TRACE {Race.Clock(r.Time)} {tr} {tx.Rejoin.State} cause {tx.Rejoin.Cause} ghost {tx.Ghost} lat {tx.Car.Lateral:0.0} past edge {tx.Rejoin.PastEdge:0.0} heading {tx.Rejoin.Heading:0} aim {tx.Rejoin.Aim:0.0} gap clear {tx.Rejoin.GapClear} coming {tx.Rejoin.Coming} yielding {tx.Rejoin.Yielding} {tx.Car.Speed * 3.6f:0} km/h at {Mod(tx.Distance):0} m");
            for (; seen < r.Contacts.Count; seen++)
            {
                Contact c = r.Contacts[seen];
                lap1.Add(byName[c.A].Laps == 0 || byName[c.B].Laps == 0);
                if (c.Speed > 3f) { events.Add((c.Time, Mod(byName[c.A].Distance), c.A, false)); events.Add((c.Time, Mod(byName[c.B].Distance), c.B, false)); }
                if (Environment.GetEnvironmentVariable("PACK_CONTACTS") != null)
                {
                    string Path(Racer x)
                    {
                        var ch = x.Craft.Chooser; var k = ch?.Chosen;
                        if (k == null) return $"{r.Name(x)} no plan{(x.Ghost ? " ghost" : "")}{(x.Rejoin.Busy ? " rejoin" : "")}";
                        return $"{r.Name(x)} {k.Pedal}{(k.Follow ? "F" : "")}{(k.Fixed ? "f" : "")}{(k.Delay > 0 ? "d" : "")} {k.Shift:0.0} {(k.Rejected ?? "ok")} {(x.Pilot.Index >= 0 && TarmacGauntlet.Raceday.PathChooser.FreeStretchAt(x.Pilot) ? "free" : "corner")} v{x.Car.Speed * 3.6f:0} lat{x.Car.Lateral:0.0}";
                    }
                    Racer xa = byName[c.A], xb = byName[c.B];
                    Console.WriteLine($"  CONTACT {Race.Clock(c.Time)} {c.Speed:0.0} m/s gap {Gap(xb.Distance - xa.Distance):0.0} | {Path(xa)} | {Path(xb)}");
                }
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
        // Crashes: hard contacts and offs joined when within 5 s and 40 m of one another, with three or more cars
        // and at least one of them off the track. A crash, not traffic: what reads as a pile-up from the stands.
        var group = new int[events.Count];
        for (int i = 0; i < events.Count; i++) group[i] = i;
        int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        for (int i = 0; i < events.Count; i++)
            for (int j = i + 1; j < events.Count && events[j].T < events[i].T + 5f; j++)
                if (Math.Abs(Gap(events[j].D - events[i].D)) < 40f) group[Find(j)] = Find(i);
        foreach (var g in Enumerable.Range(0, events.Count).GroupBy(Find))
        {
            var cars = g.Select(i => events[i].Car).Distinct().ToList();
            if (cars.Count < 3 || !g.Any(i => events[i].Off)) continue;
            p.Crashes++; p.CrashCars += cars.Count;
            var e0 = events[g.First()];
            p.CrashList.Add($"CRASH {cars.Count} cars, {g.Count(i => events[i].Off)} off, at {e0.D:0} m at {Race.Clock(e0.T)}: {string.Join(" ", cars)}");
        }
        held.Finish(race, p);
        p.Hours = race.Racers.Count * race.Time / 3600f;
        p.Piles = piles.PileUps.Count;
        p.PileList.AddRange(piles.PileUps.Select(x => "PILE " + x));
        p.Stuck = race.Racers.Count(x => x.StuckTime > 10f || x.Rejoin.Busy && x.RejoinTime > 30f);
        foreach (Racer x in race.Racers.Where(x => x.StuckTime > 10f || x.Rejoin.Busy && x.RejoinTime > 30f))
            Console.WriteLine($"  STUCK seed {seed} {race.Name(x)} stuck {x.StuckTime:0} s, rejoin {(x.Rejoin.Busy ? x.RejoinTime.ToString("0") + " s" : "no")}, ghost {x.Ghost}, at {Gap(x.Distance):0} m lap {x.Laps}, lateral {x.Car.Lateral:0.0} of {circuit.Data.width[Math.Max(x.Car.TrackIndex, 0)] / 2f:0.0}, speed {x.Car.Speed * 3.6f:0} km/h, track index {x.Car.TrackIndex}");
        return p;
    }
}

static partial class Check
{
    /// <summary>"perf": the AI's cost on a full-field grid start, ms. PERF_GAME runs it as the game does.</summary>
    static void PerfStart()
    {
        float seconds = float.TryParse(Environment.GetEnvironmentVariable("PERF_SECONDS"), out float s) ? s : 20f;
        // PERF_WARM: a short race first, so the measured one runs on code already compiled, as a player's second race would.
        if (Environment.GetEnvironmentVariable("PERF_WARM") != null)
        {
            var warm = new Race(circuit, GameField(12), LineFor);
            while (warm.Time < 1f) warm.Step();
            Race.ThinkStepTicks = 0;
        }
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
        long allocatedFrom = GC.GetTotalAllocatedBytes(true);
        float timeFrom = race.Time;
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
            if (Environment.GetEnvironmentVariable("PERF_SPIKES") is string spikes && ms > (float.TryParse(spikes, out float over) ? over : 40f)) Console.WriteLine($"  spike {ms:0} ms at {race.Time:0.00} s");
            secWorst = Math.Max(secWorst, ms); secSum += ms; secSteps++;
            if (secSteps == 100)
            {
                Console.WriteLine($"t {race.Time,5:0.0}  AI mean {secSum / secSteps,6:0.00} ms  worst {secWorst,7:0.00} ms per 10 ms step");
                secWorst = 0; secSum = 0; secSteps = 0;
            }
        }
        Console.WriteLine($"physics cars {Race.ThinkStepTicks * f / steps:0.00} ms per step, gliders {race.ThinkTicks * f / steps:0.00} ms per step");
        Console.WriteLine($"TOTAL AI mean {sum / steps:0.00} ms per 10 ms step, worst {worst:0.00} ms, {race.Racers.Count} cars");
        // Garbage the game's collector has to clear, AI and the stand-in physics together: in a game frame it all adds up.
        Console.WriteLine($"garbage {(GC.GetTotalAllocatedBytes(true) - allocatedFrom) / 1e6 / Math.Max(race.Time - timeFrom, 0.01f):0.0} MB per race second");
    }
}

static partial class Check
{
    /// <summary>
    /// Time a car spends part-throttle on the free stretch with a slower car ahead in its lane, within 2 s,
    /// and held by racecraft below the speed its own lane gives it: stuck behind it on the straight. Spells are kept with why at their middle.
    /// </summary>
    sealed class HeldWatch
    {
        sealed class Spell { public float Start, Open, Shut, Gap; public string Why, Who; public int At; }
        readonly Dictionary<Racer, Spell> open = new Dictionary<Racer, Spell>();

        public void Step(Race r, PackResult p)
        {
            foreach (Racer x in r.Racers)
            {
                bool on = Held(r, x, out Racer ahead);
                open.TryGetValue(x, out Spell sp);
                if (!on)
                {
                    if (sp != null) { Close(r, x, sp, p); open.Remove(x); }
                    continue;
                }
                bool room = RoomBeside(r, x, ahead.Id);
                if (room) p.HeldOpen += Race.Dt; else p.HeldShut += Race.Dt;
                var chooser = x.Craft.Chooser;
                if (chooser != null && chooser.Candidates.Any(k => k.Pedal == TarmacGauntlet.Raceday.PathChooser.Pedal.Go && k.Rejected == null)) p.HeldFlat += Race.Dt;
                if (sp == null) open[x] = sp = new Spell { Start = r.Time, At = Math.Max(x.Car.TrackIndex, 0) };
                if (room) sp.Open += Race.Dt; else sp.Shut += Race.Dt;
                float wide = WidestGap(r, x, ahead);
                for (int i = 0; i < 3; i++) if (wide > 2f * (i + 1)) p.HeldGap[i] += Race.Dt;
                sp.Gap = Math.Max(sp.Gap, wide);
                // Why, sampled half a second in.
                if (sp.Why == null && r.Time - sp.Start >= 0.5f) { sp.Why = Why(r, x); sp.Who = $"{r.Name(ahead)} {ahead.Car.Speed * 3.6f:0} km/h {Gap(ahead.Distance - x.Distance):0} m ahead, {ahead.Car.Lateral - x.Car.Lateral:0.0} m across"; }
            }
        }

        public void Finish(Race r, PackResult p) { foreach (var kv in open) Close(r, kv.Key, kv.Value, p); open.Clear(); }

        void Close(Race r, Racer x, Spell sp, PackResult p)
        {
            float s = sp.Open + sp.Shut;
            if (s < 0.5f) return;
            p.Spells.Add((s, $"seed {p.Seed} {r.Name(x)} at {Race.Clock(sp.Start)} from {sp.At * circuit.Step:0} m, open {sp.Open:0.0} s, gap {sp.Gap:0.0} m: behind {sp.Who} | {sp.Why}"));
        }

        /// <summary>The widest stretch across the track, edge to edge, with no car in it from alongside this one to a car's length past the car ahead, m.</summary>
        static float WidestGap(Race r, Racer x, Racer ahead)
        {
            int k = Math.Max(x.Car.TrackIndex, 0);
            float half = circuit.Data.width[k] / 2f, reach = Gap(ahead.Distance - x.Distance) + CarL;
            var taken = new List<(float lo, float hi)>();
            // Only room it can get to: not past a car alongside it.
            float left = -half, right = half, me = x.Car.Lateral;
            foreach (Racer y in r.Racers)
            {
                if (y == x || y.Ghost) continue;
                float d = Gap(y.Distance - x.Distance);
                if (d > -CarL && d < reach) taken.Add((y.Car.Lateral - 0.5f * CarW, y.Car.Lateral + 0.5f * CarW));
                if (Math.Abs(d) < CarL && y != ahead)
                {
                    if (y.Car.Lateral < me) left = Math.Max(left, y.Car.Lateral + 0.5f * CarW);
                    else right = Math.Min(right, y.Car.Lateral - 0.5f * CarW);
                }
            }
            taken.Sort((a, b) => a.lo.CompareTo(b.lo));
            float at = left, widest = 0f;
            foreach (var t in taken)
            {
                if (t.hi <= left) continue;
                if (t.lo >= right) break;
                widest = Math.Max(widest, Math.Min(t.lo, right) - at);
                at = Math.Max(at, t.hi);
            }
            return Math.Max(widest, right - at);
        }

        static bool Held(Race r, Racer x, out Racer ahead)
        {
            ahead = null;
            if (x.Pilot.Gliding || x.Rejoin.Busy || x.Ghost || x.Pilot.Index < 0) return false;
            if (!TarmacGauntlet.Raceday.PathChooser.FreeStretchAt(x.Pilot)) return false;
            // Held back by the car ahead, below the speed its own lane would give it: racecraft's cap under that speed.
            // Off its own desired throttle for the grip: the cap binds, not the lane's speed or traction.
            if (x.Pilot.HeldBy < 0.5f || x.Car.Speed < x.Pilot.SpeedCap - 1f) return false;
            float free = x.Pilot.FreeSpeed;
            float best = 2f * Math.Max(x.Car.Speed, 10f);
            foreach (Racer o in r.Racers)
            {
                if (o == x || o.Ghost) continue;
                float g = Gap(o.Distance - x.Distance);
                if (g > 0f && g < best && Math.Abs(o.Car.Lateral - x.Car.Lateral) < CarW + 0.5f) { best = g; ahead = o; }
            }
            return ahead != null && ahead.Car.Speed < free - 1f;
        }

        static string Why(Race r, Racer x)
        {
            var ch = x.Craft.Chooser;
            if (ch == null || ch.Chosen == null) return "no plan";
            var c = ch.Chosen;
            var go = ch.Candidates.Where(k => k.Pedal == TarmacGauntlet.Raceday.PathChooser.Pedal.Go).ToList();
            var bestGo = go.Where(k => k.Rejected == null).OrderByDescending(k => k.Score).FirstOrDefault();
            string tally = string.Join(" ", go.Where(k => k.Rejected != null).GroupBy(k => k.Rejected + (k.BrokeBy >= 0 ? "@" + k.BrokeBy : "")).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}"));
            return $"chose {c.Pedal} {c.Shift:0.0}{(c.Delay > 0 ? "d" + c.Delay : "")}{(c.Fixed ? "f" : "")}{(c.Follow ? "F" : "")} {(c.Rejected ?? "")} S{c.Score:0.00}=g{c.Gain:0.00}-r{c.Risk:0.00}-s{c.Stuck:0.00} [{ch.Reason}] {x.Car.Speed * 3.6f:0} km/h, free {x.Pilot.FreeSpeed * 3.6f:0}, cap {x.Pilot.SpeedCap * 3.6f:0}{(x.Craft.Braking ? " BRK" : "")}; best legal Go {(bestGo == null ? "none" : $"{bestGo.Shift:0.0}{(bestGo.Delay > 0 ? "d" + bestGo.Delay : "")}{(bestGo.Fixed ? "f" : "")} S{bestGo.Score:0.00}=g{bestGo.Gain:0.00}-r{bestGo.Risk:0.00}-s{bestGo.Stuck:0.00}")}; follow {string.Join(" ", ch.Candidates.Where(k => k.Follow).Select(k => $"{k.Shift:0.0}:{(k.Rejected != null ? k.Rejected + "@" + k.BrokeBy + "s" + k.BrokeAt : k.Score.ToString("0.00"))}"))}; Go rejected {tally}; latest {string.Join(" ", go.Where(k => k.Rejected != null).OrderByDescending(k => k.BrokeAt).Take(3).Select(k => $"{k.Shift:0.0}{(k.Fixed ? "f" : "")}{(k.Delay > 0 ? "d" + k.Delay : "")}:{k.Rejected}@{k.BrokeBy}s{k.BrokeAt}"))}";
        }
    }
}
