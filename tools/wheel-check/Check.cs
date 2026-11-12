// Reads every car's OBJ and MTL, works out each wheel's blur the way the game does (WheelBlurProfile) and checks it:
// every wheel has parts that turn past its tyre, the blur fits inside the tyre, its lip is solid, and the fade comes in
// where the spokes would strobe. With a folder, writes each car's static rim and blurred disc side by side as a PPM.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    static int failures;
    static readonly string[] Corners = { "fl", "fr", "rl", "rr" };

    static void Report(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) failures++;
    }

    /// <summary>As CarBuilder.IsWheelPart.</summary>
    static bool IsWheelPart(string name, string corner)
    {
        if (!name.StartsWith("wheel_")) return false;
        string[] words = name.Split('_');
        for (int i = 2; i < words.Length; i++)
            if (words[i] == corner) return true;
        return false;
    }

    sealed class Obj
    {
        public List<Vector3> V = new List<Vector3>();
        // Per object: triangles as vertex indices, with each triangle's material.
        public Dictionary<string, List<(int a, int b, int c, string mat)>> Parts = new Dictionary<string, List<(int, int, int, string)>>();
        public Dictionary<string, Vector3> Kd = new Dictionary<string, Vector3>();
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static Obj Load(string objPath)
    {
        var o = new Obj();
        string mtl = Path.ChangeExtension(objPath, ".mtl");
        if (File.Exists(mtl))
        {
            string cur = null;
            foreach (string line in File.ReadLines(mtl))
            {
                string[] w = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                if (w[0] == "newmtl") cur = w[1];
                else if (w[0] == "Kd" && cur != null) o.Kd[cur] = new Vector3(F(w[1]), F(w[2]), F(w[3]));
            }
        }
        string part = "default", material = null;
        foreach (string line in File.ReadLines(objPath))
        {
            if (line.Length < 2) continue;
            if (line[0] == 'v' && line[1] == ' ')
            {
                string[] w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                o.V.Add(new Vector3(F(w[1]), F(w[2]), F(w[3])));
            }
            else if (line.StartsWith("o ")) part = line.Substring(2).Trim();
            else if (line.StartsWith("usemtl ")) material = line.Substring(7).Trim();
            else if (line[0] == 'f' && line[1] == ' ')
            {
                if (!part.StartsWith("wheel_")) continue;
                string[] w = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                var idx = new List<int>();
                for (int i = 1; i < w.Length; i++)
                {
                    int k = int.Parse(w[i].Split('/')[0], CultureInfo.InvariantCulture);
                    idx.Add(k > 0 ? k - 1 : o.V.Count + k);
                }
                if (!o.Parts.TryGetValue(part, out var tris)) o.Parts[part] = tris = new List<(int, int, int, string)>();
                for (int i = 1; i + 1 < idx.Count; i++) tris.Add((idx[0], idx[i], idx[i + 1], material));
            }
        }
        return o;
    }

    static (Vector3 min, Vector3 max) Bounds(Obj o, List<(int a, int b, int c, string mat)> tris)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var t in tris)
            foreach (int i in new[] { t.a, t.b, t.c })
            {
                Vector3 p = o.V[i];
                min = new Vector3(Math.Min(min.x, p.x), Math.Min(min.y, p.y), Math.Min(min.z, p.z));
                max = new Vector3(Math.Max(max.x, p.x), Math.Max(max.y, p.y), Math.Max(max.z, p.z));
            }
        return (min, max);
    }

    static int Main(string[] args)
    {
        string vehicles = args[0];
        string previews = args.Length > 1 && args[1] != "" ? args[1] : null;
        if (previews != null) Directory.CreateDirectory(previews);

        foreach (string objPath in Directory.GetFiles(vehicles, "*.obj", SearchOption.AllDirectories).OrderBy(p => p))
        {
            string car = Path.GetFileNameWithoutExtension(objPath);
            Obj o = Load(objPath);
            var shots = new List<(Vector3[] picture, WheelBlurProfile.Result result)>();
            foreach (string corner in Corners)
            {
                string tyreName = o.Parts.Keys.FirstOrDefault(n => n == "wheel_tyre_" + corner || n.StartsWith("wheel_tyre_" + corner + "_"));
                if (tyreName == null)
                {
                    Report(false, $"{car} {corner}: no tyre");
                    continue;
                }
                var (tmin, tmax) = Bounds(o, o.Parts[tyreName]);
                Vector3 centre = (tmin + tmax) * 0.5f;
                float tyreRadius = Math.Max(tmax.y - tmin.y, tmax.z - tmin.z) * 0.5f, tyreHalfWidth = (tmax.x - tmin.x) * 0.5f;
                float outward = Math.Sign(centre.x);

                var corners = new List<Vector3>();
                var colours = new List<Vector3>();
                var turning = new List<string>();
                foreach (var kv in o.Parts)
                {
                    if (!IsWheelPart(kv.Key, corner) || kv.Key.StartsWith("wheel_tyre_")) continue;
                    turning.Add(kv.Key);
                    foreach (var t in kv.Value)
                    {
                        foreach (int i in new[] { t.a, t.b, t.c })
                        {
                            Vector3 p = o.V[i] - centre;
                            corners.Add(new Vector3(p.x * outward, p.y, p.z));
                        }
                        colours.Add(t.mat != null && o.Kd.TryGetValue(t.mat, out Vector3 kd) ? kd : new Vector3(0.5f, 0.5f, 0.5f));
                    }
                }
                if (turning.Count == 0)
                {
                    Report(false, $"{car} {corner}: nothing turns past the tyre");
                    continue;
                }
                var picture = new Vector3[WheelBlurProfile.Cells * WheelBlurProfile.Cells];
                WheelBlurProfile.Result r = WheelBlurProfile.Build(corners, colours, picture);
                float[] a = Enumerable.Range(0, WheelBlurProfile.Rings).Select(i => r.Rgba[i * 4 + 3]).ToArray();
                float lip = a.Skip(WheelBlurProfile.Rings - 8).Max();
                float least = a.Take(WheelBlurProfile.Rings - 8).Min();
                bool ok = r.Radius > 0.5f * tyreRadius && r.Radius <= tyreRadius + 1e-3f && r.Face <= tyreHalfWidth + 0.01f && lip > 0.85f;
                Report(ok, $"{car} {corner}: parts {string.Join(" ", turning.Select(n => n.Replace("_" + corner, "")))}, blur radius {r.Radius:F3} of tyre {tyreRadius:F3}, face {r.Face:F3} of {tyreHalfWidth:F3}, lip cover {lip:F2}, thinnest ring {least:F2}");
                if (corner == "fl" || corner == "rl") shots.Add((picture, r));
            }
            if (previews != null && shots.Count > 0) Picture(Path.Combine(previews, car + ".ppm"), shots);
        }

        // The fade: none at a crawl, full well before racing speed at any frame rate, and never back off as the wheel speeds up.
        const float radius = 0.32f;
        foreach (float fps in new[] { 30f, 60f, 144f })
        {
            float dt = 1f / fps;
            float crawl = WheelBlurProfile.Fade(5f / 3.6f / radius, dt), forty = WheelBlurProfile.Fade(40f / 3.6f / radius, dt);
            bool rising = true;
            float last = 0f;
            for (float kmh = 0f; kmh < 400f; kmh += 0.5f)
            {
                float f = WheelBlurProfile.Fade(kmh / 3.6f / radius, dt);
                rising &= f >= last - 1e-6f;
                last = f;
            }
            Report(crawl < 0.01f && forty > 0.99f && rising, $"fade at {fps} fps: {crawl:F2} at 5 km/h, {forty:F2} at 40 km/h, rising {rising}");
            // Before it is fully in, the spokes must not yet turn half a five-spoke gap a frame.
            float strobe = 0f;
            for (float w = 0f; w < 400f; w += 0.1f)
                if (WheelBlurProfile.Fade(w, dt) < 0.5f) strobe = Math.Max(strobe, w * dt);
            Report(strobe < (float)Math.PI / 5f, $"spokes at {fps} fps turn at most {strobe * 180f / Math.PI:F0} degrees a frame under half fade");
            float streak = Math.Abs(WheelBlurProfile.StreakStep(1000f, dt));
            Report(streak <= WheelBlurProfile.MostStreakStep + 1e-6f, $"streaks at {fps} fps step {streak * 180f / Math.PI:F0} degrees a frame at most");
        }

        Console.WriteLine(failures == 0 ? "All wheel checks pass." : $"{failures} wheel check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Each wheel's face as modelled, then as the blur draws it, over dark: one row a wheel.</summary>
    static void Picture(string path, List<(Vector3[] picture, WheelBlurProfile.Result result)> shots)
    {
        int n = WheelBlurProfile.Cells;
        int w = n * 2, h = n * shots.Count;
        var px = new byte[w * h * 3];
        var dark = new Vector3(0.05f, 0.05f, 0.06f);
        for (int s = 0; s < shots.Count; s++)
        {
            var (picture, r) = shots[s];
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                Vector3 still = picture[y * n + z];
                if (still.x < 0f) still = dark;
                float py = (y + 0.5f) / n * 2f - 1f, pz = (z + 0.5f) / n * 2f - 1f;
                float rr = (float)Math.Sqrt(py * py + pz * pz);
                Vector3 blur = dark;
                if (rr < 1f)
                {
                    int ring = Math.Min((int)(rr * WheelBlurProfile.Rings), WheelBlurProfile.Rings - 1);
                    var c = new Vector3(r.Rgba[ring * 4], r.Rgba[ring * 4 + 1], r.Rgba[ring * 4 + 2]);
                    blur = Vector3.Lerp(dark, c, r.Rgba[ring * 4 + 3]);
                }
                int row = s * n + (n - 1 - y);
                Put(px, (row * w + z) * 3, still);
                Put(px, (row * w + n + z) * 3, blur);
            }
        }
        using var f = File.Create(path);
        byte[] head = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        f.Write(head, 0, head.Length);
        f.Write(px, 0, px.Length);
    }

    static void Put(byte[] px, int at, Vector3 c)
    {
        px[at] = (byte)(Mathf.Clamp01(c.x) * 255f);
        px[at + 1] = (byte)(Mathf.Clamp01(c.y) * 255f);
        px[at + 2] = (byte)(Mathf.Clamp01(c.z) * 255f);
    }
}
