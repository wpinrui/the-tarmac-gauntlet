using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

/// <summary>
/// Renders the tyre and surface sounds through a set of moments on track: a slide through a corner, a lock-up, a launch,
/// a kerb, a trip through the gravel, a run onto the grass, a wet slide and a short lap that strings them together. Each
/// moment is written alone and again over a D class engine, mixed as the game mixes them.
/// </summary>
public static class Audition
{
    private const int Rate = 44100;
    private const int Block = 512;   // samples between control updates, like the audio callback in Unity

    private struct State
    {
        public float Speed, Slide, Kerb, Gravel, Grass, Throttle, Wet;
    }

    private sealed class Moment
    {
        public string Name;
        public double Length;
        public float TyreSize = 0.4f;
        public Func<double, State> At;
    }

    public static int Main(string[] args)
    {
        string dir = args[0];
        var wanted = args.Skip(1).ToList();
        if (!Check()) return 1;
        foreach (Moment m in Moments())
        {
            if (wanted.Count > 0 && !wanted.Contains(m.Name)) continue;
            Render(m, out float[] tyres, out float[] mixed);
            WriteWav(Path.Combine(dir, m.Name + "-tyres.wav"), tyres);
            WriteWav(Path.Combine(dir, m.Name + "-with-engine.wav"), mixed);
            Console.WriteLine($"{m.Name,-20} tyres peak {tyres.Max(Math.Abs):0.00} rms {Rms(tyres):0.000}   mixed peak {mixed.Max(Math.Abs):0.00} rms {Rms(mixed):0.000}");
        }
        return 0;
    }

    private static IEnumerable<Moment> Moments()
    {
        // Through a fast corner the rear lets go: a scrub, a full squeal, then it gathers up.
        State Corner(double t, float wet)
        {
            float slide = (float)(t < 1 ? 0.1 * t : t < 2 ? 0.1 + 0.25 * (t - 1) : t < 3.5 ? 0.35 + 0.4 * Math.Min(1, (t - 2) / 0.6) : Math.Max(0, 0.75 - 0.5 * (t - 3.5)));
            return new State { Speed = (float)(32 - 1.2 * t), Slide = slide, Throttle = 0.55f, Wet = wet };
        }
        yield return new Moment { Name = "corner-slide", Length = 6, At = t => Corner(t, 0f) };
        yield return new Moment { Name = "corner-slide-slick", Length = 6, TyreSize = 1f, At = t => Corner(t, 0f) };
        yield return new Moment { Name = "wet-slide", Length = 6, At = t => Corner(t, 1f) };

        // Hard braking from 180 km/h: the anti-lock lets the fronts scrub, and turning in they squeal.
        yield return new Moment
        {
            Name = "lock-up", Length = 5,
            At = t => t < 1
                ? new State { Speed = 50, Throttle = 1 }
                : new State
                {
                    Speed = (float)Math.Max(14, 50 - 13 * (t - 1)),
                    Slide = t < 3.4 ? 0.35f + 0.05f * (float)Math.Sin(t * 40) : t < 4.2 ? 0.65f : 0.1f,
                },
        };

        // A launch: the rears light up, then hook up.
        yield return new Moment
        {
            Name = "wheelspin", Length = 5,
            At = t => t < 1
                ? new State { Throttle = 0.3f }
                : new State { Speed = (float)Math.Min(24, 6 * (t - 1)), Slide = (float)Math.Max(0, 0.85 - 0.35 * (t - 1)), Throttle = 1 },
        };

        // Clipping the apex kerb with two wheels, then running the exit kerb with one.
        yield return new Moment
        {
            Name = "kerb", Length = 5,
            At = t => new State
            {
                Speed = (float)(28 + 2 * t),
                Kerb = t > 1 && t < 2.2 ? 0.5f : t > 2.9 && t < 3.6 ? 0.25f : 0f,
                Slide = t > 1.2 && t < 2.4 ? 0.2f : 0f,
                Throttle = t < 1.6 ? 0.4f : 1f,
            },
        };

        // Off at 100 km/h into the gravel, sliding to a stop.
        yield return new Moment
        {
            Name = "gravel", Length = 7,
            At = t => new State
            {
                Speed = (float)Math.Max(0, t < 1 ? 28 : 28 - 5.5 * (t - 1)),
                Gravel = t < 1 ? 0f : t < 1.15 ? 0.5f : 1f,
                Slide = t < 1 ? 0.3f : 0f,
            },
        };

        // Running wide onto the grass with two wheels, then all four, and back on.
        yield return new Moment
        {
            Name = "grass", Length = 6,
            At = t => new State
            {
                Speed = (float)(t < 1 ? 30 : t < 4 ? 30 - 2.5 * (t - 1) : 22.5 + 2 * (t - 4)),
                Grass = t < 1 ? 0f : t < 1.6 ? 0.5f : t < 3.6 ? 1f : t < 4.2 ? 0.5f : 0f,
                Slide = t > 4.1 && t < 4.9 ? 0.35f : 0f,
                Throttle = t < 1 ? 0.8f : t < 3.6 ? 0f : 0.7f,
            },
        };

        // A lap's worth of it: a straight, a braking zone, a slide through the corner, both kerbs, wheelspin out, wide onto the grass.
        yield return new Moment
        {
            Name = "lap", Length = 13,
            At = t =>
            {
                if (t < 2) return new State { Speed = (float)(40 + 3 * t), Throttle = 1 };
                if (t < 4) return new State { Speed = (float)(46 - 12 * (t - 2)), Slide = 0.38f };
                if (t < 6) return new State { Speed = 22, Slide = t < 5 ? 0.6f : 0.3f, Kerb = t > 4.6 && t < 5.4 ? 0.5f : 0f, Throttle = 0.4f };
                if (t < 8) return new State { Speed = (float)(22 + 4 * (t - 6)), Slide = (float)Math.Max(0, 0.7 - 0.4 * (t - 6)), Kerb = t > 6.8 && t < 7.6 ? 0.5f : 0f, Throttle = 1 };
                if (t < 11) return new State { Speed = (float)(30 - 2 * (t - 8)), Grass = t < 8.4 ? 0.5f : t < 10.4 ? 1f : 0.5f, Throttle = 0 };
                return new State { Speed = (float)(24 + 3 * (t - 11)), Slide = t < 11.8 ? 0.3f : 0f, Throttle = 1 };
            },
        };
    }

