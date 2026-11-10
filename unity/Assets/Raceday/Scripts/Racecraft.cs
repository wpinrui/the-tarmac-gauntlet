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
        private const float LengthMargin = 2f;             // LENGTH_MARGIN, m
        private const float WidthMargin = 1f;              // WIDTH_MARGIN, m
        private const float SideCollisionMargin = 2f;      // SIDE_COLLISION_MARGIN, m
        private const float BorderOvertakeMargin = 0.5f;   // BORDER_OVERTAKE_MARGIN, m
        private const float WidthDiv = 3f;                 // WIDTH_DIV
        /// <summary>OVERTAKE_OFFSET_INCREMENT, 0.1 m per robot step, TORCS calling robots every 0.02 s: m/s.</summary>
        private const float OvertakeOffsetRate = 0.1f / 0.02f;
        private const float G = 9.81f;

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
            State = Mode.Racing;
            Braking = false;
            SideSteering = false;
            if (pilot.Index < 0) return;
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float here = Mathf.Repeat(me.Distance, lap);
            float mySpeed = me.Speed;

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
            TrafficCar front = default, side = default;
            int from = 0, count = field.Count;
            TrafficField sorted = field as TrafficField;
            if (sorted != null && count > 0) from = sorted.From(here - BackCollDist);
            for (int j = 0; j < count; j++)
            {
                int i = sorted != null ? (from + j) % count : j;
                TrafficCar o = field[i];
                if (o.Id == me.Id) continue;
                float distance = (sorted != null ? sorted.At(i) : Mathf.Repeat(o.Distance, lap)) - here;
                if (distance > lap * 0.5f) distance -= lap;
                else if (distance < -lap * 0.5f) distance += lap;
                if (distance <= -BackCollDist || distance >= FrontCollDist)
                {
                    if (sorted != null && j > 0 && distance >= FrontCollDist) break;   // past the window, in lap order
                    continue;
                }

                if (distance > sideCollDist && o.Speed < mySpeed)
                {
                    // OPP_FRONT
                    float catchDist = mySpeed * distance / (mySpeed - o.Speed);
                    distance -= CarLength;   // MAX of the two cars' lengths
                    distance -= LengthMargin;
                    float carDist = Mathf.Abs(o.Lateral - me.Lateral) - Mathf.Abs(Width(o) / 2f) - CarWidth / 2f;
                    if (catchDist < minCatch)
                    {
                        minCatch = catchDist;
                        front = o;
                        hasFront = true;
                    }
                    if (carDist < WidthMargin)
                    {
                        // OPP_COLL: filterBColl.
                        float allowedSpeedSqr = o.Speed * o.Speed;
                        float brakeDist = mass * (currentSpeedSqr - allowedSpeedSqr) / (2f * (cm + allowedSpeedSqr * (ca * mu + cw)));
                        // Not in the tutorial: TORCS brakes can always lock the tyres, ours can be weaker
                        // than the tyres' grip with downforce, so the brakes' own limit stretches the distance.
                        float brakesOnly = mass * (currentSpeedSqr - allowedSpeedSqr) / (2f * (spec.BrakeDecel * G * mass + allowedSpeedSqr * cw));
                        brakeDist = Mathf.Max(brakeDist, brakesOnly);
                        if (brakeDist > distance) Braking = true;
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
            float step = OvertakeOffsetRate * dt;
            if (hasFront)
            {
                State = Mode.Overtaking;
                int ko = c.IndexOf(Mathf.Repeat(front.Distance, lap));
                float w = c.Data.width[ko] / WidthDiv - BorderOvertakeMargin;
                float otm = front.Lateral;
                if (otm > 0f && myOffset > -w) myOffset -= step;
                else if (otm < 0f && myOffset < w) myOffset += step;
            }
            else
            {
                if (myOffset > lineHere + step) myOffset -= step;
                else if (myOffset < lineHere - step) myOffset += step;
                else myOffset = lineHere;
            }
            pilot.Offset = Mathf.Clamp(myOffset - lineHere, AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
            Target = pilot.Offset;

            // filterBColl's full brakes: nothing it may go faster than.
            if (Braking) pilot.SpeedCap = 0f;

            // filterSColl: near a car alongside and heading towards it, blend the steering towards
            // running parallel to it, and take the overtake offset from where the car is now.
            if (hasSide)
            {
                float d = minSide - Width(side);
                float diffAngle = Mathf.DeltaAngle(me.Yaw * Mathf.Rad2Deg, side.Yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (d < SideCollisionMargin && diffAngle * (side.Lateral - me.Lateral) < 0f)
                {
                    const float cc = SideCollisionMargin / 2f;
                    d -= cc;
                    if (d < 0f) d = 0f;
                    int ks = c.IndexOf(Mathf.Repeat(side.Distance, lap));
                    float w = c.Data.width[ks] / WidthDiv - BorderOvertakeMargin;
                    float myToMiddle = Mathf.Clamp(me.Lateral, -w, w);
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
                }
            }
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
        /// Share of its normal drag a car feels in the wake of the cars ahead: down to 55% right
        /// behind a car, fading out by 80 m back or a car's width to the side.
        /// </summary>
        public static float Slipstream(Circuit c, TrafficCar me, IReadOnlyList<TrafficCar> field)
        {
            float lap = c.Lap, here = Mathf.Repeat(me.Distance, lap), scale = 1f;
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id) continue;
                float gap = Mathf.Repeat(Mathf.Repeat(o.Distance, lap) - here, lap);
                float side = Mathf.Abs(o.Lateral - me.Lateral);
                if (gap < WakeFrom || gap > WakeLength || side > 2f) continue;
                scale = Mathf.Min(scale, 1f - WakeDrag * (1f - gap / WakeLength) * (1f - side / 2f));
            }
            return scale;
        }
    }
}
