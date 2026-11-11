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
//   box where meant   every car stands in its box where the kit is laid out for it, BoxOut from the garage's middle
//   clear over a car  over the pit lane, every piece of kit at rest and every hose hangs at least 1.9 m up, and the
//                     boom's underside 4.5 m, so the cars and the crews pass under it
//   in its own box    the boom and kit stay over their own box, nearer it than any other, and short of the fast lane
//   rests clear       the lollipop, extinguisher, trolley and tyres stand clear of the crew's front and of the walls
//   kit cheap         the moving kit of one garage is at most 2.5k triangles
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
        var spots = new PitStopSpots(pc, new PitLane(circuit));
        var kit = new PitBoxKit(pc);
        Kit.BoxWhereMeant(pc, spots);
        Kit.ClearOverCar(pc, kit);
        Kit.InItsBox(pc, kit, looks.Values.First());
        Kit.RestsClear(pc, kit);
        Kit.Cheap();
        if (args.Length > 1) Preview.Write(args[1], circuit, pc, track, looks, spots);
        return failed || Environment.ExitCode != 0 ? 1 : 0;
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

    public static void Write(string dir, Circuit circuit, PitComplex pc, List<CircuitPart> track, Dictionary<string, List<CircuitPart>> looks, PitStopSpots spots)
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

        void AddLocal(string group, string part, MeshKit k)
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            IReadOnlyList<int> t = k.Triangles;
            foreach (int i in t) { pos.Add(k.Vertices[i]); nrm.Add(k.Normals[i]); }
            long at = bin.Position;
            // Millimetres: the kit's pieces are small.
            foreach (Vector3 p in pos) { w.Write((short)Math.Round(p.x * 1000)); w.Write((short)Math.Round(p.y * 1000)); w.Write((short)Math.Round(p.z * 1000)); }
            long nAt = bin.Position;
            foreach (Vector3 p in nrm) { w.Write((sbyte)Math.Round(p.x * 127)); w.Write((sbyte)Math.Round(p.y * 127)); w.Write((sbyte)Math.Round(p.z * 127)); }
            while (bin.Position % 4 != 0) w.Write((byte)0);
            meshes.Add(new { group, part, n = pos.Count, pos = at, nrm = nAt, uv = -1L, mm = true });
        }

        foreach (string part in track.Select(p => p.Name).Distinct()) Add("track", part, track);
        foreach (var (name, parts) in looks)
        {
            foreach (string part in parts.Select(p => p.Name).Distinct())
            {
                Add(part.StartsWith("Pit building") ? "building" : "fit", part, parts);
            }
        }

        // The moving kit: each shape once, in its own frame, and where each piece rests in the garages round the focus.
        foreach (KitModel model in Enum.GetValues(typeof(KitModel)))
            foreach (var (paint, mk) in PitBoxKit.Model(model))
            {
                AddLocal($"model {model}", $"{model} {paint}", mk);
            }
        var kitAt = new List<object>();
        {
            var bk = new PitBoxKit(pc);
            for (int g = 0; g < pc.Garages; g++)
            {
                GarageFrame gf = pc.Frame(g);
                float gx = gf.Floor.x - focus.x, gz = gf.Floor.z - focus.z;
                if (gx * gx + gz * gz > Radius * Radius) continue;
                for (int k = 0; k < PitBoxKit.ItemCount; k++)
                {
                    var item = (KitItem)k;
                    KitPose r = bk.Rest(g, item);
                    Vector3 at = r.Position - focus;
                    float yaw = (float)(Math.Atan2(r.Forward.x, r.Forward.z) * 180 / Math.PI);
                    kitAt.Add(new { g, model = PitBoxKit.ModelOf(item).ToString(), x = at.x, y = at.y, z = at.z, yaw });
                    if (!PitBoxKit.Hangs(item)) continue;
                    Vector3 an = bk.Anchor(g, item) - focus;
                    kitAt.Add(new { g, model = PitBoxKit.LineOf(item).ToString(), x = an.x, y = an.y, z = an.z, yaw, drop = an.y - at.y });
                }
            }
        }
        // A car standing in the start garage's box, nose first, for scale.
        CrewSpot box = spots.Box(pc.StartGarage);
        var car = new { x = box.Position.x - focus.x, y = box.Position.y - focus.y, z = box.Position.z - focus.z, yaw = (float)(Math.Atan2(box.Facing.x, box.Facing.z) * 180 / Math.PI) };

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
            kit = kitAt,
            car,
            crew = Enumerable.Range(0, PitComplex.StandbySpots).Select(s => { Vector3 p = pc.Standby(pc.StartGarage, s).Position - focus; return new[] { p.x, p.y, p.z }; }),
        };
        File.WriteAllText(Path.Combine(dir, "preview.json"), JsonSerializer.Serialize(info));
        File.WriteAllBytes(Path.Combine(dir, "preview.bin"), bin.ToArray());
        Console.WriteLine($"wrote {meshes.Count} meshes, {bin.Length / 1024} KB, {props.Count} props to {dir}");
    }
}

