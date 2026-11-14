using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The drive out of the garages into the formation, with no Unity objects so it can be tested outside Unity.
    /// Every car waits parked in its garage, nose angled out of the door towards the pit exit. The garages are
    /// given out in grid order from the pit exit end, pole nearest the exit, so the field already stands in
    /// its order along the lane.
    ///
    /// The field leaves on a timetable rather than one car at a time. A head runs down the pit lane ahead of
    /// the pole sitter, out of the exit and onto the track, at <see cref="PitSpeed"/> in the limiter zone,
    /// <see cref="RoadSpeed"/> on the exit road and <see cref="TrackSpeed"/> once back on the track. Car i's
    /// place is where the head was i headways ago, and the pace car's is two headways ahead of the head, so
    /// every car passes every point the same time after the car ahead. The headway is the time the head takes
    /// to pass one garage, so in the zipper the garages make, every car's place starts beside its own garage
    /// and the whole field pulls out at once. A car whose place starts further back waits in its garage until
    /// its place is <see cref="LaunchLead"/> seconds away. Each car drives a path out of its garage
    /// (<see cref="ExitPath"/>) onto its own route down the pit lane (<see cref="Route"/>) and, past the exit,
    /// onto its column of the formation. On the track the head runs at the speed that spaces the field
    /// <see cref="RollingStart.RowGap"/> per row, so it is in formation as it comes out.
    ///
    /// Places are counted along the pit lane's own route, not the track beside it (<see cref="Along"/>).
    /// </summary>
    public sealed class GarageStart
    {
        /// <summary>The head's speed in the limiter zone, m/s: well under the limiter, so every car keeps its place.</summary>
        public const float PitSpeed = 24f;
        /// <summary>The head's speed on the exit road, m/s.</summary>
        public const float RoadSpeed = 30f;
        /// <summary>Speed along the path out of the garage, m/s.</summary>
        public const float RollSpeed = 4f;
        /// <summary>Seconds after the start before the head moves: the field's time to roll out of the garages.</summary>
        public const float RollWait = 4.5f;
        /// <summary>Seconds the head takes to come up to speed: gently enough for the slowest car to keep up.</summary>
        public const float Ramp = 16f;
        /// <summary>Share of the slowest car's own speed through each bend the head goes at, so every car has grip to spare.</summary>
        private const float Margin = 0.85f;
        /// <summary>A car whose place starts behind its garage leaves when its place is this many seconds away.</summary>
        public const float LaunchLead = 6f;
        /// <summary>Over this far the head's speed changes from one stretch to the next, m.</summary>
        private const float Blend = 120f;
        /// <summary>Speed per metre a car is out of its place, and most it may run over its place's speed to get back.</summary>
        private const float Gain = 0.5f;
        private const float Catch = 5f;

        /// <summary>The car's middle in its garage: this far across from the garage's middle, to its +x side, m.</summary>
        public const float ParkX = 3.9f;
        /// <summary>The car's nose stops this far inside the door, m.</summary>
        private const float NoseIn = 0.5f;
        /// <summary>The car stands this many degrees from straight out of the door, nose towards the pit exit.</summary>
        public const float ParkAngle = 30f;
        private const float HalfLength = 2.4f;
        /// <summary>The path leaves the garage straight until the car's middle is this far out of the door, m.</summary>
        private const float ClearDoor = 1.2f;
        /// <summary>Spacing of the points along an exit path, m.</summary>
        private const float PathStep = 0.25f;

        public readonly int Cars;
        /// <summary>Seconds between one car and the next past any point.</summary>
        public readonly float Headway;
        /// <summary>The head's speed on the track, m/s: one row of the formation per two headways.</summary>
        public readonly float TrackSpeed;
        /// <summary>Each car's way down the pit lane, out of the exit and onto its column, by grid slot.</summary>
        public readonly RacingLine[] Route;
        /// <summary>The pace car's way: the fast lane, out of the exit and onto pole's column.</summary>
        public readonly RacingLine PaceRoute;
        /// <summary>Seconds since the start.</summary>
        public float Time { get; private set; }
        /// <summary>The timetable's own clock: still while the field rolls out, then up to speed with real time.</summary>
        public float Clock { get; private set; }

        // Places along the way are counted in route samples, one per track sample, from the start of the lap the
        // field starts on, so a lap later is Circuit.Count further: the pit lane's own measure, which a car's
        // track distance is not where the lane bends away from the track.
        private readonly Circuit circuit;
        private readonly RacingLine[] columns;
        private readonly Vector3[][] paths;
        private readonly int[] join;
        private readonly float[] joinAt;   // route samples
        private readonly bool[] launched;
        private readonly float outAt, origin;
        private readonly float[] times;    // seconds the head takes from origin to each route sample after it
        private readonly float[] speeds;   // the head's speed at each route sample, m/s
        private readonly float[] metres;   // length of each route sample, m
        private readonly float[] reach;    // metres along the way from origin to each route sample
        private readonly float start;      // timetable seconds of car 0's place when the clock reads 0
        private readonly int[] lastK, laps;   // per slot + 1: the pace car first

        /// <param name="garages">Each grid slot's garage.</param>
        /// <param name="formation">The rolling start's formation speed at each track sample.</param>
        public GarageStart(PitLane pits, PitComplex complex, RacingLine[] columns, IReadOnlyList<int> garages,
            IReadOnlyList<CarSpec> specs, float[] formation)
        {
            circuit = pits.Circuit;
            this.columns = columns;
            Circuit c = circuit;
            int n = c.Count;
            Cars = garages.Count;
            Route = new RacingLine[Cars];
            paths = new Vector3[Cars][];
            join = new int[Cars];
            joinAt = new float[Cars];
            launched = new bool[Cars];
            lastK = new int[Cars + 1];
            laps = new int[Cars + 1];
            var routes = new Dictionary<(int, int), RacingLine>();
            for (int i = 0; i < Cars; i++)
            {
                int column = i % 2;
                if (!routes.TryGetValue((column, garages[i]), out RacingLine r))
                    routes[(column, garages[i])] = r = pits.Route(columns[column], garages[i]);
                Route[i] = r;
                Vector3 p = Parked(complex, garages[i], out Vector3 forward);
                paths[i] = ExitPath(r, p, forward, pits.BoxSample(garages[i]), MinRadius(specs[i]), out join[i]);
                joinAt[i] = Unwrap(join[i]);
                lastK[i + 1] = join[i];
                laps[i + 1] = LapOf(join[i]);
            }
            PaceRoute = pits.Route(columns[0], -1);

            // From behind the furthest garage, round to the track a lap on.
            float first = float.MaxValue;
            for (int i = 0; i < Cars; i++) first = Mathf.Min(first, joinAt[i]);
            origin = first - 60;
            int limitEnd = -1;
            float best = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (!pits.InRange(k)) continue;
                float gap = Mathf.Abs(pits.LaneAt(k) - pits.LimitEndLane);
                if (gap < best) { best = gap; limitEnd = k; }
            }
            float limitAt = origin + c.Wrap(limitEnd - Mathf.FloorToInt(origin));
            outAt = limitAt + c.Wrap(pits.RejoinedSample() - limitEnd);
            int steps = Mathf.CeilToInt(outAt - origin) + n + 2;
            metres = new float[steps];
            reach = new float[steps];
            for (int j = 0; j < steps; j++)
            {
                metres[j] = Mathf.Max(PaceRoute.Segment[c.Wrap(Mathf.FloorToInt(origin) + j)], 0.05f);
                if (j > 0) reach[j] = reach[j - 1] + metres[j - 1];
            }

            // The headway is the time the head takes past one garage in the pit lane.
            speeds = new float[steps];
            for (int j = 0; j < steps; j++) speeds[j] = PitSpeed;
            times = new float[steps];
            Integrate();
            float lo = float.MaxValue, hi = float.MinValue;
            for (int g = 0; g < pits.Garages; g++)
            {
                float t = TimeAt(origin + c.Wrap(pits.BoxSample(g) - Mathf.FloorToInt(origin)));
                lo = Mathf.Min(lo, t);
                hi = Mathf.Max(hi, t);
            }
            Headway = pits.Garages > 1 ? (hi - lo) / (pits.Garages - 1) : 0.7f;
            TrackSpeed = 0.5f * RollingStart.RowGap / Headway;

            // Every car can take every bend of the way at the head's speed.
            var bends = new float[n];
            for (int k = 0; k < n; k++) bends[k] = float.PositiveInfinity;
            var seen = new HashSet<string>();
            foreach (CarSpec spec in specs)
            {
                if (!seen.Add(spec.Name)) continue;
                var profile = new SpeedProfile(PaceRoute, spec, 1f);
                for (int k = 0; k < n; k++) bends[k] = Mathf.Min(bends[k], Margin * profile.Speed[k]);
            }
            for (int j = 0; j < steps; j++)
            {
                float u = origin + j;
                int k = c.Wrap(Mathf.FloorToInt(u));
                float v = u < limitAt ? PitSpeed : u < outAt ? Mathf.Min(RoadSpeed, bends[k]) : Mathf.Min(TrackSpeed, formation[k]);
                speeds[j] = Mathf.Max(v, 5f);
            }
            // Smooth the changes from one stretch to the next.
            var smooth = new float[steps];
            int half = Mathf.Max(1, Mathf.RoundToInt(Blend * 0.5f / c.Step));
            for (int j = 0; j < steps; j++)
            {
                float sum = 0f;
                int m = 0;
                for (int q = Mathf.Max(0, j - half); q <= Mathf.Min(steps - 1, j + half); q++) { sum += speeds[q]; m++; }
                smooth[j] = sum / m;
            }
            System.Array.Copy(smooth, speeds, steps);
            Integrate();

            // The timetable's start: no car's place may start ahead of where it joins its route.
            start = float.MaxValue;
            for (int i = 0; i < Cars; i++) start = Mathf.Min(start, TimeAt(joinAt[i]) + i * Headway);
            float pace = Place(-1);
            lastK[0] = c.Wrap(Mathf.RoundToInt(pace));
            laps[0] = Mathf.FloorToInt(Mathf.RoundToInt(pace) / (float)n);
        }

        private void Integrate()
        {
            times[0] = 0f;
            for (int j = 1; j < times.Length; j++) times[j] = times[j - 1] + metres[j - 1] * 2f / (speeds[j - 1] + speeds[j]);
        }

        /// <summary>The lap a car at track sample k is on before the race: behind the line on the lap before.</summary>
        private int LapOf(int k) => k * circuit.Step > circuit.Lap * 0.5f ? -1 : 0;

        private float Unwrap(int k) => LapOf(k) * circuit.Count + k;

        // ---------------------------------------------------------------- the garages

        /// <summary>Each grid slot's garage: pole in the one nearest the pit exit, and so on back up the lane.</summary>
        public static int GarageFor(int slot, int garages) => slot < garages ? garages - 1 - slot : slot % garages;

        /// <summary>Where a car waits in its garage: its middle on the floor, and the way it faces.</summary>
        public static Vector3 Parked(PitComplex complex, int garage, out Vector3 forward)
        {
            GarageFrame f = complex.Frame(garage);
            float a = ParkAngle * Mathf.Deg2Rad;
            forward = (f.Out * Mathf.Cos(a) + f.Along * Mathf.Sin(a)).normalized;
            // The nose's outer corner stops NoseIn inside the door.
            float z = complex.Depth * 0.5f - NoseIn - HalfLength * Mathf.Cos(a) - 1f * Mathf.Sin(a);
            return f.At(ParkX, 0f, z);
        }

        /// <summary>Tightest turn a car can drive at walking pace, with some lock to spare, m.</summary>
        public static float MinRadius(CarSpec spec) => Mathf.Max(6f, 1.15f * 2f * spec.HalfWheelbase / (float)System.Math.Tan(spec.SteerLock));

        /// <summary>
        /// The path out of a garage: straight out of the door along the way the car faces until clear of it, then
        /// a curve no tighter than <paramref name="radius"/> onto the route at the first sample past the box it can
        /// reach so, <paramref name="joins"/>.
        /// </summary>
        public static Vector3[] ExitPath(RacingLine route, Vector3 parked, Vector3 forward, int box, float radius, out int joins)
        {
            Circuit c = route.Circuit;
            forward = new Vector3(forward.x, 0f, forward.z).normalized;
            // Out of the door: the door is where the route's side of the garage meets the lane's edge, so straight
            // until the car's middle has crossed the lane's edge and ClearDoor more.
            float straight = 2f * HalfLength;
            Vector3 d = parked + forward * straight;
            joins = c.Wrap(box + 4);
            // The first sample past the box the car can turn onto: the curve's handles are tried from half to one
            // and a half chords long, and the one with the gentlest tightest bend counts.
            Vector3[] best = null;
            float gentlest = float.MaxValue;
            int gentlestK = c.Wrap(box + 40);
            float gentlestScale = 1f;
            for (int n = 4; n < 80 && best == null; n++)
            {
                int k = c.Wrap(box + n);
                Vector3 j = route.Point[k];
                Vector3 t = route.Tangent[k];
                float chord = Flat(j - d).magnitude;
                if (chord < 2f || Vector3.Dot(Flat(j - d), t) <= 0f) continue;
                for (float scale = 0.5f; scale <= 1.51f; scale += 0.25f)
                {
                    Hermite(d, forward, j, t, chord * scale, 40, out float curvature);
                    if (curvature < gentlest) { gentlest = curvature; gentlestK = k; gentlestScale = scale; }
                    if (curvature > 1f / radius) continue;
                    joins = k;
                    best = Hermite(d, forward, j, t, chord * scale, Mathf.CeilToInt(chord * 1.4f / PathStep), out _);
                    break;
                }
            }
            if (best == null)
            {
                // None turns gently enough: the gentlest there is.
                joins = gentlestK;
                float chord = Flat(route.Point[gentlestK] - d).magnitude;
                best = Hermite(d, forward, route.Point[gentlestK], route.Tangent[gentlestK], chord * gentlestScale, Mathf.CeilToInt(chord * 1.4f / PathStep), out _);
            }
            int lead = Mathf.CeilToInt(straight / PathStep);
            var path = new Vector3[lead + best.Length];
            for (int i = 0; i < lead; i++) path[i] = parked + forward * (i * PathStep);
            System.Array.Copy(best, 0, path, lead, best.Length);
            return path;
        }

        /// <summary>A cubic Hermite curve from a to b with flat unit tangents scaled by scale, and its tightest curvature.</summary>
        private static Vector3[] Hermite(Vector3 a, Vector3 ta, Vector3 b, Vector3 tb, float scale, int n, out float tightest)
        {
            n = Mathf.Max(n, 4);
            var pts = new Vector3[n + 1];
            ta = Flat(ta).normalized * scale;
            tb = Flat(tb).normalized * scale;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n, u2 = u * u, u3 = u2 * u;
                pts[i] = (2f * u3 - 3f * u2 + 1f) * a + (u3 - 2f * u2 + u) * ta + (-2f * u3 + 3f * u2) * b + (u3 - u2) * tb;
            }
            tightest = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 p = Flat(pts[i - 1]), q = Flat(pts[i]), r = Flat(pts[i + 1]);
                Vector3 e1 = q - p, e2 = r - q;
                float l1 = e1.magnitude, l2 = e2.magnitude;
                if (l1 < 1e-4f || l2 < 1e-4f) continue;
                float cross = e1.x * e2.z - e1.z * e2.x;
                float turn = (float)System.Math.Asin(Mathf.Clamp(cross / (l1 * l2), -1f, 1f));
                tightest = Mathf.Max(tightest, Mathf.Abs(turn) / (0.5f * (l1 + l2)));
            }
            return pts;
        }

        /// <summary>The path a car drives out of its garage.</summary>
        public Vector3[] Path(int slot) => paths[slot];

        /// <summary>The route sample a car's path out of its garage ends on.</summary>
        public int Join(int slot) => join[slot];

        // ---------------------------------------------------------------- the timetable

        public void Tick(float dt)
        {
            Time += dt;
            Clock += Rate(Time) * dt;
        }

        /// <summary>How fast the timetable runs against real time: not at all while the field rolls out, then up to 1.</summary>
        private static float Rate(float t)
        {
            float u = Mathf.Clamp01((t - RollWait) / Ramp);
            return u * u * (3f - 2f * u);
        }

        /// <summary>How fast the timetable runs now, against real time.</summary>
        public float RateNow => Rate(Time);

        /// <summary>Where a car's place is now, in route samples. Slot -1 is the pace car.</summary>
        public float Place(int slot) => At(Clock + start - (slot < 0 ? -2f : slot) * Headway);

        /// <summary>The route sample a car's place is on now. Slot -1 is the pace car.</summary>
        public int PlaceSample(int slot) => circuit.Wrap(Mathf.FloorToInt(Place(slot)));

        /// <summary>True once a car may leave its garage: its place is near enough. It stays true.</summary>
        public bool Launch(int slot)
        {
            if (launched[slot]) return true;
            float placeTime = Clock + start - slot * Headway;
            if (placeTime + LaunchLead + RollWait * (1f - RateNow) >= TimeAt(joinAt[slot])) launched[slot] = true;
            return launched[slot];
        }

        /// <summary>
        /// Follows a car along its route: k is its route sample now. Returns where it is, in route samples, counting
        /// the laps as it crosses the line. Call it once a step for each car on its route. Slot -1 is the pace car.
        /// </summary>
        public float Along(int slot, int k, Vector3 position, RacingLine route)
        {
            Circuit c = circuit;
            int s = slot + 1;
            if (k < 0) return laps[s] * c.Count + lastK[s];
            if (lastK[s] > c.Count - 50 && k < 50) laps[s]++;
            else if (lastK[s] < 50 && k > c.Count - 50) laps[s]--;
            lastK[s] = k;
            Vector3 t = route.Tangent[k];
            float along = (position.x - route.Point[k].x) * t.x + (position.z - route.Point[k].z) * t.z;
            return laps[s] * c.Count + k + Mathf.Clamp(along / Mathf.Max(route.Segment[k], 0.05f), -1f, 1f);
        }

        /// <summary>
        /// Fastest a car on its route may go, m/s: back to its place, no faster than its place moves by more than
        /// <see cref="Catch"/>, and never faster than it could stop behind anything ahead (<c>follow</c>).
        /// <c>at</c> is where it is, from <see cref="Along"/>. Slot -1 is the pace car.
        /// </summary>
        public float Cap(int slot, float at, float follow)
        {
            float place = Place(slot);
            float v = RateNow * SpeedAt(place);
            float behind = MetresAt(place) - MetresAt(at);
            float cap = Mathf.Clamp(v + Gain * behind, 0f, v + Catch);
            return Mathf.Min(cap, follow);
        }

        /// <summary>
        /// Fastest a car may go to be sure of stopping behind a car ahead of it on the way, m/s: <c>at</c> and
        /// <c>ahead</c> are where the two are, from <see cref="Along"/>, and <c>speed</c> and <c>aheadSpeed</c> how fast
        /// they go. Measured along the way in metres, as the lane's samples are longer where it bends away
        /// from the track.
        /// </summary>
        public float Behind(float at, float speed, CarSpec spec, float ahead, float aheadSpeed, CarSpec aheadSpec)
        {
            float gap = MetresAt(ahead) - MetresAt(at) - 2f * HalfLength - FollowGap - Reaction * speed;
            float brake = spec.BrakeDecel * 9.81f * 0.8f;
            // Safe even if the car ahead stops as hard as it can and brakes harder than this one.
            float theirs = aheadSpec != null ? aheadSpec.BrakeDecel * 9.81f : brake;
            float ratio = Mathf.Min(brake / Mathf.Max(theirs, 1f), 1f);
            return gap > 0f ? Mathf.Sqrt(aheadSpeed * aheadSpeed * ratio + 2f * brake * gap) : 0f;
        }

        /// <summary>Seconds of the car's own speed added to the gap it keeps, for the time it takes to react.</summary>
        private const float Reaction = 0.3f;

        /// <summary>
        /// True while a car could pull out of its garage onto its route without cutting in: no car on its way out is
        /// within <see cref="JoinClearance"/> of where it joins. <c>along</c> is where each car is, from
        /// <see cref="Along"/>, and <c>onRoute</c> which of them are on their routes. A car <c>ignore</c> says yes to
        /// is no obstacle: a player behind in the grid, whom this car passes through.
        /// </summary>
        public bool JoinClear(int slot, IReadOnlyList<float> along, IReadOnlyList<bool> onRoute, System.Func<int, bool> ignore = null)
        {
            float mine = MetresAt(joinAt[slot]);
            for (int j = 0; j < along.Count; j++)
            {
                if (j == slot || !onRoute[j] || (ignore != null && ignore(j))) continue;
                float off = MetresAt(along[j]) - mine;
                if (off > -JoinClearance && off < JoinClearance * 0.6f) return false;
            }
            return true;
        }

        /// <summary>A car pulls out only with nobody this far behind where it joins, nor most of this far ahead, m.</summary>
        private const float JoinClearance = 14f;

        /// <summary>
        /// True when another car, at <c>other</c>, is on a car's way ahead of it: within half a car's width of its
        /// route a little ahead of k, its route sample now.
        /// </summary>
        public bool InWay(int slot, int k, Vector3 other, float at)
        {
            RacingLine r = Way(slot, at);
            int j = r.Nearest(other, k, 0, 45);
            Vector3 off = other - r.Point[j];
            return off.x * off.x + off.z * off.z < 2.6f * 2.6f;
        }

        /// <summary>
        /// The line a car drives at a place, from <see cref="Along"/>: its route down the pit lane, then once out its
        /// column, which runs on round the lap where the route turns back into the pit lane.
        /// </summary>
        public RacingLine Way(int slot, float at) => Out(at) ? columns[slot % 2] : Route[slot];

        /// <summary>Metres along the way from one place to another, both from <see cref="Along"/>.</summary>
        public float Between(float from, float to) => MetresAt(to) - MetresAt(from);

        /// <summary>Where a car is on its route from its position, from the route sample last found for it, k.</summary>
        public int Find(int slot, Vector3 position, int k) => Route[slot].Nearest(position, k < 0 ? join[slot] : k, 8, 20);

        /// <summary>Gap a car keeps to the one ahead even if that one stopped dead, m.</summary>
        private const float FollowGap = 2.5f;

        /// <summary>Metres along the way from the origin to a place.</summary>
        private float MetresAt(float u)
        {
            float j = u - origin;
            if (j <= 0f) return j * metres[0];
            int last = reach.Length - 1;
            if (j >= last) return reach[last] + (j - last) * metres[last];
            int a = (int)j;
            return reach[a] + (j - a) * metres[a];
        }

        /// <summary>True once a car at this place, from <see cref="Along"/>, is out of the pit lane, on its column.</summary>
        public bool Out(float at) => at >= outAt;

        private float SpeedAt(float u)
        {
            float j = u - origin;
            if (j <= 0f) return speeds[0];
            if (j >= speeds.Length - 1) return speeds[speeds.Length - 1];
            int a = (int)j;
            return Mathf.Lerp(speeds[a], speeds[a + 1], j - a);
        }

        /// <summary>Seconds the head takes from the origin to a place.</summary>
        private float TimeAt(float u)
        {
            float j = u - origin;
            if (j <= 0f) return j * metres[0] / speeds[0];
            int last = times.Length - 1;
            if (j >= last) return times[last] + (j - last) * metres[last] / speeds[last];
            int a = (int)j;
            return Mathf.Lerp(times[a], times[a + 1], j - a);
        }

        /// <summary>The place the head reaches after a number of seconds from the origin.</summary>
        private float At(float t)
        {
            if (t <= 0f) return origin + t * speeds[0] / metres[0];
            int last = times.Length - 1;
            if (t >= times[last]) return origin + last + (t - times[last]) * speeds[last] / metres[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= t) lo = mid; else hi = mid;
            }
            float u = (t - times[lo]) / Mathf.Max(times[hi] - times[lo], 1e-6f);
            return origin + lo + u;
        }

        // ---------------------------------------------------------------- driving out of the garage

        /// <summary>
        /// Pedals and steering that drive a car along its path out of the garage at <see cref="RollSpeed"/>,
        /// following a point a little ahead. <paramref name="progress"/> is the path point the car has reached,
        /// carried from one step to the next. True once the car has reached the path's end, on its route.
        /// </summary>
        public static bool Roll(Vector3[] path, ref int progress, Vector3 position, Vector3 forward, float speed, CarSpec spec, out AiPilot.Controls controls)
        {
            Vector3 p = Flat(position);
            int best = progress;
            float bestD = float.MaxValue;
            for (int i = progress; i < Mathf.Min(path.Length, progress + 40); i++)
            {
                float d = (Flat(path[i]) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            progress = best;
            controls = default;
            if (progress >= path.Length - 2) return true;

            float look = Mathf.Max(2.5f, 0.8f * speed);
            int target = Mathf.Min(path.Length - 1, progress + Mathf.CeilToInt(look / PathStep));
            Vector3 to = Flat(path[target]) - p;
            Vector3 f = Flat(forward).normalized;
            var right = new Vector3(f.z, 0f, -f.x);
            float dist = Mathf.Max(to.magnitude, 0.5f);
            float sinA = Vector3.Dot(to, right) / dist;
            float wheelbase = 2f * spec.HalfWheelbase;
            controls.Steer = Mathf.Clamp((float)System.Math.Atan(2f * wheelbase * sinA / dist), -spec.SteerLock, spec.SteerLock);

            // Slow for the bend ahead and for the end of the path, then hold the roll speed.
            float want = RollSpeed;
            float error = want - speed;
            controls.Throttle = Mathf.Clamp01(0.35f * error);
            controls.Brake = error < -0.5f ? Mathf.Clamp01(-0.3f * error) : 0f;
            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
