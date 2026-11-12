using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The trackside props of one square cell of the circuit, merged into one mesh per material. The coarse parts
    /// give each prop its shape; the fine ones (braces, rails, ladders, windows, the cameras) are dropped first as the
    /// cell shrinks on screen, then the whole cell.
    /// </summary>
    public sealed class PropCell
    {
        /// <summary>The middle of the cell, on the ground of its first prop.</summary>
        public Vector3 Centre;
        public readonly List<CircuitPart> Coarse = new List<CircuitPart>(), Fine = new List<CircuitPart>();
    }

    /// <summary>Builds the meshes of the props <see cref="TracksideProps"/> placed, cell by cell.</summary>
    public sealed class PropBuilder
    {
        /// <summary>Side of a cell, metres.</summary>
        public const float CellSize = 100f;

        // Hoardings.
        private const float PanelWidth = TracksideProps.HoardingWidth, PanelHeight = 1f, PanelLift = 0.45f, PanelFront = 0.3f;
        // Camera towers.
        private const float TubeAt = 1.1f, DeckHeight = 5f, RoofHeight = 2.4f;
        // Cabins.
        private const float CabinLength = 6f, CabinDepth = 2.4f, CabinWall = 2.5f, PlinthHeight = 0.15f;

        public readonly List<PropCell> Cells = new List<PropCell>();
        /// <summary>Where the camera operators stand on the towers, and the way they face.</summary>
        public readonly List<(Vector3 feet, Vector3 facing)> Operators = new List<(Vector3, Vector3)>();

        private readonly Circuit c;
        private readonly FurnitureMaterials m;
        private readonly Dictionary<(int, int), (PropCell cell, Dictionary<(string, bool), MeshKit> kits)> cells =
            new Dictionary<(int, int), (PropCell, Dictionary<(string, bool), MeshKit>)>();
        private readonly Dictionary<string, Material> materialOf = new Dictionary<string, Material>();
        private Dictionary<(string, bool), MeshKit> kits;

        /// <param name="materials">May be null outside Unity.</param>
        public PropBuilder(Circuit circuit, TracksideProps props, FurnitureMaterials materials)
        {
            c = circuit;
            m = materials;
            foreach (PropSpot spot in props.Spots)
            {
                var key = (Mathf.FloorToInt(spot.Base.x / CellSize), Mathf.FloorToInt(spot.Base.z / CellSize));
                if (!cells.TryGetValue(key, out var entry))
                {
                    var cell = new PropCell { Centre = new Vector3((key.Item1 + 0.5f) * CellSize, spot.Base.y, (key.Item2 + 0.5f) * CellSize) };
                    cells[key] = entry = (cell, new Dictionary<(string, bool), MeshKit>());
                    Cells.Add(cell);
                }
                kits = entry.kits;
                switch (spot.Kind)
                {
                    case PropKind.Hoarding: Hoarding(spot); break;
                    case PropKind.CameraTower: Tower(spot); break;
                    case PropKind.Cabin: Cabin(spot); break;
                }
            }
            foreach (var (cell, byName) in cells.Values)
                foreach (var pair in byName)
                {
                    var part = new CircuitPart(pair.Key.Item1, pair.Value, materialOf.TryGetValue(pair.Key.Item1, out Material mat) ? mat : null, false, true);
                    (pair.Key.Item2 ? cell.Fine : cell.Coarse).Add(part);
                }
        }

        private MeshKit Kit(string name, Material material, bool fine = false)
        {
            materialOf[name] = material;
            if (!kits.TryGetValue((name, fine), out MeshKit k)) kits[(name, fine)] = k = new MeshKit();
            return k;
        }

        /// <summary>A point in the prop's own frame: x along the track, y up from <paramref name="y0"/>, z towards the road.</summary>
        private static Vector3 P(PropSpot s, float x, float y, float z) =>
            new Vector3(s.Base.x, 0f, s.Base.z) + s.Along * x + Vector3.up * y + s.ToRoad * z;

        private float Ground(Vector3 p) => c.TerrainHeight(p.x, p.z);

        // ---------------------------------------------------------------- hoardings

        private void Hoarding(PropSpot s)
        {
            MeshKit legs = Kit("Hoarding legs", m?.Galvanised);
            MeshKit braces = Kit("Hoarding legs", m?.Galvanised, true);
            MeshKit backs = Kit("Hoarding backs", m?.BoardBack);
            MeshKit faces = Kit("Hoardings", m?.Hoardings);
            float bottom = s.High + PanelLift, top = bottom + PanelHeight;
            float legZ = PanelFront - 0.06f;
            foreach (float x in new[] { -PanelWidth * 0.38f, PanelWidth * 0.38f })
            {
                float g = Ground(P(s, x, 0f, legZ));
                FurnitureBuilder.Beam(legs, P(s, x, g - 0.4f, legZ), P(s, x, top - 0.05f, legZ), 0.08f);
                // A strut back to the ground behind each leg, against the wind.
                float gb = Ground(P(s, x, 0f, legZ - 0.75f));
                FurnitureBuilder.Beam(braces, P(s, x, bottom + 0.55f, legZ - 0.04f), P(s, x, gb - 0.15f, legZ - 0.75f), 0.05f);
            }
            Vector3 mid = P(s, 0f, bottom, PanelFront);
            FurnitureBuilder.Block(backs, mid, PanelHeight, s.Along, s.ToRoad, PanelWidth, 0.06f);

            // The advert faces the road. Atlas cells run two across and four down.
            Vector3 facing = s.ToRoad;
            Vector3 right = Vector3.Cross(facing, Vector3.up).normalized;
            Vector3 front = P(s, 0f, bottom + PanelHeight / 2f, PanelFront + 0.031f);
            float w = PanelWidth / 2f, h = PanelHeight / 2f;
            float u0 = (s.Variant % 2) * 0.5f, u1 = u0 + 0.5f;
            float v1 = 1f - (s.Variant / 2) * 0.25f, v0 = v1 - 0.25f;
            FurnitureBuilder.Textured(faces, front - right * w - Vector3.up * h, front + right * w - Vector3.up * h, front - right * w + Vector3.up * h, front + right * w + Vector3.up * h,
                new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u0, v1), new Vector2(u1, v1), facing);
        }

        // ---------------------------------------------------------------- camera towers

        private void Tower(PropSpot s)
        {
            MeshKit tubes = Kit("Camera tower scaffold", m?.Galvanised);
            MeshKit bracing = Kit("Camera tower scaffold", m?.Galvanised, true);
            MeshKit deck = Kit("Camera tower decks", m?.BoardBack);
            MeshKit roof = Kit("Camera tower roofs", m?.Roof);
            MeshKit camera = Kit("Broadcast cameras", m?.BlackSteel, true);
            float floor = s.High + DeckHeight, lid = floor + RoofHeight;

            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                corners[i] = P(s, i == 0 || i == 3 ? -TubeAt : TubeAt, 0f, i < 2 ? -TubeAt : TubeAt);
                FurnitureBuilder.Beam(tubes, At(corners[i], Ground(corners[i]) - 0.3f), At(corners[i], lid), 0.09f);
            }
            // Ledgers ring the tower every 1.25 m, with a diagonal zigzagging up each face between them.
            var levels = new List<float>();
            int steps = Mathf.Max(2, Mathf.RoundToInt((floor - s.High - 0.3f) / 1.25f));
            for (int j = 0; j <= steps; j++) levels.Add(Mathf.Lerp(s.High + 0.3f, floor, j / (float)steps));
            for (int j = 0; j < levels.Count; j++)
                for (int fi = 0; fi < 4; fi++)
                {
                    Vector3 a = corners[fi], b = corners[(fi + 1) % 4];
                    bool ends = j == 0 || j + 1 == levels.Count;
                    FurnitureBuilder.Beam(ends ? tubes : bracing, At(a, levels[j]), At(b, levels[j]), 0.06f);
                    if (j + 1 == levels.Count) continue;
                    bool up = (j + fi) % 2 == 0;
                    FurnitureBuilder.Beam(bracing, At(up ? a : b, levels[j]), At(up ? b : a, levels[j + 1]), 0.05f);
                }
            FurnitureBuilder.Block(deck, P(s, 0f, floor - 0.1f, 0f), 0.1f, s.Along, s.ToRoad, TubeAt * 2f + 0.3f, TubeAt * 2f + 0.3f);
            // Hand and knee rails round the deck, open at the back where the ladder comes up.
            for (int fi = 0; fi < 4; fi++)
            {
                if (fi == 0) continue;   // corners 0 and 1: the back
                Vector3 a = corners[fi], b = corners[(fi + 1) % 4];
                foreach (float y in new[] { 0.5f, 1.0f })
                    FurnitureBuilder.Beam(bracing, At(a, floor + y), At(b, floor + y), 0.05f);
            }
            float e = TubeAt + 0.15f;
            FurnitureBuilder.Slab(roof, P(s, -e, lid + 0.02f, -e), P(s, e, lid + 0.02f, -e), P(s, -e, lid + 0.12f, e), P(s, e, lid + 0.12f, e), 0.05f);

            // A ladder up the back face.
            const float ladder = 0.22f;
            float back = -TubeAt - 0.12f;
            float g0 = Ground(P(s, 0f, 0f, back));
            foreach (float x in new[] { -ladder, ladder })
                FurnitureBuilder.Beam(bracing, P(s, x, g0 - 0.2f, back), P(s, x, floor + 1f, back), 0.05f);
            for (float y = g0 + 0.3f; y < floor; y += 0.3f)
                FurnitureBuilder.Beam(bracing, P(s, -ladder, y, back), P(s, ladder, y, back), 0.035f);

            // The camera on its tripod, aimed at the road and a little back towards the cars coming.
            Vector3 aim = (s.ToRoad - s.Along * 0.35f).normalized;
            Vector3 foot = P(s, 0f, floor, 0.35f);
            Vector3 head = foot + Vector3.up * 1.3f;
            Vector3 side = Vector3.Cross(Vector3.up, aim).normalized;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2f * Mathf.PI / 3f;
                FurnitureBuilder.Beam(camera, head - Vector3.up * 0.1f, foot + (aim * Mathf.Cos(a) + side * Mathf.Sin(a)) * 0.38f, 0.035f);
            }
            FurnitureBuilder.Block(camera, head - aim * 0.1f, 0.34f, aim, side, 0.62f, 0.26f);
            FurnitureBuilder.Block(camera, head + aim * 0.32f + Vector3.up * 0.08f, 0.18f, aim, side, 0.36f, 0.18f);
            Operators.Add((foot - aim * 0.75f, aim));
        }

        private static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        // ---------------------------------------------------------------- cabins

        private void Cabin(PropSpot s)
        {
            MeshKit plinth = Kit("Cabin plinths", m?.Concrete);
            MeshKit walls = Kit("Cabin walls", m?.WhitePaint);
            MeshKit roof = Kit("Cabin roofs", m?.Roof);
            MeshKit glass = Kit("Cabin windows", m?.Glass, true);
            MeshKit door = Kit("Cabin doors", m?.GreenSteel, true);
            MeshKit step = Kit("Cabin plinths", m?.Concrete, true);
            float floor = s.High + PlinthHeight;
            float low = s.Base.y;
            FurnitureBuilder.Block(plinth, P(s, 0f, low - 0.4f, 0f), floor - low + 0.4f, s.Along, s.ToRoad, CabinLength + 0.4f, CabinDepth + 0.4f);
            FurnitureBuilder.Block(walls, P(s, 0f, floor, 0f), CabinWall, s.Along, s.ToRoad, CabinLength, CabinDepth);
            float hl = CabinLength / 2f + 0.15f, hd = CabinDepth / 2f + 0.15f;
            // Falls to the back, so the rain runs off away from the door.
            FurnitureBuilder.Slab(roof, P(s, -hl, floor + CabinWall + 0.02f, -hd), P(s, hl, floor + CabinWall + 0.02f, -hd),
                P(s, -hl, floor + CabinWall + 0.14f, hd), P(s, hl, floor + CabinWall + 0.14f, hd), 0.08f);

            float face = CabinDepth / 2f + 0.012f;
            foreach (float x in new[] { -1.9f, -0.3f })
                FurnitureBuilder.Block(glass, P(s, x, floor + 1.05f, face), 0.95f, s.Along, s.ToRoad, 1.3f, 0.04f);
            // Round the end, a window to see up the track.
            FurnitureBuilder.Block(glass, P(s, -CabinLength / 2f - 0.012f, floor + 1.05f, 0f), 0.95f, s.ToRoad, s.Along, 1.1f, 0.04f);
            const float doorX = 1.85f;
            FurnitureBuilder.Block(door, P(s, doorX, floor, face), 2.05f, s.Along, s.ToRoad, 0.92f, 0.04f);
            float gs = Ground(P(s, doorX, 0f, CabinDepth / 2f + 0.55f));
            FurnitureBuilder.Block(step, P(s, doorX, Mathf.Min(gs, low) - 0.2f, CabinDepth / 2f + 0.5f), floor - Mathf.Min(gs, low) + 0.2f - 0.02f, s.Along, s.ToRoad, 1.1f, 0.6f);
        }
    }
}
