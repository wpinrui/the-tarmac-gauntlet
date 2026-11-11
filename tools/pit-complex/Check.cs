// Builds the pit complex exactly as the game does (CircuitBuilder, then PitComplexBuilder) and checks
// it. Each rule prints one line: ok, or FAIL with the worst spots. Exit code 1 if any rule fails.
//
// Rules:
//   cheap to draw      the pit complex is at most 250k triangles and 300k vertices, 40k triangles in any
//                      one mesh, and no mesh spread wider than 300 m, so the meshes out of view are culled
//   closed solids      every part but the glass is closed: every edge of every face meets another face, so nothing
//                      shows a hole from any side
//   crew front clear   inside every garage the front ClearDepth metres between x = -5.5 and 5.5 (or 1.1 m short of the +x wall at the door) hold no fit-out below
//                      2.2 m, so the crew can stand there and run out to the box
//   clear of the walls nothing in a garage stands in its side walls, which round the bend sit well inside half the
//                      width
//   on the garages     the pit building's lowest corners reach into the garage roof slabs, never floating over them
//                      or hanging far below them
//
// With an output folder as the second argument it also writes the preview's meshes there (preview.json and
// preview.bin), cut to the garages round the start line.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public sealed class PitMaterials
    {
        public Material Concrete, Glass, Steel, Palette, Fit, Metal, Rubber, Lamps;
    }
}

static class Check
{
    static bool failed;

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var circuit = new Circuit(data);
        var builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> track = builder.Build();
        var pc = new PitComplex(circuit, builder.GarageFloors);
        var looks = new Dictionary<string, List<CircuitPart>>();
        looks["Pit complex"] = new PitComplexBuilder(pc, null).Build();

