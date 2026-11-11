using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Where a box's stop has got to, as its crew sees it.</summary>
    public enum CrewStage { Home, Ready, Stop, Release }

    /// <summary>
    /// A stop's clock as the crew follows it, read from <see cref="PitService"/> and never written back:
    /// the crew keep time with the stop, the stop never waits for the crew.
    /// </summary>
    public struct StopClock
    {
        public CrewStage Stage;
        /// <summary>Seconds into the stop, and when the fuel and the tyres start and end.</summary>
        public float Elapsed, FuelFrom, FuelTo, TyresFrom, TyresTo, Duration;
        public bool NewTyres, NewDriver;
        /// <summary>The car stands in the box, so the crew walk round it.</summary>
        public bool CarStanding;
        public float CarLength, CarWidth;
    }

    /// <summary>
    /// One piece of a stop's work. Wheels are part k of a person's m in the tyre time, the second person on a
    /// wheel working beside the first. The refueller, the hose holder, the marshal with the extinguisher and the
    /// windscreen cleaner work in the fuel time, the jacks in the tyre time, the lollipop and the driver's door
    /// through the whole stop.
    /// </summary>
    public struct CrewTask
    {
        public enum Kind { Fuel, Wheel, Marshal, Jack, Lollipop, Hose, Door, Screen }

        public Kind Job;
        /// <summary>The wheel, or which jack: front or rear.</summary>
        public PitStopSpots.StopJob Spot;
        public bool Second;
        public int Part, Parts;
    }

    /// <summary>
    /// One person of a box's crew: where they stand in the box's frame, which way they face, and the clip they play.
    /// The box's frame has a along the box the way the car stands in it, and c across it, plus towards the garage.
    /// </summary>
    public sealed class CrewMember
    {
        public Vector2 At, Home;
        /// <summary>Flat unit vector they face, in the box's frame.</summary>
        public Vector2 Facing;
        public readonly List<CrewTask> Tasks = new List<CrewTask>();

        /// <summary>The clip playing: seconds into it, its playback rate and whether it loops.</summary>
        public string Clip;
        public float ClipTime, Rate;
        public bool Loop;
        /// <summary>The clip fading out under it, its time and rate, and its weight, which falls to 0 over <see cref="GarageCrew.Fade"/>.</summary>
        public string Under;
        public float UnderTime, UnderRate, UnderWeight;
        public bool UnderLoop;

        internal bool Crouched;
        internal float Busy;
        internal readonly float IdleFrom;

        public CrewMember(Vector2 home, float idleFrom)
        {
            Home = At = home;
            Facing = new Vector2(0f, -1f);
            IdleFrom = idleFrom;
            Play("Idle", idleFrom, 1f, true);
            UnderWeight = 0f;
        }

        /// <summary>True while down at a wheel, or getting down or up: far away the crew show a crouching figure.</summary>
        public bool Low => Crouched || Clip == "CrouchDown" || Clip == "CrouchUpA" && ClipTime < GarageCrew.CrouchUp * 0.5f;

        internal void Play(string clip, float from, float rate, bool loop)
        {
            if (Clip == clip) return;
            Under = Clip;
            UnderTime = ClipTime;
            UnderRate = Rate;
            UnderLoop = Loop;
            UnderWeight = Clip == null ? 0f : 1f;
            Clip = clip;
            ClipTime = from;
            Rate = rate;
            Loop = loop;
        }

        internal void Advance(float dt)
        {
            ClipTime += dt * Rate;
            UnderTime += dt * UnderRate;
            UnderWeight = Mathf.Max(0f, UnderWeight - dt / GarageCrew.Fade);
        }
    }

    /// <summary>
    /// A box's crew, with no Unity objects so it can be checked outside Unity. They wait in the garage
    /// (<see cref="PitComplex.Standby"/>). When the car is on its way in they walk out to wait beside the box, clear of
    /// the car's way in. Once it stops they take their places round it (<see cref="PitStopSpots.Stop"/>) as each job's
    /// time comes, crouched at the wheels and jacks, and step back once their work is done. When the stop is done they
    /// walk back in. They walk round the nose or tail of a standing car, never through it.
    /// Every person hired has a job, handed out in this order: the refueller, one on each wheel, the marshal, a second
    /// on each wheel, the front and rear jacks, the lollipop, the hose holder, the driver's door, the windscreen. With
    /// fewer than four on the wheels, each does several in turn inside the tyre time; one person alone refuels and
    /// then does all four.
    /// </summary>
    public sealed class GarageCrew
    {
        /// <summary>Crew jog: the walk played this much faster, the ground speed with it so the feet don't slide.</summary>
        public const float WalkRate = 1.5f;
        /// <summary>The walk's ground speed at rate 1, m/s (crew_suit, as Team HQ measures it).</summary>
        public const float WalkSpeed = 1.56f;
        public const float Speed = WalkSpeed * WalkRate;
        /// <summary>Seconds to blend between clips.</summary>
        public const float Fade = 0.25f;
        /// <summary>Lengths of the clips the crew wait on, s, as packed.</summary>
        public const float CrouchDown = 0.625f, CrouchUp = 2.542f;
        private const float DownRate = 1.2f, UpRate = 1.7f;
        /// <summary>Furthest a person's step is, s: a long frame is walked in steps this long.</summary>
        private const float MaxStep = 0.05f;

        public readonly int Garage;
        public readonly CrewMember[] People;
        /// <summary>The box: its middle on the lane, the way the car faces and the way to the garage.</summary>
        public readonly Vector3 Box, Forward, ToGarage;
        /// <summary>How far the garage door is from the middle of the box, m.</summary>
        public readonly float Door;

        private readonly PitStopSpots spots;
        private readonly float floor;
        private float length = -1f, width = -1f;
        private readonly Dictionary<PitStopSpots.StopJob, (Vector2 at, Vector2 face)> placed = new Dictionary<PitStopSpots.StopJob, (Vector2, Vector2)>();

        /// <summary>True once everyone stands at home, idle: nothing to do until the next stop.</summary>
        public bool AtRest { get; private set; }

        public GarageCrew(PitComplex complex, PitStopSpots spots, int garage, int people, int seed)
        {
            this.spots = spots;
            Garage = garage;
            CrewSpot box = spots.Box(garage);
            GarageFrame f = complex.Frame(garage);
            Box = box.Position;
            Forward = box.Facing;
            var left = new Vector3(-Forward.z, 0f, Forward.x);
            ToGarage = Vector3.Dot(f.Out, left) < 0f ? left : -left;
            floor = f.Floor.y;
            Door = Vector3.Dot(f.At(0f, 0f, complex.Depth * 0.5f) - Box, ToGarage);
            var rng = new System.Random(seed);
            int n = Mathf.Clamp(people, 0, PitComplex.StandbySpots);
            People = new CrewMember[n];
            for (int i = 0; i < n; i++) People[i] = new CrewMember(Local(complex.Standby(garage, i).Position), (float)rng.NextDouble() * 2.5f);
            Plan();
            AtRest = true;
        }

        /// <summary>A point in the world to the box's frame.</summary>
        public Vector2 Local(Vector3 p)
        {
            Vector3 d = p - Box;
            return new Vector2(d.x * Forward.x + d.z * Forward.z, d.x * ToGarage.x + d.z * ToGarage.z);
        }

        /// <summary>A point in the box's frame in the world: on the garage floor inside the door, on the lane out to the box.</summary>
        public Vector3 World(Vector2 p)
        {
            Vector3 w = Box + Forward * p.x + ToGarage * p.y;
            w.y = p.y >= Door ? floor : Mathf.Lerp(Box.y, floor, Mathf.Clamp01(p.y / Door));
            return w;
        }

        /// <summary>A direction in the box's frame in the world.</summary>
        public Vector3 WorldDirection(Vector2 d) => Forward * d.x + ToGarage * d.y;

        private static readonly PitStopSpots.StopJob[] Wheels =
            { PitStopSpots.StopJob.RearRight, PitStopSpots.StopJob.FrontRight, PitStopSpots.StopJob.FrontLeft, PitStopSpots.StopJob.RearLeft };

        /// <summary>The jobs after the refueller and the first on each wheel, in the order they are handed out.</summary>
        private static readonly CrewTask[] Later =
        {
            new CrewTask { Job = CrewTask.Kind.Marshal },
            new CrewTask { Job = CrewTask.Kind.Wheel, Spot = Wheels[0], Second = true, Parts = 1 },
            new CrewTask { Job = CrewTask.Kind.Wheel, Spot = Wheels[1], Second = true, Parts = 1 },
            new CrewTask { Job = CrewTask.Kind.Wheel, Spot = Wheels[2], Second = true, Parts = 1 },
            new CrewTask { Job = CrewTask.Kind.Wheel, Spot = Wheels[3], Second = true, Parts = 1 },
            new CrewTask { Job = CrewTask.Kind.Jack, Spot = PitStopSpots.StopJob.FrontJack },
            new CrewTask { Job = CrewTask.Kind.Jack, Spot = PitStopSpots.StopJob.RearJack },
            new CrewTask { Job = CrewTask.Kind.Lollipop },
            new CrewTask { Job = CrewTask.Kind.Hose },
            new CrewTask { Job = CrewTask.Kind.Door },
            new CrewTask { Job = CrewTask.Kind.Screen },
        };

        /// <summary>Most people a stop has work for.</summary>
        public static int StopCrew => 5 + Later.Length;

        /// <summary>Hands out the stop's jobs, one person each, in order: refueller, the wheels, then the rest.</summary>
        private void Plan()
        {
            int n = People.Length;
            if (n == 0) return;
            People[0].Tasks.Add(new CrewTask { Job = CrewTask.Kind.Fuel });
            int wheelers = Mathf.Min(n - 1, 4);
            if (wheelers == 0)
                for (int k = 0; k < 4; k++) People[0].Tasks.Add(new CrewTask { Job = CrewTask.Kind.Wheel, Spot = Wheels[k], Part = k, Parts = 4 });
            else
            {
                var mine = new List<PitStopSpots.StopJob>[wheelers];
                for (int m = 0; m < wheelers; m++) mine[m] = new List<PitStopSpots.StopJob>();
                for (int k = 0; k < 4; k++) mine[k % wheelers].Add(Wheels[k]);
                for (int m = 0; m < wheelers; m++)
                    for (int k = 0; k < mine[m].Count; k++)
                        People[1 + m].Tasks.Add(new CrewTask { Job = CrewTask.Kind.Wheel, Spot = mine[m][k], Part = k, Parts = mine[m].Count });
            }
            for (int p = 1 + wheelers, k = 0; p < n && k < Later.Length; p++, k++) People[p].Tasks.Add(Later[k]);
        }

        /// <summary>Where each job stands round a car of this size, in the box's frame, facing the car.</summary>
        private void Measure(float carLength, float carWidth)
        {
            if (carLength == length && carWidth == width) return;
            length = carLength;
            width = carWidth;
            placed.Clear();
            foreach (PitStopSpots.StopJob job in (PitStopSpots.StopJob[])System.Enum.GetValues(typeof(PitStopSpots.StopJob)))
            {
                CrewSpot s = spots.Stop(Garage, job, carLength, carWidth);
                Vector3 f = s.Facing;
                placed[job] = (Local(s.Position), new Vector2(f.x * Forward.x + f.z * Forward.z, f.x * ToGarage.x + f.z * ToGarage.z));
            }
        }

        private float HalfLength => length * 0.5f;

        /// <summary>A task's place at the stop and the way it faces.</summary>
        private (Vector2 at, Vector2 face) Place(CrewTask t)
        {
            (Vector2 at, Vector2 face) fuel = placed[PitStopSpots.StopJob.Fuel];
            float side = width * 0.5f;
            switch (t.Job)
            {
                case CrewTask.Kind.Wheel:
                    (Vector2 at, Vector2 face) w = placed[t.Spot];
                    // The second on a wheel works beside the first, towards the car's nearer end.
                    return t.Second ? (w.at + new Vector2(Mathf.Sign(w.at.x) * 0.75f, 0f), w.face) : w;
                case CrewTask.Kind.Jack:
                case CrewTask.Kind.Lollipop:
                    return placed[t.Job == CrewTask.Kind.Lollipop ? PitStopSpots.StopJob.Lollipop : t.Spot];
                // The marshal stands a step behind the refueller and out from him, the hose holder just behind him.
                case CrewTask.Kind.Marshal: return (fuel.at + new Vector2(-1.0f, 0.7f), new Vector2(0.4f, -1f).normalized);
                case CrewTask.Kind.Hose: return (fuel.at + new Vector2(0.3f, 1.0f), new Vector2(-0.3f, -1f).normalized);
                // The driver's door is on the garage side, by the cockpit; the windscreen is cleaned from the far side.
                case CrewTask.Kind.Door: return (new Vector2(0.25f, side + 0.65f), new Vector2(0f, -1f));
                case CrewTask.Kind.Screen: return (new Vector2(0.55f, -side - 0.5f), new Vector2(0f, 1f));
                default: return fuel;
            }
        }

        /// <summary>
        /// Where a task waits while the car comes in, and once its work is done: beside the box on the garage side,
        /// out of the car's way. Those working the far side or the car's ends wait off its nose and tail, to walk round
        /// once it stops.
        /// </summary>
        private Vector2 Ready(CrewTask t)
        {
            float l = HalfLength;
            switch (t.Job)
            {
                case CrewTask.Kind.Jack: return new Vector2(t.Spot == PitStopSpots.StopJob.FrontJack ? l + 1.0f : -l - 1.0f, 3.0f);
                case CrewTask.Kind.Lollipop: return new Vector2(l + 2.6f, 3.0f);
                case CrewTask.Kind.Screen: return new Vector2(l + 1.8f, 3.8f);
            }
            Vector2 at = Place(t).at;
            if (at.y < 0f) return new Vector2(Mathf.Sign(at.x) * (l + (t.Second ? 1.9f : 1.1f)), 2.0f);
            return new Vector2(at.x, at.y + 1.3f);
        }

        /// <summary>The time a task is worked in, s into the stop; empty when the stop has no such work.</summary>
        private static (float from, float to) Window(CrewTask t, in StopClock c)
        {
            switch (t.Job)
            {
                case CrewTask.Kind.Wheel:
                    if (!c.NewTyres) return (0f, 0f);
                    float span = (c.TyresTo - c.TyresFrom) / t.Parts;
                    return (c.TyresFrom + span * t.Part, c.TyresFrom + span * (t.Part + 1));
                case CrewTask.Kind.Jack: return c.NewTyres ? (c.TyresFrom, c.TyresTo) : (0f, 0f);
                case CrewTask.Kind.Lollipop: return (0f, c.Duration);
                case CrewTask.Kind.Door: return c.NewDriver ? (0f, c.Duration) : (0f, 0f);
                default: return (c.FuelFrom, c.FuelTo);
            }
        }

        private static bool Crouches(CrewTask t) => t.Job == CrewTask.Kind.Wheel || t.Job == CrewTask.Kind.Jack;

        /// <summary>Where a person wants to be, which way to face, and whether crouched, at this point of the stop.</summary>
        private (Vector2 at, Vector2 face, bool low) Want(CrewMember m, in StopClock c)
        {
            var home = (m.Home, new Vector2(0f, -1f), false);
            if (m.Tasks.Count == 0 || c.Stage == CrewStage.Home || c.Stage == CrewStage.Release) return home;
            if (c.Stage == CrewStage.Ready)
            {
                Vector2 r = Ready(m.Tasks[0]);
                return (r, (-r).normalized, false);
            }
            // At the stop: the first task with work still to come. With none left, wait clear of the car.
            foreach (CrewTask t in m.Tasks)
            {
                (float from, float to) w = Window(t, c);
                if (w.to <= w.from || w.to <= c.Elapsed) continue;
                (Vector2 at, Vector2 face) p = Place(t);
                return (p.at, p.face, Crouches(t) && c.Elapsed >= w.from);
            }
            Vector2 back = Ready(m.Tasks[m.Tasks.Count - 1]);
            return (back, (-back).normalized, false);
        }

        /// <summary>Is a point in the way of the standing car, with a little room round it?</summary>
        private bool InCar(Vector2 p) => Mathf.Abs(p.x) < HalfLength + 0.5f && Mathf.Abs(p.y) < width * 0.5f + 0.35f;

        /// <summary>The next point to walk to: straight there, or out past the car's nose or tail and across.</summary>
        private Vector2 Waypoint(Vector2 from, Vector2 to, bool standing)
        {
            if (!standing) return to;
            bool hits = false;
            for (int k = 1; k < 12 && !hits; k++) hits = InCar(Vector2.Lerp(from, to, k / 12f));
            if (!hits) return to;
            float end = (from.x + to.x >= 0f ? 1f : -1f) * (HalfLength + 0.9f);
            float across = width * 0.5f + 0.95f;
            if (Mathf.Abs(from.x) < HalfLength + 0.8f) return new Vector2(end, (from.y >= 0f ? 1f : -1f) * across);
            return new Vector2(end, (to.y >= 0f ? 1f : -1f) * across);
        }

        /// <summary>Moves the crew on by dt of race time, following the stop's clock.</summary>
        public void Step(in StopClock clock, float dt)
        {
            if (AtRest && (clock.Stage == CrewStage.Home || People.Length == 0)) return;
            if (clock.CarLength > 0f) Measure(clock.CarLength, clock.CarWidth);
            else if (length < 0f) Measure(4.6f, 1.9f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep));
            float h = dt / steps;
            bool rest = true;
            foreach (CrewMember m in People)
            {
                for (int s = 0; s < steps; s++) Move(m, clock, h);
                rest &= clock.Stage == CrewStage.Home && m.At == m.Home && m.Clip == "Idle" && !m.Crouched && m.Busy <= 0f;
            }
            AtRest = rest;
        }

        private void Move(CrewMember m, in StopClock c, float dt)
        {
            m.Advance(dt);
            (Vector2 at, Vector2 face, bool low) want = Want(m, c);
            Vector2 to = want.at - m.At;
            bool far = to.sqrMagnitude > 0.06f * 0.06f;
            if (m.Busy > 0f)
            {
                m.Busy -= dt;
                return;
            }
            if (m.Crouched && (far || !want.low))
            {
                m.Play("CrouchUpA", 0f, UpRate, false);
                m.Crouched = false;
                m.Busy = CrouchUp / UpRate - 0.3f;
                return;
            }
            if (far)
            {
                Vector2 wp = Waypoint(m.At, want.at, c.CarStanding) - m.At;
                float dist = wp.magnitude;
                Vector2 dir = wp / Mathf.Max(dist, 1e-4f);
                m.Facing = Turn(m.Facing, dir, dt * 9f);
                float go = Mathf.Min(dist, Speed * dt * Mathf.Max(0f, Vector2.Dot(m.Facing, dir)));
                m.At += dir * go;
                m.Play("Walk", 0f, WalkRate, true);
                return;
            }
            m.At = want.at;
            m.Facing = Turn(m.Facing, want.face, dt * 7f);
            if (want.low && !m.Crouched)
            {
                m.Play("CrouchDown", 0f, DownRate, false);
                m.Crouched = true;
                m.Busy = CrouchDown / DownRate - 0.1f;
                return;
            }
            if (m.Crouched) m.Play("Crouch", 0f, 1f, true);
            else m.Play("Idle", m.IdleFrom, 1f, true);
        }

        /// <summary>Turns a facing towards another by a share of the way, kept a unit vector.</summary>
        private static Vector2 Turn(Vector2 from, Vector2 to, float share)
        {
            if (to.sqrMagnitude < 1e-6f) return from;
            float a = Mathf.Atan2(from.y, from.x), b = Mathf.Atan2(to.y, to.x);
            float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float r = a + d * Mathf.Min(1f, share);
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }
    }
}
