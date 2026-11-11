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
    /// <summary>The throttle its pilot asked for this step, 0 to 1.</summary>
    public float Throttle;
    public int Id;
    /// <summary>Times the car was set back on its wheels.</summary>
    public int Righted;
    public Entry Entry;
    public StandInCar Car;
    public AiPilot Pilot;
    public Racecraft Craft;
    public readonly Rejoin Rejoin = new Rejoin();
    /// <summary>Spun and waiting on the track: other cars pass through it and ignore it.</summary>
    public bool Ghost;
    /// <summary>Seconds the rejoin has driven the car without getting it back to racing.</summary>
    public float RejoinTime;
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
    /// <summary>Last step's overtake offset and place across the track, for the etiquette checks.</summary>
    public float LastDev, LastPlace = float.NaN, LastLateral = float.NaN;
    public bool WasOff;
    /// <summary>This defending spell: the direction of its move, whether it turned back, and whether it broke the rule.</summary>
    public float SpellDir;
    public bool SpellBack, SpellBroken;
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
    /// <summary>One of the two cars was getting back into the race.</summary>
    public bool Rejoining;

    /// <summary>A push along n between two cars is nose to tail when it runs along both of them.</summary>
    public static bool NoseToTail(StandInCar a, StandInCar b, float nx, float nz) =>
        Math.Abs(a.Forward.x * nx + a.Forward.z * nz) > 0.8f && Math.Abs(b.Forward.x * nx + b.Forward.z * nz) > 0.8f;
}

