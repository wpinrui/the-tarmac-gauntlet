// A race session in StandInCar run the way the raceday scene runs it: the rolling start behind the
// pace car, then racing with fuel, tyre wear and AI pit stops. RaceCar, RaceStart and AiDriver's
// glue is copied here; the deciding is done by the same AiRacer, PitPlan, RollingStart and PitLane.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using UnityEngine;

sealed class SessionCar
{
    public int Id, Slot, Garage;
    public string Model;
    public StandInCar Car;
    public AiRacer Racer;
    public int Laps, Last = -1;
    public float Distance;
    public bool InPitLane;
    public float StuckTime;
    public int Recoveries;
    public float OffTime = -1f, OffWhere, OffSeconds;
    /// <summary>Fastest the car went inside the limiter zone, m/s.</summary>
    public float LimitZoneTop;
    public PitPlan.Stage Stage;
    public readonly List<string> Stops = new List<string>();
    public float StopStart;
    public float WorstBoxMiss;
    public int Rejoins;
    public bool RanDry;
    public float LeftWith = -1f;
    public float FuelAtGreen = -1f, WearAtGreen;
    public float LapStart = -1f;
    public readonly List<float> LapTimes = new List<float>();
    public readonly List<(float Fuel, float Wear)> LapUse = new List<(float, float)>();
    public float LapFuel = -1f, LapWear;
    /// <summary>Time since the gliding car last stepped, and how many times it switched to or from gliding.</summary>
    public float GlideClock;
    public int Handovers;
    /// <summary>Pit stops booked while gliding.</summary>
    public int GlidingStops;
}

sealed class Session
{
    public const float Dt = 0.01f;
    const float KerbWidth = 1.5f;

    public readonly List<SessionCar> Cars = new List<SessionCar>();
    public readonly List<Contact> Contacts = new List<Contact>();
    public readonly RollingStart Start;
    public StandInCar PaceCar;
    public bool PaceCarGone;
    public float Time, GreenTime = -1f;
    public float PaceCarTop;
    /// <summary>Most cars stood still in their box at once, and most in the pit lane at once.</summary>
    public int MostInLane;
    /// <summary>Which cars run physics, checked four times a second as PhysicsRange does. Null: all of them.</summary>
    public Func<SessionCar, bool> Physics;
    /// <summary>With Physics: how far each car is from the camera, nearest first up to MostCars, as PhysicsRange.</summary>
    public Func<SessionCar, float> Apart;
    public int MostCars = 24;
    /// <summary>Each gliding car's step, seconds.</summary>
    public Func<SessionCar, float> GlideStep = _ => 0.05f;
    /// <summary>Gliding cars overlapping another car: in the game they pass through it.</summary>
    public int GhostOverlaps;
    /// <summary>Steps racing cars spent under 5 m/s on the track after the green, and of those, braking for the car ahead.</summary>
    public int CrawlSteps, CrawlBrakeSteps;
    static readonly bool StopLog = Environment.GetEnvironmentVariable("AI_STOPS") != null;
    /// <summary>Most cars on physics at once.</summary>
    public int MostOnPhysics;
    float lodClock = 1f;
    readonly TrafficField sorted = new TrafficField();

    readonly Circuit circuit;
    readonly PitLane pits;
    readonly RacingLine[] columns;
    readonly Dictionary<int, AiPilot> formation = new Dictionary<int, AiPilot>();
    readonly HashSet<(int, int)> touching = new HashSet<(int, int)>();
    readonly List<TrafficCar> traffic = new List<TrafficCar>();
    readonly List<TrafficCar> field = new List<TrafficCar>();
    readonly List<CarState> all = new List<CarState>();
    AiPilot paceDriver;
    bool paceOnRoute;
    int paceLaps, paceLast = -1;
    float paceDistance;

    static readonly int trace = int.TryParse(Environment.GetEnvironmentVariable("PIT_TRACE"), out int t) ? t : -1;

    static readonly int TraceEvery = int.TryParse(Environment.GetEnvironmentVariable("PIT_EVERY"), out int te) ? te : 50;

