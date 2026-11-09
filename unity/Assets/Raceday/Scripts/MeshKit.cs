using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Accumulates triangles for one material and turns them into a mesh.</summary>
    public sealed class MeshKit
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public bool IsEmpty => triangles.Count == 0;

        /// <summary>
        /// A strip through rows of points. Every row has the same number of points, ordered
        /// from left to right as seen driving along the rows. UVs are metres across and along,
        /// divided by the tile sizes.
        /// </summary>
        public void Strip(IList<Vector3[]> rows, float tileAcross, float tileAlong, bool doubleSided = false)
        {
            if (rows.Count < 2) return;
            int cols = rows[0].Length;
            int start = vertices.Count;
            float along = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                if (r > 0) along += Vector3.Distance(Mid(rows[r]), Mid(rows[r - 1]));
                float across = 0;
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) across += Vector3.Distance(rows[r][c], rows[r][c - 1]);
                    vertices.Add(rows[r][c]);
                    uvs.Add(new Vector2(across / tileAcross, along / tileAlong));
                    Vector3 fwd = rows[Mathf.Min(r + 1, rows.Count - 1)][c] - rows[Mathf.Max(r - 1, 0)][c];
                    Vector3 side = rows[r][Mathf.Min(c + 1, cols - 1)] - rows[r][Mathf.Max(c - 1, 0)];
                    Vector3 n = Vector3.Cross(fwd, side);
                    if (cols == 1 || n.sqrMagnitude < 1e-8) n = Vector3.up;
                    normals.Add(n.normalized);
                }
            }
            for (int r = 0; r < rows.Count - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = start + r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    // Left-to-right columns with rows running forwards face up in Unity's winding.
                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                    triangles.Add(b); triangles.Add(d); triangles.Add(e);
                    if (doubleSided)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(d);
                        triangles.Add(b); triangles.Add(e); triangles.Add(d);
                    }
                }
            }
        }

        /// <summary>A flat quad from four corners: back-left, back-right, front-left, front-right.</summary>
        public void Quad(Vector3 backLeft, Vector3 backRight, Vector3 frontLeft, Vector3 frontRight, float tile)
        {
            Strip(new[] { new[] { backLeft, backRight }, new[] { frontLeft, frontRight } }, tile, tile);
        }

        /// <summary>A box with its sizes along the local right, up (world up) and forward axes.</summary>
        public void Box(Vector3 centre, Vector3 right, Vector3 forward, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + right * (x * h.x) + Vector3.up * (y * h.y) + forward * (z * h.z);
            Face(P(-1, -1, 1), P(1, -1, 1), P(-1, 1, 1), P(1, 1, 1), centre);
            Face(P(-1, -1, -1), P(1, -1, -1), P(-1, 1, -1), P(1, 1, -1), centre);
            Face(P(1, -1, -1), P(1, -1, 1), P(1, 1, -1), P(1, 1, 1), centre);
            Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, -1), P(-1, 1, 1), centre);
            Face(P(-1, 1, -1), P(1, 1, -1), P(-1, 1, 1), P(1, 1, 1), centre);
            Face(P(-1, -1, -1), P(1, -1, -1), P(-1, -1, 1), P(1, -1, 1), centre);
        }

        /// <summary>
        /// One quad with corners a, b along one edge and c, d along the opposite edge
        /// (a opposite c, b opposite d), facing away from <paramref name="inside"/>.
        /// </summary>
        public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Vector3 n = -Vector3.Cross(b - a, c - a).normalized;
            Vector3 mid = (a + b + c + d) * 0.25f;
            if (Vector3.Dot(n, mid - inside) < 0)
            {
                (a, b) = (b, a);
                (c, d) = (d, c);
                n = -n;
            }
            int s = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(n);
            float w = Vector3.Distance(a, b), hgt = Vector3.Distance(a, c);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(w, 0)); uvs.Add(new Vector2(0, hgt)); uvs.Add(new Vector2(w, hgt));
            triangles.Add(s); triangles.Add(s + 2); triangles.Add(s + 1);
            triangles.Add(s + 1); triangles.Add(s + 2); triangles.Add(s + 3);
        }

        /// <summary>
        /// Joins two rows with different numbers of points, ordered left to right as in <see cref="Strip"/>,
        /// stepping along whichever row is further behind so every point of both rows is used and the two
        /// rows' edges match the strips either side exactly. UVs are metres in plan over the tile size.
        /// </summary>
        public void Zip(Vector3[] back, Vector3[] front, float tile)
        {
            int i = 0, j = 0;
            while (i < back.Length - 1 || j < front.Length - 1)
            {
                bool stepBack = j >= front.Length - 1 || (i < back.Length - 1 && (i + 1) / (float)(back.Length - 1) <= (j + 1) / (float)(front.Length - 1));
                Vector3 a = back[i], d = front[j], b = stepBack ? back[i + 1] : front[j + 1];
                Vector3 n = Vector3.Cross(d - a, b - a).normalized;
                Vector2 U(Vector3 p) => new Vector2(p.x / tile, p.z / tile);
                Triangle(a, d, b, n, n, n, U(a), U(d), U(b));
                if (stepBack) i++; else j++;
            }
        }

        /// <summary>One triangle with its own corners, normals and UVs, wound as given.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int s = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(na); normals.Add(nb); normals.Add(nc);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            triangles.Add(s); triangles.Add(s + 1); triangles.Add(s + 2);
        }

        public IReadOnlyList<Vector3> Vertices => vertices;
        public IReadOnlyList<Vector3> Normals => normals;
        public IReadOnlyList<Vector2> UVs => uvs;
        public IReadOnlyList<int> Triangles => triangles;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Mid(Vector3[] row) => (row[0] + row[row.Length - 1]) * 0.5f;
    }
}
