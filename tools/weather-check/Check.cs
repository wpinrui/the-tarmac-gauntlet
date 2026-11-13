using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;

/// <summary>
/// Rolls the weather for thousands of races and checks it reads as a European late October: how often it
/// rains, when the showers come, that fronts bring hours of rain, that the track dries again, that the
/// grip falls the right amount and that nothing jumps.
/// </summary>
public static class Check
{
    private const int Races = 4000;
    private const float Start = 15f;
    private static int failed;

    public static int Main(string[] args)
    {
        int at = Array.IndexOf(args, "--table");
        if (at >= 0) { Table(int.Parse(args[at + 1])); return 0; }
        at = Array.IndexOf(args, "--json");
        if (at >= 0) { Json(args.Skip(at + 1).Select(int.Parse)); return 0; }
        at = Array.IndexOf(args, "--races");
        if (at >= 0) { Summaries(int.Parse(args[at + 1])); return 0; }

        var kinds = new int[4];
        int wetRaces = 0, frontRaces = 0, showersTotal = 0, afternoonShowers = 0, slowDries = 0, dries = 0;
        double wetMinutes = 0, minutes = 0, rainTotal = 0, soakedMinutes = 0;
        float maxCloudStep = 0f, maxWaterStep = 0f, maxWater = 0f, maxRain = 0f;
        float worstDry = 0f;
        bool finite = true;
        int raceMinutes = 24 * 60;
        for (int seed = 1; seed <= Races; seed++)
        {
            var plan = new WeatherPlan(seed, Start);
            kinds[(int)plan.Kind]++;
            bool wet = false;
            WeatherState prev = plan.At(0f);
            float dryingSince = -1f;
            for (int m = 0; m <= raceMinutes; m++)
            {
                WeatherState w = plan.At(m * 60f);
                if (float.IsNaN(w.Cloud + w.Rain + w.Water + w.Wind) || w.Cloud < 0f || w.Cloud > 1f || w.Rain < 0f || w.Water < 0f) finite = false;
                minutes++;
                if (w.Rain > 0.1f) wet = true;
                if (w.Water > 0.1f) wetMinutes++;
                if (w.Water > 1f) soakedMinutes++;
                rainTotal += w.Rain / 60f;
                maxWater = Math.Max(maxWater, w.Water);
                maxRain = Math.Max(maxRain, w.Rain);
                if (m > 0)
                {
                    maxCloudStep = Math.Max(maxCloudStep, Math.Abs(w.Cloud - prev.Cloud));
                    maxWaterStep = Math.Max(maxWaterStep, Math.Abs(w.Water - prev.Water));
                }
                // A wet track that stops getting rain in the daytime should be dry within three hours, even in the cool autumn air.
                float local = (Start + m / 60f) % 24f;
                bool day = local > 9f && local < 16.5f;
                if (prev.Rain > 0f && w.Rain == 0f && w.Water > 0.3f && day) dryingSince = m;
                if (dryingSince >= 0f)
                {
                    if (w.Rain > 0f || !day) dryingSince = -1f;
                    else if (w.Water < 0.05f)
                    {
                        dries++;
                        worstDry = Math.Max(worstDry, m - dryingSince);
                        if (m - dryingSince > 180f) slowDries++;
                        dryingSince = -1f;
                    }
                }
                prev = w;
            }
            if (wet) wetRaces++;
            if (plan.Fronts.Any(f => f > -2f && f < 24f)) frontRaces++;
            foreach (float s in plan.Showers)
            {
                if (s < 0f || s > 24f) continue;
                showersTotal++;
                float local = (Start + s) % 24f;
                if (local >= 11f && local <= 20f) afternoonShowers++;
            }
        }

        string shares = string.Join(", ", Enum.GetNames(typeof(WeatherKind)).Select((n, i) => $"{n} {100f * kinds[i] / Races:0}%"));
        Expect("every kind of weekend comes up", kinds.All(k => k > Races / 40), shares);
        float wetShare = 100f * wetRaces / Races;
        Expect("rain falls in most races but not all", wetShare > 55f && wetShare < 92f, $"{wetShare:0}% of races see rain");
        float wetTime = (float)(100.0 * wetMinutes / minutes);
        Expect("the track is wet for a fair share of the time", wetTime > 8f && wetTime < 35f, $"{wetTime:0.0}% of race time wet, {100.0 * soakedMinutes / minutes:0.0}% soaked");
        float frontShare = 100f * frontRaces / Races;
        Expect("a front crosses in some races", frontShare > 20f && frontShare < 65f, $"{frontShare:0}% of races");
        float afternoon = 100f * afternoonShowers / Math.Max(showersTotal, 1);
        Expect("showers come most often between 11:00 and 20:00", afternoon > 45f, $"{afternoon:0}% of {showersTotal} showers");
        double mean = rainTotal / Races;
        Expect("a race's rain is an autumn day's worth", mean > 2 && mean < 10, $"{mean:0.0} mm a race on average");
        Expect("a daytime track dries within three hours of the rain", dries > 100 && slowDries <= dries / 20, $"{dries} dryings, {slowDries} slower, slowest {worstDry:0} min");
        Expect("cloud never jumps", maxCloudStep < 0.06f, $"largest change {maxCloudStep:0.000} a minute");
        Expect("the water film never jumps", maxWaterStep < 0.6f, $"largest change {maxWaterStep:0.00} mm a minute");
        Expect("rain stays within autumn extremes", maxRain <= 45f && maxWater <= WeatherPlan.Flooded + 1e-4f, $"heaviest {maxRain:0.0} mm/h, deepest {maxWater:0.00} mm");
        Expect("no value goes out of range", finite, "");

        var a = new WeatherPlan(77, Start);
        var b = new WeatherPlan(77, Start);
        bool same = true, differ = false;
        for (int m = 0; m < raceMinutes; m += 7)
        {
            if (a.At(m * 60f).Rain != b.At(m * 60f).Rain || a.At(m * 60f).Cloud != b.At(m * 60f).Cloud) same = false;
            if (Math.Abs(a.At(m * 60f).Cloud - new WeatherPlan(78, Start).At(m * 60f).Cloud) > 0.05f) differ = true;
        }
        Expect("one seed always rolls the same race", same && differ, "");

        float g0 = RaceWeather.GripFor(0f), damp = RaceWeather.GripFor(0.05f), wetGrip = RaceWeather.GripFor(1f), flood = RaceWeather.GripFor(WeatherPlan.Flooded);
        bool falls = true;
        for (float x = 0f; x < WeatherPlan.Flooded; x += 0.01f) if (RaceWeather.GripFor(x + 0.01f) > RaceWeather.GripFor(x)) falls = false;
        Expect("grip falls as the track gets wetter", g0 == 1f && falls && damp > 0.95f && wetGrip > 0.72f && wetGrip < 0.78f && flood >= 0.65f,
            $"dry {g0:0.00}, damp {damp:0.00}, wet {wetGrip:0.00}, flooded {flood:0.00}");
        float slower = 100f * (1f / (float)Math.Sqrt(wetGrip) - 1f);
        Expect("a wet lap is about 10 to 15% slower", slower > 10f && slower < 16f, $"{slower:0.0}% at 1 mm");

        Lights();

        Console.WriteLine(failed == 0 ? "all ok" : $"{failed} failed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Checks the lights come on with the clear dusk, earlier under cloud and rain, and never flick on or off.</summary>
    private static void Lights()
    {
        int raceMinutes = 24 * 60;
        var elevation = new float[raceMinutes + 1];
        for (int m = 0; m <= raceMinutes; m++) elevation[m] = SkyClock.At(Start + m / 60f, SkyClock.Look).SunElevation;
        var clear = new WeatherState { Cloud = 0.3f, Wind = 1f };
        float clearDiff = 0f;
        for (int m = 0; m <= raceMinutes; m++)
        {
            SkyState s = SkyClock.At(Start + m / 60f, SkyClock.Look);
            WeatherSky.Apply(ref s, clear, WeatherSky.Lights(s.SunElevation, WeatherSky.Dusk(clear)));
            clearDiff = Math.Max(clearDiff, Math.Abs(s.Lights - s.Night));
        }
        Expect("fair weather keeps the clear dusk for the lights", clearDiff < 1e-6f, $"largest difference {clearDiff:0.000000}");

        float On(float hour, float cloud, float rain) =>
            WeatherSky.Lights(SkyClock.At(hour, SkyClock.Look).SunElevation, WeatherSky.Dusk(new WeatherState { Cloud = cloud, Rain = rain }));
        float clearDusk = On(17.57f, 0f, 0f), greyDusk = On(17.57f, 1f, 0f), wetAfternoon = On(16.5f, 1f, 8f), wetNoon = On(12.3f, 1f, 30f), greyDay = On(15f, 1f, 0f);
        Expect("under a full overcast the lights are on by 17:34", clearDusk < 0.01f && greyDusk > 0.12f, $"clear {clearDusk:0.00}, overcast {greyDusk:0.00}");
        Expect("heavy rain brings them on by 16:30", wetAfternoon > 0.12f, $"{wetAfternoon:0.00}");
        Expect("they stay off by day, even in a downpour at noon", wetNoon == 0f && greyDay == 0f, $"noon {wetNoon:0.00}, overcast 15:00 {greyDay:0.00}");

        // How far the lights are on (NightBudget.On) may rise and fall with the weather, but never off and back on within ten minutes.
        const int Window = 10;
        float maxStep = 0f, worstFlicker = 0f;
        int worstSeed = 0, worstMinute = 0;
        var on = new float[raceMinutes + 1];
        for (int seed = 1; seed <= 400; seed++)
        {
            var plan = new WeatherPlan(seed, Start);
            float dusk = WeatherSky.Dusk(plan.At(0f));
            float prev = WeatherSky.Lights(elevation[0], dusk);
            on[0] = SkyClock.Smooth(0f, 0.12f, prev);
            for (int m = 1; m <= raceMinutes; m++)
            {
                dusk = WeatherSky.Settle(dusk, plan.At(m * 60f), 60f);
                float now = WeatherSky.Hold(prev, WeatherSky.Lights(elevation[m], dusk), dusk);
                maxStep = Math.Max(maxStep, Math.Abs(now - prev));
                on[m] = SkyClock.Smooth(0f, 0.12f, now);
                prev = now;
            }
            for (int m = 0; m + Window <= raceMinutes; m++)
            {
                float travel = 0f;
                for (int k = m + 1; k <= m + Window; k++) travel += Math.Abs(on[k] - on[k - 1]);
                float flicker = travel - Math.Abs(on[m + Window] - on[m]);
                if (flicker > worstFlicker) { worstFlicker = flicker; worstSeed = seed; worstMinute = m; }
            }
        }
        Expect("the lights fade over minutes, never in one jump", maxStep < 0.15f, $"largest change {maxStep:0.000} a race minute");
        Expect("the lights never go off and back on within ten minutes", worstFlicker < 0.5f, $"most back and forth {worstFlicker:0.00} in ten minutes, race {worstSeed} at {worstMinute} min");
    }

    private static void Expect(string what, bool ok, string detail)
    {
        if (!ok) failed++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
    }

    private static void Table(int seed)
    {
        var plan = new WeatherPlan(seed, Start);
        Console.WriteLine($"race {seed}: {plan.Kind}, fronts at {string.Join(" ", plan.Fronts.Select(f => $"{f:0.0}h"))}, {plan.Showers.Count} showers");
        Console.WriteLine("race   local  cloud  rain mm/h  water mm  grip  wind");
        for (int q = 0; q <= 24 * 4; q++)
        {
            WeatherState w = plan.At(q * 900f);
            float local = (Start + q / 4f) % 24f;
            Console.WriteLine($"{q / 4f,5:0.00}  {(int)local:00}:{(int)(local % 1f * 60f + 0.5f) % 60:00}  {w.Cloud,5:0.00}  {w.Rain,9:0.0}  {w.Water,8:0.00}  {w.Grip,4:0.00}  {w.Wind,4:0.0}");
        }
    }

    /// <summary>Each race's weather every five minutes, as JSON, for the look preview.</summary>
    private static void Json(IEnumerable<int> seeds)
    {
        var races = new List<string>();
        foreach (int seed in seeds)
        {
            var plan = new WeatherPlan(seed, Start);
            var rows = new List<string>();
            for (int q = 0; q <= 24 * 12; q++)
            {
                WeatherState w = plan.At(q * 300f);
                rows.Add($"[{w.Cloud:0.###},{w.Rain:0.##},{w.Water:0.###},{w.Wind:0.##}]");
            }
            races.Add($"{{\"seed\":{seed},\"kind\":\"{plan.Kind}\",\"rows\":[{string.Join(",", rows)}]}}");
        }
        Console.WriteLine($"[{string.Join(",", races)}]");
    }

    private static void Summaries(int count)
    {
        for (int seed = 1; seed <= count; seed++)
        {
            var plan = new WeatherPlan(seed, Start);
            var line = new char[96];
            float rain = 0f;
            for (int q = 0; q < 96; q++)
            {
                WeatherState w = plan.At(q * 900f + 450f);
                rain += w.Rain / 4f;
                line[q] = w.Water > 1f ? '#' : w.Water > 0.1f ? '=' : w.Rain > 0f ? '.' : w.Cloud > 0.8f ? '~' : w.Cloud > 0.45f ? '-' : ' ';
            }
            Console.WriteLine($"{seed,4} {plan.Kind,-10} {rain,5:0.0} mm |{new string(line)}|");
        }
    }
}