    public bool Racing => Start == null || Start.State == RollingStart.Phase.Green;

    /// <summary>
    /// The field from its grid places, pole first, each car with its share of a tank. With
    /// <c>rolling</c> it starts behind the pace car; otherwise it races from the grid at once.
    /// </summary>
    public Session(Circuit circuit, RacingLine minimum, IList<(string Model, float Fuel)> entries, bool rolling, Func<string, RacingLine> lineFor, int seed = 11)
    {
        this.circuit = circuit;
        pits = new PitLane(circuit);
        var rng = new System.Random(seed);
        for (int i = 0; i < entries.Count; i++)
        {
            string model = entries[i].Model;
            CarSpec spec = CarSpec.For(model);
            RacingLine line = lineFor(model);
            float pace = 70f + 30f * (float)rng.NextDouble();
            float consistency = 60f + 40f * (float)rng.NextDouble();
            float safety = 80f + 20f * (float)rng.NextDouble();
            float smoothness = 30f + 60f * (float)rng.NextDouble();
            var supplies = Consumables.For(spec, new SpeedProfile(minimum, spec, AiPilot.LimitFor(pace)), smoothness);
            supplies.Fuel = supplies.Capacity * entries[i].Fuel;
            int garage = i % pits.Garages;
            var pilot = new AiPilot(line, spec, pace, consistency, safety, i + 1);
            var car = new SessionCar
            {
                Id = i,
                Slot = i,
                Garage = garage,
                Model = model,
                Car = new StandInCar(spec, line) { Supplies = supplies, Pits = pits },
                Racer = new AiRacer(pilot, new Racecraft(), supplies, new PitPlan(pits, garage, supplies), i + 1),
            };
            GridPlace(i, out float d, out float lateral);
            int k = circuit.IndexOf(d - 2.6f);
            car.Car.PlaceAt(k, lateral);
            car.Last = k;
            car.Laps = k * circuit.Step > circuit.Lap * 0.5f ? -1 : 0;
            Cars.Add(car);
        }
        if (!rolling) return;
        var specs = new List<CarSpec>();
        foreach (SessionCar c in Cars) specs.Add(c.Car.Spec);
        Start = new RollingStart(circuit, specs);
        columns = new[] { new RacingLine(circuit, RollingStart.ColumnOf(0)), new RacingLine(circuit, RollingStart.ColumnOf(1)) };
        CarSpec paceSpec = CarSpec.For("c-19");
        PaceCar = new StandInCar(paceSpec, columns[0]) { Pits = pits };
        PaceCar.PlaceAt(circuit.IndexOf(circuit.Data.grid.slots[0].d + 40f), RollingStart.ColumnOf(0));
        paceDriver = new AiPilot(columns[0], paceSpec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: Start.Speeds);
    }

    void GridPlace(int i, out float d, out float lateral)
    {
        GridSlot[] slots = circuit.Data.grid.slots;
        if (i < slots.Length) { d = slots[i].d; lateral = slots[i].lateral; return; }
        int last = slots.Length - 1;
        float spacing = slots[last - 1].d - slots[last].d;
        d = slots[last].d - (i - last) * spacing;
        lateral = slots[i % 2].lateral;
    }

    public string Name(SessionCar c) => $"{c.Model}#{c.Id}";

