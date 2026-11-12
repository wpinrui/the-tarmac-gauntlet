using System;
using System.Collections.Generic;
using TarmacGauntlet.Raceday;
using UnityEngine;

/// <summary>Checks the October sun path, the length of the night and that the light never jumps.</summary>
public static class Check
{
    private static int failed;

    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--table") >= 0) Table();
        SkyLook look = SkyClock.Look;
        float rise = Crossing(0f, 12f, -0.83f), set = Crossing(12f, 24f, -0.83f);
        Expect("sunrise at about 07:10", rise > 6.9f && rise < 7.4f, Clock(rise));
        Expect("sunset at about 17:25", set > 17.2f && set < 17.7f, Clock(set));
        float noon = Elevation(SkyClock.SolarNoon);
        Expect("noon sun 28 degrees up", Math.Abs(noon - 28f) < 0.1f, $"{noon:0.00}");
        Vector3 south = SkyClock.Direction(SkyClock.SolarNoon, SkyClock.Declination);
        Expect("noon sun due south", Math.Abs(south.x) < 1e-4f && south.z < 0f, $"{south.x:0.0000} {south.z:0.000}");
        Vector3 morning = SkyClock.Direction(8f, SkyClock.Declination);
        Expect("morning sun in the east", morning.x > 0f, $"{morning.x:0.000}");

        float lowest = 90f, darkHours = 0f, maxStep = 0f, prevLight = -1f;
        bool finite = true;
        string worst = "";
        for (int m = 0; m < 24 * 60; m++)
        {
            float hour = m / 60f;
            SkyState s = SkyClock.At(hour, look);
            lowest = Math.Min(lowest, s.SunElevation);
            if (s.Night > 0.5f) darkHours += 1f / 60f;
            if (prevLight >= 0f && Math.Abs(s.LightIntensity - prevLight) > maxStep)
            {
                maxStep = Math.Abs(s.LightIntensity - prevLight);
                worst = Clock(hour);
            }
            prevLight = s.LightIntensity;
            foreach (Color c in new[] { s.LightColour, s.Zenith, s.Horizon, s.Glow, s.AmbientSky, s.AmbientEquator, s.AmbientGround, s.Fog, s.CloudLit, s.CloudShade })
                finite &= Ok(c.r) && Ok(c.g) && Ok(c.b);
            finite &= Math.Abs(s.LightDir.magnitude - 1f) < 1e-3f;
        }
        Expect("full night: sun goes below -18 degrees", lowest < -18f, $"{lowest:0.0}");
        Expect("stars out between 11 and 13.5 hours", darkHours > 11f && darkHours < 13.5f, $"{darkHours:0.00} h");
        Expect("light changes smoothly, minute to minute", maxStep < 0.02f, $"{maxStep:0.0000} at {worst}");
        Expect("colours finite and in range", finite, "");
        SkyState midnight = SkyClock.At(1.6f, look);
        Expect("moon lights the track in the middle of the night", midnight.LightDir.y > 0.2f && midnight.LightIntensity > 0.1f,
            $"{midnight.LightDir.y:0.00} {midnight.LightIntensity:0.00}");
        Expect("the next day wraps round", Math.Abs(SkyClock.At(39f, look).SunElevation - SkyClock.At(15f, look).SunElevation) < 1e-3f, "");
        float hazeDawn = SkyClock.At(SkyClock.MistHour, look).Haze, hazeAfternoon = SkyClock.At(15f, look).Haze;
        Expect("morning mist closes the haze in", hazeDawn < hazeAfternoon * 0.65f, $"{hazeDawn:0} m against {hazeAfternoon:0} m");
        Expect("afternoon haze is the clear day's", Math.Abs(hazeAfternoon - look.Haze) < look.Haze * 0.01f, $"{hazeAfternoon:0} m");
        float hazeStep = 0f;
        for (int m = 0; m < 24 * 60; m++)
            hazeStep = Math.Max(hazeStep, Math.Abs(SkyClock.At((m + 1) / 60f, look).Haze - SkyClock.At(m / 60f, look).Haze));
        Expect("haze changes smoothly, minute to minute", hazeStep < look.Haze * 0.005f, $"{hazeStep:0.0} m");
        float density = SkyClock.Density(look.Haze);
        float seen(float metres) => (float)Math.Exp(-density * metres);
        Expect("haze hides all but 2% at its distance", Math.Abs(seen(look.Haze) - 0.02f) < 1e-3f, $"{seen(look.Haze):0.000}");
        Expect("far trees fade into the haze", seen(800f) < 0.7f && seen(800f) > 0.4f, $"{seen(800f):0.00} seen at 800 m");
        Expect("the track close by stays clear", seen(60f) > 0.95f, $"{seen(60f):0.000} seen at 60 m");
        SkyState noonSky = SkyClock.At(SkyClock.SolarNoon, look);
        Expect("the day sky pales towards the horizon", Saturation(noonSky.Horizon) < Saturation(noonSky.Zenith) * 0.5f && noonSky.Horizon.grayscale > noonSky.Zenith.grayscale,
            $"{Saturation(noonSky.Horizon):0.00} against {Saturation(noonSky.Zenith):0.00}");
        Expect("the day zenith is a soft blue, not a deep one", Saturation(noonSky.Zenith) < 0.7f, $"{Saturation(noonSky.Zenith):0.00}");
        float[] noise = CloudField.Fbm(64, 4, 5, 11);
        float lo = 1f, hi = 0f, seam = 0f;
        foreach (float v in noise) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        for (int k = 0; k < 64; k++)
            seam = Math.Max(seam, Math.Max(Math.Abs(noise[k * 64 + 63] - noise[k * 64]), Math.Abs(noise[63 * 64 + k] - noise[k])));
        Expect("cloud noise spans 0 to 1", lo >= 0f && hi <= 1f && hi - lo > 0.5f, $"{lo:0.00} to {hi:0.00}");
        Expect("cloud noise wraps without a seam", seam < 0.15f, $"{seam:0.000}");
        Console.WriteLine(failed == 0 ? "All sky checks pass" : $"{failed} sky checks FAIL");
        return failed == 0 ? 0 : 1;
    }

    private static float Saturation(Color c)
    {
        float hi = Math.Max(c.r, Math.Max(c.g, c.b)), lo = Math.Min(c.r, Math.Min(c.g, c.b));
        return hi > 0f ? (hi - lo) / hi : 0f;
    }

    private static bool Ok(float v) => !float.IsNaN(v) && v >= 0f && v <= 2.5f;

    private static float Elevation(float hour) => SkyClock.At(hour, SkyClock.Look).SunElevation;

    private static float Crossing(float a, float b, float level)
    {
        bool above = Elevation(a) > level;
        for (float t = a; t < b; t += 1f / 600f)
            if (Elevation(t) > level != above) return t;
        return float.NaN;
    }

    private static string Clock(float hour) => float.IsNaN(hour) ? "never" : $"{(int)hour:00}:{(int)(hour % 1f * 60f):00}";

    private static void Expect(string what, bool ok, string detail)
    {
        if (!ok) failed++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}{(detail.Length > 0 ? "  " + detail : "")}");
    }

    private static void Table()
    {
        for (float h = 0f; h < 24f; h += 0.5f)
        {
            SkyState s = SkyClock.At(h, SkyClock.Look);
            Console.WriteLine($"{Clock(h)} sun {s.SunElevation,6:0.0} light {s.LightIntensity:0.00} {s.LightColour} zen {s.Zenith} hor {s.Horizon} amb {s.AmbientSky} {s.AmbientEquator} {s.AmbientGround} night {s.Night:0.00}");
        }
    }
}
