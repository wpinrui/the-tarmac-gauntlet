using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs the rolling start (<see cref="RollingStart"/>): drives the pace car, tells each AI car
    /// how fast it may go to hold its place in the formation, and goes green when the leader crosses
    /// the line after the pace car has peeled off into the pit lane. Each car races once it crosses the line.
    /// </summary>
    [DefaultExecutionOrder(-15)]   // after the cars are measured, before the AI drivers think
    public sealed class RaceStart : MonoBehaviour
    {
        /// <summary>The cars in grid order, pole first.</summary>
        public RaceCar[] Grid;
        public CarController PaceCar;
        public PitLane Pits;

        public bool Racing => start != null && start.State == RollingStart.Phase.Green;

        /// <summary>True once this AI car may race: after the green, once it has crossed the line.</summary>
        public bool Released(AiDriver d) => Racing && (!slots.TryGetValue(d, out int slot) || !start.Holds(Grid[slot].Laps));

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
        }

        private void FixedUpdate()
        {
            if (Grid == null || Grid.Length == 0) return;
            if (columns == null) Begin();
            float dt = Time.fixedDeltaTime;
            start.Tick(dt);
            if (Racing && !AnyHeld()) return;

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

            if (!Racing && start.PaceCarIn && Grid[0].Laps >= 1)
            {
                start.Green();
                RaceCar.StartRace();
                Debug.Log("Green flag");
            }
        }

        /// <summary>Puts an AI car in or out of the formation and passes it its speed limit. Called by the car each step.</summary>
        public void Brief(AiDriver d)
        {
            if (d.Racer == null) return;
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

            int i = paceDriver.Index;
            if (paceLast > c.Count - 50 && i < 50) paceLaps++;
            paceLast = i;
            paceDistance = paceLaps * c.Lap + i * c.Step;
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
