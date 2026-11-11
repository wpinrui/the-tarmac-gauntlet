using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the meshes of the trackside furniture where <see cref="TrackFurniture"/> placed it: the start gantry
    /// and its lights, the marshal posts and their flag panels, and the distance boards. Static pieces come out as
    /// <see cref="CircuitPart"/>s cut into chunks, like the track. The lights and each post's panel come out on
    /// their own, because race control switches them.
    /// </summary>
    public sealed class FurnitureBuilder
    {
        public enum GantryStyle { Truss, Concrete, Box }
        public enum PostStyle { Roofed, Hut }

        /// <summary>The looks to build. Tools change them to preview the others.</summary>
        public static GantryStyle Gantry = GantryStyle.Truss;
        public static PostStyle Post = PostStyle.Roofed;

        private const float ChunkSize = 250f;

        // Start gantry, metres.
        private const float BeamDepth = 1.6f;        // truss: top chord to bottom chord
        private const float LightPitch = 0.62f;      // between light columns
        private const int LightColumns = 5;
        private const float LensRadius = 0.2f;
        // Marshal posts.
        private const float PostLength = 3.2f;       // along the track
        private const float PostDepth = 2.2f;        // across it
        private const float SlabHeight = 0.3f;
        private const float PanelSize = 1.0f;
        private const float PanelHeight = 2.4f;      // panel centre above the ground
        // Distance boards.
        private const float BoardWidth = 1.5f;
        private const float BoardHeight = 1.1f;
        private const float BoardLift = 0.9f;        // ground to the board's bottom edge

        /// <summary>Each board's face covers this column of the board texture: 300, 200, 100 from the left.</summary>
        public static int BoardColumn(int metres) => metres == 300 ? 0 : metres == 200 ? 1 : 2;

        public readonly List<CircuitPart> Parts = new List<CircuitPart>();
        /// <summary>The start lights' lenses, facing the grid.</summary>
        public readonly MeshKit RedLenses = new MeshKit(), GreenLenses = new MeshKit();
        /// <summary>Each post's flag panel face, in post order.</summary>
        public readonly List<MeshKit> Panels = new List<MeshKit>();
        /// <summary>Where the marshals stand: feet, the way they face, and which pose they hold.</summary>
        public readonly List<(Vector3 feet, Vector3 facing, int pose)> Marshals = new List<(Vector3, Vector3, int)>();

        private readonly Circuit c;
        private readonly TrackFurniture f;
        private readonly FurnitureMaterials m;
        private readonly Dictionary<string, (MeshKit kit, Material material)> kits = new Dictionary<string, (MeshKit, Material)>();

        /// <param name="materials">May be null outside Unity.</param>
        public FurnitureBuilder(Circuit circuit, TrackFurniture furniture, FurnitureMaterials materials)
        {
            c = circuit;
            f = furniture;
            m = materials;
            BuildGantry();
            foreach (MarshalPost post in f.Posts) BuildPost(post);
            foreach (DistanceBoard board in f.Boards) BuildBoard(board);
            foreach (var pair in kits)
                foreach (MeshKit chunk in pair.Value.kit.Chunks(ChunkSize))
                    if (!chunk.IsEmpty) Parts.Add(new CircuitPart(pair.Key, chunk, pair.Value.material, false, true));
        }

        private MeshKit Kit(string name, Material material)
        {
            if (!kits.TryGetValue(name, out var k)) kits[name] = k = (new MeshKit(), material);
            return k.kit;
        }

        // ---------------------------------------------------------------- start gantry

        private void BuildGantry()
        {
            int k = f.GantrySample;
            Vector3 along = c.Forward[k], left = c.Left[k];
            float road = c.Centre[k].y;
            float bottom = road + TrackFurniture.GantryClearance;
            Vector3 a = f.GantryLeft, b = f.GantryRight;
            float foot = Mathf.Min(a.y, b.y) - 0.5f;   // the legs reach a little into the ground
            Vector3 la = new Vector3(a.x, foot, a.z), lb = new Vector3(b.x, foot, b.z);

            MeshKit frame;
            float top, beamFront;
            switch (Gantry)
            {
                case GantryStyle.Truss:
                {
                    frame = Kit("Start gantry", m?.GreenSteel);
                    top = bottom + BeamDepth;
                    beamFront = 0.42f;
                    // Each leg is a square tower of four corner tubes, braced across.
                    foreach (Vector3 p in new[] { la, lb })
                    {
                        float h = top + 0.3f - p.y;
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 o = along * ((i & 1) == 0 ? -0.35f : 0.35f) + left * ((i & 2) == 0 ? -0.35f : 0.35f);
                            Beam(frame, p + o, p + o + Vector3.up * h, 0.1f);
                        }
                        for (float y = p.y + 1f; y < top; y += 1.4f)
                            for (int fi = 0; fi < 4; fi++)
                            {
                                Vector3 side = fi < 2 ? along : left, across = fi < 2 ? left : along;
                                if (fi % 2 == 1) side = -side;
                                Beam(frame, p + side * 0.35f - across * 0.35f + Vector3.up * (y - p.y),
                                    p + side * 0.35f + across * 0.35f + Vector3.up * (y - p.y + 1.2f), 0.06f);
                            }
                    }
                    // Four chords from leg to leg, with diagonals zigzagging down each face.
                    float span = Flat(la, lb);
                    int bays = Mathf.Max(4, Mathf.RoundToInt(span / 1.6f));
                    foreach (float s in new[] { -0.35f, 0.35f })
                    {
                        Vector3 o = along * s;
                        Vector3 ta = At(la, top) + o, tb = At(lb, top) + o, ba = At(la, bottom) + o, bb = At(lb, bottom) + o;
                        Beam(frame, ta, tb, 0.14f);
                        Beam(frame, ba, bb, 0.14f);
                        for (int i = 0; i < bays; i++)
                        {
                            float u0 = i / (float)bays, u1 = (i + 1) / (float)bays;
                            Vector3 from = i % 2 == 0 ? Vector3.Lerp(ta, tb, u0) : Vector3.Lerp(ba, bb, u0);
                            Vector3 to = i % 2 == 0 ? Vector3.Lerp(ba, bb, u1) : Vector3.Lerp(ta, tb, u1);
                            Beam(frame, from, to, 0.07f);
                        }
                    }
                    break;
                }
                case GantryStyle.Concrete:
                {
                    frame = Kit("Start gantry", m?.Concrete);
                    top = bottom + 1.4f;
                    beamFront = 0.5f;
                    foreach (Vector3 p in new[] { la, lb }) Block(frame, p, top + 0.2f - p.y, along, left, 1.4f, 1.0f);
                    Beam(frame, At(la, (top + bottom) / 2), At(lb, (top + bottom) / 2), 1.0f, 1.4f);
                    break;
                }
                default:
                {
                    frame = Kit("Start gantry", m?.WhitePaint);
                    top = bottom + 1.1f;
                    beamFront = 0.35f;
                    foreach (Vector3 p in new[] { la, lb }) Block(frame, p, top - p.y, along, left, 0.6f, 0.6f);
                    Beam(frame, At(la, (top + bottom) / 2), At(lb, (top + bottom) / 2), 0.7f, 1.1f);
                    break;
                }
            }

            // The light box hangs from the beam over the middle of the road, its lenses facing the grid.
            MeshKit housing = Kit("Start light housing", m?.BlackSteel);
            Vector3 mid = c.Centre[k] - along * (beamFront + 0.2f);
            float boxW = LightColumns * LightPitch + 0.3f, boxH = 1.45f;
            Vector3 boxCentre = new Vector3(mid.x, bottom - boxH / 2 + 0.25f, mid.z);
            Block(housing, boxCentre - Vector3.up * boxH / 2, boxH, left, along, boxW, 0.4f);
            Vector3 face = boxCentre - along * 0.205f;
            for (int col = 0; col < LightColumns; col++)
            {
                float x = (col - (LightColumns - 1) / 2f) * LightPitch;
                Lens(RedLenses, face + left * x + Vector3.up * 0.33f, -along, left);
                Lens(GreenLenses, face + left * x - Vector3.up * 0.33f, -along, left);
                // A hood over each lens keeps the sun off it.
                foreach (float y in new[] { 0.33f, -0.33f })
                    Block(housing, face + left * x + Vector3.up * (y + LensRadius + 0.02f) - along * 0.12f, 0.03f, left, along, LensRadius * 2.2f, 0.25f);
            }
        }

        /// <summary>A round lens as a fan of triangles, facing out, its UVs mapping the disc onto the lens texture.</summary>
        private static void Lens(MeshKit kit, Vector3 centre, Vector3 outward, Vector3 right)
        {
            const int sides = 14;
            Vector3 up = Vector3.up;
            Vector2 uc = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2 * Mathf.PI / sides, a1 = (i + 1) * 2 * Mathf.PI / sides;
                Vector3 p0 = centre + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * LensRadius;
                Vector3 p1 = centre + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * LensRadius;
                Vector2 u0 = new Vector2(0.5f + 0.5f * Mathf.Cos(a0), 0.5f + 0.5f * Mathf.Sin(a0));
                Vector2 u1 = new Vector2(0.5f + 0.5f * Mathf.Cos(a1), 0.5f + 0.5f * Mathf.Sin(a1));
                // Wound so the front faces outward.
                if (Vector3.Dot(Vector3.Cross(p0 - centre, p1 - centre), outward) > 0)
                    kit.Triangle(centre, p0, p1, outward, outward, outward, uc, u0, u1);
                else kit.Triangle(centre, p1, p0, outward, outward, outward, uc, u1, u0);
            }
        }

        // ---------------------------------------------------------------- marshal posts

        private void BuildPost(MarshalPost post)
        {
            Vector3 toRoad = post.ToRoad;
            Vector3 along = new Vector3(toRoad.z, 0, -toRoad.x);
            // Along runs with the cars.
            if (Vector3.Dot(along, post.Facing) > 0) along = -along;
            Vector3 b = post.Base;
            float floor = b.y + SlabHeight;
            MeshKit concrete = Kit("Marshal posts", m?.Concrete);
            Block(concrete, b - Vector3.up * 1.2f, 1.2f + SlabHeight, along, toRoad, PostLength, PostDepth);

            Vector3 F(float x, float y, float z) => new Vector3(b.x, floor, b.z) + along * x + Vector3.up * y + toRoad * z;
            float hx = PostLength / 2, hz = PostDepth / 2;
            if (Post == PostStyle.Roofed)
            {
                MeshKit steel = Kit("Marshal post steel", m?.GreenSteel);
                MeshKit roof = Kit("Marshal post roofs", m?.Roof);
                foreach (float x in new[] { -hx + 0.1f, hx - 0.1f })
                {
                    Beam(steel, F(x, 0, hz - 0.1f), F(x, 2.75f, hz - 0.1f), 0.1f);
                    Beam(steel, F(x, 0, -hz + 0.1f), F(x, 2.45f, -hz + 0.1f), 0.1f);
                }
                // A rail along the front, waist high.
                Beam(steel, F(-hx + 0.1f, 1.0f, hz - 0.1f), F(hx - 0.1f, 1.0f, hz - 0.1f), 0.06f);
                Slab(roof, F(-hx - 0.2f, 2.8f, hz + 0.3f), F(hx + 0.2f, 2.8f, hz + 0.3f), F(-hx - 0.2f, 2.45f, -hz - 0.2f), F(hx + 0.2f, 2.45f, -hz - 0.2f), 0.06f);
            }
            else
            {
                const float wall = 0.2f, height = 2.4f;
                Block(concrete, F(0, 0, -hz + wall / 2), height, along, toRoad, PostLength, wall);
                foreach (float x in new[] { -hx + wall / 2, hx - wall / 2 })
                    Block(concrete, F(x, 0, 0), height, along, toRoad, wall, PostDepth);
                Block(concrete, F(0, 0, hz - wall / 2), 1.05f, along, toRoad, PostLength, wall);
                Block(concrete, F(0, height, 0), 0.15f, along, toRoad, PostLength + 0.3f, PostDepth + 0.3f);
            }

            // The flag panel on its pole by the track, square to the cars coming.
            MeshKit housing = Kit("Flag panel housings", m?.BlackSteel);
            MeshKit poles = Kit("Flag panel poles", m?.Galvanised);
            Vector3 facing = post.Facing;
            Vector3 right = Vector3.Cross(facing, Vector3.up).normalized;   // the viewer's right, looking at the face
            Vector3 panelAt = post.Panel + Vector3.up * PanelHeight;
            Beam(poles, post.Panel - Vector3.up * 0.4f - facing * 0.15f, panelAt - facing * 0.15f, 0.09f);
            Block(housing, panelAt - Vector3.up * (PanelSize * 0.5f + 0.06f), PanelSize + 0.12f, facing, right, 0.18f, PanelSize + 0.12f);
            var face = new MeshKit();
            Vector3 front = panelAt + facing * 0.095f;
            float h = PanelSize * 0.5f;
            Textured(face, front - right * h - Vector3.up * h, front + right * h - Vector3.up * h, front - right * h + Vector3.up * h, front + right * h + Vector3.up * h,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), facing);
            Panels.Add(face);

            // Two marshals at the front of the post, watching the track.
            Vector3 look = (toRoad - along * 0.35f).normalized;
            Marshals.Add((F(-0.55f, 0, hz - 0.65f), look, 0));
            Marshals.Add((F(0.75f, 0, hz - 0.75f), (toRoad + along * 0.2f).normalized, 1));
        }

        // ---------------------------------------------------------------- distance boards

        private void BuildBoard(DistanceBoard board)
        {
            MeshKit legs = Kit("Distance board legs", m?.Galvanised);
            MeshKit backs = Kit("Distance board backs", m?.BoardBack);
            MeshKit faces = Kit("Distance boards", m?.Boards);
            Vector3 facing = board.Facing;
            Vector3 right = Vector3.Cross(facing, Vector3.up).normalized;   // the viewer's right, looking at the face
            Vector3 p = board.Base;
            foreach (float x in new[] { -0.5f, 0.5f })
                Beam(legs, p + right * x - Vector3.up * 0.4f, p + right * x + Vector3.up * (BoardLift + BoardHeight - 0.05f) - facing * 0.06f, 0.07f);
            Vector3 mid = p + Vector3.up * (BoardLift + BoardHeight / 2);
            Block(backs, mid - Vector3.up * BoardHeight / 2, BoardHeight, facing, right, 0.05f, BoardWidth);
            float u0 = BoardColumn(board.Metres) / 3f, u1 = u0 + 1f / 3f;
            Vector3 front = mid + facing * 0.027f;
            float w = BoardWidth / 2, h = BoardHeight / 2;
            Textured(faces, front - right * w - Vector3.up * h, front + right * w - Vector3.up * h, front - right * w + Vector3.up * h, front + right * w + Vector3.up * h,
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u0, 1), new Vector2(u1, 1), facing);
        }

        // ---------------------------------------------------------------- shapes

        private static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        private static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        /// <summary>An upright block standing on <paramref name="bottom"/>: width along <paramref name="along"/>, depth along <paramref name="across"/>.</summary>
        private static void Block(MeshKit kit, Vector3 bottom, float height, Vector3 along, Vector3 across, float width, float depth)
        {
            Vector3 ax = along.normalized * (width / 2), az = Vector3.Cross(Vector3.up, along).normalized * (depth / 2);
            if (Vector3.Dot(az, across) < 0) az = -az;
            Prism(kit, bottom, bottom + Vector3.up * height, ax, az);
        }

        /// <summary>A square bar of the given thickness from a to b, or width by height across it.</summary>
        private static void Beam(MeshKit kit, Vector3 a, Vector3 b, float thickness, float height = -1f)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 side = Mathf.Abs(dir.y) > 0.95f ? Vector3.Cross(dir, Vector3.forward).normalized : Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 up = Vector3.Cross(dir, side).normalized;
            Prism(kit, a, b, side * (thickness / 2), up * ((height > 0 ? height : thickness) / 2));
        }

        /// <summary>A box from a to b with its cross-section spanned by half-axes u and v.</summary>
        private static void Prism(MeshKit kit, Vector3 a, Vector3 b, Vector3 u, Vector3 v)
        {
            Vector3 mid = (a + b) * 0.5f;
            Vector3[] A = { a - u - v, a + u - v, a + u + v, a - u + v };
            Vector3[] B = { b - u - v, b + u - v, b + u + v, b - u + v };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                kit.Face(A[i], A[j], B[i], B[j], mid);
            }
            kit.Face(A[0], A[1], A[3], A[2], mid);
            kit.Face(B[0], B[1], B[3], B[2], mid);
        }

        /// <summary>A sloping slab from its four top corners (back-left, back-right, front-left, front-right) and a thickness.</summary>
        private static void Slab(MeshKit kit, Vector3 bl, Vector3 br, Vector3 fl, Vector3 fr, float thickness)
        {
            Vector3 down = Vector3.down * thickness;
            Vector3 mid = (bl + br + fl + fr) * 0.25f + down * 0.5f;
            kit.Face(bl, br, fl, fr, mid);
            kit.Face(bl + down, br + down, fl + down, fr + down, mid);
            kit.Face(bl, br, bl + down, br + down, mid);
            kit.Face(fl, fr, fl + down, fr + down, mid);
            kit.Face(bl, fl, bl + down, fl + down, mid);
            kit.Face(br, fr, br + down, fr + down, mid);
        }

        /// <summary>A flat quad facing <paramref name="normal"/> with its own UVs at bottom-left, bottom-right, top-left and top-right.</summary>
        private static void Textured(MeshKit kit, Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr, Vector2 ubl, Vector2 ubr, Vector2 utl, Vector2 utr, Vector3 normal)
        {
            bool front = Vector3.Dot(Vector3.Cross(br - bl, tl - bl), normal) > 0;
            // Unity draws a triangle's front where Cross(second - first, third - first) points.
            if (front)
            {
                kit.Triangle(bl, br, tl, normal, normal, normal, ubl, ubr, utl);
                kit.Triangle(br, tr, tl, normal, normal, normal, ubr, utr, utl);
            }
            else
            {
                kit.Triangle(bl, tl, br, normal, normal, normal, ubl, utl, ubr);
                kit.Triangle(br, tl, tr, normal, normal, normal, ubr, utl, utr);
            }
        }
    }
}
