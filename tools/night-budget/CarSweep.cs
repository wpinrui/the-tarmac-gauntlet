// A car swept round the whole lap and down the pit lane at night: at every point, what its paint mirrors (its reflection
// probe, CarReflections) and how bright the clear coat then shows it, glint and all (CarPaint.hlsl, mirrored here).
// The probe is a cube of directions, each holding the brightest thing seen that way and how much light comes from it;
// the things are the pit building's lit windows and the floodlights' lenses, nothing in the way, so it sees at least as
// much as the game's probe. Every other surface is lamp-lit and stays under NightBudget.PixelMost.
//
// Rules:
//   glint matches   CarPaint.hlsl's CAR_GLINT_* are NightBudget's Glint* numbers
//   only lenses     every lamp-lit surface and every pit window is under the glint's threshold, every lens over it
//   car round lap   wherever the car is, the share of what it mirrors that its paint shows over the bloom's threshold,
//                   at a 70 degree glance, stays under WhiteoutMost (2%): small lights streak, nothing broad whites the car out;
//                   and the floodlights' lenses still glint somewhere on the lap
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Rendering;
using UnityEngine;

static class CarSweep
{
    /// <summary>The bloom's threshold, linear.</summary>
    const float BloomThreshold = 1.2f;
    /// <summary>
    /// The most of the world round a car, as a share of the whole sphere, its paint may show over the bloom's threshold: a
    /// lens streaking across a panel, never the body. The pit windows glinting whited out 7.5% by the pit lane.
    /// </summary>
    const float WhiteoutMost = 0.02f;
    /// <summary>The glance the paint is judged at: the cosine between the view and the panel (70 degrees off square).</summary>
    const float Glance = 0.34f;
    /// <summary>The probe's height over the road (CarReflections.Height), how far it sees (NearSight), m, and its cube's face, cells.</summary>
    const float Height = 0.8f, Sight = 600f;
    const int Face = 64;
    /// <summary>The clear coat's smoothness (CAR_COAT_SMOOTHNESS) and its sheen (CAR_COAT_SHEEN, _GRAZING).</summary>
    const float CoatSmoothness = 0.965f, Sheen = 0.1f, SheenGrazing = 0.4f;
    /// <summary>
    /// How wide the probe's blur at the glint's broad roughness spreads light, radians: URP's prefiltered mip at a
    /// perceptual roughness of 0.6 spreads a point over about this much either way.
    /// </summary>
    const float BroadSpread = 0.3f;
    /// <summary>The pit glass's most reflectance at a glance at its night smoothness, and the brightest it can mirror: lit concrete.</summary>
    const float GlassReflect = 0.64f, GlassSees = 0.5f;

    struct Source
    {
        public Vector3 A, B, C, Normal;
        public float Peak, Mean;
        public bool Lens;
    }

    static readonly float[] cellAngle = CellAngles();

    static float[] CellAngles()
    {
        var a = new float[Face * Face];
        for (int y = 0; y < Face; y++)
            for (int x = 0; x < Face; x++)
            {
                float u = (x + 0.5f) / Face * 2f - 1f, v = (y + 0.5f) / Face * 2f - 1f;
                float d = 1f + u * u + v * v;
                a[y * Face + x] = (2f / Face) * (2f / Face) / (d * Mathf.Sqrt(d));
            }
        return a;
    }

    static int Cell(Vector3 d)
    {
        float ax = Math.Abs(d.x), ay = Math.Abs(d.y), az = Math.Abs(d.z);
        int face;
        float u, v;
        if (ax >= ay && ax >= az) { face = d.x > 0 ? 0 : 1; u = d.z / ax; v = d.y / ax; }
        else if (ay >= az) { face = d.y > 0 ? 2 : 3; u = d.x / ay; v = d.z / ay; }
        else { face = d.z > 0 ? 4 : 5; u = d.x / az; v = d.y / az; }
        int x = Math.Min(Face - 1, (int)((u * 0.5f + 0.5f) * Face)), y = Math.Min(Face - 1, (int)((v * 0.5f + 0.5f) * Face));
        return face * Face * Face + y * Face + x;
    }

    static Vector3 Direction(int cell)
    {
        int face = cell / (Face * Face), y = cell % (Face * Face) / Face, x = cell % Face;
        float u = (x + 0.5f) / Face * 2f - 1f, v = (y + 0.5f) / Face * 2f - 1f;
        Vector3 d = face switch
        {
            0 => new Vector3(1f, v, u), 1 => new Vector3(-1f, v, u),
            2 => new Vector3(u, 1f, v), 3 => new Vector3(u, -1f, v),
            4 => new Vector3(u, v, 1f), _ => new Vector3(u, v, -1f),
        };
        return d.normalized;
    }

