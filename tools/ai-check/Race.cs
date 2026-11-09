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
    public readonly Dictionary<Racecraft.Mode, float> ModeTime = new Dictionary<Racecraft.Mode, float>();
}

sealed class Contact
{
    public float Time, Speed, Where;
    /// <summary>Within sight of the camera: under 500 m from the watched car.</summary>
    public bool Seen;
    public string A, B;
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
    public float Time;
    /// <summary>Closest any car got nose to tail behind a car in its path, m, and who.</summary>
    public float ClosestFollow = float.MaxValue;

    readonly Circuit circuit;
    readonly Dictionary<(int, int), int> order = new Dictionary<(int, int), int>();
    readonly HashSet<(int, int)> touching = new HashSet<(int, int)>();
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
                Craft = new Racecraft(e.Seed),
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
            // Between samples too, as RaceCar does, so gaps don't jump in 2 m steps.
            float along = (r.Car.X - circuit.Centre[k].x) * f.x + (r.Car.Z - circuit.Centre[k].z) * f.z;
            r.Distance = r.Laps * circuit.Lap + k * circuit.Step + along;
            field.Add(new TrafficCar
            {
                Id = r.Id,
                Distance = r.Distance,
                Lateral = r.Car.Lateral,
                Speed = r.Car.Vx * f.x + r.Car.Vz * f.z,
                LapPace = pace,
                Braking = r.Car.Spec.BrakeDecel * 9.81f * AiPilot.LimitFor(r.Pilot.Pace),
                GivingWay = r.Craft.State == Racecraft.Mode.GivingWay,
            });
        }
        sorted.Set(field, circuit.Lap);
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
            r.Car.DragScale = Racecraft.Slipstream(circuit, field[i], field);
            r.Pilot.Towed = r.Car.DragScale < 0.97f;
            AiPilot.Controls c = r.Pilot.Drive(r.Car.Position, r.Car.Forward, r.Car.Velocity, r.Car.YawRate);
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
            r.Distance = r.Laps * circuit.Lap + k * circuit.Step;

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
                if (Passes.Count < 400)
                    Passes.Add($"{Clock(Time)} {Name(by)} passes {Name(of)} at {Math.Max(by.Car.TrackIndex, 0) * circuit.Step:0} m{(by.Distance - of.Distance > circuit.Lap * 0.5f ? " (lapping)" : "")}");
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
                    Contacts.Add(new Contact { Time = Time, Speed = Math.Max(-vn, 0f), Where = Math.Max(a.TrackIndex, 0) * circuit.Step, A = Name(Racers[i]), B = Name(Racers[j]) });
            }
    }

    /// <summary>Separating axis test of the two car boxes: the shortest way to push b out of a.</summary>
    static bool Overlap(StandInCar a, StandInCar b, out float nx, out float nz, out float depth)
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

    public string Name(Racer r) => $"{r.Entry.Model}#{r.Id}";

    public static string Clock(float s) => $"{(int)(s / 60f)}:{s % 60f:00.0}";
}
