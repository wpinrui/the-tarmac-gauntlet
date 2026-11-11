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
        /// <summary>The car's middle in the box's frame (<see cref="GarageCrew.Local"/>), its speed, m/s, and whether it is near the box at all.</summary>
        public Vector2 Car;
        public float CarSpeed;
        public bool CarNear;
        public float CarLength, CarWidth;
    }

    /// <summary>
    /// One piece of a stop's work. Wheels are part k of a person's m in the tyre time, the second person on a
    /// wheel working beside the first. The refueller, the hose holder and the marshal with the extinguisher work in
    /// the fuel time, the jacks in the tyre time, the lollipop, the driver's door and the windscreen through the stop.
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

        /// <summary>True while at their place inside its time: the work is going on.</summary>
        public bool Working { get; internal set; }
        /// <summary>The stage of the stop as this person last saw it.</summary>
        public CrewStage Stage { get; internal set; }

        /// <summary>
        /// Where in the garage, in the box's frame, this person picks up the kit they carry to the stop, or null with
        /// none: they go there first as the car comes in and take it back there after the stop. With <see cref="FetchTyre"/>
        /// it is a fresh tyre, fetched only for a stop that changes the tyres.
        /// </summary>
        public Vector2? FetchAt;
        public bool FetchTyre;
        /// <summary>True from picking the kit up at <see cref="FetchAt"/> until putting it back there.</summary>
        public bool Carrying { get; internal set; }
        /// <summary>True once the fetch is let go for this stop: the car would be in before they were back out.</summary>
        internal bool FetchLetGo;

        internal bool Crouched;
        internal float Busy;
        /// <summary>The task being worked or walked to, and how long it has been worked.</summary>
        internal int Next;
        internal float Worked;
        /// <summary>How much quicker than usual they move: one with several wheels hurries when the tyre time is short.</summary>
        internal float Pace = 1f;
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
    /// A box's crew, with no Unity objects so it can be checked outside Unity (tools/pit-crew). They wait in the garage
    /// (<see cref="PitComplex.Standby"/>). When the car is on its way in they walk out to wait beside the box, clear of
    /// the car's way in, the lollipop on the lane in front of it. Once it stops they take their places round it
    /// (<see cref="PitStopSpots.Stop"/>), following the car if it stops off its mark, as each job's time comes, crouched
    /// at the wheels and jacks, and step back to the garage side once their work is done. Work in the car's way out ends
    /// a little before the release, so the car leaves clear. When the stop is done they walk back in, the far side round
    /// the tail. They walk round the nose or tail of the car, never through it, and keep out of the fast lane.
    /// Every person hired has a job, handed out in this order: the refueller, one on each wheel, the marshal, a second
    /// on each wheel, the front and rear jacks, the lollipop, the hose holder, the driver's door, the windscreen. With
    /// fewer than four on the wheels, each does several in turn inside the tyre time, hurrying when it is short; one
    /// person alone refuels and then does all four. A job over too soon to walk to is let go.
    /// </summary>
    public sealed class GarageCrew
    {
        /// <summary>Crew jog: the walk played this much faster, the ground speed with it so the feet don't slide.</summary>
        public const float WalkRate = 1.5f;
        /// <summary>The walk's ground speed at rate 1, m/s (crew_suit, as Team HQ measures it).</summary>
        public const float WalkSpeed = 1.56f;
        public const float Speed = WalkSpeed * WalkRate;
        /// <summary>The walk played faster still for a run, between the wheels when one person does several.</summary>
        public const float RunRate = 2.1f;
        /// <summary>The most one with several wheels hurries: running, getting down and up this much quicker still.</summary>
        public const float MaxPace = 1.7f;
        /// <summary>
        /// Work in the car's way out, in front of its nose or on the far side ahead of its tail, ends this long before
        /// the release, s, so the crew are clear when it goes: longer for those who crouch, as standing up takes time.
        /// </summary>
        public const float ClearStanding = 1.4f, ClearCrouched = 2.4f;
        /// <summary>No one walks round the car's nose this close to the release, s: it is the way out.</summary>
        public const float NoseShut = 4f;
        /// <summary>Seconds to blend between clips.</summary>
        public const float Fade = 0.25f;
        /// <summary>Lengths of the clips the crew wait on, s, as packed.</summary>
        public const float CrouchDown = 0.625f, CrouchUp = 2.542f;
        private const float DownRate = 1.2f, UpRate = 1.7f, QuickDownRate = 1.8f, QuickUpRate = 3.4f;
        /// <summary>Least work a person with several wheels gives each, s, and the time they allow for moving between them.</summary>
        private const float MinWheel = 0.3f, MoveBetween = 2.2f;
        /// <summary>Least time worth walking to a job for, s: with less left, the job is let go.</summary>
        private const float MinWork = 0.3f;
        /// <summary>Seconds to pick up or put down kit fetched from the garage.</summary>
        public const float FetchTime = 0.4f;
        /// <summary>Time to spare after fetching kit, s: the fetch is let go unless they would be out at their spot this long before the car stops.</summary>
        private const float FetchSpare = 1.5f;
        /// <summary>Furthest a person's step is, s: a long frame is walked in steps this long.</summary>
        private const float MaxStep = 0.05f;

        public readonly int Garage;
        public readonly CrewMember[] People;
        /// <summary>The box: its middle on the lane, the way the car faces and the way to the garage.</summary>
        public readonly Vector3 Box, Forward, ToGarage;
        /// <summary>How far the garage door is from the middle of the box, m.</summary>
        public readonly float Door;
        /// <summary>+1 when the car's driver's door is on the garage side, -1 when on the far side (<see cref="DriverSwap.DoorSide"/>).</summary>
        public int DoorSide = 1;

        private readonly PitStopSpots spots;
        private readonly float floor;
        private float length = -1f, width = -1f;
        /// <summary>The car this step, in the box's frame, and where it stopped: the stop's places follow a car that stops off the mark.</summary>
        private Vector2 car, stopped;
        private bool near, doorway;
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

        /// <summary>
        /// Hands out the stop's jobs, one person each, in order: refueller, the wheels, then the rest. With fewer than four
        /// on the wheels they share them out by side, so no one crosses the car between two: with three, one does the
        /// garage side's two; with two, one a side; one alone does the garage side and then round the nose to the far side.
        /// </summary>
        private void Plan()
        {
            int n = People.Length;
            if (n == 0) return;
            People[0].Tasks.Add(new CrewTask { Job = CrewTask.Kind.Fuel });
            // The car's own left or right is the garage side, by which side of the lane the garages are.
            bool leftNear = Local(spots.Stop(Garage, PitStopSpots.StopJob.FrontLeft, 4.6f, 1.9f).Position).y > 0f;
            PitStopSpots.StopJob nearFront = leftNear ? PitStopSpots.StopJob.FrontLeft : PitStopSpots.StopJob.FrontRight;
            PitStopSpots.StopJob nearRear = leftNear ? PitStopSpots.StopJob.RearLeft : PitStopSpots.StopJob.RearRight;
            PitStopSpots.StopJob farFront = leftNear ? PitStopSpots.StopJob.FrontRight : PitStopSpots.StopJob.FrontLeft;
            PitStopSpots.StopJob farRear = leftNear ? PitStopSpots.StopJob.RearRight : PitStopSpots.StopJob.RearLeft;
            int wheelers = Mathf.Min(n - 1, 4);
            PitStopSpots.StopJob[][] shares;
            switch (wheelers)
            {
                case 0: shares = new[] { new[] { nearRear, nearFront, farFront, farRear } }; break;
                case 1: shares = new[] { new[] { nearRear, nearFront, farFront, farRear } }; break;
                case 2: shares = new[] { new[] { farFront, farRear }, new[] { nearFront, nearRear } }; break;
                case 3: shares = new[] { new[] { farRear }, new[] { farFront }, new[] { nearFront, nearRear } }; break;
                default: shares = new[] { new[] { Wheels[0] }, new[] { Wheels[1] }, new[] { Wheels[2] }, new[] { Wheels[3] } }; break;
            }
            for (int m = 0; m < shares.Length; m++)
            {
                CrewMember who = People[wheelers == 0 ? 0 : 1 + m];
                for (int k = 0; k < shares[m].Length; k++)
                    who.Tasks.Add(new CrewTask { Job = CrewTask.Kind.Wheel, Spot = shares[m][k], Part = k, Parts = shares[m].Length });
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
            (Vector2 at, Vector2 face) p = Spot(t);
            return (p.at + stopped, p.face);
        }

        /// <summary>A task's place round a car standing on the box's mark.</summary>
        private (Vector2 at, Vector2 face) Spot(CrewTask t)
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
                // The door helper stands just ahead of the driver's door as it opens; the windscreen is cleaned from the far side.
                case CrewTask.Kind.Door: return DriverSwap.Helper(side, DoorSide);
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
                // The lollipop waits on the lane in front of the box, where the car stops short of it.
                case CrewTask.Kind.Lollipop: return Spot(t).at;
                case CrewTask.Kind.Screen: return new Vector2(l + 1.8f, 3.8f);
            }
            Vector2 at = Spot(t).at;
            if (at.y < 0f) return new Vector2(Mathf.Sign(at.x) * (l + (t.Second ? 1.9f : 1.1f)), 2.0f);
            return new Vector2(at.x, at.y + 1.3f);
        }

        /// <summary>
        /// Where a task stands once its work is done, until the car goes: on the garage side, those from the far side
        /// round the tail, out of the car's way out.
        /// </summary>
        private Vector2 After(CrewTask t)
        {
            float l = HalfLength;
            Vector2 at = Spot(t).at;
            switch (t.Job)
            {
                case CrewTask.Kind.Jack: return stopped + new Vector2(t.Spot == PitStopSpots.StopJob.FrontJack ? l + 1.0f : -l - 1.0f, 3.0f);
                case CrewTask.Kind.Lollipop: return stopped + new Vector2(l + 1.6f, width * 0.5f + 1.5f);
                case CrewTask.Kind.Screen: return stopped + new Vector2(-l - 1.8f, 3.8f);
            }
            if (at.y < 0f) return stopped + new Vector2(-l - (t.Second ? 1.9f : 1.1f), 2.0f);
            return stopped + new Vector2(at.x, at.y + 1.3f);
        }

        /// <summary>True when a task's place is in the car's way out: in front of the nose, or on the far side ahead of the tail.</summary>
        private bool InWayOut(CrewTask t)
        {
            Vector2 at = Spot(t).at;
            return at.x > HalfLength - 0.2f && Mathf.Abs(at.y) < width * 0.5f + 0.5f || at.y < 0f && at.x > 0.8f;
        }

        /// <summary>The time a task is worked in, s into the stop; empty when the stop has no such work.</summary>
        private (float from, float to) Window(CrewTask t, in StopClock c)
        {
            (float from, float to) w = Work(t, c);
            if (InWayOut(t)) w.to = Mathf.Min(w.to, c.Duration - (Crouches(t) ? ClearCrouched : ClearStanding));
            return w;
        }

        /// <summary>The time a task's work takes, s into the stop. A person with several wheels may work each any time in the tyre time, in turn.</summary>
        private static (float from, float to) Work(CrewTask t, in StopClock c)
        {
            switch (t.Job)
            {
                case CrewTask.Kind.Wheel:
                case CrewTask.Kind.Jack: return c.NewTyres ? (c.TyresFrom, c.TyresTo) : (0f, 0f);
                case CrewTask.Kind.Lollipop: return (0f, c.Duration);
                case CrewTask.Kind.Door: return c.NewDriver ? (0f, c.Duration) : (0f, 0f);
                // The windscreen is cleaned any time in the stop.
                case CrewTask.Kind.Screen: return (0f, c.Duration);
                default: return (c.FuelFrom, c.FuelTo);
            }
        }

        private static bool Crouches(CrewTask t) => t.Job == CrewTask.Kind.Wheel || t.Job == CrewTask.Kind.Jack;

        /// <summary>A person's task at this point of the stop, or null with none left: their place, its time and whether it is being worked now.</summary>
        public CrewTask? Current(CrewMember m) => m.Next < m.Tasks.Count && m.Stage == CrewStage.Stop ? m.Tasks[m.Next] : (CrewTask?)null;

        /// <summary>A task's place at this stop and the way it faces there, in the box's frame.</summary>
        public (Vector2 at, Vector2 face) PlaceOf(CrewTask t) => Place(t);

        /// <summary>The time a task is worked in at this stop, s into it.</summary>
        public (float from, float to) TimeOf(CrewTask t, in StopClock c) => Window(t, c);

        /// <summary>
        /// How much of a person's current task's work is done, 0 to 1: the share of its time gone, or for one of
        /// several wheels, the share of its own work given.
        /// </summary>
        public float WorkDone(CrewMember m, in StopClock c)
        {
            if (m.Next >= m.Tasks.Count) return 1f;
            CrewTask t = m.Tasks[m.Next];
            if (t.Parts > 1) return Mathf.Clamp01(m.Worked / WheelShare(m, c));
            (float from, float to) w = Window(t, c);
            return w.to <= w.from ? 0f : Mathf.Clamp01((c.Elapsed - w.from) / (w.to - w.from));
        }

        /// <summary>Is a person at their current task's place?</summary>
        public bool AtPlace(CrewMember m) => m.Next < m.Tasks.Count && (Place(m.Tasks[m.Next]).at - m.At).sqrMagnitude < 0.01f;

        /// <summary>
        /// Moves a person on to their next task once the current one is over: its time is up, or, for one of several
        /// wheels, it has had its share of work.
        /// </summary>
        private void Progress(CrewMember m, in StopClock c)
        {
            if (c.Stage != CrewStage.Stop)
            {
                m.Next = 0;
                m.Worked = 0f;
                m.Pace = 1f;
                return;
            }
            m.Pace = NeedPace(m, c);
            while (m.Next < m.Tasks.Count)
            {
                CrewTask t = m.Tasks[m.Next];
                (float from, float to) w = Window(t, c);
                // Over once its time is up, or there is not time left to get there and work it, or it has had its share.
                // At the place, the work goes on to the end of its time: the fuel nozzle stays on till the fuel is in.
                float eta = Eta(m, t, c);
                bool over = w.to <= w.from || c.Elapsed + eta > w.to - (eta > 0f ? MinWork : 0f) || t.Parts > 1 && m.Worked >= WheelShare(m, c);
                if (!over) return;
                m.Next++;
                m.Worked = 0f;
            }
        }

        /// <summary>
        /// The work a person with several wheels gives the one they are at: as much as leaves time to get to each wheel
        /// still to come and work it before its time is up, shared equally.
        /// </summary>
        private float WheelShare(CrewMember m, in StopClock c)
        {
            float now = Mathf.Max(c.Elapsed, c.TyresFrom) - m.Worked, moves = 0f, share = float.MaxValue;
            for (int k = m.Next; k < m.Tasks.Count; k++)
            {
                if (k > m.Next) moves += Hop(Place(m.Tasks[k - 1]).at, Place(m.Tasks[k]).at, m.Tasks[k], c) / m.Pace;
                share = Mathf.Min(share, (Window(m.Tasks[k], c).to - now - moves - MinWork) / (k - m.Next + 1));
            }
            return Mathf.Max(MinWheel, share);
        }

        /// <summary>The way between two points, round the car's nose or tail if it stands between them, m.</summary>
        private float Way(Vector2 from, Vector2 to, in StopClock c)
        {
            if (!near || Clear(from, to)) return (to - from).magnitude;
            Vector2 w = Waypoint(from, to, c);
            Vector2 other = new Vector2(w.x, 2f * car.y - w.y);
            return (w - from).magnitude + (Clear(w, to) ? (to - w).magnitude : (other - w).magnitude + (to - other).magnitude);
        }

        /// <summary>How long a move between two places takes: up, the walk or run there, and down again if the task there is done crouched.</summary>
        private float Hop(Vector2 from, Vector2 to, CrewTask t, in StopClock c) =>
            Way(from, to, c) / (WalkSpeed * (t.Parts > 1 ? RunRate : WalkRate)) + 0.3f + CrouchUp / QuickUpRate + (Crouches(t) ? CrouchDown / QuickDownRate : 0f);

        /// <summary>
        /// How much quicker than usual one with several wheels must move to get round them all in time, from 1 up to
        /// <see cref="MaxPace"/>: the moves still to make over the time they have, wheel by wheel.
        /// </summary>
        private float NeedPace(CrewMember m, in StopClock c)
        {
            if (c.Stage != CrewStage.Stop || m.Next >= m.Tasks.Count || m.Tasks[m.Next].Parts <= 1) return 1f;
            float now = Mathf.Max(c.Elapsed, c.TyresFrom), moves = 0f, pace = 1f;
            Vector2 at = m.At;
            bool there = (Place(m.Tasks[m.Next]).at - m.At).sqrMagnitude < 0.01f;
            for (int k = m.Next; k < m.Tasks.Count; k++)
            {
                Vector2 to = Place(m.Tasks[k]).at;
                if (k > m.Next || !there) moves += Hop(at, to, m.Tasks[k], c);
                at = to;
                float left = Window(m.Tasks[k], c).to - now - (k - m.Next + 1) * MinWheel - MinWork;
                pace = Mathf.Max(pace, moves / Mathf.Max(left, 0.1f));
            }
            return Mathf.Min(pace, MaxPace);
        }

        /// <summary>Roughly how long until a person can start a task: the walk there, round the car if it is in the way, and getting up and down.</summary>
        private float Eta(CrewMember m, CrewTask t, in StopClock c)
        {
            Vector2 at = Place(t).at;
            if ((at - m.At).sqrMagnitude < 0.01f) return 0f;
            float speed = WalkSpeed * (t.Parts > 1 ? RunRate : WalkRate);
            float eta = Way(m.At, at, c) / speed + 0.2f + (m.Crouched ? CrouchUp / QuickUpRate : 0f) + (Crouches(t) ? CrouchDown / QuickDownRate : 0f);
            return t.Parts > 1 ? eta / m.Pace : eta;
        }

        /// <summary>Where a person wants to be, which way to face, and whether crouched, at this point of the stop.</summary>
        private (Vector2 at, Vector2 face, bool low, bool fetch) Want(CrewMember m, in StopClock c)
        {
            var home = (m.Home, new Vector2(0f, -1f), false, false);
            if (m.Tasks.Count == 0) return home;
            // Kit fetched for the stop goes back where it came from on the way home.
            if (c.Stage == CrewStage.Home) m.FetchLetGo = false;
            if (m.FetchAt is Vector2 kit && (m.Carrying ? c.Stage == CrewStage.Home || c.Stage == CrewStage.Release : Fetching(m, kit, c)))
            {
                Vector2 via = Indoors(m.At, kit);
                return (via, Toward(m.At, via, m.Facing), false, via == kit);
            }
            if (c.Stage == CrewStage.Home || c.Stage == CrewStage.Release) return home;
            if (c.Stage == CrewStage.Ready)
            {
                Vector2 r = Ready(m.Tasks[0]);
                return (r, (-r).normalized, false, false);
            }
            if (m.Next < m.Tasks.Count)
            {
                CrewTask t = m.Tasks[m.Next];
                (Vector2 at, Vector2 face) p = Place(t);
                return (p.at, p.face, Crouches(t) && c.Elapsed >= Window(t, c).from, false);
            }
            Vector2 back = After(m.Tasks[m.Tasks.Count - 1]);
            return (back, (stopped - back).normalized, false, false);
        }

        /// <summary>
        /// Should a person go for their kit as the car comes in: only while the car is far enough off for them to get
        /// it and be back out at their spot beside the box before it stops, else they let it go for this stop.
        /// </summary>
        private bool Fetching(CrewMember m, Vector2 kit, in StopClock c)
        {
            if (c.Stage != CrewStage.Ready || m.FetchLetGo || m.FetchTyre && !c.NewTyres) return false;
            // The way there round the front of the garage, and from there out to the spot.
            float line = Door - 0.5f;
            Vector2 a = m.At, ready = Ready(m.Tasks[0]);
            float way = Mathf.Abs(kit.x - a.x) < 0.5f ? (kit - a).magnitude
                : Mathf.Abs(a.y - line) + Mathf.Abs(kit.x - a.x) + Mathf.Abs(kit.y - line);
            way += (ready - kit).magnitude;
            float trip = way / (WalkSpeed * RunRate) + FetchTime + FetchSpare;
            // The car's time to the box at its speed now: it is braking, so it will take longer still.
            float carIn = c.Car.x < 0f ? c.Car.magnitude / Mathf.Max(c.CarSpeed, 3f) : 0f;
            if (carIn > trip) return true;
            m.FetchLetGo = true;
            return false;
        }

        /// <summary>
        /// The next point on the way to kit inside the garage: across in front of its door, clear of
        /// the middle of the garage, where the waiting driver walks to the door and waits.
        /// </summary>
        private Vector2 Indoors(Vector2 from, Vector2 to)
        {
            float line = Door - 0.5f;
            if (Mathf.Abs(to.x - from.x) < 0.5f) return to;
            if (from.y > line + 0.1f) return new Vector2(from.x, line);
            return Mathf.Abs(from.y - line) < 0.05f || from.y < line ? new Vector2(to.x, line) : to;
        }

        /// <summary>The way from one point to another, or the given way when they are the same.</summary>
        private static Vector2 Toward(Vector2 from, Vector2 to, Vector2 otherwise) =>
            (to - from).sqrMagnitude > 1e-4f ? (to - from).normalized : otherwise;

        /// <summary>Is a point in the car's way, with a little room round it?</summary>
        private bool InCar(Vector2 p) => near && (Mathf.Abs(p.x - car.x) < HalfLength + 0.5f && Mathf.Abs(p.y - car.y) < width * 0.5f + 0.35f || InDoorway(p));

        /// <summary>
        /// Is a point where the drivers change, while they do: in front of the driver's door, from the car's side out to
        /// past the open door. The crew walk round it as they walk round the car.
        /// </summary>
        private bool InDoorway(Vector2 p)
        {
            if (!doorway) return false;
            float out_ = (p.y - car.y) * DoorSide - width * 0.5f;
            return p.x - car.x > DriverSwap.DoorwayBack && p.x - car.x < DriverSwap.DoorwayAhead && out_ > -0.1f && out_ < DriverSwap.DoorwayOut;
        }

        /// <summary>True when the straight walk between two points keeps out of the car's way.</summary>
        private bool Clear(Vector2 from, Vector2 to)
        {
            for (int k = 1; k <= 12; k++) if (InCar(Vector2.Lerp(from, to, k / 12f))) return false;
            return true;
        }

        /// <summary>
        /// The next point to walk to: straight there, or to the corner off the car's nose or tail that is clear to walk
        /// to and makes the shortest way on round the car. Near the end of the stop, and after it, no one walks round the nose, the
        /// car's way out, unless they are in front of it already and stepping out.
        /// </summary>
        private Vector2 Waypoint(Vector2 from, Vector2 to, in StopClock c)
        {
            if (!near || Clear(from, to)) return to;
            bool leaving = c.Stage == CrewStage.Release || c.Stage == CrewStage.Home || c.Stage == CrewStage.Stop && c.Elapsed > c.Duration - NoseShut;
            float end = HalfLength + 0.9f, across = width * 0.5f + 0.95f;
            bool ahead = from.x - car.x > HalfLength;
            Vector2 best = to;
            float shortest = float.MaxValue;
            for (int k = 0; k < (doorway ? 6 : 4); k++)
            {
                float x = k < 2 ? end : -end, y = k % 2 == 0 ? across : -across;
                if (k < 4 && x > 0f && leaving && !ahead) continue;
                // While the drivers change, the two outer corners of the doorway too.
                if (k >= 4)
                {
                    x = k == 4 ? DriverSwap.DoorwayAhead + 0.35f : DriverSwap.DoorwayBack - 0.35f;
                    y = DoorSide * (width * 0.5f + DriverSwap.DoorwayOut + 0.35f);
                }
                Vector2 corner = car + new Vector2(x, y);
                if ((corner - from).sqrMagnitude < 0.01f || !Clear(from, corner)) continue;
                // On from the corner: straight there, or across the car's end (or the doorway) to the other corner first.
                Vector2 other = k >= 4 ? car + new Vector2(k == 4 ? DriverSwap.DoorwayBack - 0.35f : DriverSwap.DoorwayAhead + 0.35f, y) : car + new Vector2(x, -y);
                float way = (corner - from).magnitude + (Clear(corner, to) ? (to - corner).magnitude : (other - corner).magnitude + (to - other).magnitude);
                if (way < shortest)
                {
                    shortest = way;
                    best = corner;
                }
            }
            return best;
        }

        /// <summary>Moves the crew on by dt of race time, following the stop's clock.</summary>
        public void Step(in StopClock clock, float dt)
        {
            if (AtRest && (clock.Stage == CrewStage.Home || People.Length == 0)) return;
            if (clock.CarLength > 0f) Measure(clock.CarLength, clock.CarWidth);
            else if (length < 0f) Measure(4.6f, 1.9f);
            near = clock.CarNear;
            doorway = clock.Stage == CrewStage.Stop && clock.NewDriver && clock.Elapsed < DriverSwap.End(clock);
            car = clock.Car;
            bool standing = near && clock.CarSpeed < 1f && Mathf.Abs(car.x) < 3f && Mathf.Abs(car.y) < 1.5f;
            if (clock.Stage == CrewStage.Stop && standing) stopped = car;
            else if (clock.Stage == CrewStage.Home || clock.Stage == CrewStage.Ready) stopped = Vector2.zero;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep));
            float h = dt / steps;
            bool rest = true;
            foreach (CrewMember m in People)
            {
                m.Stage = clock.Stage;
                for (int s = 0; s < steps; s++) Move(m, clock, h);
                rest &= clock.Stage == CrewStage.Home && m.At == m.Home && m.Clip == "Idle" && !m.Crouched && m.Busy <= 0f;
            }
            AtRest = rest;
        }

        private void Move(CrewMember m, in StopClock c, float dt)
        {
            m.Advance(dt);
            Progress(m, c);
            (Vector2 at, Vector2 face, bool low, bool fetch) want = Want(m, c);
            Vector2 to = want.at - m.At;
            bool far = to.sqrMagnitude > 0.06f * 0.06f;
            m.Working = false;
            bool several = m.Next < m.Tasks.Count && m.Tasks[m.Next].Parts > 1;
            if (m.Busy > 0f)
            {
                m.Busy -= dt;
                return;
            }
            if (m.Crouched && (far || !want.low))
            {
                // Up quicker as the car is released, out of its way, and for one with several wheels, to fit them all in the tyre time.
                bool hurry = c.Stage != CrewStage.Stop || c.Elapsed > c.Duration - ClearCrouched || several || m.Next > 0 && m.Next <= m.Tasks.Count && m.Tasks[m.Next - 1].Parts > 1;
                float up = hurry ? QuickUpRate * m.Pace : UpRate;
                m.Play("CrouchUpA", 0f, up, false);
                m.Crouched = false;
                m.Busy = CrouchUp / up - 0.3f * UpRate / up;
                return;
            }
            if (far)
            {
                Vector2 wp = Waypoint(m.At, want.at, c) - m.At;
                float dist = wp.magnitude;
                Vector2 dir = wp / Mathf.Max(dist, 1e-4f);
                m.Facing = Turn(m.Facing, dir, dt * 9f);
                // One with several wheels runs between them; one fetching kit as the car comes in runs for it.
                bool fetching = c.Stage == CrewStage.Ready && m.FetchAt != null && !m.Carrying && !m.FetchLetGo;
                float rate = several && c.Stage == CrewStage.Stop ? RunRate * m.Pace : fetching ? RunRate : WalkRate;
                float go = Mathf.Min(dist, WalkSpeed * rate * dt * Mathf.Max(0f, Vector2.Dot(m.Facing, dir)));
                m.At += dir * go;
                if (m.Clip == "Walk") m.Rate = rate;
                m.Play("Walk", 0f, rate, true);
                return;
            }
            m.At = want.at;
            m.Facing = Turn(m.Facing, want.face, dt * 7f);
            if (want.fetch)
            {
                // A moment to pick the kit up or put it down.
                m.Carrying = !m.Carrying;
                m.Play("Idle", m.IdleFrom, 1f, true);
                m.Busy = FetchTime;
                return;
            }
            if (want.low && !m.Crouched)
            {
                float down = several ? QuickDownRate * m.Pace : DownRate;
                m.Play("CrouchDown", 0f, down, false);
                m.Crouched = true;
                m.Busy = CrouchDown / down - 0.1f;
                return;
            }
            if (m.Crouched) m.Play("Crouch", 0f, 1f, true);
            else m.Play("Idle", m.IdleFrom, 1f, true);
            // At the place, down if the job is done crouched, inside its time: the work is going on.
            if (c.Stage == CrewStage.Stop && m.Next < m.Tasks.Count)
            {
                (float from, float to) w = Window(m.Tasks[m.Next], c);
                if (c.Elapsed >= w.from && c.Elapsed < w.to)
                {
                    m.Working = true;
                    m.Worked += dt;
                }
            }
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
