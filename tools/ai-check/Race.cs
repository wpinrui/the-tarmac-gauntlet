// Runs several AI cars together in StandInCar, with racecraft, and bodies that bump off each other.
// The bump is a plain push apart along the shortest way out plus an impulse, which is enough to
// count contacts and how hard they were, not to model a crash.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using UnityEngine;

sealed class Entry
{
    public string Model;
    public float Pace = 90f, Consistency = 90f, Safety = 100f;
    /// <summary>Lap distance and lateral offset to start from, and speed (0 for a standing start).</summary>
    public float Start, Lateral, Speed;
    public int Seed = 1;
}

sealed class Racer
{
    public int Id;
    public Entry Entry;
    public StandInCar Car;
    public AiPilot Pilot;
    public Racecraft Craft;
    public int Laps;
    public int Last;
    public float StuckTime;
    public int Recoveries;
    public int OffSteps;
    /// <summary>Steps off the track after an overcooked corner, before the car is back on: the driver's own mistake.</summary>
    public int MistakeOffSteps;
    public float SinceMistake = float.MaxValue;
    /// <summary>When and where the car first left the track, or -1.</summary>
    public float OffTime = -1f, OffWhere;
    public float Distance;
    /// <summary>How far the car moved along the track in the last step.</summary>
    public float Moved;
    public readonly List<float> LapTimes = new List<float>();
    public float LapStart = -1f;
    public Racecraft.Mode LastMode;
    /// <summary>Time since the gliding car last stepped.</summary>
    public float GlideClock;
    /// <summary>Switches between physics and gliding.</summary>
    public int Handovers;
    /// <summary>Time since the car last woke from a glide, and how far it strayed from its path in the 2 s after.</summary>
    public float SinceWake = float.MaxValue;
    public float WorstAfterWake;
    public float WorstSpeedJump;
    /// <summary>Times the offset racecraft steers for turned back by 2 m or more: out and back in is two.</summary>
    public int Swings;
    /// <summary>Times the car went back to the line, or off it, within a second of the opposite move.</summary>
    public int Dithers;
    public bool OnLine = true;
    public float SinceFlip = 99f;
    public float SwingExt;
    public int SwingDir;
    /// <summary>Time since the car last had a car within 15 m ahead in its lane.</summary>
    public float SinceQueued = 99f;
    public readonly Dictionary<Racecraft.Mode, float> ModeTime = new Dictionary<Racecraft.Mode, float>();
}

sealed class Contact
{
    public float Time, Speed, Where;
    /// <summary>Within sight of the camera: under 500 m from the watched car.</summary>
    public bool Seen;
    public string A, B;
    /// <summary>One car's nose into the other's tail: the push between them runs along both cars.</summary>
    public bool RearEnd;

    /// <summary>A push along n between two cars is nose to tail when it runs along both of them.</summary>
    public static bool NoseToTail(StandInCar a, StandInCar b, float nx, float nz) =>
        Math.Abs(a.Forward.x * nx + a.Forward.z * nz) > 0.8f && Math.Abs(b.Forward.x * nx + b.Forward.z * nz) > 0.8f;
}

sealed class Race
{
    public const float Dt = 0.01f;
    const float HalfLength = 2.3f, HalfWidth = 0.95f;   // the car's box collider
    const float KerbWidth = 1.5f;

    public readonly List<Racer> Racers = new List<Racer>();
    public readonly List<Contact> Contacts = new List<Contact>();
    /// <summary>One line per change of order between two cars running within 100 m of each other.</summary>
    public readonly List<string> Passes = new List<string>();
    public int PassCount;
    /// <summary>Passes of a car queued behind another by a car 1% or more slower, and the first of them.</summary>
    public int QueuePassed;
    public string QueuePassedLine = "";
    public float Time;
    /// <summary>Closest any car got nose to tail behind a car in its path, m, and who.</summary>
    public float ClosestFollow = float.MaxValue;

