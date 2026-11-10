// Checks for getting back into the race after leaving the track or spinning (Rejoin.cs).
//
//   rejoin from off track   cars put on the grass beside the track, at speed and at angles from 80° towards
//                           the track to 30° away from it, at corner exits and on a straight, alone and with
//                           traffic coming: each gets back to racing on its line, crosses the tarmac edge
//                           at no more than Rejoin.MaxEntryAngle from the track direction, and touches no
//                           car while rejoining. The worst heading on the tarmac afterwards is reported:
//                           a car still turning in a bend at its grip limit can run past the limit there
//   spun car turns round   cars sent into a spin on the track with traffic coming: each stops, starts to
//                           turn round within StandLimit of starting to stop, a ghost while it does on the track,
//                           rejoins within the heading limit, and touches no car once it moves again
//   out of the gravel       cars stopped in a gravel trap: each drives out or, stranded, is put back on the
//                           track, racing again within GravelLimit
//   spun cars recover       pack races on many grids: no spun car stands for longer than SpunMoveLimit, no
//                           rejoin of any kind lasts longer than that, and no car sits facing the wrong way
//                           without a rejoin, no pile-up and no long ghost (as in pack races)
//   flipped cars            cars on their roof with traffic coming: each is a ghost while upside down, is set
//                           back on its wheels after Rejoin.FlippedTime, gets back to racing and touches no car
//   pack races (in RacePack) no car starts a rejoin unless it has left the track or lost control; no pile-up
//                           (PileCars or more cars stopped together on the track, two of them solid); and no
//                           car still a ghost GhostTail after it is back racing
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
        public bool GhostWhenTurning;
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
            if (j.State == Rejoin.Stage.Stopping && was != Rejoin.Stage.Stopping)
            {
                run.Stopped = true;
                if (run.WaitStart < 0f) run.WaitStart = r.Time;
            }
            // Stopped and moving off again: how long it stood, and whether it was a ghost then.
            if (was == Rejoin.Stage.Stopping && j.State != Rejoin.Stage.Stopping && run.WaitEnd < 0f)
            {
                run.WaitEnd = r.Time;
                run.GhostWhenTurning = me.Ghost || j.PastEdge > Rejoin.OffTrackMargin;
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
            if (me.Recoveries > recoveries)
            {
                // Put back on the track: racing again from here.
                run.Recovered = true;
                if (run.BackAt < 0f) { run.Rejoined = true; run.BackAt = race.Time; }
            }
            for (; contactsSeen < race.Contacts.Count; contactsSeen++)
            {
                Contact c = race.Contacts[contactsSeen];
                if (c.A != run.Name && c.B != run.Name) continue;
                lastTouch = race.Time;
                bool moving = before != Rejoin.Stage.Racing && before != Rejoin.Stage.Stopping;
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

    /// <summary>
    /// Longest a car stopped in a gravel trap may take to be back racing, driven out or put back,
    /// seconds: the last resort's wait and a few seconds for the gap. A small car can take most of it
    /// creeping out at full throttle.
    /// </summary>
    const float GravelLimit = Rejoin.GiveUpAfter + 5f;

    /// <summary>
    /// Cars stopped in a gravel trap, 3 m past the tarmac edge and beyond, at any angle: deep gravel
    /// holds a small car whichever way it drives, so each must either drive out or, stranded, be put
    /// back on the track, and be racing again within <see cref="GravelLimit"/>, never sit there.
    /// </summary>
    static void OutOfGravel(bool verbose)
    {
        var problems = new List<string>();
        int runs = 0, drove = 0, put = 0;
        float slowest = 0f;
        string slowestLine = "";
        string[] models = { "f-01", "c-19", "a-29" };
        (float Towards, float Speed, float Out)[] cases = { (0f, 0f, 6f), (45f, 1f, 6f), (90f, 0f, 7f), (180f, 0f, 6f), (-30f, 2f, 8f) };
        int n = 0;
        foreach (float at in RejoinSpots())
            foreach (string model in models)
                foreach (var (angle, speed, outBy) in cases)
                {
                    n++;
                    int side = n % 2 == 0 ? 1 : -1;
                    int k = circuit.IndexOf(at);
                    var entries = new List<Entry> { new Entry { Model = model, Start = at, Lateral = side * (circuit.HalfWidth(k) + outBy), Speed = speed, Pace = 90f, Safety = 100f, Seed = n } };
                    bool traffic = n % 2 == 1;
                    if (traffic) entries.AddRange(Coming(at, new[] { 120f, 260f, 420f }, n));
                    var race = new Race(circuit, entries, LineFor);
                    race.Racers[0].Car.GravelFrom = 3f;
                    Aim(race.Racers[0].Car, -side * angle, speed);
                    string what = $"{model} at {at:0} m {(side > 0 ? "left" : "right")} {outBy:0} m out in gravel {angle:0}°";
                    if (Only != null && !what.StartsWith(Only)) continue;
                    if (verbose) Console.WriteLine($"  {what}{(traffic ? ", traffic" : "")}");
                    RejoinRun run = WatchRejoin(race, 90f, Only != null);
                    runs++;
                    float back = run.Rejoined ? run.BackAt : float.PositiveInfinity;
                    if (run.Recovered) put++;
                    else if (run.Rejoined) drove++;
                    if (back > slowest) { slowest = back; slowestLine = what; }
                    if (back > GravelLimit) problems.Add($"{what}: {(run.Rejoined ? $"back racing only after {back:0} s" : $"not back racing after 90 s ({race.Racers[0].Rejoin.State})")}");
                    foreach (string t in run.Touches) problems.Add($"{what}: touched {t}");
                    if (verbose) Console.WriteLine($"    {(run.Recovered ? "put back" : run.Rejoined ? "drove out" : "still there")}, back racing {(run.Rejoined ? Race.Clock(run.BackAt) : "never")}");
                }
        Report($"out of the gravel ({runs} runs, {drove} drove out, {put} put back when stranded, slowest back racing after {slowest:0} s ({slowestLine}))", problems);
    }

    /// <summary>Longest from a spun car starting to stop, sliding included, to it turning round, seconds.</summary>
    const float StandLimit = 6f;

    static void SpunCarTurnsRound(bool verbose)
    {
        var problems = new List<string>();
        int runs = 0, spun = 0, back = 0, stoppedTouches = 0;
        float worst = 0f, longestWait = 0f, slowest = 0f;
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
                float stood = (run.WaitEnd > 0f ? run.WaitEnd : race.Time) - run.WaitStart;
                longestWait = Math.Max(longestWait, stood);
                if (stood > StandLimit) problems.Add($"{what}: stood {stood:0.0} s before turning round");
                if (run.WaitEnd > 0f && !run.GhostWhenTurning) problems.Add($"{what}: turned round on the track without being a ghost");
                if (run.Rejoined) { back++; slowest = Math.Max(slowest, run.BackAt); }
                else problems.Add($"{what}: not back racing after 120 s ({race.Racers[0].Rejoin.State})");
                if (run.Recovered) problems.Add($"{what}: put back on the track");
                if (run.WorstOnTarmac > Rejoin.MaxEntryAngle) problems.Add($"{what}: heading {run.WorstOnTarmac:0.0}° on the tarmac while rejoining");
                worst = Math.Max(worst, run.WorstOnTarmac);
                foreach (string t in run.Touches) problems.Add($"{what}: touched {t}");
                stoppedTouches += run.TouchesStopped;
                if (verbose) Console.WriteLine($"    stopping from {Race.Clock(run.WaitStart)}, turning round {(run.WaitEnd > 0f ? Race.Clock(run.WaitEnd) : "never")}, back racing {(run.Rejoined ? Race.Clock(run.BackAt) : "never")}, worst heading rejoining {run.WorstOnTarmac:0.0}°, {run.Touches.Count} touches moving, {run.TouchesStopped} while stopping");
            }
        if (spun < runs / 2) problems.Add($"only {spun} of {runs} spins ended past {Rejoin.SpunAngle:0}°, too few to check");
        Report($"spun car turns round ({runs} spins, {spun} stopped past {Rejoin.SpunAngle:0}°, {back} back racing, longest from stopping to turning {longestWait:0.0} s, slowest back after {slowest:0} s, worst heading rejoining {worst:0.0}°, {stoppedTouches} touches while stopping)", problems);
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

    /// <summary>Longest a spun car may stand waiting before it starts to turn round or rejoin, seconds.</summary>
    const float SpunMoveLimit = 60f;
    static readonly string SpunTrace = Environment.GetEnvironmentVariable("AI_SPUN_TRACE");
    /// <summary>Longest a car may sit facing the wrong way, slow, without a rejoin under way, seconds.</summary>
    const float BackwardsLimit = 5f;

    /// <summary>
    /// Pack races on many grids: every car that spins and stops must start back towards its line
    /// (turning round or rejoining) within <see cref="SpunMoveLimit"/>, not wait for ever, and not be
    /// put back on the track by the last resort.
    /// </summary>
    static void SpunCarsRecover(IEnumerable<int> seeds, bool verbose)
    {
        var problems = new List<string>();
        int races = 0, spins = 0, pileUps = 0, ghostGroups = 0;
        float ghostTime = 0f, longestTail = 0f;
        float longest = 0f;
        string longestLine = "";
        foreach (int seed in seeds)
        {
            var since = new Dictionary<Racer, float>();
            var where = new Dictionary<Racer, string>();
            var resets = new Dictionary<Racer, int>();
            var backwards = new Dictionary<Racer, float>();
            var busySince = new Dictionary<Racer, float>();
            var longRun = new HashSet<Racer>();
            var reported = new HashSet<Racer>();
            var piles = new PileWatch($"seed {seed} ");
            Race race = RunRace(Pack(false, seed), r => { piles.Step(r); return r.Racers.Max(c => c.Laps) >= 4; }, 60f * 20f, r =>
            {
                foreach (Racer x in r.Racers) resets[x] = 0;
                r.OnRejoin = (rr, who, was) =>
                {
                    Rejoin.Stage now = who.Rejoin.State;
                    if (SpunTrace != null && rr.Name(who) == SpunTrace && who.Rejoin.Busy && (int)Math.Round(rr.Time / Race.Dt) % (Environment.GetEnvironmentVariable("AI_EVERY") is string ev ? int.Parse(ev) : 50) == 0)
                        Console.WriteLine($"    {Race.Clock(rr.Time)} {now} {who.Rejoin.Cause} heading {who.Rejoin.Heading:0} past {who.Rejoin.PastEdge:0.0} lat {who.Car.Lateral:0.0} {who.Car.Speed * 3.6f:0} km/h thr {who.Car.LastThrottle:0.00} brk {who.Car.LastBrake:0.00} str {who.Car.LastSteer:0.00}{(who.Car.Reverse ? " R" : "")} {who.Car.Surface}");
                    // Any rejoin, off or spun, that runs on: cars waiting on each other, or on a gap that never comes.
                    if (who.Rejoin.Busy)
                    {
                        if (!busySince.ContainsKey(who)) busySince[who] = rr.Time;
                        else if (rr.Time - busySince[who] > SpunMoveLimit && longRun.Add(who))
                            problems.Add($"seed {seed} {rr.Name(who)} rejoining ({who.Rejoin.Cause}) for {SpunMoveLimit:0} s from {Race.Clock(busySince[who])}, now {now}{(who.Rejoin.Yielding ? " yielding" : "")} {who.Rejoin.PastEdge:0.0} m past the edge at {who.Car.Speed * 3.6f:0} km/h");
                    }
                    else { busySince.Remove(who); longRun.Remove(who); }
                    // Facing the wrong way and slow without the rejoin knowing: nothing will turn it round.
                    bool lostBackwards = now == Rejoin.Stage.Racing && Math.Abs(who.Rejoin.Heading) > Rejoin.SpunAngle && who.Car.Speed < Rejoin.SpinSpeed;
                    if (lostBackwards)
                    {
                        if (!backwards.ContainsKey(who)) backwards[who] = rr.Time;
                        else if (rr.Time - backwards[who] > BackwardsLimit && !reported.Contains(who))
                        {
                            reported.Add(who);
                            problems.Add($"seed {seed} {rr.Name(who)} facing {who.Rejoin.Heading:0}° from the track at {who.Car.Speed * 3.6f:0} km/h for {BackwardsLimit:0} s without starting a rejoin, from {Race.Clock(backwards[who])} at {Math.Max(who.Car.TrackIndex, 0) * circuit.Step:0} m ({who.Craft.State}, {who.Rejoin.PastEdge:0.0} m past the edge)");
                        }
                    }
                    else { backwards.Remove(who); reported.Remove(who); }
                    bool standing = now == Rejoin.Stage.Stopping;
                    if (standing && !since.ContainsKey(who))
                    {
                        since[who] = rr.Time;
                        where[who] = $"seed {seed} {rr.Name(who)} at {Race.Clock(rr.Time)} at {Math.Max(who.Car.TrackIndex, 0) * circuit.Step:0} m";
                        resets[who] = who.Recoveries;
                        spins++;
                    }
                    else if (!standing && since.TryGetValue(who, out float t))
                    {
                        float wait = rr.Time - t;
                        string line = $"{where[who]}: waited {wait:0.0} s, then {(who.Recoveries > resets[who] ? "put back on the track" : now.ToString())}";
                        if (verbose) Console.WriteLine("  " + line);
                        if (wait > longest) { longest = wait; longestLine = line; }
                        if (who.Recoveries > resets[who]) problems.Add(line);
                        else if (wait > SpunMoveLimit) problems.Add(line);
                        since.Remove(who);
                    }
                };
            });
            races++;
            problems.AddRange(piles.PileUps);
            problems.AddRange(piles.LongGhosts);
            pileUps += piles.PileUps.Count;
            ghostGroups += piles.GhostGroups;
            ghostTime += piles.GhostSteps;
            longestTail = Math.Max(longestTail, piles.LongestTail);
            foreach (var w in since)
            {
                float wait = race.Time - w.Value;
                string line = $"{where[w.Key]}: still {w.Key.Rejoin.State} after {wait:0.0} s at the end (gap {(w.Key.Rejoin.GapClear ? "clear" : "not clear")})";
                if (verbose) Console.WriteLine("  " + line);
                if (wait > longest) { longest = wait; longestLine = line; }
                if (wait > SpunMoveLimit) problems.Add(line);
            }
        }
        Report($"spun cars recover ({races} pack races, {spins} cars stopped after a spin, {pileUps} pile-ups, {ghostGroups} groups of spun ghosts at one spot, {ghostTime:0} s of ghosts, longest a ghost after getting back {longestTail:0.0} s, longest standing {longest:0} s{(longestLine != "" ? $": {longestLine}" : "")})", problems);
    }

    /// <summary>A pile-up: this many cars or more at one spot on the track, all at a crawl or stopped.</summary>
    const int PileCars = 3;
    /// <summary>Cars closer than this along the track, metres, are at one spot.</summary>
    const float PileGap = 12f;
    /// <summary>Slower than this, m/s, a car is crawling or stopped.</summary>
    const float PileSpeed = 3f;

    /// <summary>
    /// Watches a race for pile-ups (<see cref="PileCars"/> or more cars at one spot on the track, all
    /// crawling or stopped, at least two of them solid, after the start) and for ghosts that last: a car must not still be a ghost
    /// <see cref="GhostTail"/> after it is back racing.
    /// </summary>
    sealed class PileWatch
    {
        public readonly List<string> PileUps = new List<string>();
        public readonly List<string> LongGhosts = new List<string>();
        public float GhostSteps, LongestTail;
        /// <summary>Times three or more cars, all but one ghosts, were stopped together: spun cars recovering at one corner.</summary>
        public int GhostGroups;
        readonly Dictionary<Racer, float> tail = new Dictionary<Racer, float>();
        readonly HashSet<Racer> flagged = new HashSet<Racer>();
        float lastPile = -100f;
        readonly string label;

        public PileWatch(string label) { this.label = label; }

        public void Step(Race r)
        {
            foreach (Racer x in r.Racers)
            {
                if (x.Rejoin.Ghost) GhostSteps += Race.Dt;
                bool back = x.Rejoin.Ghost && !x.Rejoin.Busy && !x.Rejoin.Flipped;
                float t = back ? (tail.TryGetValue(x, out float was) ? was : 0f) + Race.Dt : 0f;
                tail[x] = t;
                LongestTail = Math.Max(LongestTail, t);
                if (t > GhostTail && flagged.Add(x))
                    LongGhosts.Add($"{label}{r.Name(x)} still a ghost {GhostTail:0} s after getting back, at {Race.Clock(r.Time)} at {Math.Max(x.Car.TrackIndex, 0) * circuit.Step:0} m, {x.Car.Speed * 3.6f:0} km/h");
                if (t == 0f) flagged.Remove(x);
            }
            // After the start, a few times a second.
            if (r.Time < 15f || (int)Math.Round(r.Time / Race.Dt) % 10 != 0 || r.Time - lastPile < 5f) return;
            var slow = r.Racers.Where(x => x.Car.Speed < PileSpeed && x.Car.TrackIndex >= 0
                    && Math.Abs(x.Car.Lateral) - circuit.HalfWidth(x.Car.TrackIndex) < KerbWidth)
                .OrderBy(x => Mod(x.Distance)).ToList();
            for (int i = 0; i < slow.Count; i++)
            {
                int n = 1;
                while (n < slow.Count && Mod(slow[(i + n) % slow.Count].Distance - slow[(i + n - 1) % slow.Count].Distance) < PileGap) n++;
                if (n < PileCars) continue;
                var group = Enumerable.Range(0, n).Select(j => slow[(i + j) % slow.Count]).ToList();
                // Ghosts pass through each other, so spun cars recovering at one corner can't pile up;
                // it takes two solid cars stopped together.
                if (group.Count(x => !x.Rejoin.Ghost) < 2) { GhostGroups++; lastPile = r.Time; return; }
                lastPile = r.Time;
                PileUps.Add($"{label}{n} cars at {Math.Max(slow[i].Car.TrackIndex, 0) * circuit.Step:0} m at {Race.Clock(r.Time)}: " + string.Join(", ", Enumerable.Range(0, n).Select(j => slow[(i + j) % slow.Count]).Select(x => $"{r.Name(x)} {x.Rejoin.State}{(x.Rejoin.Ghost ? " ghost" : "")} {x.Car.Speed * 3.6f:0} km/h")));
                return;
            }
        }
    }

    /// <summary>Longest a car may stay a ghost once back racing, seconds: the hold, and time to clear a car still overlapping.</summary>
    const float GhostTail = Rejoin.GhostHold + 4f;

    /// <summary>
    /// Cars on their roof, at a stop on the racing line with traffic coming: each is a ghost while
    /// it is upside down, is set back on its wheels after Rejoin.FlippedTime, gets back to racing,
    /// and touches no car.
    /// </summary>
    static void FlippedCars(bool verbose)
    {
        var problems = new List<string>();
        int runs = 0, back = 0;
        float slowest = 0f;
        string[] models = { "f-01", "c-19", "a-29" };
        int n = 0;
        foreach (float at in RejoinSpots())
            foreach (string model in models)
            {
                n++;
                int k = circuit.IndexOf(at);
                var entries = new List<Entry> { new Entry { Model = model, Start = at, Lateral = LineFor(model).Lateral[k], Speed = 0f, Pace = 90f, Safety = 100f, Seed = n } };
                entries.AddRange(Coming(at, new[] { 60f, 150f, 300f }, n));
                var race = new Race(circuit, entries, LineFor);
                Racer me = race.Racers[0];
                me.Car.UpY = -1f;
                // Face it the wrong way on odd runs: righted, it turns round as a spun car does.
                if (n % 2 == 1) Aim(me.Car, 180f, 0f);
                string what = $"{model} at {at:0} m on its roof{(n % 2 == 1 ? " facing back" : "")}";
                if (Only != null && !what.StartsWith(Only)) continue;
                runs++;
                bool ghostWhileFlipped = true;
                float righted = -1f, backAt = -1f;
                int contacts = 0;
                var watch = new PileWatch("");
                while (race.Time < 90f && (backAt < 0f || race.Time < backAt + 3f))
                {
                    int seen = race.Contacts.Count;
                    race.Step();
                    watch.Step(race);
                    for (int c = seen; c < race.Contacts.Count; c++)
                        if (race.Contacts[c].A == race.Name(me) || race.Contacts[c].B == race.Name(me)) contacts++;
                    if (me.Car.UpY < Rejoin.FlippedUp && !me.Rejoin.Ghost) ghostWhileFlipped = false;
                    if (righted < 0f && me.Righted > 0) righted = race.Time;
                    if (righted >= 0f && backAt < 0f && !me.Rejoin.Busy && !me.Rejoin.Ghost) backAt = race.Time;
                }
                if (!ghostWhileFlipped) problems.Add($"{what}: not a ghost while on its roof");
                if (righted < 0f) problems.Add($"{what}: never set back on its wheels");
                else if (righted > Rejoin.FlippedTime + 0.5f) problems.Add($"{what}: set back on its wheels only after {righted:0.0} s");
                if (backAt < 0f) problems.Add($"{what}: not back racing after 90 s ({me.Rejoin.State})");
                else { back++; slowest = Math.Max(slowest, backAt); }
                if (contacts > 0) problems.Add($"{what}: {contacts} contacts");
                problems.AddRange(watch.PileUps);
                problems.AddRange(watch.LongGhosts);
                if (verbose) Console.WriteLine($"  {what}: righted at {(righted >= 0f ? Race.Clock(righted) : "never")}, back racing {(backAt >= 0f ? Race.Clock(backAt) : "never")}, {contacts} contacts");
            }
        Report($"flipped cars ({runs} on their roof, {back} back racing, slowest after {slowest:0} s)", problems);
    }
}
