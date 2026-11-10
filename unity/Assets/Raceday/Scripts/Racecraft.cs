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
        /// <summary>The car's race brain, for where it plans to be over the next few seconds. Null for a player.</summary>
        public Racecraft Craft;
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
    /// outside Unity. Each physics step it tells the pilot where to run relative to its racing line
    /// (<see cref="AiPilot.Offset"/>) and how fast it may go (<see cref="AiPilot.SpeedCap"/>,
    /// <see cref="AiPilot.SpeedScale"/>).
    ///
    /// It plans rather than reacts. A few times a second, when a car ahead would hold it up, it lays
    /// out candidate paths over the next corners: the racing line, a dive up the inside, the same with
    /// later braking, round the outside (keeping the inside for the next corner when it bends the
    /// other way), a switchback that runs wide in and cuts back under on the exit, and a pass on the
    /// straight. Each is driven forward in time at the speeds its path allows, against where every
    /// other car plans to be at each moment, not where it is now. A path is only blocked by a car
    /// that will be in the way when this car gets there. The path that gets furthest wins. A pass
    /// that stops paying off for a while is dropped, which frees the space for the car behind.
    ///
    /// Underneath, it never runs into a car beside it (a car alongside fences off that side) and
    /// holds a safe gap to the car ahead in its path. A car being lapped holds its racing line, so the
    /// faster car can plan round it. A rival close behind before a corner at the end of a long
    /// straight sometimes gets a move to the inside.
    /// </summary>
    public sealed class Racecraft
    {
        public enum Mode { Racing, Following, Passing, Defending, GivingWay }

        private enum Kind { Line, Inside, LateBrake, Outside, Switchback, Straight }

        private const float CarLength = 4.8f;
        /// <summary>Centre to centre when side by side: a car's width plus a safe space.</summary>
        private const float SideBySide = 3f;
        /// <summary>Centre to centre at which two paths pass without touching: a car's width and a little.</summary>
        private const float Clearance = 2.4f;
        /// <summary>Offset that puts a car beside another.</summary>
        private const float Beside = SideBySide + 0.4f;
        private const float MaxShift = 5f;
        private const float LookAhead = 150f;
        private const float LookBehind = 90f;
        /// <summary>Lap pace ratio that makes another car a different class.</summary>
        private const float ClassGap = 0.05f;
        private const float G = 9.81f;
        private const float PhysicsStep = 0.01f;
        private const float WakeLength = 80f;
        private const float WakeDrag = 0.45f;
        /// <summary>Extra speed an attacker carries into the braking zone on a late-braking dive.</summary>
        private const float Lunge = 1.04f;
        /// <summary>Chance a driver covers the inside when a rival is on its tail into a corner.</summary>
        private const float DefendChance = 0.5f;

        /// <summary>How often the paths are planned again, seconds.</summary>
        private const float PlanEvery = 0.2f;
        /// <summary>Planning looks this far ahead in time, in steps of <see cref="Tick"/>.</summary>
        private const int Steps = 40;
        private const float Tick = 0.1f;
        /// <summary>Metres further on, over the planning time, that make a path worth leaving the line for.</summary>
        private const float WorthIt = 1f;
        /// <summary>Metres a path is credited with for winning the place from the car ahead.</summary>
        private const float PassWorth = 15f;
        /// <summary>Distance a pass may go on losing to the line before it is dropped, metres.</summary>
        private const float GiveUpAfter = 60f;

        public Mode State { get; private set; }
        /// <summary>The offset racecraft is steering towards, before it is eased in.</summary>
        public float Target { get; private set; }
        /// <summary>Completed passes, and moves made to defend.</summary>
        public int Passes { get; private set; }
        public int Defences { get; private set; }
        /// <summary>The kind of path being followed, for traces.</summary>
        public string Plan => plan.Kind.ToString();

        // Where this car plans to be, each Tick from when the plan was made: race distance and lateral.
        private readonly float[] planDistance = new float[Steps + 1];
        private readonly float[] planLateral = new float[Steps + 1];
        private float planAge = float.PositiveInfinity;

        private sealed class Path
        {
            public Kind Kind;
            public int Corner = -1;
            public int Start;              // line sample the path starts at
            public float[] Offset;         // per sample from Start
            public float[] Speed;          // per sample from Start, braking in time for what is ahead
            public float Score;            // metres covered over the planning time
            public bool Blocked;           // held up by a car ahead in the planning
            public bool Rejected;          // runs into a car beside it
            public bool WinsPlace;         // level with or ahead of the car being passed where it has to give way
            public readonly List<int> HeldBy = new List<int>();   // cars that hold it up in the planning
            public readonly float[] Distance = new float[Steps + 1];
            public readonly float[] Lateral = new float[Steps + 1];
        }
        private Path plan = new Path { Kind = Kind.Line };
        private readonly List<Path> candidates = new List<Path>();
        private int passTarget = -1;
        private float losing;              // metres the pass has been losing to the line for
        private int bannedCorner = -1;     // corner a pass was given up at: no new pass into it
        private float speed;               // this car's, this step
        private float heldUp;              // how long the car ahead has been holding this one up, seconds
        private bool committed;            // having a go at an evenly matched car
        private int defendCorner = -1;
        private int rolledCorner = -1;
        private readonly System.Random random;
        private float defendSide;
        private bool defended;

        public Racecraft(int seed = 1)
        {
            random = new System.Random(seed);
        }

        /// <summary>
        /// For a car not racing right now (formation, pit lane): it has no plan, so the others expect
        /// it to carry on as it is going.
        /// </summary>
        public void Forget()
        {
            planAge = float.PositiveInfinity;
            plan = new Path { Kind = Kind.Line };
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
        // Where each near car will be at each Tick of the planning, the same for every candidate path.
        private float[] predGap = new float[0], predLat = new float[0];
        // The near cars that get ahead of this car's nose at some point in the planning: the rest stay
        // behind it whatever path it takes, so they can't hold it up, tow it or be run into.
        private readonly List<int> inReach = new List<int>();
        private float[] gapAt = new float[0], latAt = new float[0];
        private bool[] yields = new bool[32];        // per car in reach, in the planning: it has to leave room

        /// <summary>
        /// Decides this step's offset and speed limits and writes them into the pilot. Call before
        /// <see cref="AiPilot.Drive"/>. <c>field</c> may include this car.
        /// </summary>
        public void Think(AiPilot pilot, TrafficCar me, IReadOnlyList<TrafficCar> field, float dt)
        {
            pilot.SpeedCap = float.PositiveInfinity;
            pilot.SpeedScale = 1f;
            planAge += dt;
            if (pilot.Index < 0) { planAge = float.PositiveInfinity; plan = new Path { Kind = Kind.Line }; return; }
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float here = Mathf.Repeat(me.Distance, lap);
            float shift = pilot.Shift;
            speed = me.Speed;
            heldUp = pilot.HeldBy > 1f ? heldUp + dt : 0f;

            // Who is around, and how they relate to this car.
            near.Clear();
            if (field is TrafficField sorted)
            {
                // Only the cars from LookBehind behind to LookAhead ahead, in lap order.
                int start = sorted.From(here - LookBehind);
                for (int j = 0; j < sorted.Count; j++)
                {
                    int i = (start + j) % sorted.Count;
                    if (!Consider(sorted[i], sorted.At(i), me, line, here) && sorted[i].Id != me.Id) break;
                }
            }
            else
                for (int i = 0; i < field.Count; i++) Consider(field[i], Mathf.Repeat(field[i].Distance, lap), me, line, here);

            // A much faster car behind, or one lapping this car: hold the racing line, so it knows
            // where this car will be and can pick its own way past.
            bool givingWay = false;
            foreach (Near n in near)
                if (n.Gap < 0f && n.Gap > -70f && (n.LapsMe || n.Faster)) givingWay = true;

            // Cars alongside fence off their side, in offsets from the line. The car follows its
            // offset a moment late, and the line can sweep across the track towards an apex, so the
            // fence holds over the next 0.6 s of line, not just here.
            float lo = AiPilot.LaneShifts[0], hi = AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1];
            bool beside = false;
            float mine = line.Lateral[pilot.Index] + shift;
            int sweep = Mathf.CeilToInt(0.6f * speed / c.Step);
            foreach (Near n in near)
            {
                // Alongside, or with its nose just behind this car's tail: no cutting across it.
                if (n.Gap > CarLength + 1.5f || n.Gap < -(CarLength + 6f)) continue;
                // Overlapping, the side is where the cars are. Nose to tail, it is their places
                // relative to the line, which can sweep across the track between them.
                bool overlap = Mathf.Abs(n.Gap) < CarLength * 0.8f;
                if (overlap) beside = true;
                float apart = overlap ? n.Car.Lateral - mine : n.Shift - shift;
                if (!overlap && Mathf.Abs(apart) < 1.2f) continue;   // in its wake, not alongside
                // Making room for a car coming from behind: it plans round this car, so it doesn't push.
                if (givingWay && !overlap && n.Gap < 0f) continue;
                for (int d = 0; d <= sweep; d += 2)
                {
                    int k = c.Wrap(pilot.Index + d);
                    float other = d == 0 ? n.Car.Lateral : LateralAt(line, c.Wrap(n.Index + d), n.Shift);
                    if (apart > 0f) hi = Mathf.Min(hi, Mathf.Min(other, n.Car.Lateral) - SideBySide - line.Lateral[k]);
                    else lo = Mathf.Max(lo, Mathf.Max(other, n.Car.Lateral) + SideBySide - line.Lateral[k]);
                }
            }

            int corner = NextCorner(c, here, out float toCorner, out bool inCorner, out float inside, out float cornerLeft);


            // Defending: one move to the inside before a corner at the end of a straight, against a
            // rival close behind. Not every time: covering costs time, and a driver who always does
            // it can never be passed.
            bool defending = false;
            if (corner != rolledCorner) { defendCorner = -1; defended = false; rolledCorner = corner; }
            if (!givingWay && corner >= 0 && !inCorner && toCorner < 250f && toCorner > 40f
                && StraightBefore(c, corner) > 300f)
            {
                foreach (Near n in near)
                {
                    // A rival on this car's tail and not yet alongside.
                    if (n.Gap >= -(CarLength + 1f) || n.Gap < -15f || n.LapsMe || n.Faster || n.BeingLapped || n.Slower) continue;
                    if (n.Car.Speed < me.Speed + 0.5f && n.Gap < -10f) continue;
                    // Too late once it has committed to the inside: no moving into it.
                    if ((n.Car.Lateral - mine) * inside > 1f) continue;
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
                defending = defendCorner == corner;
            }

            // Plan: a few times a second, or straight away when there is no plan or it has run out.
            int along = c.Wrap(pilot.Index - plan.Start);
            if (planAge >= PlanEvery || plan.Offset == null || along >= plan.Offset.Length - 10)
            {
                Replan(pilot, me, c, here, corner, toCorner, inCorner, inside, givingWay, defending);
                along = c.Wrap(pilot.Index - plan.Start);
            }

            Mode mode = plan.Kind != Kind.Line ? Mode.Passing
                : defending ? Mode.Defending
                : givingWay ? Mode.GivingWay
                : plan.Blocked ? Mode.Following
                : Mode.Racing;
            float target = along < plan.Offset.Length ? plan.Offset[along] : 0f;
            // Off the line, the path's own speeds where it brakes for its corners: an inside line is slower.
            if (plan.Kind != Kind.Line && along < plan.Speed.Length)
            {
                int ahead = Mathf.Min(along + Mathf.CeilToInt(Mathf.Max(speed, 5f) * 0.1f / c.Step), plan.Speed.Length - 1);
                if (plan.Speed[ahead] < plan.Speed[along]) pilot.SpeedCap = plan.Speed[ahead];
            }

            // Ease the offset towards the target, more gently while cornering hard, never into a car alongside.
            // Boxed in on both sides: stay put rather than lurch for a gap that isn't there.
            float goal = lo <= hi ? Mathf.Clamp(target, lo, hi) : pilot.Offset;
            float load = Mathf.Abs(me.Speed * me.Speed * line.Curvature[pilot.Index]) / (pilot.Spec.Grip * G);
            float spare = 1f - Mathf.Clamp01(load);
            // Keeping up with the plan, but only as fast as the grip left over from cornering allows.
            float rate = Mathf.Max(0.3f + 2.2f * spare * spare, PlanRate(c, along) * spare * spare);
            // Back towards the line is worth doing at a fair pace on the straights: off it, corners are slower.
            if (Mathf.Abs(goal) < Mathf.Abs(pilot.Offset)) rate = Mathf.Max(rate, 2f * spare);
            pilot.Offset = Mathf.MoveTowards(pilot.Offset, goal, rate * dt);
            // A car alongside is a firm limit: the offset is pushed off it quickly, though not in one
            // jump, which would throw the car sideways. A car only nose to tail gets more gentle
            // room, within the grip the corner leaves, or the swerve spins the car.
            if (lo <= hi)
            {
                // The line moves across the track this step: pushing the offset against that motion
                // only holds the car where it is, so that part of the push is free.
                float drift = (line.Lateral[c.Wrap(pilot.Index + 1)] - line.Lateral[pilot.Index]) / c.Step * speed * dt;
                float want = Mathf.Clamp(pilot.Offset, lo, hi) - pilot.Offset;
                float free = want * drift < 0f ? Mathf.Min(Mathf.Abs(drift), Mathf.Abs(want)) : 0f;
                pilot.Offset = Mathf.MoveTowards(pilot.Offset, pilot.Offset + want, (beside ? 6f : 4f * (0.25f + 0.75f * spare)) * dt + free);
            }
            Target = goal;

            // Squeezed off the line by a car alongside: brake for that lane's corners, which can come
            // sooner and slower than the line's.
            if (plan.Kind == Kind.Line && goal != target && Mathf.Abs(pilot.Offset) > 1f)
            {
                float stop = me.Speed * me.Speed / (2f * me.Braking);
                for (int d = 0; d * c.Step < stop + 20f; d++)
                {
                    float v = pilot.LaneSpeed(c.Wrap(pilot.Index + d), pilot.Offset);
                    pilot.SpeedCap = Mathf.Min(pilot.SpeedCap, Mathf.Sqrt(v * v + 2f * me.Braking * d * c.Step));
                }
            }

            // Hold a gap to whatever is ahead in the path: now, or where the car is moving to.
            // Safe if the car ahead stopped as hard as it can: this car stops in its own braking
            // distance, which must fit in the gap plus the other car's (Gipps' rule).
            float brake = me.Braking;
            foreach (Near n in near)
            {
                if (n.Gap <= 0f) continue;
                // Overlapping side by side: the other car has to leave room, so only the space now counts.
                bool alongside = n.Gap < CarLength + 1f && Mathf.Abs(n.Car.Lateral - mine) > SideBySide - 0.6f;
                // In the path now, where the car is heading, or where the plan says the two will meet.
                // A car committed to a pass only needs its paths to clear the other car.
                float margin = mode == Mode.Passing ? Clearance : SideBySide - 0.2f;
                // A car crawling after a spin goes anywhere: give it a wide berth.
                bool crawling = n.Car.Speed < 20f && n.Car.Speed < 0.5f * me.Speed;
                if (crawling) margin = SideBySide + 1f;
                bool inPath = !alongside && (Apart(line, n, shift) < margin
                    || (n.Gap < 30f && Apart(line, n, pilot.Offset) < margin)
                    || plan.HeldBy.Contains(n.Car.Id));
                if (!inPath) continue;
                // Closer when attacking: tucked right in on a straight, in its slipstream, ready to pull out.
                bool attacking = mode == Mode.Passing || me.LapPace < n.Car.LapPace * 0.995f || n.BeingLapped || n.Slower;
                bool straight = !inCorner && (corner < 0 || toCorner > 2f * me.Speed);
                float wanted = CarLength + 1f + (attacking ? (straight ? 0.03f : 0.08f) : 0.2f) * me.Speed;
                // A gliding car looks only every few physics steps, so it keeps back the extra ground it
                // could close before it looks again, with the same again to spare.
                float room = n.Gap - wanted - 2f * Mathf.Max(dt - PhysicsStep, 0f) * Mathf.Max(me.Speed - n.Car.Speed, 0f);
                float ratio = Mathf.Min(brake / Mathf.Max(n.Car.Braking, 1f), 1f);
                float cap = room > 0f
                    ? Mathf.Sqrt(n.Car.Speed * n.Car.Speed * ratio + 2f * brake * room)
                    : Mathf.Max(n.Car.Speed * Mathf.Sqrt(ratio) + 2f * room, 0f);
                // Too close, but pulling away (a car that has just passed): lift, don't stamp on the
                // brakes, which mid-corner would run this car wide.
                if (room <= 0f && n.Car.Speed > me.Speed) cap = Mathf.Max(cap, me.Speed - 0.5f);
                pilot.SpeedCap = Mathf.Min(pilot.SpeedCap, cap);
            }

            // A late-braking dive carries a little more speed into the braking zone.
            if (plan.Kind == Kind.LateBrake && !inCorner && corner == plan.Corner && toCorner < 150f) pilot.SpeedScale = Lunge;

            // A pass that has got by.
            if (passTarget >= 0)
            {
                int p = Find(passTarget);
                if (p < 0) passTarget = -1;
                else if (near[p].Gap < -(CarLength + 2f)) { Passes++; passTarget = -1; }
            }
            State = mode;
        }

        /// <summary>
        /// Lays out the candidate paths, drives each forward against where the other cars will be, and
        /// takes the one that gets furthest. Then publishes where this car will be.
        /// </summary>
        private void Replan(AiPilot pilot, TrafficCar me, Circuit c, float here, int corner, float toCorner,
            bool inCorner, float inside, bool givingWay, bool defending)
        {
            planAge = 0f;
            plans++;
            RacingLine line = pilot.Line;
            float ramp = Mathf.Clamp(1.2f * speed, 25f, 60f);
            float horizon = Mathf.Max(250f, speed * Steps * Tick + 100f);
            int start = pilot.Index;
            int samples = Mathf.CeilToInt(horizon / c.Step);

            // The corner ahead (or this one), from here: where it starts and ends, and the one after.
            float a = 0f, b = 0f, a2 = 0f, b2 = 0f, inside2 = 0f;
            if (corner >= 0)
            {
                CornerData k = c.Data.corners[corner];
                a = inCorner ? -c.Ahead(k.s, here) : toCorner;
                b = a + (k.e - k.s);
                CornerData k2 = c.Data.corners[(corner + 1) % c.Data.corners.Length];
                a2 = b + c.Ahead(k.e, k2.s);
                b2 = a2 + (k2.e - k2.s);
                inside2 = -k2.arcs[0].dir;
            }

            // Where the near cars will be at each Tick, worked out once for all the candidate paths.
            int cells = near.Count * (Steps + 1);
            if (predGap.Length < cells) { predGap = new float[cells]; predLat = new float[cells]; }
            for (int i = 0; i < near.Count; i++)
            {
                Near o = near[i];
                Racecraft r = o.Car.Craft;
                if (r == null || float.IsInfinity(r.planAge))
                {
                    for (int step = 0; step <= Steps; step++)
                    {
                        predGap[i * (Steps + 1) + step] = o.Gap + Mathf.Max(o.Car.Speed, 0f) * step * Tick;
                        predLat[i * (Steps + 1) + step] = o.Car.Lateral;
                    }
                    continue;
                }
                r.Foresee(o.Car);
                for (int step = 0; step <= Steps; step++)
                {
                    predGap[i * (Steps + 1) + step] = o.Gap + r.seenGain[step];
                    predLat[i * (Steps + 1) + step] = r.seenLat[step];
                }
            }
            inReach.Clear();
            for (int i = 0; i < near.Count; i++)
                for (int step = 0; step <= Steps; step++)
                    if (predGap[i * (Steps + 1) + step] > -CarLength * 0.6f) { inReach.Add(i); break; }
            // Step by step, side by side in memory, for the passes over every candidate path.
            int count = inReach.Count;
            if (gapAt.Length < count * (Steps + 1)) { gapAt = new float[count * (Steps + 1)]; latAt = new float[count * (Steps + 1)]; }
            for (int x = 0; x < count; x++)
                for (int step = 0; step <= Steps; step++)
                {
                    gapAt[step * count + x] = predGap[inReach[x] * (Steps + 1) + step];
                    latAt[step * count + x] = predLat[inReach[x] * (Steps + 1) + step];
                }

            candidates.Clear();
            float now = pilot.Offset;
            Path baseline = defending
                ? Make(Kind.Line, start, samples, c, now, ramp, defendSide * 2.5f, Mathf.Max(a - 20f, ramp), a + 10f)
                : Make(Kind.Line, start, samples, c, now, ramp, 0f);
            candidates.Add(baseline);
            Drive(baseline, pilot, me, c, -1f);

            // Only worth looking for a way past when the line is going to be held up, and never while
            // covering a move. A car making room for one behind may still pass a slower one ahead:
            // the car behind plans round where it is going.
            int target = baseline.Blocked ? BlockerAhead(line, c, pilot.Shift) : -1;
            if (givingWay && target >= 0 && !(near[target].Slower || near[target].BeingLapped)) target = -1;
            if (target >= 0 && !defending && corner >= 0 && corner != bannedCorner)
            {
                Near t = near[target];
                float inner = Mathf.Clamp(t.Shift + inside * Beside, -MaxShift, MaxShift);
                float outer = Mathf.Clamp(t.Shift - inside * Beside, -MaxShift, MaxShift);
                // The inside of this corner, held to its exit.
                candidates.Add(Make(Kind.Inside, start, samples, c, now, ramp, inner, 0f, b + 20f, corner));
                // The same with later braking, while there is a braking zone still to come.
                if (a > ramp) candidates.Add(Make(Kind.LateBrake, start, samples, c, now, ramp, inner, 0f, b + 20f, corner));
                // Round the outside: through the next corner too when it bends the other way, which
                // makes the outside of this one the inside of that.
                bool flip = inside2 == -inside && a2 - b < 150f;
                candidates.Add(Make(Kind.Outside, start, samples, c, now, ramp, outer, 0f, flip ? b2 + 20f : b + 20f, corner));
                // Switchback: wide on the way in, then cut back under on the way out.
                if (a > ramp)
                {
                    float apex = a + 0.4f * (b - a);
                    Path s = Make(Kind.Switchback, start, samples, c, now, ramp, outer, 0f, apex, corner);
                    Shape(s, c, apex, outer, b + 10f, inner, b + 60f, inner, b + 60f + ramp, 0f);
                    candidates.Add(s);
                }
                // Past on the straight, back in before the corner.
                if (a > ramp + 80f)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float off = Mathf.Clamp(t.Shift + side * Beside, -MaxShift, MaxShift);
                        candidates.Add(Make(Kind.Straight, start, samples, c, now, ramp, off, 0f, a - 30f, corner));
                    }
                }
                for (int i = 1; i < candidates.Count; i++)
                {
                    Path q = candidates[i];
                    Drive(q, pilot, me, c, a, t.Car.Id);
                    // Level at the turn-in, where the other car has to leave room: the place is won,
                    // which is worth more than the metres the slower line costs.
                    if (q.WinsPlace) q.Score += PassWorth;
                }
            }

            // The furthest path wins. Leaving the line has to be worth it, but a pass already under
            // way only has to keep up with the line.
            Path best = baseline;
            bool going = plan.Kind != Kind.Line && plan.Corner == corner;
            foreach (Path p in candidates)
            {
                if (p == baseline || p.Rejected) continue;
                // A pass under way only has to stay close to the line; a committed dive more so, since
                // the inside line is slower until the place is won.
                float need = !(going && p.Kind == plan.Kind) ? WorthIt : committed ? -8f : -WorthIt;
                if (p.Score > best.Score + need) best = p;
            }

            // Between evenly matched cars no path gains much in a few seconds: the place is won by
            // having a go. Held up, or tucked into the tow, near the end of a straight, it commits to a
            // late-braking dive (or round the outside if the inside is covered) as long as that path
            // runs into nobody. If it doesn't work, the give-up rule drops it.
            if (best == baseline && target >= 0 && !going)
            {
                Near t = near[target];
                bool evenlyMatched = !(t.BeingLapped || t.Slower || t.Car.GivingWay) && me.LapPace < t.Car.LapPace * 1.002f;
                bool close = t.Gap < 6f + 0.15f * speed && (heldUp > 0.5f || pilot.Towed);
                bool endOfStraight = !inCorner && a > 60f && a < 60f + 1.5f * speed;
                if (evenlyMatched && close && endOfStraight)
                    foreach (Kind want in new[] { Kind.LateBrake, Kind.Inside, Kind.Outside })
                    {
                        Path go = candidates.Find(q => q.Kind == want && !q.Rejected);
                        if (go != null && go.Score > baseline.Score - 6f) { best = go; committed = true; break; }
                    }
            }

            // Give up a pass that has been losing to the line for a while: back in line, which frees
            // the space beside the car ahead for whoever is behind.
            if (best.Kind != Kind.Line && going)
            {
                losing = best.Score < baseline.Score ? losing + speed * PlanEvery : 0f;
                if (losing > GiveUpAfter) { bannedCorner = corner; best = baseline; }
            }
            else losing = 0f;
            if (bannedCorner != corner) bannedCorner = -1;
            if (best.Kind == Kind.Line) committed = false;

            if (best.Kind != Kind.Line && plan.Kind == Kind.Line) passTarget = target >= 0 ? near[target].Car.Id : -1;
            plan = best;
            System.Array.Copy(best.Distance, planDistance, Steps + 1);
            System.Array.Copy(best.Lateral, planLateral, Steps + 1);
        }

        /// <summary>
        /// A path that eases from the current offset to <c>off</c> (by <c>from</c> metres ahead, or
        /// over a ramp), holds it to <c>to</c> metres ahead, then eases back to the line.
        /// </summary>
        private static Path Make(Kind kind, int start, int samples, Circuit c, float now, float ramp, float off,
            float from = 0f, float to = float.MaxValue, int corner = -1)
        {
            var p = new Path { Kind = kind, Start = start, Corner = kind == Kind.Line ? -1 : corner, Offset = new float[samples], Speed = new float[samples] };
            float reach = Mathf.Max(from, ramp);
            for (int m = 0; m < samples; m++)
            {
                float d = m * c.Step;
                if (d < reach) p.Offset[m] = Mathf.Lerp(now, off, EaseOut(d / reach));
                else if (d < to) p.Offset[m] = off;
                else p.Offset[m] = Mathf.Lerp(off, 0f, Smooth((d - to) / ramp));
            }
            return p;
        }

        /// <summary>Overwrites a path from the first knot on, easing between (distance, offset) knots.</summary>
        private static void Shape(Path p, Circuit c, params float[] knots)
        {
            for (int m = 0; m < p.Offset.Length; m++)
            {
                float d = m * c.Step;
                if (d < knots[0]) continue;
                float v = knots[knots.Length - 1];
                for (int i = 0; i + 3 < knots.Length; i += 2)
                    if (d >= knots[i] && d < knots[i + 2])
                    {
                        v = Mathf.Lerp(knots[i + 1], knots[i + 3], Smooth((d - knots[i]) / Mathf.Max(knots[i + 2] - knots[i], 1f)));
                        break;
                    }
                p.Offset[m] = v;
            }
        }

        /// <summary>Moves off at once and settles gently, so a plan made again and again still gets somewhere.</summary>
        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return t * (2f - t);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Drives a path forward in time: at the speeds it allows (braking in time for what is ahead),
        /// as fast as the car accelerates, and behind any car that will be in its way. Scores how far
        /// it gets. A path that moves into a car beside it is rejected.
        /// </summary>
        private void Drive(Path p, AiPilot pilot, TrafficCar me, Circuit c, float corner, int passing = -1)
        {
            RacingLine line = pilot.Line;
            int n = p.Offset.Length;
            for (int m = 0; m < n; m++)
            {
                int k = c.Wrap(p.Start + m);
                float v = pilot.LaneSpeed(k, p.Offset[m]);
                float d = m * c.Step;
                if (p.Kind == Kind.LateBrake && d > corner - 150f && d < corner) v *= Lunge;
                p.Speed[m] = v;
            }
            float brake = me.Braking;
            for (int m = n - 2; m >= 0; m--)
                p.Speed[m] = Mathf.Min(p.Speed[m], Mathf.Sqrt(p.Speed[m + 1] * p.Speed[m + 1] + 2f * brake * c.Step));

            // Where the car is now, measured from the start of the path.
            float s = Mathf.Repeat(me.Distance, c.Lap) - p.Start * c.Step;
            s = Mathf.Repeat(s + c.Lap * 0.5f, c.Lap) - c.Lap * 0.5f;
            float startS = s;
            float vel = Mathf.Max(me.Speed, 0f);
            p.Blocked = false;
            p.Rejected = false;
            p.WinsPlace = false;
            p.HeldBy.Clear();
            p.Distance[0] = me.Distance;
            p.Lateral[0] = me.Lateral;
            float lastLat = me.Lateral;
            if (yields.Length < inReach.Count) yields = new bool[inReach.Count * 2];
            System.Array.Clear(yields, 0, inReach.Count);
            for (int step = 1; step <= Steps; step++)
            {
                int m = Mathf.Clamp(Mathf.FloorToInt(s / c.Step), 0, n - 1);
                int k = c.Wrap(p.Start + m);
                float lat = LateralAt(line, k, p.Offset[m]);
                // In another car's wake there is less drag, so the car outruns the target speeds on the
                // straights, and keeps that speed after pulling out until it has to brake.
                float wake = 1f;
                int count = inReach.Count;
                for (int x = 0, at = (step - 1) * count; x < count; x++)
                {
                    float behind = gapAt[at + x] - (s - startS);
                    if (behind <= CarLength || behind >= WakeLength) continue;
                    float side = Mathf.Abs(lat - latAt[at + x]);
                    if (side < 2f)
                        wake = Mathf.Min(wake, 1f - WakeDrag * (1f - behind / WakeLength) * (1f - side / 2f));
                }
                float resist = pilot.Profile.Resist(k, vel);
                float drag = resist - pilot.Profile.Resist(k, 0f);
                float accel = Mathf.Max(pilot.Profile.DriveAccel(vel) - resist + (1f - wake) * drag, 0.5f);
                bool braking = m + 1 < n && p.Speed[m + 1] < p.Speed[m];
                vel = Mathf.Min(braking ? p.Speed[m] : Mathf.Max(p.Speed[m], vel), vel + accel * Tick);
                float next = s + vel * Tick;
                for (int x = 0, at = step * count; x < count; x++)
                {
                    if (yields[x]) continue;
                    float og = gapAt[at + x], olat = latAt[at + x];
                    if (Mathf.Abs(lat - olat) >= SideBySide - 0.3f) continue;
                    Near o = near[inReach[x]];
                    float wasOther = latAt[at - count + x];
                    float gap = og - (next - startS);     // its place relative to this car's, then
                    // It turns across this car's nose while this car is already overlapping it: it has
                    // to leave room (it will, its own fences see this car alongside).
                    if (o.Car.Id == passing && step > 1 && Mathf.Abs(gap) < CarLength * 0.9f && Mathf.Abs(olat - wasOther) > Mathf.Abs(lat - lastLat))
                    {
                        yields[x] = true;
                        if (o.Car.Id == passing && gap < CarLength * 0.3f) p.WinsPlace = true;
                        continue;
                    }
                    float follow = CarLength + 1f + 0.03f * vel;   // tucked in, as close as an attacker sits
                    if (gap >= CarLength * 0.6f)
                    {
                        // Ahead in the path: follow it.
                        if (gap < follow)
                        {
                            float held = Mathf.Max(startS + og - follow, s);
                            if (held < next)
                            {
                                next = held;
                                vel = (next - s) / Tick;
                                p.Blocked = true;
                                if (!p.HeldBy.Contains(o.Car.Id)) p.HeldBy.Add(o.Car.Id);
                            }
                        }
                    }
                    else if (gap > -CarLength * 0.6f && o.Gap > -(CarLength + 1f) && p.Kind != Kind.Line)
                        p.Rejected = true;   // into a car beside it
                }
                s = next;
                lastLat = lat;
                p.Distance[step] = me.Distance + (s - startS);
                p.Lateral[step] = lat;
            }
            p.Score = s - startS;
        }

        // Where this car will be as the others see it, each Tick from now: ground gained and lateral.
        // The same for every car looking at it this step, so it is worked out once.
        private readonly float[] seenGain = new float[Steps + 1], seenLat = new float[Steps + 1];
        private float seenAge = float.NaN, seenSpeed, seenLateral;
        private int seenPlan = -1, plans;

        /// <summary>Fills <see cref="seenGain"/> and <see cref="seenLat"/> for this car as it is now.</summary>
        private void Foresee(in TrafficCar me)
        {
            if (seenAge == planAge && seenPlan == plans && seenSpeed == me.Speed && seenLateral == me.Lateral) return;
            seenAge = planAge;
            seenPlan = plans;
            seenSpeed = me.Speed;
            seenLateral = me.Lateral;
            float j0 = planAge / Tick;
            float d0 = Sample(planDistance, j0);
            // Its plan, eased in from where it really is now.
            float drift = me.Lateral - Sample(planLateral, j0);
            for (int step = 0; step <= Steps; step++)
            {
                float t = step * Tick, j = j0 + t / Tick;
                float d = Sample(planDistance, j);
                if (j > Steps) d += (j - Steps) * Tick * Mathf.Max(me.Speed, 0f);
                seenGain[step] = d - d0;
                seenLat[step] = Sample(planLateral, j) + drift * Mathf.Max(0f, 1f - t);
            }
        }

        private static float Sample(float[] a, float j)
        {
            if (j >= a.Length - 1) return a[a.Length - 1];
            int i = (int)j;
            return Mathf.Lerp(a[i], a[i + 1], j - i);
        }

        /// <summary>The nearest car ahead in this car's path, or else the nearest ahead, as an index into <see cref="near"/>.</summary>
        private int BlockerAhead(RacingLine line, Circuit c, float shift)
        {
            int best = -1, any = -1;
            for (int i = 0; i < near.Count; i++)
            {
                Near n = near[i];
                if (n.Gap <= 0f) continue;
                if (any < 0 || n.Gap < near[any].Gap) any = i;
                if (Apart(line, n, shift) < SideBySide && (best < 0 || n.Gap < near[best].Gap)) best = i;
            }
            return best >= 0 ? best : any;
        }

        /// <summary>How fast the plan moves sideways here, m/s, so the easing keeps up with it.</summary>
        private float PlanRate(Circuit c, int along)
        {
            if (plan.Offset == null || along + 1 >= plan.Offset.Length) return 0f;
            return Mathf.Abs(plan.Offset[along + 1] - plan.Offset[along]) / c.Step * speed * 1.2f;
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

        /// <summary>Adds another car to <see cref="near"/> if it is within sight. False when it is not.</summary>
        private bool Consider(TrafficCar o, float there, TrafficCar me, RacingLine line, float here)
        {
            if (o.Id == me.Id) return false;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float gap = there - here;
            if (gap > lap * 0.5f) gap -= lap;
            else if (gap < -lap * 0.5f) gap += lap;
            if (gap < -LookBehind || gap > LookAhead) return false;
            float race = o.Distance - me.Distance;
            int k = c.IndexOf(there);
            near.Add(new Near
            {
                Car = o,
                Gap = gap,
                Shift = o.Lateral - line.Lateral[k],
                Index = k,
                LapsMe = gap < 0f && race > lap * 0.5f,
                BeingLapped = gap > 0f && race < -lap * 0.5f,
                Faster = o.LapPace < me.LapPace * (1f - ClassGap),
                Slower = o.LapPace > me.LapPace * (1f + ClassGap),
            });
            return true;
        }

        private int Find(int id)
        {
            for (int i = 0; i < near.Count; i++) if (near[i].Car.Id == id) return i;
            return -1;
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