    readonly Circuit circuit;
    readonly Dictionary<(int, int), int> order = new Dictionary<(int, int), int>();
    readonly HashSet<(int, int)> touching = new HashSet<(int, int)>();
    readonly Dictionary<(int, int), int> stuckBehind = new Dictionary<(int, int), int>();
    /// <summary>Most corners a car more than one class quicker started behind the same slower car, and when.</summary>
    public int WorstStuck;
    public string WorstStuckLine = "";
    /// <summary>The same for the fastest car behind the slowest one.</summary>
    public int FastestStuck;
    /// <summary>Most corners a car 1% or more quicker started stuck behind a slower one.</summary>
    public int QuickStuck;
    public string QuickStuckLine = "";
    /// <summary>Most corners a car 2% or more quicker started stuck behind a slower car of its own class.</summary>
    public int ClassQueue;
    public string ClassQueueLine = "";
    public string FastestStuckLine = "";
    Racer Fastest => Racers.OrderBy(r => r.Pilot.Profile.LapTime).First();
    Racer Slowest => Racers.OrderBy(r => r.Pilot.Profile.LapTime).Last();
    readonly List<TrafficCar> field = new List<TrafficCar>();
    readonly TrafficField sorted = new TrafficField();
    public Action<Race> Watch;
    /// <summary>Gliding cars step this often, seconds. The game uses 0.01 at normal speed and 0.05 in fast forward.</summary>
    public float GlideStep = Dt;
    /// <summary>Each gliding car's step instead of <see cref="GlideStep"/>, if set.</summary>
    public Func<Racer, float> GlideStepFor;
    /// <summary>The car the camera is on, for which overlaps are in sight.</summary>
    public Racer Camera;
    /// <summary>Which cars run physics, checked four times a second as the game does. Null: all of them.</summary>
    public Func<Racer, bool> Physics;
    /// <summary>Gliding cars overlapping another car: in the game they pass through it.</summary>
    public readonly List<Contact> GhostOverlaps = new List<Contact>();
    float lodClock = 1f;
    /// <summary>Stopwatch ticks spent in racecraft and gliding for gliding cars.</summary>
    public long GlideTicks, ThinkTicks;

    public Race(Circuit circuit, IEnumerable<Entry> entries, Func<string, RacingLine> lineFor)
    {
        this.circuit = circuit;
        int id = 0;
        foreach (Entry e in entries)
        {
            CarSpec spec = CarSpec.For(e.Model);
            RacingLine line = lineFor(e.Model);
            var r = new Racer
            {
                Id = id++,
                Entry = e,
                Car = new StandInCar(spec, line),
                Pilot = new AiPilot(line, spec, e.Pace, e.Consistency, e.Safety, e.Seed),
                Craft = new Racecraft(),
            };
            int k = circuit.IndexOf(e.Start);
            r.Car.PlaceAt(k, e.Lateral);
            if (e.Speed > 0f) r.Car.Launch(e.Speed);
            r.Last = k;
            r.Laps = e.Start > circuit.Lap * 0.5f ? -1 : 0;
            Racers.Add(r);
        }
        Snapshot();
    }


    void Snapshot()
    {
        field.Clear();
        foreach (Racer r in Racers)
        {
            float pace = r.Pilot.Profile.LapTime;
            int k = Math.Max(r.Car.TrackIndex, 0);
            Vector3 f = circuit.Forward[k];
            r.Distance = r.Laps * circuit.Lap + circuit.DistanceAt(k, r.Car.Position);
            field.Add(new TrafficCar
            {
                Id = r.Id,
                Distance = r.Distance,
                Lateral = r.Car.Lateral,
                Speed = r.Car.Vx * f.x + r.Car.Vz * f.z,
                LapPace = pace,
                Braking = r.Car.Spec.BrakeDecel * 9.81f * AiPilot.LimitFor(r.Pilot.Pace),
                Across = r.Car.Vx * circuit.Left[k].x + r.Car.Vz * circuit.Left[k].z,
                Yaw = (float)Math.Atan2(r.Car.Forward.z, r.Car.Forward.x),
            });
        }
        sorted.Set(field, circuit.Lap);
    }

