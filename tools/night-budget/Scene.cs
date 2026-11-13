// Writes the scene for the night renders (render/): the built meshes round the grid with their materials, every
// floodlight as the game lights it, the full grid's places, the night sky and every number of the night's budget.
// scene.json beside scene.bin, the meshes packed little-endian: float32 positions, int8 normals, uint32 indices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Rendering;
using UnityEngine;

static class Scene
{
    /// <summary>How far round the grid's middle the meshes are written, m.</summary>
    const float Radius = 420f;

    /// <summary>Each part's colour (sRGB, as the game's materials set it), smoothness and metallic.</summary>
    static readonly Dictionary<string, (float r, float g, float b, float smooth, float metal)> Looks = new Dictionary<string, (float, float, float, float, float)>
    {
        ["Road"] = (0.17f, 0.17f, 0.18f, 0.25f, 0f),
        ["Pit lane"] = (0.21f, 0.21f, 0.22f, 0.2f, 0f),
        ["Paddock"] = (0.21f, 0.21f, 0.22f, 0.2f, 0f),
        ["Road edges"] = (0.17f, 0.17f, 0.18f, 0.25f, 0f),
        ["Edge lines"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Pit lane lines"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Pit exit line"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Paddock lines"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Start line and safety car lines"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Grid slots"] = (0.92f, 0.92f, 0.9f, 0.35f, 0f),
        ["Pit box lines"] = (0.95f, 0.78f, 0.1f, 0.35f, 0f),
        ["Grid yellow lines"] = (0.95f, 0.78f, 0.1f, 0.35f, 0f),
        ["Pit entry hatching"] = (0.86f, 0.5f, 0.48f, 0.35f, 0f),
        ["Kerbs"] = (0.8f, 0.45f, 0.42f, 0.4f, 0f),
        ["Verges"] = (0.24f, 0.36f, 0.16f, 0.05f, 0f),
        ["Grass runoff"] = (0.23f, 0.41f, 0.15f, 0.05f, 0f),
        ["Ground"] = (0.2f, 0.35f, 0.13f, 0.05f, 0f),
        ["Gravel traps"] = (0.63f, 0.58f, 0.48f, 0.1f, 0f),
        ["Armco"] = (0.72f, 0.74f, 0.76f, 0.6f, 0.7f),
        ["Tyre walls"] = (0.07f, 0.07f, 0.075f, 0.2f, 0f),
        ["Pit wall"] = (0.74f, 0.73f, 0.7f, 0.15f, 0f),
        ["Pit lane shoulders"] = (0.74f, 0.73f, 0.7f, 0.15f, 0f),
        ["Garages"] = (0.32f, 0.33f, 0.35f, 0.2f, 0f),
        ["Garage roofs"] = (0.55f, 0.56f, 0.57f, 0.3f, 0.3f),
        ["Stand concrete"] = (0.68f, 0.67f, 0.64f, 0.1f, 0f),
        ["Stand steel"] = (0.6f, 0.62f, 0.63f, 0.5f, 0.6f),
        ["Stand cladding"] = (0.3f, 0.38f, 0.46f, 0.3f, 0f),
        ["Stand seats"] = (0.2f, 0.31f, 0.5f, 0.3f, 0f),
        ["Stand roofs"] = (0.36f, 0.37f, 0.38f, 0.25f, 0f),
        ["Crowd"] = (0.45f, 0.4f, 0.38f, 0.1f, 0f),
        ["Catch fence posts"] = (0.6f, 0.62f, 0.63f, 0.5f, 0.6f),
        ["Pit building"] = (0.66f, 0.65f, 0.62f, 0.12f, 0f),
        ["Pit building glass"] = (0.07f, 0.09f, 0.11f, 0.92f, 0.2f),
        ["Pit building steel"] = (0.16f, 0.3f, 0.22f, 0.45f, 0.3f),
        ["Start gantry"] = (0.6f, 0.62f, 0.63f, 0.5f, 0.6f),
        ["Floodlight steel"] = (0.55f, 0.57f, 0.58f, 0.4f, 0.5f),
        ["Floodlight lenses"] = (0.55f, 0.57f, 0.56f, 0.9f, 0f),
    };

    /// <summary>The parts drawn with the track's own shader, which adds the full Fresnel sheen (Lamps.hlsl's FresnelCorrection).</summary>
    static readonly HashSet<string> Track = new HashSet<string> { "Road", "Road edges", "Pit lane", "Paddock", "Kerbs" };

    static float[] Xyz(Vector3 v) => new[] { v.x, v.y, v.z };
    static float[] Rgb(Color c) => new[] { c.r, c.g, c.b };

    public static void Write(string dir, Circuit c, Grandstands stands, FloodlightLayout floods, List<CircuitPart> parts)
    {
        Directory.CreateDirectory(dir);
        // The grid: the painted slots, front wheels just behind each line, as CircuitScene places the cars.
        var grid = new List<(Vector3 at, Vector3 forward)>();
        GridSlot[] slots = c.Data.grid.slots;
        Vector3 middle = Vector3.zero;
        for (int i = 0; i < slots.Length; i++)
        {
            int k = c.IndexOf(slots[i].d - 2.6f);
            Vector3 ground = c.Surface(k, slots[i].lateral) + Vector3.up * 0.05f;
            Vector3 f = c.Forward[k];
            f = new Vector3(f.x, 0f, f.z).normalized;
            grid.Add((ground, f));
            middle += ground;
        }
        middle /= slots.Length;
        Vector3 focus = middle;

        var bin = new MemoryStream();
        var w = new BinaryWriter(bin);
        var meshes = new List<object>();
        foreach (IGrouping<string, CircuitPart> g in parts.GroupBy(p => p.Name))
        {
            if (!Looks.TryGetValue(g.Key, out var look)) continue;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var idx = new List<int>();
            foreach (CircuitPart p in g)
            {
                IReadOnlyList<Vector3> v = p.Kit.Vertices, n = p.Kit.Normals;
                IReadOnlyList<int> t = p.Kit.Triangles;
                var map = new Dictionary<int, int>();
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 mid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    float dx = mid.x - focus.x, dz = mid.z - focus.z;
                    if (dx * dx + dz * dz > Radius * Radius) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int a = t[i + j];
                        if (!map.TryGetValue(a, out int b))
                        {
                            b = pos.Count;
                            map[a] = b;
                            pos.Add(v[a] - focus);
                            nrm.Add(n[a]);
                        }
                        idx.Add(b);
                    }
                }
            }
            if (idx.Count == 0) continue;
            long at = bin.Position;
            foreach (Vector3 p in pos) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
            long nAt = bin.Position;
            foreach (Vector3 p in nrm) { w.Write((sbyte)Math.Round(p.x * 127)); w.Write((sbyte)Math.Round(p.y * 127)); w.Write((sbyte)Math.Round(p.z * 127)); w.Write((sbyte)0); }
            long iAt = bin.Position;
            foreach (int i in idx) w.Write(i);
            meshes.Add(new
            {
                name = g.Key, vertices = pos.Count, indices = idx.Count, pos = at, nrm = nAt, idx = iAt,
                colour = new[] { look.r, look.g, look.b }, smooth = look.smooth, metal = look.metal,
                lens = g.Key == "Floodlight lenses", fresnel = Track.Contains(g.Key),
            });
        }

