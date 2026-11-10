using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

/// <summary>Renders each engine sound through the same drive: idle, a blip, flat out through four gears, lift off, idle.</summary>
public static class Audition
{
    private const int Rate = 44100;
    private const int Block = 512;   // samples between control updates, like the audio callback in Unity

    public static int Main(string[] args)
    {
        string dir = args[0];
        var wanted = args.Skip(1).ToList();
        var jobs = new List<(string name, EngineSound sound)>();
        foreach (CarClass c in new[] { CarClass.F, CarClass.E, CarClass.D, CarClass.C, CarClass.B, CarClass.A, CarClass.F1 })
        {
            string tier = c.ToString().ToLowerInvariant();
            if (wanted.Count == 0 || wanted.Contains(tier)) jobs.Add(($"{tier}-package", EngineSounds.Tier(c)));
        }
        foreach (CarModel m in CarModels.All)
            if (wanted.Count == 0 || wanted.Contains(m.Id))
                jobs.Add(($"{m.Id}-{Slug(m.Name)}", EngineSounds.For(m.Id)));

        foreach (var (name, sound) in jobs)
        {
            float[] samples = Drive(sound);
            float peak = samples.Max(Math.Abs);
            double rms = Math.Sqrt(samples.Average(x => (double)x * x));
            WriteWav(Path.Combine(dir, name + ".wav"), samples);
            Console.WriteLine($"{name,-34} peak {peak:0.00}  rms {rms:0.000}");
        }
        return 0;
    }

    private static float[] Drive(EngineSound s)
    {
        var synth = new EngineSynth(s, Rate);
        var output = new List<float>();
        var buffer = new float[Block];
        float idle = s.IdleRpm, red = s.RedlineRpm;
        float dt = Block / (float)Rate;
        float rpm = idle, speed = 0f;
        int gear = 1;
        double t = 0;

        void Run(double seconds, Func<double, (float rpm, float load)> at)
        {
            double end = t + seconds, start = t;
            while (t < end)
            {
                var (r, load) = at((t - start) / seconds);
                rpm = r;
                synth.Rpm = rpm;
                synth.Load = load;
                synth.Speed = speed;
                synth.Render(buffer, Block);
                output.AddRange(buffer);
                t += dt;
            }
        }

        Run(1.5, _ => (idle, 0f));
        Run(0.35, x => (idle + (0.65f * red - idle) * (float)Math.Sqrt(x), 1f));
        float top = rpm;
        Run(1.1, x => (idle + (top - idle) * (float)Math.Pow(1 - x, 2), 0f));
        Run(0.5, _ => (idle, 0f));

        // Flat out through four gears, each a little longer, dropping to 70% of the revs on each upshift.
        float low = 0.4f * red, high = 0.97f * red;
        for (gear = 1; gear <= 4; gear++)
        {
            float from = gear == 1 ? low : high * 0.7f;
            double length = 0.9 * Math.Pow(1.35, gear - 1);
            Run(length, x =>
            {
                speed = gear * 14f * (float)(from + (high - from) * x) / red;
                return (from + (high - from) * (float)Math.Pow(x, 0.85), 1f);
            });
            if (gear < 4) synth.Shift();
        }

        // Lift off and coast down in gear.
        float coastFrom = rpm;
        Run(2.4, x =>
        {
            float r = coastFrom + (0.4f * red - coastFrom) * (float)x;
            speed = 4f * 14f * r / red;
            return (r, 0f);
        });
        float after = rpm;
        Run(0.6, x => { speed *= 0.97f; return (after + (idle - after) * (float)x, 0f); });
        speed = 0f;
        Run(1.0, _ => (idle, 0f));
        return output.ToArray();
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    private static void WriteWav(string path, float[] samples)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (float x in samples) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767f));
    }
}
