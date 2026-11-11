using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>A piece of kit's place in the world: its origin, and the way its own z and y point.</summary>
    public struct KitPlace
    {
        public Vector3 Position, Forward, Up;

        public KitPlace(Vector3 position, Vector3 forward, Vector3 up)
        {
            Position = position;
            Forward = forward;
            Up = up;
        }
    }

    /// <summary>The car standing in its box, as the kit works on it, in the world.</summary>
    public struct KitCar
    {
        /// <summary>The car's middle on the ground, the way it faces and its up.</summary>
        public Vector3 Middle, Forward, Up;
        /// <summary>Wheel middles: 0 front left, 1 front right, 2 rear left, 3 rear right.</summary>
        public Vector3 Hub0, Hub1, Hub2, Hub3;
        /// <summary>The fuel filler on the car's flank, and the way out of the car there.</summary>
        public Vector3 Filler, FillerOut;
        /// <summary>The air jack's socket at the back of the car.</summary>
        public Vector3 Socket;

        public Vector3 Hub(int w) => w == 0 ? Hub0 : w == 1 ? Hub1 : w == 2 ? Hub2 : Hub3;

        /// <summary>
        /// A car standing at a point in its box, from its size: the hubs, the filler on the garage side by the rear
        /// wheel, where <see cref="PitStopSpots"/> puts the refueller, and the socket at its tail.
        /// </summary>
        public static KitCar Standing(Vector3 middle, Vector3 forward, Vector3 toGarage, float length, float width)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float axle = length * 0.32f, track = width * 0.5f - 0.12f;
            var car = new KitCar { Middle = middle, Forward = forward, Up = Vector3.up };
            Vector3 H(float ahead, float toRight) => middle + forward * ahead + right * toRight + Vector3.up * 0.33f;
            car.Hub0 = H(axle, -track);
            car.Hub1 = H(axle, track);
            car.Hub2 = H(-axle, -track);
            car.Hub3 = H(-axle, track);
            car.FillerOut = toGarage;
            car.Filler = middle - forward * (axle * 0.6f) + toGarage * (width * 0.5f) + Vector3.up * 0.75f;
            car.Socket = middle - forward * (length * 0.5f) + Vector3.up * 0.45f;
            return car;
        }
    }

    /// <summary>
    /// The kit a box's crew handle at a stop (<see cref="PitBoxKit"/>), following the crew (<see cref="GarageCrew"/>)
    /// and the stop's clock, with no Unity objects so it can be checked outside Unity (tools/pit-crew). It only says
    /// where each piece is; it never sets the clock or moves the car.
    ///
    /// The refueller takes the nozzle down off its line as the car comes in, holds it while the car stops, puts it
    /// on the filler as the fuel starts and takes it off as the fuel ends; the nozzle swings back up its line. Each
    /// wheel's gunner pulls their gun down as they reach the wheel, has it on the hub to undo the wheel and again
    /// to do it up, and lets it go when the wheel is done. Each wheel's tyre hand (the second on the wheel, or the
    /// one on it alone) fetches the fresh tyre off the trolley as the car comes in, lays it down by the wheel, pulls
    /// the old wheel off, puts the fresh one on and carries the old one back to the trolley. One with several wheels
    /// only uses the gun. The rear jack plugs the lance in for the tyre time, and the car rises on its jacks. The
    /// lollipop and the fire extinguisher are fetched from inside the door and taken back; the lollipop is lifted
    /// as the car is released.
    /// </summary>
    public sealed class KitWork
    {
        /// <summary>Where a person of the crew has a hand, in the world.</summary>
        public delegate Vector3 HandOf(int person, bool right);

        /// <summary>A hanging piece can be pulled down by one standing this close under its anchor, across, m.</summary>
        public const float Reach = 1.5f;
        /// <summary>How high the car rises on its air jacks, m.</summary>
        public const float JackLift = 0.05f;
        /// <summary>The wheel's work, as shares of its time: gun on, old wheel off, fresh one on, gun on again, done.</summary>
        public const float Undone = 0.22f, Off = 0.45f, On = 0.7f, Tight = 0.92f;
        /// <summary>Least fuel time worth putting the nozzle on for, s.</summary>
        public const float MinFuel = 0.3f;
        /// <summary>A piece is there once this close, m.</summary>
        private const float There = 0.02f;

        private enum Fresh { Rack, Hands, Ground, Lifting, OnCar }
        private enum Old { None, Off, Ground, Hands }

        public readonly GarageCrew Crew;
        private readonly PitBoxKit kit;
        private readonly int garage;
        private readonly KitPose[] rests = new KitPose[PitBoxKit.ItemCount];
        private readonly Vector3[] anchors = new Vector3[PitBoxKit.ItemCount];
        private readonly int refueller = -1, marshal = -1, lollipop = -1, jack = -1;
        private readonly int[] gunner = { -1, -1, -1, -1 }, tyreHand = { -1, -1, -1, -1 };
        private readonly KitItem[] guns = new KitItem[4];
        private bool gunsMatched;
        private readonly Fresh[] fresh = new Fresh[4];
        private readonly Old[] old = new Old[4];
        private float released, raise;

        /// <summary>Where each piece is now; a piece not <see cref="Out"/> is on its rest.</summary>
        public readonly KitPlace[] Pieces = new KitPlace[PitBoxKit.ItemCount];
        /// <summary>True while a piece is off its rest, and while a fresh tyre is on the car, hidden.</summary>
        public readonly bool[] Out = new bool[PitBoxKit.ItemCount];
        public readonly bool[] Hidden = new bool[PitBoxKit.ItemCount];
        /// <summary>By wheel: the gun on the hub, the car's own wheel off, and the old wheel off the car, where it is (its z along the axle).</summary>
        public readonly bool[] OnHub = new bool[4], WheelOff = new bool[4], OldOut = new bool[4];
        public readonly KitPlace[] OldWheel = new KitPlace[4];
        /// <summary>True while the nozzle is on the filler, and the lance in its socket.</summary>
        public bool NozzleOn { get; private set; }
        public bool LanceIn { get; private set; }
        /// <summary>How far up on its jacks the car is, 0 to 1 of <see cref="JackLift"/>.</summary>
        public float Lift { get; private set; }
        /// <summary>True while anything is off its rest or the car is up.</summary>
        public bool Busy { get; private set; }

        public KitWork(PitComplex complex, PitBoxKit boxKit, GarageCrew crew)
        {
            Crew = crew;
            kit = boxKit;
            garage = crew.Garage;
            for (int k = 0; k < PitBoxKit.ItemCount; k++)
            {
                rests[k] = kit.Rest(garage, (KitItem)k);
                if (PitBoxKit.Hangs((KitItem)k)) anchors[k] = kit.Anchor(garage, (KitItem)k);
                Pieces[k] = AtRest(k);
            }
            for (int p = 0; p < crew.People.Length; p++)
                foreach (CrewTask t in crew.People[p].Tasks)
                    switch (t.Job)
                    {
                        case CrewTask.Kind.Fuel: if (refueller < 0) refueller = p; break;
                        case CrewTask.Kind.Marshal: marshal = p; break;
                        case CrewTask.Kind.Lollipop: lollipop = p; break;
                        case CrewTask.Kind.Jack: if (t.Spot == PitStopSpots.StopJob.RearJack) jack = p; break;
                        case CrewTask.Kind.Wheel:
                            int w = Wheel(t.Spot);
                            if (t.Second) tyreHand[w] = p;
                            else
                            {
                                gunner[w] = p;
                                if (t.Parts == 1 && tyreHand[w] < 0) tyreHand[w] = p;
                            }
                            break;
                    }
            // A second on the wheel handles the tyres, the gunner the gun.
            for (int p = 0; p < crew.People.Length; p++)
                foreach (CrewTask t in crew.People[p].Tasks)
                    if (t.Job == CrewTask.Kind.Wheel && t.Second) tyreHand[Wheel(t.Spot)] = p;

            // What each fetches from inside the door, and where they stand to pick it up.
            GarageFrame f = complex.Frame(garage);
            for (int w = 0; w < 4; w++)
                if (tyreHand[w] >= 0) Fetch(tyreHand[w], rests[(int)KitItem.TyreFrontLeft + w].Position, f.Side * 0.65f, true);
            if (lollipop >= 0) Fetch(lollipop, rests[(int)KitItem.Lollipop].Position, f.Side * 0.45f, false);
            if (marshal >= 0) Fetch(marshal, rests[(int)KitItem.Extinguisher].Position, -f.Side * 0.5f, false);
            for (int w = 0; w < 4; w++) guns[w] = KitItem.GunFrontLeft + w;
        }

        private void Fetch(int person, Vector3 rest, Vector3 beside, bool tyre)
        {
            Vector3 at = rest + beside;
            Crew.People[person].FetchAt = Crew.Local(at);
            Crew.People[person].FetchTyre = tyre;
        }

        /// <summary>The wheel a stop job is at: 0 front left, 1 front right, 2 rear left, 3 rear right.</summary>
        public static int Wheel(PitStopSpots.StopJob job) =>
            job == PitStopSpots.StopJob.FrontLeft ? 0 : job == PitStopSpots.StopJob.FrontRight ? 1 : job == PitStopSpots.StopJob.RearLeft ? 2 : 3;

        /// <summary>The gun used on wheel w.</summary>
        public KitItem GunOf(int w) => guns[w];

        /// <summary>The person who handles wheel w's tyres, or -1 with none.</summary>
        public int TyreHand(int w) => tyreHand[w];

        /// <summary>Where a piece rests.</summary>
        public KitPlace AtRest(int k) => new KitPlace(rests[k].Position, rests[k].Forward, Vector3.up);

        public Vector3 Anchor(KitItem item) => anchors[(int)item];

        // ---------------------------------------------------------------- each frame

        /// <summary>Moves the kit on by dt, after the crew have moved, following the stop's clock.</summary>
        public void Step(in StopClock c, in KitCar car, HandOf hand, float dt)
        {
            if (!Busy && Crew.AtRest && c.Stage == CrewStage.Home) return;
            if (c.Stage == CrewStage.Stop && !gunsMatched) MatchGuns(car);
            if (c.Stage == CrewStage.Home) gunsMatched = false;
            released = c.Stage == CrewStage.Release ? released + dt : 0f;
            Nozzle(c, car, hand, dt);
            for (int w = 0; w < 4; w++) Gun(w, c, car, hand, dt);
            for (int w = 0; w < 4; w++) Tyres(w, c, car, hand, dt);
            Lance(c, car, hand, dt);
            Lollipop(c, hand, dt);
            Extinguisher(hand, dt);
            Lift = Mathf.MoveTowards(Lift, LanceIn ? 1f : 0f, dt / 0.35f);
            bool busy = Lift > 0f;
            for (int k = 0; k < PitBoxKit.ItemCount; k++) busy |= Out[k];
            for (int w = 0; w < 4; w++) busy |= OldOut[w] || WheelOff[w];
            Busy = busy;
        }

        /// <summary>Each wheel's gun is the one hanging nearest over it.</summary>
        private void MatchGuns(in KitCar car)
        {
            gunsMatched = true;
            var used = new bool[4];
            for (int w = 0; w < 4; w++)
            {
                float best = float.MaxValue;
                for (int g = 0; g < 4; g++)
                {
                    if (used[g]) continue;
                    Vector3 d = anchors[(int)KitItem.GunFrontLeft + g] - car.Hub(w);
                    d.y = 0f;
                    if (d.sqrMagnitude < best)
                    {
                        best = d.sqrMagnitude;
                        guns[w] = KitItem.GunFrontLeft + g;
                    }
                }
                used[guns[w] - KitItem.GunFrontLeft] = true;
            }
        }

        // ---------------------------------------------------------------- the pieces

        private void Nozzle(in StopClock c, in KitCar car, HandOf hand, float dt)
        {
            const int k = (int)KitItem.Nozzle;
            NozzleOn = false;
            if (refueller < 0) { Home(k, 2.2f, dt); return; }
            CrewMember m = Crew.People[refueller];
            bool fuel = c.FuelTo - c.FuelFrom >= MinFuel;
            bool reach = Under(m, KitItem.Nozzle);
            CrewTask? cur = Crew.Current(m);
            bool fuelling = cur != null && cur.Value.Job == CrewTask.Kind.Fuel;
            bool hold = c.Stage == CrewStage.Ready && (Out[k] || reach && m.Clip != "Walk")
                        || c.Stage == CrewStage.Stop && fuel && c.Elapsed < c.FuelTo + 0.5f && (Out[k] || reach) && (fuelling || c.Elapsed >= c.FuelTo);
            if (!hold || Far(m, KitItem.Nozzle, hand)) { Home(k, 2.2f, dt); return; }
            if (c.Stage == CrewStage.Stop && fuelling && Crew.AtPlace(m) && c.Elapsed < c.FuelTo)
            {
                // On to the filler, there as the fuel starts.
                Vector3 d = (-car.FillerOut * 0.95f + Vector3.down * 0.3f).normalized;
                var on = new KitPlace(car.Filler - d * 0.55f, Square(car.Up, d), -d);
                float left = c.FuelFrom - c.Elapsed;
                float speed = left > dt ? Mathf.Max((on.Position - Pieces[k].Position).magnitude / left, 0.3f) : 2.5f;
                if (Toward(k, on, speed, dt) && c.Elapsed >= c.FuelFrom - dt) NozzleOn = true;
                return;
            }
            Vector3 mid = Mid(hand, refueller);
            Vector3 aim = (Face(m) * 0.8f + Vector3.down * 0.6f).normalized;
            Toward(k, new KitPlace(mid - aim * 0.26f, Square(Vector3.up, aim), -aim), Out[k] ? 1.6f : 2.5f, dt);
        }

        private void Gun(int w, in StopClock c, in KitCar car, HandOf hand, float dt)
        {
            int k = (int)guns[w];
            OnHub[w] = false;
            int g = gunner[w];
            if (g < 0 || c.Stage != CrewStage.Stop || !c.NewTyres) { Home(k, 2.5f, dt); return; }
            CrewMember m = Crew.People[g];
            CrewTask? cur = Crew.Current(m);
            bool mine = cur != null && cur.Value.Job == CrewTask.Kind.Wheel && !cur.Value.Second && Wheel(cur.Value.Spot) == w;
            if (!mine || !Out[k] && !Under(m, guns[w]) || Far(m, guns[w], hand)) { Home(k, 2.5f, dt); return; }
            if (Crew.AtPlace(m) && m.Working)
            {
                float p = Crew.WorkDone(m, c);
                bool swap = fresh[w] != Fresh.Rack && fresh[w] != Fresh.Hands || old[w] != Old.None;
                bool on = swap ? p < Undone || p >= On && p < Tight : p < 0.98f;
                if (on)
                {
                    Vector3 outward = Outward(car, car.Hub(w));
                    var place = new KitPlace(car.Hub(w) + outward * 0.36f + car.Up * 0.17f, -outward, car.Up);
                    OnHub[w] = Toward(k, place, 6f, dt);
                    return;
                }
            }
            Toward(k, new KitPlace(hand(g, true) + Vector3.up * 0.12f, Face(m), Vector3.up), 5f, dt);
        }

        private void Tyres(int w, in StopClock c, in KitCar car, HandOf hand, float dt)
        {
            int k = (int)KitItem.TyreFrontLeft + w;
            int t = tyreHand[w];
            if (t < 0) { Home(k, 2.5f, dt); return; }
            CrewMember m = Crew.People[t];
            CrewTask? cur = Crew.Current(m);
            bool mine = cur != null && cur.Value.Job == CrewTask.Kind.Wheel && Wheel(cur.Value.Spot) == w;
            bool working = mine && c.NewTyres && Crew.AtPlace(m);
            float p = working ? Crew.WorkDone(m, c) : 0f;
            if (working && !m.Working && c.Elapsed < Crew.TimeOf(cur.Value, c).from) p = 0f;
            Vector3 hub = car.Hub(w), outward = Outward(car, hub);
            Vector3 at = Crew.World(m.At);
            Vector3 end = car.Forward * Mathf.Sign(Vector3.Dot(at - car.Middle, car.Forward));
            float ground = car.Middle.y + PitBoxKit.TyreWidth * 0.5f;
            Vector3 freshSpot = Flat(at + outward * 0.7f + end * 0.45f, ground), oldSpot = Flat(at + outward * 0.7f - end * 0.45f, ground);
            var upright = new KitPlace(hub, outward, car.Up);
            Vector3 along = car.Forward;
            KitPlace flat(Vector3 spot) => new KitPlace(spot, Vector3.up, along);
            KitPlace held = new KitPlace(Mid(hand, t) + Face(m) * 0.25f + Vector3.up * 0.05f, Vector3.Cross(Vector3.up, Face(m)), Vector3.up);

            // The fresh tyre: off the trolley, laid by the wheel, on the car, back on the trolley after.
            switch (fresh[w])
            {
                case Fresh.Rack:
                    if (m.Carrying && m.FetchTyre && c.Stage != CrewStage.Home && c.Stage != CrewStage.Release) fresh[w] = Fresh.Hands;
                    break;
                case Fresh.Hands:
                    if (!m.Carrying) fresh[w] = Fresh.Rack;
                    else if (working && c.Stage == CrewStage.Stop) fresh[w] = Fresh.Ground;
                    break;
                case Fresh.Ground:
                    // Back into their hands if the wheel is let go before the old one is off.
                    if (p >= Off) fresh[w] = Fresh.Lifting;
                    else if (!working) fresh[w] = old[w] == Old.None ? Fresh.Hands : Fresh.Lifting;
                    break;
                case Fresh.Lifting:
                    if (p >= On || !working) fresh[w] = Fresh.OnCar;
                    break;
                case Fresh.OnCar:
                    if (!m.Carrying && old[w] == Old.None)
                    {
                        // The old one set on the trolley in its place: the trolley's tyre again.
                        fresh[w] = Fresh.Rack;
                        Pieces[k] = AtRest(k);
                        Out[k] = false;
                    }
                    break;
            }
            Hidden[k] = fresh[w] == Fresh.OnCar;
            switch (fresh[w])
            {
                case Fresh.Rack: Home(k, 2.5f, dt); break;
                case Fresh.Hands: Toward(k, held, 2.5f, dt); break;
                case Fresh.Ground: Toward(k, flat(freshSpot), 2f, dt); break;
                case Fresh.Lifting:
                    // Up beside the hub, then in on to it.
                    Toward(k, p < (Off + On) * 0.5f ? new KitPlace(hub + outward * 0.45f, outward, car.Up) : upright, 2.2f, dt);
                    break;
                case Fresh.OnCar:
                    Pieces[k] = upright;
                    Out[k] = true;
                    break;
            }

            // The old wheel: pulled off once the gun has undone it, laid down, and carried back after.
            switch (old[w])
            {
                case Old.None:
                    if (working && p >= Undone && fresh[w] != Fresh.Rack && fresh[w] != Fresh.Hands)
                    {
                        old[w] = Old.Off;
                        OldWheel[w] = upright;
                    }
                    break;
                case Old.Off:
                    if (p >= Off || !working) old[w] = Old.Ground;
                    break;
                case Old.Ground:
                    if (!working || p >= 1f) old[w] = Old.Hands;
                    break;
                case Old.Hands:
                    if (!m.Carrying && Move(ref OldWheel[w], AtRest(k), 2.5f, dt)) old[w] = Old.None;
                    break;
            }
            OldOut[w] = old[w] != Old.None;
            WheelOff[w] = OldOut[w] && fresh[w] != Fresh.OnCar;
            switch (old[w])
            {
                case Old.Off:
                    float q = Mathf.Clamp01((p - Undone) / (Off - Undone));
                    Move(ref OldWheel[w], q < 0.5f ? new KitPlace(hub + outward * 0.4f, outward, car.Up) : flat(oldSpot), 2.2f, dt);
                    break;
                case Old.Ground: Move(ref OldWheel[w], flat(oldSpot), 2.2f, dt); break;
                case Old.Hands:
                    // Carried back, and set on the trolley where the fresh one stood.
                    if (m.Carrying) Move(ref OldWheel[w], held, 2.5f, dt);
                    break;
            }
        }

        private void Lance(in StopClock c, in KitCar car, HandOf hand, float dt)
        {
            const int k = (int)KitItem.Lance;
            LanceIn = false;
            if (jack < 0 || c.Stage != CrewStage.Stop || !c.NewTyres) { Home(k, 2.2f, dt); return; }
            CrewMember m = Crew.People[jack];
            CrewTask? cur = Crew.Current(m);
            bool mine = cur != null && cur.Value.Job == CrewTask.Kind.Jack;
            if (!mine || !Out[k] && !Under(m, KitItem.Lance) || Far(m, KitItem.Lance, hand)) { Home(k, 2.2f, dt); return; }
            Vector3 mid = Mid(hand, jack);
            (float from, float to) w = Crew.TimeOf(cur.Value, c);
            if (Crew.AtPlace(m) && c.Elapsed >= w.from - 0.4f && c.Elapsed < w.to)
            {
                // Pushed into the socket from the jack's hands, its tip inside the car.
                Vector3 d = (car.Socket - mid).normalized;
                var plugged = new KitPlace(car.Socket - d * 0.9f, Square(car.Forward, d), -d);
                float left = w.from - c.Elapsed;
                float speed = left > dt ? Mathf.Max((plugged.Position - Pieces[k].Position).magnitude / left, 0.3f) : 2.5f;
                LanceIn = Toward(k, plugged, speed, dt) && c.Elapsed >= w.from - dt;
                return;
            }
            Vector3 aim = (Face(m) * 0.7f + Vector3.down * 0.7f).normalized;
            Toward(k, new KitPlace(mid + Vector3.up * 0.05f, Square(Vector3.up, aim), -aim), Out[k] ? 2f : 2.5f, dt);
        }

        private void Lollipop(in StopClock c, HandOf hand, float dt)
        {
            const int k = (int)KitItem.Lollipop;
            if (lollipop < 0 || !Crew.People[lollipop].Carrying) { Home(k, 2.5f, dt); return; }
            CrewMember m = Crew.People[lollipop];
            // Up as the car goes, held up till it is away.
            bool up = c.Stage == CrewStage.Stop && c.Elapsed > c.Duration - 0.4f || c.Stage == CrewStage.Release && released < 1.8f;
            raise = Mathf.MoveTowards(raise, up ? 1f : 0f, dt / 0.25f);
            Vector3 foot = hand(lollipop, true) + Face(m) * 0.15f + Vector3.up * (raise * 0.55f - 0.95f);
            Toward(k, new KitPlace(foot, Face(m), Vector3.up), 3f, dt);
        }

        private void Extinguisher(HandOf hand, float dt)
        {
            const int k = (int)KitItem.Extinguisher;
            if (marshal < 0 || !Crew.People[marshal].Carrying) { Home(k, 2.5f, dt); return; }
            CrewMember m = Crew.People[marshal];
            Toward(k, new KitPlace(hand(marshal, true) - Vector3.up * 0.62f + Face(m) * 0.05f, Face(m), Vector3.up), 3f, dt);
        }

        // ---------------------------------------------------------------- moving

        /// <summary>Moves a piece towards a place at most speed m/s, turning with it; true once there.</summary>
        private bool Toward(int k, KitPlace to, float speed, float dt)
        {
            if (!Out[k]) Pieces[k] = AtRest(k);
            Out[k] = true;
            return Move(ref Pieces[k], to, speed, dt);
        }

        /// <summary>Sends a piece back to its rest; once there it is on its rest again.</summary>
        private void Home(int k, float speed, float dt)
        {
            Hidden[k] = false;
            if (!Out[k]) return;
            if (Move(ref Pieces[k], AtRest(k), speed, dt)) Out[k] = false;
        }

        private static bool Move(ref KitPlace from, KitPlace to, float speed, float dt)
        {
            Vector3 d = to.Position - from.Position;
            float dist = d.magnitude, step = speed * dt;
            bool there = dist <= Mathf.Max(step, There);
            from.Position = there ? to.Position : from.Position + d * (step / dist);
            // Turned the same share of the way as moved, or at once once there.
            float share = there ? 1f : Mathf.Clamp01(Mathf.Max(step / Mathf.Max(dist, 1e-4f), 1f - Mathf.Exp(-dt * 8f)));
            from.Forward = Vector3.Lerp(from.Forward, to.Forward, share).normalized;
            from.Up = Vector3.Lerp(from.Up, to.Up, share).normalized;
            if (from.Forward.sqrMagnitude < 0.5f) from.Forward = to.Forward;
            if (from.Up.sqrMagnitude < 0.5f) from.Up = to.Up;
            return there;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Is a person standing under a hanging piece's anchor, close enough across to pull it down?</summary>
        private bool Under(CrewMember m, KitItem item)
        {
            Vector3 d = Crew.World(m.At) - anchors[(int)item];
            d.y = 0f;
            return d.sqrMagnitude < Reach * Reach;
        }

        /// <summary>Would holding a hanging piece take it further from its anchor than its line reaches?</summary>
        private bool Far(CrewMember m, KitItem item, HandOf hand) =>
            (hand(System.Array.IndexOf(Crew.People, m), true) - anchors[(int)item]).magnitude > PitBoxKit.LineLength(item) - 0.15f;

        private static Vector3 Mid(HandOf hand, int person) => (hand(person, false) + hand(person, true)) * 0.5f;

        private Vector3 Face(CrewMember m) => Crew.WorldDirection(m.Facing).normalized;

        /// <summary>Out of the car at a point on its side, flat.</summary>
        private static Vector3 Outward(in KitCar car, Vector3 p)
        {
            Vector3 right = Vector3.Cross(car.Up, car.Forward).normalized;
            return Vector3.Dot(p - car.Middle, right) >= 0f ? right : -right;
        }

        /// <summary>The nearest to a wanted way that is square to an axis.</summary>
        private static Vector3 Square(Vector3 want, Vector3 axis)
        {
            Vector3 v = want - axis * Vector3.Dot(want, axis);
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.Cross(axis, Vector3.right).normalized;
        }

        private static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    }
}
