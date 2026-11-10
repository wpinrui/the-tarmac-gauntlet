using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The thinking half of an AI driver, with no Unity objects so it can be tested outside Unity.
    /// Each physics step it reads where the car is and returns throttle, brake and steering to
    /// follow the racing line at the speed profile's target.
    ///
    /// Driver stats, 0 to 100:
    /// Pace sets how much of the car's grip and braking the target speeds use.
    /// Consistency sets how much the corner speeds wander from lap to lap.
    /// Safety sets how often a corner is overcooked: braking too late and running wide.
    ///
    /// Besides the racing line it has lanes either side of it, each with its own target speeds.
    /// <see cref="Offset"/> picks a point between them, so the car can run beside the line to
    /// pass, defend or make room, at a speed that suits the path it is really on.
    /// <see cref="Racecraft"/> sets the offset and <see cref="SpeedCap"/> from the cars around it.
    /// </summary>
    public sealed class AiPilot
    {
        public struct Controls
        {
            public float Throttle, Brake;
            /// <summary>Front wheel angle, radians, positive to the right.</summary>
            public float Steer;
            /// <summary>In reverse gear: only <see cref="Rejoin"/> asks for it, to turn round.</summary>
            public bool Reverse;
        }

        private const float G = 9.81f;
        private const float Reaction = 0.1f;        // how far ahead the speed target is read, seconds
        private const float SpeedGain = 2.5f;       // extra acceleration per m/s off target
        private const float HeadingGain = 1.0f;
        private const float LateralGain = 1.6f;     // Stanley gain on lateral error
        private const float YawGain = 0.12f;        // steering per rad/s of yaw rate off what the line needs
        private const float SlideGain = 0.7f;       // counter-steer per radian of slide
        private const float WindowIn = 350f;        // a corner's speed change starts this far before it
        private const float RampLength = 100f;
        private const float WindowOut = 60f;

        /// <summary>How far each lane sits from the racing line, metres, positive left. The middle one is the line itself.</summary>
        public static readonly float[] LaneShifts = { -12f, -6f, -3f, 0f, 3f, 6f, 12f };
        private const int MiddleLane = 3;

        public readonly RacingLine Line;
        public readonly SpeedProfile Profile;
        public readonly RacingLine[] Lanes;
        public readonly SpeedProfile[] LaneProfiles;

        /// <summary>Where to drive relative to the racing line, metres, positive left. Set by racecraft.</summary>
        public float Offset;
        /// <summary>Fastest the car may go right now, m/s: 0 when racecraft brakes for the car ahead.</summary>
        public float SpeedCap = float.PositiveInfinity;
        /// <summary>Share of the target speed to drive at.</summary>
        public float SpeedScale = 1f;
        /// <summary>
        /// The side collision filter (TORCS filterSColl), set by racecraft for this step: whether it
        /// acts, the share d/c of the pilot's own steering kept, the steering that runs parallel to the
        /// car alongside (psteer, radians, positive to the right), and its factor, 1.5 or 2.
        /// </summary>
        public bool SideFilter;
        public float SideWeight = 1f, SideSteer, SideFactor = 2f;
        /// <summary>
        /// True while the car is in another's slipstream: with less drag it outruns the target speeds
        /// on the straights, so it keeps its foot down instead of lifting back to them.
        /// </summary>
        public bool Towed;
        /// <summary>
        /// Share of fresh tyres' grip the car has, below 1 as they wear: target speeds come down by
        /// its square root, so the driver brakes earlier and corners slower on old tyres.
        /// </summary>
        public float Grip = 1f;
        /// <summary>How much slower than it wants the car ahead made it go last step, m/s.</summary>
        public float HeldBy { get; private set; }
        /// <summary>Where the car's centre really is relative to the racing line, metres, positive left.</summary>
        public float Shift { get; private set; }
        public readonly CarSpec Spec;
        public readonly float Pace, Consistency, Safety;

        /// <summary>Line sample nearest the car's centre.</summary>
        public int Index { get; private set; } = -1;
        /// <summary>Front axle's distance from the line, positive to the left.</summary>
        public float LateralError { get; private set; }
        public float TargetSpeed { get; private set; }
        /// <summary>The speed the car would aim for with no car ahead holding it back, m/s.</summary>
        public float FreeSpeed { get; private set; }
        /// <summary>True while the driver is in an overcooked corner.</summary>
        public bool Overcooking => committed;
        /// <summary>How many corners this driver has overcooked so far.</summary>
        public int Mistakes { get; private set; }

        private int frontIndex = -1;
        private readonly System.Random random;
        private readonly CornerData[] corners;
        private readonly float[] cornerScale;
        private readonly bool[] overcooked;
        private bool committed;   // inside an overcooked corner: braking only to the wrong plan
        private readonly bool[] rolled;

        /// <summary>
        /// A driver for one line. With <c>lanes</c> false it only ever drives the line itself (a
        /// formation column or a pit route), so no lanes are built beside it. <c>maxSpeed</c>, if
        /// given, caps the target speed at each sample, as the pit lane limiter does.
        /// </summary>
        public AiPilot(RacingLine line, CarSpec spec, float pace, float consistency, float safety, int seed, bool lanes = true, float[] maxSpeed = null)
        {
            Line = line;
            Spec = spec;
            Pace = Mathf.Clamp(pace, 0f, 100f);
            Consistency = Mathf.Clamp(consistency, 0f, 100f);
            Safety = Mathf.Clamp(safety, 0f, 100f);
            float limit = LimitFor(Pace);
            Lanes = new RacingLine[LaneShifts.Length];
            LaneProfiles = new SpeedProfile[LaneShifts.Length];
            SpeedProfile only = lanes ? null : new SpeedProfile(line, spec, limit, maxSpeed);
            for (int i = 0; i < LaneShifts.Length; i++)
            {
                if (!lanes)
                {
                    Lanes[i] = line;
                    LaneProfiles[i] = only;
                    continue;
                }
                Lanes[i] = i == MiddleLane ? line : new RacingLine(line, LaneShifts[i]);
                LaneProfiles[i] = new SpeedProfile(Lanes[i], spec, limit, maxSpeed);
            }
            Profile = LaneProfiles[MiddleLane];
            random = new System.Random(seed);
            corners = line.Circuit.Data.corners;
            cornerScale = new float[corners.Length];
            overcooked = new bool[corners.Length];
            rolled = new bool[corners.Length];
            for (int i = 0; i < corners.Length; i++) cornerScale[i] = 1f;
        }

        /// <summary>Target speed at a line sample for a path that far from the racing line, m/s.</summary>
        public float LaneSpeed(int k, float offset)
        {
            float lane = Mathf.Clamp(offset, LaneShifts[0], LaneShifts[LaneShifts.Length - 1]);
            int a = 0;
            while (a < LaneShifts.Length - 2 && lane > LaneShifts[a + 1]) a++;
            float blend = (lane - LaneShifts[a]) / (LaneShifts[a + 1] - LaneShifts[a]);
            return Mathf.Lerp(LaneProfiles[a].Speed[k], LaneProfiles[a + 1].Speed[k], blend);
        }

        /// <summary>Share of the car's limit the driver uses: 86% at Pace 0, 98% at Pace 100.</summary>
        public static float LimitFor(float pace) => 0.86f + 0.12f * pace / 100f;

        /// <summary>Forget where the car was, after it is moved (a reset).</summary>
        public void Lost()
        {
            Index = -1;
            frontIndex = -1;
        }

        /// <summary>
        /// Starts the search for the car from a track sample, when it takes over from another
        /// pilot on the same stretch: every line has one sample per track sample.
        /// </summary>
        public void Relocate(int k)
        {
            Index = k;
            frontIndex = k;
        }

        public Controls Drive(Vector3 position, Vector3 forward, Vector3 velocity, float yawRate)
        {
            Circuit c = Line.Circuit;
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float speed = flatVelocity.magnitude;

            Blend(out int a, out int b, out float blend);
            RacingLine nearLane = Lanes[blend < 0.5f ? a : b];

            Index = nearLane.Nearest(position, Index);
            Vector3 front = position + flatForward * Spec.HalfWheelbase;
            frontIndex = nearLane.Nearest(front, frontIndex);
            RollCorners();
            Vector3 lineLeft = new Vector3(-Line.Tangent[Index].z, 0f, Line.Tangent[Index].x);
            Vector3 fromLine = position - Line.Point[Index];
            Shift = fromLine.x * lineLeft.x + fromLine.z * lineLeft.z;

            // Lateral error and heading error at the front axle, against the path's tangent.
            int k = frontIndex;
            Vector3 tangent = Vector3.Lerp(Lanes[a].Tangent[k], Lanes[b].Tangent[k], blend).normalized;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            Vector3 offset = front - Vector3.Lerp(Lanes[a].Point[k], Lanes[b].Point[k], blend);
            LateralError = Vector3.Dot(new Vector3(offset.x, 0f, offset.z), left);
            // Heading of travel, not of the body: at the limit the body is yawed into the corner.
            Vector3 heading = speed > 3f ? flatVelocity / speed : flatForward;
            float headingError = Mathf.Atan2(heading.z * tangent.x - heading.x * tangent.z, Vector3.Dot(heading, tangent));

            // Steering: the line's own curvature, plus Stanley corrections for heading and offset.
            float ahead = k + speed * 0.1f / c.Step;
            int ka = c.Wrap(Mathf.RoundToInt(ahead));
            float kappa = Mathf.Lerp(Lanes[a].Curvature[ka], Lanes[b].Curvature[ka], blend);
            float wheelbase = 2f * Spec.HalfWheelbase;
            float steer = Mathf.Atan(wheelbase * kappa)
                + HeadingGain * headingError
                + Mathf.Atan(LateralGain * LateralError / (speed + 4f));
            // Damp the yaw towards what the line needs, so the car doesn't weave or spin, and
            // steer into a slide: the body's angle to its path, positive when the tail steps out left.
            Vector3 right = new Vector3(flatForward.z, 0f, -flatForward.x);
            float slide = speed > 5f ? Mathf.Atan2(Vector3.Dot(flatVelocity, right), Vector3.Dot(flatVelocity, flatForward)) : 0f;
            steer += YawGain * (speed * kappa - yawRate) + SlideGain * slide;
            if (SideFilter)
            {
                float p = steer * SideWeight + SideFactor * SideSteer * (1f - SideWeight);
                // Keep the car's own steering when it already turns harder the same way.
                steer = p * steer > 0f && Mathf.Abs(steer) > Mathf.Abs(p) ? steer : p;
            }
            steer = Mathf.Clamp(steer, -Spec.SteerLock, Spec.SteerLock);

            // Speed: read the target a reaction time ahead, follow its acceleration, and close the gap.
            float lookahead = Index + Mathf.Max(speed, 5f) * Reaction / c.Step;
            int la = c.Wrap(Mathf.RoundToInt(lookahead));
            float scale = ScaleAt(la);
            float laneSpeed = Mathf.Lerp(LaneProfiles[a].SpeedAt(lookahead), LaneProfiles[b].SpeedAt(lookahead), blend);
            float worn = Mathf.Sqrt(Mathf.Clamp(Grip, 0.3f, 1f));
            float target = laneSpeed * scale * SpeedScale * worn;
            // Held back by the car ahead: match the cap and drop the profile's own acceleration.
            float laneAccel = Mathf.Lerp(LaneProfiles[a].Accel[la], LaneProfiles[b].Accel[la], blend);
            if (Towed && laneAccel > 0f && scale >= 1f) target = Mathf.Max(target, speed);
            FreeSpeed = target;
            bool held = SpeedCap < target;
            HeldBy = held ? target - SpeedCap : 0f;
            if (held) target = SpeedCap;
            float offLine = Mathf.Abs(LateralError);
            if (offLine > 4f) target = Mathf.Min(target, Mathf.Lerp(40f, 15f, (offLine - 4f) / 10f));
            if (Mathf.Abs(headingError) > 0.5f) target = Mathf.Min(target, 12f);
            TargetSpeed = target;

            // When overcooking a corner the driver believes the faster plan: no extra braking when the
            // car turns out to be too fast, so it runs wide or off.
            float correction = SpeedGain * (target - speed);
            if (committed && !held && correction < 0f) correction = 0f;
            // Held back only once the cap is down to this car's speed: until then it stays flat, rather
            // than trimming the throttle to creep up on the car ahead.
            bool capped = held && SpeedCap < speed + 0.5f;
            float want = (capped ? 0f : laneAccel * scale * scale * SpeedScale * SpeedScale * worn * worn) + correction;
            if (held && !capped) want = Mathf.Max(want, Profile.EngineAccel(speed));
            float needed = want + Profile.Resist(Index, speed);
            var controls = new Controls { Steer = steer };
            if (needed >= 0f)
            {
                // Only as much throttle as the rear tyres can take on top of the corner, and less
                // again once the tail is stepping out.
                float force = Mathf.Min(needed, Mathf.Min(Profile.Traction(speed), Profile.RearGripLeft(Index, speed, speed * yawRate)));
                float grip = Mathf.Clamp01(2f - Mathf.Abs(slide) / (0.5f * Spec.PeakSlip));
                controls.Throttle = Mathf.Clamp01(force / Mathf.Max(Profile.EngineAccel(speed), 0.5f)) * grip;
            }
            else
                controls.Brake = Mathf.Clamp01(-needed / (Spec.BrakeDecel * G));
            // Full brakes for the car ahead (racecraft's SpeedCap of 0), through the equivalent of the
            // TORCS robot's ABS filter: as hard as the tyres can take on top of the corner, as locked
            // or overloaded tyres spin the car.
            if (held && SpeedCap <= 0f)
            {
                // filterBColl's full brakes, whatever the steering: the brakes take the tyres' grip
                // (with the downforce at this speed), as far as they can use it. Not in the tutorial:
                // the grip, not a locked wheel, is the limit, standing in for the robot's ABS.
                float decel = Mathf.Min(Spec.BrakeDecel * G, TyreGrip(speed));
                controls.Throttle = 0f;
                controls.Brake = decel / (Spec.BrakeDecel * G);
            }
            return controls;
        }

        /// <summary>The tyres' grip at a speed, m/s², with the downforce and the tyres' wear.</summary>
        public float TyreGrip(float speed) =>
            Spec.Grip * Mathf.Clamp(Grip, 0.3f, 1f) * (G + 0.5f * CarSpec.AirDensity * Spec.LiftArea * speed * speed / Spec.Mass);

        /// <summary>The path is a blend of the two lanes either side of the offset.</summary>
        private void Blend(out int a, out int b, out float blend)
        {
            float lane = Mathf.Clamp(Offset, LaneShifts[0], LaneShifts[LaneShifts.Length - 1]);
            a = 0;
            while (a < LaneShifts.Length - 2 && lane > LaneShifts[a + 1]) a++;
            b = a + 1;
            blend = (lane - LaneShifts[a]) / (LaneShifts[b] - LaneShifts[a]);
        }

        // ---------------------------------------------------------------- gliding

        /// <summary>
        /// Share of the profile's speeds a gliding car drives at. Driven in physics the car is never
        /// exactly on its limit and laps 2 to 3% slower than its profile, so a gliding car does too
        /// and keeps the same pace whichever way it is run.
        /// </summary>
        public const float GlidePace = 0.976f;
        /// <summary>Share of its speed a gliding car loses through a corner it overcooks, as if it ran wide.</summary>
        private const float OvercookLoss = 0.65f;

        /// <summary>
        /// True while the car glides: far from the camera it has no physics and moves along its path
        /// at the speed this driver would manage, held back by the same racecraft as a driven car.
        /// </summary>
        public bool Gliding { get; private set; }
        /// <summary>Speed along the path while gliding, m/s.</summary>
        public float GlideSpeed { get; private set; }
        /// <summary>Where a gliding car is: a fractional sample along its path.</summary>
        private float glideAt;

        /// <summary>Starts gliding from where the car is now, at its speed along the track.</summary>
        public void StartGlide(Vector3 position, float speed)
        {
            Circuit c = Line.Circuit;
            int k = Line.Nearest(position, Index);
            Vector3 lineLeft = new Vector3(-Line.Tangent[k].z, 0f, Line.Tangent[k].x);
            Vector3 fromLine = position - Line.Point[k];
            Offset = Mathf.Clamp(fromLine.x * lineLeft.x + fromLine.z * lineLeft.z, LaneShifts[0], LaneShifts[LaneShifts.Length - 1]);
            Blend(out int a, out int b, out float blend);
            float along = fromLine.x * Line.Tangent[k].x + fromLine.z * Line.Tangent[k].z;
            float segment = Mathf.Lerp(Lanes[a].Segment[k], Lanes[b].Segment[k], blend);
            glideAt = Mathf.Repeat(k + Mathf.Clamp(along / Mathf.Max(segment, 0.1f), -0.5f, 0.5f), c.Count);
            Index = frontIndex = (int)glideAt % c.Count;
            GlideSpeed = Mathf.Max(speed, 0f);
            Shift = Offset;
            LateralError = 0f;
            committed = false;
            Gliding = true;
        }

        /// <summary>Back to physics: <see cref="Drive"/> takes over from where the glide left the car.</summary>
        public void StopGlide() => Gliding = false;

        /// <summary>How far the gliding car is from the start line, metres along the track.</summary>
        public float GlideDistance => glideAt * Line.Circuit.Step;

        /// <summary>The gliding car's offset from the track centre, metres, positive left.</summary>
        public float GlideLateral
        {
            get
            {
                Blend(out int a, out int b, out float blend);
                return Mathf.Lerp(Lanes[a].Lateral[Index], Lanes[b].Lateral[Index], blend);
            }
        }

        /// <summary>
        /// Moves a gliding car on by dt: racecraft has set the offset and speed limits, as for
        /// <see cref="Drive"/>. Its speed follows the target for where it will be at the end of the
        /// step, gaining no faster than the engine allows, and the corners wander lap to lap as a
        /// driven car's do. An overcooked corner costs time instead of sending the car off.
        /// </summary>
        public void Glide(float dt)
        {
            Circuit c = Line.Circuit;
            Blend(out int a, out int b, out float blend);
            int k = (int)glideAt % c.Count;
            Index = frontIndex = k;
            RollCorners();

            float v = GlideSpeed;
            float end = glideAt + v * dt / c.Step;
            int ke = c.Wrap(Mathf.FloorToInt(end));
            float laneSpeed = Mathf.Lerp(LaneProfiles[a].SpeedAt(end), LaneProfiles[b].SpeedAt(end), blend);
            float worn = Mathf.Sqrt(Mathf.Clamp(Grip, 0.3f, 1f));
            float target = laneSpeed * GlideScaleAt(ke) * SpeedScale * worn * GlidePace;
            bool held = SpeedCap < target;
            HeldBy = held ? target - SpeedCap : 0f;
            if (held) target = SpeedCap;
            TargetSpeed = target;

            float next;
            if (target >= v) next = Mathf.Min(target, v + Mathf.Max(Profile.DriveAccel(v) - Profile.Resist(k, v), 0.3f) * dt);
            else next = Mathf.Max(target, v - (Spec.BrakeDecel * G + Profile.Resist(k, v)) * dt);
            GlideSpeed = next;

            // Along the path, sample by sample.
            float distance = 0.5f * (v + next) * dt;
            while (distance > 0f)
            {
                int j = (int)glideAt % c.Count;
                float segment = Mathf.Max(Mathf.Lerp(Lanes[a].Segment[j], Lanes[b].Segment[j], blend), 0.01f);
                float left = (1f - (glideAt - Mathf.Floor(glideAt))) * segment;
                if (distance < left) { glideAt += distance / segment; distance = 0f; }
                else { glideAt = Mathf.Floor(glideAt) + 1f; distance -= left; }
                if (glideAt >= c.Count) glideAt -= c.Count;
            }
            Index = frontIndex = (int)glideAt % c.Count;
            Shift = Offset;
            LateralError = 0f;
        }

        /// <summary>
        /// Where a gliding car is drawn: on its path <c>ahead</c> metres on from its last step, so it
        /// moves smoothly between steps. Forward follows the slope, up the track's crossfall.
        /// </summary>
        public void GlidePose(float ahead, out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            Circuit c = Line.Circuit;
            Blend(out int a, out int b, out float blend);
            float at = glideAt;
            int k = (int)at % c.Count;
            float segment = Mathf.Max(Mathf.Lerp(Lanes[a].Segment[k], Lanes[b].Segment[k], blend), 0.01f);
            at += Mathf.Clamp(ahead, 0f, 20f) / segment;
            k = c.Wrap(Mathf.FloorToInt(at));
            int k1 = c.Wrap(k + 1);
            float t = at - Mathf.Floor(at);
            Vector3 p0 = Vector3.Lerp(Lanes[a].Point[k], Lanes[b].Point[k], blend);
            Vector3 p1 = Vector3.Lerp(Lanes[a].Point[k1], Lanes[b].Point[k1], blend);
            position = p0 + (p1 - p0) * t;
            Vector3 tangent = (Vector3.Lerp(Lanes[a].Tangent[k], Lanes[b].Tangent[k], blend) * (1f - t)
                + Vector3.Lerp(Lanes[a].Tangent[k1], Lanes[b].Tangent[k1], blend) * t).normalized;
            float grade = Mathf.Lerp(Lanes[a].Grade[k], Lanes[b].Grade[k], blend);
            forward = new Vector3(tangent.x, grade, tangent.z).normalized;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            up = (Vector3.up - left * c.Data.crossfall[k]).normalized;
        }

        /// <summary>Steering a driven car needs to hold the gliding car's path: the path's own bend.</summary>
        public float GlideSteer => Mathf.Atan(2f * Spec.HalfWheelbase * GlideCurvature);

        /// <summary>Plan curvature of the gliding car's path where it is, 1/m, positive turning right.</summary>
        public float GlideCurvature
        {
            get
            {
                Blend(out int a, out int b, out float blend);
                return Mathf.Lerp(Lanes[a].Curvature[Index], Lanes[b].Curvature[Index], blend);
            }
        }

        /// <summary>
        /// <see cref="ScaleAt"/> for a gliding car: the corners wander as for a driven car, but an
        /// overcooked one is taken slower, for the time a driven car loses running wide.
        /// </summary>
        private float GlideScaleAt(int k)
        {
            Circuit c = Line.Circuit;
            float d = k * c.Step;
            float scale = 1f;
            for (int i = 0; i < corners.Length; i++)
            {
                float length = corners[i].e - corners[i].s;
                float before = c.Ahead(d, corners[i].s);
                float into = c.Ahead(corners[i].s, d);
                if (overcooked[i])
                {
                    if (into <= length) scale *= 1f - OvercookLoss;
                    else if (into <= length + WindowOut) scale *= 1f - OvercookLoss * (1f - (into - length) / WindowOut);
                    continue;
                }
                float weight;
                if (into <= length) weight = 1f;
                else if (into <= length + WindowOut) weight = 1f - (into - length) / WindowOut;
                else if (before <= WindowIn) weight = 1f;
                else if (before <= WindowIn + RampLength) weight = 1f - (before - WindowIn) / RampLength;
                else continue;
                scale *= 1f + (cornerScale[i] - 1f) * weight;
            }
            return scale;
        }

        /// <summary>
        /// As the car nears each corner, picks how fast this driver takes it this lap: a small
        /// wander from Consistency, and now and then an overcooked entry from Safety.
        /// </summary>
        private void RollCorners()
        {
            Circuit c = Line.Circuit;
            float here = Index * c.Step;
            for (int i = 0; i < corners.Length; i++)
            {
                float toCorner = c.Ahead(here, corners[i].s);
                bool near = toCorner < WindowIn + RampLength + 50f;
                bool inside = c.Ahead(corners[i].s, here) < corners[i].e - corners[i].s + WindowOut;
                if (near && !inside && !rolled[i])
                {
                    rolled[i] = true;
                    float spread = 0.035f * (1f - Consistency / 100f);
                    float scale = 1f + spread * Mathf.Clamp(Gaussian(), -2f, 1f);
                    float risk = 0.12f * (1f - Safety / 100f) * (1f - Safety / 100f);
                    overcooked[i] = random.NextDouble() < risk;
                    if (overcooked[i])
                    {
                        scale += 0.15f + 0.15f * (float)random.NextDouble();
                        Mistakes++;
                    }
                    cornerScale[i] = scale;
                }
                else if (!near && !inside) rolled[i] = false;
            }
        }

        /// <summary>Speed multiplier at a line sample, ramping in before each corner and out after it.</summary>
        private float ScaleAt(int k)
        {
            Circuit c = Line.Circuit;
            float d = k * c.Step;
            float scale = 1f;
            committed = false;
            for (int i = 0; i < corners.Length; i++)
            {
                float length = corners[i].e - corners[i].s;
                float before = c.Ahead(d, corners[i].s);
                float into = c.Ahead(corners[i].s, d);
                float weight;
                if (into <= length) weight = 1f;
                else if (into <= length + WindowOut) weight = 1f - (into - length) / WindowOut;
                else if (before <= WindowIn) weight = 1f;
                else if (before <= WindowIn + RampLength) weight = 1f - (before - WindowIn) / RampLength;
                else continue;
                scale *= 1f + (cornerScale[i] - 1f) * weight;
                if (overcooked[i] && weight > 0f) committed = true;
            }
            return scale;
        }

        private float Gaussian()
        {
            double u = 1.0 - random.NextDouble(), v = random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
        }
    }
}
