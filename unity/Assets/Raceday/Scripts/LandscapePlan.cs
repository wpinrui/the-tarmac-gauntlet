using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How the country round the circuit looks: how much of it is forest, how much of the forest is
    /// conifer, how many fields have hedges round them, how often a field has a copse in it, and how
    /// tall the hills on the horizon stand.
    /// </summary>
    public struct LandscapeLook
    {
        /// <summary>Share of the land that is forest, 0 to 1.</summary>
        public float Forest;
        /// <summary>Share of forest trees that are conifers, 0 to 1.</summary>
        public float Conifer;
        /// <summary>Share of field edges with a hedge, 0 to 1.</summary>
        public float Hedges;
        /// <summary>Share of fields with a copse in them, 0 to 1.</summary>
        public float Copses;
        /// <summary>Height of the tallest hills on the horizon, m.</summary>
        public float Hills;
    }

    /// <summary>The kinds of tree, in the order of their impostor atlases.</summary>
    /// <summary>A square of trees drawn as one mesh, near and far versions (<see cref="LandscapePlan.Chunks"/>).</summary>
    public sealed class TreeChunk
    {
        public List<TreeSpot> Near, Far;
    }

    public enum TreeKind { Oak, Beech, Birch, PineA, PineB, SpruceA, SpruceB, Bush }

    /// <summary>One tree or hedge bush: where it stands, how big it is and which way it faces.</summary>
    public struct TreeSpot
    {
        /// <summary>Foot of the trunk, on the ground.</summary>
        public Vector3 Base;
        /// <summary>Scale of the baked model.</summary>
        public float Scale;
        /// <summary>Height squash: below 1 for hedge bushes, which are wider than tall.</summary>
        public float Squash;
        /// <summary>Turn about the vertical, radians.</summary>
        public float Yaw;
        public TreeKind Kind;
        /// <summary>Leaf colour variation, about 1.</summary>
        public float Shade;
        /// <summary>Leaf hue, -1 cool and blue to 1 warm and yellow, 0 the kind's own (<see cref="LandscapeLooks.Leaves"/>).</summary>
        public float Hue;
    }

    /// <summary>What a stretch of land is used for.</summary>
    public enum LandUse : byte { Mown, Meadow, Pasture, Wheat, Hay, Forest }

    /// <summary>
    /// Lays out the country round the circuit from its data and a look: fields in irregular parcels,
    /// some with hedges round them, forest in others, copses and lone trees in the fields, and a few
    /// trees and bushes standing forward of the rest in the mown grass. Nothing stands on the track,
    /// its runoff or the 15 m behind the armco, or round the paddock, and only those few within 35 m;
    /// none of those on the inside of a bend, where a camera up the track looks across it. Placement
    /// is repeatable: the same circuit and look give the same trees. Free of Unity's runtime, so the
    /// offline checks and previews use it too.
    /// </summary>
    public sealed class LandscapePlan
    {
        /// <summary>The woods, hedges and fields' trees stand no closer to the armco than this, m.</summary>
        public const float KeepClear = 35f;
        /// <summary>The few trees standing forward of the rest (<see cref="Fringe"/>) stand no closer than this, m.</summary>
        public const float NearClear = 15f;
        /// <summary>No tree stands closer than <see cref="KeepClear"/> on the inside of a bend tighter than this radius, m.</summary>
        public const float InsideRadius = 900f;
        /// <summary>Nothing stands within this of a garage on the pit side: the paddock and pit building, m.</summary>
        public const float PaddockClear = 120f;
        /// <summary>Spacing of the parcel seeds, m.</summary>
        private const float ParcelSize = 170f;
        /// <summary>Spacing of trees in a forest, m.</summary>
        private const float ForestSpacing = 7.5f;
        /// <summary>Share of trees kept deep inside a wood, and how much bigger each grows to fill in.</summary>
        private const float InteriorKeep = 0.5f, InteriorGrow = 1.25f;
        /// <summary>Step along field edges for hedge bushes, m.</summary>
        private const float HedgeStep = 2.6f;
        /// <summary>Trees are drawn in square chunks this wide, each one draw call, so the camera skips those off screen, m.</summary>
        public const float ChunkSize = 250f;
        /// <summary>Field map texels per terrain cell.</summary>
        public const int MapPerCell = 2;

        /// <summary>Baked model sizes, from tools/landscape/trees.json: height and the radius of the impostor quad, m.</summary>
        public static readonly float[] ModelHeight = { 5.04f, 3.41f, 4.57f, 18.92f, 14.08f, 8.85f, 7.82f, 2.17f };
        public static readonly float[] ModelRadius = { 3.19f, 2.89f, 2.65f, 9.60f, 7.17f, 4.53f, 4.03f, 2.63f };
        /// <summary>Baked tree's middle above its foot, by kind, m (tools/landscape/trees.json).</summary>
        public static readonly float[] CentreAbove = { 2.52f, 1.71f, 2.28f, 9.46f, 7.04f, 4.43f, 3.91f, 1.09f };
        /// <summary>A chunk's far version keeps one tree in this many and grows each to cover for the rest.</summary>
        public const int FarKeep = 3;
        public const float FarGrow = 1.45f;

        public readonly List<TreeSpot> Trees = new List<TreeSpot>();
        /// <summary>Land use on a grid <see cref="MapPerCell"/> times finer than the terrain's, row by row along x.</summary>
        public readonly LandUse[] Map;
        public readonly int MapX, MapZ;

        private readonly Circuit c;
        private readonly Func<int, int, float> reach;
        private readonly LandscapeLook look;
        private readonly IReadOnlyList<StandFootprint> stands;
        private readonly int seed;
        private readonly GroundData g;
        private readonly float minX, minZ, maxX, maxZ;

        /// <summary>Distance fields are worked out this far past the armco; anything further reads as this.</summary>
        private const float FieldReach = 160f;
        // On the map's grid: distance beyond the nearest armco (or runoff edge), the side of the track
        // that is on (+1 left), and where nothing may stand.
        private readonly float[] edge;
        private readonly sbyte[] sideOf;
        // Distance beyond the nearest road edge, and that stretch of road's sample (-1 out of reach).
        private readonly float[] road;
        private readonly int[] roadAt;
        private readonly bool[] blocked;
        private readonly float cell;

        // Parcel seeds on a jittered grid.
        private readonly int seedsX, seedsZ;
        private readonly Vector2[] seeds;
        private readonly LandUse[] uses;
        private readonly float[] conifer;

        /// <param name="runoffReach">How far out from the road edge the runoff reaches on a side (0 = left) at a sample: <see cref="CircuitBuilder.RunoffReach"/>.</param>
        /// <param name="stands">The grandstands' footprints (<see cref="Grandstands.Footprints"/>): no tree stands forward in front of or beside one.</param>
        public LandscapePlan(Circuit circuit, Func<int, int, float> runoffReach, LandscapeLook look, IReadOnlyList<StandFootprint> stands = null, int seed = 1)
        {
            c = circuit;
            reach = runoffReach;
            this.look = look;
            this.stands = stands;
            this.seed = seed;
            g = c.Data.terrain;
            minX = g.originX;
            minZ = g.originZ;
            maxX = g.originX + (g.countX - 1) * g.spacing;
            maxZ = g.originZ + (g.countZ - 1) * g.spacing;
            MapX = (g.countX - 1) * MapPerCell + 1;
            MapZ = (g.countZ - 1) * MapPerCell + 1;
            cell = g.spacing / MapPerCell;
            edge = new float[MapX * MapZ];
            sideOf = new sbyte[edge.Length];
            road = new float[edge.Length];
            roadAt = new int[edge.Length];
            blocked = new bool[edge.Length];
            EdgeField();
            BlockField();

            seedsX = Mathf.CeilToInt((maxX - minX) / ParcelSize) + 2;
            seedsZ = Mathf.CeilToInt((maxZ - minZ) / ParcelSize) + 2;
            seeds = new Vector2[seedsX * seedsZ];
            uses = new LandUse[seeds.Length];
            conifer = new float[seeds.Length];
            for (int j = 0; j < seedsZ; j++)
                for (int i = 0; i < seedsX; i++)
                {
                    int s = j * seedsX + i;
                    seeds[s] = new Vector2(minX + (i - 0.5f + 0.8f * Rand(i, j, 1)) * ParcelSize, minZ + (j - 0.5f + 0.8f * Rand(i, j, 2)) * ParcelSize);
                    uses[s] = Use(seeds[s], i, j);
                    conifer[s] = Mathf.Clamp01(look.Conifer + (Rand(i, j, 3) - 0.5f) * 0.6f);
                }

            Map = new LandUse[MapX * MapZ];
            for (int z = 0; z < MapZ; z++)
                for (int x = 0; x < MapX; x++)
                {
                    int i = z * MapX + x;
                    // Mown grass round the track and the paddock, out to where things may stand.
                    Map[i] = edge[i] < KeepClear - 5f || blocked[i] && edge[i] < FieldReach ? LandUse.Mown : uses[Parcel(minX + x * cell, minZ + z * cell, out _, out _)];
                }

            Forests();
            Hedges();
            Fields();
            Fringe();
        }

        // ---------------------------------------------------------------- land use

        private LandUse Use(Vector2 p, int i, int j)
        {
            // Forest is likelier further from the track and on higher ground.
            float away = Mathf.Clamp01((EdgeDistance(p.x, p.y) - 40f) / (FieldReach - 40f));
            float high = Mathf.Clamp01((c.UntouchedHeight(p.x, p.y) - c.Centre[0].y - 5f) / 30f);
            float forest = look.Forest * (0.6f + 0.5f * away + 0.35f * high);
            if (Rand(i, j, 4) < forest) return LandUse.Forest;
            float r = Rand(i, j, 5);
            return r < 0.3f ? LandUse.Meadow : r < 0.55f ? LandUse.Pasture : r < 0.8f ? LandUse.Wheat : LandUse.Hay;
        }

        /// <summary>
        /// The parcel a point lies in, from the nearest seed after a gentle warp so edges wander, with
        /// the distances to the nearest and second-nearest seeds.
        /// </summary>
        private int Parcel(float x, float z, out float d1, out float d2)
        {
            float wx = x + 28f * (Noise(x / 140f, z / 140f, 7) - 0.5f);
            float wz = z + 28f * (Noise(x / 140f, z / 140f, 8) - 0.5f);
            int ci = Mathf.FloorToInt((wx - minX) / ParcelSize + 0.5f), cj = Mathf.FloorToInt((wz - minZ) / ParcelSize + 0.5f);
            int best = 0;
            d1 = d2 = float.MaxValue;
            for (int j = cj - 1; j <= cj + 1; j++)
                for (int i = ci - 1; i <= ci + 1; i++)
                {
                    if (i < 0 || j < 0 || i >= seedsX || j >= seedsZ) continue;
                    int s = j * seedsX + i;
                    float dx = seeds[s].x - wx, dz = seeds[s].y - wz;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < d1) { d2 = d1; d1 = d; best = s; }
                    else if (d < d2) d2 = d;
                }
            return best;
        }

        // ---------------------------------------------------------------- keeping clear

        /// <summary>
        /// Works out, for every map point within reach of the track, how far it is beyond the armco
        /// (or the runoff's edge where there is no armco) of the nearest stretch of track, and on
        /// which side. Each track sample marks the points round it.
        /// </summary>
        private void EdgeField()
        {
            for (int i = 0; i < edge.Length; i++) { edge[i] = road[i] = FieldReach; sideOf[i] = 1; roadAt[i] = -1; }
            for (int k = 0; k < c.Count; k++)
            {
                Vector3 p = c.Centre[k];
                float half = c.HalfWidth(k), left = reach(0, k), right = reach(1, k);
                float r = half + Mathf.Max(left, right) + FieldReach;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((p.x - r - minX) / cell)), x1 = Mathf.Min(MapX - 1, Mathf.CeilToInt((p.x + r - minX) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((p.z - r - minZ) / cell)), z1 = Mathf.Min(MapZ - 1, Mathf.CeilToInt((p.z + r - minZ) / cell));
                for (int z = z0; z <= z1; z++)
                {
                    float dz = minZ + z * cell - p.z;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = minX + x * cell - p.x;
                        float lat = dx * c.Left[k].x + dz * c.Left[k].z;
                        float r0 = Mathf.Sqrt(dx * dx + dz * dz) - half, e = r0 - (lat >= 0f ? left : right);
                        int i = z * MapX + x;
                        if (e < edge[i]) { edge[i] = e; sideOf[i] = (sbyte)(lat >= 0f ? 1 : -1); }
                        if (r0 < road[i]) { road[i] = r0; roadAt[i] = k; }
                    }
                }
            }
        }

        /// <summary>Marks the pit lane's edges and the paddock behind the garages, where nothing may stand.</summary>
        private void BlockField()
        {
            float half = c.Data.pit.laneWidth * 0.5f + KeepClear;
            foreach (Vector3 q in c.Lane) Mark(q.x, q.z, half, 0);
            int pitSide = c.Data.pit.side == "right" ? -1 : 1;
            foreach (GarageData garage in c.Data.pit.garages) Mark(garage.x, garage.z, PaddockClear, pitSide);
        }

        /// <summary>Blocks the map points within a radius of a point, only on one side of the track if <paramref name="side"/> is not 0.</summary>
        private void Mark(float px, float pz, float radius, int side)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((px - radius - minX) / cell)), x1 = Mathf.Min(MapX - 1, Mathf.CeilToInt((px + radius - minX) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((pz - radius - minZ) / cell)), z1 = Mathf.Min(MapZ - 1, Mathf.CeilToInt((pz + radius - minZ) / cell));
            // Grown by a little over half a cell's diagonal, so a point between map points is covered too.
            float grown = radius + cell * 0.75f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = minX + x * cell - px, dz = minZ + z * cell - pz;
                    int i = z * MapX + x;
                    if (dx * dx + dz * dz < grown * grown && (side == 0 || sideOf[i] == side)) blocked[i] = true;
                }
        }

        private int Nearest(float x, float z) =>
            Mathf.Clamp(Mathf.RoundToInt((z - minZ) / cell), 0, MapZ - 1) * MapX + Mathf.Clamp(Mathf.RoundToInt((x - minX) / cell), 0, MapX - 1);

        /// <summary>How far a point is beyond the nearest armco, up to <see cref="FieldReach"/>, to within a map cell.</summary>
        public float EdgeDistance(float x, float z) => edge[Nearest(x, z)];

        /// <summary>
        /// The trees in their chunks, each with a near version and a thinner far one, nearest the track first: the
        /// cameras are on or by the track, so those trees are usually in front, and the ones behind fail the depth test.
        /// </summary>
        public List<TreeChunk> Chunks()
        {
            var byKey = new Dictionary<long, List<TreeSpot>>();
            var keys = new List<long>();
            foreach (TreeSpot t in Trees)
            {
                long key = ((long)Mathf.FloorToInt(t.Base.x / ChunkSize) << 32) ^ (uint)Mathf.FloorToInt(t.Base.z / ChunkSize);
                if (!byKey.TryGetValue(key, out var list)) { byKey[key] = list = new List<TreeSpot>(); keys.Add(key); }
                list.Add(t);
            }
            var chunks = new List<TreeChunk>(keys.Count);
            foreach (long key in keys)
            {
                List<TreeSpot> near = byKey[key];
                var order = new float[near.Count];
                for (int i = 0; i < near.Count; i++) order[i] = EdgeDistance(near[i].Base.x, near[i].Base.z);
                TreeSpot[] sorted = near.ToArray();
                Array.Sort(order, sorted);
                near = new List<TreeSpot>(sorted);
                var far = new List<TreeSpot>();
                for (int i = 0; i < near.Count; i += FarKeep)
                {
                    TreeSpot t = near[i];
                    t.Scale *= FarGrow;
                    if (t.Kind == TreeKind.Bush) t.Squash /= FarGrow;   // wider, not taller
                    far.Add(t);
                }
                chunks.Add(new TreeChunk { Near = near, Far = far });
            }
            return chunks;
        }

        /// <summary>True where nothing may stand.</summary>
        public bool Kept(float x, float z)
        {
            if (x < minX + 4f || z < minZ + 4f || x > maxX - 4f || z > maxZ - 4f) return true;
            int i = Nearest(x, z);
            // The distance changes by at most a metre a metre, and the nearest map point is within
            // half a cell's diagonal.
            return blocked[i] || edge[i] < KeepClear + cell * 0.75f;
        }

        /// <summary>Width of one mower pass, one stripe, m.</summary>
        public const float StripeWidth = 5f;

        /// <summary>
        /// The mowing at a map point: how far it is beyond the nearest road edge, m; how far out from the road the grass
        /// is mown there, m, 0 where none is; how fresh the stripes are, 0.5 to 1; and which way that stretch of road
        /// runs, across the ground. Stripes run nearly straight along the road, a pass at a time. Some stretches are mown a few
        /// passes out and left long beyond, others all the way, and some were mown longer ago, so their stripes are faint.
        /// </summary>
        public void Mowing(int i, out float fromRoad, out float reach, out float fresh, out float alongX, out float alongZ)
        {
            int k = roadAt[i];
            float x = minX + (i % MapX) * cell, z = minZ + (i / MapX) * cell;
            // Nearly straight: the passes drift a little either way over tens of metres, as a mower steered by eye does.
            fromRoad = Mathf.Max(road[i], 0f) * (1f + 0.05f * (Noise(x / 200f, z / 200f, 166) - 0.5f))
                + 2.6f * (Noise(x / 60f, z / 60f, 162) - 0.5f);
            reach = fresh = alongX = alongZ = 0f;
            if (k < 0 || Map[i] != LandUse.Mown) return;
            // Left is level and square to the road, so the road runs along (left.z, -left.x).
            alongX = c.Left[k].z;
            alongZ = -c.Left[k].x;
            float far = Noise(x / 220f, z / 220f, 165);
            reach = far > 0.6f ? FieldReach * 2f : StripeWidth * (2 + Mathf.FloorToInt(far * 12f));
            fresh = Mathf.Clamp(0.5f + 1.5f * (Noise(x / 130f, z / 130f, 164) - 0.35f), 0.5f, 1f);
        }

        /// <summary>
        /// True if a point lies on the inside of a bend tighter than <see cref="InsideRadius"/>, measured over 80 m either
        /// side of the nearest stretch of road: a trackside camera looks up the track across there.
        /// </summary>
        public bool InsideBend(float x, float z)
        {
            int i = Nearest(x, z), k = roadAt[i];
            if (k < 0) return false;
            int n = Mathf.Max(1, Mathf.RoundToInt(80f / c.Step));
            Vector3 mid = (c.Centre[c.Wrap(k - n)] + c.Centre[c.Wrap(k + n)]) * 0.5f - c.Centre[k];
            // The chord's middle lies towards the inside; how far gives the radius.
            float inward = mid.x * c.Left[k].x + mid.z * c.Left[k].z;
            float sag = Mathf.Abs(inward), half = n * c.Step;
            if (sag < 1e-3f || half * half / (2f * sag) >= InsideRadius) return false;
            float lat = (x - c.Centre[k].x) * c.Left[k].x + (z - c.Centre[k].z) * c.Left[k].z;
            return lat * inward > 0f;
        }

        // ---------------------------------------------------------------- planting

        private void Forests()
        {
            int nx = Mathf.CeilToInt((maxX - minX) / ForestSpacing), nz = Mathf.CeilToInt((maxZ - minZ) / ForestSpacing);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = minX + (i + 0.15f + 0.7f * Rand(i, j, 11)) * ForestSpacing;
                    float z = minZ + (j + 0.15f + 0.7f * Rand(i, j, 12)) * ForestSpacing;
                    int s = Parcel(x, z, out float d1, out float d2);
                    if (uses[s] != LandUse.Forest) continue;
                    // A ragged edge: trees thin out over the last few metres.
                    if ((d2 - d1) * 0.5f < 6f * Rand(i, j, 13)) continue;
                    if (Kept(x, z)) continue;
                    // Deep inside a wood only the crowns show, so fewer, bigger trees do; at the edges
                    // and near the track every tree stays.
                    bool deep = (d2 - d1) * 0.5f > 30f && EdgeDistance(x, z) > 90f;
                    if (deep && Rand(i, j, 16) > InteriorKeep) continue;
                    bool needle = Rand(i, j, 14) < conifer[s];
                    TreeKind kind = needle ? (TreeKind)((int)TreeKind.PineA + (int)(Rand(i, j, 15) * 3.999f))
                        : (TreeKind)(int)(Rand(i, j, 15) * 2.999f);
                    Plant(x, z, kind, deep ? 1.05f * InteriorGrow : 1.05f, i, j);
                }
        }

        private void Hedges()
        {
            if (look.Hedges <= 0f) return;
            int nx = Mathf.CeilToInt((maxX - minX) / HedgeStep), nz = Mathf.CeilToInt((maxZ - minZ) / HedgeStep);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = minX + i * HedgeStep, z = minZ + j * HedgeStep;
                    int s = Parcel(x, z, out float d1, out float d2);
                    // On the line between two parcels, measured across it.
                    if ((d2 - d1) * 0.5f > HedgeStep * 0.55f) continue;
                    if (uses[s] == LandUse.Forest) continue;
                    int other = Neighbour(x, z, s);
                    if (other < 0 || uses[other] == LandUse.Forest) continue;
                    if (Rand(Math.Min(s, other), Math.Max(s, other), 21) >= look.Hedges) continue;
                    if (Kept(x, z)) continue;
                    // Now and then a hedgerow tree grows out of the hedge.
                    if (Rand(i, j, 22) < 0.035f) Plant(x, z, Rand(i, j, 23) < 0.6f ? TreeKind.Oak : TreeKind.Beech, 1f, i, j);
                    else Bush(x, z, i, j);
                }
        }

        /// <summary>The second-nearest parcel to a point: the one across the edge from it.</summary>
        private int Neighbour(float x, float z, int self)
        {
            float wx = x + 28f * (Noise(x / 140f, z / 140f, 7) - 0.5f);
            float wz = z + 28f * (Noise(x / 140f, z / 140f, 8) - 0.5f);
            int ci = Mathf.FloorToInt((wx - minX) / ParcelSize + 0.5f), cj = Mathf.FloorToInt((wz - minZ) / ParcelSize + 0.5f);
            int best = -1;
            float bd = float.MaxValue;
            for (int j = cj - 1; j <= cj + 1; j++)
                for (int i = ci - 1; i <= ci + 1; i++)
                {
                    if (i < 0 || j < 0 || i >= seedsX || j >= seedsZ) continue;
                    int s = j * seedsX + i;
                    if (s == self) continue;
                    float d = (seeds[s] - new Vector2(wx, wz)).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; }
                }
            return best;
        }

        /// <summary>Copses and lone trees out in the fields.</summary>
        private void Fields()
        {
            for (int s = 0; s < seeds.Length; s++)
            {
                if (uses[s] == LandUse.Forest) continue;
                int i = s % seedsX, j = s / seedsX;
                if (Rand(i, j, 31) < look.Copses)
                {
                    float cx = seeds[s].x + (Rand(i, j, 32) - 0.5f) * 60f, cz = seeds[s].y + (Rand(i, j, 33) - 0.5f) * 60f;
                    float radius = 12f + 20f * Rand(i, j, 34);
                    int count = (int)(radius * radius / 30f);
                    bool needle = Rand(i, j, 35) < look.Conifer * 0.6f;
                    for (int t = 0; t < count; t++)
                    {
                        float a = Rand(s, t, 36) * Mathf.PI * 2f, r = radius * Mathf.Sqrt(Rand(s, t, 37));
                        float x = cx + Mathf.Cos(a) * r, z = cz + Mathf.Sin(a) * r;
                        if (Parcel(x, z, out _, out _) != s || Kept(x, z)) continue;
                        TreeKind kind = needle && Rand(s, t, 38) < 0.7f ? (TreeKind)((int)TreeKind.SpruceA + (int)(Rand(s, t, 39) * 1.999f))
                            : (TreeKind)(int)(Rand(s, t, 39) * 2.999f);
                        Plant(x, z, kind, 1f, s, t + 1000);
                    }
                    // Scrub round the copse's edge.
                    for (int t = 0; t < count / 2; t++)
                    {
                        float a = Rand(s, t, 40) * Mathf.PI * 2f, r = radius * (0.85f + 0.3f * Rand(s, t, 41));
                        float x = cx + Mathf.Cos(a) * r, z = cz + Mathf.Sin(a) * r;
                        if (Parcel(x, z, out _, out _) != s || Kept(x, z)) continue;
                        Bush(x, z, s, t + 2000);
                    }
                }
                int lone = (int)(Rand(i, j, 42) * 3.5f);
                for (int t = 0; t < lone; t++)
                {
                    float x = seeds[s].x + (Rand(s, t, 43) - 0.5f) * 120f, z = seeds[s].y + (Rand(s, t, 44) - 0.5f) * 120f;
                    if (Parcel(x, z, out float d1, out float d2) != s || (d2 - d1) * 0.5f < 15f || Kept(x, z)) continue;
                    Plant(x, z, Rand(s, t, 45) < 0.7f ? TreeKind.Oak : TreeKind.Beech, 1.1f, s, t + 3000);
                }
            }
        }

        /// <summary>
        /// A few trees and bushes standing forward of the rest, in the mown grass between <see cref="NearClear"/> and
        /// <see cref="KeepClear"/> behind the armco, in loose clumps, so the tree line has layers in front of it rather
        /// than standing as one wall. None on the inside of a tight bend.
        /// </summary>
        private void Fringe()
        {
            const float step = 9f;
            int nx = Mathf.CeilToInt((maxX - minX) / step), nz = Mathf.CeilToInt((maxZ - minZ) / step);
            float trees = Mathf.Clamp01(0.35f + look.Forest * 0.5f + look.Copses * 0.3f);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = minX + (i + 0.1f + 0.8f * Rand(i, j, 71)) * step;
                    float z = minZ + (j + 0.1f + 0.8f * Rand(i, j, 72)) * step;
                    if (x < minX + 4f || z < minZ + 4f || x > maxX - 4f || z > maxZ - 4f) continue;
                    int m = Nearest(x, z);
                    float e = edge[m];
                    if (blocked[m] || e < NearClear + cell * 0.75f || e >= KeepClear + cell * 0.75f) continue;
                    // Clumps: only where a broad noise runs high, thicker the higher it runs.
                    float clump = (Noise(x / 60f, z / 60f, 73) - 0.45f) / 0.3f;
                    if (clump <= 0f || Rand(i, j, 74) > clump * 0.9f * trees) continue;
                    if (InsideBend(x, z) || ByStand(x, z)) continue;
                    float r = Rand(i, j, 75);
                    if (r < 0.3f) { Bush(x, z, i, j + 5000); continue; }
                    TreeKind kind = Rand(i, j, 76) < look.Conifer * 0.7f ? (TreeKind)((int)TreeKind.SpruceA + (int)(Rand(i, j, 77) * 1.999f))
                        : (TreeKind)(int)(Rand(i, j, 77) * 2.999f);
                    // Younger and smaller than the woods behind, so they read as a layer in front.
                    Plant(x, z, kind, 0.6f + 0.35f * Rand(i, j, 78), i, j + 5000);
                }
        }

        /// <summary>Room left clear along the track either side of a grandstand, so the trees stand off its ends, m.</summary>
        private const float StandRoom = 40f;

        /// <summary>True if a point is level with a grandstand, or within <see cref="StandRoom"/> of either end, on its side of the track.</summary>
        private bool ByStand(float x, float z)
        {
            if (stands == null) return false;
            int k = roadAt[Nearest(x, z)];
            if (k < 0) return false;
            float lat = (x - c.Centre[k].x) * c.Left[k].x + (z - c.Centre[k].z) * c.Left[k].z;
            foreach (StandFootprint f in stands)
                if ((f.Side == 0) == (lat > 0f)
                    && Mathf.Repeat(k * c.Step - f.From + StandRoom, c.Lap) <= Mathf.Repeat(f.To - f.From, c.Lap) + 2f * StandRoom)
                    return true;
            return false;
        }

        /// <summary>Grown heights by kind, smallest and largest, m.</summary>
        private static readonly float[] MinHeight = { 13f, 15f, 12f, 20f, 16f, 15f, 14f, 1.6f };
        private static readonly float[] MaxHeight = { 21f, 24f, 18f, 27f, 23f, 24f, 22f, 2.6f };

        private void Plant(float x, float z, TreeKind kind, float grow, int i, int j)
        {
            int k = (int)kind;
            // Stands of different ages, so the skyline rises and falls, with now and then a sapling or a
            // tree standing well above its neighbours.
            float age = 0.7f + 0.55f * Noise(x / 110f, z / 110f, 54);
            float odd = Rand(i, j, 55);
            float one = odd < 0.08f ? 0.55f : odd > 0.95f ? 1.3f : 1f;
            float height = Mathf.Lerp(MinHeight[k], MaxHeight[k], Rand(i, j, 51)) * grow * age * one;
            Trees.Add(new TreeSpot
            {
                Base = new Vector3(x, c.TerrainHeight(x, z) - 0.15f, z),
                Scale = height / ModelHeight[k],
                Squash = 1f,
                Yaw = Rand(i, j, 52) * Mathf.PI * 2f,
                Kind = kind,
                Shade = ShadeAt(x, z, i, j, 53),
                Hue = HueAt(x, z, i, j, 56),
            });
        }

        private void Bush(float x, float z, int i, int j)
        {
            int k = (int)TreeKind.Bush;
            float width = 3.2f + 1.4f * Rand(i, j, 61);
            float height = Mathf.Lerp(MinHeight[k], MaxHeight[k], Rand(i, j, 62));
            float scale = width / 4.12f;   // the baked bush's crown is 4.12 m across
            Trees.Add(new TreeSpot
            {
                Base = new Vector3(x, c.TerrainHeight(x, z) - 0.25f, z),
                Scale = scale,
                Squash = height / (ModelHeight[k] * scale),
                Yaw = Rand(i, j, 63) * Mathf.PI * 2f,
                Kind = TreeKind.Bush,
                Shade = ShadeAt(x, z, i, j, 64),
                Hue = HueAt(x, z, i, j, 65),
            });
        }

        /// <summary>Leaf shade: lighter and darker stands, and each tree a little different.</summary>
        private float ShadeAt(float x, float z, int i, int j, int salt) =>
            (0.82f + 0.36f * Noise(x / 80f, z / 80f, 57)) * (0.86f + 0.28f * Rand(i, j, salt));

        /// <summary>Leaf hue: warmer and cooler stands, and each tree a little different.</summary>
        private float HueAt(float x, float z, int i, int j, int salt) =>
            Mathf.Clamp((Noise(x / 150f, z / 150f, 58) - 0.5f) * 2.6f + (Rand(i, j, salt) - 0.5f) * 0.6f, -1f, 1f);

        // ---------------------------------------------------------------- noise

        private float Rand(int i, int j, int salt) => CloudField.Hash(i, j, seed * 7919 + salt);

        /// <summary>Smooth value noise, 0 to 1.</summary>
        public static float Noise(float x, float z, int salt)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float tx = x - ix, tz = z - iz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = CloudField.Hash(ix, iz, salt), b = CloudField.Hash(ix + 1, iz, salt);
            float d = CloudField.Hash(ix, iz + 1, salt), e = CloudField.Hash(ix + 1, iz + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(d, e, tx), tz);
        }
    }
}