    public void Step()
    {
        Measure();
        float[] caps = Start != null && Cars.Any(c => Start.Holds(c.Laps)) ? StartStep() : null;
        lodClock += Dt;
        if (Physics != null && lodClock >= 0.25f)
        {
            lodClock = 0f;
            int on = 0, wanted = 0;
            IEnumerable<SessionCar> order = Apart == null ? Cars : Cars.OrderBy(Apart);
            foreach (SessionCar s in order)
            {
                bool want = Physics(s) && (Apart == null || wanted < MostCars);
                if (want) wanted++;
                if (want && s.Racer.Active.Gliding) Wake(s);
                else if (!want && !s.Racer.Active.Gliding && s.Racer.CanGlide) Sleep(s);
                if (!s.Racer.Active.Gliding) on++;
            }
            if (Time > 2f) MostOnPhysics = Math.Max(MostOnPhysics, on);   // every car starts on physics
        }

        int inLane = 0;
        for (int i = 0; i < Cars.Count; i++)
        {
            SessionCar s = Cars[i];
            AiRacer r = s.Racer;
            StandInCar car = s.Car;
            bool held = caps != null && Start.Holds(s.Laps);
            if (held)
            {
                if (!formation.TryGetValue(s.Id, out AiPilot f))
                    formation[s.Id] = f = new AiPilot(columns[s.Slot % 2], car.Spec, r.Pilot.Pace, 100f, 100f, s.Id + 1, lanes: false);
                r.Formation = f;
                r.InFormation = true;
                r.FormationCap = caps[s.Slot];
            }
            else r.InFormation = false;
            if (r.Active.Gliding)
            {
                s.GlideClock += Dt;
                if (s.GlideClock >= GlideStep(s) - 1e-5f)
                {
                    bool gliding = r.Glide(traffic[i], sorted, Racing && !held, car.Spec, s.GlideClock);
                    s.GlideClock = 0f;
                    if (!gliding) { s.GlidingStops++; Wake(s); }
                }
                if (r.Active.Gliding)
                {
                    Pose(s);
                    Watch(s, default);
                    continue;
                }
            }
            car.DragScale = Racecraft.Slipstream(circuit, traffic[i], field);
            r.Active.Towed = car.DragScale < 0.97f;
            AiPilot.Controls c = r.Step(car.Position, car.Forward, car.Velocity, car.YawRate, traffic[i], sorted, all, Racing && !held, car.Spec, Dt);
            car.Hold = r.Hold;
            if (GreenTime >= 0f && Time > GreenTime + 5f && !s.InPitLane && !r.Pitting && !r.Rejoin.Busy && r.Active == r.Pilot && car.Speed < 5f)
            {
                CrawlSteps++;
                if (r.Craft.Braking) CrawlBrakeSteps++;
                if (StopLog) Console.WriteLine($"STOP {Clock(Time)} {Name(s)} at {r.Pilot.Index * circuit.Step:0} m {car.Speed * 3.6f:0} km/h {(r.Craft.Braking ? "BRK" : "")} {r.Craft.State} tspd {r.Pilot.TargetSpeed * 3.6f:0} cap {Math.Min(r.Pilot.SpeedCap, 99f) * 3.6f:0} scale {r.Pilot.SpeedScale:0.00} off {r.Pilot.Offset:0.0} shift {r.Pilot.Shift:0.0} lat {car.Lateral:0.0} side {(r.Pilot.SideFilter ? r.Pilot.SideWeight.ToString("0.00") : "-")} thr {c.Throttle:0.00} brk {c.Brake:0.00} str {c.Steer:0.00} hold {r.Craft.HoldingLine} def {r.Craft.Defending}");
            }
            car.Reverse = c.Reverse;
            car.Step(Dt, c.Throttle, c.Brake, c.Steer);

            if (s.InPitLane) inLane++;
            Watch(s, c);
        }
        MostInLane = Math.Max(MostInLane, inLane);
        if (PaceCar != null && !PaceCarGone)
        {
            AiPilot.Controls pc = paceDriver.Drive(PaceCar.Position, PaceCar.Forward, PaceCar.Velocity, PaceCar.YawRate);
            PaceCar.Step(Dt, pc.Throttle, pc.Brake, pc.Steer);
            PaceCarTop = Math.Max(PaceCarTop, PaceCar.Speed);
        }
        Collide();
        Time += Dt;
    }

    /// <summary>PhysicsRange taking a car off physics: it glides on from where it is.</summary>
    void Sleep(SessionCar s)
    {
        int k = Math.Max(s.Car.TrackIndex, 0);
        Vector3 f = circuit.Forward[k];
        s.Racer.Active.StartGlide(s.Car.Position, s.Car.Vx * f.x + s.Car.Vz * f.z);
        s.GlideClock = (s.Id % 5) * Dt;
        s.Handovers++;
    }

