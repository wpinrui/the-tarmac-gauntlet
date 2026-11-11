using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>One of a box's two race drivers: where they stand and the clip they play, and whether they are in the car.</summary>
    public sealed class DriverFigure
    {
        /// <summary>Where they stand in the box's frame, which way they face and the clip they play, as a crew member does.</summary>
        public readonly CrewMember Body;
        /// <summary>How far they have sat down into the car: 0 standing, 1 with the hips on the seat.</summary>
        public float Lift;
        /// <summary>True while they ride in the car, on its seat: drawn in the car's frame, not the box's.</summary>
        public bool InCar;
        /// <summary>False while they are in the car away from the box: nobody is drawn then.</summary>
        public bool Shown;

        public DriverFigure(Vector2 home, float idleFrom) => Body = new CrewMember(home, idleFrom);
    }

    /// <summary>
    /// The driver change at one box, with no Unity objects so it can be checked outside Unity (tools/pit-crew). A box
    /// whose car has more than one driver has two driver figures: the one driving, in the car, and the next, waiting
    /// at the back of the garage. As the car comes in for a stop that changes driver, the next walks to the garage
    /// door; once it stops they walk out beside the driver's door. The door opens, the driver in the car gets out
    /// and walks into the garage, the next gets in and the door shuts, all in step with the stop's clock: the stop
    /// never waits for them. A short stop plays the whole change quicker. The one who got out waits for the next stop.
    /// </summary>
    public sealed class DriverSwap
    {
        /// <summary>The change at full pace, s into the stop: the door opening, the driver out, the next walking to the door and sitting in, the door shut.</summary>
        public const float OpenAt = 0.3f, Swing = 0.7f, OutAt = 0.8f, Up = 1.7f, InWalk = 2.75f, SitAt = 3.7f, Down = 1.7f, ShutAt = 5.5f, Turn = 0.35f;
        /// <summary>The change's length at full pace, and the time it leaves before the release, s.</summary>
        public const float Natural = ShutAt + Swing, Margin = 0.8f;
        /// <summary>The quickest the change is played: this share of its full length.</summary>
        public const float Quickest = 0.35f;
        /// <summary>Length of the sitting clip, s, as packed: played forwards to get in, backwards to get out.</summary>
        public const float SitClip = 53f / 24f;
        /// <summary>The driver's seat: back from the car's middle and out from its centre line to the door's side, m.</summary>
        public const float SeatBack = 0.2f, SeatOut = 0.38f;

        /// <summary>
        /// The doorway the drivers change in, which the crew walk round while they do: from this far back of the car's
        /// middle to this far ahead, and out from its side, m.
        /// </summary>
        public const float DoorwayBack = -0.75f, DoorwayAhead = 0.8f, DoorwayOut = 0.9f;

        /// <summary>The change's pace for a stop: 1 at full pace, less to fit a short stop.</summary>
        public static float PaceFor(in StopClock c) => Mathf.Clamp((c.Duration - Margin) / Natural, Quickest, 1f);

        /// <summary>When the drivers are done in the doorway, s into the stop: the new driver sat in.</summary>
        public static float End(in StopClock c) => (SitAt + Down) * PaceFor(c);

        /// <summary>The box's frame: its middle, the way the car faces and the way to the garage (a crew with no one in it).</summary>
        public readonly GarageCrew Frame;
        public readonly DriverFigure[] Figures = new DriverFigure[2];
        /// <summary>+1 when the driver's door is on the garage side of the car, -1 when on the far side.</summary>
        public readonly int DoorSide;
        /// <summary>Where the waiting driver stands in the garage, and where they wait at its door for the car, in the box's frame.</summary>
        public readonly Vector2 Rest, AtDoor;

        /// <summary>How far the driver's door is open: 0 shut, 1 fully open. The game swings the car's door to it.</summary>
        public float Door { get; private set; }
        /// <summary>The figure in the car.</summary>
        public int Driving { get; private set; }
        /// <summary>True once nothing moves until the next stop.</summary>
        public bool AtRest { get; private set; }
        /// <summary>The change's pace this stop: 1 at full pace, less to fit a short stop.</summary>
        public float Pace { get; private set; } = 1f;

        private float halfWidth = 0.95f, halfLength = 2.3f;
        private Vector2 car, stopped;
        private bool near, standing;
        /// <summary>This stop's change: under way, who gets out and in, and where the one getting in walked from.</summary>
        private bool changing, seenStop;
        private int outgoing = -1, incoming = -1;
        private Vector2 inFrom;
        private bool inFromSet;

        /// <param name="doorOnLeft">True when the driver's door is on the car's left.</param>
        public DriverSwap(PitComplex complex, PitStopSpots spots, int garage, bool doorOnLeft, int seed)
        {
            Frame = new GarageCrew(complex, spots, garage, 0, seed);
            var left = new Vector3(-Frame.Forward.z, 0f, Frame.Forward.x);
            DoorSide = (Vector3.Dot(left, Frame.ToGarage) > 0f) == doorOnLeft ? 1 : -1;
            GarageFrame f = complex.Frame(garage);
            Rest = Frame.Local(f.At(0f, 0f, complex.Depth * 0.5f - 6f));
            AtDoor = new Vector2(0.45f, Frame.Door + 0.4f);
            var rng = new System.Random(seed);
            for (int i = 0; i < 2; i++) Figures[i] = new DriverFigure(Rest, (float)rng.NextDouble() * 2.5f);
            Driving = 0;
            Figures[0].InCar = true;
            Figures[0].Lift = 1f;
            Seated(Figures[0].Body);
            AtRest = false;
        }

        // ---------------------------------------------------------------- places, round the standing car, in the box's frame

        public Vector2 Seat => stopped + new Vector2(-SeatBack, DoorSide * SeatOut);
        public Vector2 Entry => stopped + new Vector2(-0.05f, DoorSide * (halfWidth + 0.42f));
        public Vector2 Queue => stopped + new Vector2(0.45f, DoorSide * (halfWidth + 2.0f));
        private static readonly Vector2 Ahead = new Vector2(1f, 0f);
        /// <summary>Facing out of the door, the back to the car.</summary>
        private Vector2 Outward => new Vector2(0f, DoorSide);

        /// <summary>Where the door helper of the crew stands: just ahead of the open door, facing the cockpit.</summary>
        public static (Vector2 at, Vector2 face) Helper(float halfWidth, int doorSide) =>
            (new Vector2(0.6f, doorSide * (halfWidth + 1.25f)), new Vector2(-0.45f, -0.9f * doorSide).normalized);

        // ---------------------------------------------------------------- each step

        /// <summary>Moves the drivers on by dt of race time, following the stop's clock.</summary>
        public void Step(in StopClock c, float dt)
        {
            if (c.CarWidth > 0f)
            {
                halfWidth = c.CarWidth * 0.5f;
                halfLength = c.CarLength * 0.5f;
            }
            near = c.CarNear;
            car = c.Car;
            standing = near && c.CarSpeed < 1f && Mathf.Abs(car.x) < 3f && Mathf.Abs(car.y) < 1.5f;
            bool stop = c.Stage == CrewStage.Stop;
            if (stop && standing) stopped = car;
            else if (c.Stage == CrewStage.Home || c.Stage == CrewStage.Ready) stopped = Vector2.zero;

            if (stop && !seenStop)
            {
                seenStop = true;
                if (c.NewDriver) Begin(c);
            }
            if (!stop)
            {
                seenStop = false;
                if (changing) Finish();
            }
            if (AtRest && c.Stage == CrewStage.Home) return;

            float e = stop ? c.Elapsed / Pace : 0f;
            Door = changing ? (e < ShutAt ? Smooth((e - OpenAt) / Swing) : 1f - Smooth((e - ShutAt) / Swing)) : 0f;
            bool rest = c.Stage == CrewStage.Home;
            for (int i = 0; i < 2; i++)
            {
                DriverFigure d = Figures[i];
                d.Body.Advance(dt);
                if (changing && i == outgoing) GetOut(d, e, dt);
                else if (changing && i == incoming) GetIn(d, e, dt);
                else if (d.InCar) Seated(d.Body);
                else
                {
                    // The waiting driver: at the garage door while a car that will change driver comes in, else at the back.
                    bool toDoor = c.Stage == CrewStage.Ready && c.NewDriver;
                    Walk(d, toDoor ? AtDoor : Rest, new Vector2(0f, -1f), 1f, dt);
                }
                // In the car the driver shows only while it stands in the box.
                d.Shown = !d.InCar || standing && (stop || c.Stage == CrewStage.Release);
                if (!d.InCar) rest &= d.Body.At == Rest && d.Body.Clip == "Idle";
            }
            AtRest = rest;
        }

        /// <summary>The stop changes driver: the one in the car gets out, the waiting one gets in, at a pace that fits the stop.</summary>
        private void Begin(in StopClock c)
        {
            changing = true;
            outgoing = Driving;
            incoming = 1 - Driving;
            Driving = incoming;
            inFromSet = false;
            Pace = PaceFor(c);
            AtRest = false;
        }

        /// <summary>
        /// The stop is over: the change is made, the new driver in the car and the door shut. Should the stop have
        /// ended early, the other is put out beside the door; either way they walk back into the garage from there.
        /// </summary>
        private void Finish()
        {
            changing = false;
            Door = 0f;
            DriverFigure o = Figures[outgoing], n = Figures[incoming];
            if (o.InCar || o.Lift > 0f)
            {
                o.InCar = false;
                o.Lift = 0f;
                o.Body.At = Entry;
                o.Body.Play("Idle", 0f, 1f, true);
            }
            n.InCar = true;
            n.Lift = 1f;
            Seated(n.Body);
        }

        /// <summary>The driver getting out: in the seat, up and out beside the door, then into the garage.</summary>
        private void GetOut(DriverFigure d, float e, float dt)
        {
            if (e < OutAt)
            {
                d.InCar = true;
                d.Lift = 1f;
                Seated(d.Body);
                return;
            }
            if (e < OutAt + Up)
            {
                float u = (e - OutAt) / Up;
                d.InCar = false;
                Hold(d.Body, "Sit", SitClip * (1f - u));
                d.Body.At = Vector2.Lerp(Seat, Entry, Smooth(u));
                d.Lift = 1f - Smooth(u * 1.2f);
                d.Body.Facing = Between(Ahead, Outward, Smooth(u * 1.6f));
                return;
            }
            d.InCar = false;
            d.Lift = 0f;
            Walk(d, Rest, new Vector2(0f, -1f), 1f, dt);
        }

        /// <summary>The driver getting in: out to beside the door, to it once the other is out, then down into the seat.</summary>
        private void GetIn(DriverFigure d, float e, float dt)
        {
            CrewMember b = d.Body;
            if (e < InWalk)
            {
                d.InCar = false;
                d.Lift = 0f;
                Walk(d, Queue, (Entry - Queue).normalized, 1f / Pace, dt);
                return;
            }
            if (e < SitAt)
            {
                if (!inFromSet)
                {
                    inFrom = b.At;
                    inFromSet = true;
                }
                float u = Smooth((e - InWalk) / (SitAt - InWalk - Turn));
                Vector2 was = b.At;
                b.At = Vector2.Lerp(inFrom, Entry, u);
                if (u < 1f)
                {
                    Vector2 dir = b.At - was;
                    if (dir.sqrMagnitude > 1e-8f) b.Facing = TurnTo(b.Facing, dir.normalized, dt * 9f);
                    float rate = GarageCrew.WalkRate / Pace;
                    if (b.Clip == "Walk") b.Rate = rate;
                    b.Play("Walk", 0f, rate, true);
                }
                else
                {
                    b.Play("Idle", 0.5f, 1f, true);
                    b.Facing = TurnTo(b.Facing, Outward, dt * 10f / Pace);
                }
                return;
            }
            if (e < SitAt + Down)
            {
                float u = (e - SitAt) / Down;
                Hold(b, "Sit", SitClip * u);
                b.At = Vector2.Lerp(Entry, Seat, Smooth(u));
                d.Lift = Smooth((u - 0.2f) / 0.8f);
                b.Facing = Between(Outward, Ahead, Smooth((u - 0.4f) / 0.6f));
                return;
            }
            d.InCar = true;
            d.Lift = 1f;
            Seated(b);
        }

        /// <summary>Walks a figure to a point, round the car's tail if it stands in the way, then stands facing a way.</summary>
        private void Walk(DriverFigure d, Vector2 to, Vector2 face, float pace, float dt)
        {
            CrewMember b = d.Body;
            Vector2 next = Waypoint(b.At, to);
            Vector2 off = next - b.At;
            float dist = off.magnitude;
            if (dist < 0.03f && next == to)
            {
                b.At = to;
                b.Facing = TurnTo(b.Facing, face, dt * 7f);
                b.Play("Idle", b.IdleFrom, 1f, true);
                return;
            }
            Vector2 dir = off / Mathf.Max(dist, 1e-4f);
            b.Facing = TurnTo(b.Facing, dir, dt * 9f);
            float rate = GarageCrew.WalkRate * pace;
            b.At += dir * Mathf.Min(dist, GarageCrew.WalkSpeed * rate * dt * Mathf.Max(0f, Vector2.Dot(b.Facing, dir)));
            if (b.Clip == "Walk") b.Rate = rate;
            b.Play("Walk", 0f, rate, true);
        }

        /// <summary>The next point on the way: straight there, or round the standing car's tail when it is in the way.</summary>
        private Vector2 Waypoint(Vector2 from, Vector2 to)
        {
            if (!near || Clear(from, to)) return to;
            float end = -halfLength - 0.9f, across = halfWidth + 0.95f;
            Vector2 a = car + new Vector2(end, Mathf.Sign(from.y - car.y) * across), b = car + new Vector2(end, Mathf.Sign(to.y - car.y) * across);
            return (a - from).sqrMagnitude > 0.01f && Clear(from, a) ? a : b;
        }

        private bool Clear(Vector2 from, Vector2 to)
        {
            for (int k = 1; k <= 12; k++)
            {
                Vector2 p = Vector2.Lerp(from, to, k / 12f);
                if (Mathf.Abs(p.x - car.x) < halfLength + 0.5f && Mathf.Abs(p.y - car.y) < halfWidth + 0.3f) return false;
            }
            return true;
        }

        /// <summary>Sitting in the car: the sitting clip held at its end, facing forwards.</summary>
        private void Seated(CrewMember b)
        {
            Hold(b, "Sit", SitClip);
            b.At = Seat;
            b.Facing = Ahead;
        }

        private static void Hold(CrewMember b, string clip, float time)
        {
            b.Play(clip, time, 0f, false);
            b.ClipTime = Mathf.Clamp(time, 0f, SitClip);
            b.Rate = 0f;
        }

        private static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>A facing a share of the way from one to another, turning the short way round.</summary>
        private static Vector2 Between(Vector2 from, Vector2 to, float share)
        {
            float a = Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg, b = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
            float r = (a + Mathf.DeltaAngle(a, b) * Mathf.Clamp01(share)) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static Vector2 TurnTo(Vector2 from, Vector2 to, float share)
        {
            if (to.sqrMagnitude < 1e-6f) return from;
            float a = Mathf.Atan2(from.y, from.x), b = Mathf.Atan2(to.y, to.x);
            float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float r = a + d * Mathf.Min(1f, share);
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }
    }
}
