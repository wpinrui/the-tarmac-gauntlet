// Builds the team building outside Unity, checks every part and writes it as an OBJ (Unity's frame, metres).
using System;
using System.Globalization;
using System.IO;
using System.Text;
using TarmacGauntlet.TeamHq;
using UnityEngine;

internal static class Check
{
    private static int Main(string[] args)
    {
        var g = new HqGeometry();
        int fails = 0, tris = 0;
        void Fail(string m) { Console.WriteLine("FAIL " + m); fails++; }
        foreach (var kv in g.Parts)
        {
            HqPart p = kv.Value;
            if (p.Normals.Count != p.Vertices.Count || p.Uvs.Count != p.Vertices.Count) Fail(kv.Key + ": vertex, normal and uv counts differ");
            if (p.Triangles.Count == 0 || p.Triangles.Count % 3 != 0) Fail(kv.Key + ": bad triangle list");
            foreach (int t in p.Triangles) if (t < 0 || t >= p.Vertices.Count) { Fail(kv.Key + ": index out of range"); break; }
            foreach (Vector3 v in p.Vertices)
            {
                if (float.IsNaN(v.x + v.y + v.z)) { Fail(kv.Key + ": NaN vertex"); break; }
                // Inside the shell (Unity's frame flips Z), with a little give for wall overlays.
                if (Math.Abs(v.x) > HqGeometry.W / 2 + 0.01f || v.y < -0.01f || v.y > HqGeometry.H + 0.01f || Math.Abs(v.z) > HqGeometry.D / 2 + 0.01f)
                { Fail($"{kv.Key}: vertex outside the building at {v.x:F2},{v.y:F2},{v.z:F2}"); break; }
            }
            foreach (Vector3 n in p.Normals) if (Math.Abs(n.magnitude - 1) > 0.01f) { Fail(kv.Key + ": normal not unit length"); break; }
            // Each triangle's winding must agree with its vertex normals (clockwise front faces in Unity).
            int bad = 0;
            for (int i = 0; i < p.Triangles.Count; i += 3)
            {
                Vector3 a = p.Vertices[p.Triangles[i]], b = p.Vertices[p.Triangles[i + 1]], c = p.Vertices[p.Triangles[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f) continue;
                Vector3 n = p.Normals[p.Triangles[i]] + p.Normals[p.Triangles[i + 1]] + p.Normals[p.Triangles[i + 2]];
                if (Vector3.Dot(face, n) < 0) bad++;   // Unity's front faces wind clockwise: cross(b-a, c-a) points along the normal
            }
            bool doubleSided = kv.Key == "gold" || kv.Key == "silver" || kv.Key == "bronze";
            if (bad > 0 && !doubleSided) Fail($"{kv.Key}: {bad} triangles wound against their normals");
            tris += p.Triangles.Count / 3;
        }
        if (g.Bays.Count != 4) Fail("expected four bays");
        Console.WriteLine($"{g.Parts.Count} parts, {tris} triangles, {g.Lights.Count} lights, {fails} failures");

        if (args.Length > 0)
        {
            var obj = new StringBuilder();
            var mtl = new StringBuilder();
            int baseIndex = 1;
            CultureInfo ci = CultureInfo.InvariantCulture;
            obj.Append("mtllib hq.mtl\n");
            foreach (var kv in g.Parts)
            {
                HqPart p = kv.Value;
                obj.Append("o ").Append(kv.Key).Append('\n').Append("usemtl ").Append(kv.Key).Append('\n');
                mtl.Append("newmtl ").Append(kv.Key).Append('\n');
                // Back to a right-handed frame for the browser, so it reads like the viewer.
                foreach (Vector3 v in p.Vertices) obj.Append(string.Format(ci, "v {0} {1} {2}\n", v.x, v.y, -v.z));
                foreach (Vector3 n in p.Normals) obj.Append(string.Format(ci, "vn {0} {1} {2}\n", n.x, n.y, -n.z));
                foreach (Vector2 t in p.Uvs) obj.Append(string.Format(ci, "vt {0} {1}\n", t.x, t.y));
                for (int i = 0; i < p.Triangles.Count; i += 3)
                {
                    int a = p.Triangles[i] + baseIndex, b = p.Triangles[i + 2] + baseIndex, c = p.Triangles[i + 1] + baseIndex;
                    obj.Append($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n");
                }
                baseIndex += p.Vertices.Count;
            }
            Directory.CreateDirectory(args[0]);
            File.WriteAllText(Path.Combine(args[0], "hq.obj"), obj.ToString());
            File.WriteAllText(Path.Combine(args[0], "hq.mtl"), mtl.ToString());
        }
        return fails == 0 ? 0 : 1;
    }
}
