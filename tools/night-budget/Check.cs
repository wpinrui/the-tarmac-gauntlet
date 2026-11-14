// Builds the circuit, its stands, the pit complex and the floodlights exactly as the game does, then checks the night's
// lighting against its budget (NightBudget, Lamps.hlsl). Each rule prints one line: ok, or FAIL with the worst spots.
// Exit code 1 if any rule fails. With an output folder as the second argument it also writes the scene for the night
// renders there (Scene.cs).
//
// Rules on the floodlights' layout:
//   cheap to draw       the masts and lenses are at most 40k triangles in at most 120 meshes, none wider than 300 m
//   aimed down          every light points at least 5 degrees below level
//   clear of the cars   no mast, bracket or lamp is lower than 5 m over the road or the pit lane
//   clear of the gantry no mast stands within 30 m of the start gantry's feet, and nothing sits in the start light box
//   within the lights   wherever the camera is on the lap, every floodlight that reaches the track under it is one of
//                       the nearest FloodlightRig turns on, and with the most headlamps the lights on fit URP's 256
// Then the budget (Sweep.cs).
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

    /// <summary>FloodlightRig's lights on, and how far it reaches, m; the most headlamp lights; URP's most visible lights.</summary>
    const int MostOn = 200, MostHeadlamps = 20, UrpMost = 256;
    const float Reach = 600f;

    static int Main(string[] args)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "../../../../.."));
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
        var floods = new FloodlightLayout(circuit, pc, stands.RoofLamps, builder.PitWallRuns, GantrySpan.Of(circuit, placed));
        List<CircuitPart> lit = floods.Build(null, null);

        Console.WriteLine($"      {floods.Heads.Count} lamp heads in {floods.Sources.Count} lights: "
            + string.Join(", ", floods.Sources.GroupBy(f => f.Kind).Select(g => $"{g.Count()} {g.Key}")));
        Cheap(lit);
        AimedDown(floods);
        ClearOfCars(circuit, lit);
        ClearOfGantry(placed, lit);
        WithinLights(circuit, floods);
        Sweep.Run(root, circuit, pc, stands, floods);
        if (args.Length > 1) Scene.Write(args[1], circuit, stands, floods, track.Concat(standParts).Concat(pitParts).Concat(furniture).Concat(lit).ToList());
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
        Report("cheap to draw", bad, $"{parts.Count} meshes, {tris} triangles");
    }

    static void AimedDown(FloodlightLayout floods)
    {
        var bad = new List<string>();
        float limit = -Mathf.Sin(5f * Mathf.Deg2Rad);
        foreach (FloodSource f in floods.Sources)
            if (f.Aim.y > limit) bad.Add($"{f.Kind} at {f.Position.x:0},{f.Position.z:0} aims {Mathf.Asin(f.Aim.y) * Mathf.Rad2Deg:0} degrees");
        float steepest = floods.Sources.Min(f => f.Aim.y), flattest = floods.Sources.Max(f => f.Aim.y);
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
        foreach (CircuitPart p in lit.Where(p => p.Name == "Floodlight steel"))
            foreach (Vector3 v in p.Kit.Vertices)
                foreach (Vector3 foot in new[] { placed.GantryLeft, placed.GantryRight })
                    if (Flat(v - foot).magnitude < 30f && v.y < foot.y + 3f) { bad.Add($"mast {Flat(v - foot).magnitude:0.0} m from a gantry foot"); break; }
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

    /// <summary>
    /// With the camera over any point of the lap, FloodlightRig turns on the nearest lights by their targets: every light
    /// reaching the road there must be among them, or the road would go dark as the camera passes.
    /// </summary>
    static void WithinLights(Circuit c, FloodlightLayout floods)
    {
        var bad = new List<string>();
        int most = 0;
        for (int k = 0; k < c.Count; k += Math.Max(1, (int)(10f / c.Step)))
        {
            Vector3 eye = c.Centre[k] + Vector3.up * 3f;
            List<int> on = Enumerable.Range(0, floods.Sources.Count)
                .Where(i => (floods.Sources[i].Target - eye).sqrMagnitude < Reach * Reach)
                .OrderBy(i => (floods.Sources[i].Target - eye).sqrMagnitude).Take(MostOn).ToList();
            var set = new HashSet<int>(on);
            most = Math.Max(most, on.Count);
            for (int i = 0; i < floods.Sources.Count; i++)
            {
                FloodSource s = floods.Sources[i];
                if (set.Contains(i)) continue;
                if (LampLight.Raw(s.Intensity, s.Position, s.Aim, s.Range, s.Cone, s.Core, c.Centre[k], Vector3.up) > 0.01f)
                    bad.Add($"light {i} ({s.Kind}) reaches the road at {k * c.Step:0} m but is off");
            }
        }
        if (most + MostHeadlamps > UrpMost) bad.Add($"{most} floodlights and {MostHeadlamps} headlamps on, over {UrpMost}");
        Report("within the lights", bad.Distinct().ToList(), $"at most {most} floodlights on at once, {most + MostHeadlamps} lights with the most headlamps, of {UrpMost}");
    }

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

    internal static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
