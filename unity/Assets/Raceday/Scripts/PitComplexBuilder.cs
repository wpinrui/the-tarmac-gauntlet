using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>How the pit building over the garages looks.</summary>
    public enum PitLook
    {
        /// <summary>One storey of hospitality glass under a deep roof, a railed roof terrace on top.</summary>
        GlassBand,
        /// <summary>Two storeys behind tall concrete fins, the older look.</summary>
        ConcreteFins,
        /// <summary>One storey of glass with open spectator terraces on its roof under a steel canopy.</summary>
        RoofTerrace,
    }

    /// <summary>How much of each garage's back wall is in the team's colour.</summary>
    public enum TeamWall { Full, Band }

    /// <summary>A Poly Haven prop placed in every garage with a car: where in the garage's frame and which way it faces.</summary>
    public readonly struct GarageProp
    {
        public readonly string Id;
        /// <summary>Across the bay, up and out towards the door, in the garage's frame.</summary>
        public readonly float X, Y, Z;
        /// <summary>Degrees the prop turns from facing out of the door, clockwise seen from above.</summary>
        public readonly float Yaw;

        public GarageProp(string id, float x, float y, float z, float yaw)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// Builds the pit complex's meshes from <see cref="PitComplex"/>: the pit building along the top of the garages,
    /// the race offices over the garages beside the start line, and each garage's fit-out (team colour on the back
    /// wall and the raised door, tool cabinets, a tyre rack, four fresh tyres in blankets and a fuel rig). The
    /// building is one cross-section carried along the row's stations, so it runs on smoothly over the garages'
    /// stepped roofs and round the bend in the row. Everything in team colours is drawn with one palette material:
    /// its UVs pick the garage's texel, so all 100 garages take two draw calls a chunk.
    /// </summary>
    public sealed class PitComplexBuilder
    {
        /// <summary>Texels across the team palette, a single row: one per garage.</summary>
        public const int PaletteSize = 128;
        /// <summary>Window pitch along the glass, m.</summary>
        public const float WindowPitch = 2.1f;
        /// <summary>Windows across the glass's lit-window texture, and storeys down it.</summary>
        public const int WindowCells = 32, StoreyCells = 4;
        private const float ChunkSize = 150f;

        /// <summary>Props in every garage with a car, all against the back wall or the sides, clear of the crew's front.</summary>
        public static readonly GarageProp[] Props =
        {
            new GarageProp("tool_cart", -3.2f, 0f, -5.9f, 0f),
            new GarageProp("metal_tool_chest", 5.8f, 0f, -0.6f, 90f),
            new GarageProp("korean_fire_extinguisher_01", -7.9f, 0f, -3.0f, 90f),
            new GarageProp("metal_toolbox", 1.6f, 1.0f, -7.25f, 0f),
        };

        private readonly PitComplex pc;
        private readonly PitMaterials m;
        private readonly PitLook look;
        private readonly TeamWall wall;
        private readonly List<CircuitPart> parts = new List<CircuitPart>();

        private readonly MeshKit concrete = new MeshKit(), glass = new MeshKit(), steel = new MeshKit();
        private readonly MeshKit team = new MeshKit(), fit = new MeshKit(), metal = new MeshKit(), rubber = new MeshKit(), lamps = new MeshKit();

        /// <param name="materials">May be null outside Unity (tests and previews).</param>
        public PitComplexBuilder(PitComplex complex, PitMaterials materials, PitLook look, TeamWall wall)
        {
            pc = complex;
            m = materials;
            this.look = look;
            this.wall = wall;
        }

        public List<CircuitPart> Build()
        {
            parts.Clear();
            int last = pc.Stations - 1;
            // The fascia beam along the whole row hides the garages' stepped roof edges.
            Extrude(concrete, 0, last, Rect(7.9f, 9.15f, 6.9f, 7.9f), 4f, 4f);
            Building(0, pc.OfficeFrom);
            Building(pc.OfficeTo, last);
            Offices(pc.OfficeFrom, pc.OfficeTo);
            for (int g = 0; g < pc.Garages; g++) FitOut(g);

            Emit("Pit building", concrete, m?.Concrete, true);
            Emit("Pit building glass", glass, m?.Glass, true);
            Emit("Pit building steel", steel, m?.Steel, true);
            Emit("Garage team colours", team, m?.Palette, false);
            Emit("Garage fit-out", fit, m?.Fit, false);
            Emit("Garage metal", metal, m?.Metal, false);
            Emit("Garage tyres", rubber, m?.Rubber, false);
            Emit("Garage lights", lamps, m?.Lamps, false);
            return parts;
        }

        // ---------------------------------------------------------------- the building

        private void Building(int from, int to)
        {
            if (to <= from) return;
            switch (look)
            {
                case PitLook.GlassBand:
                    Storey(from, to, 7.9f, 10.5f);
                    Extrude(concrete, from, to, Rect(-8.4f, 9.6f, 10.5f, 10.9f), 4f, 4f);
                    Extrude(concrete, from, to, Rect(-8.4f, -8.05f, 10.9f, 11.3f), 4f, 4f);
                    Railing(from, to, 9.4f, 10.9f);
                    break;
                case PitLook.ConcreteFins:
                    Extrude(concrete, from, to, Rect(-7.6f, 7.95f, 7.15f, 14.0f), 4f, 4f);
                    Extrude(concrete, from, to, Rect(-8.15f, -7.6f, 6.9f, 14.0f), 4f, 4f);
                    Face(glass, from, to, new Vector2(8.0f, 7.9f), new Vector2(8.0f, 14.0f), 3.05f, true);
                    Face(glass, from, to, new Vector2(-8.2f, 8.6f), new Vector2(-8.2f, 9.8f), 3.05f, false);
                    Face(glass, from, to, new Vector2(-8.2f, 11.6f), new Vector2(-8.2f, 12.8f), 3.05f, false);
                    Extrude(concrete, from, to, Rect(7.9f, 9.3f, 10.85f, 11.25f), 4f, 4f);
                    Extrude(concrete, from, to, Rect(-8.4f, 9.4f, 14.0f, 14.9f), 4f, 4f);
                    Repeat(from, to, 8.65f, 2.8f, (p, al, o) => concrete.Box(p + Vector3.up * 3.05f, al, o, new Vector3(0.3f, 6.1f, 1.3f)), 1.4f, 7.9f);
                    break;
                case PitLook.RoofTerrace:
                    Storey(from, to, 7.9f, 10.5f);
                    Extrude(concrete, from, to, Rect(-8.4f, 9.15f, 10.5f, 10.9f), 4f, 4f);
                    // Six rows of terrace stepping up away from the lane, the walkway along the front.
                    for (int k = 0; k < 6; k++)
                        Extrude(concrete, from, to, Rect(6.9f - (k + 1) * 0.85f, 6.9f - k * 0.85f, 10.9f, 11.32f + 0.42f * k), 4f, 4f);
                    Extrude(concrete, from, to, Rect(-8.4f, 1.8f, 10.9f, 13.9f), 4f, 4f);
                    Railing(from, to, 9.0f, 10.9f);
                    // The canopy over the terraces, on a column every half bay at the back.
                    Extrude(steel, from, to, new[] { new Vector2(0.2f, 15.85f), new Vector2(9.6f, 15.25f), new Vector2(9.6f, 15.4f), new Vector2(0.2f, 16.05f) }, 4f, 4f);
                    Repeat(from, to, 0.8f, pc.Width * 0.5f, (p, al, o) => steel.Box(p + Vector3.up * 14.9f, al, o, new Vector3(0.25f, 2.1f, 0.25f)), 2.1f);
                    break;
            }
        }

        /// <summary>A glass storey on the garages: concrete body and back wall, glass front with steel mullions, a window strip at the back.</summary>
        private void Storey(int from, int to, float y0, float y1)
        {
            Extrude(concrete, from, to, Rect(-7.6f, 8.2f, 7.15f, y1), 4f, 4f);
            Extrude(concrete, from, to, Rect(-8.15f, -7.6f, 6.9f, y1), 4f, 4f);
            Face(glass, from, to, new Vector2(8.25f, y0), new Vector2(8.25f, y1), y1 - y0, true);
            Face(glass, from, to, new Vector2(-8.2f, y0 + 0.9f), new Vector2(-8.2f, y1 - 0.5f), y1 - y0, false);
            Repeat(from, to, 8.31f, WindowPitch, (p, al, o) => steel.Box(p + Vector3.up * ((y1 - y0) * 0.5f), al, o, new Vector3(0.08f, y1 - y0, 0.12f)), 0f, y0);
        }

        /// <summary>A steel railing at the edge of a roof: posts every window, a top rail and a mid rail.</summary>
        private void Railing(int from, int to, float z, float floor)
        {
            Extrude(steel, from, to, Rect(z - 0.05f, z + 0.05f, floor + 1.0f, floor + 1.08f), 4f, 4f);
            Extrude(steel, from, to, Rect(z - 0.03f, z + 0.03f, floor + 0.5f, floor + 0.55f), 4f, 4f);
            Repeat(from, to, z, WindowPitch, (p, al, o) => steel.Box(p + Vector3.up * 0.5f, al, o, new Vector3(0.06f, 1.0f, 0.06f)), 0.3f, floor);
        }

        /// <summary>
        /// The race offices: two storeys of offices behind glass, then race control on top, its windows leaning out
        /// over the pit lane, under a deep roof.
        /// </summary>
        private void Offices(int from, int to)
        {
            Extrude(concrete, from, to, Rect(-8.15f, 7.95f, 7.15f, 14.4f), 4f, 4f);
            Face(glass, from, to, new Vector2(8.0f, 7.9f), new Vector2(8.0f, 14.4f), 3.25f, true);
            Face(glass, from, to, new Vector2(-8.45f, 8.8f), new Vector2(-8.45f, 10.0f), 3.25f, false);
            Face(glass, from, to, new Vector2(-8.45f, 12.0f), new Vector2(-8.45f, 13.2f), 3.25f, false);
            Extrude(concrete, from, to, Rect(7.9f, 8.9f, 10.9f, 11.35f), 4f, 4f);
            Repeat(from, to, 8.06f, WindowPitch, (p, al, o) => steel.Box(p + Vector3.up * 11.15f, al, o, new Vector3(0.08f, 6.5f, 0.12f)), 0f);
            Extrude(concrete, from, to, Rect(-8.4f, 9.4f, 14.4f, 15.0f), 4f, 4f);
            Extrude(concrete, from, to, new[] { new Vector2(-6.0f, 15.0f), new Vector2(7.7f, 15.0f), new Vector2(8.8f, 18.2f), new Vector2(-6.0f, 18.2f) }, 4f, 4f);
            Face(glass, from, to, new Vector2(7.73f, 15.0f), new Vector2(8.83f, 18.2f), 3.38f, true);
            Extrude(concrete, from, to, Rect(-6.3f, 9.5f, 18.2f, 18.6f), 4f, 4f);
        }

        // ---------------------------------------------------------------- garage fit-out

        private void FitOut(int g)
        {
            GarageFrame f = pc.Frame(g);
            float left = pc.LeftWall;
            float back = -pc.Depth * 0.5f + 0.3f;      // inner face of the back wall
            float door = pc.Depth * 0.5f;
            Vector2 flat = PaletteUv(g);
            Func<Vector3, Vector2> paint = _ => flat;

            // Team colour on the back wall: all of it to door height, or a band above the cabinets.
            if (wall == TeamWall.Full) Span(team, f, left + 0.05f, pc.RightWall(g, back) - 0.05f, back + 0.04f, 0.08f, 0.05f, 5.0f, paint);
            else Span(team, f, left + 0.05f, pc.RightWall(g, back) - 0.05f, back + 0.04f, 0.08f, 3.1f, 4.3f, paint);

            // The door, rolled up to just under the fascia, in the team's colour, its bottom rail in steel.
            float doorRight = pc.RightWall(g, door) + 0.1f;
            Span(team, f, left - 0.1f, doorRight, door + 0.035f, 0.07f, 4.3f, 6.95f, paint);
            Span(metal, f, left - 0.1f, doorRight, door + 0.05f, 0.1f, 4.2f, 4.3f);

            // Tool cabinets along the back wall, a steel worktop and wall cupboards over it.
            Block(fit, f, -1.0f, back + 0.3f, 9.0f, 0.6f, 0f, 0.95f);
            Block(metal, f, -1.0f, back + 0.33f, 9.1f, 0.66f, 0.95f, 1.0f);
            Block(fit, f, -1.0f, back + 0.18f, 9.0f, 0.36f, 1.9f, 2.6f);

            // A tyre rack against the +x wall at the back: steel frame, two shelves of tyres standing in rows.
            float rx = pc.RightWall(g, -3.6f) - 0.45f;
            for (int s = 0; s < 3; s++) Block(metal, f, rx, -5.55f, 0.8f, 3.9f, 0.1f + s * 0.85f, 0.14f + s * 0.85f);
            foreach (float pz in new[] { -7.45f, -3.65f })
                foreach (float px in new[] { rx - 0.38f, rx + 0.38f })
                    Block(metal, f, px, pz, 0.05f, 0.05f, 0f, 1.9f);
            for (int s = 0; s < 2; s++)
                for (int t = 0; t < 10; t++)
                    Tyre(rubber, f.At(rx, 0.14f + s * 0.85f + 0.33f, -7.17f + t * 0.36f), f.Out, 0.33f, 0.28f);

            // Four fresh tyres in blankets of the team's colour, standing inside the door by the -x wall.
            for (int w = 0; w < 4; w++)
            {
                Vector3 at = pc.FreshTyres(g, w).Position + Vector3.up * 0.34f;
                Tyre(team, at, f.Out, 0.34f, 0.3f, flat);
                Tyre(rubber, at, f.Out, 0.3f, 0.32f);
            }

            // The fuel rig by the +x wall inside the door: a tank tower, a mast and a boom out to the door, the hose hanging to its rest.
            CrewSpot rest = pc.FuelRig(g);
            float right = pc.RightWall(g, door);
            Block(fit, f, right - 0.5f, door - 2.4f, 0.9f, 1.0f, 0f, 2.4f);
            Block(team, f, right - 0.5f, door - 1.88f, 0.92f, 0.04f, 1.7f, 2.2f, paint);
            Block(metal, f, right - 0.5f, door - 2.4f, 0.15f, 0.15f, 2.4f, 4.6f);
            Block(metal, f, right - 0.5f, door - 1.5f, 0.12f, 1.95f, 4.45f, 4.6f);
            float rx2 = Dot(rest.Position - f.Floor, f.Side), rz = Dot(rest.Position - f.Floor, f.Out);
            Block(fit, f, rx2, rz, 0.07f, 0.07f, 1.0f, 4.45f);
            Block(metal, f, rx2, rz, 0.3f, 0.3f, 0f, 1.0f);

            // Strip lights across the ceiling.
            foreach (float lz in new[] { -5.0f, -1.5f, 2.0f, 5.5f })
                Block(lamps, f, 0f, lz, 11.0f, 0.3f, 6.82f, 6.88f);
        }

        /// <summary>The palette's UV for a garage.</summary>
        public static Vector2 PaletteUv(int garage) => new Vector2((garage + 0.5f) / PaletteSize, 0.5f);

        // ---------------------------------------------------------------- shapes

        private static Vector2[] Rect(float z0, float z1, float y0, float y1) =>
            new[] { new Vector2(z0, y0), new Vector2(z1, y0), new Vector2(z1, y1), new Vector2(z0, y1) };

        /// <summary>The fractional stations a run from one station to another passes, every half bay.</summary>
        private static List<float> Rows(int from, int to)
        {
            var rows = new List<float>();
            for (int h = from * 2; h <= to * 2; h++) rows.Add(h * 0.5f);
            return rows;
        }

        /// <summary>Distance along the row at offset z, from the first of the rows to each.</summary>
        private float[] Along(List<float> rows, float z)
        {
            var d = new float[rows.Count];
            for (int r = 1; r < rows.Count; r++)
            {
                Vector3 a = pc.RowPoint(rows[r - 1], 0f, z), b = pc.RowPoint(rows[r], 0f, z);
                a.y = b.y = 0f;
                d[r] = d[r - 1] + (b - a).magnitude;
            }
            return d;
        }

        /// <summary>
        /// A convex cross-section, (z out towards the lane, y up from the floor), carried along the row from one
        /// station to another and closed at both ends. UVs run along in metres over tileAlong and round the section
        /// over tileRound.
        /// </summary>
        private void Extrude(MeshKit kit, int from, int to, Vector2[] poly, float tileAlong, float tileRound)
        {
            if (to <= from) return;
            List<float> rows = Rows(from, to);
            Vector2 mid = Vector2.zero;
            foreach (Vector2 p in poly) mid += p;
            mid /= poly.Length;
            float round = 0f;
            for (int e = 0; e < poly.Length; e++)
            {
                Vector2 a = poly[e], b = poly[(e + 1) % poly.Length];
                Vector2 n2 = new Vector2(b.y - a.y, -(b.x - a.x)).normalized;
                if (Vector2.Dot(n2, (a + b) * 0.5f - mid) < 0f) n2 = -n2;
                float len = (b - a).magnitude;
                float[] along = Along(rows, (a.x + b.x) * 0.5f);
                for (int r = 0; r < rows.Count - 1; r++)
                {
                    float s0 = rows[r], s1 = rows[r + 1];
                    Vector3 n0 = pc.RowOut(s0) * n2.x + Vector3.up * n2.y, n1 = pc.RowOut(s1) * n2.x + Vector3.up * n2.y;
                    Vector3 p00 = pc.RowPoint(s0, a.y, a.x), p01 = pc.RowPoint(s0, b.y, b.x);
                    Vector3 p10 = pc.RowPoint(s1, a.y, a.x), p11 = pc.RowPoint(s1, b.y, b.x);
                    Vector2 u00 = new Vector2(along[r] / tileAlong, round / tileRound), u01 = new Vector2(along[r] / tileAlong, (round + len) / tileRound);
                    Vector2 u10 = new Vector2(along[r + 1] / tileAlong, round / tileRound), u11 = new Vector2(along[r + 1] / tileAlong, (round + len) / tileRound);
                    Quad(kit, p00, p01, p10, p11, n0, n0, n1, n1, u00, u01, u10, u11);
                }
                round += len;
            }
            Cap(kit, rows[0], poly, -pc.RowAlong(rows[0]));
            Cap(kit, rows[rows.Count - 1], poly, pc.RowAlong(rows[rows.Count - 1]));
        }

        private void Cap(MeshKit kit, float station, Vector2[] poly, Vector3 normal)
        {
            Vector3 P(Vector2 q) => pc.RowPoint(station, q.y, q.x);
            for (int i = 1; i < poly.Length - 1; i++)
            {
                Vector3 a = P(poly[0]), b = P(poly[i]), c = P(poly[i + 1]);
                Tri(kit, a, b, c, normal, new Vector2(poly[0].x, poly[0].y) * 0.25f, new Vector2(poly[i].x, poly[i].y) * 0.25f, new Vector2(poly[i + 1].x, poly[i + 1].y) * 0.25f);
            }
        }

        /// <summary>
        /// A single face from a to b in the cross-section, carried along the row, facing out (towards the lane) or
        /// back. UVs count windows along and storeys up, for the lit-window texture.
        /// </summary>
        private void Face(MeshKit kit, int from, int to, Vector2 a, Vector2 b, float storey, bool outward)
        {
            List<float> rows = Rows(from, to);
            Vector2 n2 = new Vector2(b.y - a.y, -(b.x - a.x)).normalized;
            if ((n2.x > 0f) != outward) n2 = -n2;
            float[] along = Along(rows, (a.x + b.x) * 0.5f);
            float len = (b - a).magnitude;
            for (int r = 0; r < rows.Count - 1; r++)
            {
                float s0 = rows[r], s1 = rows[r + 1];
                Vector3 n0 = pc.RowOut(s0) * n2.x + Vector3.up * n2.y, n1 = pc.RowOut(s1) * n2.x + Vector3.up * n2.y;
                float ua = along[r] / WindowPitch, ub = along[r + 1] / WindowPitch;
                Quad(kit, pc.RowPoint(s0, a.y, a.x), pc.RowPoint(s0, b.y, b.x), pc.RowPoint(s1, a.y, a.x), pc.RowPoint(s1, b.y, b.x), n0, n0, n1, n1,
                    new Vector2(ua, 0f), new Vector2(ua, len / storey), new Vector2(ub, 0f), new Vector2(ub, len / storey));
            }
        }

        /// <summary>
        /// Calls place at every pitch along the row at offset z, starting offset metres in, with the spot at
        /// height y over the floor and the row's along and out directions there.
        /// </summary>
        private void Repeat(int from, int to, float z, float pitch, Action<Vector3, Vector3, Vector3> place, float offset, float y = 0f)
        {
            if (to <= from) return;
            List<float> rows = Rows(from, to);
            float[] along = Along(rows, z);
            float total = along[along.Length - 1];
            int r = 0;
            for (float d = offset > 0f ? offset : pitch; d < total - 0.05f; d += pitch)
            {
                while (r < rows.Count - 2 && along[r + 1] < d) r++;
                float t = (d - along[r]) / Mathf.Max(along[r + 1] - along[r], 1e-4f);
                float st = Mathf.Lerp(rows[r], rows[r + 1], t);
                place(pc.RowPoint(st, y, z), pc.RowAlong(st), pc.RowOut(st));
            }
        }

        /// <summary>A box in a garage's frame: centred at x across and z out, its sizes across and out, from y0 up to y1.</summary>
        private static void Block(MeshKit kit, GarageFrame f, float x, float z, float sx, float sz, float y0, float y1, Func<Vector3, Vector2> uv = null)
        {
            Vector3 c = f.At(x, (y0 + y1) * 0.5f, z);
            Box(kit, c, f.Side, f.Out, new Vector3(sx, y1 - y0, sz), uv);
        }

        /// <summary>A block across the garage from x0 to x1.</summary>
        private static void Span(MeshKit kit, GarageFrame f, float x0, float x1, float z, float sz, float y0, float y1, Func<Vector3, Vector2> uv = null) =>
            Block(kit, f, (x0 + x1) * 0.5f, z, x1 - x0, sz, y0, y1, uv);

        /// <summary>A box with its own UVs, sized along right, up and forward.</summary>
        private static void Box(MeshKit kit, Vector3 c, Vector3 right, Vector3 forward, Vector3 size, Func<Vector3, Vector2> uv)
        {
            if (uv == null)
            {
                kit.Box(c, right, forward, size);
                return;
            }
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => c + right * (x * h.x) + Vector3.up * (y * h.y) + forward * (z * h.z);
            void F(Vector3 a, Vector3 b, Vector3 d, Vector3 e)
            {
                Vector3 n = ((a + b + d + e) * 0.25f - c).normalized;
                Quad(kit, a, b, d, e, n, n, n, n, uv(a), uv(b), uv(d), uv(e));
            }
            F(P(-1, -1, 1), P(1, -1, 1), P(-1, 1, 1), P(1, 1, 1));
            F(P(-1, -1, -1), P(1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
            F(P(1, -1, -1), P(1, -1, 1), P(1, 1, -1), P(1, 1, 1));
            F(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, -1), P(-1, 1, 1));
            F(P(-1, 1, -1), P(1, 1, -1), P(-1, 1, 1), P(1, 1, 1));
            F(P(-1, -1, -1), P(1, -1, -1), P(-1, -1, 1), P(1, -1, 1));
        }

        /// <summary>A tyre standing on its tread, its axle along axis: an eight-sided drum, closed at both sides.</summary>
        private static void Tyre(MeshKit kit, Vector3 centre, Vector3 axis, float radius, float width, Vector2? uv = null)
        {
            const int sides = 8;
            Vector3 ax = axis.normalized;
            Vector3 u = Vector3.up, v = Vector3.Cross(ax, u).normalized;
            Vector3 half = ax * (width * 0.5f);
            Vector2 U(float a, float b) => uv ?? new Vector2(a, b);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 r0 = (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * radius, r1 = (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * radius;
                Vector3 n0 = r0.normalized, n1 = r1.normalized;
                Quad(kit, centre + r0 - half, centre + r0 + half, centre + r1 - half, centre + r1 + half, n0, n0, n1, n1,
                    U(i / (float)sides, 0f), U(i / (float)sides, 1f), U((i + 1f) / sides, 0f), U((i + 1f) / sides, 1f));
                Tri(kit, centre - half, centre + r0 - half, centre + r1 - half, -ax, U(0.5f, 0.5f), U(0f, 0f), U(1f, 0f));
                Tri(kit, centre + half, centre + r0 + half, centre + r1 + half, ax, U(0.5f, 0.5f), U(0f, 0f), U(1f, 0f));
            }
        }

        /// <summary>A quad from corners a, b along one edge and c, d along the next row, wound to face along its normals.</summary>
        private static void Quad(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 n = na + nb + nc + nd;
            Wound(kit, a, c, b, na, nc, nb, ua, uc, ub, n);
            Wound(kit, b, c, d, nb, nc, nd, ub, uc, ud, n);
        }

        private static void Tri(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc) =>
            Wound(kit, a, b, c, normal, normal, normal, ua, ub, uc, normal);

        /// <summary>One triangle, its corners swapped if need be so it faces the given way in Unity's winding.</summary>
        private static void Wound(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
                kit.Triangle(a, c, b, na, nc, nb, ua, uc, ub);
            else
                kit.Triangle(a, b, c, na, nb, nc, ua, ub, uc);
        }

        private static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        private void Emit(string name, MeshKit kit, Material material, bool shadows)
        {
            foreach (MeshKit chunk in kit.Indexed().Chunks(ChunkSize))
                if (!chunk.IsEmpty) parts.Add(new CircuitPart(name, chunk, material, false, shadows));
        }
    }
}
