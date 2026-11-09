// Builds the circuit exactly as the game does (CircuitBuilder from circuit.json) and checks that what the
// player can see sits right. Each rule prints one line: ok, or FAIL with the worst spots as lap distance and
// side. Exit code 1 if any rule fails.
//
// Rules:
//   surfaces over ground  every flat surface (road, pit lane, kerbs, verges, runoff, gravel, infield) lies over
//                         the ground mesh, never under it, so no ground pokes through
//   no cliffs             where a flat surface ends, the next thing outside it (another surface or the ground,
//                         carried back to the edge along its slope) is at most 0.4 m lower, so no road or
//                         runoff edge stands up off the ground
//   barriers and buildings grounded  every armco, tyre wall, pit wall, garage and bollard reaches down into what it
//                         stands on, so no gap shows under it
//   fences never cross    no armco or pit wall line crosses another, or itself, seen from above
//   fences run smooth     along each armco or pit wall line no joint turns more than 30 degrees, and no piece
//                         is shorter than 30 m, so no fence zigzags or stands as a stray stub
//   no surfaces fighting  no two surfaces lie within 2 cm of each other, so none flickers through another
//   no holes              every spot from the road out to the fence is covered, so no bare ground shows through
//   no steps to drive over  where two surfaces meet, or a surface meets bare ground, the step is at most 6 cm,
//                         and 1.5 cm where asphalt meets asphalt
//   no launching crests   no crest on the racing line is sharp enough to lift a car at 250 km/h
//   fences clear of the road  no fence stands on or leans over the road or pit lane
//   no ground walls       past an unfenced runoff edge the ground rises no steeper than 1 in 1
//   no missing faces      fences and buildings are closed solids, with no open face to see through
// GC_KINDS=1 lists failures per pair of parts; GC_STEP or GC_FIGHT set to such a pair lists its spots.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static partial class Check
{
    const float Cliff = 0.4f;
    const float Cell = 2f;

    static readonly HashSet<string> Barriers = new HashSet<string> { "Armco", "Tyre walls", "Pit wall" };
    // Not part of the ground cover: paint sits on top of other surfaces, buildings stand on their own.
    static readonly HashSet<string> NotCover = new HashSet<string>
    {
        "Ground", "Edge lines", "Pit lane lines", "Pit entry hatching", "Pit exit line", "Pit box lines",
        "Start line and safety car lines", "Grid slots", "Grid yellow lines", "Garage roofs",
        "Pit entry bollard", "Road edges", "Armco", "Tyre walls", "Pit wall",
    };

    static Circuit circuit;
    static CircuitBuilder builder;
    static readonly List<(Vector3 a, Vector3 b, Vector3 c)> tris = new List<(Vector3, Vector3, Vector3)>();
    static readonly List<string> triPart = new List<string>();
    static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    static bool failed;

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        circuit = new Circuit(data);
        builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> parts = builder.Build();

        foreach (CircuitPart p in parts.Where(p => !NotCover.Contains(p.Name)))
            foreach (var t in TopTriangles(p)) Add(t, p.Name);

        IndexBarriers(parts);
        SurfacesOverGround(parts);
        NoCliffs(parts);
        BarriersGrounded(parts);
        FenceRules(parts);
        MoreRules(parts);
        return failed ? 1 : 0;
    }

    // ------------------------------------------------------------------ rules

    static void SurfacesOverGround(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            foreach (Vector3 p in new[] { t.a, t.b, t.c, (t.a + t.b + t.c) / 3f, (t.a + t.b) / 2f, (t.b + t.c) / 2f, (t.c + t.a) / 2f })
            {
                n++;
                float under = Ground(p.x, p.z) - p.y;
                if (under > -0.01f) bad.Add((under, p, triPart[i]));
            }
        }
        Report("surfaces over ground", bad, n, "points", "m under the ground");
    }

    static void NoCliffs(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => !NotCover.Contains(p.Name) && p.Name != "Garages"))
        {
            var top = TopTriangles(part).ToList();
            var edges = new Dictionary<(long, long), (Vector3 a, Vector3 b, Vector3 inner, int count)>();
            foreach (var t in top)
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
                foreach (Vector3 p in new[] { e.a, m, e.b })
                {
                    n++;
                    Vector3 probe = p + outward * 0.25f;
                    // Ground above the edge is the other rule's fault, not a cliff; a barrier standing
                    // along the edge hides any drop behind it.
                    if (Ground(probe.x, probe.z) > p.y || NearBarrier(p)) continue;
                    // The step is measured against what lies beyond, carried back to the edge along its
                    // own slope, so an edge on a steep bank isn't blamed for the bank.
                    float below = Below(probe.x, probe.z, p.y + 0.05f);
                    Vector3 further = p + outward * 0.5f;
                    float next = Below(further.x, further.z, below + 0.05f);
                    float atEdge = float.IsNegativeInfinity(next) ? below : below + Math.Max(0f, below - next);
                    float drop = p.y - atEdge;
                    if (drop > Cliff) bad.Add((drop, p, part.Name));
                    if (drop > Cliff && Environment.GetEnvironmentVariable("GC_DEBUG") != null)
                        Console.WriteLine($"  {part.Name} edge {p.x:0.0},{p.y:0.00},{p.z:0.0} out {outward.x:0.00},{outward.z:0.00} below {below:0.00} atEdge {atEdge:0.00} ground {Ground(probe.x, probe.z):0.00} at edge {Ground(p.x, p.z):0.00}");
                }
            }
        }
        Report("no cliffs", bad, n, "edge points", "m drop");
    }

    static readonly HashSet<(int, int)> barrierCells = new HashSet<(int, int)>();

    /// <summary>True when an upright barrier face passes within half a metre of p.</summary>
    static bool NearBarrier(Vector3 p)
    {
        int x = (int)Math.Floor(p.x * 2), z = (int)Math.Floor(p.z * 2);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (barrierCells.Contains((x + i, z + j))) return true;
        return false;
    }

    static void IndexBarriers(List<CircuitPart> parts)
    {
        // Garage walls stop a car as a barrier does; their floor slabs do not.
        foreach (CircuitPart part in parts.Where(p => Barriers.Contains(p.Name) || p.Name == "Garages"))
        {
            var v = part.Kit.Vertices;
            var t = part.Kit.Triangles;
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (part.Name == "Garages" && Math.Max(a.y, Math.Max(b.y, c.y)) - Math.Min(a.y, Math.Min(b.y, c.y)) < 3f) continue;
                // Mark half-metre cells along each edge of the triangle.
                foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                {
                    int steps = (int)Math.Ceiling((q - p).magnitude * 4) + 1;
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector3 m = Vector3.Lerp(p, q, s / (float)steps);
                        barrierCells.Add(((int)Math.Floor(m.x * 2), (int)Math.Floor(m.z * 2)));
                    }
                }
            }
        }
    }

    static void BarriersGrounded(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int n = 0;
        foreach (CircuitPart part in parts.Where(p => Barriers.Contains(p.Name) || p.Name == "Garages" || p.Name == "Pit entry bollard"))
        {
            // Lowest point of each upright face of the barrier at each spot along it.
            var lowest = new Dictionary<(int, int), Vector3>();
            var v3 = part.Kit.Vertices;
            var tr = part.Kit.Triangles;
            for (int i = 0; i < tr.Count; i += 3)
            {
                Vector3 nrm = Vector3.Cross(v3[tr[i + 1]] - v3[tr[i]], v3[tr[i + 2]] - v3[tr[i]]);
                if (nrm.sqrMagnitude < 1e-10f || Math.Abs(nrm.y) / nrm.magnitude > 0.5f) continue;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 v = v3[tr[i + j]];
                    var k = ((int)Math.Round(v.x * 20), (int)Math.Round(v.z * 20));
                    if (!lowest.TryGetValue(k, out Vector3 l) || v.y < l.y) lowest[k] = v;
                }
            }
            foreach (Vector3 v in lowest.Values)
            {
                n++;
                // A foot right on the edge of what it stands on counts as standing on it.
                float floor = new[] { (0f, 0f), (0.05f, 0f), (-0.05f, 0f), (0f, 0.05f), (0f, -0.05f) }
                    .Max(o => Below(v.x + o.Item1, v.z + o.Item2, v.y + 3f));
                float gap = v.y - floor;
                if (gap > 0.02f) bad.Add((gap, v, part.Name));
            }
        }
        Report("barriers and buildings grounded", bad, n, "feet", "m gap underneath");
    }

    static readonly HashSet<string> Fences = new HashSet<string> { "Armco", "Pit wall" };
    const float MaxTurn = 30f;
    const float MinPiece = 30f;

    static void FenceRules(List<CircuitPart> parts)
    {
        // The top edge of each upright face, seen from above.
        var segs = new List<(Vector3 a, Vector3 b, long ka, long kb, string part)>();
        var seen = new HashSet<(long, long)>();
        foreach (CircuitPart part in parts.Where(p => Fences.Contains(p.Name)))
        {
            var v = part.Kit.Vertices;
            var t = part.Kit.Triangles;
            for (int i = 0; i < t.Count; i += 3)
            {
                var tri = new[] { v[t[i]], v[t[i + 1]], v[t[i + 2]] }.OrderByDescending(p => p.y).ToArray();
                Vector3 nrm = Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]);
                if (nrm.sqrMagnitude < 1e-10f || Math.Abs(nrm.y) / nrm.magnitude > 0.5f) continue;
                if (tri[1].y - tri[2].y < 0.3f) continue;
                long ka = FlatKey(tri[0]), kb = FlatKey(tri[1]);
                if (ka == kb) continue;
                Vector3 run = tri[1] - tri[0];
                run.y = 0;
                if (run.magnitude < 0.7f) continue;   // the end cap across a wall, not part of its line
                var k = ka < kb ? (ka, kb) : (kb, ka);
                if (seen.Add(k)) segs.Add((tri[0], tri[1], ka, kb, part.Name));
            }
        }

        // Crossings.
        var cells = new Dictionary<long, List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            var (a, b, _, _, _) = segs[i];
            for (int x = (int)Math.Floor(Math.Min(a.x, b.x) / 4); x <= (int)Math.Floor(Math.Max(a.x, b.x) / 4); x++)
                for (int z = (int)Math.Floor(Math.Min(a.z, b.z) / 4); z <= (int)Math.Floor(Math.Max(a.z, b.z) / 4); z++)
                {
                    long ck = CellKey(x, z);
                    if (!cells.TryGetValue(ck, out var list)) cells[ck] = list = new List<int>();
                    list.Add(i);
                }
        }
        var crossings = new List<(float, Vector3, string)>();
        var tested = new HashSet<(int, int)>();
        foreach (var list in cells.Values)
            for (int x = 0; x < list.Count; x++)
                for (int y = x + 1; y < list.Count; y++)
                {
                    int i = Math.Min(list[x], list[y]), j = Math.Max(list[x], list[y]);
                    if (!tested.Add((i, j))) continue;
                    var s1 = segs[i];
                    var s2 = segs[j];
                    if (s1.ka == s2.ka || s1.ka == s2.kb || s1.kb == s2.ka || s1.kb == s2.kb) continue;
                    if (Crosses(s1.a, s1.b, s2.a, s2.b, out Vector3 at)) crossings.Add((1f, at, s1.part));
                }
        Report("fences never cross", crossings, segs.Count, "fence segments", "crossings");

        // Joints and pieces.
        var ends = new Dictionary<long, List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            foreach (long k in new[] { segs[i].ka, segs[i].kb })
            {
                if (!ends.TryGetValue(k, out var list)) ends[k] = list = new List<int>();
                list.Add(i);
            }
        }
        var rough = new List<(float, Vector3, string)>();
        int joints = 0;
        foreach (var (key, list) in ends)
        {
            if (list.Count != 2) continue;
            joints++;
            var s1 = segs[list[0]];
            var s2 = segs[list[1]];
            Vector3 at = s1.ka == key ? s1.a : s1.b;
            Vector3 d1 = (s1.ka == key ? s1.b : s1.a) - at, d2 = (s2.ka == key ? s2.b : s2.a) - at;
            d1.y = 0; d2.y = 0;
            // Straight on is 180 degrees between the two directions out of the joint.
            float cos = Vector3.Dot(d1, d2) / (d1.magnitude * d2.magnitude);
            float turn = 180f - (float)(Math.Acos(Math.Clamp(cos, -1f, 1f)) * 180 / Math.PI);
            if (turn > MaxTurn) rough.Add((turn, at, s1.part));
            if (turn > MaxTurn && Environment.GetEnvironmentVariable("GC_DEBUG") != null)
                Console.WriteLine($"  {s1.part} joint {at.x:0.0},{at.y:0.0},{at.z:0.0} turns {turn:0}");
        }
        var parent = Enumerable.Range(0, segs.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        foreach (var list in ends.Values)
            for (int i = 1; i < list.Count; i++) parent[Find(list[i])] = Find(list[0]);
        var length = new Dictionary<int, float>();
        for (int i = 0; i < segs.Count; i++)
        {
            Vector3 d = segs[i].b - segs[i].a;
            d.y = 0;
            length[Find(i)] = length.GetValueOrDefault(Find(i)) + d.magnitude;
        }
        foreach (var (root, len) in length)
            if (len < MinPiece) rough.Add((MinPiece - len, segs[root].a, segs[root].part));
        Report("fences run smooth", rough, joints, "fence joints", "degrees too sharp or m too short");
    }

    static long FlatKey(Vector3 p) => ((long)Math.Round(p.x * 100) << 32) ^ (uint)(int)Math.Round(p.z * 100);

    static bool Crosses(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 at)
    {
        at = default;
        float rx = b.x - a.x, rz = b.z - a.z, sx = d.x - c.x, sz = d.z - c.z;
        float den = rx * sz - rz * sx;
        if (Math.Abs(den) < 1e-9f) return false;
        float t = ((c.x - a.x) * sz - (c.z - a.z) * sx) / den;
        float u = ((c.x - a.x) * rz - (c.z - a.z) * rx) / den;
        if (t <= 1e-4f || t >= 1 - 1e-4f || u <= 1e-4f || u >= 1 - 1e-4f) return false;
        at = Vector3.Lerp(a, b, t);
        return true;
    }

    // ------------------------------------------------------------------ geometry

    static IEnumerable<(Vector3 a, Vector3 b, Vector3 c)> TopTriangles(CircuitPart p)
    {
        var v = p.Kit.Vertices;
        var t = p.Kit.Triangles;
        var seen = new HashSet<(long, long, long)>();
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f || n.y / n.magnitude < 0.5f) continue;   // facing up
            var ks = new[] { Key(a), Key(b), Key(c) };
            Array.Sort(ks);
            if (!seen.Add((ks[0], ks[1], ks[2]))) continue;   // double-sided copies
            yield return (a, b, c);
        }
    }

    static long Key(Vector3 p) => ((long)Math.Round(p.x * 1000) * 2000003L) ^ ((long)Math.Round(p.z * 1000) * 1000003L) ^ (long)Math.Round(p.y * 1000);

    static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

    static void Add((Vector3 a, Vector3 b, Vector3 c) t, string part)
    {
        int id = tris.Count;
        tris.Add(t);
        triPart.Add(part);
        int x0 = (int)Math.Floor(Math.Min(t.a.x, Math.Min(t.b.x, t.c.x)) / Cell), x1 = (int)Math.Floor(Math.Max(t.a.x, Math.Max(t.b.x, t.c.x)) / Cell);
        int z0 = (int)Math.Floor(Math.Min(t.a.z, Math.Min(t.b.z, t.c.z)) / Cell), z1 = (int)Math.Floor(Math.Max(t.a.z, Math.Max(t.b.z, t.c.z)) / Cell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                long k = CellKey(x, z);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                list.Add(id);
            }
    }

    /// <summary>The highest surface (ground cover or the ground itself) at x, z that is not above limit.</summary>
    static float Below(float x, float z, float limit)
    {
        float best = Ground(x, z);
        if (best > limit) best = float.NegativeInfinity;
        if (grid.TryGetValue(CellKey((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell)), out var list))
            foreach (int id in list)
            {
                var (a, b, c) = tris[id];
                if (HeightIn(a, b, c, x, z, out float h) && h <= limit && h > best) best = h;
            }
        return best;
    }

    static bool HeightIn(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float h, double inside = -1e-3)
    {
        h = 0;
        // In double, relative to c, and with a margin in metres (negative reaches past the sides): the builder cuts surfaces into pieces
        // a few centimetres across, where a tolerance in barycentric terms would be far too tight.
        double ax = a.x - c.x, az = a.z - c.z, bx = b.x - c.x, bz = b.z - c.z, px = x - c.x, pz = z - c.z;
        double d = bz * ax - bx * az;
        if (Math.Abs(d) < 1e-9) return false;
        double u = (bz * px - bx * pz) / d;
        double v = (-az * px + ax * pz) / d;
        double w = 1 - u - v;
        // Each weight times the corner's height above the opposite side is the distance in from that side.
        double la = Math.Sqrt(bx * bx + bz * bz), lb = Math.Sqrt(ax * ax + az * az);
        double lc = Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
        if (u * Math.Abs(d) / la < inside || v * Math.Abs(d) / lb < inside || w * Math.Abs(d) / lc < inside) return false;
        h = (float)(u * a.y + v * b.y + w * c.y);
        return true;
    }

    /// <summary>Height of the ground mesh as drawn: each 8 m cell split from (x+1, z) to (x, z+1).</summary>
    static float Ground(float x, float z)
    {
        GroundData t = circuit.Data.terrain;
        float fx = Math.Clamp((x - t.originX) / t.spacing, 0, t.countX - 1.0001f);
        float fz = Math.Clamp((z - t.originZ) / t.spacing, 0, t.countZ - 1.0001f);
        int ix = (int)fx, iz = (int)fz;
        float u = fx - ix, v = fz - iz;
        float a = circuit.TerrainHeightAt(ix, iz), b = circuit.TerrainHeightAt(ix + 1, iz);
        float d = circuit.TerrainHeightAt(ix, iz + 1), e = circuit.TerrainHeightAt(ix + 1, iz + 1);
        return u + v <= 1 ? a + (b - a) * u + (d - a) * v : e + (d - e) * (1 - u) + (b - e) * (1 - v);
    }

    // ------------------------------------------------------------------ report

    static void Report(string rule, List<(float amount, Vector3 at, string part)> bad, int checkedCount, string what, string unit)
    {
        if (bad.Count == 0)
        {
            Console.WriteLine($"ok    {rule}: {checkedCount} {what}");
            return;
        }
        failed = true;
        if (Environment.GetEnvironmentVariable("GC_KINDS") != null)
            foreach (var g in bad.GroupBy(b => b.part).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {rule}: {g.Key} x{g.Count()} worst {g.Max(b => b.amount):0.00}");
        // Worst spot per 50 m of lap, worst first.
        var worst = bad.Select(b => (b.amount, b.part, place: Place(b.at)))
            .GroupBy(b => (int)(b.place.lap / 50))
            .Select(g => g.OrderByDescending(b => b.amount).First())
            .OrderByDescending(b => b.amount).Take(8)
            .Select(b => $"{b.part} at lap {b.place.lap:0} m {Math.Abs(b.place.side):0} m {(b.place.side >= 0 ? "left" : "right")} {b.amount:0.00}");
        Console.WriteLine($"FAIL  {rule}: {bad.Count} of {checkedCount} {what} ({100.0 * bad.Count / checkedCount:0.0}%), worst {unit}: {string.Join(", ", worst)}");
    }

    static (float lap, float side) Place(Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int k = 0; k < circuit.Count; k++)
        {
            Vector3 d = circuit.Centre[k] - p;
            d.y = 0;
            if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = k; }
        }
        return (best * circuit.Step, Vector3.Dot(p - circuit.Centre[best], circuit.Left[best]));
    }
}
