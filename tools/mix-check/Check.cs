using System;
using System.Diagnostics;
using System.Linq;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

/// <summary>
/// Checks the mixer's rules (<see cref="Mix"/>, <see cref="RaceVoices"/>) and what Medium's synthesised voices cost the
/// audio thread, then renders the before and after mixes (<see cref="Render"/>).
/// </summary>
public static class Check
{
    private const int Rate = 44100, Block = 512;
    /// <summary>
    /// The most of one CPU core Medium's synths may take. They run on Unity's audio thread, beside the main thread, so
    /// they only cost frames when the CPU runs short of cores; a quarter of one keeps the audio thread three quarters
    /// idle on the Intel Arc B390's laptop, with room for Unity's own mixing.
    /// </summary>
    private const double MediumCpuShare = 0.25;

    private static int fails;

    internal static void Expect(string what, bool ok)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) fails++;
    }

    public static int Main(string[] args)
    {
        string assets = args[0], outDir = args[1];
        Rules();
        SynthGains.Run(assets);
        Budget();
        if (args.Skip(2).Contains("check")) return Done();
        Render.All(assets, outDir);
        return Done();
    }

    private static int Done()
    {
        Console.WriteLine(fails == 0 ? "mix-check: all passed" : $"mix-check: {fails} FAILED");
        return fails == 0 ? 0 : 1;
    }

    private static void Frames(int n, float dt, float realDt)
    {
        for (int i = 0; i < n; i++) Mix.Tick(dt, realDt);
    }

    private static void Rules()
    {
        Mix.Settle();
        Expect("a slider's curve runs 0 to 1, halfway a quarter (-12 dB)", Mix.Taper(0f) == 0f && Mix.Taper(1f) == 1f && Mix.Taper(0.5f) == 0.25f);
        Expect("every group answers to a slider, and the tyres to the cars'", Mix.SliderOf(SoundGroup.Tyres) == VolumeSlider.Cars && Mix.SliderOf(SoundGroup.Engines) == VolumeSlider.Cars);
        Expect("every group plays at or under full volume", Enum.GetValues(typeof(SoundGroup)).Cast<SoundGroup>().All(g => Mix.Gain(g) > 0f && Mix.Gain(g) <= 1f));
        Expect("the music's default plays as loud as its old 70%", Math.Abs(Mix.Gain(SoundGroup.Music) - 0.7f) < 0.03f);
        Mix.SetPosition(VolumeSlider.Effects, 0f);
        Expect("a slider at zero silences its group", Mix.Gain(SoundGroup.Effects) == 0f);
        Mix.SetPosition(VolumeSlider.Effects, 1f);

        // The pause.
        float engines = Mix.Gain(SoundGroup.Engines), music = Mix.Gain(SoundGroup.Music), ui = Mix.Gain(SoundGroup.Ui);
        Expect("open, the muffle lets everything through", Mix.Cutoff >= 20000f);
        Mix.Paused = true;
        Frames(15, 0f, 1f / 60f);
        Expect("paused, the race muffles within a quarter of a second", Mix.Muffle == 1f && Math.Abs(Mix.Cutoff - Mix.PausedCutoff) < 1f);
        Expect("paused, the engines and tyres cut out", Mix.Gain(SoundGroup.Engines) == 0f && Mix.Gain(SoundGroup.Tyres) == 0f);
        Expect("paused, the effects and ambience drop to 40%", new[] { SoundGroup.Effects, SoundGroup.Ambience }
            .All(g => Math.Abs(Mix.Gain(g) - Mix.Trim(g) * Mix.TrackPausedGain) < 1e-5));
        Expect("paused, the music and UI stay as they were", Mix.Gain(SoundGroup.Music) == music && Mix.Gain(SoundGroup.Ui) == ui);
        Mix.Paused = false;
        Frames(10, 1f / 60f, 1f / 60f);
        Expect("resuming, the muffle is still opening a sixth of a second in", Mix.Muffle > 0f && Mix.Muffle < 1f);
        Frames(15, 1f / 60f, 1f / 60f);
        Expect("and fully open by a third of a second", Mix.Muffle == 0f && Mix.Gain(SoundGroup.Engines) == engines);

        // The ducking.
        Mix.Settle();
        for (int i = 0; i < 60; i++) { Mix.Engine(Mix.Closeness(2f, 0f)); Mix.Tick(1f / 60f, 1f / 60f); }
        Expect("a car parked beside the camera ducks nothing, so the start's roar lands", Mix.Duck == 0f);
        for (int i = 0; i < 60; i++) { Mix.Engine(Mix.Closeness(25f, 70f)); Mix.Tick(1f / 60f, 1f / 60f); }
        Expect("a car going by 25 m off ducks nothing", Mix.Duck == 0f);
        for (int i = 0; i < 9; i++) { Mix.Engine(Mix.Closeness(3f, 60f)); Mix.Tick(1f / 60f, 1f / 60f); }
        Expect("a car flashing by 3 m off ducks the crowd and PA in 0.15 s", Mix.Duck == 1f && Math.Abs(Mix.CrowdDuck - (1f - Mix.DuckDepth)) < 1e-5);
        Mix.Engine(Mix.Closeness(3f, 60f));
        Mix.Paused = true;
        Frames(120, 0f, 1f / 60f);
        Expect("the ducking holds still under the pause", Mix.Duck == 1f);
        Mix.Paused = false;
        Frames(30, 1f / 60f, 1f / 60f);
        Expect("once the car has gone the crowd comes back slowly, still dipped half a second on", Mix.Duck > 0.5f && Mix.Duck < 1f);
        Frames(60, 1f / 60f, 1f / 60f);
        Expect("and is back after 1.2 s", Mix.Duck == 0f && Mix.CrowdDuck == 1f);
        Mix.Engine(1f);
        Mix.Paused = true;
        Frames(5, 1f / 60f, 1f / 60f);
        Mix.Settle();
        Expect("a new scene starts unducked and open", Mix.Duck == 0f && Mix.Muffle == 0f && !Mix.Paused);
    }

    private static void Budget()
    {
        for (int level = 0; level < 4; level++)
        {
            Console.WriteLine($"     level {level}: {RaceVoices.Total(level)} race voices of {Mix.RealVoices}, {RaceVoices.Synthesised(level)} of them synthesised");
            Expect($"level {level} fits Unity's real voices with the music and UI's kept free", RaceVoices.Fits(level));
            if (level > 0)
                Expect($"level {level} plays at least what the level below does", RaceVoices.Total(level) >= RaceVoices.Total(level - 1)
                    && RaceVoices.EnginesAt(level) >= RaceVoices.EnginesAt(level - 1) && RaceVoices.TyresAt(level) >= RaceVoices.TyresAt(level - 1));
        }

        // What each synth costs, flat out, on this machine's one core: ms of CPU per second of sound.
        double engine = Cost(() => { var s = new EngineSynth(EngineSounds.Tier(CarClass.A), Rate); s.Rpm = 9000f; s.Load = 1f; s.Speed = 60f; return n => s.Render(Buffer, n); });
        double tyre = Cost(() => { var s = new TyreSynth(Rate, 0.5f); s.Slide = 0.6f; s.Speed = 40f; s.Kerb = 0.5f; return n => s.Render(Buffer, n); });
        double wet = Cost(() => { var s = new WetRoadSynth(Rate); s.Speed = 50f; s.Wet = 1f; s.Water = 1f; return n => s.Render(Buffer, n); });
        double wind = Cost(() => { var s = new WindSynth(Rate); s.Speed = 60f; return n => s.Render(Buffer, n, 2); });
        const int level1 = 1;
        AmbienceBudget b = AmbienceMix.Budget(level1);
        double medium = RaceVoices.EnginesAt(level1) * engine + RaceVoices.TyresAt(level1) * tyre + b.WetCars * wet + wind;
        Console.WriteLine($"     one engine {engine:0.0} ms a second, tyres {tyre:0.0}, wet road {wet:0.0}, wind {wind:0.0}");
        // Before the one budget, every level played ten engines.
        double before = 10 * engine + RaceVoices.TyresAt(level1) * tyre + b.WetCars * wet + wind;
        Console.WriteLine($"     Medium's synths before the budget took {before / 10:0.0}% of one core");
        Expect($"Medium's synths take {medium / 10:0.0}% of one core, under {MediumCpuShare:P0}", medium / 1000 < MediumCpuShare);
    }

    private static readonly float[] Buffer = new float[2 * Block];

    private static double Cost(Func<Action<int>> make)
    {
        Action<int> render = make();
        for (int i = 0; i < 200; i++) render(Block);   // warm up
        var watch = Stopwatch.StartNew();
        int blocks = 10 * Rate / Block;
        for (int i = 0; i < blocks; i++) render(Block);
        return watch.Elapsed.TotalMilliseconds / 10;
    }
}
