// The light budget: every light the circuit adds at night, swept over everywhere it can fall, with the same functions
// the shaders use (Rendering/Shaders/Headlamps.hlsl and NightLit.hlsl, the mist in Atmosphere/Volumetrics.shader),
// their constants read from those files. Each rule prints one line, ok or FAIL, with the worst case it found.
//
// Rules:
//   headlamps bounded   1 to 24 cars on one spot, every distance from 0.1 to 100 m ahead, every angle across and up and
//                       down the beam, every way a surface can face (a wall at the nose too): what a white surface gives
//                       back never exceeds NightLook.NightMost
//   floodlights bounded every cell of the floodlights' map, every height up to 30 m, every way a surface can face, with all
//                       24 cars' headlamps on top: never over NightLook.NightMost
//   below the bloom     NightLook.NightMost is under the bloom's threshold, so the lamps' light alone never blooms
//   mist bounded        the beams in the mist, from cameras at and near every lens and down the straight, looking every
//                       way, in the thickest mist of any hour and weather: never over NightLook.BeamMost
//   cars under floods   the floodlights' real lights on a white car right under each lamp: never over NightLook.NightMost
//   every hour          every hour, clear, overcast and in rain, floodlights on and off: the most the lamps add after the
//                       exposure, and the floodlights at no more than a tenth of full at 17:47 under an overcast sky
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Rendering;
using UnityEngine;

static class Budget
{
    static Dictionary<string, float> defs;
    static float D(string name) => defs[name];

