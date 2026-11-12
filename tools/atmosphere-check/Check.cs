using System;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Rendering;
using UnityEngine;

/// <summary>
/// Checks the clouds' map and their shadows on the ground, the volumetric clouds' 3D noise, and how the colour grade
/// follows the sun in and out of the clouds' shadows.
/// </summary>
public static class Check
{
    private static int failed;
    private const int Size = 256;
    /// <summary>The sky shader's cloud scale, as TimeOfDay sets it.</summary>
    private const float CloudScale = 0.45f;

    public static int Main(string[] args)
    {
        float[] big = CloudField.Fbm(Size, 4, 5, 11), small = CloudField.Fbm(Size, 8, 4, 29);
        CloudMap Map(float cover, float overcast, Vector4 offset = default) => new CloudMap
        {
            Big = big, Small = small, Size = Size, Tiles = CloudMap.TilesFor(CloudScale, overcast), Cover = cover, Overcast = overcast, Offset = offset,
        };

        if (Array.IndexOf(args, "--table") >= 0)
            for (float c = 0.1f; c < 0.95f; c += 0.05f)
                Console.WriteLine($"cover {c:0.00}: {Shaded(Map(c, 0f)):P0} shaded, overcast 0.5 {Shaded(Map(c, 0.5f)):P0}");
        // The fair day's sky, and the dry race's weather (a third of the sky under cloud).
        float dryCloud = WeatherState.Dry.Cloud;
        CloudMap fair = Map(CloudMap.CoverFor(dryCloud), WeatherSky.Overcast(dryCloud));
        float fairShare = Shaded(fair);
        Expect("the dry race leaves most of the ground in sun, and clouds for it to break through", fairShare > 0.12f && fairShare < 0.4f,
            $"{fairShare:P0} in shade under {dryCloud:P0} cloud");
        float before = -1f, worst = 0f;
        bool more = true;
        for (float cloud = 0f; cloud <= 1.001f; cloud += 0.1f)
        {
            float share = Shaded(Map(CloudMap.CoverFor(cloud), WeatherSky.Overcast(cloud)));
            more &= share >= before - 0.01f;
            before = share;
            if (cloud > 0.05f && cloud < 0.95f) worst = Math.Max(worst, Math.Abs(share - cloud));
        }
        Expect("more cloud in the weather, more of the ground in shade", more, "");
        Expect("the ground's shade follows the weather's share of the sky", worst < 0.2f, $"at worst {worst:P0} out");
        float deck = Shaded(Map(CloudMap.CoverFor(1f), WeatherSky.Overcast(1f)));
        Expect("an overcast deck covers nearly everything", deck > 0.9f, $"{deck:P0}");

        float lo = 1f, hi = 0f;
        bool finite = true;
        for (int i = 0; i < 4000; i++)
        {
            float c = fair.At(i * 37.3f - 70000f, i * 91.7f - 120000f);
            finite &= !float.IsNaN(c);
            lo = Math.Min(lo, c);
            hi = Math.Max(hi, c);
        }
        Expect("cover runs 0 to 1", finite && lo >= 0f && hi <= 1f && lo < 0.05f && hi > 0.95f, $"{lo:0.00} to {hi:0.00}");

        // A cloud's shadow falls along the sun's rays: with the sun low in the west, east of the spot under the cloud.
        Vector3 west = new Vector3(-0.8f, 0.6f, 0f).normalized;
        Vector3 spot = new Vector3(300f, 20f, -200f);
        float along = (CloudMap.Base - spot.y) / west.y;
        float cast = fair.Shading(spot, west), under = fair.At(spot.x + west.x * along, spot.z + west.z * along);
        Expect("the shadow is cast along the sun's rays", Math.Abs(cast - under) < 1e-5f, $"{cast:0.000} against {under:0.000}");
        Vector3 high = new Vector3(0f, 1f, 0f);
        Expect("a sun overhead shades the spot under the cloud", Math.Abs(fair.Shading(spot, high) - fair.At(spot.x, spot.z)) < 1e-5f, "");

        // The wind carries the shadows: the map's offset moves them the other way.
        CloudMap moved = Map(fair.Cover, 0f, new Vector4(0.01f, 0f, 0.023f, 0f));
        float shift = 0.01f / fair.Tiles, slip = 0f;
        for (int i = 0; i < 200; i++)
            slip = Math.Max(slip, Math.Abs(moved.At(i * 29f - shift, 100f) - fair.At(i * 29f, 100f)));
        Expect("the drift carries the clouds the offset's metres the other way", slip < 1e-3f, $"{slip:0.0000} at {shift:0} m");

        Expect("fair weather cloud shadows are deep", CloudMap.Strength(0f) > 0.7f, $"{CloudMap.Strength(0f):0.00}");
        Expect("an overcast deck's shadows are faint", CloudMap.Strength(1f) < 0.1f, $"{CloudMap.Strength(1f):0.00}");

        if (Array.IndexOf(args, "--time") >= 0)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CloudNoise.Field(CloudNoise.Size);
            Console.WriteLine($"the 3D noise takes {clock.ElapsedMilliseconds} ms to make");
        }
        Noise();
        Grade();

