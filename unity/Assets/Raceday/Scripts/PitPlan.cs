using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Where a car is and how it is moving, for cars that need more than the track position.</summary>
    public struct CarState
    {
        public int Id;
        public Vector3 Position;
        public Vector3 Velocity;
    }

    /// <summary>
    /// A stop in the box: refuelling, then a new set of tyres, with the clock running. The car's
    /// <see cref="PitOrders"/> can ask for fewer litres than a full tank or for the tyres to stay on,
    /// and are put back to a full tank and new tyres once the stop starts. The
    /// car's Pit Stop Time stat makes the crew quicker or slower. In a career race the car's
    /// <see cref="CarRaceLog"/> decides the rest: tyres only while a set is left, the next driver in,
    /// the active issues fixed while the spares last, and the crew's size and Engineer skill scale the work.
    /// </summary>
    public sealed class PitService
    {
        private const float Settle = 2f;           // s before the fuel goes in
        private const float RefuelRate = 3f;       // L/s
        private const float TyreChange = 6f;       // s

        public readonly float Duration;
        public float Elapsed { get; private set; }
        public bool Done => Elapsed >= Duration;
        /// <summary>True once the fuel is in.</summary>
        public bool Fuelled { get; private set; }
        /// <summary>True when the stop changes the tyres, and when it changes the driver.</summary>
        public readonly bool NewTyres, NewDriver;

        private readonly Consumables supplies;
        private readonly float startFuel, litres, scale;
        private readonly bool tyres;

        public PitService(Consumables supplies, CarSpec spec)
        {
            this.supplies = supplies;
            startFuel = supplies.Fuel;
            PitOrders orders = supplies.Orders;
            litres = Mathf.Clamp(orders.Litres ?? float.PositiveInfinity, 0f, Mathf.Max(supplies.Capacity - supplies.Fuel, 0f));
            scale = 0.85f + 0.3f * (float)spec.Stats.PitStopTime / 100f;
            PitWork work = supplies.Log?.Pit(orders.Tyres);
            tyres = work == null ? orders.Tyres : work.ChangeTyres;
            NewTyres = tyres;
            NewDriver = work?.NewDriver != null;
            orders.Reset();
            if (work != null) scale *= (float)work.CrewMultiplier;
            float extra = work != null ? (float)work.ExtraSeconds : 0f;
            Duration = (Settle + litres / RefuelRate + (tyres ? TyreChange : 0f)) * scale + extra;
            // A stop resets the engine map to Normal, so a driver chooses Push or Conserve again.
            supplies.Map = EngineMap.Normal;
            if (supplies.Log != null) supplies.Effects = supplies.Log.Effects;
        }

        public void Step(float dt)
        {
            if (Done) return;
            Elapsed += dt;
            float refuel = Mathf.Clamp01((Elapsed / scale - Settle) / Mathf.Max(litres / RefuelRate, 0.01f));
            supplies.Fuel = startFuel + litres * refuel;
            Fuelled = refuel >= 1f;
            if (!Done) return;
            supplies.Fuel = startFuel + litres;
            if (tyres) supplies.Wear = 0f;
            supplies.Log?.PitDone(Duration, litres);
        }
    }

    /// <summary>
    /// When an AI car pits and how it drives the stop, with no Unity objects so it can be tested
    /// outside Unity. Each lap, a little before the pit entry, it checks whether the fuel or the tyres
    /// will last another lap. If not, the car takes its pit route, slows to the limiter, stops in its
    /// own box, is serviced, waits for the fast lane to be clear and rejoins. A car the team calls in
    /// (<see cref="PitOrders.Requested"/>) comes in at the next pit entry. A retired car comes in
    /// at the first pit entry and parks in its box for good.
    /// </summary>
    public sealed class PitPlan
    {
        public enum Stage { Racing, ToBox, Stopped, Leaving }

        /// <summary>The car decides this far before the pit entry, m.</summary>
        public const float DecideBefore = 450f;
        private const float StopBraking = 3.5f;     // m/s² into the box
        private const float CarLength = 4.8f;
        /// <summary>The car comes in before its tyres are this worn.</summary>
        public const float WearLimit = 0.9f;

        public readonly PitLane Lane;
        public readonly int Garage;
        public Stage State { get; private set; }
        /// <summary>The pilot for the pit route, while the car is pitting.</summary>
        public AiPilot Route { get; private set; }
        public PitService Service { get; private set; }
        public int Stops { get; private set; }
        /// <summary>Fuel and wear the last full lap used.</summary>
        public float FuelPerLap { get; private set; }
        public float WearPerLap { get; private set; }

        private readonly float[] limits;
        private readonly int decideSample, boxSample, rejoinSample;
        private readonly Vector3 boxPoint;
        private int lastIndex = -1;
        private float lapFuel = -1f, lapWear = -1f;

        public PitPlan(PitLane lane, int garage, Consumables supplies)
        {
            Lane = lane;
            Garage = garage;
            Circuit c = lane.Circuit;
            limits = lane.Limits();
            decideSample = c.IndexOf(lane.Data.entry - DecideBefore);
            boxSample = lane.BoxSample(garage);
            rejoinSample = lane.RejoinedSample();
            boxPoint = lane.BoxPoint(garage);
            FuelPerLap = supplies.FuelPerLap;
            WearPerLap = supplies.WearPerLap;
        }

        /// <summary>
        /// Called each step while racing, with the car's track sample: as it passes the decision
        /// point it measures the last lap and returns true if the car should pit this time by.
        /// </summary>
        public bool Due(int k, Consumables s)
        {
            Circuit c = Lane.Circuit;
            int moved = lastIndex < 0 ? 0 : c.Wrap(k - lastIndex);
            int toDecide = lastIndex < 0 ? 0 : c.Wrap(decideSample - lastIndex);
            bool crossed = moved > 0 && moved < 50 && toDecide > 0 && toDecide <= moved;
            lastIndex = k;
            if (!crossed) return false;
            if (lapFuel >= 0f && s.Fuel < lapFuel && s.Wear > lapWear)
            {
                FuelPerLap = lapFuel - s.Fuel;
                WearPerLap = s.Wear - lapWear;
            }
            lapFuel = s.Fuel;
            lapWear = s.Wear;
            // A retired car limps in at the first chance and stays, and a car the team calls in comes in.
            if (s.Retired || s.Orders.Requested) return true;
            // Another lap must leave a fifth of a lap's fuel spare, and the tyres short of worn out.
            // With no tyre sets left a stop can't change them, so only the fuel brings the car in.
            return s.Fuel < FuelPerLap * 1.2f + 0.5f || (s.TyresLeft && s.Wear + WearPerLap > WearLimit);
        }

        /// <summary>Takes the pit route this lap: a pilot that drives it, built from the car's racing line.</summary>
        public void Box(AiPilot racing, int seed)
        {
            RacingLine route = Lane.Route(racing.Line, Garage);
            Route = new AiPilot(route, racing.Spec, racing.Pace, 100f, 100f, seed, lanes: false, maxSpeed: limits);
            State = Stage.ToBox;
        }

        /// <summary>Gives up on the stop, after the car is moved back to the track.</summary>
        public void Abort()
        {
            State = Stage.Racing;
            Route = null;
            Service = null;
            lastIndex = -1;
        }

        /// <summary>
        /// One step of a stop, before the route pilot drives: sets how fast the car may go, and
        /// returns true while it must stand still in its box.
        /// </summary>
        public bool Step(Vector3 position, Vector3 velocity, Consumables s, CarSpec spec, int id, IReadOnlyList<CarState> cars, float dt)
        {
            Circuit c = Lane.Circuit;
            AiPilot p = Route;
            p.SpeedScale = 1f;
            p.SpeedCap = float.PositiveInfinity;
            if (p.Index < 0) return false;
            float speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            switch (State)
            {
                case Stage.ToBox:
                {
                    float toBox = ToBox(position, p.Index);
                    p.SpeedCap = Mathf.Min(Follow(p, position, id, cars, spec), Mathf.Sqrt(2f * StopBraking * Mathf.Max(toBox - 0.3f, 0f)));
                    if (toBox < 0.5f || (toBox < 1.5f && speed < 0.5f))
                    {
                        State = Stage.Stopped;
                        if (s.Retired) return true;   // parked for good: no service
                        Service = new PitService(s, spec);
                        Stops++;
                        lapFuel = -1f;
                        return true;
                    }
                    return false;
                }
                case Stage.Stopped:
                    if (s.Retired || Service == null) return true;
                    Service.Step(dt);
                    if (Service.Done && FastLaneClear(id, cars)) State = Stage.Leaving;
                    return State == Stage.Stopped;
                case Stage.Leaving:
                    p.SpeedCap = Follow(p, position, id, cars, spec);
                    int past = c.Wrap(p.Index - boxSample);
                    if (past < c.Count / 2 && past >= c.Wrap(rejoinSample - boxSample)) State = Stage.Racing;
                    return false;
            }
            return false;
        }

        /// <summary>Distance along the route to the middle of the box, m, negative once past it.</summary>
        private float ToBox(Vector3 position, int index)
        {
            Circuit c = Lane.Circuit;
            int ahead = c.Wrap(boxSample - index);
            if (ahead > c.Count / 2) ahead -= c.Count;
            if (Mathf.Abs(ahead) > 15) return ahead * c.Step;
            Vector3 t = Route.Line.Tangent[boxSample];
            return (boxPoint.x - position.x) * t.x + (boxPoint.z - position.z) * t.z;
        }

        /// <summary>
        /// Fastest a car driving a route may go to keep a safe gap to the nearest car ahead on it, m/s:
        /// it could stop in its own braking distance if that car stopped as hard as it can. A car
        /// standing still beside the route, as in a box, is passed.
        /// </summary>
        public static float Follow(AiPilot route, Vector3 position, int id, IReadOnlyList<CarState> cars, CarSpec spec)
        {
            RacingLine line = route.Line;
            int k = route.Index;
            if (k < 0) return float.PositiveInfinity;
            Vector3 t = line.Tangent[k];
            float brake = spec.BrakeDecel * 9.81f * 0.8f;
            float cap = float.PositiveInfinity;
            for (int i = 0; i < cars.Count; i++)
            {
                CarState o = cars[i];
                if (o.Id == id) continue;
                float dx = o.Position.x - position.x, dz = o.Position.z - position.z;
                float along = dx * t.x + dz * t.z;
                if (along <= 0f || along > 80f || dx * dx + dz * dz > 100f * 100f) continue;
                int j = line.Nearest(o.Position, k, 0, Mathf.CeilToInt(90f / line.Circuit.Step));
                Vector3 off = o.Position - line.Point[j];
                // A car standing in a box beside the route is passed; one moving may be pulling into the way.
                bool still = o.Velocity.x * o.Velocity.x + o.Velocity.z * o.Velocity.z < 0.25f;
                float reach = still ? 2.4f : 3.2f;
                if (off.x * off.x + off.z * off.z > reach * reach) continue;
                float gap = line.Circuit.Wrap(j - k) * line.Circuit.Step - CarLength - 2f;
                Vector3 tj = line.Tangent[j];
                float theirs = Mathf.Max(o.Velocity.x * tj.x + o.Velocity.z * tj.z, 0f);
                float c = gap > 0f ? Mathf.Sqrt(theirs * theirs + 2f * brake * gap) : 0f;
                cap = Mathf.Min(cap, c);
            }
            return cap;
        }

        /// <summary>No car coming down the fast lane behind the box that the car would pull out in front of.</summary>
        private bool FastLaneClear(int id, IReadOnlyList<CarState> cars)
        {
            Vector3 left = Lane.LaneLeft(Lane.BoxLane[Garage]);
            var along = new Vector3(left.z, 0f, -left.x);
            for (int i = 0; i < cars.Count; i++)
            {
                CarState o = cars[i];
                if (o.Id == id) continue;
                Vector3 d = o.Position - boxPoint;
                float a = d.x * along.x + d.z * along.z;
                float across = d.x * left.x + d.z * left.z;
                bool moving = o.Velocity.x * o.Velocity.x + o.Velocity.z * o.Velocity.z > 9f;
                if (moving && a > -110f && a < 8f && across < -Lane.BoxLateral + Lane.FastLane + 2.5f && across > -12f) return false;
            }
            return true;
        }
    }
}
