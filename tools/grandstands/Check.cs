// Builds the circuit and its grandstands exactly as the game does and checks them. Each rule prints one line: ok, or
// FAIL with the worst spots. Exit code 1 if any rule fails. Every stand shape is built and checked.
//
// Rules:
//   clear of the track    every stand vertex stands at least 2 m behind the armco of the nearest stretch of track, and
//                         every catch fence vertex behind the armco, so nothing stands on the runoff or the road
//   clear of the pits     nothing stands within 30 m of the pit lane, so the pit lane, garages and pit building stay clear
//   nothing buried        no stand, seat or fan sinks into the ground beyond the 0.5 m its walls and posts reach down
//   fenced                every metre along each stand has catch fence in front of it, between it and the track
//   modest crowd          2,500 to 5,000 fans in all
//   cheap to draw         at most 200k triangles and 350k vertices for the stands and crowd together, 40k triangles in
//                         any one mesh, and no mesh wider than 300 m, so the ones out of view are culled
//
// With a folder as the second argument it also writes the look preview there: preview.html and its data.js, the circuit
// near each stand and every shape of stand. Open preview.html from that folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    static Circuit circuit;
    static CircuitBuilder builder;
    static bool failed;
    static readonly Dictionary<(int, int), List<int>> centreCells = new Dictionary<(int, int), List<int>>();
    const float Cell = 25f;

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        circuit = new Circuit(data);
        builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> track = builder.Build();
        for (int k = 0; k < circuit.Count; k++)
        {
            var key = Key(circuit.Centre[k]);
            if (!centreCells.TryGetValue(key, out var list)) centreCells[key] = list = new List<int>();
            list.Add(k);
        }

        var builds = new[]
        {
            (MainStandShape.Cantilever, CornerStandShape.Bleachers),
            (MainStandShape.Barrel, CornerStandShape.Terrace),
            (MainStandShape.Open, CornerStandShape.Covered),
        };
        var built = new List<(MainStandShape main, CornerStandShape corner, Grandstands stands, List<CircuitPart> parts)>();
        foreach (var (main, corner) in builds)
        {
            var stands = new Grandstands(circuit, builder, null, main, corner);
            List<CircuitPart> parts = stands.Build();
            Console.WriteLine($"-- {main} main stand, {corner} corner stands");
            ClearOfTrack(parts);
            ClearOfPits(parts);
            NothingBuried(parts);
            Fenced(stands, parts);
            Crowd(stands);
            Budget(parts);
            built.Add((main, corner, stands, parts));
        }
        if (args.Length > 1) Preview(args[1], track, built.Select(b => (b.main, b.corner, b.stands, b.parts)).ToList());
        return failed ? 1 : 0;
    }

    static (int, int) Key(Vector3 p) => (Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

    /// <summary>The track sample nearest a point in plan, searching outwards cell by cell.</summary>
    static int Nearest(Vector3 p)
    {
        var (cx, cz) = Key(p);
        int best = -1;
        float bestD = float.MaxValue;
        for (int r = 0; r < 40; r++)
        {
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) != r) continue;
                    if (!centreCells.TryGetValue((x, z), out var list)) continue;
                    foreach (int k in list)
                    {
                        float d = Flat(p, circuit.Centre[k]);
                        if (d < bestD) { bestD = d; best = k; }
                    }
                }
            if (best >= 0 && bestD < (r - 1) * Cell) break;
        }
        return best;
    }

    static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    static IEnumerable<Vector3> Points(CircuitPart part) => part.Kit.Vertices;

    static void ClearOfTrack(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int count = 0;
        foreach (CircuitPart part in parts)
        {
            bool fence = part.Name.StartsWith("Catch fence");
            float need = fence ? 0.3f : 2f;
            foreach (Vector3 p in Points(part))
            {
                count++;
                int k = Nearest(p);
                float lateral = Vector3.Dot(p - circuit.Centre[k], circuit.Left[k]);
                int side = lateral > 0 ? 0 : 1;
                float armco = circuit.HalfWidth(k) + Math.Max(builder.RunoffReach(side, k), 0f);
                float behind = Mathf.Abs(lateral) - armco;
                // Fence tops lean in over the armco by design.
                if (fence && p.y > circuit.TerrainHeight(p.x, p.z) + 3.9f) behind += 1.05f;
                if (behind < need) bad.Add((need - behind, p, part.Name));
            }
        }
        Report("clear of the track", bad, count, "stand points", "m short");
    }

    static void ClearOfPits(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int count = 0;
        Vector3[] lane = circuit.Lane;
        foreach (CircuitPart part in parts)
            foreach (Vector3 p in Points(part))
            {
                count++;
                float best = float.MaxValue;
                for (int i = 0; i < lane.Length; i += 2) best = Math.Min(best, Flat(p, lane[i]));
                if (best < 30f) bad.Add((30f - best, p, part.Name));
            }
        Report("clear of the pits", bad, count, "stand points", "m short");
    }

    static void NothingBuried(List<CircuitPart> parts)
    {
        var bad = new List<(float, Vector3, string)>();
        int count = 0;
        foreach (CircuitPart part in parts)
        {
            // Seats and fans must sit fully above the ground; walls, posts and banks may reach 0.5 m into it.
            bool above = part.Name == "Crowd" || part.Name == "Stand seats";
            float allowed = above ? -0.02f : 0.55f;
            foreach (Vector3 p in Points(part))
            {
                count++;
                float under = circuit.TerrainHeight(p.x, p.z) - p.y;
                if (under > allowed) bad.Add((under - allowed, p, part.Name));
            }
        }
        Report("nothing buried", bad, count, "points", "m too deep");
    }

    /// <summary>Every metre along each stand's front has catch fence between it and the track.</summary>
    static void Fenced(Grandstands stands, List<CircuitPart> parts)
    {
        var fence = new List<Vector3>();
        foreach (CircuitPart part in parts) if (part.Name == "Catch fence") fence.AddRange(part.Kit.Vertices);
        var bad = new List<(float, Vector3, string)>();
        int count = 0;
        foreach (StandSite site in Grandstands.Sites)
        {
            int side = stands.SideOf(site);
            (float from, float to) = stands.Span(site);
            float length = Mathf.Repeat(to - from, circuit.Lap);
            for (float d = 0; d <= length; d += 1f)
            {
                count++;
                int k = circuit.IndexOf(from + d);
                float armco = circuit.HalfWidth(k) + builder.RunoffReach(side, k);
                Vector3 o = circuit.Left[k] * (side == 0 ? 1f : -1f);
                Vector3 behind = circuit.Centre[k] + o * (armco + 0.6f);
                float near = fence.Count == 0 ? float.MaxValue : fence.Min(f => Flat(f, behind));
                if (near > 2.5f) bad.Add((near, behind, site.Name));
            }
        }
        Report("fenced", bad, count, "metres of stand", "m to the nearest fence");
    }

    static void Crowd(Grandstands stands)
    {
        bool ok = stands.Fans >= 2500 && stands.Fans <= 5000;
        if (!ok) failed = true;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  modest crowd: {stands.Fans} fans");
    }

    static void Budget(List<CircuitPart> parts)
    {
        int tris = 0, verts = 0, worstTris = 0;
        float widest = 0;
        string worst = "";
        foreach (CircuitPart part in parts)
        {
            int t = part.Kit.Triangles.Count / 3;
            tris += t;
            verts += part.Kit.Vertices.Count;
            if (t > worstTris) { worstTris = t; worst = part.Name; }
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in part.Kit.Vertices)
            {
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
            }
            widest = Math.Max(widest, Math.Max(maxX - minX, maxZ - minZ));
        }
        bool ok = tris <= 200000 && verts <= 350000 && worstTris <= 40000 && widest <= 300f;
        if (!ok) failed = true;
        foreach (var g in parts.GroupBy(p => p.Name))
            Console.WriteLine($"        {g.Key}: {g.Sum(p => p.Kit.Triangles.Count / 3)} triangles, {g.Sum(p => p.Kit.Vertices.Count)} vertices");
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  cheap to draw: {parts.Count} meshes, {tris} triangles, {verts} vertices, most in one mesh {worstTris} ({worst}), widest {widest:F0} m");
    }

    static void Report(string rule, List<(float amount, Vector3 at, string part)> bad, int checkedCount, string what, string unit)
    {
        if (bad.Count == 0)
        {
            Console.WriteLine($"ok    {rule}: {checkedCount} {what}");
            return;
        }
        failed = true;
        var worst = bad.OrderByDescending(b => b.amount).Take(5)
            .Select(b => { int k = Nearest(b.at); return $"{b.part} at lap {k * circuit.Step:F0} m {b.amount:F2} {unit}"; });
        Console.WriteLine($"FAIL  {rule}: {bad.Count} of {checkedCount} {what}, worst: {string.Join(", ", worst)}");
    }

    // ---------------------------------------------------------------- preview data

    /// <summary>
    /// For each stand: the circuit around it and every shape of stand, in metres from the stand's middle quantised to
    /// 2 cm. Identical meshes are stored once. Written gzipped, as the look preview embeds it.
    /// </summary>
    static void Preview(string folder, List<CircuitPart> track, List<(MainStandShape main, CornerStandShape corner, Grandstands stands, List<CircuitPart> parts)> built)
    {
        Directory.CreateDirectory(folder);
        Grandstands first = built[0].stands;
        var centres = new List<Vector3>();
        foreach (StandSite site in Grandstands.Sites)
        {
            int side = first.SideOf(site);
            (float from, float to) = first.Span(site);
            int k = circuit.IndexOf(from + Mathf.Repeat(to - from, circuit.Lap) * 0.5f);
            Vector3 centre = circuit.Centre[k] + circuit.Left[k] * (side == 0 ? 45f : -45f);
            centre.y = circuit.TerrainHeight(centre.x, centre.z);
            centres.Add(centre);
        }
        int NearestSite(Vector3 p)
        {
            int best = 0;
            for (int i = 1; i < centres.Count; i++) if (Flat(p, centres[i]) < Flat(p, centres[best])) best = i;
            return best;
        }

        var blobs = new Dictionary<string, object>();
        string Store(string name, MeshKit kit, Vector3 origin)
        {
            (string id, object blob) = Encode(name, kit, origin);
            blobs[id] = blob;
            return id;
        }

        var sites = new List<object>();
        for (int s = 0; s < Grandstands.Sites.Length; s++)
        {
            StandSite site = Grandstands.Sites[s];
            Vector3 origin = centres[s];
            int side = first.SideOf(site);
            (float from, float to) = first.Span(site);
            float mid = from + Mathf.Repeat(to - from, circuit.Lap) * 0.5f;
            int k = circuit.IndexOf(mid);

            var circuitIds = new List<string>();
            foreach (var group in track.GroupBy(p => p.Name))
            {
                var kit = new MeshKit();
                float reach = group.Key == "Ground" ? 480f : 300f;
                foreach (CircuitPart part in group) Keep(part.Kit, kit, t => Flat(t, origin) < reach);
                if (!kit.IsEmpty) circuitIds.Add(Store(group.Key, kit, origin));
            }
            var shapes = new Dictionary<string, List<string>>();
            foreach (var b in built)
            {
                string shape = site.Main ? b.main.ToString() : b.corner.ToString();
                if (shapes.ContainsKey(shape)) continue;
                var ids = new List<string>();
                foreach (var group in b.parts.GroupBy(p => p.Name))
                {
                    var kit = new MeshKit();
                    foreach (CircuitPart part in group) Keep(part.Kit, kit, t => NearestSite(t) == s);
                    if (!kit.IsEmpty) ids.Add(Store(group.Key, kit, origin));
                }
                shapes[shape] = ids;
            }
            Vector3 o = circuit.Left[k] * (side == 0 ? 1f : -1f);
            sites.Add(new
            {
                name = site.Name,
                main = site.Main,
                track = V(circuit.Centre[k] - origin),
                outward = V(o),
                along = V(circuit.Forward[k]),
                circuit = circuitIds,
                shapes,
            });
        }
        var json = JsonSerializer.Serialize(new
        {
            sites,
            blobs,
            palette = new[] { CrowdPalette.Shirts, CrowdPalette.Skins, CrowdPalette.Trousers }
                .Select(row => row.Select(cl => new[] { cl.r, cl.g, cl.b }).ToArray()).ToArray(),
            paletteWidth = CrowdPalette.Width,
        });
        string path = Path.Combine(folder, "preview.json.gz");
        using (var file = File.Create(path))
        using (var zip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.SmallestSize))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            zip.Write(bytes, 0, bytes.Length);
        }
        File.WriteAllText(Path.Combine(folder, "data.js"), "window.STANDS_DATA=\"" + Convert.ToBase64String(File.ReadAllBytes(path)) + "\";\n");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "preview.html"), Path.Combine(folder, "preview.html"), true);
        Console.WriteLine($"wrote {path} ({new FileInfo(path).Length / 1024} KB, {json.Length / 1024} KB unzipped, {blobs.Count} meshes)");
    }

    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

    /// <summary>Copies the triangles whose centre passes a test into another kit.</summary>
    static void Keep(MeshKit from, MeshKit into, Func<Vector3, bool> keep)
    {
        var v = from.Vertices;
        var n = from.Normals;
        var uv = from.UVs;
        var t = from.Triangles;
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 mid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
            if (!keep(mid)) continue;
            into.Triangle(v[t[i]], v[t[i + 1]], v[t[i + 2]], n[t[i]], n[t[i + 1]], n[t[i + 2]], uv[t[i]], uv[t[i + 1]], uv[t[i + 2]]);
        }
    }

    /// <summary>
    /// One mesh as base64: positions as 2 cm steps in int16, triangles as uint32, and for the crowd each corner's shirt
    /// colour, so each fan's card shows flat in it. Corners at the same spot with the same normal are one vertex, so the preview's flat normals come out right.
    /// The id is a hash of the contents, so the same mesh is stored once.
    /// </summary>
    static (string id, object blob) Encode(string name, MeshKit kit, Vector3 origin)
    {
        var index = new Dictionary<(int, int, int, int, int, int), int>();
        var pos = new List<short>();
        var col = new List<byte>();
        var idx = new List<int>();
        var v = kit.Vertices;
        var n = kit.Normals;
        var uv = kit.UVs;
        bool crowd = name == "Crowd";
        foreach (int i in kit.Triangles)
        {
            Vector3 p = v[i] - origin, q = n[i];
            var key = ((int)Math.Round(p.x * 50), (int)Math.Round(p.y * 50), (int)Math.Round(p.z * 50),
                (int)Math.Round(q.x * 20), (int)Math.Round(q.y * 20), (int)Math.Round(q.z * 20));
            if (!index.TryGetValue(key, out int j))
            {
                j = pos.Count / 3;
                index[key] = j;
                pos.Add(checked((short)key.Item1)); pos.Add(checked((short)key.Item2)); pos.Add(checked((short)key.Item3));
                if (crowd) col.Add((byte)CrowdCard.Shirt(uv[i]));
            }
            idx.Add(j);
        }
        var posBytes = new byte[pos.Count * 2];
        Buffer.BlockCopy(pos.ToArray(), 0, posBytes, 0, posBytes.Length);
        var idxBytes = new byte[idx.Count * 4];
        Buffer.BlockCopy(idx.ToArray(), 0, idxBytes, 0, idxBytes.Length);
        string id;
        using (var sha = System.Security.Cryptography.SHA1.Create())
        {
            sha.TransformBlock(Encoding.UTF8.GetBytes(name), 0, name.Length, null, 0);
            sha.TransformBlock(posBytes, 0, posBytes.Length, null, 0);
            sha.TransformFinalBlock(idxBytes, 0, idxBytes.Length);
            id = Convert.ToBase64String(sha.Hash).Substring(0, 12);
        }
        return (id, new { name, pos = Convert.ToBase64String(posBytes), idx = Convert.ToBase64String(idxBytes), col = crowd ? Convert.ToBase64String(col.ToArray()) : null });
    }
}
