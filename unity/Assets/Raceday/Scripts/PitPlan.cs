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
    /// <see cref="StopPlanner"/>, the player's, sets the litres, the tyres and the repairs from its
    /// <see cref="PitOrders"/>, which go back to the crew's call once the stop starts. A car with no planner
    /// fills the tank and changes the tyres. The car's Pit Stop Time stat makes the crew quicker or slower. In
    /// a career race the car's <see cref="CarRaceLog"/> decides the rest: tyres only while a set is left, the
    /// next driver in, the issues fixed while the spares last, and the crew's size and Engineer skill scale the work.
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
            StopPlan plan = supplies.Planner != null ? supplies.Planner.Plan() : StopPlan.Default(supplies);
            litres = Mathf.Clamp(plan.Litres, 0f, Mathf.Max(supplies.Capacity - supplies.Fuel, 0f));
            scale = Scale(spec);
            PitWork work = supplies.Log?.Pit(plan.Tyres, plan.Fix, plan.DamageTo);
            tyres = work == null ? plan.Tyres : work.ChangeTyres;
            NewTyres = tyres;
            NewDriver = work?.NewDriver != null;
            if (NewDriver) supplies.Stint.NewStint();
            supplies.Orders.Reset();
            if (work != null) scale *= (float)work.CrewMultiplier;
            float extra = work != null ? (float)work.ExtraSeconds : 0f;
            Duration = (Settle + litres / RefuelRate + (tyres ? TyreChange : 0f)) * scale + extra;
            // A stop resets the engine map to Normal, so a driver chooses Push or Conserve again.
            supplies.Map = EngineMap.Normal;
            if (supplies.Log != null) supplies.Effects = supplies.Log.Effects;
        }

        /// <summary>How the car's Pit Stop stat scales the work: 0.7 times at 0, 1.3 times at 100.</summary>
        private static float Scale(CarSpec spec) => 0.7f + 0.6f * (float)spec.Stats.PitStopTime / 100f;

        /// <summary>Seconds a stop would take: the fuel, the tyres and the <paramref name="extra"/> work, scaled as a stop scales them.</summary>
        public static float Seconds(CarSpec spec, float litres, bool tyres, CarRaceLog log, double extra)
        {
            float scale = Scale(spec);
            if (log != null) scale *= (float)PitStop.CrewMultiplier(log.Car.CrewSize, log.Car.EngineerSkill);
            return (Settle + litres / RefuelRate + (tyres ? TyreChange : 0f)) * scale + (float)extra;
        }

        /// <summary>Seconds in the box each litre takes, before the crew's scaling.</summary>
        public static double SecondsPerLitre(CarSpec spec) => Scale(spec) / RefuelRate;

        /// <summary>When the fuel starts and stops going in, s into the stop: the crew's cue, never their doing.</summary>
        public float FuelFrom => Settle * scale;
        public float FuelTo => (Settle + litres / RefuelRate) * scale;
        /// <summary>When the tyre change starts and ends, s into the stop: both at the end of the fuel when the tyres stay on.</summary>
        public float TyresFrom => FuelTo;
        public float TyresTo => FuelTo + (tyres ? TyreChange * scale : 0f);

        public void Step(float dt)
        {
            if (Done) return;
            Elapsed += dt;
            float refuel = Mathf.Clamp01((Elapsed / scale - Settle) / Mathf.Max(litres / RefuelRate, 0.01f));
            supplies.Fuel = startFuel + litres * refuel;
            Fuelled = refuel >= 1f;
            if (!Done) return;
            supplies.Fuel = startFuel + litres;
            if (tyres)
            {
                supplies.Wear = 0f;
                supplies.Stint.NewTyres();
            }
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
        private const float Reaction = 0.4f;        // s a car leaving keeps in hand behind the car ahead
        /// <summary>The car comes in before its tyres are this worn.</summary>
        public const float WearLimit = 0.9f;
        /// <summary>A car this damaged or worse comes in for repairs (<see cref="CarDamage"/>).</summary>
        public const double DamageLimit = 40;

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
            // A car whose race is over comes in at the first chance and stays, and a car the team calls in comes in.
            if (s.RaceOver || s.Orders.Requested) return true;
            // Another lap must leave a fifth of a lap's fuel spare, and the tyres short of worn out.
            // With no tyre sets left a stop can't change them, so only the fuel brings the car in.
            // A badly damaged car comes in for repairs while there are spares to make them with.
            return s.Fuel < FuelPerLap * 1.2f + 0.5f || (s.TyresLeft && s.Wear + WearPerLap > WearLimit)
                || (s.Log != null && s.Log.Damage >= DamageLimit && s.Log.SpareParts > 0);
        }

        /// <summary>Takes the pit route this lap: a pilot that drives it, built from the car's racing line.</summary>
        public void Box(AiPilot racing, int seed)
        {
            RacingLine route = Lane.Route(racing.Line, Garage);
            Route = new AiPilot(route, racing.Spec, racing.Pace, 100f, 100f, seed, lanes: false, maxSpeed: limits);
            State = Stage.ToBox;
        }

        /// <summary>
        /// Takes over a car someone else drove into the pit lane, at track sample <paramref name="k"/>: on to the box
        /// if it is still ahead, on with the <paramref name="service"/> already under way in it, or out of the lane
        /// once the box is behind or the stop is done.
        /// </summary>
        public void TakeOver(AiPilot racing, int seed, int k, PitService service)
        {
            Box(racing, seed);
            if (service != null)
            {
                Service = service;
                State = service.Done ? Stage.Leaving : Stage.Stopped;
                return;
            }
            Circuit c = Lane.Circuit;
            int ahead = c.Wrap(boxSample - k);
            if (ahead == 0 || ahead > c.Count / 2) State = Stage.Leaving;
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
                        if (s.RaceOver) return true;   // parked for good: no service
                        Service = new PitService(s, spec);
                        Stops++;
                        lapFuel = -1f;
                        return true;
                    }
                    return false;
                }
                case Stage.Stopped:
                    if (s.RaceOver || Service == null) return true;
                    Service.Step(dt);
                    if (Service.Done && FastLaneClear(id, cars)) State = Stage.Leaving;
                    return State == Stage.Stopped;
                case Stage.Leaving:
                    p.SpeedCap = Follow(p, position, id, cars, spec, Reaction);
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
        /// standing still beside the route, as in a box, is passed. With a <c>reaction</c> time it also keeps
        /// that long in hand at its speed, so it can follow a car braking hard for the exit road's bends.
        /// </summary>
        public static float Follow(AiPilot route, Vector3 position, int id, IReadOnlyList<CarState> cars, CarSpec spec, float reaction = 0f)
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
                float lag = brake * reaction;
                float c = gap > 0f ? Mathf.Max(Mathf.Sqrt(lag * lag + theirs * theirs + 2f * brake * gap) - lag, 0f) : 0f;
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
                // Out past the line cars take by the boxes: a car swinging into a nearby box isn't fast lane traffic.
                if (moving && a > -110f && a < 8f && across < Lane.PassLateral - Lane.BoxLateral - 0.3f && across > -12f) return false;
            }
            return true;
        }
    }
}