    /// <summary>Back to physics where the gliding car is, moving along its path.</summary>
    void Wake(SessionCar s)
    {
        AiPilot p = s.Racer.Active;
        p.GlidePose(p.GlideSpeed * s.GlideClock, out Vector3 at, out Vector3 forward, out _);
        s.Car.Wake(at, forward, p.GlideSpeed, p.GlideSpeed * p.GlideCurvature, p.GlideSteer);
        p.StopGlide();
        s.Handovers++;
    }

    void Pose(SessionCar s)
    {
        AiPilot p = s.Racer.Active;
        p.GlidePose(p.GlideSpeed * s.GlideClock, out Vector3 at, out Vector3 forward, out _);
        s.Car.Pose(at, forward, p.GlideSpeed, p.GlideSpeed * p.GlideCurvature);
    }

    /// <summary>RaceCar's part: laps, race distance, pit lane and limiter, and the lists the AI reads.</summary>
    void Measure()
    {
        traffic.Clear();
        field.Clear();
        all.Clear();
        foreach (SessionCar s in Cars)
        {
            StandInCar car = s.Car;
            int k = Math.Max(car.TrackIndex, 0);
            if (s.Last > circuit.Count - 50 && k < 50) s.Laps++;
            else if (s.Last < 50 && k > circuit.Count - 50) s.Laps--;
            s.Last = k;
            s.Distance = s.Laps * circuit.Lap + circuit.DistanceAt(k, car.Position);
            s.InPitLane = pits.OffTrackInLane(k, car.Lateral);
            car.SpeedLimit = pits.InLimitZone(car.Position, k) ? pits.LimitSpeed : float.PositiveInfinity;
            Vector3 f = circuit.Forward[k];
            var t = new TrafficCar
            {
                Id = s.Id,
                Distance = s.Distance,
                Lateral = car.Lateral,
                Speed = car.Vx * f.x + car.Vz * f.z,
                LapPace = s.Racer.Pilot.Profile.LapTime,
                Braking = car.Spec.BrakeDecel * 9.81f * AiPilot.LimitFor(s.Racer.Pilot.Pace),
                Across = car.Vx * circuit.Left[k].x + car.Vz * circuit.Left[k].z,
                Yaw = (float)Math.Atan2(car.Forward.z, car.Forward.x),
                Ghost = s.Racer.Ghost,
            };
            traffic.Add(t);
            if (!s.InPitLane) field.Add(t);
            all.Add(new CarState { Id = s.Id, Position = car.Position, Velocity = car.Velocity });
        }
        sorted.Set(field, circuit.Lap);
    }

    /// <summary>RaceStart's part: the pace car, everyone's formation speed, and the green.</summary>
    float[] StartStep()
    {
        Start.Tick(Dt);
        float paceSpeed = 0f;
        if (!PaceCarGone)
        {
            if (!paceOnRoute && paceDriver.Index >= 0 && paceDistance > circuit.Lap * 0.5f)
            {
                int peel = circuit.IndexOf(pits.Data.entry - RollingStart.PeelBefore);
                if (circuit.Wrap(paceDriver.Index - peel) < 20)
                {
                    int k = paceDriver.Index;
                    float[] limits = pits.Limits();
                    for (int j = 0; j < limits.Length; j++) limits[j] = Math.Min(limits[j], Start.Speeds[j]);
                    paceDriver = new AiPilot(pits.Route(columns[0], -1), PaceCar.Spec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: limits);
                    paceDriver.Relocate(k);
                    paceOnRoute = true;
                    Start.PeelOff();
                }
            }
            paceDriver.SpeedCap = Start.PaceCarCap;
            int i = Math.Max(paceDriver.Index, 0);
            if (paceLast > circuit.Count - 50 && i < 50) paceLaps++;
            paceLast = i;
            paceDistance = paceLaps * circuit.Lap + i * circuit.Step;
            if (paceOnRoute && pits.LaneAt(i) > pits.LimitStartLane + 120) PaceCarGone = true;
            paceSpeed = PaceCar.Speed;
        }

        var caps = new float[Cars.Count];
        TrafficCar leader = traffic[0];
        caps[0] = Start.LeaderCap(leader.Distance, paceDistance, paceSpeed);
        if (!PaceCarGone)
        {
            int k = Math.Max(PaceCar.TrackIndex, 0);
            float gap = paceDistance - leader.Distance - 4.8f - 4f;
            if (gap < 80f && Math.Abs(PaceCar.Lateral - leader.Lateral) < 2.6f)
                caps[0] = Math.Min(caps[0], gap > 0f ? (float)Math.Sqrt(paceSpeed * paceSpeed + 2f * leader.Braking * gap) : 0f);
        }
        for (int i = 1; i < Cars.Count; i++)
        {
            bool has = i >= 2;
            TrafficCar ahead = has ? traffic[i - 2] : default;
            caps[i] = Start.Cap(i, traffic[i].Distance, traffic[i].Speed, leader.Distance, has ? ahead.Distance : float.NaN, ahead.Speed, ahead.Braking, traffic[i].Braking);
        }
        if (!Racing && Start.PaceCarIn && Cars[0].Laps >= 1)
        {
            Start.Green();
            GreenTime = Time;
            foreach (SessionCar s in Cars)
            {
                s.Laps--;
                s.FuelAtGreen = s.Racer.Supplies.Fuel;
                s.WearAtGreen = s.Racer.Supplies.Wear;
            }
        }
        return caps;
    }