sealed class Race
{
    static readonly bool Touches = Environment.GetEnvironmentVariable("AI_TOUCHES") != null;
    public readonly Dictionary<string, float> OffWhy = new Dictionary<string, float>();
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
    /// <summary>Called after each physics car's rejoin step, with the stage it was in before it.</summary>
    public Action<Race, Racer, Rejoin.Stage> OnRejoin;
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
    /// <summary>Cars passing through a spun car waiting on the track, a ghost: in the game they pass through it.</summary>
    public readonly List<Contact> SpunOverlaps = new List<Contact>();
    /// <summary>Times a car became a ghost, and steps spent as one.</summary>
    public int GhostSpells, GhostSteps;
    /// <summary>Steps a racing car spent under 5 m/s on the track, and of those, braking for the car ahead.</summary>
    public int QueueStopSteps, QueueBrakeSteps;
    static readonly bool StopLog = Environment.GetEnvironmentVariable("AI_STOPS") != null;
    float lodClock = 1f;
    /// <summary>Stopwatch ticks spent in racecraft and gliding for gliding cars.</summary>
    public long GlideTicks, ThinkTicks;
    static readonly bool DecisionStats = Environment.GetEnvironmentVariable("E1_STATS") == "1";
    static readonly Dictionary<string, long> Stats = new();
    static readonly double[] Gains = new double[4];
    static Race() { if (DecisionStats) AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var kv in Stats.OrderByDescending(k => k.Value)) Console.WriteLine($"STAT {kv.Key} {kv.Value}"); }; }
    static void Tally(PathChooser ch)
    {
        void Add(string k) => Stats[k] = (Stats.TryGetValue(k, out long n) ? n : 0) + 1;
        var best = ch.Chosen;
        Add("pick " + best.Pedal + (best.Rejected != null ? " fallback" : ""));
        if (best.Pedal == PathChooser.Pedal.Go) return;
        PathChooser.Candidate go = null;
        foreach (var k in ch.Candidates) if (k.Pedal == PathChooser.Pedal.Go && k.Rejected == null && (go == null || k.Score > go.Score)) go = k;
        if (go != null)
        {
            Add($"  {best.Pedal}: free go scored lower{(Math.Abs(go.Shift - best.Shift) < 0.1f ? " same lane" : "")}");
            string why = go.Gain - best.Gain < go.Risk - best.Risk && go.Risk - best.Risk > go.Stuck - best.Stuck ? "risk" : go.Stuck - best.Stuck > 0.01f ? "stuck" : "other";
            Add($"  {best.Pedal}: go lost on {why}");
            Gains[0] += go.Gain - best.Gain; Gains[1] += go.Risk - best.Risk; Gains[2] += go.Stuck - best.Stuck; Gains[3]++;
            if (Gains[3] % 100000 == 0) Console.WriteLine($"STATAVG gain {Gains[0] / Gains[3]:0.000} risk {Gains[1] / Gains[3]:0.000} stuck {Gains[2] / Gains[3]:0.000}");
            return;
        }
        foreach (var k in ch.Candidates) if (k.Pedal == PathChooser.Pedal.Go) Add($"  {best.Pedal}: go rejected {k.Rejected}{(k.Delay > 0 ? " delayed" : k.Shift == 0 ? " line" : "")}");
    }

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
                Ghost = r.Ghost && !r.Pilot.Gliding,
                Profile = r.Pilot.Profile,
                Planned = true,
                Aim = r.Craft.Target,
                Cap = r.Pilot.SpeedCap,
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

    /// <summary>Defending spells with a second move off the line, and the first of them.</summary>
    public int SecondMoves;
    public string SecondMoveLine = "";
    /// <summary>Times a car closed the room owed to a car alongside to under a car's width, and the first.</summary>
    public int RoomViolations;
    public string RoomViolationLine = "";
    readonly HashSet<(int, int)> squeezing = new HashSet<(int, int)>();
    /// <summary>Times a car went off at the bends at 1036 m and the hairpin at 1442 m.</summary>
    public int HairpinOffs;

    /// <summary>
    /// The F1 one-move rule, measured on the overtake offset while defending and not overtaking: after
    /// the first move turns back, any move away from the line breaks it. And never towards a car alongside.
    /// </summary>
    static readonly bool EtiquetteLog = Environment.GetEnvironmentVariable("AI_ETIQ") != null;

    void Etiquette(Racer r)
    {
        float dev = r.Pilot.Offset, place = r.Craft.Place;
        if (!r.Craft.Defending || r.Craft.Exempt) { r.SpellDir = 0f; r.SpellBack = false; r.SpellBroken = false; }
        else
        {
            float change = dev - r.LastDev;
            if (Math.Abs(change) > 1e-3f)
            {
                bool away = Math.Abs(dev) > Math.Abs(r.LastDev) || dev * r.LastDev < 0f;
                float dir = Math.Sign(change);
                if (r.SpellDir == 0f) { if (away) r.SpellDir = dir; }
                else if (dir != r.SpellDir) { r.SpellBack = true; r.SpellDir = dir; }
                if (r.SpellBack && away && EtiquetteLog)
                    Console.WriteLine($"ETIQ {Clock(Time)} {Name(r)} {Math.Max(r.Car.TrackIndex, 0) * circuit.Step:0} m dev {r.LastDev:0.000} -> {dev:0.000} side {r.Craft.SideSteering} state {r.Craft.State} brk {r.Craft.Braking} phase {r.Craft.MovePhase} rejoin {r.Rejoin.State} pit {r.Pilot.Index}");
                if (r.SpellBack && away && !r.SpellBroken && !r.Craft.SideSteering)
                {
                    r.SpellBroken = true;
                    if (SecondMoves++ == 0)
                        SecondMoveLine = $"{Name(r)} moved off its line a second time while defending, at {Clock(Time)} at {Math.Max(r.Car.TrackIndex, 0) * circuit.Step:0} m";
                }
            }
        }
        r.LastDev = dev;
        Room(r);
        r.LastLateral = r.Car.Lateral;
        r.LastPlace = r.Pilot.Index >= 0 ? place : float.NaN;
    }

    /// <summary>
    /// Room: a car alongside with its front axle past this car's rear axle is owed a car's width
    /// between this car and the track edge on its side. This car breaks it when it is the one that
    /// moved across to close the room.
    /// </summary>
    void Room(Racer r)
    {
        int k = r.Car.TrackIndex;
        if (k < 0 || float.IsNaN(r.LastLateral)) return;
        float axles = 2f * r.Pilot.Spec.HalfWheelbase;
        foreach (Racer o in Racers)
        {
            if (o == r || o.Pilot.Gliding || float.IsNaN(o.LastLateral)) continue;
            float gap = o.Distance - r.Distance;
            gap -= circuit.Lap * (float)Math.Round(gap / circuit.Lap);
            var key = (r.Id, o.Id);
            if (!(gap > -axles && gap < Racecraft.CarLength)) { squeezing.Remove(key); continue; }
            int ko = Math.Max(o.Car.TrackIndex, 0);
            if (Math.Abs(o.Car.Lateral) > circuit.HalfWidth(ko)) { squeezing.Remove(key); continue; }
            float side = Math.Sign(o.Car.Lateral - r.Car.Lateral);
            float room = circuit.HalfWidth(k) - (side * r.Car.Lateral + Racecraft.CarWidth / 2f);
            float mine = side * (r.Car.Lateral - r.LastLateral), theirs = -side * (o.Car.Lateral - o.LastLateral);
            bool closing = room < Racecraft.CarWidth && mine > 0.25f * Dt && mine >= theirs;
            if (!closing) { squeezing.Remove(key); continue; }
            if (!squeezing.Add(key)) continue;
            if (EtiquetteLog) Console.WriteLine($"ROOM {Clock(Time)} {Name(r)} lat {r.Car.Lateral:0.00} place {r.Craft.Place:0.00} line {r.Pilot.Line.Lateral[Math.Max(r.Pilot.Index,0)]:0.00} off {r.Pilot.Offset:0.00} spd {r.Car.Speed * 3.6f:0} | {Name(o)} lat {o.Car.Lateral:0.00} gap {gap:0.0} hw {circuit.HalfWidth(k):0.0} room {room:0.00} mine {mine:0.000} theirs {theirs:0.000} side {r.Craft.SideSteering}");
            if (RoomViolations++ == 0)
                RoomViolationLine = $"{Name(r)} squeezed {Name(o)} to {Math.Max(room, 0f):0.0} m of room, at {Clock(Time)} at {k * circuit.Step:0} m";
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

    static readonly bool RearLog = Environment.GetEnvironmentVariable("RE_LOG") != null;
    readonly Dictionary<Racer, Queue<string>> history = new Dictionary<Racer, Queue<string>>();
    int historyTick;

    /// <summary>RE_LOG: what each car was doing over its last 1.5 s, every 0.1 s, printed for the car behind at a rear-end.</summary>
    void Remember()
    {
        if (!RearLog || historyTick++ % 10 != 0) return;
        foreach (Racer r in Racers)
        {
            if (!history.TryGetValue(r, out var q)) history[r] = q = new Queue<string>();
            var pc = r.Craft.Chooser;
            q.Enqueue($"{Time,7:0.0} {Math.Max(r.Car.TrackIndex, 0) * circuit.Step,5:0} m {r.Car.Speed * 3.6f,3:0} km/h lat {r.Car.Lateral,5:0.0} thr {r.Throttle:0.00} cap {Math.Min(r.Pilot.SpeedCap, 99f) * 3.6f,3:0} {(r.Craft.Braking ? "BRK for " + r.Craft.BrakeFor + " gap " + r.Craft.BrakeGap.ToString("0.0") : "")} [{pc?.Reason} {pc?.Chosen?.Pedal} {pc?.Chosen?.Shift:0.0} {pc?.Chosen?.Rejected}@{pc?.Chosen?.BrokeBy}]");
            while (q.Count > 15) q.Dequeue();
        }
    }

    public void Step()
    {
        Snapshot();
        Remember();
        lodClock += Dt;
        if (Physics != null && lodClock >= 0.25f)
        {
            lodClock = 0f;
            foreach (Racer r in Racers)
            {
                bool want = Physics(r);
                if (want && r.Pilot.Gliding) Wake(r);
                else if (!want && !r.Pilot.Gliding && !r.Rejoin.Busy && !r.Rejoin.Ghost) Sleep(r);
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
            if (DecisionStats && Time > 20f && r.Craft.Chooser?.Chosen != null && !r.Rejoin.Busy) Tally(r.Craft.Chooser);
            if (Time > 20f && !r.Rejoin.Busy && r.Car.Speed < 5f && r.Pilot.Index >= 0)
            {
                QueueStopSteps++;
                if (r.Craft.Braking) QueueBrakeSteps++;
                if (StopLog && r.Craft.Braking) Console.WriteLine($"STOP {Clock(Time)} {Name(r)} at {r.Pilot.Index * circuit.Step:0} m {r.Car.Speed * 3.6f:0} km/h");
            }
            Etiquette(r);
            r.Rejoin.Prepare(r.Pilot);
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
            // On its roof or side the car can't drive; after a moment it is set back on its wheels.
            if (r.Rejoin.Upturned(r.Car.UpY, Dt)) { r.Car.UpY = 1f; r.Righted++; }
            AiPilot.Controls c = r.Pilot.Drive(r.Car.Position, r.Car.Forward, r.Car.Velocity, r.Car.YawRate);
            Rejoin.Stage was = r.Rejoin.State;
            c = r.Rejoin.Apply(r.Pilot, c, r.Car.Position, r.Car.Forward, r.Car.Velocity, r.Car.YawRate, field[i], sorted, Dt);
            bool wasGhost = r.Ghost;
            r.Rejoin.Haunt(r.Pilot, field[i], sorted, r.Car.Speed, Dt);
            r.Ghost = r.Rejoin.Ghost;
            if (r.Ghost && !wasGhost) GhostSpells++;
            if (r.Ghost) GhostSteps++;
            OnRejoin?.Invoke(this, r, was);
            Feathering(r, c);
            OpenRoom(r);
            Queued(r);
            r.Car.Reverse = c.Reverse;
            if (r.Car.UpY < Rejoin.FlippedUp) c = new AiPilot.Controls { Brake = 1f };
            r.Throttle = c.Throttle;
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
            bool isOff = off > KerbWidth;
            float at = k * circuit.Step;
            if (isOff && !r.WasOff && (at > 1000f && at < 1110f || at > 1430f && at < 1500f)) HairpinOffs++;
            r.WasOff = isOff;
            if (off > KerbWidth && r.SinceMistake < 2f) { }
            else if (off > KerbWidth)
            {
                if (r.OffSteps++ == 0) { r.OffTime = Time; r.OffWhere = k * circuit.Step; }
                if (Environment.GetEnvironmentVariable("AI_OFFWHY") != null) OffWhy[$"{r.Rejoin.State}{(r.Rejoin.Yielding ? " yielding" : "")}"] = OffWhy.GetValueOrDefault($"{r.Rejoin.State}{(r.Rejoin.Yielding ? " yielding" : "")}") + Dt;
            }
            // As AiDriver: stuck, or the rejoin hasn't got the car back to racing after a long while,
            // and only once the gap behind is clear.
            r.StuckTime = r.Car.Speed < 2f && !r.Rejoin.Busy ? r.StuckTime + Dt : 0f;
            if (r.Rejoin.Trying) r.RejoinTime += Dt;
            else if (!r.Rejoin.Busy) r.RejoinTime = 0f;
            if ((r.StuckTime > 4f || r.RejoinTime > Rejoin.GiveUpAfter || r.Rejoin.Stranded) && r.Rejoin.GapClear)
            {
                r.Recoveries++;
                r.Car.PlaceAt(k, 0f);
                r.Pilot.Lost();
                r.Rejoin.Reset();
                r.StuckTime = r.RejoinTime = 0f;
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
        r.Rejoin.Reset();   // it finds the track again from the pilot's place when it wakes
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
                if (Racers[i].Ghost || Racers[j].Ghost)
                {
                    float closing = Math.Max(-((b.Vx - a.Vx) * nx + (b.Vz - a.Vz) * nz), 0f);
                    if (touching.Add(key))
                        SpunOverlaps.Add(new Contact { Time = Time, Speed = closing, Where = Math.Max(a.TrackIndex, 0) * circuit.Step, A = Name(Racers[i]), B = Name(Racers[j]) });
                    continue;
                }
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
                bool fresh = touching.Add(key);
                if (fresh && Touches && (Racers[i].Rejoin.Busy || Racers[j].Rejoin.Busy))
                {
                    string Who(Racer x) => $"{Name(x)} {x.Rejoin.State}{(x.Rejoin.Yielding ? " yielding" : "")} {x.Rejoin.PastEdge:0.0} past, {x.Car.Speed * 3.6f:0} km/h, heading {x.Rejoin.Heading:0}, dist {x.Distance:0}";
                    Console.WriteLine($"  touch {Clock(Time)} {Math.Max(-vn, 0f):0.0} m/s at {Math.Max(a.TrackIndex, 0) * circuit.Step:0} m: {Who(Racers[i])} | {Who(Racers[j])}");
                }
                if (fresh && RearLog && Contact.NoseToTail(a, b, nx, nz) && Math.Max(-vn, 0f) > 1f)
                {
                    Racer back = Racers[i].Distance < Racers[j].Distance ? Racers[i] : Racers[j], front = back == Racers[i] ? Racers[j] : Racers[i];
                    Console.WriteLine($"REAR {Clock(Time)} {Name(back)} into {Name(front)} {Math.Max(-vn, 0f):0.0} m/s");
                    foreach (Racer x in new[] { back, front })
                        if (history.TryGetValue(x, out var q)) foreach (string h in q) Console.WriteLine($"  {(x == back ? "B" : "F")} {h}");
                }
                if (fresh)
                    Contacts.Add(new Contact { Time = Time, Speed = Math.Max(-vn, 0f), RearEnd = Contact.NoseToTail(a, b, nx, nz), Where = Math.Max(a.TrackIndex, 0) * circuit.Step, A = Name(Racers[i]), B = Name(Racers[j]), Rejoining = Racers[i].Rejoin.Busy || Racers[j].Rejoin.Busy });
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
