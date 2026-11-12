// Makes the track surface's textures and the racing line map exactly as the game does (TrackTextures, CircuitBuilder,
// RacingLine from circuit.json), checks them, and writes previews. Each check prints one line: ok, or FAIL with
// details. Exit code 1 if any check fails.
//
// Checks:
//   textures repeat       every texture's opposite edges match as well as its neighbouring pixels do, so no seam
//                         shows where the tile repeats (a kerb's only along it, as its blocks change across)
//   line map on the road  the road's uv along it, as the builder lays it, matches CircuitBuilder.RoadAlong within
//                         2 cm, so the shader finds the racing line at the right place on the road
//   line on the road      the racing line runs at least 0.9 m inside both road edges, so its tyre tracks stay on it
//   rubber in the corners every corner has more rubber on its line than the longest straight
//
// Previews, in the out folder given: tarmac.png (colour, lit by a low sun), patches.png, kerbs.png (clean and worn,
// lit) and rubber.png (the road through the tightest corner, straightened, as the shader draws its rubber).
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
        Color[] road = TrackTextures.TarmacColour(tarmac, new Color(0.17f, 0.17f, 0.18f), 0.9f);
        Color[] roadNormals = TrackTextures.TarmacNormals(tarmac, 1.6f);
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
        Report(seams.Count == 0, "textures repeat", seams.Count == 0 ? "4 textures" : string.Join(", ", seams));

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

        // The road through the sharpest corner, straightened: across left to right, along bottom to top.
        const float pxPerM = 24f, alongScale = 6f;
        float from = along[sharpest] - 140f, to = along[sharpest] + 60f;
        int width = (int)(2f * MaxHalf(circuit) * pxPerM) + 40, height = (int)((to - from) * alongScale);
        WritePng(Path.Combine(outDir, "rubber.png"), width, height, (x, y) =>
        {
            float s = Repeat(from + y / alongScale, along[n]);
            float a = (x - 20) / pxPerM;
            int k = SampleAt(along, s);
            float w = 2f * circuit.HalfWidth(k);
            if (a < 0f || a > w) return new Color(0.23f, 0.41f, 0.15f);
            if (a < 0.12f || a > w - 0.12f) return new Color(0.92f, 0.92f, 0.9f);
            float u = a / 4f, v = s / 4f;
            Color grain = Sample(road, ts, ts, u, v);
            Color onLine = Sample1D(map, s / along[n]);
            float d = a - onLine.r;
            float band = Bell(d, 1.1f) * 0.5f + (Bell(d - 0.8f, 0.24f) + Bell(d + 0.8f, 0.24f)) * 0.5f;
            float streak = Sample(macro, ms, ms, a * 0.45f, s * 0.012f).g;
            float rubber = Mathf.Clamp01(band * onLine.g * (0.45f + streak * 1.1f));
            float k2 = 1f - rubber * 0.55f;
            return new Color(grain.r * k2, grain.g * k2, grain.b * k2);
        });
        Console.WriteLine($"previews in {outDir}");
        return failed ? 1 : 0;
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
