using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs the pit lane start (<see cref="PitLaneStart"/>): each AI car moves up the queue behind the
    /// car ahead and waits at the pit exit line until its turn, then drives out along the route, keeping a safe gap to anything
    /// ahead on it, and hands it to its racing line once it has rejoined. Pit stops open once every
    /// AI car has left the pit lane.
    /// </summary>
    [DefaultExecutionOrder(-15)]   // after the cars are measured, before the AI drivers think
    public sealed class PitStart : Starter
    {
        /// <summary>The cars in queue order, front first.</summary>
        public RaceCar[] Grid;
        public PitLaneStart Queue;
        public PitLane Pits;

        private readonly Dictionary<AiDriver, int> places = new Dictionary<AiDriver, int>();
        private readonly Dictionary<AiDriver, AiPilot> routes = new Dictionary<AiDriver, AiPilot>();
        private AiDriver[] drivers;
        private bool[] gone;
        private float[] limits;
        private bool open;

        private void Begin()
        {
            gone = new bool[Grid.Length];
            drivers = new AiDriver[Grid.Length];
            limits = Pits.Limits();
            for (int i = 0; i < Grid.Length; i++)
            {
                drivers[i] = Grid[i].GetComponent<AiDriver>();
                if (drivers[i] != null) places[drivers[i]] = i;
            }
        }

        private void FixedUpdate()
        {
            if (Grid == null || Queue == null) return;
            if (gone == null) Begin();
            Queue.Tick(Time.fixedDeltaTime);
            if (open) return;
            // Open once every car the AI drives is out. A car the player drives is theirs to bring out.
            open = true;
            for (int i = 0; i < Grid.Length && open; i++)
                if (!gone[i] && drivers[i] != null && drivers[i].enabled) open = false;
            if (open) Debug.Log("Every car is out of the pit lane: pit stops open");
        }

        public override bool Released(AiDriver d) => open;

        public override void Brief(AiDriver d)
        {
            if (d.Racer == null || gone == null) return;
            if (!places.TryGetValue(d, out int place) || gone[place])
            {
                d.Racer.InFormation = false;
                return;
            }
            if (!routes.TryGetValue(d, out AiPilot route))
            {
                route = new AiPilot(Queue.Route, d.Racer.Pilot.Spec, d.Pace, 100f, 100f, d.Seed, lanes: false, maxSpeed: limits);
                routes[d] = route;
            }
            if (route.Index >= 0 && Queue.Out(route.Index))
            {
                gone[place] = true;
                d.Racer.InFormation = false;
                return;
            }
            d.Racer.Formation = route;
            d.Racer.InFormation = true;
            Vector3 at = d.Car.transform.position;
            float follow = PitPlan.Follow(route, at, Grid[place].Id, RaceCar.All, d.Racer.Pilot.Spec);
            d.Racer.FormationCap = Queue.Cap(place, route.Index, at, follow, Ahead(place, route.Index, at));
        }

        /// <summary>Middle to middle along the route to the next car up the queue, m, or infinity once it is out.</summary>
        private float Ahead(int place, int k, Vector3 at)
        {
            if (place == 0 || k < 0 || gone[place - 1]) return float.PositiveInfinity;
            AiDriver next = drivers[place - 1];
            if (next == null || !routes.TryGetValue(next, out AiPilot theirs) || theirs.Index < 0) return float.PositiveInfinity;
            return Queue.ToLine(k, at) - Queue.ToLine(theirs.Index, next.Car.transform.position);
        }
    }
}
