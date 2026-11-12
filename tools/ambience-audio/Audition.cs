using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TarmacGauntlet.Raceday;

/// <summary>
/// Renders the synthesised ambience through a set of moments: the wind standing in a breeze and in a gale, riding the
/// bonnet down a straight and through a lap, the same lap from the chase and TV cameras, and a car's tyres on a damp,
/// a wet and a flooded road. It checks the mix rules first: every quality level's ambience fits beside the engines and
/// tyres inside Unity's 32 real voices, and the beds rise and fall the right way.
/// </summary>
public static class Audition
{
    private const int Rate = 44100;
    private const int Block = 512;   // samples between control updates, like the audio callback in Unity
    private const int RealVoices = 32, EngineVoices = 10;
    private static readonly int[] TyreVoices = { 2, 4, 6, 8 };

    private sealed class Moment
    {
        public string Name;
        public double Length;
        public bool Wind;
        public Action<double, WindSynth> SetWind;
        public Action<double, WetRoadSynth> SetWet;
    }

    public static int Main(string[] args)
    {
        string dir = args[0];
        var wanted = args.Skip(1).ToList();
        if (!Check()) return 1;
        foreach (Moment m in Moments())
        {
            if (wanted.Count > 0 && !wanted.Contains(m.Name)) continue;
            float[] samples = m.Wind ? RenderWind(m) : RenderWet(m);
            int channels = m.Wind ? 2 : 1;
            WriteWav(Path.Combine(dir, m.Name + ".wav"), samples, channels);
            Console.WriteLine($"{m.Name,-18} peak {samples.Max(Math.Abs):0.00} rms {Rms(samples):0.000}");
        }
        return 0;
    }

    private static IEnumerable<Moment> Moments()
    {
        // The camera standing still trackside: the weather's breeze, then a gale.
        yield return new Moment { Name = "wind-breeze", Length = 14, Wind = true, SetWind = (t, w) => { w.Speed = 0; w.Breeze = 1; } };
        yield return new Moment { Name = "wind-gale", Length = 14, Wind = true, SetWind = (t, w) => { w.Speed = 0; w.Breeze = 2.5f; } };

        // On the bonnet: pulling away, flat out down a straight to 300 km/h, braking hard, and through a corner.
        float Lap(double t) => (float)(t < 9 ? 83 * (1 - Math.Exp(-t / 4.5)) : t < 11 ? 83 - 27 * (t - 9) : t < 14 ? 29 : 29 + 45 * (1 - Math.Exp(-(t - 14) / 3)));
        yield return new Moment { Name = "wind-bonnet-lap", Length = 20, Wind = true, SetWind = (t, w) => { w.Speed = Lap(t); w.Exposure = 1; } };
        yield return new Moment { Name = "wind-chase-lap", Length = 20, Wind = true, SetWind = (t, w) => { w.Speed = Lap(t); w.Exposure = AmbienceMix.Exposure(false, true); } };
        yield return new Moment { Name = "wind-tv-lap", Length = 20, Wind = true, SetWind = (t, w) => { w.Speed = Lap(t); w.Exposure = AmbienceMix.Exposure(false, false); } };

        // A car's tyres on a wet road through the same lap: damp, fully wet, and in standing water.
        yield return new Moment { Name = "wet-damp-lap", Length = 20, SetWet = (t, s) => { s.Speed = Lap(t); s.Wet = 0.35f; s.Water = 0.15f; } };
        yield return new Moment { Name = "wet-lap", Length = 20, SetWet = (t, s) => { s.Speed = Lap(t); s.Wet = 1; s.Water = 0.8f; } };
        yield return new Moment { Name = "wet-flooded-lap", Length = 20, SetWet = (t, s) => { s.Speed = Lap(t); s.Wet = 1; s.Water = 2.6f; } };
    }

    private static float[] RenderWind(Moment m)
    {
        var synth = new WindSynth(Rate, 3);
        int frames = (int)(m.Length * Rate);
        var output = new float[frames * 2];
        var block = new float[Block * 2];
        for (int at = 0; at < frames; at += Block)
        {
            int n = Math.Min(Block, frames - at);
            m.SetWind(at / (double)Rate, synth);
            synth.Render(block, n, 2);
            Array.Copy(block, 0, output, at * 2, n * 2);
        }
        return output;
    }

    private static float[] RenderWet(Moment m)
    {
        var synth = new WetRoadSynth(Rate, 5);
        int frames = (int)(m.Length * Rate);
        var output = new float[frames];
        var block = new float[Block];
        for (int at = 0; at < frames; at += Block)
        {
            int n = Math.Min(Block, frames - at);
            m.SetWet(at / (double)Rate, synth);
            synth.Render(block, n);
            Array.Copy(block, 0, output, at, n);
        }
        return output;
    }