    /// <summary>
    /// A minute of random driving at every tyre size must stay finite and within -1 to 1, and a car standing still must
    /// be silent.
    /// </summary>
    private static bool Check()
    {
        var rng = new Random(5);
        var buffer = new float[Block];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int size = 0; size <= 2; size++)
        {
            var synth = new TyreSynth(Rate, size / 2f, size + 1);
            for (int block = 0; block < 60 * Rate / Block; block++)
            {
                if (block % 20 == 0)
                {
                    synth.Speed = (float)rng.NextDouble() * 90f;
                    synth.Slide = rng.NextDouble() < 0.4 ? (float)rng.NextDouble() : 0f;
                    synth.Kerb = rng.Next(5) / 4f;
                    synth.Gravel = rng.Next(5) / 4f;
                    synth.Grass = rng.Next(5) / 4f;
                    synth.Grounded = rng.Next(5) / 4f;
                    synth.Wet = (float)rng.NextDouble();
                }
                synth.Render(buffer, Block);
                foreach (float x in buffer)
                    if (float.IsNaN(x) || Math.Abs(x) > 1f)
                    {
                        Console.WriteLine($"FAIL: tyre size {size / 2f} gave {x}");
                        return false;
                    }
            }
            synth.Speed = 0f;
            synth.Slide = 0f;
            for (int block = 0; block < Rate / Block; block++) synth.Render(buffer, Block);
            if (buffer.Max(Math.Abs) > 1e-3f)
            {
                Console.WriteLine($"FAIL: tyre size {size / 2f} still sounds a second after stopping");
                return false;
            }
        }
        Console.WriteLine($"check passed: finite, within range, silent at rest. One car costs {clock.Elapsed.TotalMilliseconds / 180:0.0} ms of CPU per second of sound.");
        return true;
    }

    private static void Render(Moment m, out float[] tyres, out float[] mixed)
    {
        var tyre = new TyreSynth(Rate, m.TyreSize, 3);
        EngineSound sound = EngineSounds.Tier(CarClass.D);
        var engine = new EngineSynth(sound, Rate, 3);
        var t1 = new List<float>();
        var mix = new List<float>();
        var a = new float[Block];
        var b = new float[Block];
        int gear = 1;
        for (double t = 0; t < m.Length; t += Block / (double)Rate)
        {
            State s = m.At(t);
            tyre.Speed = s.Speed;
            tyre.Slide = s.Slide;
            tyre.Kerb = s.Kerb;
            tyre.Gravel = s.Gravel;
            tyre.Grass = s.Grass;
            tyre.Wet = s.Wet;
            tyre.Render(a, Block);

            // Gears as the engine audition spaces them: each holds 14 m/s more than the last.
            int want = Math.Clamp((int)Math.Ceiling(s.Speed / (14f * 0.95f)), 1, 6);
            if (want > gear) engine.Shift();
            gear = want;
            float fraction = s.Speed / (gear * 14f);
            float rpm = fraction * sound.RedlineRpm;
            if (gear == 1) rpm = Math.Max(rpm, sound.IdleRpm + (0.45f * sound.RedlineRpm - sound.IdleRpm) * s.Throttle);
            engine.Rpm = Math.Max(rpm, sound.IdleRpm);
            engine.Load = s.Throttle;
            engine.Speed = s.Speed;
            engine.Render(b, Block);

            for (int i = 0; i < Block; i++)
            {
                t1.Add(a[i]);
                mix.Add((float)Math.Tanh(a[i] + b[i]));
            }
        }
        tyres = t1.ToArray();
        mixed = mix.ToArray();
    }

    private static double Rms(float[] x) => Math.Sqrt(x.Average(v => (double)v * v));

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
