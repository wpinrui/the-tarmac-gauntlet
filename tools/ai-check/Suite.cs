// "suite": the AI racecraft test suite, one overall score 0 to 100.
//   suite         quick mode: every set-up, then 2 seeds of Race A. Printed only.
//   suite full    every set-up, 8 seeds each of Race A, Race B and Race C, and a row on the scoreboard
//                 (scoreboard.csv, shown in scoreboard.md). SUITE_NOTE says what the run tried.
// Hard limits (H set-ups) pass or fail and stay out of the score: any fail shows red. Behaviour (T set-ups)
// scores 0 to 100 each. Races score each measure 0 to 100 against the targets in RaceTargets.
// Overall = average of the behaviour score and the race scores. Every measure comes from where the cars
// are, never from what racecraft thinks it is doing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TarmacGauntlet.Raceday;

static partial class Check
{
    const float CarL = Racecraft.CarLength, CarW = Racecraft.CarWidth;
    /// <summary>Room the spec's "a car's width" leaves on top of the car, m.</summary>
    const float RoomMargin = 0.3f;
    /// <summary>Gap kept nose to tail, m: the spec's "a safety margin is always kept".</summary>
    const float NoseMargin = 0.5f;
    const float T7Start = 3966.8f, T7End = 4022.6f;

    /// <summary>Where each car of a set-up was at one step.</summary>
    sealed class Frame
    {
        public float T;
        public float[] D, L, V, Off, Angle;
        public int[] K;
        public bool[] Ghost;
    }

    sealed class SetupRun
    {
        public Situation S;
        public Race Race;
        public readonly List<Frame> Frames = new List<Frame>();
        /// <summary>The first car run alone from the same start: how far it got at each step.</summary>
        public readonly List<float> Solo = new List<float>();

        public int N => S.Cars.Length;
        public string Name(int i) => Race.Name(Race.Racers[i]);
        public int Contacts(int i) => Race.Contacts.Count(c => c.A == Name(i) || c.B == Name(i));
        public float Gap(Frame f, int i, int j) => Check.Gap(f.D[j] - f.D[i]);
        /// <summary>Lap distance, m.</summary>
        public float At(Frame f, int i) => ((f.D[i] % circuit.Lap) + circuit.Lap) % circuit.Lap;
        /// <summary>On the track as F1 counts it: some part of a tyre on the tarmac or its white line. Kerbs are not track.</summary>
        public bool OnTrack(Frame f, int i) => Math.Abs(f.L[i]) - 0.5f * CarW <= circuit.HalfWidth(f.K[i]);
        /// <summary>Where the car's braking for Turn 7 starts: where its racing speed peaks before the corner.</summary>
        public float BrakeAt(int i)
        {
            SpeedProfile p = Race.Racers[i].Pilot.Profile;
            int best = circuit.IndexOf(T7Start - 600f);
            for (int k = best; k != circuit.IndexOf(T7Start); k = circuit.Wrap(k + 1))
                if (p.Speed[k] >= p.Speed[best]) best = k;
            return best * circuit.Step;
        }
        /// <summary>Seconds the first car lost against running alone.</summary>
        public float Lost()
        {
            float end = Frames[Frames.Count - 1].D[0];
            int i = Solo.FindIndex(d => d >= end);
            return i < 0 ? 0f : (Frames.Count - 1 - i) * Race.Dt;
        }
        /// <summary>Car j's time gap behind car i, s: how long j takes to reach where i is.</summary>
        public float Behind(Frame f, int i, int j) => Gap(f, j, i) / Math.Max(f.V[j], 5f);
    }

