// Builds the circuit, its stands and the pit complex exactly as the game does, lays the floodlights over them
// (Floodlights) and checks them. Each rule prints one line: ok, or FAIL with the worst spots. Exit code 1 if any
// rule fails.
//
// Rules:
//   cheap to draw     the floodlights are at most 40k triangles in at most 120 meshes, none wider than 300 m
//   aimed down        every lamp points at least 5 degrees below level
//   clear of the cars no mast, bracket or lamp is lower than 5 m over the road or the pit lane
//   clear of the gantry no mast stands within 30 m of the start gantry's feet, and no lamp or bracket under the beam
//                     lies inside the start light box
//   lit evenly        the floodlights' map (FloodField) lights the start straight along the main stand, across its width,
//                     and the pit lane in front of every garage, with the brightest no more than 1.5 times the dimmest
//                     and none under half of the most
// Then the light budget (Budget.cs).
//
// With an output folder as the second argument it also writes the night preview's data there. With FIELD_DUMP set to a
// file, it writes the floodlights' map there and prints the light down the straight and the lane.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    static bool failed;


    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var circuit = new Circuit(data);
        var builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> track = builder.Build();
        var stands = new Grandstands(circuit, builder, null, StandLook.Main, StandLook.Corners);
        List<CircuitPart> standParts = stands.Build();
        var pc = new PitComplex(circuit, builder.GarageFloors);
        List<CircuitPart> pitParts = new PitComplexBuilder(pc, null).Build();
        var placed = new TrackFurniture(circuit, builder, stands.Footprints);
        List<CircuitPart> furniture = new FurnitureBuilder(circuit, placed, null).Parts;
        var floods = new Floodlights(circuit, pc, stands.RoofLamps, builder.PitWallRuns, GantrySpan.Of(circuit, placed));
        List<CircuitPart> lit = floods.Build(null, null);

        Console.WriteLine($"      {floods.Lights.Count} lamps: " + string.Join(", ", floods.Lights.GroupBy(f => f.Kind).Select(g => $"{g.Count()} {g.Key}")));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var field = new FloodField(floods);
        Console.WriteLine($"      field {watch.ElapsedMilliseconds} ms");
        if (Environment.GetEnvironmentVariable("FIELD_DUMP") is string dump) Dump(dump, circuit, floods, field);
        Cheap(lit);
        AimedDown(floods);
        ClearOfCars(circuit, lit);
        ClearOfGantry(placed, lit);
        Even(circuit, pc, field, stands);
        Budget.Run(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]), "../../../../..")), circuit, floods, field);
        if (args.Length > 1) Preview.Write(args[1], circuit, pc, floods, track, standParts, pitParts.Concat(furniture).ToList(), lit);
        return failed ? 1 : 0;
    }

    internal static void Report(string rule, List<string> bad, string ok)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {ok}");
        else
        {
            failed = true;
            Console.WriteLine($"FAIL  {rule}: {bad.Count} failures, {string.Join("; ", bad.Take(8))}");
        }
    }

    static void Cheap(List<CircuitPart> parts)
    {
        var bad = new List<string>();
        int tris = parts.Sum(p => p.Kit.Triangles.Count / 3);
        if (tris > 40_000) bad.Add($"{tris} triangles");
        if (parts.Count > 120) bad.Add($"{parts.Count} meshes");
        foreach (CircuitPart p in parts)
        {
            IReadOnlyList<Vector3> v = p.Kit.Vertices;
            float span = Math.Max(v.Max(q => q.x) - v.Min(q => q.x), v.Max(q => q.z) - v.Min(q => q.z));
            if (span > 300f) bad.Add($"{p.Name} {span:0} m wide");
        }
        string each = string.Join(", ", parts.GroupBy(p => p.Name).Select(g => $"{g.Key} {g.Count()} meshes {g.Sum(p => p.Kit.Triangles.Count / 3)} tris"));
        Report("cheap to draw", bad, $"{parts.Count} meshes, {tris} triangles ({each})");
    }

    static void AimedDown(Floodlights floods)
    {
        var bad = new List<string>();
        float limit = -Mathf.Sin(5f * Mathf.Deg2Rad);
        foreach (Floodlight f in floods.Lights)
            if (f.Aim.y > limit) bad.Add($"{f.Kind} at {f.Position.x:0},{f.Position.z:0} aims {Mathf.Asin(f.Aim.y) * Mathf.Rad2Deg:0} degrees");
        float steepest = floods.Lights.Min(f => f.Aim.y), flattest = floods.Lights.Max(f => f.Aim.y);
        Report("aimed down", bad, $"from {-Mathf.Asin(flattest) * Mathf.Rad2Deg:0} to {-Mathf.Asin(steepest) * Mathf.Rad2Deg:0} degrees below level");
    }

    static void ClearOfCars(Circuit c, List<CircuitPart> lit)
    {
        var bad = new List<string>();
        float laneHalf = c.Data.pit.laneWidth * 0.5f;
        int n = 0;
        foreach (CircuitPart p in lit)
            foreach (Vector3 v in p.Kit.Vertices)
            {
                n++;
                int k = Nearest(c.Centre, v);
                float lat = Vector3.Dot(v - c.Centre[k], c.Left[k]);
                bool overRoad = Math.Abs(lat) < c.HalfWidth(k) + 0.5f;
                int i = Nearest(c.Lane, v);
                bool overLane = Flat(v - c.Lane[i]).magnitude < laneHalf;
                float floor = overRoad ? c.Surface(k, lat).y : c.Lane[i].y;
                if ((overRoad || overLane) && v.y - floor < 5f)
                    bad.Add($"{p.Name} {v.y - floor:0.0} m over the {(overRoad ? "road" : "pit lane")} at {v.x:0},{v.z:0}");
            }
        Report("clear of the cars", bad, $"{n} corners");
    }

    static void ClearOfGantry(TrackFurniture placed, List<CircuitPart> lit)
    {
        var bad = new List<string>();
        // Masts are the only steel reaching down to the pit wall: anything below 3 m over a foot's level is one.
        foreach (CircuitPart p in lit.Where(p => p.Name == "Floodlight steel"))
            foreach (Vector3 v in p.Kit.Vertices)
                foreach (Vector3 foot in new[] { placed.GantryLeft, placed.GantryRight })
                    if (Flat(v - foot).magnitude < 30f && v.y < foot.y + 3f) { bad.Add($"mast {Flat(v - foot).magnitude:0.0} m from a gantry foot"); break; }
        // The start light box: 3.4 m wide over the middle of the road, from the beam down 1.45 m, on the grid side.
        Vector3 mid = (placed.GantryLeft + placed.GantryRight) * 0.5f;
        Vector3 span = Flat(placed.GantryRight - placed.GantryLeft).normalized;
        foreach (CircuitPart p in lit)
            foreach (Vector3 v in p.Kit.Vertices)
            {
                Vector3 d = Flat(v - mid);
                if (Math.Abs(Vector3.Dot(d, span)) < 1.9f && d.magnitude < 3f && v.y > mid.y + 3f) bad.Add($"{p.Name} in the start light box");
            }
        Report("clear of the gantry", bad.Distinct().ToList(), "no mast near its feet, nothing in the start light box");
    }

    /// <summary>The floodlights, the circuit's middle line, the pit lane and the floodlights' map, for frames.py.</summary>
    static void Dump(string path, Circuit c, Floodlights floods, FloodField field)
    {
        float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        var data = new
        {
            lamps = floods.Lights.Select(f => new { kind = f.Kind.ToString(), position = V(f.Position), target = V(f.Target), aim = V(f.Aim), axis = V(f.PoolAxis), f.Long, f.Wide, f.Reach, f.Cone }),
            centre = c.Centre.Select(V), left = c.Left.Select(V), half = Enumerable.Range(0, c.Count).Select(k => c.HalfWidth(k)),
            lane = c.Lane.Select(V), laneWidth = c.Data.pit.laneWidth,
            field = new { field.Width, field.Height, field.X0, field.Z0, texel = FloodField.Texel, light = field.Light, ground = field.Ground,
                towardX = field.Toward.Select(t => t.x), towardZ = field.Toward.Select(t => t.z) },
        };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(data));
    }

    static void Even(Circuit c, PitComplex pc, FloodField field, Grandstands stands)
    {
        var bad = new List<string>();
        StandSite main = Grandstands.Sites.First(s => s.Main);
        (float from, float to) = stands.Span(main);
        var straight = new List<float>();
        for (float d = from; ; d += 2f)
        {
            float dd = Mathf.Repeat(d, c.Lap);
            if (c.Ahead(from, dd) > c.Ahead(from, to)) break;
            int k = c.IndexOf(dd);
            foreach (float lat in new[] { -0.6f, 0f, 0.6f })
            {
                Vector3 p = c.Centre[k] + c.Left[k] * (lat * c.HalfWidth(k));
                straight.Add(field.At(p.x, p.z));
            }
        }
        var lane = new List<float>();
        for (float g = 0.5f; g < pc.Garages; g += 0.25f)
        {
            Vector3 p = pc.RowPoint(g, 0f, pc.Depth * 0.5f + c.Data.pit.laneWidth * 0.5f);
            lane.Add(field.At(p.x, p.z));
        }
        if (Environment.GetEnvironmentVariable("FIELD_DUMP") != null)
        {
            Console.WriteLine("straight " + string.Join(" ", straight.Where((v, i) => i % 3 == 1).Select(v => v.ToString("0.00"))));
            Console.WriteLine("lane " + string.Join(" ", lane.Select(v => v.ToString("0.00"))));
        }
        float sMin = straight.Min(), sMax = straight.Max(), lMin = lane.Min(), lMax = lane.Max();
        if (sMax > EvenMost * sMin) bad.Add($"straight from {sMin:0.00} to {sMax:0.00}");
        if (lMax > EvenMost * lMin) bad.Add($"lane from {lMin:0.00} to {lMax:0.00}");
        if (sMin < 0.5f || lMin < 0.5f) bad.Add($"dim: straight {sMin:0.00}, lane {lMin:0.00}");
        float most = field.Light.Max();
        if (most > FloodField.Most) bad.Add($"{most:0.00} somewhere, over {FloodField.Most}");
        Report("lit evenly", bad, $"the straight from {sMin:0.00} to {sMax:0.00} ({sMax / sMin:0.00} to 1), the pit lane from {lMin:0.00} to {lMax:0.00} ({lMax / lMin:0.00} to 1), {most:0.00} at most; {field.Width} x {field.Height} cells");
    }

    /// <summary>The brightest point of the start straight and of the pit lane against the dimmest.</summary>
    const float EvenMost = 1.5f;

    static int Nearest(Vector3[] points, Vector3 p)
    {
        int best = 0;
        float nearest = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            float d = Flat(points[i] - p).sqrMagnitude;
            if (d < nearest) { nearest = d; best = i; }
        }
        return best;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