    /// <summary>Records what the checks look at: the limiter, the stops, laps, the track and the tank.</summary>
    void Watch(SessionCar s, AiPilot.Controls c)
    {
        StandInCar car = s.Car;
        int k = Math.Max(car.TrackIndex, 0);
        if (trace == s.Id && (int)(Time / Dt) % TraceEvery == 0)
            Console.WriteLine($"    {Clock(Time)} #{s.Id} {k * circuit.Step,5:0} m lat {car.Lateral,6:0.0} {car.Speed * 3.6f,4:0} km/h {s.Racer.Pit.State} {(s.Racer.Active == s.Racer.Pilot ? "racing" : s.Racer.Active == s.Racer.Formation ? "formation" : "route")} idx {s.Racer.Active.Index * circuit.Step:0} err {s.Racer.Active.LateralError:0.0} {car.Surface} cap {Math.Min(s.Racer.Active.SpeedCap, 999f) * 3.6f:0} tgt {s.Racer.Active.TargetSpeed * 3.6f:0} R {1f / Math.Max(Math.Abs(s.Racer.Active.Line.Curvature[Math.Max(s.Racer.Active.Index,0)]), 1e-4f):0} seg {s.Racer.Active.Line.Segment[Math.Max(s.Racer.Active.Index,0)]:0.00} {s.Racer.Craft.State} {(s.Racer.Craft.Braking ? "BRK" : "")} {(s.Racer.Craft.SideSteering ? "SIDE" : "")} off {s.Racer.Pilot.Offset:0.0}");
        if (car.SpeedLimit < 1000f) s.LimitZoneTop = Math.Max(s.LimitZoneTop, car.Speed);
        if (s.Racer.Supplies.Empty) s.RanDry = true;

        PitPlan pit = s.Racer.Pit;
        if (pit.State != s.Stage)
        {
            if (pit.State == PitPlan.Stage.Stopped)
            {
                Vector3 box = pits.BoxPoint(s.Garage) - car.Position;
                float miss = (float)Math.Sqrt(box.x * box.x + box.z * box.z);
                s.WorstBoxMiss = Math.Max(s.WorstBoxMiss, miss);
                s.StopStart = Time;
                s.Stops.Add($"{Session.Clock(Time)} stopped {miss:0.00} m from its box, {s.Racer.Supplies.Fuel:0.0} L, tyres {s.Racer.Supplies.Wear * 100f:0}% worn");
            }
            if (s.Stage == PitPlan.Stage.Stopped)
            {
                s.Stops[s.Stops.Count - 1] += $", left after {Time - s.StopStart:0.0} s with {s.Racer.Supplies.Fuel:0.0} L";
                s.LeftWith = s.Racer.Supplies.Fuel;
            }
            if (pit.State == PitPlan.Stage.Racing && s.Stage == PitPlan.Stage.Leaving) s.Rejoins++;
            s.Stage = pit.State;
        }

        // Laps and what each used, from the green.
        if (Racing && s.Last > circuit.Count - 50 && k < 50)
        {
            if (s.LapStart >= 0f) s.LapTimes.Add(Time - s.LapStart);
            if (s.LapFuel >= 0f && s.Racer.Supplies.Fuel < s.LapFuel) s.LapUse.Add((s.LapFuel - s.Racer.Supplies.Fuel, s.Racer.Supplies.Wear - s.LapWear));
            s.LapStart = Time;
            s.LapFuel = s.Racer.Supplies.Fuel;
            s.LapWear = s.Racer.Supplies.Wear;
        }

        float off = Math.Abs(car.Lateral) - circuit.HalfWidth(k);
        if (off > KerbWidth && !car.OnPitLane(car.Position))
        {
            if (s.OffTime < 0f) { s.OffTime = Time; s.OffWhere = k * circuit.Step; }
            s.OffSeconds += Dt;
        }
        // As AiDriver: stuck wanting to go, or the rejoin hasn't got the car back to racing after a
        // long while, and only once the gap behind is clear.
        bool rejoining = s.Racer.Rejoin.Busy;
        bool stalled = !rejoining && car.Speed < 2f && c.Throttle > 0.3f;
        if (stalled || s.Racer.Rejoin.Trying) s.StuckTime += Dt;
        else if (!rejoining) s.StuckTime = 0f;
        if (s.StuckTime > (rejoining ? Rejoin.GiveUpAfter : 4f) && s.Racer.Rejoin.GapClear)
        {
            s.Recoveries++;
            car.PlaceAt(k, 0f);
            s.Racer.Lost();
            s.StuckTime = 0f;
        }
    }

