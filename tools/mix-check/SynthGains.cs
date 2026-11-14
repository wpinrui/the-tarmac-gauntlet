using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TarmacGauntlet.Audio;

/// <summary>
/// The live synths' volume (<see cref="SynthGain"/>) through a race's opening and a pause: the engines, tyres and wet
/// road keep their AudioSource at a volume Unity never drops, and their samples follow the mix to silence and back.
/// </summary>
public static class SynthGains
{
    private const float Dt = 1f / 60f;
    private const int Frames = 735;   // a 60 fps frame's samples at 44.1 kHz

    /// <summary>The components that play a live synth through a carrier source.</summary>
    private static readonly string[] Players =
    {
        "Raceday/Scripts/Audio/EngineAudio.cs",
        "Raceday/Scripts/Audio/TyreAudio.cs",
        "Raceday/Scripts/Audio/WetRoadAudio.cs",
    };

    public static void Run(string assets)
    {
        Sources(assets);
        Ramp();
        Opening();
    }

    /// <summary>Each player sets its source's volume once, to the carrier, and puts its gain on the samples.</summary>
    private static void Sources(string assets)
    {
        Check.Expect("the carrier volume is above 0, so Unity never drops a synth's source", SynthGain.Carrier > 0f);
        foreach (string rel in Players)
        {
            string code = File.ReadAllText(Path.Combine(assets, rel));
            string name = Path.GetFileNameWithoutExtension(rel);
            var sets = Regex.Matches(code, @"\.volume\s*=\s*([^;]+);").Select(m => m.Groups[1].Value.Trim()).ToList();
            Check.Expect($"{name} sets its source's volume only to the carrier", sets.Count == 1 && sets[0] == "SynthGain.Carrier");
            Check.Expect($"{name} puts the mix's gain on its samples", code.Contains("gain.Apply(mono, frames)") && code.Contains("gain.Target = "));
        }
    }

    /// <summary>A change of gain moves in a straight line across one buffer: no click, and the target on its last sample.</summary>
    private static void Ramp()
    {
        var g = new SynthGain { Target = 0.8f };
        float[] a = Ones();
        g.Apply(a, Frames);
        Check.Expect("the first buffer plays at its gain from the start", a.All(x => x == 0.8f));
        g.Target = 0f;
        float[] b = Ones();
        g.Apply(b, Frames);
        float worst = 0f;
        for (int i = 1; i < Frames; i++) worst = Math.Max(worst, Math.Abs(b[i] - b[i - 1]));
        Check.Expect("a cut to silence ramps over one buffer, and lands on 0", b[Frames - 1] == 0f && b[0] < 0.8f && b[0] > 0.79f && worst < 0.8f / Frames * 1.01f);
        g.Target = float.NaN;
        Check.Expect("a gain that isn't a number plays silence, not noise", g.Target == 0f);
    }

    /// <summary>
    /// From the HQ into a race: silent through the load, the crossfade up as it arrives, the intro's stages at full
    /// volume, then a pause and back. The engine's samples follow the mix all the way, and end where they started.
    /// </summary>
    private static void Opening()
    {
        Mix.Settle();
        Mix.FadeRace(1f, 0f);
        float full = Mix.Gain(SoundGroup.Engines);
        var g = new SynthGain();
        float Frame()
        {
            Mix.Tick(Dt, Dt);
            g.Target = Mix.Gain(SoundGroup.Engines);
            float[] s = Ones();
            g.Apply(s, Frames);
            return s[Frames - 1];
        }

        Mix.FadeRace(0f, 0f);   // leaving the HQ: the race is silent under the loading screen
        float loading = 0f;
        for (int i = 0; i < 60; i++) loading = Math.Max(loading, Frame());
        Check.Expect("loading the race, the engines' samples are silent", loading == 0f);

        Mix.FadeRace(1f, Mix.Crossfade);   // the race arrives and crosses over from the HQ's music
        float half = 0f;
        for (int i = 0; i < (int)(Mix.Crossfade / Dt / 2); i++) half = Frame();
        Check.Expect("halfway through the crossfade the engines are on their way up", half > 0.3f * full && half < full);
        float opened = 0f;
        for (int i = 0; i < (int)(Mix.Crossfade / Dt); i++) opened = Frame();
        Check.Expect("after the crossfade the engines play at full volume", Math.Abs(opened - full) < 1e-6f);

        // The preview, the grid show, the drive out of the pits and the formation lap leave the mix alone.
        float least = float.MaxValue;
        for (int i = 0; i < 4 * 60; i++) least = Math.Min(least, Frame());
        Check.Expect("through the intro's stages the engines stay at full volume", Math.Abs(least - full) < 1e-6f);

        Mix.Paused = true;
        float paused = 1f;
        for (int i = 0; i < 30; i++) paused = Frame();
        Check.Expect("paused, the engines' samples cut to silence", paused == 0f);
        Mix.Paused = false;
        float back = 0f;
        for (int i = 0; i < 30; i++) back = Frame();
        Check.Expect("resumed, the engines are back at full volume within half a second", Math.Abs(back - full) < 1e-6f);
        Mix.Settle();
    }

    private static float[] Ones() => Enumerable.Repeat(1f, Frames).ToArray();
}
