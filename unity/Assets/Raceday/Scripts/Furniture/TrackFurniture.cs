using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>One marshal post: where it stands, the stretch of track it flags, and which way its panel faces.</summary>
    public sealed class MarshalPost
    {
        /// <summary>Lap distance of the track beside the post. The post flags the track from here to the next post.</summary>
        public float Distance;
        /// <summary>0 for the left of the track, 1 for the right.</summary>
        public int Side;
        /// <summary>The middle of the post's floor, on the ground.</summary>
        public Vector3 Base;
        /// <summary>Flat unit vector the panel faces: back down the track, at the cars coming.</summary>
        public Vector3 Facing;
        /// <summary>Flat unit vector across the track, from the post towards the road.</summary>
        public Vector3 ToRoad;
        /// <summary>The foot of the flag panel's pole: just past the verge, where the drivers see it.</summary>
        public Vector3 Panel;
    }

    /// <summary>One 300, 200 or 100 board before a braking zone.</summary>
    public sealed class DistanceBoard
    {
        public float Distance;
        public int Metres;
        public int Side;
        public Vector3 Base;
        public Vector3 Facing;
    }

    /// <summary>
    /// Where the trackside furniture stands, worked out from the circuit and the runoff the
    /// <see cref="CircuitBuilder"/> laid, so it moves with the layout. Marshal posts stand behind the runoff, with
    /// their flag panels on poles just past the verge, before every corner and at most <see cref="PostGapMax"/> apart. Boards count down the last 300 m to
    /// every corner a car brakes hard for. The start gantry spans the start line.
    /// </summary>
    public sealed class TrackFurniture
    {
        /// <summary>Longest stretch one post flags.</summary>
        public const float PostGapMax = 360f;
        /// <summary>Posts closer than this to the one before are dropped, unless they guard a corner.</summary>
        public const float PostGapMin = 100f;
        /// <summary>A corner's post stands this far before its turn-in.</summary>
        public const float BeforeCorner = 40f;
        /// <summary>How far behind the barrier, or past the runoff's edge where there is none, a post stands.</summary>
        public const float BehindBarrier = 2.2f;
        /// <summary>A post's flag panel stands this far past the road edge, or in front of the barrier where the runoff is narrower.</summary>
        public const float PanelOut = 4.5f;
        /// <summary>A flag panel keeps this far from any distance board.</summary>
        public const float BoardRoom = 8f;
        /// <summary>Most a post's floor sits above or below the road beside it.</summary>
        public const float MaxDrop = 2.5f;
        /// <summary>A post never stands closer than this to any road, pit lane or runoff but its own.</summary>
        public const float PostClearance = 4f;
        /// <summary>A board's middle stands this far past the road edge, on the verge's outer side.</summary>
        public const float BoardOut = 3.6f;
        /// <summary>Corners at least this tight, after a run of at least <see cref="BrakingRunUp"/>, get boards.</summary>
        public const float BrakingRadius = 150f;
        public const float BrakingRunUp = 300f;
        public static readonly int[] BoardMetres = { 300, 200, 100 };
        /// <summary>Clear height under the gantry beam.</summary>
        public const float GantryClearance = 6.5f;
        /// <summary>The gantry's feet stand at most this far past the road edge.</summary>
        public const float GantryReach = 9f;
        /// <summary>A gantry foot by the pit wall stands this far in front of where the runoff meets it, clear of the wall.</summary>
        public const float PitWallClearance = 1.8f;
        /// <summary>A gantry foot behind the armco stands this far back, between a grandstand's catch fence and its front wall.</summary>
        public const float BehindArmco = 1.9f;

        /// <summary>Posts keep this far, along the track, from either end of a grandstand.</summary>
        public const float StandMargin = 10f;

        public readonly List<MarshalPost> Posts = new List<MarshalPost>();
        public readonly List<DistanceBoard> Boards = new List<DistanceBoard>();
        /// <summary>The start gantry: the sample it spans and its two feet, left then right.</summary>
        public int GantrySample;
        public Vector3 GantryLeft, GantryRight;

        private readonly Circuit c;
        private readonly CircuitBuilder b;
        private readonly IReadOnlyList<StandFootprint> stands;

        /// <param name="stands">The ground the grandstands take. Posts that would stand there move to the other side.</param>
        public TrackFurniture(Circuit circuit, CircuitBuilder built, IReadOnlyList<StandFootprint> stands)
        {
            c = circuit;
            b = built;
            this.stands = stands;
            PlaceBoards();
            PlacePosts();
            PlaceGantry();
        }

        // ---------------------------------------------------------------- marshal posts

        private void PlacePosts()
        {
            var seeds = new List<(float d, int side, bool corner)>();
            foreach (CornerData corner in c.Data.corners)
                seeds.Add((Mathf.Repeat(corner.s - BeforeCorner, c.Lap), Outside(corner), true));
            seeds.Sort((x, y) => x.d.CompareTo(y.d));

            // Drop posts crowding the one before, then fill the long stretches evenly.
            var kept = new List<(float d, int side, bool corner)>();
            foreach (var s in seeds)
                if (kept.Count == 0 || s.d - kept[kept.Count - 1].d >= PostGapMin) kept.Add(s);
            var all = new List<(float d, int side)>();
            for (int i = 0; i < kept.Count; i++)
            {
                all.Add((kept[i].d, kept[i].side));
                float gap = c.Ahead(kept[i].d, kept[(i + 1) % kept.Count].d);
                int extra = Mathf.CeilToInt(gap / PostGapMax) - 1;
                int next = kept[(i + 1) % kept.Count].side;
                for (int e = 1; e <= extra; e++) all.Add((Mathf.Repeat(kept[i].d + gap * e / (extra + 1), c.Lap), next));
            }

            foreach (var (d, side) in all)
            {
                MarshalPost post = PlacePost(d, side);
                if (post != null) Posts.Add(post);
            }
            Posts.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        }

        /// <summary>The side a corner runs wide to: left (0) for a right-hander.</summary>
        private static int Outside(CornerData corner) => corner.arcs[0].dir > 0 ? 0 : 1;

        /// <summary>A post near lap distance d, behind the barrier on the preferred side if it fits, else nearby.</summary>
        private MarshalPost PlacePost(float d, int side)
        {
            foreach (float shift in new[] { 0f, -10f, 10f, -20f, 20f, -30f, 30f, -45f, 45f, -60f, 60f, -80f, 80f, -110f, 110f, -140f, 140f })
                foreach (int s in new[] { side, 1 - side })
                {
                    float at = Mathf.Repeat(d + shift, c.Lap);
                    int k = c.IndexOf(at);
                    if (b.RunoffReach(s, k) <= 0f || b.PitWallAt(s, k) || InStand(s, at)) continue;
                    float sign = s == 0 ? 1f : -1f;
                    float lateral = sign * (c.HalfWidth(k) + b.RunoffReach(s, k) + BehindBarrier);
                    Vector3 p = c.Centre[k] + c.Left[k] * lateral;
                    if (!Clear(p, k, PostClearance)) continue;
                    p.y = c.TerrainHeight(p.x, p.z);
                    // Level enough with the road for the marshals to see it, not down an embankment or up a cutting.
                    if (Mathf.Abs(p.y - c.Centre[k].y) > MaxDrop) continue;
                    Vector3 toRoad = c.Left[k] * -sign;
                    // Square to the cars coming, turned a little towards the road so the panel reads from the line.
                    Vector3 facing = (-c.Forward[k] + toRoad * 0.35f).normalized;
                    float panelOut = Mathf.Min(PanelOut, b.RunoffReach(s, k) - 1f);
                    Vector3 panel = b.RunoffPoint(k, sign * (c.HalfWidth(k) + panelOut));
                    if (Boards.Exists(d => (d.Base - panel).sqrMagnitude < BoardRoom * BoardRoom)) continue;
                    return new MarshalPost { Distance = at, Side = s, Base = p, Facing = facing, ToRoad = toRoad, Panel = panel };
                }
            return null;
        }

        private bool InStand(int side, float d)
        {
            foreach (StandFootprint stand in stands)
                if (stand.Side == side && c.Ahead(stand.From - StandMargin, d) <= c.Ahead(stand.From - StandMargin, stand.To + StandMargin)) return true;
            return false;
        }

        /// <summary>
        /// True when p stands clear of every road, runoff and pit lane except the stretch beside track sample k:
        /// nothing of the furniture lands on another leg of the track.
        /// </summary>
        private bool Clear(Vector3 p, int k, float margin)
        {
            int near = Mathf.CeilToInt(60f / c.Step);
            for (int j = 0; j < c.Count; j++)
            {
                if (Mathf.Abs(c.Wrap(j - k + c.Count / 2) - c.Count / 2) <= near) continue;
                Vector3 v = p - c.Centre[j];
                float along = v.x * c.Forward[j].x + v.z * c.Forward[j].z;
                if (Mathf.Abs(along) > c.Step) continue;
                float lat = v.x * c.Left[j].x + v.z * c.Left[j].z;
                int s = lat > 0 ? 0 : 1;
                float reach = c.HalfWidth(j) + Mathf.Max(b.RunoffReach(s, j), 2f);
                if (Mathf.Abs(lat) < reach + margin) return false;
            }
            float lane = c.Data.pit.laneWidth * 0.5f + c.Data.pit.garageDepth + margin;
            foreach (Vector3 q in c.Lane)
            {
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < lane * lane) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- distance boards

        private void PlaceBoards()
        {
            CornerData[] corners = c.Data.corners;
            for (int i = 0; i < corners.Length; i++)
            {
                CornerData corner = corners[i];
                CornerData before = corners[(i + corners.Length - 1) % corners.Length];
                if (corner.radius > BrakingRadius || c.Ahead(before.e, corner.s) < BrakingRunUp) continue;
                int side = Outside(corner);
                // All three boards stand on one side, the outside if it has room along the whole run.
                bool fits = true;
                foreach (int m in BoardMetres) fits &= Room(side, c.IndexOf(corner.s - m));
                if (!fits) side = 1 - side;
                foreach (int m in BoardMetres)
                {
                    float d = Mathf.Repeat(corner.s - m, c.Lap);
                    int k = c.IndexOf(d);
                    if (!Room(side, k)) continue;
                    float sign = side == 0 ? 1f : -1f;
                    float reach = b.RunoffReach(side, k);
                    // On the verge's outer side, or just in front of the barrier where the runoff is narrow.
                    float out_ = Mathf.Min(BoardOut, reach - 0.9f);
                    Vector3 p = b.RunoffPoint(k, sign * (c.HalfWidth(k) + out_));
                    Boards.Add(new DistanceBoard { Distance = d, Metres = m, Side = side, Base = p, Facing = -c.Forward[k] });
                }
            }
        }

        /// <summary>True where a board fits on a side: runoff at least as deep as the verge plus a board.</summary>
        private bool Room(int side, int k) => b.RunoffReach(side, k) >= 3f && !b.PitWallAt(side, k);

        // ---------------------------------------------------------------- start gantry

        private void PlaceGantry()
        {
            GantrySample = c.IndexOf(0f);
            int k = GantrySample;
            GantryLeft = c.Centre[k] + c.Left[k] * Foot(0, k);
            GantryRight = c.Centre[k] - c.Left[k] * Foot(1, k);
            GantryLeft.y = FootHeight(0, k, GantryLeft);
            GantryRight.y = FootHeight(1, k, GantryRight);
        }

        /// <summary>How far out from the centre a gantry foot stands: on the grass in front of the pit wall, behind the armco, or on the runoff.</summary>
        private float Foot(int side, int k)
        {
            float edge = c.HalfWidth(k);
            float reach = b.RunoffReach(side, k);
            if (b.PitWallAt(side, k)) return edge + reach - PitWallClearance;
            if (b.Fenced(side, k) && reach + BehindArmco <= GantryReach) return edge + reach + BehindArmco;
            return edge + Mathf.Min(Mathf.Max(reach, 2f), GantryReach - 1f);
        }

        /// <summary>The ground under a foot: the ground behind the armco, or the runoff.</summary>
        private float FootHeight(int side, int k, Vector3 p)
        {
            float sign = side == 0 ? 1f : -1f;
            float beyond = Mathf.Abs(Vector3.Dot(p - c.Centre[k], c.Left[k])) - c.HalfWidth(k);
            if (beyond >= b.RunoffReach(side, k)) return c.TerrainHeight(p.x, p.z);
            return b.RunoffPoint(k, sign * (c.HalfWidth(k) + beyond)).y;
        }
    }
}