    /// <summary>URP's clear coat environment weight at a full coat (CarCoatUrpWeight).</summary>
    static float UrpWeight(float NoV, float perceptualRoughness)
    {
        float r = perceptualRoughness * perceptualRoughness;
        float grazing = Mathf.Clamp01(1f - perceptualRoughness + 0.04f);
        float f = 1f - NoV;
        return (0.04f + (grazing - 0.04f) * f * f * f * f) / (r * r + 1f);
    }

    /// <summary>What the coat shows of something this bright with this much round it, as CarPaint.hlsl adds it up at full night.</summary>
    static float Coat(float env, float around, float NoV, float from, float spread)
    {
        float rough = 1f - CoatSmoothness;
        float f = 1f - NoV;
        float sheen = Sheen + SheenGrazing * f * f * f;
        float urp = UrpWeight(NoV, rough);
        float hot = NightBudget.GlintGain * Math.Max(env - Math.Max(from, around * spread), 0f) / Math.Max(env, 1e-3f);
        return env * (urp + sheen) * (1f + hot);
    }

    public static void Run(string root, Circuit c, PitComplex pc, List<CircuitPart> pitParts, List<CircuitPart> lit)
    {
        GlintMatches(root);
        var sources = Sources(pitParts, lit);
        OnlyLenses(sources);
        RoundLap(c, pc, sources);
    }

    static string Squash(string s) => Regex.Replace(s, @"\s+", "");

    static void GlintMatches(string root)
    {
        var bad = new List<string>();
        string hlsl = File.ReadAllText(Path.Combine(root, "unity/Assets/Raceday/Resources/CarPaint/CarPaint.hlsl"));
        void Is(string name, float value)
        {
            Match m = Regex.Match(hlsl, @"#define\s+" + name + @"\s+([0-9.]+)");
            if (!m.Success || Math.Abs(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - value) > 1e-4f)
                bad.Add($"{name} is not {value}");
        }
        Is("CAR_GLINT_FROM", NightBudget.GlintFrom);
        Is("CAR_GLINT_GAIN", NightBudget.GlintGain);
        Is("CAR_GLINT_SPREAD", NightBudget.GlintSpread);
        Is("CAR_GLINT_BROAD", NightBudget.GlintBroad);
        Is("CAR_COAT_SMOOTHNESS", CoatSmoothness);
        Is("CAR_COAT_SHEEN", Sheen);
        Is("CAR_COAT_SHEEN_GRAZING", SheenGrazing);
        if (!Squash(hlsl).Contains(Squash("half from = max(CAR_GLINT_FROM, max(around.r, max(around.g, around.b)) * CAR_GLINT_SPREAD);")))
            bad.Add("the glint isn't held off by what is round it");
        if (!Squash(hlsl).Contains(Squash("GlossyEnvironmentReflection(r, d.positionWS, CAR_GLINT_BROAD, 1, d.normalizedScreenSpaceUV)")))
            bad.Add("what is round the glint isn't read at CAR_GLINT_BROAD");
        Check.Report("glint matches", bad, $"from {NightBudget.GlintFrom}, {NightBudget.GlintSpread} times what is round it at roughness {NightBudget.GlintBroad}, gain {NightBudget.GlintGain}");
    }

