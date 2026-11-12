using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public enum PropKind { Hoarding, CameraTower, Cabin }

    /// <summary>One free-standing trackside prop: where it stands and which way it faces.</summary>
    public sealed class PropSpot
    {
        public PropKind Kind;
        /// <summary>Lap distance of the track beside it.</summary>
        public float Distance;
        /// <summary>0 for the left of the track, 1 for the right.</summary>
        public int Side;
        /// <summary>The middle of its footprint, at the lowest ground under it.</summary>
        public Vector3 Base;
        /// <summary>The highest ground under its footprint.</summary>
        public float High;
        /// <summary>Flat unit vector across the track, from the prop towards the road.</summary>
        public Vector3 ToRoad;
        /// <summary>Flat unit vector along the track, the way the cars run.</summary>
        public Vector3 Along;
        /// <summary>Which advert a hoarding shows.</summary>
        public int Variant;
    }

    /// <summary>
    /// Where the free-standing trackside props stand: rows of advertising hoardings behind the barrier, TV camera
    /// towers on the outside of the corners, and portable cabins further back. Each one is small and stands on its own
    /// legs or plinth, so nothing has to follow the ground: a prop goes only where the ground under its whole footprint
    /// is close to level and close to the road's height. Every prop stands behind the runoff in the strip the trees keep
    /// clear of, off every road, runoff and pit lane, clear of the grandstands and paddock, the marshal posts, boards and
    /// gantry, the armco's buried ends and the broadcast cameras' spots. Placement is repeatable and free of Unity's runtime.
    /// </summary>
    public sealed class TracksideProps
    {
        // Hoardings, metres.
        public const float HoardingWidth = 4f;
        public const float HoardingPitch = 4.3f;     // panel to panel along the track
        public const int HoardingRow = 5;            // panels in a full row
        public const float HoardingBack = 3f;        // behind the barrier, or past the runoff's edge
        public const float HoardingEvery = 170f;     // between rows along the lap
        public const float HoardingTop = 1.6f;       // the panel's top above the ground
        public const float RowGap = 60f;             // fewest metres between two rows
        // Camera towers.
        public const float TowerHalf = 1.25f;        // half the footprint's side
        public const float TowerBack = 7f;
        public const float TowerTop = 7.6f;          // roof above the ground
        public const float TowerGap = 250f;          // fewest metres of lap between towers
        // Cabins.
        public const float CabinHalfLength = 3.2f, CabinHalfDepth = 1.4f;
        public const float CabinBack = 8f;
        public const float CabinTop = 2.9f;
        public const float CabinEvery = 650f;

        /// <summary>The ground under a hoarding's legs may differ by this much, a tower's feet or a cabin's corners by these.</summary>
        public const float HoardingSpread = 0.5f, TowerSpread = 0.6f, CabinSpread = 0.35f;
        /// <summary>Most a prop's ground sits above or below the road beside it.</summary>
        public const float MaxDrop = 3f;
        /// <summary>No prop stands closer than this to any road, runoff or pit lane.</summary>
        public const float RoadClearance = 1.5f;
        /// <summary>No prop stands within this of the start line, where the gantry and its floodlights stand.</summary>
        public const float StartClear = 70f;
        /// <summary>Nor within this of a garage, where the paddock and pit buildings are.</summary>
        public const float PaddockClear = 130f;
        /// <summary>Nor within this, along the track, of either end of a grandstand.</summary>
        public const float StandMargin = 15f;
        /// <summary>Most a prop's back stands past the barrier: trees stand no closer than <see cref="LandscapePlan.NearClear"/>.</summary>
        public const float TreeFree = LandscapePlan.NearClear - 2f;
        public const int Adverts = 8;

        public readonly List<PropSpot> Spots = new List<PropSpot>();

        private readonly Circuit c;
        private readonly CircuitBuilder b;
        private readonly IReadOnlyList<StandFootprint> stands;
        private readonly TrackFurniture f;
        private readonly List<Vector3> armcoEnds = new List<Vector3>();
        /// <summary>Spots a broadcast camera may stand at, with the height of its lens.</summary>
        private readonly Dictionary<(int, int), List<Vector3>> cameras = new Dictionary<(int, int), List<Vector3>>();
        private readonly Dictionary<(int, int), List<int>> samples = new Dictionary<(int, int), List<int>>();
        /// <summary>The trees by bucket: where each stands and its crown's radius.</summary>
        private readonly Dictionary<(int, int), List<(Vector3 at, float crown)>> crowns = new Dictionary<(int, int), List<(Vector3, float)>>();
        private const float Bucket = 25f;

        /// <param name="armcoEnds">The armco's buried ends, each a line of points; may be null.</param>
        /// <param name="trees">The landscape's trees, kept clear of; may be null.</param>
        public TracksideProps(Circuit circuit, CircuitBuilder built, IReadOnlyList<StandFootprint> stands, TrackFurniture furniture, IReadOnlyList<Vector3[]> armcoEnds, IReadOnlyList<TreeSpot> trees)
        {
            c = circuit;
            b = built;
            this.stands = stands;
            f = furniture;
            if (armcoEnds != null) foreach (Vector3[] end in armcoEnds) this.armcoEnds.AddRange(end);
            for (int k = 0; k < c.Count; k++)
            {
                var key = Key(c.Centre[k]);
                if (!samples.TryGetValue(key, out var list)) samples[key] = list = new List<int>();
                list.Add(k);
            }
            if (trees != null)
                foreach (TreeSpot t in trees)
                {
                    var key = Key(t.Base);
                    if (!crowns.TryGetValue(key, out var list)) crowns[key] = list = new List<(Vector3, float)>();
                    list.Add((t.Base, LandscapePlan.ModelRadius[(int)t.Kind] * t.Scale));
                }
            BroadcastSpots();
            PlaceTowers();
            PlaceCabins();
            PlaceHoardings();
        }

        // ---------------------------------------------------------------- camera towers

        private void PlaceTowers()
        {
            foreach (CornerData corner in c.Data.corners)
            {
                // Level with the first bend's middle, on the outside, looking across at the apex.
                float apex = corner.s + Mathf.Min(corner.arcs[0].e - corner.s, 120f) * 0.5f;
                int outside = corner.arcs[0].dir > 0 ? 0 : 1;
                PropSpot spot = Find(PropKind.CameraTower, apex, outside, TowerBack, TowerHalf, TowerHalf, TowerSpread, TowerTop, 60f, false);
                if (spot == null || Spots.Exists(p => p.Kind == PropKind.CameraTower && Gap(p.Distance, spot.Distance) < TowerGap)) continue;
                Spots.Add(spot);
            }
        }

        /// <summary>Metres of lap between two distances, whichever way round is shorter.</summary>
        private float Gap(float a, float b) => Mathf.Min(c.Ahead(a, b), c.Ahead(b, a));

        // ---------------------------------------------------------------- cabins

        private void PlaceCabins()
        {
            for (float d = CabinEvery * 0.5f; d < c.Lap - CabinEvery * 0.3f; d += CabinEvery)
            {
                int k = c.IndexOf(d);
                // The side with more ground past the runoff.
                int side = c.Data.clearLeft[k] >= c.Data.clearRight[k] ? 0 : 1;
                PropSpot spot = Find(PropKind.Cabin, d, side, CabinBack, CabinHalfLength, CabinHalfDepth, CabinSpread, CabinTop, 120f, true);
                if (spot != null) Spots.Add(spot);
            }
        }

        // ---------------------------------------------------------------- hoardings

        private void PlaceHoardings()
        {
            int row = 0;
            for (float d = HoardingEvery * 0.5f; d < c.Lap - HoardingEvery * 0.3f; d += HoardingEvery, row++)
            {
                int side = SeenSide(d);
                List<PropSpot> best = null;
                foreach (float shift in new[] { 0f, -25f, 25f, -50f, 50f })
                    foreach (int s in new[] { side, 1 - side })
                    {
                        List<PropSpot> run = Row(Mathf.Repeat(d + shift, c.Lap), s, row);
                        if (run.Count <= (best?.Count ?? 1)) continue;
                        if (run.Exists(p => Spots.Exists(q => q.Kind == PropKind.Hoarding && Flat(q.Base - p.Base) < RowGap))) continue;
                        best = run;
                    }
                if (best == null) continue;
                Spots.AddRange(best);
            }
        }

        /// <summary>The side a driver looks to coming up to d: the outside of the next corner within 400 m, else the side with more room.</summary>
        private int SeenSide(float d)
        {
            foreach (CornerData corner in c.Data.corners)
                if (c.Ahead(d, corner.s) < 400f) return corner.arcs[0].dir > 0 ? 0 : 1;
            int k = c.IndexOf(d);
            return c.Data.clearLeft[k] >= c.Data.clearRight[k] ? 0 : 1;
        }

        /// <summary>The longest unbroken run of panels that fits round d on a side, up to a full row; two adverts alternate.</summary>
        private List<PropSpot> Row(float d, int side, int row)
        {
            var best = new List<PropSpot>();
            var run = new List<PropSpot>();
            for (int i = 0; i < HoardingRow; i++)
            {
                float at = Mathf.Repeat(d + (i - (HoardingRow - 1) / 2f) * HoardingPitch, c.Lap);
                PropSpot panel = At(PropKind.Hoarding, at, side, HoardingBack, HoardingWidth / 2f + 0.2f, 0.9f, HoardingSpread, HoardingTop, true);
                if (panel != null && !Overlaps(panel, run))
                {
                    panel.Variant = (row * 3 + (i % 2) * 5) % Adverts;
                    run.Add(panel);
                    if (run.Count > best.Count) best = new List<PropSpot>(run);
                }
                else run.Clear();
            }
            return best;
        }

        private bool Overlaps(PropSpot p, List<PropSpot> run)
        {
            foreach (PropSpot q in run)
                if (Flat(p.Base - q.Base) < HoardingWidth) return true;
            return false;
        }

        // ---------------------------------------------------------------- finding room

        /// <summary>A spot near lap distance d on a side, shifting along the track up to <paramref name="reach"/> to find one.</summary>
        private PropSpot Find(PropKind kind, float d, int side, float back, float halfLength, float halfDepth, float spread, float top, float reach, bool eitherSide)
        {
            for (float shift = 0f; shift <= reach; shift += 10f)
                foreach (float sgn in shift == 0f ? new[] { 1f } : new[] { -1f, 1f })
                    foreach (int s in eitherSide ? new[] { side, 1 - side } : new[] { side })
                    {
                        PropSpot spot = At(kind, Mathf.Repeat(d + sgn * shift, c.Lap), s, back, halfLength, halfDepth, spread, top, false);
                        if (spot != null) return spot;
                    }
            return null;
        }

        /// <summary>
        /// A prop at lap distance d, its footprint's middle <paramref name="back"/> plus half its depth past the barrier,
        /// or null where it doesn't fit.
        /// </summary>
        private PropSpot At(PropKind kind, float d, int side, float back, float halfLength, float halfDepth, float spread, float top, bool row)
        {
            int k = c.IndexOf(d);
            if (b.RunoffReach(side, k) <= 0f || b.PitWallAt(side, k)) return null;
            if (Gap(d, 0f) < StartClear) return null;
            if (back + halfDepth * 2f > TreeFree) return null;
            float sign = side == 0 ? 1f : -1f;
            float lateral = c.HalfWidth(k) + b.RunoffReach(side, k) + back + halfDepth;
            Vector3 centre = c.Centre[k] + c.Left[k] * (sign * lateral);
            Vector3 along = c.Forward[k], toRoad = c.Left[k] * -sign;
            float road = c.Centre[k].y;

            float low = float.MaxValue, high = float.MinValue;
            for (int i = -2; i <= 2; i++)
                for (int j = -1; j <= 1; j++)
                {
                    Vector3 p = centre + along * (halfLength * i / 2f) + toRoad * (halfDepth * j);
                    if (!Clear(p, side)) return null;
                    float h = c.TerrainHeight(p.x, p.z);
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            if (high - low > spread || Mathf.Abs(low - road) > MaxDrop || Mathf.Abs(high - road) > MaxDrop) return null;
            centre.y = low;

            float radius = Mathf.Sqrt(halfLength * halfLength + halfDepth * halfDepth);
            if (InStand(side, d, halfLength)) return null;
            if (NearFurniture(centre, radius, kind)) return null;
            if (!row && NearProps(centre, radius, kind)) return null;
            if (row && Spots.Exists(q => Flat(q.Base - centre) < radius + Room(q.Kind, kind))) return null;
            var (cx, cz) = Key(centre);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (cameras.TryGetValue((cx + dx, cz + dz), out var near))
                        foreach (Vector3 cam in near)
                            if (Flat(cam - centre) < radius + 1.5f && cam.y < high + top + 0.5f) return null;
            // Crowns reach at most about 14 m, so the buckets round this one hold every tree that could overhang it.
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (crowns.TryGetValue((cx + dx, cz + dz), out var trees))
                        foreach (var (at, crown) in trees)
                            if (Flat(at - centre) < radius + crown + 0.5f) return null;
            foreach (Vector3 end in armcoEnds)
                if (Flat(end - centre) < radius + 2f) return null;
            return new PropSpot { Kind = kind, Distance = d, Side = side, Base = centre, High = high, ToRoad = toRoad, Along = along };
        }

        /// <summary>Space kept between two props of these kinds, past their footprints.</summary>
        private static float Room(PropKind a, PropKind b) => a == PropKind.Hoarding && b == PropKind.Hoarding ? 0.2f : 6f;

        private bool NearProps(Vector3 p, float radius, PropKind kind)
        {
            foreach (PropSpot q in Spots)
            {
                float r = q.Kind == PropKind.Cabin ? 3.5f : q.Kind == PropKind.CameraTower ? 1.8f : 2.4f;
                float keep = q.Kind == kind ? 40f : Room(q.Kind, kind);
                if (Flat(q.Base - p) < radius + r + keep) return true;
            }
            return false;
        }

        /// <summary>True when p is within reach of the marshal posts, their panels, the boards or the gantry.</summary>
        private bool NearFurniture(Vector3 p, float radius, PropKind kind)
        {
            if (f == null) return false;
            float room = radius + (kind == PropKind.Hoarding ? 5f : 12f);
            foreach (MarshalPost post in f.Posts)
                if (Flat(post.Base - p) < room + 2f || Flat(post.Panel - p) < room) return true;
            foreach (DistanceBoard board in f.Boards)
                if (Flat(board.Base - p) < room) return true;
            return Flat(f.GantryLeft - p) < room + 10f || Flat(f.GantryRight - p) < room + 10f;
        }

        private bool InStand(int side, float d, float halfLength)
        {
            if (stands == null) return false;
            float margin = StandMargin + halfLength;
            foreach (StandFootprint stand in stands)
                if (stand.Side == side && c.Ahead(stand.From - margin, d) <= c.Ahead(stand.From - margin, stand.To + margin)) return true;
            return false;
        }

        /// <summary>
        /// True when p stands clear of every road and runoff, its own included, the pit lane, the paddock and the
        /// grandstands. Only samples within a few metres along the track of p count, so the test is a stretch's own width.
        /// </summary>
        private bool Clear(Vector3 p, int side)
        {
            var (bx, bz) = Key(p);
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (!samples.TryGetValue((bx + dx, bz + dz), out var list)) continue;
                    foreach (int j in list)
                    {
                        Vector3 v = p - c.Centre[j];
                        float along = v.x * c.Forward[j].x + v.z * c.Forward[j].z;
                        if (Mathf.Abs(along) > c.Step) continue;
                        float lat = v.x * c.Left[j].x + v.z * c.Left[j].z;
                        int s = lat > 0 ? 0 : 1;
                        float reach = c.HalfWidth(j) + Mathf.Max(b.RunoffReach(s, j), 2f);
                        if (Mathf.Abs(lat) < reach + RoadClearance) return false;
                        if (stands != null)
                            foreach (StandFootprint stand in stands)
                                if (stand.Covers(c.Lap, j * c.Step, lat)) return false;
                    }
                }
            float lane = c.Data.pit.laneWidth * 0.5f + c.Data.pit.garageDepth + RoadClearance;
            foreach (Vector3 q in c.Lane)
                if (Flat(p - q) < lane) return false;
            foreach (GarageData garage in c.Data.pit.garages)
            {
                float dx = p.x - garage.x, dz = p.z - garage.z;
                if (dx * dx + dz * dz < PaddockClear * PaddockClear) return false;
            }
            return true;
        }

        /// <summary>Where the broadcast director's trackside and pan cameras can stand, as it picks them.</summary>
        private void BroadcastSpots()
        {
            for (int a = 0; a < c.Count; a++)
            {
                float l = c.Data.clearLeft != null && a < c.Data.clearLeft.Length ? c.Data.clearLeft[a] : 10f;
                float r = c.Data.clearRight != null && a < c.Data.clearRight.Length ? c.Data.clearRight[a] : 10f;
                float side = l >= r ? 1f : -1f, clear = Mathf.Max(l, r);
                Camera(c.Centre[a] + c.Left[a] * side * (c.HalfWidth(a) + Mathf.Min(clear, 20f) + 4f) + Vector3.up * 4.5f);
                Camera(c.Centre[a] + c.Left[a] * side * (c.HalfWidth(a) + Mathf.Min(clear, 4f) + 2f) + Vector3.up * 1.5f);
            }
        }

        private void Camera(Vector3 p)
        {
            var key = Key(p);
            if (!cameras.TryGetValue(key, out var list)) cameras[key] = list = new List<Vector3>();
            list.Add(p);
        }

        private static (int, int) Key(Vector3 p) => (Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.z / Bucket));

        private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);
    }
}