        Cheap(looks);
        Closed(looks);
        CrewClear(pc, looks.Values.First());
        ClearOfWalls(pc, looks.Values.First());
        OnGarages(pc, looks);
        if (args.Length > 1) Preview.Write(args[1], circuit, pc, track, looks);
        return failed ? 1 : 0;
    }

    static void Report(string rule, List<string> bad, string ok)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {ok}");
        else
        {
            failed = true;
            Console.WriteLine($"FAIL  {rule}: {bad.Count} failures, {string.Join("; ", bad.Take(8))}");
        }
    }

    static void Cheap(Dictionary<string, List<CircuitPart>> looks)
    {
        var bad = new List<string>();
        string summary = "";
        foreach (var (name, parts) in looks)
        {
            int tris = parts.Sum(p => p.Kit.Triangles.Count / 3), verts = parts.Sum(p => p.Kit.Vertices.Count);
            if (tris > 250_000 || verts > 300_000) bad.Add($"{name} {tris} triangles {verts} vertices");
            foreach (CircuitPart p in parts)
            {
                if (p.Kit.Triangles.Count / 3 > 40_000) bad.Add($"{name} {p.Name} {p.Kit.Triangles.Count / 3} triangles");
                IReadOnlyList<Vector3> v = p.Kit.Vertices;
                float span = Math.Max(v.Max(q => q.x) - v.Min(q => q.x), v.Max(q => q.z) - v.Min(q => q.z));
                if (span > 300f) bad.Add($"{name} {p.Name} {span:0} m wide");
            }
            summary += $"{name} {parts.Count} meshes {tris / 1000}k tris; ";
        }
        Report("cheap to draw", bad, summary.TrimEnd(' ', ';'));
    }

    static void Closed(Dictionary<string, List<CircuitPart>> looks)
    {
        var bad = new List<string>();
        int edges = 0;
        foreach (var (name, parts) in looks)
        {
            // Parts are merged by name: a solid split across two chunks still closes.
            foreach (var group in parts.Where(p => p.Name != "Pit building glass").GroupBy(p => p.Name))
            {
                var count = new Dictionary<(long, long, long, long, long, long), int>();
                (long, long, long) K(Vector3 p) => ((long)Math.Round(p.x * 1000), (long)Math.Round(p.y * 1000), (long)Math.Round(p.z * 1000));
                foreach (CircuitPart p in group)
                {
                    IReadOnlyList<Vector3> v = p.Kit.Vertices;
                    IReadOnlyList<int> t = p.Kit.Triangles;
                    for (int i = 0; i < t.Count; i += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            var a = K(v[t[i + e]]);
                            var b = K(v[t[i + (e + 1) % 3]]);
                            var key = a.CompareTo(b) < 0 ? (a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3) : (b.Item1, b.Item2, b.Item3, a.Item1, a.Item2, a.Item3);
                            count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                        }
                }
                edges += count.Count;
                var open = count.Where(kv => kv.Value % 2 == 1).ToList();
                if (open.Count > 0)
                {
                    var k = open[0].Key;
                    bad.Add($"{name} {group.Key} {open.Count} open edges, e.g. at {k.Item1 / 1000f:0.0},{k.Item2 / 1000f:0.0},{k.Item3 / 1000f:0.0}");
                }
            }
        }
        Report("closed solids", bad, $"{edges} edges");
    }

    static void CrewClear(PitComplex pc, List<CircuitPart> parts)
    {
        var bad = new List<string>();
        string[] fit = { "Garage fit-out", "Garage metal", "Garage tyres", "Garage team colours" };
        float door = pc.Depth * 0.5f;
        int n = 0;
        foreach (CircuitPart p in parts.Where(p => fit.Contains(p.Name)))
            foreach (Vector3 v in p.Kit.Vertices)
            {
                n++;
                int g = Nearest(pc, v);
                GarageFrame f = pc.Frame(g);
                Vector3 d = v - f.Floor;
                float x = Vector3.Dot(d, f.Side), z = Vector3.Dot(d, f.Out);
                // The clear front runs 5.5 m either side, or to a metre short of the +x wall at the door where that is nearer.
                if (x > -5.5f && x < Math.Min(5.5f, pc.RightWall(g, door) - 1.1f) && z > door - PitComplex.ClearDepth && z < door - 0.01f && d.y < 2.2f)
                    bad.Add($"{p.Name} in garage {g + 1} at x {x:0.0} z {z:0.0} y {d.y:0.0}");
            }
        // The standby spots themselves sit inside the clear front.
        for (int g = 0; g < pc.Garages; g++)
            for (int s = 0; s < PitComplex.StandbySpots; s++)
            {
                GarageFrame f = pc.Frame(g);
                Vector3 d = pc.Standby(g, s).Position - f.Floor;
                float x = Vector3.Dot(d, f.Side), z = Vector3.Dot(d, f.Out);
                if (Math.Abs(x) > 5.5f || x > pc.RightWall(g, door) - 1.1f || z < door - PitComplex.ClearDepth) bad.Add($"standby spot {s} of garage {g + 1} outside the clear front");
            }
        Report("crew front clear", bad, $"{n} fit-out corners, {pc.Garages * PitComplex.StandbySpots} standby spots");
    }

    /// <summary>Nothing in a garage stands in its side walls, which round the bend sit well inside half the width.</summary>
    static void ClearOfWalls(PitComplex pc, List<CircuitPart> parts)
    {
        var bad = new List<string>();
        string[] fit = { "Garage fit-out", "Garage metal", "Garage tyres" };
        float half = pc.Width * 0.5f, depth = pc.Depth * 0.5f;
        int n = 0;
        foreach (CircuitPart p in parts.Where(p => fit.Contains(p.Name)))
            foreach (Vector3 v in p.Kit.Vertices)
            {
                int g = Nearest(pc, v);
                for (int h = Math.Max(0, g - 1); h <= Math.Min(pc.Garages - 1, g + 1); h++)
                {
                    GarageFrame f = pc.Frame(h);
                    Vector3 d = v - f.Floor;
                    float x = Vector3.Dot(d, f.Side), z = Vector3.Dot(d, f.Out);
                    // Strictly inside the wall, short of the door and the back wall, from the floor to the roof.
                    if (Math.Abs(x + half) < PitComplex.WallHalf - 0.01f && Math.Abs(z) < depth - 0.5f && d.y > 0.02f && d.y < 6.9f)
                    {
                        n++;
                        if (bad.Count < 5) bad.Add($"{p.Name} in the wall of garage {h + 1} at z {z:0.0} y {d.y:0.0}");
                    }
                }
            }
        if (n > 5) bad.Add($"and {n - 5} more");
        Report("clear of the walls", bad, "no corner in a side wall");
    }

    static void OnGarages(PitComplex pc, Dictionary<string, List<CircuitPart>> looks)
    {
        var bad = new List<string>();
        int n = 0;
        foreach (var (name, parts) in looks)
            foreach (CircuitPart p in parts.Where(p => p.Name == "Pit building"))
                foreach (Vector3 v in p.Kit.Vertices)
                {
                    int g = Nearest(pc, v);
                    GarageFrame f = pc.Frame(g);
                    Vector3 d = v - f.Floor;
                    float x = Vector3.Dot(d, f.Side), z = Vector3.Dot(d, f.Out);
                    // The building's underside reaches down into the roof slab (7.0 to 7.3 m), or just past its
                    // edges where the fascia beam and back wall hang over them, never above the slab's top.
                    if (Math.Abs(x) > pc.Width * 0.5f - 0.2f || Math.Abs(z) > 9.2f || d.y > 7.6f) continue;
                    n++;
                    if (d.y < 6.75f || d.y > PitComplex.RoofTop) bad.Add($"{name} over garage {g + 1} underside at {d.y:0.00} m");
                }
        Report("on the garages", bad, $"{n} underside corners");
    }

    static int Nearest(PitComplex pc, Vector3 v)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int g = 0; g < pc.Garages; g++)
        {
            Vector3 d = pc.Frame(g).Floor - v;
            float dd = d.x * d.x + d.z * d.z;
            if (dd < bestD) { bestD = dd; best = g; }
        }
        return best;
    }
}