    static SetupRun RunSetup(Situation s)
    {
        var run = new SetupRun { S = s, Race = SituationRace(s) };
        Race race = run.Race;
        while (race.Time < s.Seconds)
        {
            race.Step();   // a car's place across the track is only known once it has stepped
            var f = new Frame { T = race.Time, D = new float[run.N], L = new float[run.N], V = new float[run.N], Off = new float[run.N], Angle = new float[run.N], K = new int[run.N], Ghost = new bool[run.N] };
            for (int i = 0; i < run.N; i++)
            {
                Racer r = race.Racers[i];
                int k = Math.Max(r.Car.TrackIndex, 0);
                f.D[i] = r.Distance;
                f.L[i] = r.Car.Lateral;
                f.K[i] = k;
                f.Off[i] = r.Car.Lateral - r.Pilot.Line.Lateral[k];
                float along = r.Car.Vx * circuit.Forward[k].x + r.Car.Vz * circuit.Forward[k].z;
                float across = r.Car.Vx * circuit.Left[k].x + r.Car.Vz * circuit.Left[k].z;
                f.V[i] = along;
                f.Angle[i] = (float)(Math.Atan2(Math.Abs(across), Math.Max(along, 0.1f)) * 180.0 / Math.PI);
                f.Ghost[i] = r.Ghost;
            }
            run.Frames.Add(f);
        }
        var alone = new Situation { Id = s.Id, At = s.At, Seconds = s.Seconds, Cars = new[] { s.Cars[0] } };
        Race solo = SituationRace(alone);
        while (solo.Time < s.Seconds) { solo.Step(); run.Solo.Add(solo.Racers[0].Distance); }
        return run;
    }

    // ---------------------------------------------------------------- hard limits