    static readonly int FlipWatch = int.TryParse(System.Environment.GetEnvironmentVariable("AI_FLIPS"), out int fw) ? fw : -1;
    /// <summary>Steps on a straight with a car within two seconds ahead in the way, and those of them on part throttle held back by it.</summary>
    public int FollowSteps, FeatherSteps;
    static readonly float FeatherFrom = float.TryParse(Environment.GetEnvironmentVariable("AI_FEATHER_FROM"), out float ff) ? ff : 0f;
    static readonly bool FeatherLog = Environment.GetEnvironmentVariable("AI_FEATHER") != null;

    void Feathering(Racer r, AiPilot.Controls c)
    {
        int k = r.Car.TrackIndex;
        if (k < 0 || Math.Abs(r.Pilot.Line.Curvature[k]) > 0.003f || r.Car.Speed < 20f || Time < FeatherFrom) return;
        bool behind = false;
        foreach (Racer o in Racers)
        {
            if (o == r) continue;
            float gap = o.Distance - r.Distance;
            gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
            if (gap > 0f && gap < 2f * r.Car.Speed && Math.Abs(o.Car.Lateral - r.Car.Lateral) < 2.5f) { behind = true; break; }
        }
        if (!behind) return;
        FollowSteps++;
        // Lifting or braking for the car ahead, rather than flat out on a straight.
        if (r.Pilot.HeldBy > 0.5f && r.Pilot.SpeedCap < r.Car.Speed + 0.5f && c.Throttle < 0.95f)
        {
            FeatherSteps++;
            if (FeatherLog && FeatherSteps % 50 == 0)
                Console.WriteLine($"FEATHER {Clock(Time)} {Name(r)} {k * circuit.Step:0} m {r.Car.Speed * 3.6f:0} kmh thr {c.Throttle:0.00} cap {r.Pilot.SpeedCap * 3.6f:0} {r.Craft.State} off {r.Pilot.Offset:0.0} lat {r.Car.Lateral:0.0} towed {r.Pilot.Towed}");
        }
    }

    /// <summary>Whether a car sits in a queue: a car within 15 m ahead in its lane.</summary>
    void Queued(Racer r)
    {
        r.SinceQueued += Dt;
        foreach (Racer o in Racers)
        {
            if (o == r) continue;
            float gap = o.Distance - r.Distance;
            gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
            if (gap > 0f && gap < 15f && Math.Abs(o.Car.Lateral - r.Car.Lateral) < 2.5f) { r.SinceQueued = 0f; return; }
        }
    }

    /// <summary>Steps in a corner held back by a car close ahead, and those of them with a clear lane beside it.</summary>
    public int CornerHeldSteps, OpenRoomSteps;
    public string OpenRoomLine = "";

