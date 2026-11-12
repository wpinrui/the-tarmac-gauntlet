// Makes the track surface's textures and the racing line map exactly as the game does (TrackTextures, CircuitBuilder,
// RacingLine from circuit.json), checks them, and writes previews. Each check prints one line: ok, or FAIL with
// details. Exit code 1 if any check fails.
//
// Checks:
//   textures repeat       every texture's opposite edges match as well as its neighbouring pixels do, so no seam
//                         shows where the tile repeats (a kerb's and the rubber marks' only along, as they don't
//                         repeat across)
//   repairs clear of ends no sealant or patch within 0.8 m of the repairs tile's ends, so a repeat shifted or mirrored
//                         against the next shows no seam, and some of both in the tile
//   marks fade at edges   the rubber marks are clear of the tile's sides, so the clamped edge never streaks, and
//                         darker near the line than 3 m off it
//   line map on the road  the road's uv along it, as the builder lays it, matches CircuitBuilder.RoadAlong within
//                         2 cm, so the shader finds the racing line at the right place on the road
//   line on the road      the racing line runs at least 0.9 m inside both road edges, so its tyre tracks stay on it
//   rubber in the corners every corner has more rubber on its line than the longest straight
//   ripples repeat        the raindrop ripple tile's opposite edges match, and every drop's ring points out from it
//   puddles in the dips   the road is ready to hold water in a share of the lap, not all of it, most of all at the
//                         lap's lowest point, and the camber's drain side follows the crossfall
//   wet builds and dries  as the water deepens the road darkens, shines and stands more puddles, never less, and the
//                         racing line holds less water than the rest of the road
//
// Previews, in the out folder given: tarmac.png (colour, lit by a low sun), patches.png, kerbs.png (clean and worn,
// lit), repairs.png (sealant, patches and their shades), marks.png, rubber.png (the road through the tightest corner,
// straightened, as the shader draws it dry: grain, patches, repairs, rubber and marks, lit, each pixel averaged over
// its footprint as a mip would), road-close.png (6 m of that road at 100 pixels a metre), ripples.png
// (the ripple tile's rings and drop directions) and wet.png (the road through the lap's deepest dip, straightened, as
// the shader draws it at a film of 0.05, 0.25, 0.6 and 1.5 mm: damp, drying on the line, wet, flooded).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    static bool failed;

    static void Report(bool ok, string name, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {name}: {detail}");
        if (!ok) failed = true;
    }

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        string outDir = args.Length > 1 ? args[1] : ".";
        var circuit = new Circuit(data);
        var line = new RacingLine(circuit);
        int n = circuit.Count;

        TrackTextures.Tarmac tarmac = TrackTextures.MakeTarmac(1);
        Color[] road = TrackTextures.TarmacColour(tarmac, new Color(0.17f, 0.17f, 0.18f), 0.25f);
        Color[] roadNormals = TrackTextures.TarmacNormals(tarmac, TrackTextures.TarmacBump);
        Color[] repairs = TrackTextures.Repairs(12), marks = TrackTextures.Marks(14);
        int ra = TrackTextures.RepairsAcross, rl = TrackTextures.RepairsAlong, mka = TrackTextures.MarksAcross, mkl = TrackTextures.MarksAlong;
        Color[] macro = TrackTextures.Macro(8);
        Color[] kerbClean = TrackTextures.Kerb(false, 9), kerbWorn = TrackTextures.Kerb(true, 9);
        Color[] kerbCleanN = TrackTextures.KerbNormals(false, 9), kerbWornN = TrackTextures.KerbNormals(true, 9);
        int ts = TrackTextures.TarmacSize, ms = TrackTextures.MacroSize, ka = TrackTextures.KerbAcross, kl = TrackTextures.KerbAlong;

        // ---- textures repeat
        var seams = new List<string>();
        void Seam(string name, Color[] px, int w, int h, bool acrossToo)
        {
            float inside = 0f, edge = 0f;
            for (int y = 0; y < h; y++)
            {
                inside += Diff(px[y * w + w / 2], px[y * w + w / 2 + 1]);
                if (acrossToo) edge += Diff(px[y * w + w - 1], px[y * w]);
            }
            for (int x = 0; x < w; x++)
            {
                inside += Diff(px[(h / 2) * w + x], px[(h / 2 + 1) * w + x]);
                edge += Diff(px[(h - 1) * w + x], px[x]);
            }
            int count = acrossToo ? w + h : w;
            int insideCount = w + h;
            if (edge / count > 2.5f * inside / insideCount) seams.Add($"{name} {edge / count:0.000} vs {inside / insideCount:0.000}");
        }
        Seam("tarmac", road, ts, ts, true);
        Seam("tarmac normals", roadNormals, ts, ts, true);
        Seam("patches", macro, ms, ms, true);
        Seam("worn kerb normals", kerbWornN, ka, kl, false);
        Seam("repairs", repairs, ra, rl, true);
        Seam("rubber marks", marks, mka, mkl, false);
        Report(seams.Count == 0, "textures repeat", seams.Count == 0 ? "6 textures" : string.Join(", ", seams));

        // ---- repairs clear of ends
        int clearRows = (int)Math.Ceiling(0.8f / (TrackTextures.RepairsMetresAlong / rl));
        float atEnds = 0f, sealShare = 0f, patchShare = 0f;
        for (int y = 0; y < rl; y++)
            for (int x = 0; x < ra; x++)
            {
                Color c = repairs[y * ra + x];
                if (y < clearRows || y >= rl - clearRows) atEnds = Math.Max(atEnds, Math.Max(c.r, c.g));
                sealShare += c.r;
                patchShare += c.g;
            }
        sealShare /= ra * rl;
        patchShare /= ra * rl;
        Report(atEnds == 0f && sealShare > 0.003f && patchShare > 0.02f && patchShare < 0.25f, "repairs clear of ends",
            $"most at the ends {atEnds:0.00}; sealant over {sealShare * 100f:0.0}% of the tile, patches over {patchShare * 100f:0.0}%");

        // ---- marks fade at edges
        float atSides = 0f, nearLine = 0f, offLine = 0f;
        int nearCount = 0, offCount = 0;
        for (int y = 0; y < mkl; y++)
            for (int x = 0; x < mka; x++)
            {
                float across = (x + 0.5f) * TrackTextures.MarksMetresAcross / mka - TrackTextures.MarksMetresAcross / 2f;
                float v = marks[y * mka + x].r;
                if (x == 0 || x == mka - 1) atSides = Math.Max(atSides, v);
                if (Math.Abs(across) < 1.2f) { nearLine += v; nearCount++; }
                else if (Math.Abs(across) > 3f) { offLine += v; offCount++; }
            }
        nearLine /= nearCount;
        offLine /= offCount;
        Report(atSides < 0.01f && nearLine > 2f * offLine && nearLine > 0.03f, "marks fade at edges",
            $"most at the sides {atSides:0.000}; within 1.2 m of the line {nearLine:0.000}, beyond 3 m {offLine:0.000}");

        // ---- line map on the road
        float[] along = CircuitBuilder.RoadAlong(circuit);
        var builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> parts = builder.Build();
        float worst = 0f;
        int checkedVerts = 0;
        foreach (CircuitPart part in parts)
        {
            if (part.Name != "Road") continue;
            IReadOnlyList<Vector3> v = part.Kit.Vertices;
            IReadOnlyList<Vector2> uv = part.Kit.UVs;
            for (int i = 0; i < v.Count; i++)
            {
                int k = Nearest(circuit, v[i]);
                float s = uv[i].y * 4f;
                // The first and last rows share a sample: compare with whichever end is nearer.
                float off = Math.Min(Math.Abs(s - along[k]), k == 0 ? Math.Abs(s - along[n]) : float.MaxValue);
                worst = Math.Max(worst, off);
                checkedVerts++;
            }
        }
        Report(worst < 0.02f, "line map on the road", $"{checkedVerts} road vertices, worst {worst * 100f:0.0} cm off");

        // ---- line on the road
        var fromLeft = new float[n];
        float tightest = float.MaxValue;
        int tightAt = 0;
        for (int k = 0; k < n; k++)
        {
            fromLeft[k] = circuit.HalfWidth(k) - line.Lateral[k];
            float room = Math.Min(fromLeft[k], 2f * circuit.HalfWidth(k) - fromLeft[k]);
            if (room < tightest) { tightest = room; tightAt = k; }
        }
        Report(tightest >= 0.9f, "line on the road", $"closest to an edge {tightest:0.00} m at lap {tightAt * circuit.Step:0} m");

        // ---- rubber in the corners
        int texels = Math.Clamp((int)Math.Ceiling(along[n]), 16, 16384);
        Color[] map = TrackTextures.Line(along, fromLeft, line.Curvature, texels);
        float RubberAt(float s) => Sample1D(map, s / along[n]).g;
        float straight = float.MaxValue, cornerLeast = float.MaxValue;
        int sharpest = 0;
        for (int k = 0; k < n; k++)
        {
            float c = Math.Abs(line.Curvature[k]);
            if (c > Math.Abs(line.Curvature[sharpest])) sharpest = k;
            float r = RubberAt(along[k]);
            if (c > 1f / 60f) cornerLeast = Math.Min(cornerLeast, r);
            if (c < 1f / 2000f) straight = Math.Min(straight, r);
        }
        Report(cornerLeast > straight, "rubber in the corners", $"least in a corner {cornerLeast:0.00}, on the straights {straight:0.00}");

        // ---- ripples repeat
        Color[] ripples = TrackTextures.Ripples(17);
        int rs = TrackTextures.RippleSize;
        var rippleRings = new Color[rs * rs];
        for (int i = 0; i < rippleRings.Length; i++) rippleRings[i] = new Color(ripples[i].r, ripples[i].r, ripples[i].r);
        seams.Clear();
        Seam("ripples", rippleRings, rs, rs, true);
        int pointing = 0, inside = 0;
        for (int y = 1; y < rs - 1; y++)
            for (int x = 1; x < rs - 1; x++)
            {
                Color c = ripples[y * rs + x];
                if (c.r < 0.2f || c.r > 0.8f) continue;
                inside++;
                // Out from the centre is where the ring's nearness falls.
                float dx = ripples[y * rs + x + 1].r - ripples[y * rs + x - 1].r, dy = ripples[(y + 1) * rs + x].r - ripples[(y - 1) * rs + x].r;
                if (dx * (c.g * 2 - 1) + dy * (c.b * 2 - 1) <= 0f) pointing++;
            }
        float pointShare = inside > 0 ? pointing / (float)inside : 0f;
        Report(seams.Count == 0 && pointShare > 0.97f, "ripples repeat", $"{(seams.Count == 0 ? "no seam" : string.Join(", ", seams))}, {pointShare * 100f:0.0}% of ring pixels point out from their drop");

        // ---- puddles in the dips
        var centreHeight = new float[n];
        var crossfall = new float[n];
        var roadWidth = new float[n];
        int lowest = 0;
        for (int k = 0; k < n; k++)
        {
            centreHeight[k] = circuit.Centre[k].y;
            crossfall[k] = circuit.Data.crossfall[k];
            roadWidth[k] = 2f * circuit.HalfWidth(k);
            if (centreHeight[k] < centreHeight[lowest]) lowest = k;
        }
        Color[] pools = TrackTextures.Pools(along, centreHeight, crossfall, roadWidth, texels);
        int ready = 0, sideRight = 0, sideWrong = 0;
        float deepest = 0f;
        int deepAt = 0;
        for (int j = 0; j < pools.Length; j++)
        {
            if (pools[j].r > 0.6f) ready++;
            if (pools[j].r > deepest) { deepest = pools[j].r; deepAt = j; }
        }
        for (int k = 0; k < n; k++)
        {
            Color p = Sample1D(pools, along[k] / along[n]);
            if (Math.Abs(crossfall[k]) < 0.015f) continue;
            if (Math.Sign(p.g) == Math.Sign(crossfall[k])) sideRight++; else sideWrong++;
        }
        float readyShare = ready / (float)pools.Length;
        float atLowest = Sample1D(pools, along[lowest] / along[n]).r;
        Report(readyShare > 0.01f && readyShare < 0.45f && atLowest > 0.6f && sideWrong == 0,
            "puddles in the dips", $"{readyShare * 100f:0.0}% of the lap ready to pool, {atLowest:0.00} at the lowest point (lap {along[lowest]:0} m), camber side right at {sideRight} of {sideRight + sideWrong} cambered samples");

        // ---- wet builds and dries
        float dipAlong = along[n] * (deepAt + 0.5f) / pools.Length;
        int dipK = SampleAt(along, Repeat(dipAlong, along[n]));
        var levels = new[] { 0.02f, 0.05f, 0.1f, 0.25f, 0.4f, 0.6f, 0.9f, 1.3f, 1.8f, 2.5f, 3f };
        float lastDark = 2f, lastShine = -1f, lastStand = -1f;
        bool steady = true;
        float lineWater = 0f, offLineWater = 0f;
        foreach (float wm in levels)
        {
            float dark = 0f, shine = 0f, stand = 0f;
            int count = 0;
            for (float s0 = dipAlong - 60f; s0 < dipAlong + 60f; s0 += 0.5f)
            {
                float s1 = Repeat(s0, along[n]);
                int k = SampleAt(along, s1);
                float w = 2f * circuit.HalfWidth(k);
                for (float a = 0.1f; a < w; a += 0.25f)
                {
                    Wet look = WetAt(map, pools, road, macro, ts, ms, along[n], a, s1, wm, 0.4f, 0.6f);
                    dark += look.Darken;
                    shine += look.Smoothness;
                    stand += look.Puddle;
                    count++;
                }
            }
            dark /= count; shine /= count; stand /= count;
            if (dark > lastDark + 1e-4f || shine < lastShine - 1e-4f || stand < lastStand - 1e-4f) steady = false;
            lastDark = dark; lastShine = shine; lastStand = stand;
        }
        for (int k = 0; k < n; k += 4)
        {
            float onLine = Sample1D(map, along[k] / along[n]).r;
            lineWater += WetAt(map, pools, road, macro, ts, ms, along[n], onLine, along[k], 0.4f, 0f, 0.8f).Water;
            float w = 2f * circuit.HalfWidth(k);
            float off = onLine > w * 0.5f ? onLine - 3.2f : onLine + 3.2f;
            offLineWater += WetAt(map, pools, road, macro, ts, ms, along[n], off, along[k], 0.4f, 0f, 0.8f).Water;
        }
        Report(steady && lineWater < 0.7f * offLineWater, "wet builds and dries",
            $"darker, shinier and more puddles at every depth from 0.02 to 3 mm: {(steady ? "yes" : "no")}; water on the line {lineWater / offLineWater * 100f:0}% of off it after the rain");

        // ---- previews
        Directory.CreateDirectory(outDir);
        WritePng(Path.Combine(outDir, "tarmac.png"), ts * 2, ts, (x, y) =>
            x < ts ? road[y * ts + x] : Lit(road[y * ts + x - ts], roadNormals[y * ts + x - ts]));
        WritePng(Path.Combine(outDir, "patches.png"), ms * 2, ms, (x, y) =>
        {
            Color c = macro[y * ms + x % ms];
            float v = x < ms ? c.r : c.g;
            return new Color(v, v, v);
        });
        WritePng(Path.Combine(outDir, "kerbs.png"), ka * 4, kl, (x, y) =>
        {
            int i = y * ka + x % ka;
            return (x / ka) switch
            {
                0 => kerbClean[i],
                1 => Lit(kerbClean[i], kerbCleanN[i]),
                2 => kerbWorn[i],
                _ => Lit(kerbWorn[i], kerbWornN[i]),
            };
        });

        WritePng(Path.Combine(outDir, "repairs.png"), ra * 3, rl / 2, (x, y) =>
        {
            Color c = repairs[(y * 2) * ra + x % ra];
            float v = (x / ra) switch { 0 => c.r, 1 => c.g, _ => c.g > 0f ? c.b : 0.5f };
            return new Color(v, v, v);
        });
        WritePng(Path.Combine(outDir, "marks.png"), mka, mkl / 2, (x, y) =>
        {
            float v = 1f - marks[(y * 2) * mka + x].r;
            return new Color(v, v, v);
        });

        // The road through the sharpest corner, straightened: across left to right, along bottom to top. Each pixel
        // averages the shader's colour over its footprint, as a mip would.
        float from = along[sharpest] - 140f, to = along[sharpest] + 60f;
        Color Dry(float a, float s)
        {
            int k = SampleAt(along, s);
            float w = 2f * circuit.HalfWidth(k);
            if (a < 0f || a > w) return new Color(0.23f, 0.41f, 0.15f);
            if (a < 0.12f || a > w - 0.12f) return new Color(0.92f, 0.92f, 0.9f);
            return DryAt(map, road, roadNormals, macro, repairs, marks, along[n], a, s);
        }
        void Road(string file, float pxPerM, float a0, float a1, float s0, float s1, int samples)
        {
            int width = (int)((a1 - a0) * pxPerM), height = (int)((s1 - s0) * pxPerM);
            WritePng(Path.Combine(outDir, file), width, height, (x, y) =>
            {
                float r = 0f, g = 0f, b = 0f;
                for (int j = 0; j < samples; j++)
                    for (int i = 0; i < samples; i++)
                    {
                        Color c = Dry(a0 + (x + (i + 0.5f) / samples) / pxPerM, Repeat(s0 + (y + (j + 0.5f) / samples) / pxPerM, along[n]));
                        r += c.r; g += c.g; b += c.b;
                    }
                float q = samples * samples;
                return new Color(r / q, g / q, b / q);
            });
        }
        Road("rubber.png", 8f, -1f, 2f * MaxHalf(circuit) + 1f, from, to, 8);
        float lineHere = Sample1D(map, along[sharpest] / along[n]).r;
        Road("road-close.png", 100f, lineHere - 3f, lineHere + 3f, along[sharpest] - 30f, along[sharpest] - 24f, 2);
        WritePng(Path.Combine(outDir, "ripples.png"), rs * 2, rs, (x, y) =>
        {
            Color c = ripples[y * rs + x % rs];
            return x < rs ? new Color(c.r, c.r, c.r) : new Color(c.g, c.b, 0.5f);
        });

        // The road through the lap's deepest dip, straightened, at four depths of water side by side.
        var depths = new[] { (0.05f, 0.3f, 0f), (0.25f, 0.8f, 0f), (0.6f, 0.4f, 0.5f), (1.5f, 0.35f, 0.9f) };
        const float wetPx = 12f;
        float wetFrom = dipAlong - 50f, wetTo = dipAlong + 50f;
        int panel = (int)(2f * MaxHalf(circuit) * wetPx) + 20, wetHeight = (int)((wetTo - wetFrom) * wetPx);
        WritePng(Path.Combine(outDir, "wet.png"), panel * depths.Length, wetHeight, (x, y) =>
        {
            var (wm, lineDry, rainNow) = depths[x / panel];
            float s = Repeat(wetFrom + y / wetPx, along[n]);
            float a = (x % panel - 10) / wetPx;
            int k = SampleAt(along, s);
            float w = 2f * circuit.HalfWidth(k);
            if (a < 0f || a > w) return new Color(0.23f, 0.41f, 0.15f);
            Color grain = Sample(road, ts, ts, a / 4f, s / 4f);
            Wet look = WetAt(map, pools, road, macro, ts, ms, along[n], a, s, wm, rainNow, lineDry);
            // A grey sky in the shine: more the glossier it is, most where the water stands.
            float mirror = Mathf.Clamp01((look.Smoothness - 0.35f) / 0.6f) * 0.35f + look.Puddle * 0.45f;
            Color sky = new Color(0.62f, 0.66f, 0.7f);
            Color c = new Color(grain.r * look.Darken, grain.g * look.Darken, grain.b * look.Darken);
            return Color.Lerp(c, sky, mirror);
        });
        Console.WriteLine($"previews in {outDir}");
        return failed ? 1 : 0;
    }

    /// <summary>What the track shader's wet part makes of the road at one spot.</summary>
    struct Wet
    {
        public float Water, Darken, Smoothness, Puddle;
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The track shader's wet road, line for line (Track.shader, _TRACK_WET), with tarmac detail on: at a across the road
    /// from its left edge and s along it, under a film of water mm deep, the rain this hard and this much of the film
    /// swept off the line. The dips are read at the road's own position, as across and along stand in for world x and z.
    /// </summary>
    static Wet WetAt(Color[] map, Color[] pools, Color[] road, Color[] macro, int ts, int ms, float lap, float a, float s, float waterMm, float rain, float lineDry)
    {
        Color grain = Sample(road, ts, ts, a / 4f, s / 4f);
        float gloss = 0.25f * (0.7f + 0.6f * grain.a);
        Color onLine = Sample1D(map, s / lap);
        float d = a - onLine.r;
        float band = Bell(d, 1.1f) * 0.5f + (Bell(d - 0.8f, 0.24f) + Bell(d + 0.8f, 0.24f)) * 0.5f;
        float dips = Sample(macro, ms, ms, a / 13f, s / 13f).b;
        float onRoad = Mathf.Clamp01(onLine.g * 3f);
        float water = waterMm * (1f - Mathf.Clamp01(band * 1.25f) * onRoad * lineDry);
        float held = water * (0.75f + 0.5f * dips) * (1.15f + (0.85f - 1.15f) * grain.a);
        float damp = Mathf.Clamp01(held / 0.06f);
        float film = SmoothStep(0.04f, 0.35f, held);
        float puddle = 0f;
        if (water > 0.12f)
        {
            Color p = Sample1D(pools, s / lap);
            float right = Math.Max(p.b - a, 0f), left = Math.Max(a, 0f);
            float edge = (float)Math.Exp(-right / 1.3f) * Mathf.Clamp01(0.5f + p.g) + (float)Math.Exp(-left / 1.3f) * Mathf.Clamp01(0.5f - p.g);
            float readyHere = Mathf.Clamp01(Math.Max(p.r, edge * 0.9f) * (dips * 1.7f - 0.25f));
            float stands = 2.2f + (0.2f - 2.2f) * readyHere;
            puddle = SmoothStep(0f, 0.3f, water - stands);
        }
        float darken = (1f - 0.55f * damp) * (1f - 0.2f * puddle);
        float smooth = gloss + (0.88f - gloss) * film;
        smooth += (0.96f - smooth) * puddle;
        return new Wet { Water = water, Darken = darken, Smoothness = smooth, Puddle = puddle };
    }

    /// <summary>
    /// The track shader's dry road, line for line (Track.shader with _TRACK_DETAIL and _TRACK_RUBBER), lit by a low sun:
    /// at a across the road from its left edge and s along it. Across and along stand in for world x and z, and the
    /// repairs tile is read without its per-repeat shift, as the preview never reaches past one repeat's worth of road.
    /// </summary>
    static Color DryAt(Color[] map, Color[] road, Color[] normals, Color[] macro, Color[] repairs, Color[] marks, float lap, float a, float s)
    {
        int ts = TrackTextures.TarmacSize, ms = TrackTextures.MacroSize;
        Color grain = Sample(road, ts, ts, a / 4f, s / 4f);
        Color nrm = Sample(normals, ts, ts, a / 4f, s / 4f);
        float k = 1f;
        float broad = Sample(macro, ms, ms, a / 60f, s / 60f).r;
        float fine = Sample(macro, ms, ms, s / 60f * 3.7f + 0.31f, -a / 60f * 3.7f + 0.31f).r;
        float patch = (broad * 0.65f + fine * 0.35f) * 2f - 1f;
        k *= 1f + patch * 0.35f;
        Color rep = Sample(repairs, TrackTextures.RepairsAcross, TrackTextures.RepairsAlong, a / TrackTextures.RepairsMetresAcross, s / TrackTextures.RepairsMetresAlong);
        k *= 1f + (0.76f + (1.1f - 0.76f) * rep.b - 1f) * rep.g;
        Color onLine = Sample1D(map, s / lap);
        float d = a - onLine.r;
        float band = Bell(d, 1.1f) * 0.5f + (Bell(d - 0.8f, 0.24f) + Bell(d + 0.8f, 0.24f)) * 0.5f;
        float streak = Sample(macro, ms, ms, a * 0.45f, s * 0.012f).g;
        float rubber = Mathf.Clamp01(band * onLine.g * (0.5f + streak * 1.1f));
        float u = d / 8f + 0.5f;
        float mk = u <= 0f || u >= 1f ? 0f : Sample(marks, TrackTextures.MarksAcross, TrackTextures.MarksAlong, u, s / 160f).r * Mathf.Clamp01(onLine.g * 1.6f - 0.45f);
        k *= (1f - rubber * 0.7f) * (1f - mk * 0.7f);
        var c = new Color(grain.r * k, grain.g * k, grain.b * k);
        float seal = rep.r * 0.75f;
        c = Color.Lerp(c, new Color(0.03f, 0.03f, 0.033f), seal);
        // Sealant lies flat over the grain.
        nrm = Color.Lerp(nrm, new Color(0.5f, 0.5f, 1f), rep.r * 0.8f);
        return Lit(c, nrm);
    }

    static float Bell(float d, float w) => (float)Math.Exp(-d * d / (2 * w * w));

    static float Repeat(float t, float l) => t - (float)Math.Floor(t / l) * l;

    static float MaxHalf(Circuit c)
    {
        float m = 0f;
        for (int k = 0; k < c.Count; k++) m = Math.Max(m, c.HalfWidth(k));
        return m;
    }

    static int SampleAt(float[] along, float s)
    {
        int lo = 0, hi = along.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (along[mid] <= s) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    static int Nearest(Circuit c, Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int k = 0; k < c.Count; k++)
        {
            float dx = c.Centre[k].x - p.x, dz = c.Centre[k].z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    static float Diff(Color a, Color b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);

    /// <summary>Bilinear, repeating, as the GPU samples the line map: texel j at the middle of its length.</summary>
    static Color Sample1D(Color[] px, float t)
    {
        float f = Repeat(t, 1f) * px.Length - 0.5f;
        int a = (int)Math.Floor(f);
        float w = f - a;
        Color x = px[((a % px.Length) + px.Length) % px.Length], y = px[(((a + 1) % px.Length) + px.Length) % px.Length];
        return Color.Lerp(x, y, w);
    }

    static Color Sample(Color[] px, int w, int h, float u, float v)
    {
        int x = (int)Math.Floor(Repeat(u, 1f) * w) % w, y = (int)Math.Floor(Repeat(v, 1f) * h) % h;
        return px[y * w + x];
    }

    /// <summary>A texel lit by a low sun from the upper left, with a little shine on its glossy parts.</summary>
    static Color Lit(Color albedo, Color normal)
    {
        float nx = normal.r * 2 - 1, ny = normal.g * 2 - 1, nz = normal.b * 2 - 1;
        float lx = -0.75f, ly = 0.45f, lz = 0.48f;
        float l = (float)Math.Sqrt(lx * lx + ly * ly + lz * lz);
        float diffuse = Math.Max(0f, (nx * lx + ny * ly + nz * lz) / l);
        float shade = 0.3f + 1.6f * diffuse;
        return new Color(Math.Min(1f, albedo.r * shade), Math.Min(1f, albedo.g * shade), Math.Min(1f, albedo.b * shade));
    }

    /// <summary>An 8-bit RGB PNG, the pixel function given x right and y up.</summary>
    static void WritePng(string path, int w, int h, Func<int, int, Color> pixel)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int row = 0; row < h; row++)
        {
            int o = row * (w * 3 + 1);
            raw[o] = 0;
            for (int x = 0; x < w; x++)
            {
                Color c = pixel(x, h - 1 - row);
                raw[o + 1 + x * 3] = ToByte(c.r);
                raw[o + 2 + x * 3] = ToByte(c.g);
                raw[o + 3 + x * 3] = ToByte(c.b);
            }
        }
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BigEndian(header, 0, w);
        BigEndian(header, 4, h);
        header[8] = 8; header[9] = 2;
        Chunk(file, "IHDR", header);
        using (var packed = new MemoryStream())
        {
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true)) z.Write(raw);
            Chunk(file, "IDAT", packed.ToArray());
        }
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    static void BigEndian(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, data.Length);
        s.Write(len);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc(t, 0xffffffff);
        crc = Crc(data, crc) ^ 0xffffffff;
        var c = new byte[4];
        BigEndian(c, 0, (int)crc);
        s.Write(c);
    }

    static uint Crc(byte[] data, uint crc)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }
}