    /// <summary>Whether the set-up kept its hard limit, and what decided it.</summary>
    static (bool Pass, string Why) Judge(SetupRun run)
    {
        List<Frame> fs = run.Frames;
        int hits = run.Contacts(0);
        switch (run.S.Id)
        {
            case "H1":
            {
                float closest = float.MaxValue;
                foreach (Frame f in fs)
                    for (int j = 1; j < run.N; j++)
                    {
                        float g = run.Gap(f, 0, j);
                        if (g > 0f && g < 30f && Math.Abs(f.L[j] - f.L[0]) < CarW) closest = Math.Min(closest, g - CarL);
                    }
                return (hits == 0 && closest >= NoseMargin, $"{hits} contacts, closest nose to tail {closest:0.0} m");
            }
            case "H2":
            {
                // The inside car, on the right into the right-hander, keeps a car's width beside the first car.
                float least = float.MaxValue;
                foreach (Frame f in fs)
                    if (Math.Abs(run.Gap(f, 0, 1)) < CarL && f.L[1] < f.L[0] && run.At(f, 0) < T7End)
                        least = Math.Min(least, f.L[0] - 0.5f * CarW + circuit.HalfWidth(f.K[0]));
                return (hits == 0 && least >= CarW + RoomMargin, $"{hits} contacts, least room inside {least:0.0} m");
            }
            case "H3":
            {
                // The outside car, its front level or ahead, keeps a car's width beside the first car.
                float least = float.MaxValue;
                foreach (Frame f in fs)
                    if (run.Gap(f, 0, 1) >= 0f && run.Gap(f, 0, 1) < CarL && run.At(f, 0) < T7End)
                        least = Math.Min(least, circuit.HalfWidth(f.K[0]) - (f.L[0] + 0.5f * CarW));
                return (hits == 0 && least >= CarW + RoomMargin, $"{hits} contacts, least room outside {least:0.0} m");
            }
            case "H4":
            {
                // Defending on the straight: moves away from the line, counted from the last turn back.
                float brake = run.BrakeAt(0);
                int moves = 0;
                float low = 0f, high = 0f;
                bool out_ = false;
                foreach (Frame f in fs)
                {
                    if (run.At(f, 0) > brake) break;
                    float b = run.Behind(f, 0, 1);
                    if (b < 0f || b > 1f) continue;
                    float off = Math.Abs(f.Off[0]);
                    if (!out_ && off > low + 1f) { moves++; out_ = true; high = off; }
                    else if (out_ && off < high - 1f) { out_ = false; low = off; }
                    high = Math.Max(high, off);
                    low = Math.Min(low, off);
                }
                return (moves <= 1, $"{moves} moves off the line");
            }
            case "H5":
            {
                float brake = run.BrakeAt(0);
                float? from = null;
                float worst = 0f;
                foreach (Frame f in fs)
                {
                    if (run.At(f, 0) < brake || run.At(f, 0) > T7End) continue;
                    float b = run.Behind(f, 0, 1);
                    if (b < -0.1f || b > 1f) continue;
                    from ??= f.Off[0];
                    worst = Math.Max(worst, Math.Sign(f.L[1] - f.L[0]) * (f.Off[0] - from.Value));
                }
                return (worst < 1f, $"moved {worst:0.0} m towards the car behind");
            }
            case "H6":
            {
                float worst = 0f;
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 0, 1);
                    if (b > 0f && b < 2f) worst = Math.Max(worst, Math.Abs(f.Off[0]));
                }
                return (worst < 1f, $"{worst:0.0} m off the line");
            }
            case "H7":
            case "H10":
            {
                int off = fs.Count(f => !run.OnTrack(f, 0));
                return (hits == 0 && off == 0, $"{hits} contacts, {off * Race.Dt:0.0} s off the track");
            }
            case "H8":
            {
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= T7Start);
                if (f == null) return (false, "never reached the corner");
                float g = run.Gap(f, 0, 1);
                bool overlapped = g < CarL;
                bool backedOut = Math.Abs(f.L[1] - f.L[0]) < 1.5f || g > 10f;
                return (hits == 0 && (overlapped || backedOut), $"{hits} contacts, at turn-in {(overlapped ? "overlapped" : backedOut ? "backed out" : $"{g - CarL:0.0} m short of overlapping")}");
            }
            case "H9":
            {
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 0, 1);
                    if (b < 0.5f) return (false, $"only {Math.Abs(f.Off[0]):0.0} m off the line with the lapping car {Math.Max(b, 0f):0.0} s behind");
                    if (Math.Abs(f.Off[0]) >= 1.5f) return (true, $"off the line with the lapping car {b:0.0} s behind");
                }
                return (false, "the lapping car never came within 0.5 s");
            }
            case "H12":
            {
                bool ghost = fs.Any(f => f.Ghost[0]);
                int all = run.Race.Contacts.Count;
                return (all == 0 && ghost, $"{all} contacts, {(ghost ? "a ghost" : "never a ghost")}");
            }
        }
        return (false, "no judge");
    }

    // ---------------------------------------------------------------- behaviour

    static float Clamp100(float x) => Math.Max(0f, Math.Min(100f, x));
    /// <summary>100 at or under good, 0 at or over bad, in a straight line between.</summary>
    static float Scale(float x, float good, float bad) => Clamp100(100f * (bad - x) / (bad - good));

    static (float Score, string Why) Score(SetupRun run)
    {
        List<Frame> fs = run.Frames;
        int hits = run.Contacts(0);
        Frame last = fs[fs.Count - 1];
        switch (run.S.Id)
        {
            case "T1":
            {
                int follow = 0, onLine = 0;
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 1, 0);
                    if (b < 0.3f || b > 2.5f) continue;
                    follow++;
                    if (Math.Abs(f.Off[0]) < 1f) onLine++;
                }
                float share = follow > 0 ? (float)onLine / follow : 0f;
                return (Clamp100(100f * share - 25f * hits), $"on the line {100f * share:0}% of {follow * Race.Dt:0.0} s following, {hits} contacts");
            }
            case "T2":
            case "T12":
            {
                float lost = run.Lost();
                return (Clamp100(Scale(lost, 0.2f, 1.5f) - 50f * hits), $"lost {lost:0.00} s, {hits} contacts");
            }
            case "T3":
            {
                int stuck = 0;
                float lastAt = run.At(fs[0], 0);
                foreach (Frame f in fs)
                {
                    float at = run.At(f, 0);
                    if (run.Gap(f, 0, 1) > 0f && run.Behind(f, 1, 0) < 2f)
                        foreach (CornerData c in circuit.Data.corners)
                            if (c.s > lastAt && c.s <= at) stuck++;
                    lastAt = at;
                }
                return (Clamp100(100f - 25f * Math.Max(stuck - 2, 0) - 25f * hits), $"{stuck} corners started behind, {hits} contacts");
            }
            case "T4":
            {
                float brake = run.BrakeAt(0);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= brake);
                if (f == null) return (0f, "never reached its braking point");
                float off = Math.Abs(f.Off[0]);
                bool alongside = Math.Abs(run.Gap(f, 0, 1)) < CarL;
                float room = f.L[1] < f.L[0] ? f.L[0] - 0.5f * CarW + circuit.HalfWidth(f.K[0]) : circuit.HalfWidth(f.K[0]) - f.L[0] - 0.5f * CarW;
                if (off < 1f || (alongside && room >= CarW + RoomMargin)) return (Clamp100(100f - 50f * hits), $"{off:0.0} m off the line at its braking point, {hits} contacts");
                return (Clamp100(Scale(off, 1f, 4f) - 50f * hits), $"{off:0.0} m off the line at its braking point, {hits} contacts");
            }
            case "T5":
            {
                bool off = fs.Any(f => !run.OnTrack(f, 0));
                return (Clamp100(100f - 50f * hits - (off ? 50f : 0f)), $"{hits} contacts, {(off ? "forced off the track" : "kept on the track")}");
            }
            case "T6":
            case "T18":
            {
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= T7End + 30f) ?? last;
                bool ahead = run.Gap(f, 0, 1) < 0f;
                return (hits > 0 ? 0f : ahead ? 100f : 40f, $"{(ahead ? "ahead" : "behind")} out of the corner, {hits} contacts");
            }
            case "T7":
            {
                float lost = run.Lost();
                float least = float.MaxValue;
                foreach (Frame f in fs)
                    if (Math.Abs(run.Gap(f, 0, 1)) < CarL) least = Math.Min(least, Math.Abs(f.L[1] - f.L[0]));
                float score = Scale(lost, 0.3f, 2f) * (least >= CarW + 1f ? 1f : 0.5f);
                return (hits > 0 ? 0f : score, $"lost {lost:0.00} s, closest alongside {(least == float.MaxValue ? 0f : least - CarW):0.0} m apart, {hits} contacts");
            }
            case "T8":
            {
                bool swiped = fs.Any(f => Math.Abs(run.Gap(f, 0, 1)) < CarL && Math.Abs(f.L[1] - f.L[0]) < CarW + 0.2f);
                Frame clear = fs.FirstOrDefault(f => -run.Gap(f, 0, 1) > CarL);
                if (clear == null) return (0f, "never cleared the other car");
                Frame back = fs.FirstOrDefault(f => f.T >= clear.T && Math.Abs(f.Off[0]) < 1f);
                float took = back == null ? 99f : back.T - clear.T;
                return (hits > 0 || swiped ? 0f : Scale(took, 1.5f, 5f), $"back on the line {(back == null ? "never" : $"{took:0.0} s after clearing")}, {(swiped ? "swiped across" : "no swipe")}, {hits} contacts");
            }
            case "T9":
            {
                int passed = Enumerable.Range(1, run.N - 1).Count(j => run.Gap(last, 0, j) < 0f);
                bool threeWide = fs.Any(f =>
                {
                    float at = run.At(f, 0);
                    return at > run.BrakeAt(0) && at < T7End && Enumerable.Range(0, run.N).Count(j => Math.Abs(run.Gap(f, 0, j)) < CarL) >= 3;
                });
                return (Clamp100((100f * passed / (run.N - 1) - 20f * hits) * (threeWide ? 0.5f : 1f)), $"passed {passed} of {run.N - 1}, {hits} contacts{(threeWide ? ", three wide into a corner" : "")}");
            }
            case "T11":
            {
                int all = run.Race.Contacts.Count;
                int stopped = Enumerable.Range(0, run.N).Count(j => fs.Any(f => f.T > 8f && f.V[j] < 5f));
                return (Clamp100(100f - 20f * all - 50f * stopped), $"{all} contacts, {stopped} cars stopped after the start");
            }
            case "T17":
            {
                Frame f = fs.FirstOrDefault(x => x.T > 0.1f && run.OnTrack(x, 0));
                if (f == null) return (0f, "never back on the track");
                int all = run.Race.Contacts.Count;
                return (Clamp100(Scale(f.Angle[0], 10f, 30f) - 50f * all), $"back on at {f.Angle[0]:0} degrees, {all} contacts");
            }
            case "T18b":
            case "T19":
            {
                float closest = float.MaxValue;
                foreach (Frame f in fs)
                    for (int j = 1; j < run.N; j++)
                    {
                        float g = run.Gap(f, 0, j);
                        if (g > 0f && g < 30f && Math.Abs(f.L[j] - f.L[0]) < CarW) closest = Math.Min(closest, g - CarL);
                    }
                return (hits > 0 ? 0f : Scale(-closest, -1f, 0f), $"closest nose to tail {closest:0.0} m, {hits} contacts");
            }
        }
        return (0f, "no score");
    }

    // ---------------------------------------------------------------- races

    /// <summary>Each race measure, the way its score runs and the values that score 100 and 0. Lower is better unless Higher.</summary>
    static readonly (string Name, float Good, float Bad)[] RaceTargets =
    {
        ("field lap over solo %", 3f, 15f),
        ("A lap over solo %", 3f, 15f),
        ("A lost to traffic s/lap", 1f, 10f),
        ("field lost to traffic s/lap", 0.5f, 5f),
        ("braking with room s/car-hour", 0f, 120f),
        ("contacts /car-hour", 2f, 50f),
        ("rear-ends /car-hour", 0f, 2f),
        ("pile-ups", 0f, 1f),
        ("fastest stuck 3 corners", 0f, 1f),
    };
    /// <summary>Race B's extra measure.</summary>
    static readonly (string Name, float Good, float Bad) LappedTarget = ("lost passing lapped cars s/car-hour", 5f, 60f);

    /// <summary>Seconds each car loses to traffic, from where the cars are: with a car within 2 s ahead or alongside, the time it took over the time its own line's speeds give for the same ground.</summary>
    sealed class TrafficLoss
    {
        public readonly Dictionary<Racer, float> Lost = new Dictionary<Racer, float>();
        public float LappedLost;

        public void Step(Race r)
        {
            if (r.Time < 30f) return;   // the standing start is not traffic
            foreach (Racer x in r.Racers)
            {
                if (x.Ghost || x.Rejoin.Busy || x.Car.TrackIndex < 0 || x.Moved <= 0f) continue;
                Racer near = null;
                foreach (Racer o in r.Racers)
                {
                    if (o == x || o.Ghost) continue;
                    float g = Gap(o.Distance - x.Distance);
                    if (g > -CarL && g < 2f * Math.Max(x.Car.Speed, 10f)) { near = o; break; }
                }
                if (near == null) continue;
                int k = x.Car.TrackIndex;
                float free = x.Moved * (x.Pilot.Line.Segment[k] / circuit.Step) / Math.Max(x.Pilot.Profile.Speed[k], 1f);
                float lost = Race.Dt - free;
                Lost[x] = (Lost.TryGetValue(x, out float was) ? was : 0f) + lost;
                if (near.Distance < x.Distance - circuit.Lap * 0.5f || near.Laps < x.Laps) LappedLost += lost;
            }
        }
    }

    sealed class RaceResult
    {
        public float[] Measures;
        public string Line;
    }

    static RaceResult RunScoredRace(List<Entry> field, float minutes, bool tenX, bool lapped, int seed)
    {
        var watch = new ClassWatch();
        var piles = new PileWatch($"seed {seed} ");
        var loss = new TrafficLoss();
        Race race = RunRace(field, r =>
        {
            if (watch.Cut == 0f) watch.Cut = 1.3f * r.Racers.Max(x => x.Pilot.Profile.LapTime);
            watch.Step(r);
            piles.Step(r);
            loss.Step(r);
            return false;
        }, minutes * 60f, tenX ? r =>
        {
            // At 10x every AI car glides, finer near the camera on the A car.
            r.Physics = x => false;
            Racer a0 = r.Racers[r.Racers.Count - 1];
            r.Camera = a0;
            r.GlideStepFor = x => Math.Abs(Gap(x.Distance - a0.Distance)) < 600f ? 0.1f : 0.2f;
        } : (Action<Race>)null);
        Racer a = race.Racers[race.Racers.Count - 1];
        float carHours = race.Racers.Count * minutes / 60f;
        var overSolo = new List<float>();
        foreach (var kv in watch.Clean)
            foreach (float lap in kv.Value) overSolo.Add(100f * (lap / kv.Key.Pilot.Profile.LapTime - 1f));
        List<float> aLaps = watch.Clean.TryGetValue(a, out var al) ? al : new List<float>();
        float aOver = aLaps.Count > 0 ? 100f * (aLaps.Average() / a.Pilot.Profile.LapTime - 1f) : 100f;
        float laps = race.Racers.Sum(x => Math.Max(x.Laps, 1));
        var m = new List<float>
        {
            overSolo.Count > 0 ? overSolo.Average() : 100f,
            aOver,
            (loss.Lost.TryGetValue(a, out float aLost) ? aLost : 0f) / Math.Max(a.Laps, 1),
            loss.Lost.Values.Sum() / laps,
            watch.ToolBrakeRoom / carHours,
            race.Contacts.Count / carHours,
            race.Contacts.Count(c => c.RearEnd && c.Speed > RearHit) / carHours,
            piles.PileUps.Count,
            race.FastestStuck >= 3 ? 1f : 0f,
        };
        if (lapped) m.Add(loss.LappedLost / carHours);
        return new RaceResult { Measures = m.ToArray(), Line = $"A place {race.Racers.Count(x => x.Distance > a.Distance) + 1}, A laps {a.Laps}, {race.PassCount} passes" };
    }

    /// <summary>The game's full field: the season's roster by class, quickest class first, the A car at the back.</summary>
    static List<Entry> GameField(int seed)
    {
        var models = new List<string>();
        foreach (var cls in new[] { TarmacGauntlet.Simulation.CarClass.A, TarmacGauntlet.Simulation.CarClass.B, TarmacGauntlet.Simulation.CarClass.C,
            TarmacGauntlet.Simulation.CarClass.D, TarmacGauntlet.Simulation.CarClass.E, TarmacGauntlet.Simulation.CarClass.F })
        {
            var ids = TarmacGauntlet.Simulation.CarModels.All.Where(x => x.CarClass == cls && x.Id[1] == '-').Select(x => x.Id).OrderByDescending(x => x, StringComparer.Ordinal).ToList();
            int j = 0;
            foreach (var team in TarmacGauntlet.Simulation.AiTeamRoster.All)
                if (team.CarClass == cls) models.Add(ids[j++ % ids.Count]);
        }
        models.Add("a-29");
        var rng = new Random(seed);
        GridSlot[] slots = circuit.Data.grid.slots;
        var entries = new List<Entry>();
        for (int i = 0; i < models.Count; i++)
        {
            float d, lateral;
            if (i < slots.Length) { d = slots[i].d; lateral = slots[i].lateral; }
            else
            {
                int lastSlot = slots.Length - 1;
                d = slots[lastSlot].d - (i - lastSlot) * (slots[lastSlot - 1].d - slots[lastSlot].d);
                lateral = slots[i % 2].lateral;
            }
            entries.Add(new Entry
            {
                Model = models[i], Start = d - 2.6f, Lateral = lateral,
                Pace = 70f + 30f * (float)rng.NextDouble(), Consistency = 60f + 40f * (float)rng.NextDouble(), Safety = 80f + 20f * (float)rng.NextDouble(),
                Seed = i + 1,
            });
        }
        return entries;
    }

    sealed class RaceScore
    {
        public string Name;
        public (string Name, float Good, float Bad)[] Targets;
        public List<RaceResult> Seeds = new List<RaceResult>();
        public float Mean(int i) => Seeds.Average(s => s.Measures[i]);
        public float Spread(int i) => Seeds.Max(s => s.Measures[i]) - Seeds.Min(s => s.Measures[i]);
        public float Score => Targets.Select((t, i) => Scale(Mean(i), t.Good, t.Bad)).Average();
    }

    // ---------------------------------------------------------------- the suite

    static int Suite(bool full)
    {
        var clock = Stopwatch.StartNew();
        foreach (Situation s in Situations.All) foreach (SituationCar c in s.Cars) LineFor(c.Model);
        foreach (string m in BModels.Concat(new[] { "a-29" })) LineFor(m);
        if (full) foreach (Entry e in GameField(0)) LineFor(e.Model);

        Situation[] ready = Situations.All.Where(x => x.Ready).ToArray();
        var runs = new SetupRun[ready.Length];
        Parallel.For(0, ready.Length, i => runs[i] = RunSetup(ready[i]));
        var limits = new List<(string Id, bool Pass, string Why)>();
        var behaviour = new List<(string Id, float Score, string Why)>();
        foreach (SetupRun run in runs)
        {
            if (run.S.Id.StartsWith("H")) { var j = Judge(run); limits.Add((run.S.Id, j.Pass, j.Why)); }
            else { var sc = Score(run); behaviour.Add((run.S.Id, sc.Score, sc.Why)); }
        }

        int[] seeds = full ? new[] { 11, 12, 13, 14, 15, 16, 17, 18 } : new[] { 11, 12 };
        if (Environment.GetEnvironmentVariable("SUITE_SEEDS") is string ss) seeds = ss.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        float minutes = float.TryParse(Environment.GetEnvironmentVariable("SUITE_MINUTES"), out float mm) ? mm : 15f;
        var races = new List<RaceScore> { new RaceScore { Name = "Race A", Targets = RaceTargets } };
        if (full)
        {
            races.Add(new RaceScore { Name = "Race B", Targets = RaceTargets.Append(LappedTarget).ToArray() });
            races.Add(new RaceScore { Name = "Race C", Targets = RaceTargets.Append(LappedTarget).ToArray() });
        }
        var jobs = new List<(RaceScore Into, Func<RaceResult> Run)>();
        foreach (int seed in seeds)
        {
            int sd = seed;
            jobs.Add((races[0], () => RunScoredRace(ClassField(sd), minutes, false, false, sd)));
            if (full)
            {
                jobs.Add((races[1], () => RunScoredRace(GameField(sd), minutes, false, true, sd)));
                jobs.Add((races[2], () => RunScoredRace(GameField(sd), minutes, true, true, sd)));
            }
        }
        var results = new RaceResult[jobs.Count];
        Parallel.For(0, jobs.Count, i => results[i] = jobs[i].Run());
        for (int i = 0; i < jobs.Count; i++) jobs[i].Into.Seeds.Add(results[i]);

        // ---- report
        string version = File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "v?";
        string label = $"{version} {Experiments.Label}";
        Console.WriteLine($"AI suite, {(full ? "full" : "quick")} mode, {label}, {clock.Elapsed.TotalMinutes:0.0} min");
        Console.WriteLine();
        int fails = limits.Count(l => !l.Pass);
        Console.WriteLine($"Hard limits: {limits.Count - fails} of {limits.Count} pass{(fails > 0 ? "  RED" : "")}");
        foreach (var l in limits) Console.WriteLine($"  {l.Id,-5} {(l.Pass ? "pass" : "FAIL")}  {l.Why}");
        float behaviourScore = behaviour.Average(b => b.Score);
        Console.WriteLine($"Behaviour: {behaviourScore:0.0}");
        foreach (var b in behaviour) Console.WriteLine($"  {b.Id,-5} {b.Score,5:0}  {b.Why}");
        foreach (RaceScore r in races)
        {
            Console.WriteLine($"{r.Name}: {r.Score:0.0} over seeds {string.Join(" ", seeds)}");
            for (int i = 0; i < r.Targets.Length; i++)
                Console.WriteLine($"  {r.Targets[i].Name,-36} {r.Mean(i),8:0.00}  spread {r.Spread(i),6:0.00}  score {Scale(r.Mean(i), r.Targets[i].Good, r.Targets[i].Bad),3:0}");
            foreach (RaceResult s in r.Seeds) Console.WriteLine($"  seed: {s.Line}, measures {string.Join(" ", s.Measures.Select(x => x.ToString("0.000")))}");
        }
        float overall = new[] { behaviourScore }.Concat(races.Select(r => r.Score)).Average();
        Console.WriteLine();
        Console.WriteLine($"Overall {overall:0.0}{(fails > 0 ? $"  RED: {fails} hard limits fail" : "")}");

        if (full) Record(label, overall, fails, limits, behaviour, races);
        return 0;
    }

    static readonly string VersionFile = FindRepo("unity/Assets/Raceday/Resources/AiVersion.txt");
    static string FindRepo(string path)
    {
        for (string d = Directory.GetCurrentDirectory(); d != null; d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, path))) return Path.Combine(d, path);
        return path;
    }

    /// <summary>A row on the scoreboard for a full run, and the scoreboard page made again from every row.</summary>
    static void Record(string label, float overall, int fails, List<(string Id, bool Pass, string Why)> limits,
        List<(string Id, float Score, string Why)> behaviour, List<RaceScore> races)
    {
        string csv = Path.Combine(Path.GetDirectoryName(FindRepo("tools/ai-check/AiCheck.csproj")), "scoreboard.csv");
        var cols = new List<(string, string)>
        {
            ("date", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")),
            ("commit", Environment.GetEnvironmentVariable("SUITE_COMMIT") ?? ""),
            ("label", label),
            ("tried", (Environment.GetEnvironmentVariable("SUITE_NOTE") ?? "").Replace(",", ";")),
            ("overall", $"{overall:0.0}"),
            ("hard limits failed", fails == 0 ? "0" : $"{fails} ({string.Join(" ", limits.Where(l => !l.Pass).Select(l => l.Id))})"),
            ("behaviour", $"{behaviour.Average(b => b.Score):0.0}"),
        };
        foreach (var b in behaviour) cols.Add((b.Id, $"{b.Score:0}"));
        foreach (RaceScore r in races)
        {
            cols.Add((r.Name, $"{r.Score:0.0}"));
            for (int i = 0; i < r.Targets.Length; i++) cols.Add(($"{r.Name} {r.Targets[i].Name}", $"{r.Mean(i):0.00}"));
        }
        bool fresh = !File.Exists(csv);
        using (var w = new StreamWriter(csv, append: true))
        {
            if (fresh) w.WriteLine(string.Join(",", cols.Select(c => c.Item1)));
            w.WriteLine(string.Join(",", cols.Select(c => c.Item2)));
        }
        WriteScoreboard(csv);
    }

    static void WriteScoreboard(string csv)
    {
        string[] lines = File.ReadAllLines(csv);
        string[] head = lines[0].Split(',');
        List<string[]> rows = lines.Skip(1).Select(l => l.Split(',')).ToList();
        var md = new List<string>
        {
            "# AI racecraft scoreboard",
            "",
            "Full suite runs only, made by `ai-check suite full` from scoreboard.csv. Higher scores are better.",
            "",
            "| " + string.Join(" | ", head.Take(7)) + " |",
            "|" + string.Concat(Enumerable.Repeat(" --- |", 7)),
        };
        foreach (string[] r in rows) md.Add("| " + string.Join(" | ", r.Take(7)) + " |");
        md.Add("");
        md.Add("## Best per test");
        md.Add("");
        md.Add("| Test | Best | Run |");
        md.Add("| --- | --- | --- |");
        for (int c = 4; c < head.Length; c++)
        {
            // Scores only: the T set-ups, the behaviour score and each race's score.
            string h = head[c];
            if (!(h == "overall" || h == "behaviour" || (h.StartsWith("T") && !h.Contains(' ')) || (h.StartsWith("Race ") && h.Length == 6))) continue;
            int best = -1;
            float bestValue = float.MinValue;
            for (int i = 0; i < rows.Count; i++)
                if (c < rows[i].Length && float.TryParse(rows[i][c], out float v) && v > bestValue) { bestValue = v; best = i; }
            if (best >= 0) md.Add($"| {head[c]} | {bestValue:0.0} | {rows[best][0]} {rows[best][2]} {rows[best][1]} |");
        }
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(csv), "scoreboard.md"), md);
    }
}
