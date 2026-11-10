using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How an AI car gets back into the race after leaving the tarmac or spinning, with no Unity
    /// objects so it can be tested outside Unity. It takes over from the racing pilot only after one
    /// of those two things, never from the car's heading alone, so racing in a queue, braking or
    /// changing lanes never sets it off.
    ///
    /// Off the track: the car steers back to its racing line with its heading kept within
    /// <see cref="MaxEntryAngle"/> of the track direction, at the speed the runoff's grip allows.
    /// Spun: a car that has lost control and faces more than <see cref="SpunAngle"/> from the track
    /// direction brakes to a stop, then turns round at walking pace, forwards or in reverse, whichever
    /// has the room, and rejoins as above. On the track it is a ghost meanwhile
    /// (<see cref="Racecraft.Ghosted"/>), so the cars coming pass through it and it never waits for them.
    ///
    /// Call <see cref="Prepare"/> after racecraft and before <see cref="AiPilot.Drive"/>, and
    /// <see cref="Apply"/> on the controls the pilot returns.
    /// </summary>
    public sealed class Rejoin
    {
        public enum Stage { Racing, Rejoining, Stopping, Turning }

        // Thresholds, all here to tune.
        /// <summary>Most the car's heading may differ from the track direction while it rejoins, degrees.</summary>
        public const float MaxEntryAngle = 15f;
        /// <summary>A car that has lost control and faces further than this from the track direction has spun, degrees.</summary>
        public const float SpunAngle = 90f;
        /// <summary>A car is only put back on the track once the nearest car approaching from behind is this many seconds away at its speed...</summary>
        public const float GapSeconds = 5f;
        /// <summary>...and at least this many metres away.</summary>
        public const float GapMetres = 200f;
        /// <summary>Width of the kerb past the tarmac edge, metres: it grips nearly as well as the tarmac.</summary>
        public const float KerbWidth = 1.5f;
        /// <summary>The car is off the track once its centre is this far past the tarmac edge (past the kerb), metres.</summary>
        public const float OffTrackMargin = KerbWidth;
        /// <summary>Sliding sideways past this angle, degrees, or spinning faster than <see cref="SpinYawRate"/>, means the car has lost control.</summary>
        public const float SpinSlip = 35f;
        /// <summary>Yaw rate that means the car is spinning, radians a second.</summary>
        public const float SpinYawRate = 2.5f;
        /// <summary>Slip and yaw only count above this speed, m/s.</summary>
        public const float SpinSpeed = 5f;
        /// <summary>How long after losing control the car still counts as spun if it ends up facing the wrong way, seconds.</summary>
        public const float LostControlMemory = 3f;
        /// <summary>Most the rejoining car steers for, degrees: under <see cref="MaxEntryAngle"/>, so its swing stays inside it.</summary>
        public const float AimAngle = 10f;
        /// <summary>Further out on the runoff than <see cref="NearEdge"/>, metres, the car may head back more steeply, up to <see cref="FarAimAngle"/> at <see cref="FarEdge"/>.</summary>
        public const float NearEdge = 8f;
        public const float FarEdge = 20f;
        public const float FarAimAngle = 25f;
        /// <summary>Heading towards the line per metre from it, radians.</summary>
        public const float AimGain = 0.06f;
        /// <summary>Turning round ends once the car faces this close to the track direction, degrees.</summary>
        public const float AlignedAngle = 8f;
        /// <summary>Speed while turning round, m/s.</summary>
        public const float ManoeuvreSpeed = 3f;
        /// <summary>
        /// How fast the throttle builds, per second, while a car that wants to move at walking pace
        /// isn't getting going: deep gravel or a slope can hold it at the gentle throttle it starts with.
        /// </summary>
        public const float PushRate = 0.5f;
        /// <summary>At full push and still standing for this long, the way it is going is blocked, such as by a wall, seconds.</summary>
        public const float BlockedTime = 1.5f;
        /// <summary>
        /// Blocked this many times running, both ways, the car can't get itself out, as a small car
        /// can't from deep gravel: it is <see cref="Stranded"/> and waits to be put back on the track.
        /// </summary>
        public const int StrandedBlocks = 2;
        /// <summary>Gaining speed faster than this, m/s², the car is getting going and needs no more push.</summary>
        public const float PickUp = 0.3f;
        /// <summary>How far past the tarmac edge a car turning round on the track may go, metres.</summary>
        public const float ManoeuvreRoom = 1.5f;
        /// <summary>How far further out a car turning round off the track may go, metres.</summary>
        public const float RunoffRoom = 6f;
        /// <summary>Below this the car is stopped, m/s.</summary>
        public const float StoppedSpeed = 0.5f;
        /// <summary>Back to racing once on the tarmac, this close to the racing line and facing along it, metres.</summary>
        public const float OnLine = 1f;
        /// <summary>The runoff's grip relative to tarmac: gravel's, the worst of it.</summary>
        public const float RunoffGrip = 0.5f;
        /// <summary>A car leaving the runoff waits while a car coming couldn't brake to its speed in the room, with this many seconds to spare.</summary>
        public const float YieldSeconds = 1f;
        /// <summary>The braking assumed of the car coming, m/s²: well inside what any car can do.</summary>
        public const float YieldBraking = 6f;
        /// <summary>A car within this far ahead or behind is alongside: no joining beside it, metres.</summary>
        public const float AlongsideMetres = 10f;
        /// <summary>
        /// Only a car this far past the tarmac edge or more waits off the track for room, metres: closer
        /// in it is still half on the kerb, in sight of the cars coming, and rejoins at its shallow angle.
        /// </summary>
        public const float YieldFrom = KerbWidth + 1f;
        /// <summary>After trying this long without getting back to racing, the car is put back on the track as a last resort, seconds.</summary>
        public const float GiveUpAfter = 60f;

        private const float HalfLength = 2.3f;
        private const int TrackSearch = 40;   // samples either side searched for the nearest track sample
        private const float HeadingGain = 1.0f;
        private const float YawGain = 0.12f;
        private const float SlideGain = 0.7f;
        /// <summary>Rejoining further than this off the heading it wants, degrees, the car brakes down to <see cref="TurnSlowSpeed"/> to turn.</summary>
        private const float TurnSlowAngle = 20f;
        private const float TurnSlowSpeed = 12f;
        private const float SlowingBrake = 0.4f;
        /// <summary>Share of the runoff's grip the brakes may use.</summary>
        private const float RunoffBraking = 0.75f;
        /// <summary>Share of the throttle used on the runoff.</summary>
        private const float RunoffThrottle = 0.6f;
        /// <summary>Sliding past this on the runoff, degrees, the car neither brakes nor drives, to catch the slide.</summary>
        private const float SlideLimit = 6f;
        /// <summary>How far ahead the car looks for bends while on the runoff, metres.</summary>
        private const float RunoffLookahead = 200f;

        public Stage State { get; private set; }
        /// <summary>True while the rejoin drives the car, not the racing pilot.</summary>
        public bool Busy => State != Stage.Racing;
        /// <summary>True while it is getting the car back.</summary>
        public bool Trying => Busy;
        /// <summary>
        /// True when the car has tried both ways and can't move, so only being put back on the track
        /// gets it out. The driver does that once the gap is clear.
        /// </summary>
        public bool Stranded => Busy && blocks >= StrandedBlocks;
        /// <summary>Why the car last left racing: "off" (left the tarmac) or "spun".</summary>
        public string Cause { get; private set; } = "";
        /// <summary>The car's heading from the track direction last step, degrees, positive with the nose to the left.</summary>
        public float Heading { get; private set; }
        /// <summary>How far the car's centre was past the tarmac edge last step, metres (negative on the tarmac).</summary>
        public float PastEdge { get; private set; }
        /// <summary>True when no car approaching from behind is inside the gap a car needs before it is put back on the track.</summary>
        public bool GapClear { get; private set; } = true;
        /// <summary>True while it waits off the tarmac for room to rejoin.</summary>
        public bool Yielding { get; private set; }
        /// <summary>The heading from the track direction the rejoining car steers for, degrees.</summary>
        public float Aim { get; private set; }
        /// <summary>Seconds since the car last lost control.</summary>
        public float SinceLostControl { get; private set; } = float.PositiveInfinity;

        private bool reverse, switching, turningLeft;
        private bool legChosen;
        private int trackIndex = -1;
        private float lateralNow;   // the car's offset from the track's middle, last step, metres, positive left
        private float turnOuter;   // furthest out from the track middle a car turning round off the track may go
        private bool turnOff;      // the turn started off the tarmac, so keeps to the runoff's bounds
        private float push;        // extra throttle built up while the car can't get going, 0 to 1
        private float blockedFor;  // seconds at full push without moving
        private int blocks;        // times blocked running, since it last got going
        private float stepDt, lastSpeed, prevSpeed;   // speeds this step and the one before, m/s

        /// <summary>Back to racing, as after the car is put back on the track.</summary>
        public void Reset()
        {
            State = Stage.Racing;
            reverse = switching = legChosen = false;
            push = blockedFor = 0f;
            blocks = 0;
            SinceLostControl = float.PositiveInfinity;
            trackIndex = -1;
        }

        /// <summary>The car has lost control another way, such as a crash into a barrier.</summary>
        public void LostControl() => SinceLostControl = 0f;

        /// <summary>After racecraft, before the pilot drives: while rejoining, the pilot's lane is where the car is, at the runoff's grip.</summary>
        public void Prepare(AiPilot pilot)
        {
            if (State == Stage.Racing || pilot.Index < 0 || trackIndex < 0) return;
            pilot.SideFilter = false;
            pilot.Offset = Mathf.Clamp(pilot.Shift, AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
            if (PastEdge > KerbWidth)
            {
                pilot.SpeedScale *= Mathf.Sqrt(RunoffGrip);
                pilot.SpeedCap = Mathf.Min(pilot.SpeedCap, RunoffSpeed(pilot, trackIndex, lateralNow, RunoffGrip));
            }
            // Back on the tarmac off its line, it follows the track where it is, not where its line is.
            else pilot.SpeedCap = Mathf.Min(pilot.SpeedCap, RunoffSpeed(pilot, trackIndex, lateralNow, 1f));
        }

        /// <summary>
        /// Fastest the car may go on the runoff: slow enough to follow every bend in the next stretch
        /// of track at the runoff's grip, braking for them as hard as the runoff allows. The car keeps
        /// along the track where it is, so out to the inside of a bend it turns tighter than the
        /// track's middle does.
        /// </summary>
        private static float RunoffSpeed(AiPilot pilot, int k, float lateral, float surface)
        {
            CarSpec spec = pilot.Spec;
            Circuit c = pilot.Line.Circuit;
            float grip = surface * spec.Grip * 9.81f;
            // Braking planned on less than the brakes may use, as the turn takes its share of the grip.
            float braking = 0.5f * RunoffBraking * grip;
            float cap = float.PositiveInfinity;
            int n = Mathf.RoundToInt(RunoffLookahead / c.Step);
            for (int i = 0; i < n; i++)
            {
                float kappa = Mathf.Abs(Along(c, c.Wrap(k + i), lateral));
                if (kappa < 1e-4f) continue;
                cap = Mathf.Min(cap, Mathf.Sqrt(grip / kappa + 2f * braking * i * c.Step));
            }
            return cap;
        }

        /// <summary>
        /// After the pilot drives: decides whether the car is racing, rejoining, stopped or turning
        /// round, and returns the controls for it. <c>field</c> is every car on track, this one included.
        /// </summary>
        public AiPilot.Controls Apply(AiPilot pilot, AiPilot.Controls c, Vector3 position, Vector3 forward, Vector3 velocity,
            float yawRate, TrafficCar me, IReadOnlyList<TrafficCar> field, float dt)
        {
            if (pilot.Index < 0) return c;
            stepDt = dt;
            RacingLine line = pilot.Line;
            Circuit circuit = line.Circuit;
            // The track sample beside the car. Far out on the runoff the line's nearest sample can be
            // well along the track from it, so the track's own centre is searched.
            int k = trackIndex = NearestTrack(circuit, position, trackIndex >= 0 ? trackIndex : pilot.Index);
            Vector3 t = circuit.Forward[k], left = circuit.Left[k];
            Vector3 f = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 v = new Vector3(velocity.x, 0f, velocity.z);
            float speed = v.magnitude;
            prevSpeed = lastSpeed;
            lastSpeed = speed;
            if (speed > 1f) blocks = 0;
            float heading = Mathf.Atan2(Vector3.Dot(f, left), Vector3.Dot(f, t));
            Heading = heading * Mathf.Rad2Deg;
            Vector3 fromCentre = position - circuit.Centre[k];
            float lateral = fromCentre.x * left.x + fromCentre.z * left.z;
            lateralNow = lateral;
            float half = circuit.HalfWidth(k);
            PastEdge = Mathf.Abs(lateral) - half;
            bool off = PastEdge > OffTrackMargin;

            float slip = speed > SpinSpeed ? Mathf.Abs(Mathf.Atan2(f.x * v.z - f.z * v.x, Vector3.Dot(f, v))) * Mathf.Rad2Deg : 0f;
            bool losing = speed > SpinSpeed && (slip > SpinSlip || Mathf.Abs(yawRate) > SpinYawRate);
            SinceLostControl = losing ? 0f : SinceLostControl + dt;
            bool spun = SinceLostControl < LostControlMemory && Mathf.Abs(Heading) > SpunAngle;
            GapClear = Clear(me, field, circuit.Lap);
            bool yielding = Yielding = State == Stage.Rejoining && PastEdge > YieldFrom && PastEdge < NearEdge && !RoomToJoin(me, field, circuit.Lap, speed, half);

            switch (State)
            {
                case Stage.Racing:
                    if (spun) Enter(Stage.Stopping, "spun");
                    else if (off) Enter(Stage.Rejoining, "off");
                    break;
                case Stage.Rejoining:
                    // Sliding on the runoff: stop, and start again from there.
                    if (spun || (losing && PastEdge > 0f)) Enter(Stage.Stopping, "spun");
                    break;
            }

            switch (State)
            {
                case Stage.Racing:
                    return c;

                case Stage.Stopping:
                    // Stopped: straight on to turning round, or to rejoining if it already faces the way.
                    if (speed < StoppedSpeed)
                    {
                        if (Mathf.Abs(Heading) > AlignedAngle) StartTurning(heading, lateral);
                        else State = Stage.Rejoining;
                    }
                    return Brakes();

                case Stage.Turning:
                    if (Mathf.Abs(Heading) <= AlignedAngle)
                    {
                        State = Stage.Rejoining;
                        reverse = false;
                        return Brakes();
                    }
                    return Turn(pilot, heading, lateral, half, speed, Vector3.Dot(v, f));

                default:
                    return Steer(pilot, c, heading, lateral, half, speed, v, yawRate, yielding, me, field);
            }
        }

        private void Enter(Stage stage, string cause)
        {
            State = stage;
            Cause = cause;
            reverse = switching = legChosen = false;
        }

        private static AiPilot.Controls Brakes() => new AiPilot.Controls { Brake = 1f };

        /// <summary>
        /// Rejoining: steer for the racing line, never more than <see cref="AimAngle"/> from the track
        /// direction. Heading for the tarmac at a steeper angle than <see cref="MaxEntryAngle"/>, it
        /// brakes before the edge, and once stopped turns round off the track first.
        /// </summary>
        private AiPilot.Controls Steer(AiPilot pilot, AiPilot.Controls c, float heading, float lateral, float half, float speed, Vector3 v, float yawRate, bool yielding, TrafficCar me, IReadOnlyList<TrafficCar> field)
        {
            int k = trackIndex;
            RacingLine line = pilot.Line;
            Circuit circuit = line.Circuit;
            Vector3 lt = line.Tangent[k];
            float lineAngle = Mathf.Atan2(Vector3.Dot(lt, circuit.Left[k]), Vector3.Dot(lt, circuit.Forward[k]));
            float fromLine = lateral - line.Lateral[k];
            // Far out on the runoff it may head back more steeply, easing to the aim by the edge.
            float far = Mathf.Clamp01((PastEdge - NearEdge) / (FarEdge - NearEdge));
            float aim = Mathf.Lerp(AimAngle, FarAimAngle, far) * Mathf.Deg2Rad;
            float want = Mathf.Clamp(lineAngle - AimGain * fromLine, -aim, aim);
            // Traffic coming: stay off the tarmac, running alongside it off the throttle, until there is room.
            if (yielding) c.Throttle = 0f;
            // Off the tarmac with a car alongside, it runs parallel until the car is by.
            if (yielding || (PastEdge > 0f && Alongside(me, field, circuit.Lap, half))) want = 0f;

            Aim = want * Mathf.Rad2Deg;
            // Rejoined: on the tarmac, on the line and running along it.
            if (PastEdge <= 0f && Mathf.Abs(fromLine) < OnLine && Mathf.Abs(heading - lineAngle) < AlignedAngle * Mathf.Deg2Rad)
            {
                State = Stage.Racing;
                return c;
            }

            // Going towards the tarmac too steeply: turn away and slow before the edge, and if it comes
            // to a stop still too steep, turn round off the track.
            float across = lateral > 0f ? -Mathf.Sin(heading) : Mathf.Sin(heading);   // share of forward travel towards the middle
            float max = MaxEntryAngle * Mathf.Deg2Rad;
            bool tooSteep = false;
            if (PastEdge > 0f && Mathf.Abs(heading) > max && across > 0f)
            {
                float stopping = speed * speed / (2f * RunoffBraking * RunoffGrip * pilot.Spec.Grip * 9.81f);
                if (PastEdge < stopping * across + HalfLength + 1f)
                {
                    if (speed < StoppedSpeed)
                    {
                        StartTurning(heading, lateral);
                        return Brakes();
                    }
                    tooSteep = true;
                }
            }
            // Slow and facing well away from the way to go: stop and turn round where it is.
            if (speed < 2f * ManoeuvreSpeed && Mathf.Abs(heading) > 2f * max)
            {
                if (speed > StoppedSpeed) return Brakes();
                // On the tarmac it turns round as a spun car does, a ghost while it does.
                if (PastEdge <= 0f) Cause = "spun";
                StartTurning(heading, lateral);
                return Brakes();
            }

            // Steering on the body's heading, which is what the limit is on, with the curvature it takes
            // to keep along the track where the car is, and yaw damping, no more lock than the surface's grip can use, and counter-steer in a
            // slide on top, which the grip doesn't limit.
            CarSpec spec = pilot.Spec;
            float wheelbase = 2f * spec.HalfWheelbase;
            float kappa = Along(circuit, k, lateral);
            Vector3 f = circuit.Forward[k] * Mathf.Cos(heading) + circuit.Left[k] * Mathf.Sin(heading);
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            float slide = speed > 5f ? Mathf.Atan2(Vector3.Dot(v, right), Vector3.Dot(v, f)) : 0f;
            float steer = Mathf.Atan(wheelbase * kappa) + HeadingGain * (heading - want) + YawGain * (speed * kappa - yawRate);
            float grip = spec.Grip * (PastEdge > KerbWidth ? RunoffGrip : 1f) * 9.81f;
            float lockForGrip = Mathf.Atan(wheelbase * grip / Mathf.Max(speed * speed, 1f)) + spec.PeakSlip;
            // Far off the heading it wants at speed: slow down first, turning gently, rather than slide.
            bool slowing = Mathf.Abs(heading - want) > TurnSlowAngle * Mathf.Deg2Rad && speed > TurnSlowSpeed;
            if (slowing)
            {
                c.Throttle = 0f;
                c.Brake = Mathf.Max(c.Brake, SlowingBrake);
                lockForGrip -= 0.7f * spec.PeakSlip;
            }
            // Too steep for the edge: off the throttle and braking, with grip kept for turning away.
            if (tooSteep)
            {
                c.Throttle = 0f;
                c.Brake = 1f;
            }
            float lockNow = Mathf.Min(spec.SteerLock, lockForGrip);
            c.Steer = Mathf.Clamp(Mathf.Clamp(steer, -lockNow, lockNow) + SlideGain * slide, -spec.SteerLock, spec.SteerLock);
            if (PastEdge > KerbWidth)
            {
                // On the runoff the brakes and throttle share what grip the turn leaves, and neither
                // is used in a slide: locked or spinning rear wheels on the runoff spin the car.
                float turning = Mathf.Clamp01(Mathf.Abs(c.Steer) / Mathf.Max(lockNow, 0.01f));
                float left = speed < SpinSpeed ? 1f : Mathf.Sqrt(Mathf.Max(1f - turning * turning, 0f));
                if (Mathf.Abs(slide) > SlideLimit * Mathf.Deg2Rad) left = 0f;
                c.Brake = Mathf.Min(c.Brake, left * RunoffBraking * grip / (spec.BrakeDecel * 9.81f));
                c.Throttle *= Mathf.Max(left, 0.15f) * RunoffThrottle;
            }
            else
            {
                // On the tarmac or kerb, still turning onto the line: no throttle while the turn needs all the grip.
                float turning = Mathf.Clamp01(Mathf.Abs(c.Steer) / Mathf.Max(lockNow, 0.01f));
                c.Throttle *= Mathf.Sqrt(Mathf.Max(1f - turning * turning, 0f));
            }
            c.Throttle = Crawl(c.Throttle, speed);
            c.Reverse = false;
            // Can't get going forwards: back out, as turning round does.
            if (Blocked(c.Throttle, speed))
            {
                StartTurning(heading, lateral);
                legChosen = true;
                reverse = true;
                return Brakes();
            }
            return c;
        }

        /// <summary>
        /// The throttle at walking pace: what is asked, or more while the car isn't getting going, up
        /// to full. Above walking pace it is what is asked.
        /// </summary>
        private float Crawl(float throttle, float speed)
        {
            if (throttle <= 0f || speed > ManoeuvreSpeed)
            {
                push = 0f;
                return throttle;
            }
            // Builds only while the car isn't picking up speed, and eases off once it is.
            bool gaining = speed - prevSpeed > PickUp * stepDt;
            push = Mathf.Clamp01(push + (gaining ? -PushRate : PushRate) * stepDt);
            return Mathf.Max(throttle, push);
        }

        /// <summary>True once the car has stood at full throttle for <see cref="BlockedTime"/>.</summary>
        private bool Blocked(float throttle, float speed)
        {
            blockedFor = throttle >= 0.99f && speed < StoppedSpeed ? blockedFor + stepDt : 0f;
            if (blockedFor < BlockedTime) return false;
            blockedFor = 0f;
            push = 0f;
            blocks++;
            return true;
        }

        private void StartTurning(float heading, float lateral)
        {
            turnOuter = Mathf.Abs(lateral) + RunoffRoom;
            turnOff = PastEdge > 0f;
            State = Stage.Turning;
            turningLeft = heading < 0f;   // nose right of the track direction: turn left, the short way
            legChosen = switching = false;
        }

        /// <summary>
        /// Turning round at walking pace with full lock: forwards or in reverse, whichever has the room
        /// for the turn, and back and forth, stopping each time, when neither has.
        /// </summary>
        private AiPilot.Controls Turn(AiPilot pilot, float heading, float lateral, float half, float speed, float along)
        {
            CarSpec spec = pilot.Spec;
            // Where the car may go across the track: a little past the edges on the track, and off it
            // only on its own side, out from the tarmac.
            // The bounds are those of where the turn started: switching as the car crosses the edge
            // would put it outside both and leave it rocking on the spot.
            float lo, hi;
            if (turnOff)
            {
                if (lateral > 0f) { lo = half + 0.5f; hi = turnOuter; }
                else { lo = -turnOuter; hi = -half - 0.5f; }
            }
            else { lo = -half - ManoeuvreRoom; hi = half + ManoeuvreRoom; }

            float sin = Mathf.Sin(heading);
            float sweep = HalfLength * Mathf.Abs(sin);
            // Forwards the car moves across towards the side its nose points; in reverse the other way.
            float Room(bool back)
            {
                float dir = (back ? -1f : 1f) * sin;
                return (dir >= 0f ? hi - lateral : lateral - lo) - sweep;
            }
            if (!legChosen)
            {
                float radius = 2f * spec.HalfWheelbase * Mathf.Cos(spec.SteerLock) / Mathf.Sin(spec.SteerLock);
                float need = radius * (1f - Mathf.Cos(heading));
                float ahead = Room(false), behind = Room(true);
                reverse = ahead < need && behind > ahead;
                legChosen = true;
            }
            // Out of room this way: the other way, unless that has even less.
            else if (!switching && Room(reverse) < 0.3f && Room(!reverse) > Room(reverse) && speed > 0.2f) switching = true;

            if (switching)
            {
                if (speed < StoppedSpeed)
                {
                    switching = false;
                    reverse = !reverse;
                }
                return new AiPilot.Controls { Brake = 1f, Reverse = reverse, Steer = 0f };
            }

            var c = new AiPilot.Controls { Reverse = reverse };
            // Front wheels to the turn going forwards, away from it in reverse.
            float lockSide = (turningLeft ? -1f : 1f) * (reverse ? -1f : 1f);
            c.Steer = lockSide * spec.SteerLock;
            // Still rolling the wrong way: brake first.
            if ((reverse && along > 0.3f) || (!reverse && along < -0.3f)) c.Brake = 1f;
            else if (speed < ManoeuvreSpeed) c.Throttle = Mathf.Clamp01(0.2f + 0.15f * (ManoeuvreSpeed - speed));
            else c.Brake = Mathf.Clamp01(0.3f * (speed - ManoeuvreSpeed));
            c.Throttle = Crawl(c.Throttle, speed);
            // Full throttle and going nowhere: something is in the way, so it goes the other way.
            if (Blocked(c.Throttle, speed)) switching = true;
            return c;
        }

        /// <summary>
        /// Curvature of a path that keeps along the track at a lateral offset, positive for a right-hander:
        /// tighter than the track's middle on the inside of a bend, looser on the outside.
        /// </summary>
        private static float Along(Circuit c, int k, float lateral)
        {
            Vector3 a = c.Forward[k], b = c.Forward[c.Wrap(k + 1)];
            float bend = (a.z * b.x - a.x * b.z) / c.Step;
            return bend / Mathf.Max(1f + bend * lateral, 0.2f);
        }

        /// <summary>The track sample nearest a point, searched from one near it.</summary>
        private static int NearestTrack(Circuit c, Vector3 p, int from)
        {
            int best = c.Wrap(from);
            float bestSqr = float.MaxValue;
            for (int i = -TrackSearch; i <= TrackSearch; i++)
            {
                int j = c.Wrap(from + i);
                float dx = c.Centre[j].x - p.x, dz = c.Centre[j].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < bestSqr) { bestSqr = d; best = j; }
            }
            return best;
        }

        /// <summary>
        /// True when a car moving off the runoff at <c>speed</c> can join the tarmac in front of the
        /// cars coming: each could brake to its speed in the room there is, with
        /// <see cref="YieldSeconds"/> to spare, and none is alongside.
        /// </summary>
        public static bool RoomToJoin(TrafficCar me, IReadOnlyList<TrafficCar> field, float lap, float speed, float half)
        {
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id || OffTrack(o, half)) continue;
                float behind = Mathf.Repeat(me.Distance - o.Distance + AlongsideMetres, lap) - AlongsideMetres;
                if (behind > lap * 0.5f) continue;
                if (behind < AlongsideMetres) return false;
                float closing = o.Speed - speed;
                if (closing <= 0f) continue;
                float need = (o.Speed * o.Speed - speed * speed) / (2f * YieldBraking) + YieldSeconds * o.Speed;
                if (behind < need) return false;
            }
            return true;
        }

        /// <summary>
        /// True for a car off the track itself, such as another one rejoining: it is not in the way on
        /// the tarmac, and two cars that each waited for the other would wait for ever.
        /// </summary>
        private static bool OffTrack(in TrafficCar o, float half) => Mathf.Abs(o.Lateral) > half + OffTrackMargin;

        /// <summary>True when another car is within <see cref="AlongsideMetres"/> ahead or behind.</summary>
        public static bool Alongside(TrafficCar me, IReadOnlyList<TrafficCar> field, float lap, float half)
        {
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id || OffTrack(o, half)) continue;
                if (Mathf.Abs(Mathf.Repeat(me.Distance - o.Distance + 0.5f * lap, lap) - 0.5f * lap) < AlongsideMetres) return true;
            }
            return false;
        }

        /// <summary>
        /// True when every car approaching from behind is at least <see cref="GapSeconds"/> at its speed
        /// and <see cref="GapMetres"/> away. Stopped cars are not approaching.
        /// </summary>
        public static bool Clear(TrafficCar me, IReadOnlyList<TrafficCar> field, float lap)
        {
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id || o.Speed < 1f) continue;
                // A car alongside or just ahead counts too: the car turning round could swing into it.
                float behind = Mathf.Repeat(me.Distance - o.Distance + AlongsideMetres, lap) - AlongsideMetres;
                if (behind > lap * 0.5f) continue;
                if (behind < Mathf.Max(GapMetres, GapSeconds * o.Speed)) return false;
            }
            return true;
        }
    }
}
