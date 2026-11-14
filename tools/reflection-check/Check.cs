// Drives the cars' reflection probes' plan (ProbePlan, as CarReflections runs it) round the lap with the full field, at
// 60 frames a second: thirty cars bunching in the slow parts and passing each other, watched from the bonnet and from
// the chase camera, at High and at Ultra. A probe drawn a face a frame takes 14 frames, a whole one the frame it starts.
// What each car shows is its share of its own probe, the rest the shared probe's: the two can differ wholly, so a jump
// in the share is a jump in the paint.
//
// Rules, for each camera and level:
//   watched car keeps its probe  from its first drawing on, the watched car shows its own probe alone, every frame
//   no jumps                     no car's share changes by more than a frame's worth of ProbePlan.Fade
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    const int Cars = 30, Watched = 2, Fps = 60, Laps = 3;
    /// <summary>Frames a probe drawn a face a frame takes: six faces, then its blur.</summary>
    const int SlicedFrames = 14;
    static bool failed;

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var c = new Circuit(data);
        string csv = args.Length > 1 ? args[1] : null;
        foreach ((string level, int slots, int busy) in new[] { ("High", 3, 1), ("Ultra", 5, 2) })
            foreach (bool bonnet in new[] { true, false })
                Run(c, level, slots, busy, bonnet, bonnet && level == "Ultra" ? csv : null);
        return failed ? 1 : 0;
    }

    static void Report(string rule, List<string> bad, string ok)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {ok}");
        else
        {
            failed = true;
            Console.WriteLine($"FAIL  {rule}: {bad.Count} failures, {string.Join("; ", bad.Take(5))}");
        }
    }

    /// <summary>The field: each car's distance round the lap, its line across the road and how fast it goes for where it is.</summary>
    sealed class Field
    {
        readonly Circuit c;
        public readonly float[] Distance = new float[Cars], Line = new float[Cars], Pace = new float[Cars];
        readonly Random rng = new Random(7);

        public Field(Circuit c)
        {
            this.c = c;
            for (int i = 0; i < Cars; i++)
            {
                Distance[i] = c.Lap - 8f * i;
                Line[i] = i % 2 == 0 ? -0.4f : 0.4f;
                Pace[i] = 0.96f + 0.08f * (float)rng.NextDouble();
            }
        }

        /// <summary>Slow in the corners and quick on the straights: where the cars bunch up and spread out again.</summary>
        float Speed(int i)
        {
            float d = Mathf.Repeat(Distance[i], c.Lap);
            int k = c.IndexOf(d), ahead = c.IndexOf(d + 40f);
            float turn = Vector3.Angle(c.Forward[k], c.Forward[ahead]);
            return Pace[i] * Mathf.Lerp(65f, 22f, Mathf.Clamp01(turn / 60f));
        }

        public void Step(float dt)
        {
            for (int i = 0; i < Cars; i++)
            {
                Distance[i] += Speed(i) * dt;
                Line[i] = Mathf.Clamp(Line[i] + (float)(rng.NextDouble() - 0.5) * dt, -0.7f, 0.7f);
            }
        }

        public Vector3 At(int i)
        {
            int k = c.IndexOf(Mathf.Repeat(Distance[i], c.Lap));
            return c.Surface(k, Line[i] * c.HalfWidth(k));
        }

        public Vector3 Forward(int i) => c.Forward[c.IndexOf(Mathf.Repeat(Distance[i], c.Lap))];
    }

    static void Run(Circuit c, string level, int slots, int busy, bool bonnet, string csv)
    {
        var field = new Field(c);
        var plan = new ProbePlan(slots);
        var places = new List<Vector3?>(new Vector3?[Cars]);
        var draws = new List<(int slot, bool whole)>();
        var done = new int[slots];
        float[] share = new float[Cars], last = new float[Cars];
        float dt = 1f / Fps, most = dt / ProbePlan.Fade + 1e-4f;
        var lost = new List<string>();
        var jumps = new List<string>();
        float biggest = 0f;
        int handovers = 0, frames = (int)(Laps * c.Lap / 45f * Fps);
        int[] holder = Enumerable.Repeat(-1, slots).ToArray();
        bool firstDrawn = false;
        TextWriter rows = csv != null ? new StreamWriter(csv) : null;
        rows?.WriteLine("frame,watched share,cars sharing probes");
        for (int f = 0; f < frames; f++)
        {
            field.Step(dt);
            for (int i = 0; i < Cars; i++) places[i] = field.At(i);
            Vector3 fwd = field.Forward(Watched), at = field.At(Watched);
            Vector3 eye = bonnet ? at + fwd * 0.3f + Vector3.up * 1.2f : at - fwd * 6f + Vector3.up * 2.5f;
            plan.Step(dt, f, eye, fwd, places, Watched, busy, s => f >= done[s], draws);
            foreach ((int s, bool whole) in draws) done[s] = f + (whole ? 1 : SlicedFrames);
            for (int s = 0; s < slots; s++)
            {
                if (plan.Slots[s].Car != holder[s] && plan.Slots[s].Car >= 0) handovers++;
                holder[s] = plan.Slots[s].Car;
            }
            Array.Clear(share, 0, Cars);
            foreach (ProbePlan.Slot s in plan.Slots)
                if (s.Car >= 0) share[s.Car] = Math.Max(share[s.Car], s.Weight);
            if (share[Watched] >= 1f) firstDrawn = true;
            else if (firstDrawn) lost.Add($"frame {f}, {share[Watched]:0.00}");
            for (int i = 0; i < Cars; i++)
            {
                float jump = Math.Abs(share[i] - last[i]);
                biggest = Math.Max(biggest, i == Watched && f < 2 ? 0f : jump);
                if (jump > most && !(i == Watched && f < 2)) jumps.Add($"car {i + 1} by {jump:0.00} at frame {f}");
                last[i] = share[i];
            }
            rows?.WriteLine($"{f},{share[Watched]:0.000},{share.Count(x => x > 0f)}");
        }
        rows?.Dispose();
        string view = bonnet ? "bonnet" : "chase";
        Report($"{level}, {view}: watched car keeps its probe", lost, $"{frames} frames over {Laps} laps, always its own");
        Report($"{level}, {view}: no jumps", jumps, $"{handovers} handovers, the largest change in a frame {biggest:0.000}, at most {most:0.000}");
    }
}
