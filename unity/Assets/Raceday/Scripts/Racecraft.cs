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
        /// <summary>True while the car is making room for a faster car behind it.</summary>
        public bool GivingWay;
    }

    /// <summary>
    /// The race brain on top of an <see cref="AiPilot"/>, with no Unity objects so it can be tested
    /// outside Unity. Each physics step it looks at the cars around and tells the pilot where to run
    /// relative to its racing line (<see cref="AiPilot.Offset"/>) and how fast it may go
    /// (<see cref="AiPilot.SpeedCap"/>, <see cref="AiPilot.SpeedScale"/>).
    ///
    /// It never runs into a car beside it: a car alongside fences off that side. It holds a gap to the
    /// car ahead in its path. When it is quicker, or the car ahead is a lap down, a slower class or
    /// making room, it picks the side with room (the inside of the next corner if it can) and goes
    /// past. A rival close behind before a corner gets one move to the inside. A car being lapped,
    /// or caught by a faster class, moves aside on the straights and holds its line into corners,
    /// so the faster car knows where it will be.
    /// </summary>
    public sealed class Racecraft
    {
        public enum Mode { Racing, Following, Passing, Defending, GivingWay }

        private const float CarLength = 4.8f;
        /// <summary>Centre to centre when side by side: a car's width plus a safe space.</summary>
        private const float SideBySide = 3f;
        private const float MaxShift = 5f;
        private const float LookAhead = 150f;
        private const float LookBehind = 90f;
        /// <summary>Lap pace ratio that makes another car a different class.</summary>
        private const float ClassGap = 0.05f;
        private const float G = 9.81f;
        private const float WakeLength = 80f;
        private const float WakeDrag = 0.45f;
        /// <summary>Seconds of trying to pass a rival before dropping back in behind it.</summary>
        private const float GiveUp = 12f;
        /// <summary>Extra speed an attacker carries into the braking zone when side by side.</summary>
        private const float Lunge = 1.04f;
        /// <summary>Chance a driver covers the inside when a rival is on its tail into a corner.</summary>
        private const float DefendChance = 0.5f;

        public Mode State { get; private set; }
        /// <summary>The offset racecraft is steering towards, before it is eased in.</summary>
        public float Target { get; private set; }
        /// <summary>Completed passes, and moves made to defend.</summary>
        public int Passes { get; private set; }
        public int Defences { get; private set; }

        private int passing = -1;     // id of the car being passed
        private float passSide;
        private float passBlocked;
        private float passTime;
        private int waitFor = -1;
        private float waitTime;
        private float holdTime;
        private int holdFor = -1;
        private float heldUp;
        private float speed;      // this car's, this step
        private int defendCorner = -1;
        private int rolledCorner = -1;
        private readonly System.Random random;
        private float defendSide;
        private bool defended;

        public Racecraft(int seed = 1)
        {
            random = new System.Random(seed);
        }

        private struct Near
        {
            public TrafficCar Car;
            public float Gap;      // along the track, positive ahead
            public float Shift;    // offset from this car's racing line at its position
            public int Index;      // its track sample
            public bool LapsMe, BeingLapped, Faster, Slower;
        }
        private readonly List<Near> near = new List<Near>();

        /// <summary>
        /// Decides this step's offset and speed limits and writes them into the pilot. Call before
        /// <see cref="AiPilot.Drive"/>. <c>field</c> may include this car.
        /// </summary>
        public void Think(AiPilot pilot, TrafficCar me, IReadOnlyList<TrafficCar> field, float dt)
        {
            pilot.SpeedCap = float.PositiveInfinity;
            pilot.SpeedScale = 1f;
            if (pilot.Index < 0) return;
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float here = Mathf.Repeat(me.Distance, lap);
            float shift = pilot.Shift;
            speed = me.Speed;

            // Who is around, and how they relate to this car.
            near.Clear();
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id) continue;
                float there = Mathf.Repeat(o.Distance, lap);
                float gap = Mathf.Repeat(there - here + lap * 0.5f, lap) - lap * 0.5f;
                if (gap < -LookBehind || gap > LookAhead) continue;
                float race = o.Distance - me.Distance;
                near.Add(new Near
                {
                    Car = o,
                    Gap = gap,
                    Shift = o.Lateral - line.Lateral[c.IndexOf(there)],
                    Index = c.IndexOf(there),
                    LapsMe = gap < 0f && race > lap * 0.5f,
                    BeingLapped = gap > 0f && race < -lap * 0.5f,
                    Faster = o.LapPace < me.LapPace * (1f - ClassGap),
                    Slower = o.LapPace > me.LapPace * (1f + ClassGap),
                });
            }

            // Cars alongside fence off their side, in offsets from the line. The car follows its
            // offset a moment late, and the line can sweep across the track towards an apex, so the
            // fence holds over the next 0.6 s of line, not just here.
            float lo = -MaxShift, hi = MaxShift;
            float mine = line.Lateral[pilot.Index] + shift;
            int sweep = Mathf.CeilToInt(0.6f * speed / c.Step);
            foreach (Near n in near)
            {
                // Alongside, or with its nose just behind this car's tail: no cutting across it.
                if (n.Gap > CarLength + 1.5f || n.Gap < -(CarLength + 6f)) continue;
                // Overlapping, the side is where the cars are. Nose to tail, it is their places
                // relative to the line, which can sweep across the track between them.
                bool overlap = Mathf.Abs(n.Gap) < CarLength * 0.8f;
                float apart = overlap ? n.Car.Lateral - mine : n.Shift - shift;
                if (!overlap && Mathf.Abs(apart) < 1.2f) continue;   // in its wake, not alongside
                for (int d = 0; d <= sweep; d += 2)
                {
                    int k = c.Wrap(pilot.Index + d);
                    float other = d == 0 ? n.Car.Lateral : LateralAt(line, c.Wrap(n.Index + d), n.Shift);
                    if (apart > 0f) hi = Mathf.Min(hi, Mathf.Min(other, n.Car.Lateral) - SideBySide - line.Lateral[k]);
                    else lo = Mathf.Max(lo, Mathf.Max(other, n.Car.Lateral) + SideBySide - line.Lateral[k]);
                }
            }

            int corner = NextCorner(c, here, out float toCorner, out bool inCorner, out float inside, out float cornerLeft);

            // The nearest car ahead in this car's path.
            int blocker = -1;
            for (int i = 0; i < near.Count; i++)
            {
                Near n = near[i];
                if (n.Gap <= 0f) continue;
                bool inPath = Apart(line, n, shift) < SideBySide || Apart(line, n, pilot.Offset) < SideBySide;
                if (inPath && (blocker < 0 || n.Gap < near[blocker].Gap)) blocker = i;
            }

            // A faster car behind that this car should let by.
            int letBy = -1;
            for (int i = 0; i < near.Count; i++)
            {
                Near n = near[i];
                if (n.Gap >= 0f || !(n.LapsMe || n.Faster)) continue;
                bool coming = n.Gap > -40f || n.Car.Speed > me.Speed - 1f;
                if (coming && n.Gap > -70f && (letBy < 0 || n.Gap > near[letBy].Gap)) letBy = i;
            }

            heldUp = pilot.HeldBy > 1f ? heldUp + dt : 0f;
            Mode mode = Mode.Racing;
            float target = 0f;
            waitTime -= dt;
            holdTime -= dt;

            // Passing: keep going on the chosen side until clear, or pick a side when there is reason to go.
            if (passing >= 0)
            {
                int p = Find(passing);
                passTime += dt;
                if (p < 0) passing = -1;
                else if (near[p].Gap < -(CarLength + 2f)) { Passes++; passing = -1; }
                else if (near[p].Gap > 25f && !(near[p].BeingLapped || near[p].Slower)) passing = -1;   // fell back: tuck in again
                else if (near[p].Gap > CarLength + 2f && corner >= 0 && (inCorner || toCorner < 1.2f * me.Speed))
                    passing = -1;   // not alongside by the turn-in: tuck in behind
                else if (near[p].Gap > CarLength * 0.5f && corner >= 0 && (inCorner || toCorner < 0.5f * me.Speed))
                {
                    // Less than half alongside at the turn-in: the corner is the other car's. Stay on
                    // this side, out of its way, until it has gone.
                    holdFor = passing;
                    holdTime = 3f;
                    passing = -1;
                }
                else if (passTime > GiveUp && !(near[p].BeingLapped || near[p].Slower))
                {
                    // Not getting by: drop back in behind and wait a while before trying again.
                    waitFor = passing;
                    holdFor = passing;
                    holdTime = 1.5f;
                    waitTime = 5f;
                    passing = -1;
                }
                // Once alongside, the car being passed has to leave room, so only a car in the way stops it.
                else if ((near[p].Gap > CarLength && !Room(pilot, near[p], passSide)) || !Clear(line, Shift(near[p], passSide), passing, near[p].Gap))
                {
                    passBlocked += dt;
                    if (passBlocked > 0.6f) { holdFor = passing; holdTime = 1.5f; passing = -1; }
                    else { mode = Mode.Passing; target = Shift(near[p], passSide); }
                }
                else { passBlocked = 0f; mode = Mode.Passing; target = Shift(near[p], passSide); }
            }
            if (passing < 0 && blocker >= 0)
            {
                Near b = near[blocker];
                // Worth a go when catching it fast (into a braking zone), or sitting on its tail in a quicker car.
                float closing = me.Speed - b.Car.Speed;
                bool catching = closing > 1f && b.Gap < 40f && b.Gap < 2f * closing;
                // Moving over takes a second or two, so there must be that much straight left.
                float needed = b.Gap + 40f + 1.5f * me.Speed;
                bool space = inCorner ? cornerLeft < 40f : corner < 0 || toCorner > needed;
                // Or held up: it would go faster than the car ahead lets it. A rival is attacked at the
                // end of a straight, out of its slipstream, rather than as soon as the corner opens out.
                bool towing = pilot.Towed && b.Gap < 12f && me.LapPace < b.Car.LapPace * 1.002f;
                bool quicker = b.Gap < 12f && (heldUp > 0.5f || towing) && !inCorner && (corner < 0 || toCorner < needed + 2f * me.Speed);
                bool reason = space && (b.BeingLapped || b.Slower || b.Car.GivingWay || catching || quicker);
                if (waitTime > 0f && waitFor == b.Car.Id && !(b.BeingLapped || b.Slower)) reason = false;
                if (reason && b.Gap < 80f)
                {
                    float best = float.NegativeInfinity;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (!Room(pilot, b, side)) continue;
                        float cand = Shift(b, side);
                        if (!Clear(line, cand, b.Car.Id, b.Gap)) continue;
                        float score = -0.3f * Mathf.Abs(cand);
                        if (corner >= 0 && toCorner < 300f && side == inside) score += 2f;
                        // Round the outside of a corner is slow and rarely sticks, unless the car ahead
                        // has left the racing line to cover the inside.
                        bool covering = corner >= 0 && b.Shift * inside > 1f;
                        if (corner >= 0 && (inCorner || toCorner < 150f) && side != inside && !covering) continue;
                        if (covering && side == inside) continue;   // the door is shut
                        if (b.Car.GivingWay && side * b.Shift < 0f) score += 2f;
                        if (score > best) { best = score; passSide = side; }
                    }
                    if (best > float.NegativeInfinity)
                    {
                        passing = b.Car.Id;
                        passBlocked = 0f;
                        passTime = 0f;
                        mode = Mode.Passing;
                        target = Shift(b, passSide);
                    }
                }
                if (mode != Mode.Passing) mode = Mode.Following;
            }

            // Letting a faster car by: aside on the straights, on the line into corners.
            if (letBy >= 0 && mode != Mode.Passing)
            {
                Near f = near[letBy];
                mode = Mode.GivingWay;
                bool straight = !inCorner && (corner < 0 || toCorner > 150f);
                if (straight)
                {
                    float away = f.Shift > 0.5f ? -1f : f.Shift < -0.5f ? 1f : (corner >= 0 ? -inside : 1f);
                    target = away * 3f;
                    if (f.Gap > -25f) pilot.SpeedScale = 0.96f;
                }
                else target = 0f;
            }

            // Defending: one move to the inside before a corner at the end of a straight, against a
            // rival close behind. Not every time: covering costs time, and a driver who always does
            // it can never be passed.
            if (corner != rolledCorner) { defendCorner = -1; defended = false; rolledCorner = corner; }
            if (mode == Mode.Racing && corner >= 0 && !inCorner && toCorner < 250f && toCorner > 40f
                && StraightBefore(c, corner) > 300f)
            {
                foreach (Near n in near)
                {
                    // A rival on this car's tail and not yet alongside.
                    if (n.Gap >= -(CarLength + 1f) || n.Gap < -15f || n.LapsMe || n.Faster || n.BeingLapped || n.Slower) continue;
                    if (n.Car.Speed < me.Speed + 0.5f && n.Gap < -10f) continue;
                    // Too late once it has committed to the inside: no moving into it.
                    if ((n.Car.Lateral - (line.Lateral[pilot.Index] + shift)) * inside > 1f) continue;
                    if (!defended)
                    {
                        defended = true;
                        if (random.NextDouble() < DefendChance)
                        {
                            defendCorner = corner;
                            defendSide = inside;
                            Defences++;
                        }
                    }
                    break;
                }
                if (defendCorner == corner)
                {
                    mode = Mode.Defending;
                    target = defendSide * 2.5f;
                }
            }

            if (holdTime > 0f && mode != Mode.Passing)
            {
                // In a corner only while the other car is still beside or just ahead.
                int h = Find(holdFor);
                bool overlapping = h >= 0 && near[h].Gap > -2f && near[h].Gap < CarLength + 3f;
                if (!inCorner || overlapping) target = pilot.Offset;
                else holdTime = 0f;
            }

            // Lunge: side by side or nearly into a braking zone, the attacker brakes a little later.
            if (mode == Mode.Passing && !inCorner && corner >= 0 && toCorner < 200f)
            {
                int p = Find(passing);
                if (p >= 0 && near[p].Gap < CarLength + 4f && !near[p].BeingLapped && !near[p].Slower) pilot.SpeedScale = Lunge;
            }

            // Ease the offset towards the target, more gently while cornering hard, never into a car alongside.
            // Boxed in on both sides: stay put rather than lurch for a gap that isn't there.
            float goal = lo <= hi ? Mathf.Clamp(target, lo, hi) : pilot.Offset;
            float load = Mathf.Abs(me.Speed * me.Speed * line.Curvature[pilot.Index]) / (pilot.Spec.Grip * G);
            float spare = 1f - Mathf.Clamp01(load);
            float rate = 0.3f + 2.2f * spare * spare;
            // Back towards the line is always worth doing at a fair pace: off it, corners are slower.
            if (Mathf.Abs(goal) < Mathf.Abs(pilot.Offset)) rate = Mathf.Max(rate, 2f);
            pilot.Offset = Mathf.MoveTowards(pilot.Offset, goal, rate * dt);
            // A car alongside is a firm limit: the offset is pushed off it quickly, though not in one
            // jump, which would throw the car sideways.
            if (lo <= hi) pilot.Offset = Mathf.MoveTowards(pilot.Offset, Mathf.Clamp(pilot.Offset, lo, hi), 6f * dt);
            Target = goal;

            // Hold a gap to whatever is ahead in the path: now, or where the car is moving to.
            // Safe if the car ahead stopped as hard as it can: this car stops in its own braking
            // distance, which must fit in the gap plus the other car's (Gipps' rule).
            float brake = me.Braking;
            foreach (Near n in near)
            {
                if (n.Gap <= 0f) continue;
                // Overlapping side by side: the other car has to leave room, so only the space now counts.
                bool alongside = n.Gap < CarLength + 1f && Mathf.Abs(n.Car.Lateral - mine) > SideBySide - 0.6f;
                bool inPath = !alongside && (Apart(line, n, shift) < SideBySide - 0.2f
                    || (n.Gap < 30f && Apart(line, n, pilot.Offset) < SideBySide - 0.2f));
                if (!inPath) continue;
                // Closer when attacking: tucked right in on a straight, in its slipstream, ready to pull out.
                bool attacking = mode == Mode.Passing || me.LapPace < n.Car.LapPace * 0.995f || n.BeingLapped || n.Slower;
                bool straight = !inCorner && (corner < 0 || toCorner > 2f * me.Speed);
                float wanted = CarLength + 1f + (attacking ? (straight ? 0.03f : 0.08f) : 0.2f) * me.Speed;
                float room = n.Gap - wanted;
                float ratio = Mathf.Min(brake / Mathf.Max(n.Car.Braking, 1f), 1f);
                float cap = room > 0f
                    ? Mathf.Sqrt(n.Car.Speed * n.Car.Speed * ratio + 2f * brake * room)
                    : Mathf.Max(n.Car.Speed * Mathf.Sqrt(ratio) + 2f * room, 0f);
                pilot.SpeedCap = Mathf.Min(pilot.SpeedCap, cap);
            }
            State = mode;
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
                if (gap < CarLength || gap > WakeLength || side > 2f) continue;
                scale = Mathf.Min(scale, 1f - WakeDrag * (1f - gap / WakeLength) * (1f - side / 2f));
            }
            return scale;
        }

        private int Find(int id)
        {
            for (int i = 0; i < near.Count; i++) if (near[i].Car.Id == id) return i;
            return -1;
        }

        /// <summary>Offset that puts this car beside another, on one side.</summary>
        private static float Shift(Near n, float side) => Mathf.Clamp(n.Shift + side * (SideBySide + 0.4f), -MaxShift, MaxShift);

        /// <summary>
        /// Whether there is a car's width of track beside another car on one side, from just behind it
        /// to 30 m past it, assuming it holds its offset from the line. Further on, a car that gets
        /// alongside makes its own room: the other car may not turn in on it.
        /// </summary>
        private static bool Room(AiPilot pilot, Near n, float side)
        {
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float there = Mathf.Repeat(n.Car.Distance, c.Lap);
            for (float d = -10f; d <= 30f; d += 10f)
            {
                int k = c.IndexOf(there + d);
                float edge = c.HalfWidth(k) - RacingLine.EdgeMargin + 0.2f;
                float other = d <= 0f ? n.Car.Lateral : LateralAt(line, k, n.Shift);
                float mine = Mathf.Clamp(other + side * SideBySide, -edge, edge);
                if ((mine - other) * side < SideBySide - 0.3f) return false;
            }
            return true;
        }

        /// <summary>Where a car holding an offset from the line sits at a track sample, kept on the track.</summary>
        private static float LateralAt(RacingLine line, int k, float offset)
        {
            float edge = line.Circuit.HalfWidth(k) - RacingLine.EdgeMargin;
            return Mathf.Clamp(line.Lateral[k] + offset, -edge, edge);
        }

        /// <summary>
        /// Least sideways space between another car and this car's path at an offset, from where the
        /// other car is to where this car would reach it in a second and a half: lines that look
        /// apart can meet at an apex.
        /// </summary>
        private float Apart(RacingLine line, Near n, float offset)
        {
            Circuit c = line.Circuit;
            float least = Mathf.Abs(LateralAt(line, n.Index, offset) - n.Car.Lateral);
            int reach = Mathf.Max(25, Mathf.CeilToInt(1.5f * speed / c.Step));
            for (int d = 5; d <= reach; d += 5)
            {
                int k = c.Wrap(n.Index + d);
                least = Mathf.Min(least, Mathf.Abs(LateralAt(line, k, offset) - LateralAt(line, k, n.Shift)));
            }
            return least;
        }

        /// <summary>No other car sits in that lane between just behind this car and 40 m past the one being passed.</summary>
        private bool Clear(RacingLine line, float shift, int passing, float upTo)
        {
            foreach (Near n in near)
            {
                if (n.Car.Id == passing || n.Gap < -(CarLength + 2f) || n.Gap > upTo + 40f) continue;
                if (Apart(line, n, shift) < SideBySide) return false;
            }
            return true;
        }

        /// <summary>Length of the straight leading into a corner, from the end of the one before it.</summary>
        private static float StraightBefore(Circuit c, int corner)
        {
            CornerData[] corners = c.Data.corners;
            CornerData before = corners[(corner - 1 + corners.Length) % corners.Length];
            return c.Ahead(before.e, corners[corner].s);
        }

        /// <summary>
        /// The corner this car is in, or the next one ahead: its index, the distance to its start (0
        /// when inside it), and the side of its inside as a lateral sign (+1 left).
        /// </summary>
        private static int NextCorner(Circuit c, float here, out float toCorner, out bool inCorner, out float inside, out float cornerLeft)
        {
            CornerData[] corners = c.Data.corners;
            int best = -1;
            toCorner = float.MaxValue;
            inCorner = false;
            inside = 0f;
            cornerLeft = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                CornerData k = corners[i];
                float into = c.Ahead(k.s, here);
                if (into <= k.e - k.s)
                {
                    // Inside the corner: the inside is the bend being driven now.
                    foreach (ArcData arc in k.arcs)
                        if (c.Ahead(arc.s, here) <= arc.e - arc.s) inside = -arc.dir;
                    if (inside == 0f) inside = -k.arcs[0].dir;
                    toCorner = 0f;
                    cornerLeft = k.e - k.s - into;
                    inCorner = true;
                    return i;
                }
                float ahead = c.Ahead(here, k.s);
                if (ahead < toCorner) { toCorner = ahead; best = i; inside = -k.arcs[0].dir; }
            }
            return best;
        }
    }
}