    void Collide()
    {
        var bodies = new List<(StandInCar Car, string Name, bool Pits)>();
        foreach (SessionCar s in Cars) bodies.Add((s.Car, Name(s) + (s.Racer.Active.Gliding ? " (gliding)" : "") + (s.Racer.Ghost ? " (ghost)" : "") + (s.Racer.Rejoin.Busy ? $" (rejoining: {s.Racer.Rejoin.State}{(s.Racer.Rejoin.Yielding ? " yielding" : "")} {s.Racer.Rejoin.PastEdge:0.0} m past, heading {s.Racer.Rejoin.Heading:0}, {s.Car.Speed * 3.6f:0} km/h)" : ""), s.InPitLane || s.Racer.Pitting));
        if (PaceCar != null && !PaceCarGone) bodies.Add((PaceCar, "pace car", false));
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
            {
                StandInCar a = bodies[i].Car, b = bodies[j].Car;
                float dx = b.X - a.X, dz = b.Z - a.Z;
                float nx = 0f, nz = 0f, depth = 0f;
                bool hit = dx * dx + dz * dz < 30f && Race.Overlap(a, b, out nx, out nz, out depth);
                var key = (i, j);
                if (!hit) { touching.Remove(key); continue; }
                if (bodies[i].Name.Contains("(ghost)") || bodies[j].Name.Contains("(ghost)"))
                {
                    touching.Add(key);
                    continue;
                }
                if (bodies[i].Name.EndsWith("(gliding)") || bodies[j].Name.EndsWith("(gliding)"))
                {
                    // No bodies to bump: a gliding car has no collider in the game.
                    if (touching.Add(key)) GhostOverlaps++;
                    continue;
                }
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
                    Contacts.Add(new Contact
                    {
                        Time = Time,
                        Speed = Math.Max(-vn, 0f),
                        RearEnd = Contact.NoseToTail(a, b, nx, nz),
                        Where = Math.Max(a.TrackIndex, 0) * circuit.Step,
                        A = bodies[i].Name + (bodies[i].Pits ? " (pits)" : ""),
                        B = bodies[j].Name + (bodies[j].Pits ? " (pits)" : ""),
                    });
            }
    }

    public static string Clock(float s) => $"{(int)(s / 60f)}:{s % 60f:00.0}";
}
