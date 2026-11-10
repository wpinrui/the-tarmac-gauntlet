using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>What one car on track looks like to the others.</summary>
    public struct TrafficCar
    {
        public int Id;
        /// <summary>Race distance: laps started times the lap length, plus the distance from the start line.</summary>
        public float Distance;
        /// <summary>Car centre's offset from the track centre, metres, positive left.</summary>
        public float Lateral;
        /// <summary>Speed along the track, m/s.</summary>
        public float Speed;
        /// <summary>The car's lap time at a typical pace, seconds: lower is a faster class.</summary>
        public float LapPace;
        /// <summary>Hardest the car's driver brakes, m/s².</summary>
        public float Braking;
        /// <summary>Speed across the track, m/s, positive left.</summary>
        public float Across;
        /// <summary>Heading of the car's body, radians anticlockwise seen from above.</summary>
        public float Yaw;
        /// <summary>A spun car stopping or turning round on the track: other cars pass through it and race as if it weren't there.</summary>
        public bool Ghost;
    }

    /// <summary>
    /// Every car on track, sorted by where it is on the lap, so each car finds the ones near it
    /// without looking at the whole field. Fill it once per step with <see cref="Set"/>.
    /// </summary>
    public sealed class TrafficField : IReadOnlyList<TrafficCar>
    {
        private TrafficCar[] cars = new TrafficCar[0];
        private float[] at = new float[0];
        private float lap = 1f;

        public int Count { get; private set; }
        public TrafficCar this[int i] => cars[i];
        /// <summary>The i-th car's distance from the start line, on this lap.</summary>
        public float At(int i) => at[i];

        public void Set(IReadOnlyList<TrafficCar> field, float lapLength)
        {
            lap = lapLength;
            Count = field.Count;
            if (cars.Length < Count)
            {
                cars = new TrafficCar[Count];
                at = new float[Count];
            }
            for (int i = 0; i < Count; i++)
            {
                cars[i] = field[i];
                at[i] = Mathf.Repeat(field[i].Distance, lap);
            }
            System.Array.Sort(at, cars, 0, Count);
        }

        /// <summary>The first car at or after a lap distance, wrapping round to the first car on the lap.</summary>
        public int From(float distance)
        {
            distance = Mathf.Repeat(distance, lap);
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (at[mid] < distance) lo = mid + 1;
                else hi = mid;
            }
            return lo == Count ? 0 : lo;
        }

        public IEnumerator<TrafficCar> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return cars[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// The race brain on top of an <see cref="AiPilot"/>, with no Unity objects so it can be tested
    /// outside Unity. It is the overtaking and collision avoidance of Bernhard Wymann's TORCS robot
    /// tutorial (chapter 6), implemented as written, in four parts. Where the online summary of the
    /// tutorial drops or garbles a line, Wymann's own robot (TORCS bt, driver.cpp) settles it: the
    /// side filter acts only on a car being steered towards and resets the offset, the overtake
    /// offset steps right for a car left of the middle and left for one right of it, and the
    /// braking distance's air term is CA * mu + CW.
    ///
    /// 1. Opponents: each car within 200 m ahead or 50 m behind is classed as in front and slower,
    ///    alongside, or neither (Opponent::update). A car in front is on a collision course when the
    ///    sideways space between the two would be under a metre.
    /// 2. Overtaking: of the cars in front, the one this car will catch soonest (least catch distance)
    ///    is the one to pass. The overtake offset, a place across the track, steps towards the side of
    ///    the track away from it, no further than a third of the track width less half a metre from
    ///    the middle, and steps back to the racing line when no car is in front (getOvertakeOffset).
    /// 3. Braking: for each car on a collision course, brake fully when the distance needed to slow to
    ///    its speed is longer than the gap to it (filterBColl).
    /// 4. Side collisions: when the nearest car alongside is within 2 m and this car is heading towards
    ///    it, the steering is blended towards running parallel to it (1.5 or 2 times the heading
    ///    difference, by who is nearer the middle or on the outside of the turn), unless the car's own
    ///    steering already turns harder the same way. Applied by <see cref="AiPilot.Drive"/>; the
    ///    overtake offset restarts from where the car is (filterSColl, as in TORCS bt's driver.cpp).
    ///
    /// The tutorial's constants are kept. The only scaling is the overtake offset step, given per
    /// TORCS robot step (0.1 m every 0.02 s), which becomes a rate over this car's step.
    /// </summary>
    public sealed class Racecraft
    {
        public enum Mode { Racing, Overtaking }

        // The tutorial's constants (opponent.h and driver.h).
        private const float FrontCollDist = 200f;          // FRONT_COLLDIST, m
        private const float BackCollDist = 50f;            // BACK_COLLDIST, m
        // LENGTH_MARGIN, WIDTH_MARGIN and SIDE_COLLISION_MARGIN are in Endurance, scaled per driver.
        private const float BorderOvertakeMargin = 0.5f;   // BORDER_OVERTAKE_MARGIN, m
        private const float WidthDiv = 3f;                 // WIDTH_DIV
        /// <summary>Not in the tutorial: how far ahead the braking check follows a car drifting across the track, s.</summary>
        private const float PredictFor = 1f;
        /// <summary>Not in the tutorial: how far ahead picking the overtaking side follows the car ahead's drift, s.</summary>
        private const float CatchPredictFor = 2f;
        // The overtaking width and OVERTAKE_OFFSET_INCREMENT are in Endurance.
        private const float G = 9.81f;

        // Racing etiquette, not in the tutorial: the F1 rule of one move when defending, and room for a car alongside.
        /// <summary>A car defends when a car behind closing on it is this close in time, s.</summary>
        private const float DefendTime = 1f;
        // A car closing faster than Endurance.NoDefendClosing is not defended against: the car ahead holds its line...
        /// <summary>...once that car is this close in time, s.</summary>
        private const float NoDefendTime = 2f;
        /// <summary>SPEED_PASS_MARGIN from bt's OPP_BACK: a car behind counts unless this much slower, m/s.</summary>
        private const float SpeedPassMargin = 5f;
        /// <summary>A defender with a slower car this close ahead in time is overtaking, and may move freely, s.</summary>
        private const float ExemptTime = 1f;

        /// <summary>The car's size, from its box collider: the tutorial's _dimension_x and _dimension_y.</summary>
        public const float CarLength = 4.6f, CarWidth = 1.9f;

        // Slipstream, which is the car physics' drag, not racecraft.
        private const float WakeFrom = 4.8f;
        private const float WakeLength = 80f;
        private const float WakeDrag = 0.45f;

        /// <summary>Overtaking while there is a car in front to pass, racing otherwise.</summary>
        public Mode State { get; private set; }
        /// <summary>The overtake offset: where the car runs relative to its racing line, metres, positive left.</summary>
        public float Target { get; private set; }
        /// <summary>True on a step the braking filter called for full brakes.</summary>
        public bool Braking { get; private set; }
        /// <summary>True on a step the side collision filter changed the steering.</summary>
        public bool SideSteering { get; private set; }
        /// <summary>True while a car behind is closing on this one: it may make one move.</summary>
        public bool Defending { get; private set; }
        /// <summary>True while defending with a slower car close ahead: it is overtaking, so moves freely.</summary>
        public bool Exempt { get; private set; }
        /// <summary>True while a car behind closes so fast that this car makes no move off its line.</summary>
        public bool HoldingLine { get; private set; }
        /// <summary>Where the car is told to run across the track this step, from the middle, positive left.</summary>
        public float Place { get; private set; }

        // The defending move: 0 none yet, 1 moving in moveDir, 2 coming back to the line.
        private int movePhase;
        public int MovePhase => movePhase;
        private float moveDir;

        /// <summary>
        /// Sets this step's overtake offset, braking and side steering in the pilot. Call before
        /// <see cref="AiPilot.Drive"/> or <see cref="AiPilot.Glide"/>. <c>field</c> may include this car.
        /// </summary>
        public void Think(AiPilot pilot, TrafficCar me, IReadOnlyList<TrafficCar> field, float dt)
        {
            pilot.SpeedCap = float.PositiveInfinity;
            pilot.SpeedScale = 1f;
            pilot.SideFilter = false;
            pilot.SideWeight = 1f;
            pilot.SideSteer = 0f;
            float margin = Endurance.MarginScale(pilot.Safety);
            State = Mode.Racing;
            Braking = false;
            SideSteering = false;
            Defending = false;
            Exempt = false;
            HoldingLine = false;
            if (pilot.Index < 0) { movePhase = 0; return; }
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float here = Mathf.Repeat(me.Distance, lap);
            float mySpeed = me.Speed;
            // Not in the tutorial: a car held back by the one ahead compares with the speed it would
            // go without it, so a car it has slowed to match still counts as slower and is overtaken.
            float wantSpeed = Mathf.Max(mySpeed, pilot.FreeSpeed);

            // filterBColl's terms for this car.
            CarSpec spec = pilot.Spec;
            float mass = spec.Mass;
            float mu = spec.Grip * Mathf.Clamp(pilot.Grip, 0.3f, 1f);
            float ca = 0.5f * CarSpec.AirDensity * spec.LiftArea;   // CA: downforce per speed squared
            float cw = 0.5f * CarSpec.AirDensity * spec.DragArea;   // CW: drag per speed squared
            float cm = mu * G * mass;
            float currentSpeedSqr = mySpeed * mySpeed;

            // Opponent::update for each car, keeping what the filters use.
            const float sideCollDist = CarLength;   // MIN of the two cars' lengths
            float minCatch = float.MaxValue, minSide = float.MaxValue;
            bool hasFront = false, hasSide = false;
            bool fastBehind = false;
            bool behind = false, closeAhead = false, roomLeft = false, roomRight = false;
            float axles = 2f * pilot.Spec.HalfWheelbase;
            TrafficCar front = default, side = default;
            int from = 0, count = field.Count;
            TrafficField sorted = field as TrafficField;
            if (sorted != null && count > 0) from = sorted.From(here - BackCollDist);
            for (int j = 0; j < count; j++)
            {
                int i = sorted != null ? (from + j) % count : j;
                TrafficCar o = field[i];
                if (o.Id == me.Id || o.Ghost) continue;
                float distance = (sorted != null ? sorted.At(i) : Mathf.Repeat(o.Distance, lap)) - here;
                if (distance > lap * 0.5f) distance -= lap;
                else if (distance < -lap * 0.5f) distance += lap;
                if (distance <= -BackCollDist || distance >= FrontCollDist)
                {
                    if (sorted != null && j > 0 && distance >= FrontCollDist) break;   // past the window, in lap order
                    continue;
                }

                // Not in the tutorial: who makes this a defence, and who is alongside.
                if (distance < -sideCollDist && -distance < mySpeed * DefendTime && o.Speed > mySpeed - SpeedPassMargin) behind = true;
                float closing = o.Speed - mySpeed;
                if (distance < -sideCollDist && closing > Endurance.NoDefendClosing && -distance < closing * NoDefendTime) fastBehind = true;
                if (distance > sideCollDist && distance < mySpeed * ExemptTime && o.Speed < wantSpeed) closeAhead = true;
                // Room: a car alongside with its front axle past my rear axle is owed a car's width. A car
                // alongside ahead of me always is; one behind once it is more than its axles' length up.
                // A car off the track is owed nothing.
                if (distance > -axles && distance < CarLength && Mathf.Abs(o.Lateral) < c.Data.width[c.IndexOf(Mathf.Repeat(o.Distance, lap))] / 2f)
                {
                    if (o.Lateral > me.Lateral) roomLeft = true;
                    else roomRight = true;
                }

                if (distance > sideCollDist && o.Speed < wantSpeed)
                {
                    // OPP_FRONT
                    float catchDist = wantSpeed * distance / (wantSpeed - o.Speed);
                    distance -= CarLength;   // MAX of the two cars' lengths
                    distance -= Endurance.LengthMargin * margin;
                    // Not in the tutorial: the car ahead is taken where it will be across the track when
                    // I reach it, at the speed it moves across, as well as where it is now.
                    float reachIn = mySpeed > o.Speed ? Mathf.Min(distance / (mySpeed - o.Speed), PredictFor) : 0f;
                    float later = o.Lateral + o.Across * reachIn;
                    float nearest = Mathf.Clamp(me.Lateral, Mathf.Min(o.Lateral, later), Mathf.Max(o.Lateral, later));
                    float carDist = Mathf.Abs(nearest - me.Lateral) - Mathf.Abs(Width(o) / 2f) - CarWidth / 2f;
                    if (catchDist < minCatch)
                    {
                        minCatch = catchDist;
                        front = o;
                        hasFront = true;
                    }
                    if (carDist < Endurance.WidthMargin * margin)
                    {
                        // OPP_COLL: filterBColl.
                        float allowedSpeedSqr = o.Speed * o.Speed;
                        float brakeDist = mass * (currentSpeedSqr - allowedSpeedSqr) / (2f * (cm + allowedSpeedSqr * (ca * mu + cw)));
                        // Not in the tutorial: TORCS brakes can always lock the tyres, ours can be weaker
                        // than the tyres' grip with downforce, so the brakes' own limit stretches the distance.
                        float brakesOnly = mass * (currentSpeedSqr - allowedSpeedSqr) / (2f * (spec.BrakeDecel * G * mass + allowedSpeedSqr * cw));
                        brakeDist = Mathf.Max(brakeDist, brakesOnly);
                        // Not in the tutorial: only while closing. Inside the margin and already no faster
                        // than the car ahead, the tutorial still brakes, to a stop, until the gap opens.
                        if (brakeDist > distance && currentSpeedSqr > allowedSpeedSqr) Braking = true;
                    }
                }
                else if (distance > -sideCollDist && distance < sideCollDist)
                {
                    // OPP_SIDE
                    float sideDist = o.Lateral - me.Lateral;
                    if (Mathf.Abs(sideDist) < minSide)
                    {
                        minSide = Mathf.Abs(sideDist);
                        side = o;
                        hasSide = true;
                    }
                }
            }

            // getOvertakeOffset. The tutorial's car drives the track middle, and its offset is where
            // it runs across the track. Here the same offset is held across the track, and with no car
            // in front it steps back to the racing line where the tutorial's steps back to the middle.
            float lineHere = line.Lateral[pilot.Index];
            float myOffset = lineHere + pilot.Offset;   // from the track middle
            float step = Endurance.OvertakeOffsetRate * dt;
            // Not in the tutorial: while braking for a car ahead, the offset steps only as fast as the
            // grip the brakes leave over.
            if (Braking)
            {
                float tyres = pilot.TyreGrip(mySpeed);
                float decel = Mathf.Min(spec.BrakeDecel * G, tyres);
                step *= Mathf.Sqrt(Mathf.Max(tyres * tyres - decel * decel, 0f)) / Mathf.Max(tyres, 0.1f);
            }
            if (hasFront)
            {
                State = Mode.Overtaking;
                int ko = c.IndexOf(Mathf.Repeat(front.Distance, lap));
                float w = c.Data.width[ko] / Endurance.OvertakeWidthDiv - BorderOvertakeMargin;
                // Not in the tutorial: the side is picked from where the car ahead will be across the
                // track when I catch it, at the speed it moves across.
                float catchIn = Mathf.Min(minCatch / Mathf.Max(wantSpeed, 1f), CatchPredictFor);
                float otm = front.Lateral + front.Across * catchIn;
                if (otm > 0f && myOffset > -w) myOffset -= step;
                else if (otm < 0f && myOffset < w) myOffset += step;
            }
            else
            {
                if (myOffset > lineHere + step) myOffset -= step;
                else if (myOffset < lineHere - step) myOffset += step;
                else myOffset = lineHere;
            }
            float newOffset = Mathf.Clamp(myOffset - lineHere, AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);

            // Not in the tutorial: racing etiquette. A defending car makes one move off its line, in one
            // direction, then may only come back towards the line. Overtaking a car close ahead, it may
            // move freely.
            Defending = behind;
            Exempt = behind && closeAhead;
            if (!Defending) movePhase = 0;
            else if (!Exempt) newOffset = OneMove(pilot.Offset, newOffset, true);
            // Caught far too fast to defend: no move off the line at all, only back towards it.
            HoldingLine = fastBehind;
            if (HoldingLine)
            {
                float was = pilot.Offset;
                if (was * newOffset < 0f) newOffset = 0f;
                else if (Mathf.Abs(newOffset) > Mathf.Abs(was)) newOffset = was;
            }
            pilot.Offset = newOffset;
            Target = pilot.Offset;

            // filterBColl's full brakes: nothing it may go faster than.
            if (Braking) pilot.SpeedCap = 0f;

            // filterSColl: near a car alongside and heading towards it, blend the steering towards
            // running parallel to it, and take the overtake offset from where the car is now.
            if (hasSide)
            {
                float d = minSide - Width(side);
                float diffAngle = Mathf.DeltaAngle(me.Yaw * Mathf.Rad2Deg, side.Yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (d < Endurance.SideCollisionMargin * margin && diffAngle * (side.Lateral - me.Lateral) < 0f)
                {
                    float cc = Endurance.SideCollisionMargin * margin / 2f;
                    d -= cc;
                    if (d < 0f) d = 0f;
                    int ks = c.IndexOf(Mathf.Repeat(side.Distance, lap));
                    float w = c.Data.width[ks] / WidthDiv - BorderOvertakeMargin;
                    float myToMiddle = Mathf.Clamp(me.Lateral, -w, w);
                    float was = pilot.Offset;
                    pilot.Offset = Mathf.Clamp(myToMiddle - line.Lateral[pilot.Index], AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
                    Target = pilot.Offset;
                    pilot.SideWeight = d / cc;
                    // psteer: TORCS steers positive to the left, the pilot positive to the right, and
                    // the pilot's steering is in radians, so diffangle / steerLock becomes -diffangle.
                    pilot.SideSteer = -diffAngle;
                    // On a straight the car nearer the middle corrects more (2), the other less (1.5).
                    // In a turn the car on the outside corrects less.
                    int turn = TurnAt(c, here);
                    if (turn == 0)
                        pilot.SideFactor = Mathf.Abs(me.Lateral) > Mathf.Abs(side.Lateral) ? 1.5f : 2f;
                    else
                    {
                        float outside = me.Lateral - side.Lateral;
                        float sign = turn > 0 ? 1f : -1f;   // TR_RGT: +1
                        pilot.SideFactor = outside * sign > 0f ? 1.5f : 2f;
                    }
                    pilot.SideFilter = true;
                    SideSteering = true;
                    // The filter's new offset is where the car is: it counts towards the one move.
                    if (Defending && !Exempt) OneMove(was, pilot.Offset, false);
                }
            }

            // Not in the tutorial: room. Leave a car's width between this car and the track edge on the
            // side of a car that is owed room. The racing line and the overtake offset give way, and the
            // pilot's speed follows the lane it is then on, so it lifts or brakes as that lane needs.
            if (roomLeft || roomRight)
            {
                float edge = c.Data.width[c.IndexOf(here)] / 2f - CarWidth / 2f - CarWidth;
                float place = line.Lateral[pilot.Index] + pilot.Offset;
                if (roomLeft) place = Mathf.Min(place, edge);
                if (roomRight) place = Mathf.Max(place, -edge);
                pilot.Offset = Mathf.Clamp(place - line.Lateral[pilot.Index], AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
                Target = pilot.Offset;
            }
            Place = line.Lateral[pilot.Index] + pilot.Offset;
        }

        /// <summary>
        /// The F1 one-move rule on the offset from the racing line: the first move away from the line
        /// may carry on in its direction; once it turns back, the offset may only come back to the line,
        /// never past it. Without <c>hold</c> it only notes the change.
        /// </summary>
        private float OneMove(float was, float now, bool hold)
        {
            float change = now - was;
            if (Mathf.Abs(change) < 1e-5f) return now;
            bool crosses = was != 0f && now * was < 0f;
            bool toLine = Mathf.Abs(now) < Mathf.Abs(was) && !crosses;
            float dir = Mathf.Sign(change);
            if (movePhase == 0)
            {
                if (!toLine) { movePhase = 1; moveDir = dir; }
                return now;
            }
            if (movePhase == 1 && dir == moveDir) return now;
            if (toLine) { movePhase = 2; return now; }
            if (!hold) return now;
            if (crosses) { movePhase = 2; return 0f; }
            return was;
        }

        /// <summary>The bend at a lap distance: +1 a right-hander, -1 a left-hander, 0 on a straight.</summary>
        private static int TurnAt(Circuit c, float here)
        {
            foreach (CornerData k in c.Data.corners)
                foreach (ArcData arc in k.arcs)
                    if (c.Ahead(arc.s, here) <= arc.e - arc.s) return arc.dir;
            return 0;
        }

        /// <summary>
        /// Opponent::getWidth: the room a car takes across the track, its length times the sine and its
        /// width times the cosine of the angle between where it is going and the track.
        /// </summary>
        private static float Width(in TrafficCar o)
        {
            float v = Mathf.Sqrt(o.Speed * o.Speed + o.Across * o.Across);
            float cosa = v > 1e-3f ? Mathf.Clamp(o.Speed / v, -1f, 1f) : 1f;
            float alpha = (float)System.Math.Acos(cosa);
            return CarLength * Mathf.Sin(alpha) + CarWidth * cosa;
        }

        /// <summary>
        /// Whether a car is a ghost this step: only a spun car stopping or turning round on the tarmac
        /// or kerb. Once it starts to rejoin it stays a ghost only until no other car overlaps it, so
        /// it never turns solid inside one.
        /// </summary>
        public static bool Ghosted(bool was, Rejoin rejoin, TrafficCar me, IReadOnlyList<TrafficCar> field, float lap)
        {
            bool spun = rejoin.Cause == "spun" && (rejoin.State == Rejoin.Stage.Stopping || rejoin.State == Rejoin.Stage.Turning);
            if (spun && rejoin.PastEdge <= Rejoin.OffTrackMargin) return true;
            if (!was) return false;
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id) continue;
                float gap = Mathf.Repeat(o.Distance - me.Distance + lap * 0.5f, lap) - lap * 0.5f;
                if (Mathf.Abs(gap) < CarLength + GhostClear && Mathf.Abs(o.Lateral - me.Lateral) < CarLength) return true;
            }
            return false;
        }

        /// <summary>Space a ghost keeps clear of other cars, metres, before it turns solid again.</summary>
        public const float GhostClear = 2f;

        /// <summary>
        /// Share of its normal drag a car feels in the wake of the cars ahead: down to 55% right
        /// behind a car, fading out by 80 m back or a car's width to the side.
        /// </summary>
        public static float Slipstream(Circuit c, TrafficCar me, IReadOnlyList<TrafficCar> field)
        {
            float lap = c.Lap, here = Mathf.Repeat(me.Distance, lap), scale = 1f;
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id || o.Ghost) continue;
                float gap = Mathf.Repeat(Mathf.Repeat(o.Distance, lap) - here, lap);
                float side = Mathf.Abs(o.Lateral - me.Lateral);
                if (gap < WakeFrom || gap > WakeLength || side > 2f) continue;
                scale = Mathf.Min(scale, 1f - WakeDrag * (1f - gap / WakeLength) * (1f - side / 2f));
            }
            return scale;
        }
    }
}
