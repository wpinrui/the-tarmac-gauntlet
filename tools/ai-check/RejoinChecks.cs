// Checks for getting back into the race after leaving the track or spinning (Rejoin.cs).
//
//   rejoin from off track   cars put on the grass beside the track, at speed and at angles from 80° towards
//                           the track to 30° away from it, at corner exits and on a straight, alone and with
//                           traffic coming: each gets back to racing on its line, crosses the tarmac edge
//                           at no more than Rejoin.MaxEntryAngle from the track direction, and touches no
//                           car while rejoining. The worst heading on the tarmac afterwards is reported:
//                           a car still turning in a bend at its grip limit can run past the limit there
//   spun car waits          cars sent into a spin on the track with traffic coming: each stops, never moves
//                           while a car approaching from behind is inside the gap (Rejoin.GapSeconds at its
//                           speed, at least Rejoin.GapMetres), turns round, rejoins within the heading limit,
//                           and touches no car once it moves again
//   pack races (in RacePack) no car starts a rejoin unless it has left the track or lost control
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;

static partial class Check
{
    /// <summary>What one rejoin run measured on the car that left the track or spun.</summary>
    sealed class RejoinRun
    {
        public string Name;
        public bool Rejoined, Stopped, Recovered;
        public float WorstOnTarmac, WorstEntry = -1f;
        public float WaitStart = -1f, WaitEnd = -1f, BackAt = -1f;
        public float WorstWaitSpeed;
        public string GapBroken;
        public readonly List<string> Touches = new List<string>();
        public int TouchesStopped;
        public float SpunTo;
    }

    /// <summary>Spots to test at: just after four corners, where cars run wide, and the middle of the longest straight.</summary>
    static List<float> RejoinSpots()
    {
        CornerData[] corners = circuit.Data.corners;
        var spots = new List<float>();
        for (int i = 0; i < corners.Length; i += Math.Max(1, corners.Length / 4)) spots.Add(Mod(corners[i].e + 15f));
        float best = 0f, at = 0f;
        for (int i = 0; i < corners.Length; i++)
        {
            float gap = circuit.Ahead(corners[i].e, corners[(i + 1) % corners.Length].s);
            if (gap > best) { best = gap; at = corners[i].e + gap * 0.5f; }
        }
        spots.Add(Mod(at));
        return spots;
    }

    /// <summary>AI_REJOIN: run only the runs whose description starts with this, e.g. "f-01 at 601 m right", and trace them.</summary>
    static readonly string Only = Environment.GetEnvironmentVariable("AI_REJOIN");