/// <summary>Writes the preview's meshes: the track and pit complex round the start line, in Unity's frame.</summary>
static class Preview
{
    const float Radius = 230f;

    static short Cm(float v) => (short)Math.Clamp(Math.Round(v * 100), short.MinValue, short.MaxValue);

    public static void Write(string dir, Circuit circuit, PitComplex pc, List<CircuitPart> track, Dictionary<string, List<CircuitPart>> looks)
    {
        Directory.CreateDirectory(dir);
        Vector3 focus = pc.Frame(pc.StartGarage).Floor;
        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        var meshes = new List<object>();

        void Add(string group, string part, List<CircuitPart> parts)
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            foreach (CircuitPart p in parts.Where(p => p.Name == part))
            {
                IReadOnlyList<Vector3> v = p.Kit.Vertices;
                IReadOnlyList<Vector3> nn = p.Kit.Normals;
                IReadOnlyList<Vector2> u = p.Kit.UVs;
                IReadOnlyList<int> t = p.Kit.Triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 mid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    float dx = mid.x - focus.x, dz = mid.z - focus.z;
                    if (dx * dx + dz * dz > Radius * Radius) continue;
                    for (int k = 0; k < 3; k++) { pos.Add(v[t[i + k]] - focus); nrm.Add(nn[t[i + k]]); uv.Add(u[t[i + k]]); }
                }
            }
            if (pos.Count == 0) return;
            // Positions in centimetres from the focus, normals to a 127th; UVs only where a texture reads them.
            bool uvs = part == "Pit building glass" || part == "Garage team colours";
            long at = bin.Position;
            foreach (Vector3 p in pos) { w.Write(Cm(p.x)); w.Write(Cm(p.y)); w.Write(Cm(p.z)); }
            long nAt = bin.Position;
            foreach (Vector3 p in nrm) { w.Write((sbyte)Math.Round(p.x * 127)); w.Write((sbyte)Math.Round(p.y * 127)); w.Write((sbyte)Math.Round(p.z * 127)); }
            while (bin.Position % 4 != 0) w.Write((byte)0);
            long uvAt = -1;
            if (uvs)
            {
                uvAt = bin.Position;
                foreach (Vector2 p in uv) { w.Write(p.x); w.Write(p.y); }
            }
            meshes.Add(new { group, part, n = pos.Count, pos = at, nrm = nAt, uv = uvAt });
        }

        foreach (string part in track.Select(p => p.Name).Distinct()) Add("track", part, track);
        foreach (var (name, parts) in looks)
        {
            foreach (string part in parts.Select(p => p.Name).Distinct())
            {
                Add(part.StartsWith("Pit building") ? "building" : "fit", part, parts);
            }
        }

        // Props in garages with a car, in the preview's frame: position and the yaw the prop faces, degrees.
        var props = new List<object>();
        for (int g = 0; g < pc.Garages; g++)
        {
            GarageFrame f = pc.Frame(g);
            float dx = f.Floor.x - focus.x, dz = f.Floor.z - focus.z;
            if (dx * dx + dz * dz > Radius * Radius) continue;
            float yaw = (float)(Math.Atan2(f.Out.x, f.Out.z) * 180 / Math.PI);
            foreach (GarageProp p in PitComplexBuilder.Props)
            {
                Vector3 at = f.At(p.X, p.Y, p.Z) - focus;
                props.Add(new { id = p.Id, g, x = at.x, y = at.y, z = at.z, yaw = yaw + p.Yaw });
            }
        }
        // The sun's frame: the preview's x and z are the world's, shifted to the focus.
        var info = new
        {
            meshes,
            props,
            startGarage = pc.StartGarage,
            startOut = new[] { pc.Frame(pc.StartGarage).Out.x, pc.Frame(pc.StartGarage).Out.z },
            startAlong = new[] { pc.Frame(pc.StartGarage).Along.x, pc.Frame(pc.StartGarage).Along.z },
            garages = pc.Garages,
            palette = PitComplexBuilder.PaletteSize,
            windowCells = PitComplexBuilder.WindowCells,
            storeyCells = PitComplexBuilder.StoreyCells,
            crew = Enumerable.Range(0, PitComplex.StandbySpots).Select(s => { Vector3 p = pc.Standby(pc.StartGarage, s).Position - focus; return new[] { p.x, p.y, p.z }; }),
        };
        File.WriteAllText(Path.Combine(dir, "preview.json"), JsonSerializer.Serialize(info));
        File.WriteAllBytes(Path.Combine(dir, "preview.bin"), bin.ToArray());
        Console.WriteLine($"wrote {meshes.Count} meshes, {bin.Length / 1024} KB, {props.Count} props to {dir}");
    }
}
