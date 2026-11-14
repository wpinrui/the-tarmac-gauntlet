// The night's budget, swept: the shader's maths against the game's numbers, one headlamp at every distance, the
// floodlit track along the main straight and the pit lane, and the full grid at night with every car's headlamps and
// every floodlight on at once, on the road and on the cars. Lamps.hlsl is mirrored here line for line (LampLight);
// "shown" is what the screen shows: the pixel through the night's exposure and the ACES tone curve, sRGB.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Sweep
{
    /// <summary>The tone curve's own exposure (PostEffects.ToneExposure), stops, and the bloom's threshold.</summary>
    const float ToneExposure = 0.45f, BloomThreshold = 1.2f;
    /// <summary>A white car's paint, and the most a white car may show under every lamp at once, sRGB: bright, not blown.</summary>
    const float White = 0.85f, WhiteMost = 0.95f;
    /// <summary>The floodlit track: no dimmer than this share of Floodlit, and the brightest no more than this times the dimmest (a uniformity of 0.55, as broadcast lighting asks).</summary>
    const float DimMost = 0.6f, EvenMost = 1.8f;
    /// <summary>Every grid place under the floodlights alone takes at least this share of Floodlit, out to the road's edges.</summary>
    const float GridLit = 0.4f;

    /// <summary>One lamp, in the budget's units: its intensity is what Lamps.hlsl weighs against the caps.</summary>
    struct Lamp
    {
        public Vector3 Position, Aim, Up, Right;
        public float Range, Cone, Core, Intensity;
        public bool Beam;

        public float Raw(Vector3 at, Vector3 normal)
        {
            float raw = LampLight.Raw(Intensity, Position, Aim, Range, Cone, Core, at, normal);
            if (!Beam || raw <= 0f) return raw;
            Vector3 d = at - Position;
            float z = Vector3.Dot(d, Aim);
            if (z <= 1e-4f) return 0f;
            float tan = Mathf.Tan(Cone * 0.5f * Mathf.Deg2Rad);
            float u = Vector3.Dot(d, Right) / z / tan, v = Vector3.Dot(d, Up) / z / tan;
            if (Math.Abs(u) > 1f || Math.Abs(v) > 1f) return 0f;
            return raw * LampLight.Beam(u, v);
        }
    }

    /// <summary>The irradiance all these lamps give a surface under the budget, as Lamps.hlsl sums it.</summary>
    static float Light(IEnumerable<Lamp> lamps, Vector3 at, Vector3 normal)
    {
        float sum = 0f;
        foreach (Lamp l in lamps) sum += LampLight.Knee(l.Raw(at, normal), NightBudget.LampMost);
        return LampLight.Knee(sum, NightBudget.AllMost);
    }

    /// <summary>A matte surface's pixel under the pixel cap.</summary>
    static float Pixel(float albedo, float light) => LampLight.Knee(albedo * light, NightBudget.PixelMost);

    /// <summary>What the screen shows for a pixel, sRGB.</summary>
    static float Shown(float pixel)
    {
        float x = pixel * Mathf.Pow(2f, ToneExposure + NightBudget.Exposure);
        float aces = (x * (x + 0.0245786f) - 0.000090537f) / (x * (0.983729f * x + 0.432951f) + 0.238081f);
        aces = Mathf.Clamp01(aces);
        return aces <= 0.0031308f ? aces * 12.92f : 1.055f * Mathf.Pow(aces, 1f / 2.4f) - 0.055f;
    }

    /// <summary>A car's headlamp light, as HeadlampRig places it for a car at this ground point facing this way.</summary>
    static Lamp Headlamp(Vector3 ground, Vector3 forward, float noseHeight = 0.7f, float front = 2.3f)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float dip = NightBudget.HeadlampDip * Mathf.Deg2Rad;
        Vector3 aim = (forward * Mathf.Cos(dip) - Vector3.up * Mathf.Sin(dip)).normalized;
        return new Lamp
        {
            Position = ground + Vector3.up * noseHeight + forward * (front + NightBudget.HeadlampAhead),
            Aim = aim, Right = right, Up = Vector3.Cross(aim, right).normalized,
            Range = NightBudget.HeadlampRange, Cone = NightBudget.HeadlampCone, Core = NightBudget.HeadlampCore,
            Intensity = NightBudget.HeadlampIntensity, Beam = true,
        };
    }

    static Lamp Flood(FloodSource s) => new Lamp
    {
        Position = s.Position, Aim = s.Aim, Range = s.Range, Cone = s.Cone, Core = s.Core, Intensity = s.Intensity,
    };

    public static void Run(string root, Circuit c, PitComplex pc, Grandstands stands, FloodlightLayout floods)
    {
        ShaderMatches(root);
        OneHeadlamp();
        Floodlit(c, pc, stands, floods);
        FullGrid(c, floods);
    }

    static string Squash(string s) => Regex.Replace(s, @"\s+", "");

    /// <summary>Lamps.hlsl's knee and caps are the game's: the same formula, the budget's numbers in the places it reads them.</summary>
    static void ShaderMatches(string root)
    {
        var bad = new List<string>();
        string hlsl = File.ReadAllText(Path.Combine(root, "unity/Assets/Rendering/Shaders/Lamps.hlsl"));
        string cs = File.ReadAllText(Path.Combine(root, "unity/Assets/Raceday/Scripts/Night/LampLight.cs"));
        string rig = File.ReadAllText(Path.Combine(root, "unity/Assets/Raceday/Scripts/Night/NightLighting.cs"));
        string hk = Squash("return x <= k ? x : k + (most - k) * (1.0 - exp(-(x - k) / (most - k)));");
        string ck = Squash("return x <= k ? x : k + (most - k) * (1f - Mathf.Exp(-(x - k) / (most - k)));");
        if (!Squash(hlsl).Contains(hk)) bad.Add("Lamps.hlsl's LampKnee is not the knee");
        if (!Squash(cs).Contains(ck)) bad.Add("LampLight.Knee is not the knee");
        if (!Squash(hlsl).Contains(Squash("e = LampKnee(raw, _TgLampBudget.y);"))) bad.Add("one lamp's cap is not _TgLampBudget.y");
        if (!Squash(hlsl).Contains(Squash("LampKnee(irradiance, _TgLampBudget.z)"))) bad.Add("all lamps' cap is not _TgLampBudget.z");
        if (!Squash(hlsl).Contains(Squash("LampKnee(l, _TgLampBudget.w)"))) bad.Add("the pixel cap is not _TgLampBudget.w");
        foreach (Match loop in Regex.Matches(hlsl, @"LIGHT_LOOP_BEGIN(.*?)LIGHT_LOOP_END", RegexOptions.Singleline))
            if (loop.Groups[1].Value.Contains("FresnelCorrection")) bad.Add("the lamps get the full Fresnel sheen, which whites out a floodlit road");
        if (!Squash(rig).Contains(Squash("Shader.SetGlobalVector(BudgetId, NightBudget.ShaderBudget)"))) bad.Add("NightLighting doesn't set the budget");
        Vector4 b = NightBudget.ShaderBudget;
        if (b.x != 1f || b.y != NightBudget.LampMost || b.z != NightBudget.AllMost || b.w != NightBudget.PixelMost) bad.Add("ShaderBudget is not the budget");
        float last = -1f;
        for (float x = 0f; x < 1e5f; x = x * 1.02f + 0.001f)
        {
            float k = LampLight.Knee(x, 1f);
            if (k < last - 1e-6f || k > 1f) { bad.Add($"the knee at {x} is {k}"); break; }
            last = k;
        }
        Check.Report("shader matches", bad, $"one lamp under {NightBudget.LampMost}, all under {NightBudget.AllMost}, a pixel under {NightBudget.PixelMost}, the knee rising and bounded");
    }

    /// <summary>One headlamp, on the road ahead and on a surface facing it at every distance.</summary>
    static void OneHeadlamp()
    {
        var bad = new List<string>();
        Lamp l = Headlamp(Vector3.zero, Vector3.forward);
        var road = new List<string>();
        float most = 0f;
        for (float d = 0.2f; d <= 90f; d += 0.1f)
        {
            Vector3 at = new Vector3(0f, 0f, 2.6f + d);
            float onRoad = Light(new[] { l }, at, Vector3.up);
            Vector3 wall = new Vector3(0f, 0.7f, 2.6f + d);
            float facing = Light(new[] { l }, wall, (l.Position - wall).normalized);
            most = Math.Max(most, Math.Max(onRoad, facing));
            if (onRoad > NightBudget.LampMost || facing > NightBudget.LampMost) bad.Add($"{Math.Max(onRoad, facing):0.00} at {d:0.0} m");
        }
        foreach (float d in new[] { 5f, 10f, 20f, 30f, 45f, 60f, 80f })
            road.Add($"{d:0} m {Light(new[] { l }, new Vector3(0f, 0f, 2.6f + d), Vector3.up):0.00}");
        Check.Report("one headlamp", bad, $"at most {most:0.00}, under {NightBudget.LampMost}; on the road ahead {string.Join(", ", road)}");
    }

    /// <summary>The floodlights alone along the main stand's straight, across its width, and the pit lane in front of the garages.</summary>
    static void Floodlit(Circuit c, PitComplex pc, Grandstands stands, FloodlightLayout floods)
    {
        var bad = new List<string>();
        Lamp[] lamps = floods.Sources.Select(Flood).ToArray();
        StandSite main = Grandstands.Sites.First(s => s.Main);
        (float from, float to) = stands.Span(main);
        var straight = new List<float>();
        for (float d = from; c.Ahead(from, Mathf.Repeat(d, c.Lap)) <= c.Ahead(from, to); d += 2f)
        {
            int k = c.IndexOf(Mathf.Repeat(d, c.Lap));
            foreach (float lat in new[] { -0.6f, 0f, 0.6f })
                straight.Add(Light(lamps, c.Surface(k, lat * c.HalfWidth(k)), Vector3.up));
            if (d - from > c.Lap) break;
        }
        var lane = new List<float>();
        for (float g = 0.5f; g < pc.Garages; g += 0.25f)
        {
            Vector3 p = pc.RowPoint(g, 0f, pc.Depth * 0.5f + c.Data.pit.laneWidth * 0.5f);
            p.y = floods.Ground(p);
            lane.Add(Light(lamps, p, Vector3.up));
        }
        if (Environment.GetEnvironmentVariable("NIGHT_DUMP") != null)
        {
            Console.WriteLine("straight, every 2 m, left middle right:");
            for (int i = 0; i + 2 < straight.Count; i += 3) Console.Write($" {straight[i]:0.00}/{straight[i + 1]:0.00}/{straight[i + 2]:0.00}");
            Console.WriteLine();
            Console.WriteLine("lane: " + string.Join(" ", lane.Select(v => v.ToString("0.00"))));
            int worst = lane.IndexOf(lane.Min());
            Vector3 wp = pc.RowPoint(0.5f + worst * 0.25f, 0f, pc.Depth * 0.5f + c.Data.pit.laneWidth * 0.5f);
            wp.y = floods.Ground(wp);
            Console.WriteLine($"darkest lane point {wp.x:0.0},{wp.y:0.0},{wp.z:0.0}");
            foreach (FloodSource s in floods.Sources.OrderBy(s => (s.Position - wp).sqrMagnitude).Take(6))
                Console.WriteLine($"  {s.Kind} at {(s.Position - wp).magnitude:0.0} m, target {(s.Target - wp).magnitude:0.0} m off, cone {s.Cone:0}, I {s.Intensity:0}, gives {LampLight.Raw(s.Intensity, s.Position, s.Aim, s.Range, s.Cone, s.Core, wp, Vector3.up):0.000}, aim y {s.Aim.y:0.00}");
        }
        float sMin = straight.Min(), sMax = straight.Max(), lMin = lane.Min(), lMax = lane.Max();
        float dim = DimMost * NightBudget.Floodlit;
        if (sMin < dim) bad.Add($"the straight down to {sMin:0.00}");
        if (lMin < dim) bad.Add($"the pit lane down to {lMin:0.00}");
        if (sMax > EvenMost * sMin) bad.Add($"the straight from {sMin:0.00} to {sMax:0.00}");
        if (lMax > EvenMost * lMin) bad.Add($"the pit lane from {lMin:0.00} to {lMax:0.00}");
        Check.Report("floodlit", bad, $"the straight {sMin:0.00} to {sMax:0.00}, shown {Shown(Pixel(Asphalt, sMin)):0.00} to {Shown(Pixel(Asphalt, sMax)):0.00}; "
            + $"the pit lane {lMin:0.00} to {lMax:0.00}; white lines shown {Shown(Pixel(Line, sMin)):0.00} to {Shown(Pixel(Line, sMax)):0.00}");
    }

    static int Nearest(Circuit c, Vector3 p)
    {
        int best = 0;
        float nearest = float.MaxValue;
        for (int i = 0; i < c.Count; i++)
        {
            float d = Check.Flat(c.Centre[i] - p).sqrMagnitude;
            if (d < nearest) { nearest = d; best = i; }
        }
        return best;
    }

    /// <summary>The asphalt and the white lines, linear albedo.</summary>
    const float Asphalt = 0.024f, Line = 0.83f;

    /// <summary>
    /// The full grid at night, every car's headlamps on (more than the game ever lights) with every floodlight, nothing
    /// in the way: on the road round the grid, and on every car's tail, roof, bonnet and sides.
    /// </summary>
    static void FullGrid(Circuit c, FloodlightLayout floods)
    {
        var bad = new List<string>();
        GridSlot[] slots = c.Data.grid.slots;
        var cars = new List<(Vector3 ground, Vector3 forward)>();
        foreach (GridSlot s in slots)
        {
            int k = c.IndexOf(s.d - 2.6f);
            Vector3 f = Check.Flat(c.Forward[k]).normalized;
            cars.Add((c.Surface(k, s.lateral), f));
        }
        var lamps = new List<Lamp>();
        Vector3 mid = cars.Aggregate(Vector3.zero, (a, b) => a + b.ground) / cars.Count;
        lamps.AddRange(floods.Sources.Where(s => Check.Flat(s.Target - mid).magnitude < 600f).Select(Flood));
        int floodCount = lamps.Count;
        lamps.AddRange(cars.Select(car => Headlamp(car.ground, car.forward)));

        float road = 0f, roadFloods = float.MaxValue, carMost = 0f, pixelMost = 0f;
        string worst = "", darkAt = "";
        Lamp[] floodOnly = lamps.Take(floodCount).ToArray();
        foreach ((Vector3 g, Vector3 f) in cars)
        {
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
            for (float a = -8f; a <= 12f; a += 0.5f)
                for (float x = -1f; x <= 1f; x += 0.125f)
                {
                    int k = Nearest(c, g + f * a);
                    Vector3 p = c.Surface(k, x * (c.HalfWidth(k) - 0.3f));
                    road = Math.Max(road, Light(lamps, p, Vector3.up));
                    float fl = Light(floodOnly, p, Vector3.up);
                    if (fl < roadFloods) { roadFloods = fl; darkAt = $"car {cars.IndexOf((g, f)) + 1}, {a:0.0} m ahead, {x:0.00} across"; }
                }
            var faces = new (string name, Vector3 at, Vector3 n)[]
            {
                ("tail", g + f * -2.2f + Vector3.up * 0.6f, -f), ("roof", g + Vector3.up * 1.4f, Vector3.up),
                ("bonnet", g + f * 1.6f + Vector3.up * 0.9f, (Vector3.up + f * 0.3f).normalized),
                ("left side", g - r * 0.9f + Vector3.up * 0.6f, -r), ("right side", g + r * 0.9f + Vector3.up * 0.6f, r),
                ("rear screen", g + f * -1.3f + Vector3.up * 1.15f, (Vector3.up - f).normalized),
            };
            foreach (var face in faces)
            {
                float e = Light(lamps, face.at, face.n);
                float px = Pixel(White, e);
                if (e > carMost) { carMost = e; worst = face.name; }
                pixelMost = Math.Max(pixelMost, px);
            }
        }
        if (Environment.GetEnvironmentVariable("NIGHT_DUMP") != null)
            foreach ((Vector3 g, Vector3 f) in cars)
            {
                int k = Nearest(c, g);
                Console.WriteLine($"  car {cars.IndexOf((g, f)) + 1}: " + string.Join(" ", new[] { -1f, -0.5f, 0f, 0.5f, 1f }.Select(x => Light(floodOnly, c.Surface(k, x * (c.HalfWidth(k) - 0.3f)), Vector3.up).ToString("0.00"))));
            }
        if (road > NightBudget.AllMost || carMost > NightBudget.AllMost) bad.Add($"{Math.Max(road, carMost):0.00} over {NightBudget.AllMost}");
        if (Shown(Pixel(White, carMost)) > WhiteMost) bad.Add($"a white car's {worst} shown {Shown(Pixel(White, carMost)):0.00}");
        if (roadFloods < GridLit * NightBudget.Floodlit) bad.Add($"the grid under the floodlights down to {roadFloods:0.00} ({darkAt})");
        if (pixelMost > BloomThreshold) bad.Add($"a white car's pixel {pixelMost:0.00} over the bloom's threshold {BloomThreshold}");
        Check.Report("full grid", bad, $"{cars.Count} cars' headlamps and {floodCount} floodlights: the road under the floodlights alone at least {roadFloods:0.00} ({darkAt}), with every lamp at most {road:0.00} (shown {Shown(Pixel(Asphalt, road)):0.00}); "
            + $"a white car at most {carMost:0.00} on its {worst}, pixel {pixelMost:0.00}, shown {Shown(Pixel(White, carMost)):0.00}");
    }
}