        var lights = floods.Sources.Select(s => new
        {
            kind = s.Kind.ToString(), position = Xyz(s.Position - focus), target = Xyz(s.Target - focus), aim = Xyz(s.Aim), cone = s.Cone, core = s.Core,
            range = s.Range, intensity = s.Intensity / NightBudget.Luma(NightBudget.Flood),
        }).ToList();

        // Where the main stand's seats are near the grid, for the view from the stands.
        StandSite main = Grandstands.Sites.First(s => s.Main);
        var seats = parts.Where(p => p.Name == "Stand seats").SelectMany(p => p.Kit.Vertices)
            .Where(v => (new Vector3(v.x - focus.x, 0f, v.z - focus.z)).magnitude < 120f).ToList();
        Vector3 stand = seats.Count > 0 ? seats.Aggregate(Vector3.zero, (a, b) => a + b) / seats.Count - focus : Vector3.zero;

        // The night as the race shows it on a clear night: the sky, the ambient light and the moon, matched for Linear.
        SkyState sky = SkyClock.At(23f, SkyClock.Look);
        WeatherState weather = WeatherState.Dry;
        WeatherSky.Apply(ref sky, weather, WeatherSky.Lights(sky.SunElevation, WeatherSky.Dusk(weather)));
        float seen = SkyClock.SkySeen(sky, SkyClock.Look);
        var night = new
        {
            sunElevation = sky.SunElevation, lights = sky.Lights,
            lightDir = Xyz(sky.LightDir), lightColour = Rgb(sky.LightColour),
            lightIntensity = LinearLight.Match(sky.LightColour, sky.LightIntensity, sky.AmbientSky, Mathf.Max(sky.LightDir.y, 0.3f)),
            ambientSky = Rgb(LinearLight.Ambient(sky.AmbientSky)), ambientEquator = Rgb(LinearLight.Ambient(sky.AmbientEquator)),
            ambientGround = Rgb(LinearLight.Ambient(sky.AmbientGround)),
            zenith = Rgb(sky.Zenith * seen), horizon = Rgb(sky.Horizon * seen), fog = Rgb(sky.Fog * seen), fogDensity = SkyClock.Density(sky.Haze),
        };

        var budget = new
        {
            NightBudget.Exposure, NightBudget.Floodlit, NightBudget.LampMost, NightBudget.AllMost, NightBudget.PixelMost,
            headlampIntensity = NightBudget.HeadlampIntensity / NightBudget.Luma(NightBudget.Headlamp),
            NightBudget.HeadlampRange, NightBudget.HeadlampCone, NightBudget.HeadlampCore, NightBudget.HeadlampAhead, NightBudget.HeadlampDip,
            flood = Rgb(NightBudget.Flood), headlamp = Rgb(NightBudget.Headlamp), red = Rgb(NightBudget.Red),
            NightBudget.FloodLens, NightBudget.HeadlampGlow, NightBudget.TailGlow, NightBudget.BrakeGlow, NightBudget.StandGlow,
            on = NightBudget.On(sky.Lights), floodLevel = NightBudget.On(sky.Lights) * NightBudget.FloodDark(sky.SunElevation),
            headlampLevel = NightBudget.On(sky.Lights) * NightBudget.HeadlampDark(sky.SunElevation),
        };

        var info = new { meshes, lights, grid = grid.Select(g => new { at = Xyz(g.at - focus), forward = Xyz(g.forward) }), stand = Xyz(stand), night, budget };
        File.WriteAllBytes(Path.Combine(dir, "scene.bin"), bin.ToArray());
        File.WriteAllText(Path.Combine(dir, "scene.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { IncludeFields = true }));
        Console.WriteLine($"      wrote {meshes.Count} meshes, {bin.Length / 1024 / 1024} MB, {lights.Count} floodlights and {grid.Count} grid places to {dir}");
    }
}