    /// <summary>The car's sideslip, degrees: the angle between where it points and where it goes.</summary>
    static float Slip(StandInCar car)
    {
        if (car.Speed < 1f) return 0f;
        float cos = (car.Vx * car.Forward.x + car.Vz * car.Forward.z) / car.Speed;
        return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180.0 / Math.PI);
    }

    static float Mod(float d) => (float)(d - circuit.Lap * Math.Floor(d / circuit.Lap));

    /// <summary>Puts the car at a heading from the track direction, degrees, positive with the nose left, moving the way it faces.</summary>
    static void Aim(StandInCar car, float degrees, float speed)
    {
        int k = Math.Max(car.TrackIndex, 0);
        float track = (float)Math.Atan2(circuit.Forward[k].x, circuit.Forward[k].z);
        car.Heading = track - degrees * (float)Math.PI / 180f;
        car.Vx = car.Forward.x * speed;
        car.Vz = car.Forward.z * speed;
    }

    /// <summary>Traffic coming up behind a spot: one car at each gap, on the racing line at racing speed.</summary>
    static IEnumerable<Entry> Coming(float at, float[] gaps, int seed)
    {
        string[] models = { "c-18", "a-27", "e-07", "b-22", "f-03", "d-13" };
        for (int i = 0; i < gaps.Length; i++)
        {
            string m = models[(i + seed) % models.Length];
            int k = circuit.IndexOf(Mod(at - gaps[i]));
            yield return new Entry { Model = m, Start = Mod(at - gaps[i]), Lateral = LineFor(m).Lateral[k], Speed = 40f, Pace = 90f, Safety = 100f, Seed = 10 + i };
        }
    }

    /// <summary>Runs a race with the first car as the one rejoining, measuring it, until it is back racing (and a little after) or the time runs out.</summary>
    static RejoinRun WatchRejoin(Race race, float limit, bool verbose)
    {
        Racer me = race.Racers[0];
        var run = new RejoinRun { Name = race.Name(me) };
        float lastEdge = float.NaN;
        int contactsSeen = 0;
        float lastTouch = -99f;
        race.OnRejoin = (r, who, was) =>
        {
            if (who != me) return;
            Rejoin j = me.Rejoin;
            float edge = j.PastEdge;
            if (j.State == Rejoin.Stage.Rejoining && edge <= 0f)
            {
                run.WorstOnTarmac = Math.Max(run.WorstOnTarmac, Math.Abs(j.Heading));
                if (lastEdge > 0f) run.WorstEntry = Math.Max(run.WorstEntry, Math.Abs(j.Heading));
            }
            lastEdge = edge;
            run.SpunTo = Math.Max(run.SpunTo, Math.Abs(j.Heading));
            if (j.State == Rejoin.Stage.Waiting && was != Rejoin.Stage.Waiting)
            {
                run.Stopped = true;
                if (run.WaitStart < 0f) run.WaitStart = r.Time;
            }
            if (was == Rejoin.Stage.Waiting && j.State == Rejoin.Stage.Waiting && r.Time - lastTouch > 1f)
                run.WorstWaitSpeed = Math.Max(run.WorstWaitSpeed, me.Car.Speed);
            if (was == Rejoin.Stage.Waiting && j.State != Rejoin.Stage.Waiting)
            {
                run.WaitEnd = r.Time;
                // The gap, measured here from where the cars are, not from what the rejoin was told.
                foreach (Racer o in r.Racers)
                {
                    if (o == me) continue;
                    int k = Math.Max(o.Car.TrackIndex, 0);
                    float along = o.Car.Vx * circuit.Forward[k].x + o.Car.Vz * circuit.Forward[k].z;
                    float behind = Mod(me.Distance - o.Distance);
                    if (along < 1f || behind > circuit.Lap * 0.5f) continue;
                    float need = Math.Max(Rejoin.GapMetres, Rejoin.GapSeconds * along);
                    if (behind < need && run.GapBroken == null)
                        run.GapBroken = $"{run.Name} moved off at {Race.Clock(r.Time)} with {r.Name(o)} {behind:0} m behind at {along * 3.6f:0} km/h (needs {need:0} m)";
                }
            }
            if (was != Rejoin.Stage.Racing && j.State == Rejoin.Stage.Racing && run.BackAt < 0f)
            {
                run.Rejoined = true;
                run.BackAt = r.Time;
            }
        };
        while (race.Time < limit && (run.BackAt < 0f || race.Time < run.BackAt + 3f))
        {
            Rejoin.Stage before = me.Rejoin.State;
            int recoveries = me.Recoveries;
            race.Step();
            if (me.Recoveries > recoveries) run.Recovered = true;
            for (; contactsSeen < race.Contacts.Count; contactsSeen++)
            {
                Contact c = race.Contacts[contactsSeen];
                if (c.A != run.Name && c.B != run.Name) continue;
                lastTouch = race.Time;
                bool moving = before != Rejoin.Stage.Racing && before != Rejoin.Stage.Waiting && before != Rejoin.Stage.Stopping;
                if (moving) run.Touches.Add($"{Race.Clock(c.Time)} {c.A} and {c.B} at {c.Where:0} m, {c.Speed:0.0} m/s while {before}");
                else if (before != Rejoin.Stage.Racing) run.TouchesStopped++;
            }
            if (verbose && (int)Math.Round(race.Time / Race.Dt) % (Environment.GetEnvironmentVariable("AI_EVERY") is string ev ? int.Parse(ev) : 25) == 0)
                Console.WriteLine($"    {Race.Clock(race.Time)} {me.Rejoin.State,-9} {me.Rejoin.Cause,-4} heading {me.Rejoin.Heading,6:0} aim {me.Rejoin.Aim,4:0} cap {Math.Min(me.Pilot.SpeedCap, 99f) * 3.6f,4:0} shift {me.Pilot.Shift,5:0.0} at {me.Pilot.Index * circuit.Step,5:0} past edge {me.Rejoin.PastEdge,5:0.0} {me.Car.Speed * 3.6f,4:0} km/h yaw {me.Car.YawRate,5:0.00} slip {Slip(me.Car),4:0} thr {me.Car.LastThrottle:0.00} brk {me.Car.LastBrake:0.00} str {me.Car.LastSteer:0.00} lat {me.Car.Lateral,5:0.0}{(me.Car.Reverse ? " R" : "")} gap {(me.Rejoin.GapClear ? "clear" : "-")}");
        }
        return run;
    }

    static void RejoinOffTrack(bool verbose)
    {
        var problems = new List<string>();
        int runs = 0, rejoined = 0, touches = 0;
        float worst = 0f, worstEntry = 0f, slowest = 0f;
        string worstLine = "", worstEntryLine = "";
        string[] models = { "f-01", "c-19", "a-29" };
        // Angle towards the track (negative: away), speed and how far past the tarmac edge: running
        // wide at speed, crossing the grass towards the track with room to slow, and stopped or crawling
        // at any angle, even facing back.
        (float Towards, float Speed, float Out)[] cases =
        {
            (-30f, 35f, 4f), (-10f, 35f, 4f), (0f, 30f, 4f), (0f, 15f, 4f),
            (20f, 15f, 30f), (45f, 15f, 30f),
            (20f, 2f, 4f), (45f, 2f, 4f), (80f, 2f, 5f), (135f, 1f, 6f), (180f, 0f, 6f),
        };
        int n = 0;
        foreach (float at in RejoinSpots())
            foreach (string model in models)
                foreach (var (angle, speedIn, outBy) in cases)
                {
                    n++;
                    int side = n % 2 == 0 ? 1 : -1;
                    int k = circuit.IndexOf(at);
                    float lateral = side * (circuit.HalfWidth(k) + outBy);
                    var entries = new List<Entry> { new Entry { Model = model, Start = at, Lateral = lateral, Speed = speedIn, Pace = 90f, Safety = 100f, Seed = n } };
                    bool traffic = n % 2 == 1;
                    if (traffic) entries.AddRange(Coming(at, new[] { 120f, 260f, 420f }, n));
                    var race = new Race(circuit, entries, LineFor);
                    // Running wide, the car is a little over its own corner speed, not flat out mid-bend.
                    float speed = Math.Min(speedIn, 1.1f * race.Racers[0].Pilot.Profile.Speed[k]);
                    Aim(race.Racers[0].Car, -side * angle, speed);
                    string what = $"{model} at {at:0} m {(side > 0 ? "left" : "right")} {outBy:0} m out {angle:0}° {speed * 3.6f:0} km/h";
                    if (Only != null && !what.StartsWith(Only)) continue;
                    if (verbose) Console.WriteLine($"  {what}{(traffic ? ", traffic" : "")}");
                    RejoinRun run = WatchRejoin(race, 90f, Only != null);
                    runs++;
                    if (run.Rejoined) { rejoined++; slowest = Math.Max(slowest, run.BackAt); }
                    else problems.Add($"{what}: not back racing after 90 s ({race.Racers[0].Rejoin.State})");
                    if (run.Recovered) problems.Add($"{what}: put back on the track");
                    if (run.WorstOnTarmac > worst) { worst = run.WorstOnTarmac; worstLine = what; }
                    if (run.WorstEntry > worstEntry) { worstEntry = run.WorstEntry; worstEntryLine = what; }
                    touches += run.Touches.Count;
                    foreach (string t in run.Touches) problems.Add($"{what}: touched {t}");
                    if (verbose) Console.WriteLine($"    back racing {(run.Rejoined ? Race.Clock(run.BackAt) : "never")}, worst heading on the tarmac {run.WorstOnTarmac:0.0}°, at the edge {run.WorstEntry:0.0}°");
                }
        if (worstEntry > Rejoin.MaxEntryAngle) problems.Insert(0, $"crossed the tarmac edge at {worstEntry:0.0}° from the track: {worstEntryLine}");
        Report($"rejoin from off track ({runs} runs, {rejoined} back racing, slowest after {slowest:0} s, worst heading on the tarmac {worst:0.0}° ({worstLine}), crossing the edge {Math.Max(worstEntry, 0f):0.0}°, {touches} touches while rejoining)", problems);
    }

    static void SpunCarWaits(bool verbose)
    {
        var problems = new List<string>();
        int runs = 0, spun = 0, back = 0, stoppedTouches = 0;
        float worst = 0f, longestWait = 0f, slowest = 0f, worstWait = 0f;
        string[] models = { "f-01", "c-19", "a-29" };
        float[] spins = { 3f, -3.5f, 4f, -2.5f };
        float[][] gaps = { new[] { 150f, 300f, 450f, 700f }, new[] { 400f, 650f }, new float[0] };
        int n = 0;
        foreach (float at in RejoinSpots())
            foreach (string model in models)
            {
                n++;
                float yaw = spins[n % spins.Length];
                float[] coming = gaps[n % gaps.Length];
                int k = circuit.IndexOf(at);
                var entries = new List<Entry> { new Entry { Model = model, Start = at, Lateral = LineFor(model).Lateral[k], Speed = 28f, Pace = 90f, Safety = 100f, Seed = n } };
                entries.AddRange(Coming(at, coming, n));
                var race = new Race(circuit, entries, LineFor);
                race.Racers[0].Car.YawRate = yaw;
                string what = $"{model} at {at:0} m spin {yaw:0.0}";
                if (Only != null && !what.StartsWith(Only)) continue;
                if (verbose) Console.WriteLine($"  {model} at {at:0} m spinning at {yaw:0.0} rad/s, {coming.Length} cars coming");
                RejoinRun run = WatchRejoin(race, 120f, Only != null);
                runs++;
                if (!run.Stopped)
                {
                    if (verbose) Console.WriteLine($"    did not end up past {Rejoin.SpunAngle:0}° (at most {run.SpunTo:0}°), back racing {(run.Rejoined ? Race.Clock(run.BackAt) : "never")}");
                    if (run.Recovered) problems.Add($"{what}: put back on the track");
                    continue;
                }
                spun++;
                if (run.GapBroken != null) problems.Add(run.GapBroken);
                if (run.WorstWaitSpeed > Rejoin.StoppedSpeed + 0.3f) problems.Add($"{what}: crept at {run.WorstWaitSpeed:0.0} m/s while waiting");
                worstWait = Math.Max(worstWait, run.WorstWaitSpeed);
                if (run.WaitEnd > 0f) longestWait = Math.Max(longestWait, run.WaitEnd - run.WaitStart);
                if (run.Rejoined) { back++; slowest = Math.Max(slowest, run.BackAt); }
                else problems.Add($"{what}: not back racing after 120 s ({race.Racers[0].Rejoin.State})");
                if (run.Recovered) problems.Add($"{what}: put back on the track");
                if (run.WorstOnTarmac > Rejoin.MaxEntryAngle) problems.Add($"{what}: heading {run.WorstOnTarmac:0.0}° on the tarmac while rejoining");
                worst = Math.Max(worst, run.WorstOnTarmac);
                foreach (string t in run.Touches) problems.Add($"{what}: touched {t}");
                stoppedTouches += run.TouchesStopped;
                if (verbose) Console.WriteLine($"    stopped at {Race.Clock(run.WaitStart)}, moved off {(run.WaitEnd > 0f ? Race.Clock(run.WaitEnd) : "never")}, back racing {(run.Rejoined ? Race.Clock(run.BackAt) : "never")}, worst heading rejoining {run.WorstOnTarmac:0.0}°, {run.Touches.Count} touches moving, {run.TouchesStopped} while stopped");
            }
        if (spun < runs / 2) problems.Add($"only {spun} of {runs} spins ended past {Rejoin.SpunAngle:0}°, too few to check");
        Report($"spun car waits ({runs} spins, {spun} stopped past {Rejoin.SpunAngle:0}°, {back} back racing, longest wait {longestWait:0} s, slowest back after {slowest:0} s, fastest while waiting {worstWait:0.00} m/s, worst heading rejoining {worst:0.0}°, {stoppedTouches} touches by others while stopped)", problems);
    }

    /// <summary>
    /// Watches a pack race for rejoins: each one must follow the car leaving the track or losing
    /// control, measured here, never a car racing in traffic. Returns a description of each.
    /// </summary>
    /// <summary>How far two measures of the car's distance past the edge can differ, metres.</summary>
    const float EdgeTolerance = 0.25f;
    static readonly bool Episodes = Environment.GetEnvironmentVariable("AI_EPISODES") != null;

    static List<string> WatchPackRejoins(Race race, List<string> unexplained)
    {
        var seen = new List<string>();
        var lost = new Dictionary<Racer, float>();
        var episode = new Dictionary<Racer, float[]>();
        race.OnRejoin = (r, who, was) =>
        {
            StandInCar car = who.Car;
            float speed = car.Speed;
            float slip = 0f;
            if (speed > Rejoin.SpinSpeed) slip = Slip(car);
            if (speed > Rejoin.SpinSpeed && (slip > Rejoin.SpinSlip || Math.Abs(car.YawRate) > Rejoin.SpinYawRate)) lost[who] = r.Time;
            if (Episodes)
            {
                if (who.Rejoin.Busy)
                {
                    if (!episode.TryGetValue(who, out var e)) episode[who] = e = new float[6] { r.Time, 0f, 0f, car.Speed, 0f, who.Rejoin.Cause == "spun" ? 1f : 0f };
                    e[1] = Math.Max(e[1], who.Rejoin.PastEdge);
                    if (who.Rejoin.Yielding) e[2] += Race.Dt;
                    if (who.Rejoin.State != Rejoin.Stage.Rejoining) e[4] += Race.Dt;
                }
                else if (episode.TryGetValue(who, out var e))
                {
                    Console.WriteLine($"  {r.Name(who)} {(e[5] > 0f ? "spun" : "off")}->{who.Rejoin.Cause} from {Race.Clock(e[0])} for {r.Time - e[0]:0.0} s, out {e[1]:0.0} m, yielding {e[2]:0.0} s, stopped or turning {e[4]:0.0} s, at {e[3] * 3.6f:0} km/h");
                    episode.Remove(who);
                }
            }
            if (was != Rejoin.Stage.Racing || who.Rejoin.State == Rejoin.Stage.Racing) return;
            int k = Math.Max(car.TrackIndex, 0);
            // The car's own lateral is from its nearest sample and the rejoin's from its own search, so
            // a car riding the kerb's outer edge can be just past it to one and just short to the other.
            bool off = Math.Abs(car.Lateral) - circuit.HalfWidth(k) > KerbWidth - EdgeTolerance;
            bool spun = lost.TryGetValue(who, out float t) && r.Time - t < Rejoin.LostControlMemory;
            string line = $"{r.Name(who)} {who.Rejoin.Cause} at {Race.Clock(r.Time)} at {k * circuit.Step:0} m";
            seen.Add(line);
            if (!off && !spun) unexplained.Add(line + $", on the track (lateral {car.Lateral:0.0}, half {circuit.HalfWidth(k):0.0}, rejoin sees {who.Rejoin.PastEdge:0.00} past the edge) without a spin, {who.Craft.State}");
        };
        return seen;
    }
}