    /// <summary>Held up behind a car in a corner while the track beside it is empty for 10 m behind to 30 m ahead.</summary>
    void OpenRoom(Racer r)
    {
        int k = r.Car.TrackIndex;
        if (k < 0 || Math.Abs(r.Pilot.Line.Curvature[k]) < 0.003f || r.Car.Speed < 10f) return;
        if (!(r.Pilot.HeldBy > 0.5f && r.Pilot.SpeedCap < r.Car.Speed + 0.5f)) return;
        Racer ahead = null;
        foreach (Racer o in Racers)
        {
            if (o == r) continue;
            float gap = o.Distance - r.Distance;
            gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
            if (gap > 0f && gap < 25f && Math.Abs(o.Car.Lateral - r.Car.Lateral) < 2.5f) { ahead = o; break; }
        }
        if (ahead == null) return;
        CornerHeldSteps++;
        float half = circuit.HalfWidth(k) - 1.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            float lane = ahead.Car.Lateral + side * 3.5f;
            if (Math.Abs(lane) > half) continue;
            bool clear = true;
            foreach (Racer o in Racers)
            {
                if (o == r || o == ahead) continue;
                float gap = o.Distance - r.Distance;
                gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
                if (gap > -10f && gap < 30f && Math.Abs(o.Car.Lateral - lane) < 2.8f) { clear = false; break; }
            }
            if (!clear) continue;
            OpenRoomSteps++;
            if (OpenRoomSteps % 100 == 1)
                OpenRoomLine = $"{Name(r)} held behind {Name(ahead)} in the corner at {k * circuit.Step:0} m at {Clock(Time)} with the {(side * Math.Sign(r.Pilot.Line.Curvature[k]) > 0 ? "inside" : "outside")} open";
            if (FeatherLog && OpenRoomSteps % 20 == 1) Console.WriteLine("OPEN " + OpenRoomLine + $" {r.Craft.State} off {r.Pilot.Offset:0.0}");
            return;
        }
    }

    public void Step()
    {
        Snapshot();
        lodClock += Dt;
        if (Physics != null && lodClock >= 0.25f)
        {
            lodClock = 0f;
            foreach (Racer r in Racers)
            {
                bool want = Physics(r);
                if (want && r.Pilot.Gliding) Wake(r);
                else if (!want && !r.Pilot.Gliding) Sleep(r);
            }
        }
        for (int i = 0; i < Racers.Count; i++)
        {
            Racer r = Racers[i];
            if (r.Pilot.Gliding)
            {
                r.GlideClock += Dt;
                if (r.GlideClock >= (GlideStepFor != null ? GlideStepFor(r) : GlideStep) - 1e-5f)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    r.Craft.Think(r.Pilot, field[i], sorted, r.GlideClock);
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    r.Pilot.Glide(r.GlideClock);
                    GlideTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    ThinkTicks += t1 - t0;
                    r.GlideClock = 0f;
                }
                PoseGlider(r);
                r.ModeTime[r.Craft.State] = (r.ModeTime.TryGetValue(r.Craft.State, out float g) ? g : 0f) + Dt;
                continue;
            }
            r.Craft.Think(r.Pilot, field[i], sorted, Dt);
            {
                bool onLine = r.Craft.State == Racecraft.Mode.Racing;
                r.SinceFlip += Dt;
                if (onLine != r.OnLine && r.Id == FlipWatch) Console.WriteLine($"FLIP {Clock(Time)} {r.Car.TrackIndex * circuit.Step:0} m {r.Car.Speed * 3.6f:0} kmh {(onLine ? "->Racing" : "->Overtaking")} after {r.SinceFlip:0.0}s lat {r.Car.Lateral:0.0} off {r.Pilot.Offset:0.0}");
                if (onLine != r.OnLine) { if (r.SinceFlip < 1f) r.Dithers++; r.SinceFlip = 0f; r.OnLine = onLine; }
                float g = r.Craft.Target;
                if (r.SwingDir > 0 && g > r.SwingExt || r.SwingDir < 0 && g < r.SwingExt) r.SwingExt = g;
                else if (r.SwingDir >= 0 && g < r.SwingExt - 2f) { if (r.SwingDir > 0) r.Swings++; r.SwingDir = -1; r.SwingExt = g; }
                else if (r.SwingDir <= 0 && g > r.SwingExt + 2f) { if (r.SwingDir < 0) r.Swings++; r.SwingDir = 1; r.SwingExt = g; }
            }
            r.Car.DragScale = Racecraft.Slipstream(circuit, field[i], field);
            r.Pilot.Towed = r.Car.DragScale < 0.97f;
            AiPilot.Controls c = r.Pilot.Drive(r.Car.Position, r.Car.Forward, r.Car.Velocity, r.Car.YawRate);
            Feathering(r, c);
            OpenRoom(r);
            Queued(r);
            r.Car.Step(Dt, c.Throttle, c.Brake, c.Steer);
            r.ModeTime[r.Craft.State] = (r.ModeTime.TryGetValue(r.Craft.State, out float t) ? t : 0f) + Dt;
        }
        Collide();
        Time += Dt;

        foreach (Racer r in Racers)
        {
            int k = r.Car.TrackIndex;
            if (r.Last > circuit.Count - 50 && k < 50)
            {
                r.Laps++;
                if (r.LapStart >= 0f) r.LapTimes.Add(Time - r.LapStart);
                r.LapStart = Time;
            }
            else if (r.Last < 50 && k > circuit.Count - 50) r.Laps--;
            r.Last = k;
            float before = r.Distance;
            r.Distance = r.Laps * circuit.Lap + circuit.DistanceAt(k, r.Car.Position);
            r.Moved = r.Distance - before;

            if (r.Pilot.Gliding) continue;
            if (r.SinceWake < 2f)
            {
                r.SinceWake += Dt;
                r.WorstAfterWake = Math.Max(r.WorstAfterWake, Math.Abs(r.Pilot.LateralError));
            }
            float off = Math.Abs(r.Car.Lateral) - circuit.HalfWidth(k);
            // A mistake lasts until the car has been back on the track for a couple of seconds.
            r.SinceMistake = r.Pilot.Overcooking ? 0f : off > KerbWidth && r.SinceMistake < 2f ? 0f : r.SinceMistake + Dt;
            if (off > KerbWidth && r.SinceMistake < 2f) r.MistakeOffSteps++;
            else if (off > KerbWidth)
            {
                if (r.OffSteps++ == 0) { r.OffTime = Time; r.OffWhere = k * circuit.Step; }
            }
            float facing = r.Car.Forward.x * circuit.Forward[k].x + r.Car.Forward.z * circuit.Forward[k].z;
            r.StuckTime = r.Car.Speed < 2f || facing < -0.2f ? r.StuckTime + Dt : 0f;
            if (r.StuckTime > 4f)
            {
                r.Recoveries++;
                r.Car.PlaceAt(k, 0f);
                r.Pilot.Lost();
                r.StuckTime = 0f;
            }
        }

        // Changes of order between cars close together on the road, once a car is clear ahead.
        foreach (Racer a in Racers)
            foreach (Racer b in Racers)
            {
                if (a.Id >= b.Id) continue;
                // On the road, not in the race: a car being lapped is passed too.
                float now = a.Distance - b.Distance;
                now = now - circuit.Lap * (float)Math.Round(now / circuit.Lap);
                if (Math.Abs(now) < 2f * HalfLength + 2f) continue;
                int sign = Math.Sign(now);
                if (!order.TryGetValue((a.Id, b.Id), out int was)) { order[(a.Id, b.Id)] = sign; continue; }
                if (sign == was) continue;
                order[(a.Id, b.Id)] = sign;
                if (Math.Abs(now) > 100f) continue;
                Racer by = now > 0f ? a : b, of = now > 0f ? b : a;
                PassCount++;
                if (by.Pilot.Profile.LapTime > of.Pilot.Profile.LapTime * 1.01f && of.SinceQueued < 1f && by.Distance - of.Distance < circuit.Lap * 0.5f)
                {
                    if (QueuePassed++ == 0)
                        QueuePassedLine = $"{Name(by)} is {100f * (by.Pilot.Profile.LapTime / of.Pilot.Profile.LapTime - 1f):0.0}% slower and passed {Name(of)} while it queued behind a car, at {Clock(Time)} at {Math.Max(by.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
                if (Passes.Count < 400)
                    Passes.Add($"{Clock(Time)} {Name(by)} passes {Name(of)} at {Math.Max(by.Car.TrackIndex, 0) * circuit.Step:0} m{(by.Distance - of.Distance > circuit.Lap * 0.5f ? " (lapping)" : "")}");
            }

        // A car quicker held up behind a slower one: corners it starts while still behind.
        foreach (Racer fast in Racers)
            foreach (Racer slow in Racers)
            {
                bool twoClasses = Class(slow) - Class(fast) >= 2;
                bool quicker = fast.Pilot.Profile.LapTime < slow.Pilot.Profile.LapTime * 0.99f;
                if (!twoClasses && !quicker) continue;
                float gap = slow.Distance - fast.Distance;
                gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
                var key = (fast.Id, slow.Id);
                if (gap <= 0f || gap > 40f) { stuckBehind.Remove(key); continue; }
                float was = fast.Distance - fast.Moved, now = fast.Distance;
                int corners = stuckBehind.TryGetValue(key, out int n) ? n : 0;
                foreach (CornerData c in circuit.Data.corners)
                {
                    float at = c.s + circuit.Lap * (float)Math.Floor(now / circuit.Lap);
                    if (at > now) at -= circuit.Lap;
                    if (at > was && at <= now) corners++;
                }
                stuckBehind[key] = corners;
                if (fast == Fastest && slow == Slowest && corners > FastestStuck)
                {
                    FastestStuck = corners;
                    FastestStuckLine = $"{Name(fast)}, the fastest car, started {corners} corners behind {Name(slow)}, the slowest, the last at {Clock(Time)} at {Math.Max(fast.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
                if (quicker && corners > QuickStuck)
                {
                    QuickStuck = corners;
                    QuickStuckLine = $"{Name(fast)} is {100f * (1f - fast.Pilot.Profile.LapTime / slow.Pilot.Profile.LapTime):0.0}% quicker and started {corners} corners behind {Name(slow)}, the last at {Clock(Time)} at {Math.Max(fast.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
                if (Class(fast) == Class(slow) && fast.Pilot.Profile.LapTime < slow.Pilot.Profile.LapTime * 0.98f && corners > ClassQueue)
                {
                    ClassQueue = corners;
                    ClassQueueLine = $"{Name(fast)} is {100f * (1f - fast.Pilot.Profile.LapTime / slow.Pilot.Profile.LapTime):0.0}% quicker and started {corners} corners behind {Name(slow)} of its own class, the last at {Clock(Time)} at {Math.Max(fast.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
                if (!twoClasses) continue;
                if (corners > WorstStuck)
                {
                    WorstStuck = corners;
                    WorstStuckLine = $"{Name(fast)} started {corners} corners behind {Name(slow)}, the last at {Clock(Time)} at {Math.Max(fast.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
            }

        // Closest nose to tail gap behind a car in the same lane.
        foreach (Racer a in Racers)
            foreach (Racer b in Racers)
            {
                if (a == b) continue;
                float gap = b.Distance - a.Distance;
                if (gap <= 0f || gap > 30f) continue;
                if (Math.Abs(a.Car.Lateral - b.Car.Lateral) > 1.5f) continue;
                ClosestFollow = Math.Min(ClosestFollow, gap - 2f * HalfLength);
            }
        Watch?.Invoke(this);
    }

    /// <summary>The game's PhysicsRange taking a car off physics: it glides on from where it is.</summary>
    void Sleep(Racer r)
    {
        int k = Math.Max(r.Car.TrackIndex, 0);
        Vector3 f = circuit.Forward[k];
        r.Pilot.StartGlide(r.Car.Position, r.Car.Vx * f.x + r.Car.Vz * f.z);
        r.GlideClock = (r.Id % Math.Max(1, (int)Math.Round(GlideStep / Dt))) * Dt;   // spread the cars' steps out, as the game does
        r.Handovers++;
    }

    /// <summary>Back to physics where the gliding car is drawn, moving along its path.</summary>
    void Wake(Racer r)
    {
        AiPilot p = r.Pilot;
        float before = p.GlideSpeed;
        p.GlidePose(p.GlideSpeed * r.GlideClock, out Vector3 at, out Vector3 forward, out _);
        r.Car.Wake(at, forward, p.GlideSpeed, p.GlideSpeed * p.GlideCurvature, p.GlideSteer);
        p.StopGlide();
        r.Handovers++;
        r.SinceWake = 0f;
        r.WorstSpeedJump = Math.Max(r.WorstSpeedJump, Math.Abs(r.Car.Speed - before));
    }

    void PoseGlider(Racer r)
    {
        AiPilot p = r.Pilot;
        p.GlidePose(p.GlideSpeed * r.GlideClock, out Vector3 at, out Vector3 forward, out _);
        r.Car.Pose(at, forward, p.GlideSpeed, p.GlideSpeed * p.GlideCurvature);
    }

    /// <summary>Pushes overlapping cars apart and trades the speed they hit with, counting each new contact.</summary>
    void Collide()
    {
        for (int i = 0; i < Racers.Count; i++)
            for (int j = i + 1; j < Racers.Count; j++)
            {
                StandInCar a = Racers[i].Car, b = Racers[j].Car;
                float dx = b.X - a.X, dz = b.Z - a.Z;
                float nx = 0f, nz = 0f, depth = 0f;
                bool hit = dx * dx + dz * dz < 30f && Overlap(a, b, out nx, out nz, out depth);
                var key = (i, j);
                if (!hit) { touching.Remove(key); continue; }
                if (Racers[i].Pilot.Gliding || Racers[j].Pilot.Gliding)
                {
                    // No bodies to bump: in the game a gliding car has no collider.
                    float closing = Math.Max(-((b.Vx - a.Vx) * nx + (b.Vz - a.Vz) * nz), 0f);
                    if (touching.Add(key))
                        GhostOverlaps.Add(new Contact { Time = Time, Speed = closing, Seen = Camera != null && Apart(a, Camera.Car) < 500f, Where = Math.Max(a.TrackIndex, 0) * circuit.Step, A = Name(Racers[i]), B = Name(Racers[j]) });
                    continue;
                }
                // n points from a to b.
                a.X -= nx * depth * 0.5f; a.Z -= nz * depth * 0.5f;
                b.X += nx * depth * 0.5f; b.Z += nz * depth * 0.5f;
                float vn = (b.Vx - a.Vx) * nx + (b.Vz - a.Vz) * nz;
                if (vn < 0f)
                {
                    float ma = a.Spec.Mass, mb = b.Spec.Mass;
                    float jn = -1.1f * vn / (1f / ma + 1f / mb);
                    a.Vx -= jn * nx / ma; a.Vz -= jn * nz / ma;
                    b.Vx += jn * nx / mb; b.Vz += jn * nz / mb;
                }
                if (touching.Add(key))
                    Contacts.Add(new Contact { Time = Time, Speed = Math.Max(-vn, 0f), RearEnd = Contact.NoseToTail(a, b, nx, nz), Where = Math.Max(a.TrackIndex, 0) * circuit.Step, A = Name(Racers[i]), B = Name(Racers[j]) });
            }
    }

    /// <summary>Separating axis test of the two car boxes: the shortest way to push b out of a.</summary>
    internal static bool Overlap(StandInCar a, StandInCar b, out float nx, out float nz, out float depth)
    {
        nx = nz = 0f;
        depth = float.MaxValue;
        float dx = b.X - a.X, dz = b.Z - a.Z;
        Vector3[] axes = { a.Forward, a.Right, b.Forward, b.Right };
        foreach (Vector3 ax in axes)
        {
            float ra = HalfLength * Math.Abs(Dot(a.Forward, ax)) + HalfWidth * Math.Abs(Dot(a.Right, ax));
            float rb = HalfLength * Math.Abs(Dot(b.Forward, ax)) + HalfWidth * Math.Abs(Dot(b.Right, ax));
            float d = dx * ax.x + dz * ax.z;
            float over = ra + rb - Math.Abs(d);
            if (over <= 0f) return false;
            if (over < depth) { depth = over; nx = ax.x * Math.Sign(d); nz = ax.z * Math.Sign(d); }
        }
        return true;
    }

    static float Apart(StandInCar a, StandInCar b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.z * b.z;

    static int Class(Racer r) => r.Entry.Model[0] - 'a';

    public string Name(Racer r) => $"{r.Entry.Model}#{r.Id}";

    public static string Clock(float s) => $"{(int)(s / 60f)}:{s % 60f:00.0}";
}
