using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The country beyond the ground mesh, out to where the haze hides everything: a ring round the
    /// circuit that starts just under the ground's edge at the ground's own height and rolls up into
    /// hills. Its colour map shows woods and fields from afar; nothing stands on it. Free of Unity's
    /// runtime so the preview builds the same ring.
    /// </summary>
    public sealed class Horizon
    {
        /// <summary>Where the ring ends, m from the middle of the ground. Past the haze.</summary>
        public const float Reach = 7600f;
        public const int Around = 192, Rings = 34;
        public const int MapAround = 1024, MapOut = 128;

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        /// <summary>Land use on the ring, <see cref="MapAround"/> by <see cref="MapOut"/>, row by row around.</summary>
        public readonly LandUse[] Map = new LandUse[MapAround * MapOut];

        private readonly Circuit c;
        private readonly LandscapeLook look;
        private readonly float cx, cz, minX, minZ, maxX, maxZ, level;

        public Horizon(Circuit circuit, LandscapeLook look)
        {
            c = circuit;
            this.look = look;
            GroundData g = c.Data.terrain;
            minX = g.originX;
            minZ = g.originZ;
            maxX = g.originX + (g.countX - 1) * g.spacing;
            maxZ = g.originZ + (g.countZ - 1) * g.spacing;
            cx = (minX + maxX) * 0.5f;
            cz = (minZ + maxZ) * 0.5f;
            float sum = 0f;
            for (int k = 0; k < c.Count; k++) sum += c.Centre[k].y;
            level = sum / c.Count;

            for (int a = 0; a <= Around; a++)
                for (int r = 0; r < Rings; r++)
                {
                    Vector3 p = Point(a / (float)Around, r / (float)(Rings - 1));
                    Vertices.Add(p);
                    Uvs.Add(new Vector2(a / (float)Around, r / (float)(Rings - 1)));
                }
            for (int a = 0; a < Around; a++)
                for (int r = 0; r < Rings - 1; r++)
                {
                    int i = a * Rings + r, j = (a + 1) * Rings + r;
                    // Clockwise seen from above, so the faces point up in Unity's left-handed frame.
                    Triangles.Add(i); Triangles.Add(j); Triangles.Add(i + 1);
                    Triangles.Add(j); Triangles.Add(j + 1); Triangles.Add(i + 1);
                }
            for (int r = 0; r < MapOut; r++)
                for (int a = 0; a < MapAround; a++)
                {
                    Vector3 p = Point(a / (float)MapAround, r / (float)(MapOut - 1));
                    Map[r * MapAround + a] = Use(p.x, p.z);
                }
        }

        /// <summary>Distance from the middle to the ground's edge along a bearing.</summary>
        private float Edge(float dx, float dz)
        {
            float tx = dx > 1e-6f ? (maxX - cx) / dx : dx < -1e-6f ? (minX - cx) / dx : float.MaxValue;
            float tz = dz > 1e-6f ? (maxZ - cz) / dz : dz < -1e-6f ? (minZ - cz) / dz : float.MaxValue;
            return Mathf.Min(tx, tz);
        }

        /// <summary>
        /// A point on the ring: <paramref name="u"/> once round from east, <paramref name="v"/> from
        /// 40 m inside the ground's edge (tucked under it) out to <see cref="Reach"/>, rings closer
        /// together near the ground.
        /// </summary>
        public Vector3 Point(float u, float v)
        {
            float t = u * Mathf.PI * 2f;
            float dx = Mathf.Cos(t), dz = Mathf.Sin(t);
            float edge = Edge(dx, dz);
            if (v <= 0f)
            {
                float r0 = edge - 40f;
                float x0 = cx + dx * r0, z0 = cz + dz * r0;
                return new Vector3(x0, c.TerrainHeight(x0, z0) - 3f, z0);
            }
            float w = (v * (Rings - 1) - 1f) / (Rings - 2);   // 0 at the ground's edge, 1 at the far end
            float r = edge + (Reach - edge) * Mathf.Pow(Mathf.Max(w, 0f), 1.7f);
            float x = cx + dx * r, z = cz + dz * r;
            float ex = cx + dx * edge, ez = cz + dz * edge;
            float start = c.TerrainHeight(Mathf.Clamp(ex, minX, maxX), Mathf.Clamp(ez, minZ, maxZ)) - 0.5f;
            float rise = Smooth((r - edge) / 1600f);
            return new Vector3(x, start + (Hill(x, z, r - edge) - start) * rise, z);
        }

        /// <summary>Rolling hills, growing taller further out.</summary>
        private float Hill(float x, float z, float out_)
        {
            float n = 0.55f * LandscapePlan.Noise(x / 1900f, z / 1900f, 71) + 0.3f * LandscapePlan.Noise(x / 800f, z / 800f, 72)
                + 0.15f * LandscapePlan.Noise(x / 330f, z / 330f, 73);
            float grow = 0.35f + 0.65f * Smooth(out_ / 3500f);
            return level - 6f + look.Hills * grow * n * 1.25f;
        }

        private LandUse Use(float x, float z)
        {
            float wood = LandscapePlan.Noise(x / 600f, z / 600f, 81) * 0.7f + LandscapePlan.Noise(x / 210f, z / 210f, 82) * 0.3f;
            if (wood < look.Forest * 0.85f + 0.1f) return LandUse.Forest;
            float f = LandscapePlan.Noise(x / 170f, z / 170f, 83);
            return f < 0.3f ? LandUse.Meadow : f < 0.55f ? LandUse.Pasture : f < 0.8f ? LandUse.Wheat : LandUse.Hay;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
