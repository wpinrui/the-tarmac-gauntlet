using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs the rolling start (<see cref="RollingStart"/>): drives the pace car, tells each AI car
    /// how fast it may go to hold its place in the formation, and goes green when the leader crosses
    /// the line after the pace car has peeled off into the pit lane. Each car races once it crosses the line.
    ///
    /// With <see cref="Complex"/> set the field starts parked in its garages and the pace car in the fast
    /// lane, held there while <see cref="Holding"/>. Then the whole field drives out together on
    /// <see cref="GarageStart"/>'s timetable, down the pit lane and onto the track in formation behind the
    /// pace car, and the formation lap carries on from there.
    /// </summary>
    [DefaultExecutionOrder(-15)]   // after the cars are measured, before the AI drivers think
    public sealed class RaceStart : Starter
    {
        /// <summary>The cars in grid order, pole first.</summary>
        public RaceCar[] Grid;
        public CarController PaceCar;
        public PitLane Pits;
        /// <summary>The garages the field starts in, or null to start on the grid.</summary>
        public PitComplex Complex;
        /// <summary>Each grid slot's garage, with <see cref="Complex"/>.</summary>
        public int[] Garages;
        /// <summary>True while the field waits where it stands, before the drive out of the garages.</summary>
        [System.NonSerialized] public bool Holding;

        /// <summary>The drive out of the garages, or null for a start on the grid.</summary>
        public GarageStart Exit => exit;
        /// <summary>True once every car the AI drives is out of the pit lane on its column, or with no garage start.</summary>
        public bool PitOutDone => exit == null || pitOutDone;

        public bool Racing => start != null && start.State == RollingStart.Phase.Green;

        /// <summary>True once this AI car may race: after the green, once it has crossed the line.</summary>
        public override bool Released(AiDriver d) => Racing && (!slots.TryGetValue(d, out int slot) || !start.Holds(Grid[slot].Laps));

        private bool AnyHeld()
        {
            foreach (RaceCar r in Grid) if (start.Holds(r.Laps)) return true;
            return false;
        }

        private RollingStart start;
        private readonly Dictionary<AiDriver, int> slots = new Dictionary<AiDriver, int>();
        private readonly Dictionary<AiDriver, AiPilot> formation = new Dictionary<AiDriver, AiPilot>();
        private RacingLine[] columns;
        private float[] caps;
        private AiPilot paceDriver;
        private bool paceOnRoute;
        private int paceLaps, paceLast = -1;
        private float paceDistance;

        private enum Leaving { Parked, Rolling, Lane }
        private GarageStart exit;
        private bool pitOutDone;
        private Leaving[] leaving;
        private float[] along;
        private float paceAlong;
        private int[] progress;
        private AiPilot[] lanes;
        private AiDriver[] drivers;
        private float[] laneLimits;
        private bool[] onRoute;
        private int[] playerK;

        private void Begin()
        {
            Circuit c = PaceCar.Circuit;
            var specs = new List<CarSpec>();
            foreach (RaceCar r in Grid) specs.Add(r.GetComponent<CarController>().Spec);
            start = new RollingStart(c, specs);
            columns = new[] { new RacingLine(c, RollingStart.ColumnOf(0)), new RacingLine(c, RollingStart.ColumnOf(1)) };
            caps = new float[Grid.Length];
            for (int i = 0; i < Grid.Length; i++)
            {
                AiDriver d = Grid[i].GetComponent<AiDriver>();
                if (d != null) slots[d] = i;
            }
            paceDriver = new AiPilot(columns[0], PaceCar.Spec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: start.Speeds);
            PaceCar.AiDriving = true;
            if (Complex != null && Garages != null) BeginGarages(c, specs);
            int kp = c.IndexOf(c.DistanceAt(paceDriver.Line.Nearest(PaceCar.transform.position, -1), PaceCar.transform.position));
            paceLaps = kp * c.Step > c.Lap * 0.5f ? -1 : 0;
            paceLast = kp;
            paceDistance = paceLaps * c.Lap + kp * c.Step;
        }

        /// <summary>The timetable out of the garages, each car's route down the pit lane, and the pace car parked in the fast lane.</summary>
        private void BeginGarages(Circuit c, List<CarSpec> specs)
        {
            exit = new GarageStart(Pits, Complex, columns, Garages, specs, start.Speeds);
            leaving = new Leaving[Grid.Length];
            along = new float[Grid.Length];
            progress = new int[Grid.Length];
            lanes = new AiPilot[Grid.Length];
            drivers = new AiDriver[Grid.Length];
            laneLimits = Pits.Limits();
            onRoute = new bool[Grid.Length];
            playerK = new int[Grid.Length];
            for (int i = 0; i < Grid.Length; i++)
            {
                drivers[i] = Grid[i].GetComponent<AiDriver>();
                playerK[i] = -1;
            }

            // The pace car waits in the fast lane at its place on the timetable.
            int kp = exit.PlaceSample(-1);
            RacingLine way = exit.PaceRoute;
            PaceCar.PlaceAt(way.Point[kp], way.Tangent[kp]);
            float[] limits = Pits.Limits();
            for (int j = 0; j < limits.Length; j++) limits[j] = Mathf.Min(limits[j], start.Speeds[j] > 0f ? Mathf.Max(start.Speeds[j], GarageStart.RoadSpeed) : limits[j]);
            paceDriver = new AiPilot(way, PaceCar.Spec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: limits);
            paceDriver.Relocate(kp);
        }

        private void FixedUpdate()
        {
            if (Grid == null || Grid.Length == 0) return;
            if (columns == null) Begin();
            if (Holding)
            {
                HoldPaceCar();
                return;
            }
            float dt = Time.fixedDeltaTime;
            start.Tick(dt);
            if (Racing && !AnyHeld()) return;
            if (exit != null && !pitOutDone)
            {
                exit.Tick(dt);
                FollowPlayers();
                DrivePaceCarOut();
                pitOutDone = AllOut();
                if (pitOutDone) EndGarages();
                else return;
            }

            float paceSpeed = DrivePaceCar();
            TrafficCar leader = Grid[0].Traffic;
            float leaderCap = start.LeaderCap(leader.Distance, paceDistance, paceSpeed);
            if (PaceCar.gameObject.activeSelf) leaderCap = Mathf.Min(leaderCap, BehindPaceCar(leader, paceSpeed));
            caps[0] = leaderCap;
            for (int i = 1; i < Grid.Length; i++)
            {
                TrafficCar me = Grid[i].Traffic;
                bool hasAhead = i >= 2;
                TrafficCar ahead = hasAhead ? Grid[i - 2].Traffic : default;
                caps[i] = start.Cap(i, me.Distance, me.Speed, leader.Distance,
                    hasAhead ? ahead.Distance : float.NaN, ahead.Speed, ahead.Braking, me.Braking);
            }

            LimitPlayers();

            if (!Racing && start.PaceCarIn && Grid[0].Laps >= 1)
            {
                start.Green();
                RaceCar.StartRace();
                Debug.Log("Green flag");
            }
        }

        /// <summary>
        /// A car someone drives, not the AI, keeps its place in the formation on a limiter until it
        /// crosses the line after the green: no faster than its place allows, and never past the car
        /// before it in grid order. Above the limit the power is cut, and a little further over the
        /// brakes come on. The pit lane limiter still holds where it is lower.
        /// </summary>
        private void LimitPlayers()
        {
            for (int i = 0; i < Grid.Length; i++)
            {
                RaceCar r = Grid[i];
                CarController car = r.GetComponent<CarController>();
                if (car.AiDriving || !r.isActiveAndEnabled || !start.Holds(r.Laps)) continue;
                TrafficCar me = r.Traffic;
                float before = i > 0 ? Grid[i - 1].Traffic.Distance : float.NaN;
                float beforeSpeed = i > 0 ? Grid[i - 1].Traffic.Speed : 0f;
                float limit = RollingStart.Limiter(i, caps[i], me.Distance, before, beforeSpeed);
                car.SpeedLimit = Mathf.Min(car.SpeedLimit, limit);
                car.BrakeAbove = limit + RollingStart.LimiterGrace;
            }
        }

        /// <summary>
        /// Cuts to the last part of the formation lap: the pace car is gone into the pit lane and the
        /// field runs two by two in grid order, the leader at race distance <c>leader</c> on the
        /// formation lap, every car at the formation speed where it is.
        /// </summary>
        public void CutToFinal(float leader)
        {
            if (columns == null) Begin();
            Circuit c = PaceCar.Circuit;
            start.CutToFinal();
            paceOnRoute = true;
            PaceCar.gameObject.SetActive(false);
            for (int i = 0; i < Grid.Length; i++)
            {
                float d = RollingStart.PlaceOf(leader, i);
                int k = c.IndexOf(Mathf.Repeat(d, c.Lap));
                Vector3 at = c.Surface(k, RollingStart.ColumnOf(i));
                Quaternion facing = Quaternion.LookRotation(c.Forward[k], Vector3.up);
                float speed = start.SpeedAt(d);
                RaceCar r = Grid[i];
                if (r.TryGetComponent(out AiDriver driver)) driver.Place(at, facing, speed, k);
                else r.GetComponent<CarController>().Wake(at, facing, speed, 0f, 0f);
                r.Moved(Mathf.FloorToInt(d / c.Lap), k);
            }
        }

        /// <summary>Puts an AI car in or out of the formation and passes it its speed limit. Called by the car each step.</summary>
        public override void Brief(AiDriver d)
        {
            if (d.Racer == null) return;
            if (exit != null && !pitOutDone && slots.TryGetValue(d, out int at) && leaving[at] == Leaving.Lane)
            {
                AiPilot lane = lanes[at];
                if (lane == null)
                {
                    // Handed back by the player on its way out: the AI drives on from where the car is.
                    lane = lanes[at] = new AiPilot(exit.Route[at], d.Racer.Pilot.Spec, d.Pace, 100f, 100f, d.Seed, lanes: false, maxSpeed: laneLimits);
                    lane.Relocate(playerK[at] >= 0 ? playerK[at] : exit.Join(at));
                }
                Vector3 p = d.Car.transform.position;
                along[at] = exit.Along(at, lane.Index, p, lane.Line);
                RacingLine way = exit.Way(at, along[at]);
                if (lane.Line != way && lane.Index >= 0)
                {
                    // Out of the pit lane: on round the lap on its column, not back into the pits with its route.
                    int k = lane.Index;
                    lane = lanes[at] = new AiPilot(way, d.Racer.Pilot.Spec, d.Pace, 100f, 100f, d.Seed, lanes: false);
                    lane.Relocate(k);
                }
                d.Racer.Formation = lane;
                d.Racer.InFormation = true;
                d.Racer.FormationCap = Holding ? 0f : exit.Cap(at, along[at], FollowOut(at, d.Racer.Pilot.Spec));
                return;
            }
            if (!slots.TryGetValue(d, out int slot) || caps == null || !start.Holds(Grid[slot].Laps))
            {
                d.Racer.InFormation = false;
                return;
            }
            if (!formation.TryGetValue(d, out AiPilot pilot))
            {
                pilot = new AiPilot(columns[slot % 2], d.Racer.Pilot.Spec, d.Pace, 100f, 100f, d.Seed, lanes: false);
                formation[d] = pilot;
            }
            d.Racer.Formation = pilot;
            d.Racer.InFormation = true;
            d.Racer.FormationCap = caps[slot];
        }

        /// <summary>
        /// Drives a car out of its garage itself, until it is on its route down the pit lane: parked, held on its
        /// brakes, until its place on the timetable comes near, then along its path out of the door.
        /// </summary>
        public override bool Steers(AiDriver d, out AiPilot.Controls controls, out bool hold)
        {
            controls = default;
            hold = false;
            if (exit == null || pitOutDone || !slots.TryGetValue(d, out int slot)) return false;
            if (leaving == null) return false;
            switch (leaving[slot])
            {
                case Leaving.Parked:
                    if (Holding || !exit.Launch(slot) || !exit.JoinClear(slot, along, onRoute))
                    {
                        controls.Brake = 1f;
                        hold = true;
                        return true;
                    }
                    leaving[slot] = Leaving.Rolling;
                    goto case Leaving.Rolling;
                case Leaving.Rolling:
                {
                    CarController car = d.Car;
                    Transform t = car.transform;
                    if (Holding)
                    {
                        controls.Brake = 1f;
                        return true;
                    }
                    if (!GarageStart.Roll(exit.Path(slot), ref progress[slot], t.position, t.forward, car.Speed, car.Spec, out controls))
                        return true;
                    // On the route: its pilot drives from here.
                    var lane = new AiPilot(exit.Route[slot], d.Racer.Pilot.Spec, d.Pace, 100f, 100f, d.Seed, lanes: false, maxSpeed: laneLimits);
                    lane.Relocate(exit.Join(slot));
                    lanes[slot] = lane;
                    leaving[slot] = Leaving.Lane;
                    onRoute[slot] = true;
                    along[slot] = exit.Along(slot, exit.Join(slot), t.position, exit.Route[slot]);
                    Brief(d);
                    return false;
                }
                default:
                    return false;
            }
        }

        /// <summary>A car still in its garage or on its way out of it keeps its physics.</summary>
        public override bool Pinned(AiDriver d) => exit != null && !pitOutDone && slots.TryGetValue(d, out int slot) && leaving != null && leaving[slot] != Leaving.Lane;

        /// <summary>
        /// Fastest a car on its way out may go to stay able to stop behind anything ahead of it on its way: the pace
        /// car, and any car on its way out up to <see cref="LookAhead"/> ahead, which is the car before it in the grid
        /// unless a player has driven out of turn.
        /// </summary>
        private float FollowOut(int slot, CarSpec spec)
        {
            float cap = float.PositiveInfinity;
            CarController me = Grid[slot].GetComponent<CarController>();
            float speed = me.Speed;
            int k = lanes[slot] != null && lanes[slot].Index >= 0 ? lanes[slot].Index : exit.Join(slot);
            if (PaceCar.gameObject.activeSelf && Close(slot, k, paceAlong, PaceCar.transform.position))
                cap = exit.Behind(along[slot], speed, spec, paceAlong, PaceCar.Speed, PaceCar.Spec);
            for (int j = 0; j < Grid.Length; j++)
            {
                if (j == slot || !onRoute[j] || !Close(slot, k, along[j], Grid[j].transform.position)) continue;
                CarController ahead = Grid[j].GetComponent<CarController>();
                cap = Mathf.Min(cap, exit.Behind(along[slot], speed, spec, along[j], ahead.Speed, ahead.Spec));
            }
            return cap;
        }

        /// <summary>How far ahead a car on its way out looks for a car in its way, m.</summary>
        private const float LookAhead = 80f;

        private bool Close(int slot, int k, float at, Vector3 position)
        {
            float gap = exit.Between(along[slot], at);
            return gap > 0f && gap < LookAhead && exit.InWay(slot, k, position, along[slot]);
        }

        /// <summary>
        /// Keeps track of a car the player drives out: once it is out of its garage and on the lane, the cars behind
        /// it in the grid see it as they see the AI cars, and queue behind it however slowly it goes.
        /// </summary>
        private void FollowPlayers()
        {
            for (int i = 0; i < Grid.Length; i++)
            {
                AiDriver d = drivers[i];
                if (d != null && d.enabled) continue;
                Vector3 p = Grid[i].transform.position;
                int k = exit.Find(i, p, playerK[i]);
                RacingLine way = exit.Way(i, along[i]);
                Vector3 off = p - way.Point[k];
                if (!onRoute[i] && off.x * off.x + off.z * off.z > 3f * 3f) continue;   // still in its garage
                if (!onRoute[i]) along[i] = exit.Along(i, exit.Join(i), p, exit.Route[i]);
                playerK[i] = k;
                onRoute[i] = true;
                leaving[i] = Leaving.Lane;
                along[i] = exit.Along(i, k, p, way);
            }
        }

        /// <summary>True once every car the AI drives is out of the pit lane, past the exit and on its column.</summary>
        private bool AllOut()
        {
            for (int i = 0; i < Grid.Length; i++)
            {
                AiDriver d = drivers[i];
                // A car the player drives is waited for once it is out of its garage; left there, it is theirs to bring out.
                if (d == null || !d.enabled)
                {
                    if (onRoute[i] && !exit.Out(along[i])) return false;
                    continue;
                }
                if (leaving[i] != Leaving.Lane || !exit.Out(along[i])) return false;
            }
            return true;
        }

        /// <summary>The field is out: the formation lap carries on from here, two by two behind the pace car.</summary>
        private void EndGarages()
        {
            pitOutDone = true;
            Circuit c = PaceCar.Circuit;
            int k = paceDriver.Index >= 0 ? paceDriver.Index : c.IndexOf(Mathf.Repeat(paceDistance, c.Lap));
            paceDriver = new AiPilot(columns[0], PaceCar.Spec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: start.Speeds);
            paceDriver.Relocate(k);
            Debug.Log($"Out of the garages and in formation after {exit.Time:0.0} s");
        }

        /// <summary>
        /// Skips the drive out of the garages: the start stops driving the cars out, wherever they are, and the
        /// formation's own driving takes over. Whoever skips puts the field where it should be, as
        /// <see cref="CutToFinal"/> does.
        /// </summary>
        public void EndPitOut()
        {
            if (columns == null) Begin();
            Holding = false;
            if (exit == null || pitOutDone) return;
            EndGarages();
            Debug.Log("Drive out of the garages skipped");
        }

        /// <summary>The pace car waits on its brakes.</summary>
        private void HoldPaceCar()
        {
            if (!PaceCar.gameObject.activeSelf) return;
            PaceCar.SetControls(0f, 1f, 0f);
            PaceCar.Hold = true;
        }

        /// <summary>Drives the pace car one step at its place on the timetable out of the pit lane, and counts its distance.</summary>
        private void DrivePaceCarOut()
        {
            Circuit c = PaceCar.Circuit;
            Transform t = PaceCar.transform;
            PaceCar.Hold = false;
            paceAlong = exit.Along(-1, paceDriver.Index, t.position, paceDriver.Line);
            paceDriver.SpeedCap = exit.Cap(-1, paceAlong, float.PositiveInfinity);
            AiPilot.Controls ctl = paceDriver.Drive(t.position, t.forward, PaceCar.Velocity, PaceCar.YawRate);
            PaceCar.SetControls(ctl.Throttle, ctl.Brake, ctl.Steer);
            CountPace(c);
        }

        private void CountPace(Circuit c)
        {
            int i = paceDriver.Index;
            if (i < 0) return;
            if (paceLast > c.Count - 50 && i < 50) paceLaps++;
            else if (paceLast >= 0 && paceLast < 50 && i > c.Count - 50) paceLaps--;
            paceLast = i;
            paceDistance = paceLaps * c.Lap + c.DistanceAt(i, PaceCar.transform.position);
        }

        /// <summary>Drives the pace car one step at the formation speed, then into the pit lane and away. Returns its speed.</summary>
        private float DrivePaceCar()
        {
            if (!PaceCar.gameObject.activeSelf) return 0f;
            Circuit c = PaceCar.Circuit;
            Transform t = PaceCar.transform;
            if (!paceOnRoute && paceDriver.Index >= 0 && paceDistance > c.Lap * 0.5f)
            {
                int peel = c.IndexOf(Pits.Data.entry - RollingStart.PeelBefore);
                if (c.Wrap(paceDriver.Index - peel) < 20)
                {
                    int k = paceDriver.Index;
                    float[] limits = Pits.Limits();
                    for (int j = 0; j < limits.Length; j++) limits[j] = Mathf.Min(limits[j], start.Speeds[j]);
                    paceDriver = new AiPilot(Pits.Route(columns[0], -1), PaceCar.Spec, 100f, 100f, 100f, 1, lanes: false, maxSpeed: limits);
                    paceDriver.Relocate(k);
                    paceOnRoute = true;
                    start.PeelOff();
                }
            }
            paceDriver.SpeedCap = start.PaceCarCap;
            AiPilot.Controls ctl = paceDriver.Drive(t.position, t.forward, PaceCar.Velocity, PaceCar.YawRate);
            PaceCar.SetControls(ctl.Throttle, ctl.Brake, ctl.Steer);
            PaceCar.Hold = false;

            CountPace(c);
            int i = paceDriver.Index;
            // Well down the pit lane, out of sight of the field: its job is done.
            if (paceOnRoute && Pits.LaneAt(i) > Pits.LimitStartLane + 120) PaceCar.gameObject.SetActive(false);
            Vector3 v = PaceCar.Velocity;
            return Mathf.Sqrt(v.x * v.x + v.z * v.z);
        }

        /// <summary>Speed that keeps the leader able to stop behind the pace car while it is in the leader's path.</summary>
        private float BehindPaceCar(TrafficCar leader, float paceSpeed)
        {
            Circuit c = PaceCar.Circuit;
            int k = c.IndexOf(Mathf.Repeat(paceDistance, c.Lap));
            Vector3 d = PaceCar.transform.position - c.Centre[k];
            float lateral = d.x * c.Left[k].x + d.z * c.Left[k].z;
            float gap = paceDistance - leader.Distance - 4.8f - 4f;
            if (gap > 80f || Mathf.Abs(lateral - leader.Lateral) > 2.6f) return float.PositiveInfinity;
            return gap > 0f ? Mathf.Sqrt(paceSpeed * paceSpeed + 2f * leader.Braking * gap) : 0f;
        }
    }
}
