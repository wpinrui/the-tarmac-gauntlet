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
                Racer = new AiRacer(pilot, new Racecraft(i + 1), supplies, new PitPlan(pits, garage, supplies), i + 1),
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
            car.DragScale = Racecraft.Slipstream(circuit, traffic[i], field);
            r.Active.Towed = car.DragScale < 0.97f;
            AiPilot.Controls c = r.Step(car.Position, car.Forward, car.Velocity, car.YawRate, traffic[i], field, all, Racing && !held, car.Spec, Dt);
            car.Hold = r.Hold;
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
                GivingWay = !s.Racer.Pitting && s.Racer.Craft.State == Racecraft.Mode.GivingWay,
                Craft = s.Racer.Craft,
            };
            traffic.Add(t);
            if (!s.InPitLane) field.Add(t);
            all.Add(new CarState { Id = s.Id, Position = car.Position, Velocity = car.Velocity });
        }
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
        if (trace == s.Id && (int)(Time / Dt) % 50 == 0)
            Console.WriteLine($"    {Clock(Time)} #{s.Id} {k * circuit.Step,5:0} m lat {car.Lateral,6:0.0} {car.Speed * 3.6f,4:0} km/h {s.Racer.Pit.State} {(s.Racer.Active == s.Racer.Pilot ? "racing" : s.Racer.Active == s.Racer.Formation ? "formation" : "route")} idx {s.Racer.Active.Index * circuit.Step:0} err {s.Racer.Active.LateralError:0.0} {car.Surface} cap {Math.Min(s.Racer.Active.SpeedCap, 999f) * 3.6f:0} tgt {s.Racer.Active.TargetSpeed * 3.6f:0} R {1f / Math.Max(Math.Abs(s.Racer.Active.Line.Curvature[Math.Max(s.Racer.Active.Index,0)]), 1e-4f):0} seg {s.Racer.Active.Line.Segment[Math.Max(s.Racer.Active.Index,0)]:0.00}");
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
        bool stalled = car.Speed < 2f && c.Throttle > 0.3f;
        s.StuckTime = stalled ? s.StuckTime + Dt : 0f;
        if (s.StuckTime > 4f)
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
        foreach (SessionCar s in Cars) bodies.Add((s.Car, Name(s), s.InPitLane || s.Racer.Pitting));
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
                        Where = Math.Max(a.TrackIndex, 0) * circuit.Step,
                        A = bodies[i].Name + (bodies[i].Pits ? " (pits)" : ""),
                        B = bodies[j].Name + (bodies[j].Pits ? " (pits)" : ""),
                    });
            }
    }

    public static string Clock(float s) => $"{(int)(s / 60f)}:{s % 60f:00.0}";
}