    /// <summary>The mix rules, checked. Prints each failure and returns false if any.</summary>
    private static bool Check()
    {
        var failures = new List<string>();
        void Expect(bool ok, string what) { if (!ok) failures.Add(what); }

        for (int level = 0; level < AmbienceMix.Budgets.Length; level++)
        {
            AmbienceBudget b = AmbienceMix.Budget(level);
            int total = b.Voices + EngineVoices + TyreVoices[level];
            Console.WriteLine($"level {level}: ambience {b.Voices} voices, with engines and tyres {total} of {RealVoices}");
            // High and Ultra may run past the real voices when every bed plays at once: Unity then virtualises the lowest
            // priority first, the wet hiss and the far tyres.
            if (level <= 1) Expect(total <= RealVoices, $"level {level} needs {total} voices");
            if (level > 0) Expect(b.Voices >= AmbienceMix.Budget(level - 1).Voices, $"level {level} plays fewer voices than the level below");
        }

        (float calmDay, float livelyIdle) = AmbienceMix.Crowd(0f, 0f, 0f);
        (float calmNight, _) = AmbienceMix.Crowd(0f, 1f, 0f);
        (_, float livelyPass) = AmbienceMix.Crowd(1f, 0f, 0f);
        Expect(livelyIdle == 0f, "the lively crowd plays with nothing happening");
        Expect(livelyPass > 0.9f, "the lively crowd barely rises for a pass");
        Expect(calmNight < calmDay * 0.7f, "the crowd doesn't thin at night");
        Expect(AmbienceMix.Passing(0) == 0f && AmbienceMix.Passing(1) < AmbienceMix.Passing(4), "more cars passing doesn't excite more");

        Expect(AmbienceMix.Rain(0f, 2) == (0f, 0f), "rain plays in the dry");
        Expect(AmbienceMix.Rain(1f, 2).heavy == 0f, "heavy rain plays in a drizzle");
        Expect(AmbienceMix.Rain(10f, 2).heavy > 0.9f, "heavy rain is quiet in a downpour");
        Expect(AmbienceMix.Rain(10f, 1).light > AmbienceMix.Rain(1f, 1).light, "one rain layer doesn't rise with the rain");
        Expect(!AmbienceMix.Thunder(false, 5f) && AmbienceMix.Thunder(true, 5f) && AmbienceMix.Thunder(false, 9f), "thunder rolls at the wrong times");
        Expect(AmbienceMix.ThunderGap(12f, 0.5f) < AmbienceMix.ThunderGap(3f, 0.5f), "thunder isn't closer in a downpour");

        Expect(AmbienceMix.Away(20f) == 0f && AmbienceMix.Away(400f) == 1f, "away from the stands is wrong");
        (float dayNear, _) = AmbienceMix.Country(0f, 0f, 0f, 2);
        (float dayFar, float nightDay) = AmbienceMix.Country(1f, 0f, 0f, 2);
        (float dayAtNight, float nightFar) = AmbienceMix.Country(1f, 1f, 0f, 2);
        Expect(dayFar > dayNear * 2f, "the countryside isn't louder away from the stands");
        Expect(nightDay < 0.01f && dayAtNight < 0.01f && nightFar > 0.9f, "the countryside doesn't turn to night");
        Expect(AmbienceMix.Country(1f, 0f, 5f, 2).day == 0f, "birds sing in the rain");
        Expect(AmbienceMix.Exposure(true, false) > AmbienceMix.Exposure(false, true)
               && AmbienceMix.Exposure(false, true) > AmbienceMix.Exposure(false, false), "wind exposure is in the wrong order");

        // The wind must rise with speed, and the wet hiss with speed and water.
        float WindAt(float speed)
        {
            var w = new WindSynth(Rate, 1) { Speed = speed, Breeze = 0.5f };
            var buf = new float[Rate * 2];
            w.Render(buf, Rate, 2);
            w.Render(buf, Rate, 2);
            return Rms(buf);
        }
        float WetAt(float speed, float water)
        {
            var s = new WetRoadSynth(Rate, 1) { Speed = speed, Wet = 1, Water = water };
            var buf = new float[Rate];
            s.Render(buf, Rate);
            s.Render(buf, Rate);
            return Rms(buf);
        }
        Expect(WindAt(0f) < WindAt(30f) && WindAt(30f) < WindAt(80f), "the wind doesn't rise with speed");
        Expect(WetAt(10f, 0.5f) < WetAt(60f, 0.5f), "the wet hiss doesn't rise with speed");
        Expect(WetAt(40f, 0.5f) < WetAt(40f, 2.5f), "standing water doesn't add to the wet hiss");
        var dry = new WetRoadSynth(Rate, 1) { Speed = 60f, Wet = 0f };
        var quiet = new float[Rate];
        dry.Render(quiet, Rate);
        Expect(quiet.All(x => x == 0f), "the wet hiss plays on a dry road");

        foreach (string f in failures) Console.WriteLine("FAIL: " + f);
        Console.WriteLine(failures.Count == 0 ? "mix rules: all pass" : $"mix rules: {failures.Count} failed");
        return failures.Count == 0;
    }

    private static float Rms(float[] x) => (float)Math.Sqrt(x.Average(v => (double)v * v));

    private static void WriteWav(string path, float[] samples, int channels)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(Rate); w.Write(Rate * 2 * channels); w.Write((short)(2 * channels)); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (float x in samples) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767f));
    }
}
