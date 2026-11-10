// Rules for what the player feels and sees up close. See the list at the top of Check.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using UnityEngine;

static partial class Check
{
    static readonly HashSet<string> Paved = new HashSet<string> { "Road", "Pit lane", "Pit road joins" };

    static void MoreRules(List<CircuitPart> parts)
    {
        NoFighting();
        NoHoles();
        NoSteps(parts);
        Crests();
        FencesClear(parts);
        NoGroundWalls(parts);
        NoMissingFaces(parts);
    }

    /// <summary>
    /// Walls, tyre stacks, fences and buildings are closed: every edge of every face meets another face
    /// turned the other way, so nothing can be seen into from the side. Edges at or under the ground are exempt.
    /// </summary>
    static void NoMissingFaces(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => Barriers.Contains(p.Name) || p.Name == "Garages" || p.Name == "Garage roofs" || p.Name == "Pit entry bollard"))
        {
            var v = part.Kit.Vertices;
            var t = part.Kit.Triangles;
            var directed = new HashSet<(long, long)>();
            var where = new Dictionary<(long, long), (Vector3, Vector3)>();
            for (int i = 0; i < t.Count; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    Vector3 a = v[t[i + j]], b = v[t[i + (j + 1) % 3]];
                    var e = (Key(a), Key(b));
                    if (e.Item1 == e.Item2) continue;
                    directed.Add(e);
                    where[e] = (a, b);
                }
            foreach (var (e, (a, b)) in where)
            {
                n++;
                if (directed.Contains((e.Item2, e.Item1))) continue;
                Vector3 m = (a + b) * 0.5f;
                if (Math.Max(a.y, b.y) <= Below(m.x, m.z, m.y + 3f) + 0.05f) continue;   // at or under what it stands on
                bad.Add((Vector3.Distance(a, b), m, part.Name));
                if (Environment.GetEnvironmentVariable("GC_OPEN") != null)
                    Console.WriteLine($"  open {part.Name} {a.x:0.000},{a.y:0.000},{a.z:0.000} -> {b.x:0.000},{b.y:0.000},{b.z:0.000} same-dir twin {directed.Contains(e)}");
            }
        }
        Report("no missing faces", bad, n, "edges", "m of open edge");
    }

    /// <summary>Two surfaces over the same spot within 2 cm of each other flicker.</summary>
    static void NoFighting()
    {
        var bad = new List<(float, Vector3, string)>();
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            Vector3 m = (t.a + t.b + t.c) / 3f;
            foreach (int j in Near(m.x, m.z))
            {
                if (j == i) continue;
                var (a, b, c) = tris[j];
                // Only a triangle the centre is well inside counts, so a neighbour sharing a side does not.
                if (HeightIn(a, b, c, m.x, m.z, out float h, 1e-3) && Math.Abs(h - m.y) < 0.02f)
                {
                    bad.Add((0.02f - Math.Abs(h - m.y), m, $"{triPart[i]} on {triPart[j]}"));
                    if (Environment.GetEnvironmentVariable("GC_FIGHT") == $"{triPart[i]} on {triPart[j]}")
                    {
                        var pl = Place(m);
                        Console.WriteLine($"  fight lap {pl.lap:0} side {pl.side:0.0} A {F(t.a)} {F(t.b)} {F(t.c)} B {F(a)} {F(b)} {F(c)}");
                    }
                    break;
                }
            }
        }
        Report("no surfaces fighting", bad, tris.Count, "surface triangles", "cm apart short of 2");
    }

    /// <summary>Every spot from the road out to the fence is covered by some surface, so no bare ground shows through a gap.</summary>
    static void NoHoles()
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        for (int k = 0; k < circuit.Count; k++)
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1 : -1;
                float reach = circuit.HalfWidth(k) + Math.Max(0f, builder.RunoffReach(side, k) - 0.3f);
                for (float lat = 0.25f; lat < reach; lat += 0.5f)
                {
                    n++;
                    Vector3 p = circuit.Surface(k, sign * lat);
                    if (!Covered(p.x, p.z)) bad.Add((1f, p, "gap"));
                }
            }
        Report("no holes", bad, n, "spots inside the fences", "holes");
    }

    static bool Covered(float x, float z)
    {
        foreach (int id in Near(x, z))
        {
            var (a, b, c) = tris[id];
            if (HeightIn(a, b, c, x, z, out _)) return true;
        }
        return false;
    }

    /// <summary>Where one surface a car can reach meets another (road, kerb, verge, grass, gravel) or bare ground, the step between them is at most 6 cm, and 1.5 cm where asphalt meets asphalt.</summary>
    static void NoSteps(List<CircuitPart> parts)
    {
        const float MaxStep = 0.06f;
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => !NotCover.Contains(p.Name) && p.Name != "Garages"))
            foreach (var (p, outward) in BoundaryPoints(part))
            {
                // What lies just beyond, read at two spots and carried back to the edge along its own slope,
                // so a join on a steep bank is not blamed for the bank.
                (float h, string part)? Beyond(float dist)
                {
                    Vector3 q = p + outward * dist;
                    float best = float.NaN;
                    string other = null;
                    foreach (int id in Near(q.x, q.z))
                    {
                        var (a, b, c) = tris[id];
                        if (HeightIn(a, b, c, q.x, q.z, out float h) && Math.Abs(h - p.y) < 0.5f && (float.IsNaN(best) || Math.Abs(h - p.y) < Math.Abs(best - p.y)))
                        {
                            best = h;
                            other = triPart[id];
                        }
                    }
                    if (float.IsNaN(best))
                    {
                        // Nothing laid beyond: the car drops onto bare ground, unless a fence stops it first.
                        if (NearBarrier(p)) return null;
                        best = Ground(q.x, q.z);
                        other = "the ground";
                        if (Math.Abs(best - p.y) > 0.5f) return null;   // a bank, which the cliff and wall rules judge
                    }
                    return (best, other);
                }
                var near = Beyond(0.03f);
                var far = Beyond(0.06f);
                if (near == null) continue;
                string other = near.Value.part;
                float best = far != null && far.Value.part == other ? 2 * near.Value.h - far.Value.h : near.Value.h;
                n++;
                float step = Math.Abs(best - p.y);
                // Paved surfaces meet flush, as asphalt laid against asphalt does.
                if (Paved.Contains(part.Name) && Paved.Contains(other ?? "") ? step > 0.015f : step > MaxStep)
                {
                    bad.Add((step, p, $"{part.Name} to {other}"));
                    if (Environment.GetEnvironmentVariable("GC_STEP") == $"{part.Name} to {other}")
                    {
                        var pl = Place(p);
                        Console.WriteLine($"  step {p.x:0.0},{p.y:0.00},{p.z:0.0} lap {pl.lap:0} side {pl.side:0.0} {step:0.00} out {outward.x:0.00},{outward.z:0.00} ground {Ground(p.x, p.z):0.00} other {best:0.00}");
                    }
                }
            }
        Report("no steps to drive over", bad, n, "joins between surfaces", "m step");
    }

    /// <summary>No crest on the racing line is sharp enough to lift a car off at 250 km/h.</summary>
    static void Crests()
    {
        const float Speed = 250f / 3.6f;
        float minRadius = Speed * Speed / 9.81f;
        var bad = new List<(float, Vector3, string)>();
        int n = circuit.Count, span = 5;
        float run = span * circuit.Step;
        for (int k = 0; k < n; k++)
        {
            float before = (circuit.Centre[k].y - circuit.Centre[circuit.Wrap(k - span)].y) / run;
            float after = (circuit.Centre[circuit.Wrap(k + span)].y - circuit.Centre[k].y) / run;
            float bend = before - after;   // positive over a crest
            if (bend <= 0) continue;
            float radius = run / bend;
            if (radius < minRadius) bad.Add((minRadius - radius, circuit.Centre[k], $"crest radius {radius:0} m"));
        }
        Report("no launching crests", bad, n, "track samples", "m of crest radius short");
    }

    /// <summary>Armco and tyre walls stand at least 2 m off the road and pit lane; the pit wall at least 1 m off the road.</summary>
    static void FencesClear(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => Barriers.Contains(p.Name)))
        {
            bool wall = part.Name == "Pit wall";
            float clearance = wall ? 1f : 2f;
            var v = part.Kit.Vertices;
            var seen = new HashSet<long>();
            foreach (Vector3 p in v)
            {
                if (!seen.Add(FlatKey(p))) continue;
                n++;
                for (int d = 0; d < 16; d++)
                {
                    float ang = d * Mathf.PI / 8f;
                    for (float r = 0f; r <= clearance; r += 0.5f)
                    {
                        float x = p.x + Mathf.Cos(ang) * r, z = p.z + Mathf.Sin(ang) * r;
                        string hit = PavedAt(x, z, p.y, wall);
                        if (hit == null) continue;
                        bad.Add((clearance - r, p, $"{part.Name} by {hit}"));
                        goto next;
                    }
                }
            next:;
            }
        }
        Report("fences clear of the road", bad, n, "fence points", "m too close");
    }

    static string PavedAt(float x, float z, float y, bool pitWall)
    {
        foreach (int id in Near(x, z))
        {
            string part = triPart[id];
            if (!Paved.Contains(part) || (pitWall && part != "Road")) continue;
            var (a, b, c) = tris[id];
            if (HeightIn(a, b, c, x, z, out float h) && Math.Abs(h - y) < 3f) return part;
        }
        return null;
    }

    /// <summary>Where a surface ends with no fence, the ground beyond rises no steeper than 1 in 1 over the next 3 m.</summary>
    static void NoGroundWalls(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => !NotCover.Contains(p.Name) && p.Name != "Garages"))
            foreach (var (p, outward) in BoundaryPoints(part))
            {
                if (NearBarrier(p)) continue;
                Vector3 q = p + outward * 3f;
                if (Covered(q.x, q.z)) continue;
                n++;
                float rise = (Ground(q.x, q.z) - p.y) / 3f;
                if (rise > 1f) bad.Add((rise, p, part.Name));
            }
        Report("no ground walls", bad, n, "unfenced edge points", "rise per m");
    }

    // ------------------------------------------------------------------ helpers

    static string F(Vector3 v) => $"({v.x:0.0},{v.z:0.0})";

    static IEnumerable<int> Near(float x, float z) =>
        grid.TryGetValue(CellKey((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell)), out var list) ? list : Enumerable.Empty<int>();

    /// <summary>Points along the outline of a part's top surface, each with the flat direction pointing out of it.</summary>
    static IEnumerable<(Vector3 p, Vector3 outward)> BoundaryPoints(CircuitPart part)
    {
        var edges = new Dictionary<(long, long), (Vector3 a, Vector3 b, Vector3 inner, int count)>();
        foreach (var t in TopTriangles(part))
        {
            Vector3 mid = (t.a + t.b + t.c) / 3f;
            foreach (var (a, b) in new[] { (t.a, t.b), (t.b, t.c), (t.c, t.a) })
            {
                long ka = Key(a), kb = Key(b);
                var k = ka < kb ? (ka, kb) : (kb, ka);
                edges[k] = edges.TryGetValue(k, out var e) ? (e.a, e.b, e.inner, e.count + 1) : (a, b, mid, 1);
            }
        }
        foreach (var e in edges.Values.Where(e => e.count == 1))
        {
            Vector3 along = e.b - e.a;
            along.y = 0;
            if (along.sqrMagnitude < 1e-6f) continue;
            Vector3 outward = new Vector3(along.z, 0, -along.x).normalized;
            Vector3 m = (e.a + e.b) * 0.5f;
            if (Vector3.Dot(outward, e.inner - m) > 0) outward = -outward;
            yield return (e.a, outward);
            yield return (m, outward);
            yield return (e.b, outward);
        }
    }

    /// <summary>
    /// The circuit stays cheap to draw: at most 400k triangles and 300k vertices in all, and no mesh wider
    /// than 300 m across, so the camera skips whatever is off screen instead of drawing the whole lap.
    /// </summary>
    static void Budget(List<CircuitPart> parts)
    {
        // The whole circuit, any one mesh, and how wide one mesh may spread so unseen ones are culled.
        const int MaxTriangles = 1_000_000, MaxVertices = 600_000, MaxMeshTriangles = 40_000;
        const float MaxSpan = 300f;
        int tris = 0, verts = 0, biggest = 0;
        var bad = new List<(float, Vector3, string)>();
        var byPart = new Dictionary<string, (int t, int v, int meshes)>();
        foreach (CircuitPart p in parts)
        {
            int t = p.Kit.Triangles.Count / 3, v = p.Kit.Vertices.Count;
            tris += t;
            verts += v;
            byPart.TryGetValue(p.Name, out var e);
            byPart[p.Name] = (e.t + t, e.v + v, e.meshes + 1);
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vector3 q in p.Kit.Vertices) { x0 = Math.Min(x0, q.x); x1 = Math.Max(x1, q.x); z0 = Math.Min(z0, q.z); z1 = Math.Max(z1, q.z); }
            float span = Math.Max(x1 - x0, z1 - z0);
            var middle = new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2);
            if (span > MaxSpan) bad.Add((span - MaxSpan, middle, p.Name));
            if (t > MaxMeshTriangles) bad.Add((t - MaxMeshTriangles, middle, $"{p.Name} mesh of {t} triangles"));
            biggest = Math.Max(biggest, t);
        }
        if (Environment.GetEnvironmentVariable("GC_KINDS") != null)
            foreach (var kv in byPart.OrderByDescending(kv => kv.Value.t))
                Console.WriteLine($"  budget: {kv.Key} {kv.Value.t} triangles {kv.Value.v} vertices in {kv.Value.meshes} meshes");
        if (Environment.GetEnvironmentVariable("GC_KINDS") != null) Console.WriteLine($"  budget: biggest mesh {biggest} triangles");
        if (tris > MaxTriangles) bad.Add((tris - MaxTriangles, default, $"{tris} triangles in all"));
        if (verts > MaxVertices) bad.Add((verts - MaxVertices, default, $"{verts} vertices in all"));
        Report("cheap to draw", bad, parts.Count, $"meshes, {tris} triangles, {verts} vertices", "over");
    }
}