/// <summary>The pit box kit's rules: where the boom and each piece stand, against the box, the car and the crew.</summary>
static class Kit
{
    static void Report(string rule, List<string> bad, string ok)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {ok}");
        else
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"FAIL  {rule}: {bad.Count} failures, {string.Join("; ", bad.Take(8))}");
        }
    }

    static (float x, float y, float z) Local(GarageFrame f, Vector3 v)
    {
        Vector3 d = v - f.Floor;
        return (Vector3.Dot(d, f.Side), d.y, Vector3.Dot(d, f.Out));
    }

    /// <summary>The lowest point of a shape below its origin, m.</summary>
    static float Bottom(KitModel model) => PitBoxKit.Model(model).Values.SelectMany(k => k.Vertices).Min(v => v.y);

    public static void BoxWhereMeant(PitComplex pc, PitStopSpots spots)
    {
        var bad = new List<string>();
        float worst = 0f;
        for (int g = 0; g < pc.Garages; g++)
        {
            var (x, _, z) = Local(pc.Frame(g), spots.Box(g).Position);
            worst = Math.Max(worst, Math.Abs(z - PitBoxKit.BoxOut));
            if (Math.Abs(z - PitBoxKit.BoxOut) > 0.4f || Math.Abs(x) > 0.5f) bad.Add($"garage {g + 1} box at x {x:0.00} z {z:0.00}");
        }
        Report("box where meant", bad, $"every box within {worst:0.00} m of {PitBoxKit.BoxOut} m out");
    }

    public static void ClearOverCar(PitComplex pc, PitBoxKit kit)
    {
        var bad = new List<string>();
        float door = pc.Depth * 0.5f, lowest = float.MaxValue;
        for (int g = 0; g < pc.Garages; g++)
        {
            GarageFrame f = pc.Frame(g);
            for (int k = 0; k < PitBoxKit.ItemCount; k++)
            {
                var item = (KitItem)k;
                KitPose r = kit.Rest(g, item);
                var (x, y, z) = Local(f, r.Position);
                if (z < door) continue;
                float low = y + Bottom(PitBoxKit.ModelOf(item));
                lowest = Math.Min(lowest, low);
                if (low < 1.9f) bad.Add($"garage {g + 1} {item} down to {low:0.00} m");
            }
            var boom = new MeshKit();
            var hoses = new MeshKit();
            kit.Boom(g, boom, null, hoses);
            foreach (Vector3 v in boom.Vertices)
            {
                var (x, y, z) = Local(f, v);
                if (z > PitBoxKit.Fascia && y < 4.5f) { bad.Add($"garage {g + 1} boom down to {y:0.00} m at z {z:0.0}"); break; }
            }
            foreach (Vector3 v in hoses.Vertices)
            {
                var (x, y, z) = Local(f, v);
                if (z > door && y < 1.9f) { bad.Add($"garage {g + 1} hose down to {y:0.00} m at z {z:0.0}"); break; }
                if (z > door + 0.3f && z < PitBoxKit.Fascia && y < PitBoxKit.DoorUnder - 0.1f) { bad.Add($"garage {g + 1} hose sags through the door at {y:0.00} m"); break; }
            }
        }
        Report("clear over a car", bad, $"lowest kit over the lane {lowest:0.00} m, boom from {PitBoxKit.BoomUnder} m");
    }

    public static void InItsBox(PitComplex pc, PitBoxKit kit, List<CircuitPart> parts)
    {
        var bad = new List<string>();
        // The fast lane starts where the box lines end, this far out from the box's middle.
        float boxFront = 3.85f;
        int n = 0;
        for (int g = 0; g < pc.Garages; g++)
        {
            GarageFrame f = pc.Frame(g);
            var boom = new MeshKit();
            kit.Boom(g, boom, null, boom);
            var points = new List<Vector3>(boom.Vertices);
            for (int k = 0; k < PitBoxKit.ItemCount; k++) points.Add(kit.Rest(g, (KitItem)k).Position);
            foreach (Vector3 v in points)
            {
                n++;
                var (x, y, z) = Local(f, v);
                if (z > PitBoxKit.BoxOut + boxFront) { bad.Add($"garage {g + 1} over the fast lane at z {z:0.0}"); break; }
                // Nearest its own box of all the boxes, by distance across the row at the same height.
                int best = g;
                float own = Box(pc, g, v), bestD = own;
                for (int h = Math.Max(0, g - 2); h <= Math.Min(pc.Garages - 1, g + 2); h++)
                    if (Box(pc, h, v) < bestD - 0.01f) { bestD = Box(pc, h, v); best = h; }
                if (best != g) { bad.Add($"garage {g + 1} kit at x {x:0.0} z {z:0.0} nearer garage {best + 1}"); break; }
            }
        }
        Report("in its own box", bad, $"{n} boom corners and rests");
    }

    static float Box(PitComplex pc, int g, Vector3 v)
    {
        GarageFrame f = pc.Frame(g);
        Vector3 d = f.At(0f, v.y - f.Floor.y, PitBoxKit.BoxOut) - v;
        return (float)Math.Sqrt(d.x * d.x + d.z * d.z);
    }

    public static void RestsClear(PitComplex pc, PitBoxKit kit)
    {
        var bad = new List<string>();
        float door = pc.Depth * 0.5f;
        // Each standing piece's footprint across and out: half its size.
        var halves = new Dictionary<KitModel, (float x, float z)>
        {
            [KitModel.Lollipop] = (0.3f, 0.2f), [KitModel.Extinguisher] = (0.1f, 0.12f), [KitModel.Trolley] = (0.3f, (PitBoxKit.TyrePitch * 4f + 0.2f) * 0.5f),
            [KitModel.Tyre] = (PitBoxKit.TyreRadius, PitBoxKit.TyreWidth * 0.5f),
        };
        for (int g = 0; g < pc.Garages; g++)
        {
            GarageFrame f = pc.Frame(g);
            var boxes = new List<(KitItem item, float x0, float x1, float z0, float z1)>();
            for (int k = 0; k < PitBoxKit.ItemCount; k++)
            {
                var item = (KitItem)k;
                if (PitBoxKit.Hangs(item)) continue;
                var (x, y, z) = Local(f, kit.Rest(g, item).Position);
                var (hx, hz) = halves[PitBoxKit.ModelOf(item)];
                float x0 = x - hx, x1 = x + hx, z0 = z - hz, z1 = z + hz;
                // In the clear front only within a metre of the +x wall at the door, as the crew check has it.
                bool front = z1 > door - PitComplex.ClearDepth && z0 < door;
                if (front && x1 > -5.5f && x0 < Math.Min(5.5f, pc.RightWall(g, door) - 1.1f)) bad.Add($"garage {g + 1} {item} in the crew's front at x {x:0.0}");
                if (x0 < pc.LeftWall || x1 > pc.RightWall(g, z1) || z1 > door) bad.Add($"garage {g + 1} {item} in a wall or the door at x {x:0.0} z {z:0.0}");
                if (item < KitItem.TyreFrontLeft || item > KitItem.TyreRearRight) boxes.Add((item, x0, x1, z0, z1));
            }
            // The fuel rig's tower stands there too.
            Vector2 rig = kit.FuelRigAt(g);
            boxes.Add((KitItem.Nozzle, rig.x - 0.45f, rig.x + 0.45f, rig.y - 0.5f, rig.y + 0.5f));
            for (int a = 0; a < boxes.Count; a++)
                for (int b = a + 1; b < boxes.Count; b++)
                    if (boxes[a].x0 < boxes[b].x1 && boxes[b].x0 < boxes[a].x1 && boxes[a].z0 < boxes[b].z1 && boxes[b].z0 < boxes[a].z1)
                        bad.Add($"garage {g + 1} {(boxes[a].item == KitItem.Nozzle ? "fuel rig" : boxes[a].item.ToString())} and {(boxes[b].item == KitItem.Nozzle ? "fuel rig" : boxes[b].item.ToString())} overlap");
        }
        Report("rests clear", bad, "lollipop, extinguisher, trolley and tyres clear of the crew, the walls and each other");
    }

    public static void Cheap()
    {
        int tris = 0;
        for (int k = 0; k < PitBoxKit.ItemCount; k++)
        {
            var item = (KitItem)k;
            tris += PitBoxKit.Model(PitBoxKit.ModelOf(item)).Values.Sum(m => m.Triangles.Count / 3);
            if (PitBoxKit.Hangs(item)) tris += PitBoxKit.Model(PitBoxKit.LineOf(item)).Values.Sum(m => m.Triangles.Count / 3);
        }
        Report("kit cheap", tris <= 2500 ? new List<string>() : new List<string> { $"{tris} triangles a garage" }, $"{tris} triangles of moving kit a garage");
    }
}
