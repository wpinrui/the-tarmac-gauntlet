// Builds the circuit, its stands and the pit complex exactly as the game does, lays the floodlights over them
// (Floodlights) and checks them. Each rule prints one line: ok, or FAIL with the worst spots. Exit code 1 if any
// rule fails.
//
// Rules:
//   cheap to draw     the floodlights are at most 40k triangles in at most 120 meshes, none wider than 300 m
//   aimed down        every lamp points at least 5 degrees below level
//   on the ground     every pool corner lies on the built ground under it: never under it, so it shows, and no more than
//                     0.35 m above it, so it doesn't float
//   clear of the cars no mast, bracket or lamp is lower than 5 m over the road or the pit lane
//   no dark gaps      the start straight's centre line from the start of the main stand to its end, and the pit lane in
//                     front of every garage, are lit at least a third as bright as a pool's middle
//
// With an output folder as the second argument it also writes the night preview's data there.
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
        var floods = new Floodlights(circuit, pc, stands.RoofLamps, builder.PitWallRuns, track);
        List<CircuitPart> lit = floods.Build(null, null, null);

        Console.WriteLine($"      {floods.Lights.Count} lamps: " + string.Join(", ", floods.Lights.GroupBy(f => f.Kind).Select(g => $"{g.Count()} {g.Key}")));
        var ground = new Surface(track.Where(p => Floodlights.GroundParts.Contains(p.Name)));
        Cheap(lit);
        AimedDown(floods);
        OnGround(lit, ground);
        ClearOfCars(circuit, lit, ground);
        NoGaps(circuit, pc, floods, stands);
        if (args.Length > 1) Preview.Write(args[1], circuit, pc, floods, track, standParts, pitParts, lit);
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

    static void OnGround(List<CircuitPart> lit, Surface ground)
    {
        var bad = new List<(float, string)>();
        int n = 0, open = 0;
        float worstUnder = 0f, worstOver = 0f;
        foreach (CircuitPart p in lit.Where(p => p.Name == "Floodlight pools"))
            foreach (Vector3 v in p.Kit.Vertices)
            {
                float? h = ground.Height(v.x, v.z, v.y + 0.5f);
                if (h == null) { open++; continue; }
                n++;
                float gap = v.y - h.Value;
                worstUnder = Math.Min(worstUnder, gap);
                worstOver = Math.Max(worstOver, gap);
                if (gap < 0f || gap > 0.35f) bad.Add((Math.Abs(gap), $"{gap:+0.00;-0.00} m at {v.x:0},{v.z:0}"));
            }
        Report("on the ground", bad.OrderByDescending(b => b.Item1).Select(b => b.Item2).ToList(),
            $"{n} pool corners from {worstUnder:+0.00;-0.00} to {worstOver:+0.00;-0.00} m over the ground ({open} over nothing built)");
    }

    static void ClearOfCars(Circuit c, List<CircuitPart> lit, Surface ground)
    {
        var bad = new List<string>();
        float laneHalf = c.Data.pit.laneWidth * 0.5f;
        int n = 0;
        foreach (CircuitPart p in lit.Where(p => p.Name != "Floodlight pools"))
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

    static void NoGaps(Circuit c, PitComplex pc, Floodlights floods, Grandstands stands)
    {
        var bad = new List<string>();
        StandSite main = Grandstands.Sites.First(s => s.Main);
        (float from, float to) = stands.Span(main);
        float darkest = float.MaxValue;
        for (float d = from; ; d += 5f)
        {
            float dd = Mathf.Repeat(d, c.Lap);
            float ahead = c.Ahead(from, dd);
            if (ahead > c.Ahead(from, to)) break;
            Vector3 p = c.Centre[c.IndexOf(dd)];
            float light = Light(floods, p);
            darkest = Math.Min(darkest, light);
            if (light < 1f / 3f) bad.Add($"straight at {dd:0} m {light:0.00}");
        }
        float darkestLane = float.MaxValue;
        for (int g = 0; g < pc.Garages; g++)
        {
            Vector3 p = pc.RowPoint(g + 0.5f, 0f, pc.Depth * 0.5f + c.Data.pit.laneWidth * 0.5f);
            float light = Light(floods, p);
            darkestLane = Math.Min(darkestLane, light);
            if (light < 1f / 3f) bad.Add($"lane at garage {g + 1} {light:0.00}");
        }
        Report("no dark gaps", bad, $"darkest {darkest:0.00} on the straight, {darkestLane:0.00} in the pit lane");
    }

    /// <summary>How bright the pools make a point, as the shader draws them: 1 at a pool's middle.</summary>
    public static float Light(Floodlights floods, Vector3 p)
    {
        float sum = 0f;
        foreach (Floodlight f in floods.Lights)
        {
            Vector3 d = Flat(p - f.Target);
            Vector3 b = new Vector3(-f.PoolAxis.z, 0f, f.PoolAxis.x);
            float u = Vector3.Dot(d, f.PoolAxis) / f.Long, v = Vector3.Dot(d, b) / f.Wide;
            float fall = Mathf.Clamp01(1f - (u * u + v * v));
            sum += fall * fall;
        }
        return sum;
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

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>The built ground's triangles in a grid, to find the height of what is built under a point.</summary>
    sealed class Surface
    {
        const float Cell = 4f;
        readonly Dictionary<(int, int), List<(Vector3, Vector3, Vector3)>> cells = new Dictionary<(int, int), List<(Vector3, Vector3, Vector3)>>();

        public Surface(IEnumerable<CircuitPart> parts)
        {
            foreach (CircuitPart p in parts)
            {
                IReadOnlyList<Vector3> v = p.Kit.Vertices;
                IReadOnlyList<int> t = p.Kit.Triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                    int x0 = Mathf.FloorToInt(Math.Min(a.x, Math.Min(b.x, d.x)) / Cell), x1 = Mathf.FloorToInt(Math.Max(a.x, Math.Max(b.x, d.x)) / Cell);
                    int z0 = Mathf.FloorToInt(Math.Min(a.z, Math.Min(b.z, d.z)) / Cell), z1 = Mathf.FloorToInt(Math.Max(a.z, Math.Max(b.z, d.z)) / Cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            if (!cells.TryGetValue((x, z), out var list)) cells[(x, z)] = list = new List<(Vector3, Vector3, Vector3)>();
                            list.Add((a, b, d));
                        }
                }
            }
        }

        /// <summary>The highest built surface under a point that is below the ceiling, or null for none.</summary>
        public float? Height(float x, float z, float ceiling)
        {
            if (!cells.TryGetValue((Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var list)) return null;
            float? best = null;
            foreach ((Vector3 a, Vector3 b, Vector3 c) in list)
            {
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Math.Abs(det) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (y < ceiling && (best == null || y > best)) best = y;
            }
            return best;
        }
    }
}