    /// <summary>The pit building's windows, as PitMaterials lights them at night, and the floodlights' lenses.</summary>
    static List<Source> Sources(List<CircuitPart> pitParts, List<CircuitPart> lit)
    {
        var rooms = PitComplexBuilder.WindowLight();
        float roomMean = rooms.Average(r => Math.Max(LinearLight.Linear(r.r), Math.Max(LinearLight.Linear(r.g), LinearLight.Linear(r.b))));
        int pane = 0, all = 0;
        for (float a = 0.0125f; a < 1f; a += 0.025f)
            for (float u = 0.0125f; u < 1f; u += 0.025f, all++)
                if (!PitComplexBuilder.WindowFrame(a, u)) pane++;
        float glass = NightBudget.WindowGlow + GlassReflect * GlassSees;
        var list = new List<Source>();
        void Add(IEnumerable<CircuitPart> parts, string name, float peak, float mean, bool lens)
        {
            foreach (CircuitPart p in parts.Where(p => p.Name == name))
            {
                IReadOnlyList<Vector3> v = p.Kit.Vertices;
                IReadOnlyList<int> t = p.Kit.Triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], cc = v[t[i + 2]];
                    Vector3 n = Vector3.Cross(b - a, cc - a);
                    if (n.sqrMagnitude < 1e-10f) continue;
                    list.Add(new Source { A = a, B = b, C = cc, Normal = n.normalized, Peak = peak, Mean = mean, Lens = lens });
                }
            }
        }
        Add(pitParts, "Pit building glass", glass, NightBudget.WindowGlow * roomMean * pane / all + GlassReflect * GlassSees, false);
        Add(lit, "Floodlight lenses", NightBudget.FloodLens, NightBudget.FloodLens, true);
        return list;
    }

    static void OnlyLenses(List<Source> sources)
    {
        var bad = new List<string>();
        float window = sources.Where(s => !s.Lens).Select(s => s.Peak).DefaultIfEmpty(0f).Max();
        if (NightBudget.PixelMost >= NightBudget.GlintFrom) bad.Add($"a lamp-lit pixel ({NightBudget.PixelMost}) glints");
        if (window >= NightBudget.GlintFrom) bad.Add($"a pit window ({window:0.00}) glints");
        foreach ((string name, float level) in new[] { ("floodlight lens", NightBudget.FloodLens), ("headlamp", NightBudget.HeadlampGlow), ("brake lamp", NightBudget.BrakeGlow), ("tail lamp", NightBudget.TailGlow) })
            if (level <= NightBudget.GlintFrom) bad.Add($"a {name} ({level}) doesn't glint");
        Check.Report("only lenses", bad, $"lamp-lit surfaces under {NightBudget.PixelMost}, the pit windows under {window:0.00}, the glint from {NightBudget.GlintFrom}, the dimmest lens {NightBudget.TailGlow}");
    }

    /// <summary>Every 2 m round the lap on the racing surface's middle, and down the pit lane.</summary>
    static IEnumerable<(string where, Vector3 at)> Places(Circuit c, PitComplex pc)
    {
        int stride = Math.Max(1, (int)Math.Round(2f / c.Step));
        for (int k = 0; k < c.Count; k += stride)
            yield return ($"{k * c.Step:0} m round the lap", c.Surface(k, 0f) + Vector3.up * Height);
        float lane = pc.Depth * 0.5f + c.Data.pit.laneWidth * 0.5f;
        for (float g = 0f; g <= pc.Garages; g += 0.25f)
        {
            Vector3 p = pc.RowPoint(g, 0f, lane);
            p.y = c.Surface(Nearest(c, p), 0f).y;
            yield return ($"the pit lane at garage {g:0.0}", p + Vector3.up * Height);
        }
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

    /// <summary>What the probe at this point holds: each cell's brightest, and its light spread over the cell.</summary>
    static void Probe(Vector3 at, List<Source> sources, Dictionary<int, (float peak, float light, bool lens)> cells)
    {
        cells.Clear();
        float cell = Mathf.PI * 0.5f / Face;
        foreach (Source s in sources)
        {
            Vector3 mid = (s.A + s.B + s.C) / 3f;
            Vector3 to = mid - at;
            float d = to.magnitude;
            if (d > Sight || d < 0.05f) continue;
            if (Vector3.Dot(s.Normal, to) >= 0f) continue;
            float edge = Math.Max((s.B - s.A).magnitude, Math.Max((s.C - s.B).magnitude, (s.A - s.C).magnitude));
            int n = Math.Clamp((int)Math.Ceiling(edge / (d * cell) * 1.5f), 1, 48);
            float area = Vector3.Cross(s.B - s.A, s.C - s.A).magnitude * 0.5f / (n * n);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n - i; j++)
                    for (int up = 0; up < (j + i < n - 1 ? 2 : 1); up++)
                    {
                        // The n squared small triangles of the source, each by its middle.
                        float a = up == 0 ? (i + 1f / 3f) / n : (i + 2f / 3f) / n;
                        float b = up == 0 ? (j + 1f / 3f) / n : (j + 2f / 3f) / n;
                        Vector3 p = s.A + (s.B - s.A) * a + (s.C - s.A) * b;
                        Vector3 dir = p - at;
                        float dd = dir.sqrMagnitude;
                        if (dd < 1e-4f) continue;
                        float dist = Mathf.Sqrt(dd);
                        float facing = -Vector3.Dot(s.Normal, dir) / dist;
                        if (facing <= 0f) continue;
                        float solid = area * facing / dd;
                        int k = Cell(dir);
                        cells.TryGetValue(k, out var was);
                        cells[k] = (Math.Max(was.peak, s.Peak), was.light + s.Mean * solid, was.lens || s.Lens);
                    }
        }
    }

    static void RoundLap(Circuit c, PitComplex pc, List<Source> sources)
    {
        var bad = new List<string>();
        var cells = new Dictionary<int, (float peak, float light, bool lens)>();
        float norm = 0f;
        for (int k = 0; k < 6 * Face * Face; k++)
        {
            float t = Vector3.Angle(Direction(k), Vector3.up) * Mathf.Deg2Rad;
            norm += Mathf.Exp(-t * t / (2f * BroadSpread * BroadSpread)) * cellAngle[k % (Face * Face)];
        }
        float from = NightBudget.GlintFrom, spread = NightBudget.GlintSpread;
        if (Environment.GetEnvironmentVariable("NIGHT_OLD_GLINT") != null) { from = 1.2f; spread = 0f; }
        float worst = 0f, worstBright = 0f, lensGlint = 0f;
        string worstAt = "";
        // NIGHT_PAINT_DUMP writes the worst place's picture, or NIGHT_PAINT_PLACE's.
        string place = Environment.GetEnvironmentVariable("NIGHT_PAINT_PLACE");
        Vector3 shownAt = Vector3.zero;
        int places = 0;
        foreach ((string where, Vector3 at) in Places(c, pc))
        {
            places++;
            Probe(at, sources, cells);
            var dirs = cells.Keys.ToDictionary(k => k, Direction);
            float over = 0f, bright = 0f;
            foreach (var kv in cells)
            {
                float env = kv.Value.peak;
                if (Coat(env, 0f, Glance, from, spread) <= BloomThreshold) continue;
                // What the probe's blur at the broad roughness holds round this direction.
                float around = 0f;
                Vector3 d = dirs[kv.Key];
                foreach (var o in cells)
                {
                    float t = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, dirs[o.Key]), -1f, 1f));
                    if (t > BroadSpread * 3f) continue;
                    around += Mathf.Exp(-t * t / (2f * BroadSpread * BroadSpread)) * o.Value.light;
                }
                around /= norm;
                float shown = Coat(env, around, Glance, from, spread);
                if (kv.Value.lens) lensGlint = Math.Max(lensGlint, shown);
                if (shown > BloomThreshold) over += cellAngle[kv.Key % (Face * Face)];
                bright = Math.Max(bright, shown);
            }
            float share = over / (4f * Mathf.PI);
            if (share > worst) { worst = share; worstAt = where; worstBright = bright; }
            if (share >= worst && place == null || where == place) shownAt = at;
        }
        string dump = Environment.GetEnvironmentVariable("NIGHT_PAINT_DUMP");
        if (dump != null) Paint(dump, shownAt, sources, cells, norm, from, spread);
        if (worst > WhiteoutMost) bad.Add($"{worst * 100f:0.00}% of what the paint mirrors over the bloom's threshold at {worstAt}, as bright as {worstBright:0.0}");
        if (lensGlint <= BloomThreshold) bad.Add("the floodlights' lenses no longer glint in the paint");
        Check.Report("car round lap", bad, $"{places} places: at most {worst * 100f:0.000}% of what the paint mirrors over the bloom's threshold ({worstAt}); the lenses still glint, up to {lensGlint:0.0}");
    }

    /// <summary>
    /// What the paint shows of the world round a car at this point at a 70 degree glance, as a picture a direction a
    /// pixel (longitude across, latitude up), the night's exposure and ACES applied: a portable greymap of linear values
    /// written to the path, for looking at the worst place.
    /// </summary>
    static void Paint(string path, Vector3 at, List<Source> sources, Dictionary<int, (float peak, float light, bool lens)> cells, float norm, float from, float spread)
    {
        Probe(at, sources, cells);
        var dirs = cells.Keys.ToDictionary(k => k, Direction);
        var shown = new Dictionary<int, float>();
        foreach (var kv in cells)
        {
            float around = 0f;
            Vector3 d = dirs[kv.Key];
            foreach (var o in cells)
            {
                float t = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, dirs[o.Key]), -1f, 1f));
                if (t > BroadSpread * 3f) continue;
                around += Mathf.Exp(-t * t / (2f * BroadSpread * BroadSpread)) * o.Value.light;
            }
            shown[kv.Key] = Coat(kv.Value.peak, around / norm, Glance, from, spread);
        }
        const int W = 720, H = 360;
        using var w = new StreamWriter(path);
        w.Write($"P2\n{W} {H}\n65535\n");
        for (int y = 0; y < H; y++)
        {
            float lat = (0.5f - (y + 0.5f) / H) * Mathf.PI;
            for (int x = 0; x < W; x++)
            {
                float lon = ((x + 0.5f) / W * 2f - 1f) * Mathf.PI;
                var d = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
                shown.TryGetValue(Cell(d), out float v);
                w.Write((int)Math.Min(65535f, v * 1000f));
                w.Write(x + 1 < W ? ' ' : '\n');
            }
        }
    }
}