        Console.WriteLine(failed == 0 ? "All atmosphere checks pass" : $"{failed} atmosphere checks FAILED");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>The volumetric clouds' 3D noise: it spans its range and wraps on every face.</summary>
    private static void Noise()
    {
        const int n = 32;
        byte[] f = CloudNoise.Field(n);
        int At(int x, int y, int z, int c) => f[((z * n + y) * n + x) * 2 + c];
        for (int c = 0; c < 2; c++)
        {
            int lo = 255, hi = 0;
            double inner = 0, face = 0;
            int innerCount = 0, faceCount = 0;
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int v = At(x, y, z, c);
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                        int step = Math.Abs(v - At((x + 1) % n, y, z, c)) + Math.Abs(v - At(x, (y + 1) % n, z, c)) + Math.Abs(v - At(x, y, (z + 1) % n, c));
                        if (x == n - 1 || y == n - 1 || z == n - 1) { face += step; faceCount++; }
                        else { inner += step; innerCount++; }
                    }
            string name = c == 0 ? "billows" : "detail";
            Expect($"3D noise {name} spans its range", lo < 40 && hi > 200, $"{lo} to {hi}");
            double a = inner / innerCount, b = face / faceCount;
            Expect($"3D noise {name} wraps without a seam", b < a * 1.25, $"{b:0.0} across the faces against {a:0.0} inside");
        }
    }

    /// <summary>The grade as the sun breaks through, and nothing changed where there is nothing to break through.</summary>
    private static void Grade()
    {
        SkyState noon = SkyClock.At(13f, SkyClock.Look), night = SkyClock.At(1f, SkyClock.Look);
        float broken = SkyGrade.Patchy(0.4f, WeatherSky.Overcast(0.4f));
        PostEffects.Grade plain = SkyGrade.For(noon, 0f, 0f);
        PostEffects.Grade sun = SkyGrade.For(noon, 0f, 0f, 0f, broken), shadow = SkyGrade.For(noon, 0f, 0f, 1f, broken);
        Expect("broken cloud: the sun breaking through lifts the picture", sun.Exposure > shadow.Exposure + 0.1f && sun.Contrast > shadow.Contrast
            && sun.Saturation > shadow.Saturation && sun.Temperature > shadow.Temperature, $"{sun.Exposure:0.00} against {shadow.Exposure:0.00}");
        PostEffects.Grade clear = SkyGrade.For(noon, 0f, 0f, 0f, SkyGrade.Patchy(0.05f, 0f));
        Expect("a clear sky in full sun grades as before", Same(clear, plain), "");
        PostEffects.Grade grey = SkyGrade.For(noon, 1f, 0f, 1f, SkyGrade.Patchy(1f, 1f)), greyPlain = SkyGrade.For(noon, 1f, 0f);
        Expect("an overcast sky grades as before", Same(grey, greyPlain), "");
        Expect("the night grades as before", Same(SkyGrade.For(night, 0f, 0f, 1f, broken), SkyGrade.For(night, 0f, 0f)), "");
        Expect("broken cloud is most between a third and a half of the sky", SkyGrade.Patchy(0.4f, WeatherSky.Overcast(0.4f)) > 0.8f
            && SkyGrade.Patchy(0.1f, 0f) < 0.05f && SkyGrade.Patchy(1f, WeatherSky.Overcast(1f)) < 0.05f, "");
    }

    private static bool Same(PostEffects.Grade a, PostEffects.Grade b) =>
        Math.Abs(a.Exposure - b.Exposure) < 1e-4f && Math.Abs(a.Contrast - b.Contrast) < 1e-3f && Math.Abs(a.Saturation - b.Saturation) < 1e-3f
        && Math.Abs(a.Temperature - b.Temperature) < 1e-3f && Math.Abs(a.Tint - b.Tint) < 1e-3f;

    /// <summary>Share of a 6 km square of ground more than half shaded by cloud, the sun overhead.</summary>
    private static float Shaded(CloudMap map)
    {
        int shaded = 0, all = 0;
        for (float x = -3000f; x < 3000f; x += 50f)
            for (float z = -3000f; z < 3000f; z += 50f, all++)
                if (map.At(x, z) > 0.5f) shaded++;
        return (float)shaded / all;
    }

    private static void Expect(string what, bool ok, string detail)
    {
        if (!ok) failed++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}  {detail}");
    }
}