    public static void Run(string root, Circuit c, Floodlights floods, FloodField field)
    {
        defs = new Dictionary<string, float>();
        foreach (string f in new[] { "Headlamps.hlsl", "NightLit.hlsl" })
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(root, "unity/Assets/Rendering/Shaders", f)), @"#define\s+(TG_\w+)\s+([-0-9.]+)\s"))
                defs[m.Groups[1].Value] = float.Parse(m.Groups[2].Value);
        float bloomThreshold = Num(root, "unity/Assets/Rendering/Scripts/PostEffects.cs", @"BloomThreshold = ([0-9.]+)f");
        float toneEv = Num(root, "unity/Assets/Rendering/Scripts/PostEffects.cs", @"ToneExposure = ([0-9.]+)f");
        float nightEv = Num(root, "unity/Assets/Raceday/Scripts/SkyGrade.cs", @"Exposure = ([0-9.]+)f \* night");
        float most = NightLook.NightMost;

        Vector3 head = Lin(NightLook.Headlamps) * NightLook.HeadlampGround;
        Vector3 flood = Lin(NightLook.Flood) * NightLook.FloodGround;
        Vector3[] normals = Normals();

        // Headlamps: up to 24 cars on one spot, the worst overlap there is.
        float headGiven = 0f, headLit = 0f;
        string headWhere = "";
        foreach (int cars in new[] { 1, 3, 24 })
            foreach (float h in new[] { 0.45f, 0.7f, 1.0f })
            {
                var lamps = Enumerable.Range(0, cars).Select(_ => (at: new Vector3(0f, h, 0f), h, fwd: new Vector3(0f, 0f, 1f), share: 1f)).ToArray();
                for (float z = 0.1f; z <= 100f; z *= 1.1f)
                    for (float yaw = -60f; yaw <= 60f; yaw += 5f)
                        foreach (float y in new[] { -h, -h + 0.1f, -h + 0.4f, 0f, 0.3f, 1f, 3f })
                        {
                            float a = yaw * Mathf.Deg2Rad;
                            var p = new Vector3(Mathf.Sin(a) * z, h + y, Mathf.Cos(a) * z);
                            foreach (Vector3 n in normals)
                            {
                                Vector3 e = Scale(head, HeadlampLight(lamps, p, n));
                                float given = Max(e), lit = Max(Knee(e, most));
                                if (given > headGiven) { headGiven = given; headWhere = $"{cars} cars, {z:0.0} m ahead, {yaw:0} degrees, {y + h:0.0} m up, facing {n.x:0.0},{n.y:0.0},{n.z:0.0}"; }
                                headLit = Math.Max(headLit, lit);
                            }
                        }
            }
        Report("headlamps bounded", headLit <= most + 1e-5f ? null : $"{headLit:0.000} over {most}",
            $"a white surface gives back at most {headLit:0.000} of {most} (it is given {headGiven:0.000} at worst: {headWhere})");

        // Floodlights: every cell, every height, every facing, with 24 cars' worth of headlamps on top.
        float floodGiven = 0f, floodLit = 0f, worstBoth = 0f;
        float headPeak = headGiven;
        for (int j = 0; j < field.Height; j++)
            for (int i = 0; i < field.Width; i++)
            {
                int k = j * field.Width + i;
                if (field.Light[k] <= 0f) continue;
                foreach (float up in new[] { 0f, 1f, 5f, 12f, 30f })
                {
                    var p = new Vector3(field.X0 + i * FloodField.Texel, field.Ground[k] + up, field.Z0 + j * FloodField.Texel);
                    foreach (Vector3 n in normals)
                    {
                        Vector3 e = Scale(flood, FloodLight(field, p, n));
                        floodGiven = Math.Max(floodGiven, Max(e));
                        floodLit = Math.Max(floodLit, Max(Knee(e, most)));
                        worstBoth = Math.Max(worstBoth, Max(Knee(e + new Vector3(1f, 1f, 1f) * headPeak, most)));
                    }
                }
            }
        Report("floodlights bounded", Math.Max(floodLit, worstBoth) <= most + 1e-5f ? null : $"{Math.Max(floodLit, worstBoth):0.000} over {most}",
            $"a white surface gives back at most {floodLit:0.000} of {most} (given {floodGiven:0.000}), {worstBoth:0.000} with the worst headlamps on top");
        Report("below the bloom", most < bloomThreshold ? null : $"{most} is not under the bloom's threshold {bloomThreshold}",
            $"the lamps give back at most {most}, under the bloom's threshold {bloomThreshold}");

        // The mist, in the thickest mist the sky ever has, the floodlights full on.
        float rho = Num(root, "unity/Assets/Raceday/Scripts/TimeOfDay.cs", @"MistClear = ([0-9.]+)f")
            + Num(root, "unity/Assets/Raceday/Scripts/TimeOfDay.cs", @"MistMorning = ([0-9.]+)f")
            + Num(root, "unity/Assets/Raceday/Scripts/TimeOfDay.cs", @"MistRain = ([0-9.]+)f")
            + Num(root, "unity/Assets/Raceday/Scripts/TimeOfDay.cs", @"MistNight = ([0-9.]+)f");
        float strength = Num(root, "unity/Assets/Raceday/Scripts/NightLights.cs", @"BeamStrength = ([0-9.]+)f")
            * LinearLight.Linear(0.2f) / 0.2f;
        float carry = Num(root, "unity/Assets/Raceday/Scripts/NightLights.cs", @"BeamCarry = ([0-9.]+)f");
        Vector3 beamLight = Lin(NightLook.Flood) * strength;
        float mistRaw = 0f, mistLit = 0f;
        string mistWhere = "";
        var eyes = new List<Vector3>();
        foreach (Floodlight f in floods.Lights)
            foreach (Vector3 n in normals)
                foreach (float r in new[] { 0.3f, 3f, 12f })
                    eyes.Add(f.Position + n * r);
        for (int i = 0; i < c.Count; i += 10) eyes.Add(c.Centre[i] + Vector3.up * 1.2f);
        Vector3[] looks = Normals(12);
        var rng = new System.Random(7);
        for (int e = 0; e < eyes.Count; e++)
        {
            Vector3 eye = eyes[e];
            Floodlight[] near = floods.Lights.OrderBy(f => (f.Position - eye).sqrMagnitude).Take((int)Num(root, "unity/Assets/Rendering/Scripts/Atmosphere.cs", @"MostBeams = ([0-9]+)"))
                .Where(f => (f.Position - eye).sqrMagnitude < 400f * 400f).ToArray();
            if (near.Length == 0) continue;
            // Every look from the near and far points, a random handful from the rest: the near ones see the most.
            IEnumerable<Vector3> dirs = e % 7 == 0 || eyes.Count - e <= c.Count / 10 ? looks : looks.Where(_ => rng.NextDouble() < 0.15);
            foreach (Vector3 d in dirs)
            {
                Vector3 g = beamLight * (BeamMist(near, carry, eye, d, 700f, rho, 0.5f) / (4f * Mathf.PI));
                float raw = Max(g), lit = Max(Knee(g, NightLook.BeamMost));
                if (raw > mistRaw) { mistRaw = raw; mistWhere = $"{(near[0].Position - eye).magnitude:0.0} m from a lens"; }
                mistLit = Math.Max(mistLit, lit);
            }
        }
        Report("mist bounded", mistLit <= NightLook.BeamMost + 1e-5f ? null : $"{mistLit:0.000} over {NightLook.BeamMost}",
            $"at most {mistLit:0.000} of {NightLook.BeamMost} ({mistRaw:0.00} before the cap, {mistWhere}), from {eyes.Count} cameras");

        // The floodlights' real lights on a white car right under each lamp, its roof 1.4 m up.
        float floodIntensity = Num(root, "unity/Assets/Raceday/Scripts/NightLights.cs", @"FloodIntensity = ([0-9.]+)f");
        float carWorst = 0f;
        string carWhere = "";
        foreach (Floodlight f in floods.Lights)
        {
            float range = f.Reach * 1.6f + 10f;
            float intensity = LightFalloff.UrpLinear(NightLook.Flood, floodIntensity, range, f.Reach);
            int k = Nearest(c.Centre, f.Position);
            Vector3 roof = c.Centre[k];
            float lat = Mathf.Clamp(Vector3.Dot(f.Position - roof, c.Left[k]), -c.HalfWidth(k), c.HalfWidth(k));
            roof += c.Left[k] * lat + Vector3.up * 1.4f;
            foreach (Vector3 p in new[] { roof, f.Target + Vector3.up * 1.4f })
            {
                Vector3 v = f.Position - p;
                float d = v.magnitude;
                float cone = Vector3.Dot(-v / d, f.Aim) >= Mathf.Cos(f.Cone * 0.5f * Mathf.Deg2Rad) ? 1f : 0f;
                float given = Max(Lin(NightLook.Flood)) * intensity * LightFalloff.At(d, range) * cone;
                if (given > carWorst) { carWorst = given; carWhere = $"{f.Kind} lamp {d:0.0} m off"; }
            }
        }
        Report("cars under floods", carWorst <= most ? null : $"{carWorst:0.000} over {most} ({carWhere})",
            $"a white car is given at most {carWorst:0.000} ({carWhere})");

        // Every hour, in each weather, with the floodlights on and off.
        float worstSeen = 0f, dusk = 0f;
        string seenWhere = "";
        foreach ((string name, float duskDegrees) in new[] { ("clear", 0f), ("overcast", WeatherSky.CloudDusk), ("rain", WeatherSky.CloudDusk + WeatherSky.RainDusk) })
            foreach (bool floodsOn in new[] { true, false })
                for (float hour = 0f; hour < 24f; hour += 0.05f)
                {
                    SkyState s = SkyClock.At(hour, SkyClock.Look);
                    float on = NightLook.On(WeatherSky.Lights(s.SunElevation, duskDegrees));
                    float floodsShare = floodsOn ? on * NightLook.FloodDark(s.SunElevation) : 0f;
                    float headsShare = on * NightLook.HeadlampDark(s.SunElevation);
                    float given = floodGiven * floodsShare + headGiven * headsShare;
                    float seen = Max(Knee(new Vector3(1f, 1f, 1f) * given, most)) * Mathf.Pow(2f, toneEv + nightEv * s.Night);
                    if (seen > worstSeen) { worstSeen = seen; seenWhere = $"{Clock(hour)} {name}"; }
                    if (floodsOn && name == "overcast" && Math.Abs(hour - (17f + 47f / 60f)) < 0.026f) dusk = floodsShare;
                }
        foreach (float hour in new[] { 17f + 41f / 60f, 17f + 47f / 60f, 18f, 18.5f, 19.1f, 20.7f, 21f })
        {
            SkyState s = SkyClock.At(hour, SkyClock.Look);
            Console.WriteLine($"      {Clock(hour)}: sun {s.SunElevation:0.0} degrees, sky {s.Night:0.00} dark, floodlights {NightLook.FloodDark(s.SunElevation):0.00}, headlamps {NightLook.HeadlampDark(s.SunElevation):0.00}");
        }
        Report("every hour", dusk <= 0.1f ? null : $"the floodlights at {dusk:0.00} of full at 17:47 overcast",
            $"the lamps add at most {worstSeen:0.00} after the exposure ({seenWhere}); at 17:47 overcast the floodlights are at {dusk:0.00} of full");
    }

    // ------------------------------------------------------------------ the shaders' functions

    static Vector2 HeadlampBeam(Vector3 v, Vector3 fwd, float h, float reach, Vector3 n)
    {
        float z = Vector3.Dot(v, fwd);
        if (z <= 0.2f || z >= reach) return Vector2.zero;
        Vector3 up = (new Vector3(0, 1, 0) - fwd * fwd.y).normalized;
        float x = Vector3.Dot(v, Vector3.Cross(up, fwd)), y = Vector3.Dot(v, up);
        float d = Math.Max(v.magnitude, 1e-3f);
        Vector3 toLamp = -v / d;
        float u = Math.Abs(x) / Math.Min(2f + 0.35f * z, 6f);
        float side = Mathf.Exp(-1.2f * u * u);
        float fade = 1f - Smooth(15f, reach, z);
        float cut = (1f - Smooth(0.05f, 0.3f, y / z)) * Smooth(-h - 4f, -h - 2.5f, y);
        float facing = Mathf.Clamp01(Vector3.Dot(n, toLamp));
        float along = 0.5f + 0.5f * Smooth(0.5f, 9f, z);
        float ground = along * Math.Min(facing / Math.Max(h / d, 0.01f), 1f);
        float nearest = D("TG_HEADLAMP_NEAREST");
        float near = nearest * nearest / Math.Max(d * d, nearest * nearest);
        float face = facing * near;
        return new Vector2(ground, face) * (side * fade * cut);
    }

    static float Saturate(float s) =>
        (1f - Mathf.Exp(-D("TG_HEADLAMP_SATURATE") * s)) / (1f - Mathf.Exp(-D("TG_HEADLAMP_SATURATE")));

    static float HeadlampLight((Vector3 at, float h, Vector3 fwd, float share)[] lamps, Vector3 p, Vector3 n)
    {
        n = n.normalized;
        float up = Smooth(D("TG_HEADLAMP_GROUND_UP_FROM"), D("TG_HEADLAMP_GROUND_UP_TO"), n.y);
        Vector2 total = Vector2.zero;
        foreach (var l in lamps)
        {
            Vector3 v = p - l.at;
            Vector2 beam = HeadlampBeam(v, l.fwd, l.h, NightLook.HeadlampReach, n) * l.share;
            float g = up * (1f - Smooth(D("TG_HEADLAMP_GROUND_HIGH_FROM"), D("TG_HEADLAMP_GROUND_HIGH_TO"), v.y + l.h));
            total += new Vector2(beam.x * g, beam.y * (1f - g));
        }
        return D("TG_HEADLAMP_GROUND") * Saturate(total.x) + D("TG_HEADLAMP_FACE") * Saturate(total.y);
    }

    static float FloodLight(FloodField f, Vector3 p, Vector3 n)
    {
        float gi = (p.x - f.X0) / FloodField.Texel, gj = (p.z - f.Z0) / FloodField.Texel;
        if (gi < 0f || gj < 0f || gi > f.Width - 1 || gj > f.Height - 1) return 0f;
        int i = Math.Min((int)gi, f.Width - 2), j = Math.Min((int)gj, f.Height - 2);
        float fi = gi - i, fj = gj - j;
        float Bi(Func<int, float> at) => Mathf.Lerp(Mathf.Lerp(at(j * f.Width + i), at(j * f.Width + i + 1), fi),
            Mathf.Lerp(at((j + 1) * f.Width + i), at((j + 1) * f.Width + i + 1), fi), fj);
        float light = Bi(k => f.Light[k]), ground = Bi(k => f.Ground[k]);
        float tx = Bi(k => f.Toward[k].x), tz = Bi(k => f.Toward[k].z);
        var toward = new Vector3(tx, Mathf.Sqrt(Mathf.Clamp01(1f - tx * tx - tz * tz)), tz);
        float facing = Math.Min(Mathf.Clamp01(Vector3.Dot(n.normalized, toward)) / Math.Max(toward.y, D("TG_FLOOD_SLANT")), D("TG_FLOOD_FACING"));
        float high = 1f - Smooth(D("TG_FLOOD_HIGH_FROM"), D("TG_FLOOD_HIGH_TO"), p.y - ground);
        return Mathf.Clamp01(light) * high * Mathf.Lerp(D("TG_FLOOD_WRAP"), 1f, facing);
    }

    static float BeamAt(Floodlight f, float carry, Vector3 p)
    {
        Vector3 v = p - f.Position;
        float dd = v.sqrMagnitude, reach = f.Reach * carry;
        if (dd >= reach * reach) return 0f;
        float cs = Vector3.Dot(v / Mathf.Sqrt(dd + 1e-4f), f.Aim);
        float edge = Mathf.Cos(f.Cone * 0.5f * Mathf.Deg2Rad);
        float cone = Smooth(edge, Mathf.Lerp(edge, 1f, 0.4f), cs);
        return cone * Mathf.Clamp01(1f - dd / (reach * reach)) / (dd + 9f);
    }

    /// <summary>Volumetrics.shader's BeamMist with the mist at one density, before its colour and cap.</summary>
    static float BeamMist(Floodlight[] beams, float carry, Vector3 cam, Vector3 d, float far, float rho, float jitter)
    {
        float lit = 0f;
        const int samples = 8;
        foreach (Floodlight f in beams)
        {
            float reach = f.Reach * carry;
            Vector3 w0 = cam - f.Position;
            float b = Vector3.Dot(w0, d), cc = Vector3.Dot(w0, w0) - reach * reach;
            float disc = b * b - cc;
            if (disc <= 0f) continue;
            float root = Mathf.Sqrt(disc);
            float t0 = Math.Max(-b - root, 0f), t1 = Math.Min(-b + root, far);
            float da = Vector3.Dot(d, f.Aim), e = Vector3.Dot(f.Aim, w0);
            float side = Math.Max(1f - da * da, 1e-4f);
            float tNear = (da * e - b) / side;
            float sNear = Math.Max((e - da * b) / side, 0f);
            float cosEdge = Mathf.Cos(f.Cone * 0.5f * Mathf.Deg2Rad);
            float wide = sNear * Mathf.Sqrt(Math.Max(1f - cosEdge * cosEdge, 0f)) / Math.Max(cosEdge, 0.05f) + 2f;
            float stay = wide / Math.Max(Mathf.Sqrt(side), 0.05f);
            float ta = Math.Max(t0, tNear - stay), tb = Math.Min(t1, tNear + stay);
            if (tb <= ta) continue;
            float dt = (tb - ta) / samples;
            for (int i = 0; i < samples; i++)
            {
                float t = ta + dt * (i + jitter);
                lit += BeamAt(f, carry, cam + d * t) * rho * Mathf.Exp(-rho * t) * dt;
            }
        }
        return lit;
    }

    // ------------------------------------------------------------------ helpers

    static Vector3 Knee(Vector3 x, float most) => new Vector3(FloodField.Knee(x.x, most), FloodField.Knee(x.y, most), FloodField.Knee(x.z, most));
    static Vector3 Scale(Vector3 v, float k) => v * k;
    static float Max(Vector3 v) => Math.Max(v.x, Math.Max(v.y, v.z));
    static Vector3 Lin(Color c) => new Vector3(LinearLight.Linear(c.r), LinearLight.Linear(c.g), LinearLight.Linear(c.b));

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    static float Num(string root, string file, string pattern)
    {
        Match m = Regex.Match(File.ReadAllText(Path.Combine(root, file)), pattern);
        if (!m.Success) throw new Exception($"{pattern} not found in {file}");
        return float.Parse(m.Groups[1].Value);
    }

    /// <summary>Unit vectors spread over the sphere: the axes, their diagonals, and more when asked.</summary>
    static Vector3[] Normals(int rings = 0)
    {
        var list = new List<Vector3>();
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (x != 0 || y != 0 || z != 0) list.Add(new Vector3(x, y, z).normalized);
        for (int r = 1; r < rings; r++)
        {
            float lat = Mathf.PI * r / rings - Mathf.PI / 2f;
            int around = Math.Max(4, (int)(2 * rings * Mathf.Cos(lat)));
            for (int a = 0; a < around; a++)
            {
                float lon = 2f * Mathf.PI * a / around;
                list.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)));
            }
        }
        return list.ToArray();
    }

    static int Nearest(Vector3[] points, Vector3 p)
    {
        int best = 0;
        float nearest = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 d = points[i] - p;
            d.y = 0f;
            if (d.sqrMagnitude < nearest) { nearest = d.sqrMagnitude; best = i; }
        }
        return best;
    }

    static string Clock(float hour) => $"{(int)hour:00}:{(int)((hour - (int)hour) * 60f):00}";

    static void Report(string rule, string fail, string ok) => Check.Report(rule, fail == null ? new List<string>() : new List<string> { fail }, ok);
}
