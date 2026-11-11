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
        public float[] D, L, V, Off, Angle, Throttle;
        public int[] K;
        public bool[] Ghost, Busy;
        /// <summary>How far each car's body reaches across the track either side of its centre, m: more for a car turned sideways.</summary>
        public float[] Across;
    }

    sealed class SetupRun
    {
        public Situation S;
        public Race Race;
        public readonly List<Frame> Frames = new List<Frame>();
        /// <summary>The first car run alone from the same start: how far it got at each step.</summary>
        public readonly List<float> Solo = new List<float>();
        /// <summary>Seconds each car braked with room, from positions.</summary>
        public float[] BrakeRoom;

        public int N => S.Cars.Length;
        public string Name(int i) => Race.Name(Race.Racers[i]);
        public int Contacts(int i) => Race.Contacts.Count(c => c.A == Name(i) || c.B == Name(i));
        public float Gap(Frame f, int i, int j) => Check.Gap(f.D[j] - f.D[i]);
        /// <summary>
        /// Seconds car i spent off the throttle behind car j: within 2.5 s of it, no faster than it,
        /// and short of its own lane's speed, while the track beside it had room for a car.
        /// </summary>
        public float LiftedBehind(int i, int j)
        {
            AiPilot p = Race.Racers[i].Pilot;
            float t = 0f;
            foreach (Frame f in Frames)
            {
                float g = Gap(f, i, j);
                if (g <= CarL || Behind(f, j, i) > 2.5f || f.V[i] > f.V[j] + 0.5f) continue;
                // Off the throttle, where its lane alone would not have it lift.
                int k = f.K[i];
                if (f.Throttle[i] > 0.9f || f.V[i] >= 0.98f * p.LaneSpeed(k, f.L[i] - p.Line.Lateral[k])) continue;
                float hw = circuit.HalfWidth(k);
                bool room = hw - (f.L[j] + 0.5f * CarW) >= CarW + RoomMargin || f.L[j] - 0.5f * CarW + hw >= CarW + RoomMargin;
                if (room) t += Race.Dt;
            }
            return t;
        }
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

    static SetupRun RunSetup(Situation s, int shift = 0)
    {
        var run = new SetupRun { S = s, Race = SituationRace(s, shift) };
        Race race = run.Race;
        var braking = new PositionBraking();
        while (race.Time < s.Seconds)
        {
            race.Step();   // a car's place across the track is only known once it has stepped
            braking.Step(race);
            var f = new Frame { T = race.Time, D = new float[run.N], L = new float[run.N], V = new float[run.N], Throttle = new float[run.N], Off = new float[run.N], Angle = new float[run.N], K = new int[run.N], Ghost = new bool[run.N], Busy = new bool[run.N], Across = new float[run.N] };
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
                f.Throttle[i] = r.Throttle;
                f.Angle[i] = (float)(Math.Atan2(Math.Abs(across), Math.Max(along, 0.1f)) * 180.0 / Math.PI);
                f.Ghost[i] = r.Ghost;
                f.Busy[i] = r.Rejoin.Busy;
                float side = Math.Abs(r.Car.Forward.x * circuit.Left[k].x + r.Car.Forward.z * circuit.Left[k].z);
                f.Across[i] = 0.5f * (CarL * side + CarW * (float)Math.Sqrt(Math.Max(1f - side * side, 0f)));
            }
            run.Frames.Add(f);
        }
        run.BrakeRoom = race.Racers.Select(r => braking.Seconds.TryGetValue(r, out float b) ? b : 0f).ToArray();
        var alone = new Situation { Id = s.Id, At = s.At, Seconds = s.Seconds, Cars = new[] { s.Cars[0] } };
        Race solo = SituationRace(alone);
        while (solo.Time < s.Seconds) { solo.Step(); run.Solo.Add(solo.Racers[0].Distance); }
        return run;
    }

    // ---------------------------------------------------------------- every set-up

    /// <summary>Seconds of reaction a set-up's start allows the cars before they must brake.</summary>
    const float StartReaction = 0.3f;

    /// <summary>
    /// Why the set-up's start can't be raced cleanly, or null: two cars placed overlapping, or a car
    /// closing on a car in its lane faster than its brakes can shed in the gap. A correct driver must
    /// always be able to avoid contact.
    /// </summary>
    static string BadStart(SetupRun run)
    {
        Frame f = run.Frames[0];
        for (int i = 0; i < run.N; i++)
            for (int j = 0; j < run.N; j++)
            {
                if (i == j) continue;
                float g = run.Gap(f, i, j);
                float apart = Math.Abs(f.L[j] - f.L[i]), reach = f.Across[i] + f.Across[j];
                if (g >= 0f && g < CarL && apart < reach) return $"{run.Name(i)} and {run.Name(j)} placed overlapping";
                float dv = f.V[i] - f.V[j];
                if (g <= 0f || apart >= reach + RoomMargin || dv <= 0f) continue;
                // A car ahead that is braking for a corner already takes away most of the brakes' edge.
                SpeedProfile pj = run.Race.Racers[j].Pilot.Profile;
                bool braking = pj.Speed[circuit.Wrap(f.K[j] + (int)(Math.Max(f.V[j], 10f) / circuit.Step))] < f.V[j] - 5f;
                float shed = braking ? Math.Max(BrakingOf(run, i) - BrakingOf(run, j), 2f) : BrakingOf(run, i);
                float need = dv * dv / (2f * shed) + StartReaction * dv;
                if (g - CarL - NoseMargin < need) return $"{run.Name(i)} can't brake for {run.Name(j)}: {g - CarL:0} m to shed {dv * 3.6f:0} km/h needs {need:0} m";
            }
        return null;
    }

    /// <summary>
    /// The first car to leave the track while racing, or null. A car already getting back (spun or
    /// rejoining) may be off it; a car that runs off is getting back only once it is off.
    /// </summary>
    static string LeftTrack(SetupRun run)
    {
        for (int i = 0; i < run.N; i++)
        {
            float racing = float.NegativeInfinity, off = 0f;
            foreach (Frame f in run.Frames)
            {
                bool on = run.OnTrack(f, i);
                if (on && !f.Busy[i]) racing = f.T;
                else if (!on && f.T - racing < 0.5f) off += Race.Dt;
            }
            if (off > 0f) return $"{run.Name(i)} left the track";
        }
        return null;
    }

    /// <summary>Hard limit set-ups: a bad start, any contact or a car leaving the track fails, whatever the limit's own check says.</summary>
    static (bool Pass, string Why) Verdict(SetupRun run)
    {
        string bad = BadStart(run);
        if (bad != null) return (false, "bad start: " + bad);
        var j = Judge(run);
        int all = run.Race.Contacts.Count;
        string off = LeftTrack(run);
        if (all > 0 || off != null) return (false, $"{(all > 0 ? $"{all} contacts, " : "")}{(off != null ? off + ", " : "")}{j.Why}");
        return j;
    }

    /// <summary>Behaviour set-ups: a bad start, any contact or a car leaving the track scores 0.</summary>
    static (float Score, string Why) Mark(SetupRun run)
    {
        string bad = BadStart(run);
        if (bad != null) return (0f, "bad start: " + bad);
        var s = Score(run);
        int all = run.Race.Contacts.Count;
        string off = LeftTrack(run);
        if (all > 0 || off != null) return (0f, $"{(all > 0 ? $"{all} contacts, " : "")}{(off != null ? off + ", " : "")}{s.Why}");
        return s;
    }

    // ---------------------------------------------------------------- hard limits

    /// <summary>
    /// Whether the set-up kept its hard limit, and what decided it. Each set-up must make its limit
    /// bind: a limit never put to the test is a fail of the set-up, not a pass.
    /// </summary>
    static (bool Pass, string Why) Judge(SetupRun run)
    {
        List<Frame> fs = run.Frames;
        switch (run.S.Id)
        {
            case "H1":
            {
                float closest = float.MaxValue, nearest = float.MaxValue;
                foreach (Frame f in fs)
                    for (int j = 1; j < run.N; j++)
                    {
                        float g = run.Gap(f, 0, j);
                        if (g > 0f) nearest = Math.Min(nearest, run.Behind(f, j, 0));
                        if (g > 0f && g < 30f && Math.Abs(f.L[j] - f.L[0]) < CarW) closest = Math.Min(closest, g - CarL);
                    }
                if (nearest > 1f) return (false, $"never closed within 1 s of the cars ahead: did not bind");
                return (closest >= NoseMargin, $"closest nose to tail {(closest == float.MaxValue ? "none in line" : $"{closest:0.0} m")}");
            }
            case "H2":
            {
                // The inside car, on the right into the right-hander, keeps a car's width beside the first car.
                // It owes that width and no more: it keeps racing on the widest line that leaves it, as
                // fast as that line allows (Ivan: it must not wave the inside car through).
                float least = float.MaxValue, given = 0f, lifted = 0f;
                int alongside = 0;
                AiPilot p0 = run.Race.Racers[0].Pilot;
                foreach (Frame f in fs)
                    if (Math.Abs(run.Gap(f, 0, 1)) < CarL && f.L[1] < f.L[0] && run.At(f, 0) < T7End)
                    {
                        alongside++;
                        float room = f.L[0] - 0.5f * CarW + circuit.HalfWidth(f.K[0]);
                        least = Math.Min(least, room);
                        float lineRoom = p0.Line.Lateral[f.K[0]] - 0.5f * CarW + circuit.HalfWidth(f.K[0]);
                        given += Math.Max(room - Math.Max(CarW + RoomMargin, lineRoom), 0f);
                        if (f.V[0] < LiftShare * p0.LaneSpeed(f.K[0], f.L[0] - p0.Line.Lateral[f.K[0]])) lifted += Race.Dt;
                        if (Environment.GetEnvironmentVariable("H2_DEBUG") == "1") Console.WriteLine($"H2 {f.T:0.00} v {f.V[0] * 3.6f:0} lane {p0.LaneSpeed(f.K[0], f.L[0] - p0.Line.Lateral[f.K[0]]) * 3.6f:0} lat {f.L[0]:0.0} line {p0.Line.Lateral[f.K[0]]:0.0} off {f.Off[0]:0.0} in {f.L[1]:0.0} room {room:0.0}");
                    }
                if (alongside * Race.Dt < 0.5f) return (false, "the inside car was never alongside: did not bind");
                given /= alongside;
                bool pass = least >= CarW + RoomMargin && given <= GiveSlack && lifted <= LiftSlack;
                return (pass, $"least room inside {least:0.0} m, {given:0.0} m more than owed, lifted {lifted:0.0} s, over {alongside * Race.Dt:0.0} s alongside");
            }
            case "H3":
            {
                // The outside car, its front level or ahead, keeps a car's width beside the first car.
                float least = float.MaxValue;
                int alongside = 0;
                foreach (Frame f in fs)
                    if (run.Gap(f, 0, 1) >= 0f && run.Gap(f, 0, 1) < CarL && run.At(f, 0) < T7End)
                    {
                        alongside++;
                        least = Math.Min(least, circuit.HalfWidth(f.K[0]) - (f.L[0] + 0.5f * CarW));
                    }
                if (alongside * Race.Dt < 0.5f) return (false, "the outside car was never almost ahead alongside: did not bind");
                return (least >= CarW + RoomMargin, $"least room outside {least:0.0} m over {alongside * Race.Dt:0.0} s alongside");
            }
            case "H4":
            {
                // Defending on the straight: one move away from the line, then only back towards it.
                float brake = run.BrakeAt(0);
                int moves = 0;
                float low = 0f, high = 0f, closest = float.MaxValue;
                bool out_ = false;
                foreach (Frame f in fs)
                {
                    if (run.At(f, 0) > brake) break;
                    float b = run.Behind(f, 0, 1);
                    if (b < 0f || b > 1f) continue;
                    closest = Math.Min(closest, b);
                    float off = Math.Abs(f.Off[0]);
                    if (!out_ && off > low + 1f) { moves++; out_ = true; high = off; }
                    else if (out_ && off < high - 1f) { out_ = false; low = off; }
                    high = Math.Max(high, off);
                    low = Math.Min(low, off);
                }
                if (closest > 1f) return (false, "the attacker never closed within 1 s: did not bind");
                if (moves == 0) return (false, "never made its defensive move: did not bind");
                return (moves == 1, $"{moves} moves off the line");
            }
            case "H5":
            {
                float brake = run.BrakeAt(0);
                float? from = null;
                float worst = 0f;
                bool attacked = false;
                foreach (Frame f in fs)
                {
                    if (run.At(f, 0) < brake || run.At(f, 0) > T7End) continue;
                    float b = run.Behind(f, 0, 1);
                    if (b < -0.1f || b > 1f) continue;
                    if (Math.Abs(f.L[1] - f.L[0]) > 1.5f) attacked = true;
                    from ??= f.Off[0];
                    worst = Math.Max(worst, Math.Sign(f.L[1] - f.L[0]) * (f.Off[0] - from.Value));
                }
                if (!attacked) return (false, "no attack off the line in the braking zone: did not bind");
                return (worst < 1f, $"moved {worst:0.0} m towards the car behind");
            }
            case "H6":
            {
                float worst = 0f;
                bool close = false;
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 0, 1);
                    if (b > 0f && b < 2f) { close = true; worst = Math.Max(worst, Math.Abs(f.Off[0])); }
                }
                if (!close) return (false, "the fast car never came within 2 s: did not bind");
                return (worst < 1f, $"{worst:0.0} m off the line");
            }
            case "H7":
            {
                // No dive: it reaches its braking point close behind the car ahead, and makes the corner.
                float brake = run.BrakeAt(0);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= brake);
                if (f == null) return (false, "never reached its braking point");
                float b = run.Behind(f, 1, 0);
                if (b < 0f || b > 1f) return (false, $"{(b < 0f ? "already past" : $"{b:0.0} s behind")} at its braking point: did not bind");
                return (true, $"{b:0.0} s behind at its braking point, made the corner");
            }
            case "H10":
            {
                int alongside = fs.Count(f => Math.Abs(run.Gap(f, 0, 1)) < CarL && run.At(f, 0) >= T7Start && run.At(f, 0) <= T7End);
                if (alongside * Race.Dt < 0.3f) return (false, "never alongside in the corner: did not bind");
                return (true, $"{alongside * Race.Dt:0.0} s alongside in the corner, on the track");
            }
            case "H8":
            {
                float brake = run.BrakeAt(0);
                bool tried = fs.Any(x => run.At(x, 0) >= brake && run.At(x, 0) < T7Start && run.Gap(x, 0, 1) > CarL && run.Gap(x, 0, 1) < 15f && Math.Abs(x.L[1] - x.L[0]) > 1.5f);
                if (!tried) return (false, "never alongside short of overlapping in the braking zone: did not bind");
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= T7Start);
                if (f == null) return (false, "never reached the corner");
                float g = run.Gap(f, 0, 1);
                bool overlapped = g < CarL;
                bool backedOut = Math.Abs(f.L[1] - f.L[0]) < 1.5f || g > 10f;
                return (overlapped || backedOut, $"at turn-in {(overlapped ? "overlapped" : backedOut ? "backed out" : $"{g - CarL:0.0} m short of overlapping")}");
            }
            case "H9":
            {
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 0, 1);
                    if (b < 0.5f) return (false, $"only {Math.Abs(f.Off[0]):0.0} m off the line with the lapping car {Math.Max(b, 0f):0.0} s behind");
                    if (Math.Abs(f.Off[0]) >= 1.5f) return (true, $"off the line with the lapping car {b:0.0} s behind");
                }
                return (false, "the lapping car never came within 0.5 s: did not bind");
            }
            case "H12":
            {
                // A car comes up on a stopped one: it is a ghost, and the car coming slows down and
                // goes round it where there is room, as under a yellow flag.
                bool ghost = fs.Any(f => f.Ghost[0]);
                float slowest = float.MaxValue, apart = float.MaxValue, need = 0f;
                bool met = false;
                foreach (Frame f in fs)
                {
                    float g = run.Gap(f, 1, 0);
                    if (g < -CarL || g > 100f || f.V[0] > 10f) continue;
                    met = true;
                    Racer r = run.Race.Racers[1];
                    slowest = Math.Min(slowest, f.V[1] / Math.Max(r.Pilot.Profile.Speed[f.K[1]], 1f));
                    if (Math.Abs(g) < CarL) { apart = Math.Min(apart, Math.Abs(f.L[1] - f.L[0])); need = Math.Max(need, f.Across[0] + f.Across[1] + RoomMargin); }
                }
                if (!met) return (false, "never came up on the stopped car: did not bind");
                bool slowed = slowest <= SlowFor;
                bool round = apart == float.MaxValue || apart >= need;
                return (ghost && slowed && round, $"{(ghost ? "a ghost" : "never a ghost")}, slowed to {100f * slowest:0}% of its racing speed, {(apart == float.MaxValue ? "never level" : $"passed {apart - need + RoomMargin:0.0} m clear")}");
            }
        }
        return (false, "no judge");
    }

    /// <summary>Share of its racing speed a car coming up on a stopped car slows to, at most: as under a yellow flag.</summary>
    const float SlowFor = 0.75f;

    // ---------------------------------------------------------------- behaviour

    static float Clamp100(float x) => Math.Max(0f, Math.Min(100f, x));
    /// <summary>100 at or under good, 0 at or over bad, in a straight line between.</summary>
    static float Scale(float x, float good, float bad) => Clamp100(100f * (bad - x) / (bad - good));

    /// <summary>Most a car rejoining from the grass may take to get back on once the track is clear, seconds.</summary>
    const float GrassAfterClear = 1f;

    /// <summary>
    /// Car j is coming at car 0: solid, on the track and behind it, within <see cref="Rejoin.ComingSeconds"/>
    /// at its speed, or alongside, unless already past and pulling away.
    /// </summary>
    static bool CarComing(SetupRun run, Frame f, int j) => CarComing(f.D[0], f.V[0], f.D[j], f.V[j], f.Ghost[j], run.OnTrack(f, j));

    static (float Score, string Why) Score(SetupRun run)
    {
        List<Frame> fs = run.Frames;
        int hits = run.Contacts(0);
        int all = run.Race.Contacts.Count;
        Frame last = fs[fs.Count - 1];
        switch (run.S.Id)
        {
            case "T1":
            {
                // Catching a slower car: stays on the line in the slipstream.
                int follow = 0, onLine = 0;
                foreach (Frame f in fs)
                {
                    float b = run.Behind(f, 1, 0);
                    if (b < 0.3f || b > 2.5f) continue;
                    follow++;
                    if (Math.Abs(f.Off[0]) < 1f) onLine++;
                }
                float share = follow > 0 ? (float)onLine / follow : 0f;
                // Flat in the tow, not lifting to sit at the speed of the car ahead (Ivan).
                float lifted = run.LiftedBehind(0, 1);
                return (Clamp100(Math.Min(100f * share, Scale(lifted, 0.3f, 2f)) - 25f * hits), $"on the line {100f * share:0}% of {follow * Race.Dt:0.0} s following, lifted behind it {lifted:0.0} s, {hits} contacts");
            }
            case "T2":
            case "T12":
            {
                // Pulls out at the last safe moment instead of braking: no braking with room, a late
                // pull-out, and little time lost.
                float room = run.BrakeRoom[0];
                float lost = run.Lost();
                Frame pullAt = fs.FirstOrDefault(f => Math.Abs(f.Off[0]) > 1f && run.Gap(f, 0, 1) > 0f);
                Frame level = fs.FirstOrDefault(f => run.Gap(f, 0, 1) < CarL);
                float early = pullAt != null && level != null ? level.T - pullAt.T : 0f;
                float lifted = run.LiftedBehind(0, 1);
                float score = Math.Min(Math.Min(Math.Min(Scale(room, 0f, 1f), Scale(lost, 0.2f, 1.5f)), Scale(early, 2f, 5f)), Scale(lifted, 0.3f, 2f));
                return (Clamp100(score - 50f * hits), $"braked with room {room:0.00} s, lifted behind {lifted:0.0} s, pulled out {early:0.0} s before drawing level, lost {lost:0.00} s, {hits} contacts");
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
                // Back on the line before the braking zone, or a car's width left beside a car alongside.
                // A car further up the road, too far ahead to pass before the corner, changes nothing:
                // once back on the line the car stays on it.
                float brake = run.BrakeAt(0);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= brake);
                if (f == null) return (0f, "never reached its braking point");
                float off = Math.Abs(f.Off[0]);
                bool alongside = Math.Abs(run.Gap(f, 0, 1)) < CarL;
                float room = f.L[1] < f.L[0] ? f.L[0] - 0.5f * CarW + circuit.HalfWidth(f.K[0]) : circuit.HalfWidth(f.K[0]) - f.L[0] - 0.5f * CarW;
                float back = off < 1f || (alongside && room >= CarW + RoomMargin) ? 100f : Scale(off, 1f, 4f);
                Frame onLine = fs.FirstOrDefault(x => Math.Abs(x.Off[0]) < 1f && Math.Abs(run.Gap(x, 0, 1)) > CarL);
                float strayed = onLine == null ? 0f : fs.Where(x => x.T >= onLine.T && x.T <= f.T).Select(x => Math.Abs(x.Off[0])).DefaultIfEmpty(0f).Max();
                return (Math.Min(back, Scale(strayed, 1f, 3f)), $"{off:0.0} m off the line at its braking point, strayed {strayed:0.0} m once back on it");
            }
            case "T5":
            {
                // Squeezed on the outside, not yet almost ahead: the inside car takes the room it may
                // (H3), and the outside car cuts back or backs out before it is forced wide.
                bool forced = fs.Any(f => Math.Abs(run.Gap(f, 0, 1)) < CarL && Math.Abs(f.L[1] - f.L[0]) < CarW + 1f
                    && circuit.HalfWidth(f.K[0]) - (Math.Abs(f.L[0]) + 0.5f * CarW) < 0.3f);
                float given = 0f;
                int alongside = 0;
                foreach (Frame f in fs)
                {
                    float g = run.Gap(f, 0, 1);
                    if (g <= 0f || g >= CarL || run.At(f, 0) < run.BrakeAt(1) || run.At(f, 0) > T7End) continue;
                    alongside++;
                    float outside = Math.Sign(f.L[0] - f.L[1]);
                    float left = circuit.HalfWidth(f.K[1]) - (outside * f.L[1] + 0.5f * CarW);
                    given += Math.Max(left - (CarW + RoomMargin), 0f);
                }
                given = alongside > 0 ? given / alongside : 0f;
                float score = forced ? 0f : Scale(given, 0.5f, 3f);
                return (score, $"outside car {(forced ? "forced to the edge alongside" : "cut back or backed out in time")}, inside car left {given:0.0} m it didn't owe");
            }
            case "T6":
            case "T6b":
            case "T6c":
            {
                // Outbrakes only a car with worse braking, and never goes three wide into a corner.
                float brake = run.BrakeAt(0);
                bool threeWide = ThreeWide(run, brake);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= T7End + 30f) ?? last;
                Frame atBrake = fs.FirstOrDefault(x => run.At(x, 0) >= brake) ?? fs[0];
                var passed = Enumerable.Range(1, run.N - 1).Where(j => run.Gap(atBrake, 0, j) > -CarL * 0.5f && run.Gap(f, 0, j) < 0f).ToList();
                bool wrong = passed.Any(j => run.Race.Racers[j].Entry != null && BrakingOf(run, j) >= BrakingOf(run, 0));
                bool worseAlongside = Enumerable.Range(1, run.N - 1).Any(j => BrakingOf(run, j) < BrakingOf(run, 0) && run.Gap(atBrake, 0, j) > -CarL && run.Gap(atBrake, 0, j) < 2f * CarL);
                float score = threeWide || wrong ? 0f : worseAlongside ? (passed.Count > 0 ? 100f : 0f) : 100f;
                string late = "";
                if (worseAlongside)
                {
                    // Outbraking means braking late: against the latest point its own brakes allow.
                    float early = EarlyBraking(run, 0);
                    score = Math.Min(score, Scale(early, 5f, 30f));
                    late = $", braked {early:0} m before its latest point";
                }
                return (score, $"{(threeWide ? "three wide into the corner, " : "")}outbraked {passed.Count}{(wrong ? " including a car that brakes as well or better" : "")}{(worseAlongside && passed.Count == 0 ? ", left a worse braker alongside unpassed" : "")}{late}");
            }
            case "T7":
            {
                // Passes promptly: little time lost against running alone, no lift or brake on the
                // straight, and a reasonable margin alongside.
                float lost = run.Lost();
                float least = float.MaxValue;
                foreach (Frame f in fs)
                    if (Math.Abs(run.Gap(f, 0, 1)) < CarL) least = Math.Min(least, Math.Abs(f.L[1] - f.L[0]));
                float hesitated = Hesitation(run, 0);
                float score = Math.Min(Scale(lost, 0.2f, 1f), Scale(hesitated, 0f, 0.3f)) * (least >= CarW + 1f ? 1f : 0.5f);
                return (score, $"lost {lost:0.00} s, lifted or braked {hesitated:0.0} s on the straight, closest alongside {(least == float.MaxValue ? 0f : least - CarW):0.0} m apart");
            }
            case "T8":
            {
                // After a pass, moves back as soon as its rear is past the other car's front; never swipes.
                bool swiped = fs.Any(f => Math.Abs(run.Gap(f, 0, 1)) < CarL && Math.Abs(f.L[1] - f.L[0]) < CarW + 0.2f);
                Frame clear = fs.FirstOrDefault(f => -run.Gap(f, 0, 1) > CarL);
                if (clear == null) return (0f, "never cleared the other car");
                Frame turn = fs.FirstOrDefault(f => f.T >= clear.T && Math.Abs(f.Off[0]) < Math.Abs(clear.Off[0]) - 0.3f);
                float took = turn == null ? 99f : turn.T - clear.T;
                return (hits > 0 || swiped ? 0f : Scale(took, 0.3f, 2f), $"started back {(turn == null ? "never" : $"{took:0.0} s after clearing")}, {(swiped ? "swiped across" : "no swipe")}, {hits} contacts");
            }
            case "T9":
            {
                // Passes as many as it can; at its braking point it is outbraking the car level with
                // it, in a gap on the line big enough for a car and a margin, or behind. Never three wide into the corner.
                int passed = Enumerable.Range(1, run.N - 1).Count(j => run.Gap(last, 0, j) < 0f);
                float brake = run.BrakeAt(0);
                bool threeWide = ThreeWide(run, brake);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= brake);
                string at = "never reached its braking point";
                bool sorted = false;
                if (f != null)
                {
                    int level = Enumerable.Range(1, run.N - 1).Count(j => Math.Abs(run.Gap(f, 0, j)) < CarL);
                    float ahead = Enumerable.Range(1, run.N - 1).Where(j => run.Gap(f, 0, j) >= CarL && Math.Abs(f.L[j] - f.L[0]) < CarW).Select(j => run.Gap(f, 0, j) - CarL).DefaultIfEmpty(99f).Min();
                    float behind = Enumerable.Range(1, run.N - 1).Where(j => run.Gap(f, j, 0) >= CarL && Math.Abs(f.L[j] - f.L[0]) < CarW).Select(j => run.Gap(f, j, 0) - CarL).DefaultIfEmpty(99f).Min();
                    bool onLine = Math.Abs(f.Off[0]) < 1.5f;
                    sorted = level == 1 || (onLine && level == 0 && ahead >= NoseMargin && behind >= NoseMargin) || (level == 0 && ahead >= NoseMargin);
                    at = level == 1 ? "level with one car" : level == 0 ? $"in a gap ({Math.Min(ahead, behind):0.0} m)" : $"level with {level} cars";
                }
                float score = (100f * passed / (run.N - 1) - 20f * hits) * (threeWide || !sorted ? 0.5f : 1f);
                return (Clamp100(score), $"passed {passed} of {run.N - 1}, at its braking point {at}{(threeWide ? ", three wide into the corner" : "")}, {hits} contacts");
            }
            case "T11":
            {
                // Lap one: no braking with room, no pile-ups.
                int stopped = Enumerable.Range(0, run.N).Count(j => fs.Any(f => f.T > 8f && f.V[j] < 5f));
                float room = run.BrakeRoom.Sum();
                return (Clamp100(Scale(room, 0f, 3f) - 20f * all - 50f * stopped), $"braked with room {room:0.0} s over all cars, {all} contacts, {stopped} cars stopped after the start");
            }
            case "T17":
            {
                // Off on the grass with traffic coming: waits for it, then back on as soon as the runoff's
                // grip allows. The shallow angle only counts with a car coming when it crosses.
                Frame f = fs.FirstOrDefault(x => x.T > 0.1f && run.OnTrack(x, 0));
                if (f == null) return (0f, "never back on the track");
                bool coming = Enumerable.Range(1, run.N - 1).Any(j => CarComing(run, f, j));
                Frame clear = fs.FirstOrDefault(x => !Enumerable.Range(1, run.N - 1).Any(j => CarComing(run, x, j))) ?? last;
                float waited = f.T - clear.T;
                float angle = coming ? Scale(f.Angle[0], 10f, 30f) : Scale(f.Angle[0], Rejoin.ClearEntryAngle, Rejoin.ClearEntryAngle + 15f);
                return (Clamp100(Math.Min(angle, Scale(waited, GrassAfterClear, GrassAfterClear + 1.5f)) - 50f * all),
                    $"on the grass {f.T:0.0} s, {waited:0.0} s of it with the track clear, back on at {f.Angle[0]:0} degrees{(coming ? " with a car coming" : "")}, {all} contacts");
            }
            case "T18":
            {
                // The car ahead brakes earlier: outbrake it.
                float brake = run.BrakeAt(0);
                Frame f = fs.FirstOrDefault(x => run.At(x, 0) >= T7End + 30f) ?? last;
                Frame pass = fs.FirstOrDefault(x => run.Gap(x, 0, 1) < -CarL);
                bool ahead = run.Gap(f, 0, 1) < 0f;
                bool inZone = pass != null && run.At(pass, 0) >= brake - 50f;
                float score = hits > 0 ? 0f : !ahead ? 0f : inZone ? 100f : 70f;
                return (score, $"{(!ahead ? "stayed behind" : inZone ? "outbraked it" : "passed before the braking zone")}, {hits} contacts");
            }
            case "T18b":
            case "T19":
            {
                // Blocked from outbraking (T18b) or on worn tyres (T19): brakes with a margin, never a
                // rear-end, never off the track, and not so early it drops far back.
                float closest = float.MaxValue;
                foreach (Frame f in fs)
                    for (int j = 1; j < run.N; j++)
                    {
                        float g = run.Gap(f, 0, j);
                        if (g > 0f && g < 30f && Math.Abs(f.L[j] - f.L[0]) < CarW) closest = Math.Min(closest, g - CarL);
                    }
                bool off = fs.Any(f => !run.OnTrack(f, 0));
                Frame turnIn = fs.FirstOrDefault(x => run.At(x, 0) >= T7Start);
                float gapIn = turnIn == null ? 99f : Enumerable.Range(1, run.N - 1).Where(j => run.Gap(turnIn, 0, j) > 0f).Select(j => run.Gap(turnIn, 0, j) - CarL).DefaultIfEmpty(99f).Min();
                float score = hits > 0 || off ? 0f : Math.Min(Scale(-closest, -NoseMargin, 0f), Scale(gapIn, 15f, 40f));
                return (score, $"closest nose to tail {closest:0.0} m, {gapIn:0.0} m behind at turn-in, {(off ? "off the track, " : "")}{hits} contacts");
            }
        }
        return (0f, "no score");
    }

    /// <summary>
    /// Metres before its latest braking point a car started braking for Turn 7: where its speed
    /// would have met what its own lane and brakes allow there.
    /// </summary>
    static float EarlyBraking(SetupRun run, int i)
    {
        List<Frame> fs = run.Frames;
        Racer r = run.Race.Racers[i];
        for (int n = 10; n < fs.Count; n++)
        {
            Frame f = fs[n];
            float at = run.At(f, i);
            if (at < T7Start - 600f || at > T7Start) continue;
            if ((fs[n - 10].V[i] - f.V[i]) / (10 * Race.Dt) < 6f) continue;
            Frame from = fs[n - 10];
            float v = from.V[i];
            int k = from.K[i];
            for (int m = 0; m < 400; m++, k = circuit.Wrap(k + 1))
                if (r.Pilot.LaneSpeed(k, from.Off[i]) <= v) return m * circuit.Step;
            return 0f;
        }
        return 0f;
    }

    /// <summary>Seconds a car lifted or braked on the straight before its braking point, below its racing speed.</summary>
    static float Hesitation(SetupRun run, int i)
    {
        List<Frame> fs = run.Frames;
        Racer r = run.Race.Racers[i];
        float brake = run.BrakeAt(i), t = 0f;
        for (int n = 20; n < fs.Count; n++)
        {
            Frame f = fs[n];
            float at = run.At(f, i);
            if (at < 2917f || at > brake - 10f) continue;
            if (f.V[i] < fs[n - 20].V[i] - 0.2f && f.V[i] < r.Pilot.Profile.Speed[f.K[i]] - 1f) t += Race.Dt;
        }
        return t;
    }

    /// <summary>The braking a car has, m/s², as the others see it.</summary>
    static float BrakingOf(SetupRun run, int i)
    {
        Racer r = run.Race.Racers[i];
        return r.Car.Spec.BrakeDecel * 9.81f * AiPilot.LimitFor(r.Pilot.Pace);
    }

    /// <summary>Three or more cars level with each other from the first car's braking point to the corner.</summary>
    static bool ThreeWide(SetupRun run, float brake) => run.Frames.Any(f =>
    {
        float at = run.At(f, 0);
        return at > brake && at < T7End && Enumerable.Range(0, run.N).Count(j => Math.Abs(run.Gap(f, 0, j)) < CarL) >= 3;
    });

    /// <summary>
    /// Seconds each car braked for the car ahead while there was room to pull out, from where the cars
    /// are: slowing faster than 4 m/s² outside its own braking zone, with a car within 2 s ahead in its
    /// lane and a free lane beside that car.
    /// </summary>
    sealed class PositionBraking
    {
        public readonly Dictionary<Racer, float> Seconds = new Dictionary<Racer, float>();
        readonly Dictionary<Racer, float> was = new Dictionary<Racer, float>();

        public void Step(Race r)
        {
            foreach (Racer x in r.Racers)
            {
                int k = Math.Max(x.Car.TrackIndex, 0);
                float v = x.Car.Vx * circuit.Forward[k].x + x.Car.Vz * circuit.Forward[k].z;
                bool slowing = was.TryGetValue(x, out float v0) && (v0 - v) / Race.Dt > 4f;
                was[x] = v;
                if (!slowing || x.Ghost || x.Rejoin.Busy || InBrakingZone(x)) continue;
                Racer ahead = null;
                float best = 2f * Math.Max(v, 10f);
                foreach (Racer o in r.Racers)
                {
                    if (o == x || o.Ghost) continue;
                    float g = Gap(o.Distance - x.Distance);
                    if (g > 0f && g < best && Math.Abs(o.Car.Lateral - x.Car.Lateral) < CarW + 0.5f) { best = g; ahead = o; }
                }
                if (ahead != null && RoomBeside(r, x, ahead.Id)) Seconds[x] = (Seconds.TryGetValue(x, out float s) ? s : 0f) + Race.Dt;
            }
        }
    }

    // ---------------------------------------------------------------- races

    /// <summary>Each race measure, the way its score runs and the values that score 100 and 0. Lower is better unless Higher.</summary>
    /// <summary>H2: room beyond a car's width the outside car may leave on average, m, and seconds it may lift below its lane's speed.</summary>
    const float GiveSlack = 1f, LiftSlack = 0.5f;
    /// <summary>Share of its lane's speed below which a car counts as lifting: the pilot's own braking runs a few per cent under it.</summary>
    const float LiftShare = 0.93f;
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
        var braking = new PositionBraking();
        Race race = RunRace(field, r =>
        {
            braking.Step(r);
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
            braking.Seconds.Values.Sum() / carHours,
            race.Contacts.Count / carHours,
            race.Contacts.Count(c => c.RearEnd && c.Speed > RearHit) / carHours,
            piles.PileUps.Count,
            race.FastestStuck >= 3 ? 1f : 0f,
        };
        if (Environment.GetEnvironmentVariable("PILE_LOG") == "1") foreach (string p in piles.PileUps) Console.WriteLine("PILE " + p);
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

    /// <summary>Set-ups run over several driver seeds, and how many.</summary>
    static readonly string[] Repeated = { "T9", "T11" };
    const int Repeats = 5;

    static int Suite(bool full)
    {
        var clock = Stopwatch.StartNew();
        foreach (Situation s in Situations.All) foreach (SituationCar c in s.Cars) LineFor(c.Model);
        foreach (string m in BModels.Concat(new[] { "a-29" })) LineFor(m);
        if (full) foreach (Entry e in GameField(0)) LineFor(e.Model);

        Situation[] ready = Situations.All.Where(x => x.Ready && Environment.GetEnvironmentVariable("SUITE_NO_SETUPS") == null).ToArray();
        var runs = new SetupRun[ready.Length];
        Parallel.For(0, ready.Length, i => runs[i] = RunSetup(ready[i]));
        var limits = new List<(string Id, bool Pass, string Why)>();
        var behaviour = new List<(string Id, float Score, string Why)>();
        foreach (SetupRun run in runs)
        {
            if (run.S.Id.StartsWith("H")) { var j = Verdict(run); limits.Add((run.S.Id, j.Pass, j.Why)); }
            else if (Repeated.Contains(run.S.Id))
            {
                // A set-up whose score turns on one chance contact between the other cars: the mean over
                // several driver seeds, with how many of them scored, so one draw doesn't swing the total.
                var marks = Enumerable.Range(0, Repeats).AsParallel().AsOrdered().Select(i => Mark(i == 0 ? run : RunSetup(run.S, i))).ToArray();
                behaviour.Add((run.S.Id, marks.Average(m => m.Score), $"mean of {Repeats} seeds, {marks.Count(m => m.Score > 0)} scored; first: {marks[0].Why}"));
            }
            else { var sc = Mark(run); behaviour.Add((run.S.Id, sc.Score, sc.Why)); }
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
        float behaviourScore = behaviour.Count > 0 ? behaviour.Average(b => b.Score) : 0f;
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
