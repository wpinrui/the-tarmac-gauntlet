// Writes the night preview (preview.html beside its data.js): the built meshes round the start line, the floodlights there, the track's centre
// line for the preview's cars, the sky at night from SkyClock and every look's colours from NightLook.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Preview
{
    const float Radius = 320f;

    static short Cm(float v) => (short)Math.Clamp(Math.Round(v * 100), short.MinValue, short.MaxValue);
    static float[] Rgb(Color c) => new[] { c.r, c.g, c.b };
    static float[] Xyz(Vector3 v) => new[] { v.x, v.y, v.z };

    static readonly HashSet<string> WithUv = new HashSet<string> { "Pit building glass", "Crowd", "Floodlight pools" };

    public static void Write(string dir, Circuit c, PitComplex pc, Floodlights floods, List<CircuitPart> track, List<CircuitPart> stands,
        List<CircuitPart> pit, List<CircuitPart> lit)
    {
        Directory.CreateDirectory(dir);
        Vector3 focus = c.Centre[0];
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
            long at = bin.Position;
            foreach (Vector3 p in pos) { w.Write(Cm(p.x)); w.Write(Cm(p.y)); w.Write(Cm(p.z)); }
            long nAt = bin.Position;
            foreach (Vector3 p in nrm) { w.Write((sbyte)Math.Round(p.x * 127)); w.Write((sbyte)Math.Round(p.y * 127)); w.Write((sbyte)Math.Round(p.z * 127)); }
            while (bin.Position % 4 != 0) w.Write((byte)0);
            long uvAt = -1;
            if (WithUv.Contains(part))
            {
                uvAt = bin.Position;
                foreach (Vector2 p in uv) { w.Write(p.x); w.Write(p.y); }
            }
            meshes.Add(new { group, part, n = pos.Count, pos = at, nrm = nAt, uv = uvAt });
        }

        foreach (string part in track.Select(p => p.Name).Distinct()) Add("track", part, track);
        foreach (string part in stands.Select(p => p.Name).Distinct()) Add("stand", part, stands);
        foreach (string part in pit.Select(p => p.Name).Distinct()) Add("pit", part, pit);
        foreach (string part in lit.Select(p => p.Name).Distinct()) Add("flood", part, lit);

        var lights = floods.Lights
            .Where(f => Flat(f.Target - focus).magnitude < Radius + 40f)
            .Select(f => new { kind = f.Kind.ToString(), p = Xyz(f.Position - focus), aim = Xyz(f.Aim), target = Xyz(f.Target - focus), reach = f.Reach, cone = f.Cone })
            .ToList();

        // The centre line from behind the grid to past turn 1's approach, for the preview's cars.
        var path = new List<float[]>();
        for (float d = -460f; d <= 320f; d += c.Step)
        {
            int k = c.IndexOf(Mathf.Repeat(d, c.Lap));
            Vector3 p = c.Centre[k] - focus, l = c.Left[k], f = c.Forward[k];
            path.Add(new[] { p.x, p.y, p.z, l.x, l.z, f.x, f.y, f.z, c.HalfWidth(k) });
        }

        object Sky(float hour)
        {
            SkyState s = SkyClock.At(hour, SkyClock.Look);
            return new
            {
                hour, night = s.Night, lightDir = Xyz(s.LightDir), light = Rgb(s.LightColour), intensity = s.LightIntensity,
                zenith = Rgb(s.Zenith), horizon = Rgb(s.Horizon), ambientSky = Rgb(s.AmbientSky), ambientEquator = Rgb(s.AmbientEquator),
                ambientGround = Rgb(s.AmbientGround), fog = Rgb(s.Fog),
            };
        }

        var info = new
        {
            meshes,
            lights,
            path,
            skies = new[] { Sky(22.75f), Sky(1.5f) },
            haze = SkyClock.Look.Haze,
            flood = Rgb(NightLook.Flood),
            headlamps = Rgb(NightLook.Headlamps),
            red = Rgb(NightLook.Red),
            look = new
            {
                NightLook.PoolBrightening, NightLook.PoolLift, NightLook.BeamBrightening, NightLook.BeamLift, NightLook.LensGlow,
                NightLook.HeadlampGlow, NightLook.TailGlow, NightLook.BrakeGlow, NightLook.StandGlow,
            },
            crowd = new[] { CrowdPalette.Shirts, CrowdPalette.Skins, CrowdPalette.Trousers }.Select(row => row.Select(Rgb).ToArray()).ToArray(),
            crowdSize = new[] { CrowdPalette.Width, CrowdPalette.Height },
            windowCells = PitComplexBuilder.WindowCells,
            storeyCells = PitComplexBuilder.StoreyCells,
            garageOut = Xyz(pc.Frame(pc.StartGarage).Out),
        };
        // One script the preview page loads: the json as it is, the meshes gzipped and in base64.
        var zipped = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(bin.ToArray());
        string json = JsonSerializer.Serialize(info, new JsonSerializerOptions { IncludeFields = true });
        File.WriteAllText(Path.Combine(dir, "data.js"), $"window.NIGHT = {json};\nwindow.NIGHT_MESHES = \"{Convert.ToBase64String(zipped.ToArray())}\";\n");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "preview.html"), Path.Combine(dir, "preview.html"), true);
        Console.WriteLine($"wrote {meshes.Count} meshes, {bin.Length / 1024} KB, {lights.Count} lamps to {dir}");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
