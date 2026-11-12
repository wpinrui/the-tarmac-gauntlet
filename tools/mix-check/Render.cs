using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

/// <summary>
/// Renders a race lap and a stretch of the menus twice, as the game played them before the mixer and as it plays them
/// now, from the game's own synths, clips and mix rules. The lap is a D-class car seen from the chase camera: away past
/// the main grandstand as the PA talks, three corners with their slides and kerbs, a rival going by close, a brush with
/// the armco, and five seconds in the pause menu. The menus are the music under a minute of clicking about. Each render
/// is a mono WAV in the out folder, and the after lap's groups are measured against each other.
/// </summary>
public static class Render
{
    private const int Rate = 44100, Block = 512;
    private const float Dt = Block / (float)Rate;

    private static string assets;

    public static void All(string assetsDir, string outDir)
    {
        assets = assetsDir;
        foreach (bool after in new[] { false, true })
        {
            string tag = after ? "after" : "before";
            float[] lap = Lap(after, out Dictionary<string, float[]> stems);
            Write(Path.Combine(outDir, $"lap-{tag}.wav"), lap);
            Console.WriteLine($"     lap {tag}: peak {Db(Peak(lap)):0.0} dBFS, loudest 400 ms {Db(Loudest(lap)):0.0} dBFS");
            float[] menus = Menus(after);
            Write(Path.Combine(outDir, $"menus-{tag}.wav"), menus);
            Console.WriteLine($"     menus {tag}: peak {Db(Peak(menus)):0.0} dBFS, loudest 400 ms {Db(Loudest(menus)):0.0} dBFS");
            float engine = Db(Loudest(stems["engine"]));
            foreach (var kv in stems) Console.WriteLine($"       {kv.Key,-8} loudest 400 ms {Db(Loudest(kv.Value)) - engine:+0.0;-0.0} dB against the engine");
            if (!after) continue;
            Check.Expect("after: the tyres' loudest moment sits 8 dB or more under the engine's", Db(Loudest(stems["tyres"])) <= engine - 8f);
            Check.Expect("after: the crowd and PA sit under the engine", Db(Loudest(stems["crowd"])) < engine);
            Check.Expect("after: the wind sits 6 dB or more under the engine", Db(Loudest(stems["wind"])) <= engine - 6f);
            Check.Expect("after: the lap never clips", Peak(lap) < 1f);
            Check.Expect("after: the pause plays the race on, muffled", Loudest(Slice(lap, 33f, 36f)) > 0.005f);
        }
    }

    // ---------------------------------------------------------------- the lap

    /// <summary>The car's speed through the lap in game seconds, m/s: each pair is a time and the speed then.</summary>
    private static readonly (float t, float v)[] Speeds =
    {
        (0f, 30f), (8f, 66f), (9.5f, 26f), (11f, 28f), (17f, 58f), (18.2f, 32f), (19.5f, 34f), (26f, 62f), (26.8f, 50f),
        (28f, 52f), (32f, 64f), (36f, 70f),
    };
    /// <summary>Top of each gear, m/s.</summary>
    private static readonly float[] GearTops = { 22f, 33f, 44f, 55f, 66f, 77f };
    private const float PauseFrom = 32f, PauseTo = 37f, Length = 40f;
    /// <summary>The chase camera sits this far behind the car, m.</summary>
    private const float Chase = 7f;

    private static float[] Lap(bool after, out Dictionary<string, float[]> stems)
    {
        int n = (int)(Length * Rate);
        var mix = new float[n];
        stems = new Dictionary<string, float[]>();
        foreach (string s in new[] { "engine", "rival", "tyres", "wind", "crowd", "country", "effects" }) stems[s] = new float[n];

        EngineSound sound = EngineSounds.Tier(CarClass.D);
        var engine = new EngineSynth(sound, Rate, 1);
        var rivalEngine = new EngineSynth(sound, Rate, 2);
        var tyres = new TyreSynth(Rate, 0.4f, 1);
        var wind = new WindSynth(Rate, 7);
        float[] calm = Clip("Raceday/Resources/Ambience/crowd-calm.ogg"), lively = Clip("Raceday/Resources/Ambience/crowd-lively.ogg");
        float[] country = Clip("Raceday/Resources/Ambience/country-day.ogg"), pa = Clip("Raceday/Resources/Ambience/pa-1.ogg");
        float[] hit = Clip("Raceday/Resources/RaceSfx/hit-light/hit-light-2.ogg"), scrape = Clip("Raceday/Resources/RaceSfx/scrape/scrape-1.ogg");
        var buf = new float[Block];
        var air = new float[2 * Block];
        var low = new Biquad();
        Mix.Settle();

        float g = 0f;   // game time: it stops under the pause
        int gear = 0, played = 0;   // samples each clip has played: they hold still under the old pause
        float calmLevel = 0f, livelyLevel = 0f, countryLevel = 0f, excitement = 0.7f, scrapeGain = 0f;
        int hitAt = -1, paAt = -1;
        for (int at = 0; at < n; at += Block)
        {
            float t = at / (float)Rate;
            bool paused = t >= PauseFrom && t < PauseTo;
            int frames = Math.Min(Block, n - at);
            // The old pause stopped every sound dead.
            if (paused && !after) continue;
            Mix.Paused = after && paused;
            float dt = paused ? 0f : Dt;

            // The car.
            float v = SpeedAt(g), dv = SpeedAt(g + 0.05f) - v;
            int want = GearFor(v);
            if (want > gear) engine.Shift();
            gear = want;
            engine.Rpm = Math.Max(sound.IdleRpm, sound.RedlineRpm * v / GearTops[gear]);
            engine.Load = dv > 0f ? 1f : 0f;
            engine.Speed = v;
            tyres.Speed = v;
            tyres.Slide = In(g, 9f, 11.2f) ? 0.45f : In(g, 17.8f, 19.5f) ? 0.35f : In(g, 26.5f, 27.2f) ? 0.6f : 0.04f;
            tyres.Kerb = In(g, 10.6f, 11.2f) || In(g, 19f, 19.4f) ? 0.5f : 0f;
            wind.Speed = v;
            wind.Exposure = AmbienceMix.Exposure(false, true);
            wind.Breeze = 0.5f;

            // A rival closes from 30 m behind and goes by 3 m off to the side, then pulls away.
            float x = g < 17f ? -30f : -30f + 9f * (g - 17f);
            float rivalMetres = (float)Math.Sqrt(x * x + 9f), rivalSpeed = g < 17f ? v : v + 9f;
            rivalEngine.Rpm = Math.Max(sound.IdleRpm, sound.RedlineRpm * rivalSpeed / GearTops[GearFor(rivalSpeed)]);
            rivalEngine.Load = 1f;
            rivalEngine.Speed = rivalSpeed;

            // The game's mix: the nearest engine tells it how close it is.
            bool rivalNearest = rivalMetres < Chase;
            Mix.Engine(Mix.Closeness(rivalNearest ? rivalMetres : Chase, rivalNearest ? rivalSpeed : v));
            Mix.Tick(dt, Dt);
            float eng = after ? Mix.Gain(SoundGroup.Engines) : 1f, tyr = after ? Mix.Gain(SoundGroup.Tyres) : 1f;
            float amb = after ? Mix.Gain(SoundGroup.Ambience) : 1f, crowdGain = after ? amb * Mix.CrowdDuck : 1f;
            float fx = after ? Mix.Gain(SoundGroup.Effects) : 1f;
            float windLevel = after ? 0.4f : 1f;

            // The main grandstand is beside the start of the lap; the camera leaves it after five seconds.
            float stand = g < 5f ? 22f : g < 12f ? 22f + (g - 5f) / 7f * 378f : 400f;
            excitement = g < 6f ? 0.7f : Math.Max(0f, excitement - 0.35f * dt);
            (float c, float l) = AmbienceMix.Crowd(excitement, 0f, 0f);
            calmLevel = Glide(calmLevel, c * crowdGain);
            livelyLevel = Glide(livelyLevel, l * 0.9f * crowdGain);
            countryLevel = Glide(countryLevel, 0.7f * AmbienceMix.Country(AmbienceMix.Away(stand), 0f, 0f, 2).day * amb);
            float standHeard = Heard(stand, 30f), paHeard = Heard(stand, 35f);
            if (paAt < 0 && g >= 3f) paAt = played;
            if (hitAt < 0 && g >= 26.5f) hitAt = played;
            bool scraping = In(g, 26.5f, 28f);
            scrapeGain = Math.Clamp(scrapeGain + (scraping ? 1f : -1f) * Dt / 0.12f, 0f, 1f);

            float engineHeard = Heard(Chase, 6f), tyreHeard = Heard(Chase, 5f), rivalHeard = rivalMetres > 350f ? 0f : Heard(rivalMetres, 6f);
            float hitHeard = Heard(Chase, 8f), scrapeHeard = Heard(Chase, 6f);
            for (int k = 0; k < frames; k++)
            {
                int i = at + k, p = played + k;
                stems["crowd"][i] = (Loop(calm, p) * calmLevel + Loop(lively, p + 7919) * livelyLevel) * standHeard
                    + (paAt >= 0 && p - paAt < pa.Length ? pa[p - paAt] * 0.9f * crowdGain * paHeard : 0f);
                stems["country"][i] = Loop(country, p) * countryLevel;
                stems["effects"][i] = (hitAt >= 0 && p - hitAt < hit.Length ? hit[p - hitAt] * 0.6f * fx * hitHeard : 0f)
                    + Loop(scrape, p) * 0.45f * scrapeGain * fx * scrapeHeard;
            }
            engine.Render(buf, frames);
            for (int k = 0; k < frames; k++) stems["engine"][at + k] = buf[k] * eng * engineHeard;
            rivalEngine.Render(buf, frames);
            for (int k = 0; k < frames; k++) stems["rival"][at + k] = buf[k] * eng * rivalHeard;
            tyres.Render(buf, frames);
            for (int k = 0; k < frames; k++) stems["tyres"][at + k] = buf[k] * tyr * tyreHeard;
            wind.Render(air, frames, 2);
            for (int k = 0; k < frames; k++) stems["wind"][at + k] = 0.5f * (air[2 * k] + air[2 * k + 1]) * windLevel * amb;

            low.Set(Mix.Cutoff);
            bool muffled = after && Mix.Muffle > 0.001f;
            for (int k = 0; k < frames; k++)
            {
                int i = at + k;
                float sum = 0f;
                foreach (float[] s in stems.Values) sum += s[i];
                mix[i] = muffled ? low.Run(sum) : sum;
            }
            played += frames;
            g += dt;
        }
        stems.Remove("country");
        return mix;
    }

    private static float SpeedAt(float t)
    {
        if (t <= Speeds[0].t) return Speeds[0].v;
        for (int i = 1; i < Speeds.Length; i++)
            if (t <= Speeds[i].t)
                return Speeds[i - 1].v + (Speeds[i].v - Speeds[i - 1].v) * (t - Speeds[i - 1].t) / (Speeds[i].t - Speeds[i - 1].t);
        return Speeds[Speeds.Length - 1].v;
    }

    /// <summary>The gear for a speed: up a gear just short of the top of each.</summary>
    private static int GearFor(float v)
    {
        int gear = 0;
        while (gear < GearTops.Length - 1 && v > GearTops[gear] * 0.97f) gear++;
        return gear;
    }

    private static bool In(float t, float from, float to) => t >= from && t < to;

    /// <summary>Unity's logarithmic rolloff: full inside the near distance, halving with each doubling past it.</summary>
    private static float Heard(float metres, float near) => metres <= near ? 1f : near / metres;

    /// <summary>The ambience's glide toward a level: across the whole range in 0.6 s, as the game's.</summary>
    private static float Glide(float level, float target)
    {
        float step = Dt / 0.6f;
        return Math.Abs(target - level) <= step ? target : level + Math.Sign(target - level) * step;
    }

    private static float Loop(float[] clip, int p) => clip[p % clip.Length];

    // ---------------------------------------------------------------- the menus

    private static float[] Menus(bool after)
    {
        const float length = 30f;
        int n = (int)(length * Rate);
        var mix = new float[n];
        float[] music = Clip("Management/Resources/Music/chill-wave.ogg");
        int from = 60 * Rate;
        float level = after ? Mix.Gain(SoundGroup.Music) : 0.7f, ui = after ? Mix.Gain(SoundGroup.Ui) : 1f;
        for (int i = 0; i < n; i++) mix[i] = music[(from + i) % music.Length] * level * Math.Min(1f, i / (2f * Rate));

        // A player looking round the HQ: hovering, opening settings, sliding the volume, ticking a switch, hiring,
        // buying and going racing.
        var cues = new List<(float t, string clip)>
        {
            (2f, "hover"), (3.2f, "hover"), (3.5f, "click"), (4.4f, "panel"), (5.8f, "hover"), (6.4f, "step"), (7.5f, "toggle-on"),
            (9f, "toggle-off"), (12.2f, "dropdown-open"), (13.4f, "dropdown-close"), (14.6f, "back"), (15.8f, "hover"),
            (16.4f, "confirm"), (18.6f, "hire"), (21f, "hover"), (21.6f, "buy"), (23.4f, "error"), (25f, "hover"), (25.6f, "race"),
            (27f, "go"),
        };
        for (float t = 10f; t < 11.6f; t += 0.08f) cues.Add((t, "slider"));
        var clips = new Dictionary<string, float[]>();
        foreach (var (t, name) in cues)
        {
            if (!clips.TryGetValue(name, out float[] c)) clips[name] = c = Clip($"Management/Resources/UiSounds/{name}.wav");
            int start = (int)(t * Rate);
            for (int k = 0; k < c.Length && start + k < n; k++) mix[start + k] += c[k] * ui;
        }
        return mix;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A game clip, decoded by ffmpeg to mono at the mix's rate.</summary>
    private static float[] Clip(string path)
    {
        var info = new ProcessStartInfo("ffmpeg", $"-v quiet -i \"{Path.Combine(assets, path)}\" -ac 1 -ar {Rate} -f f32le -")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using Process p = Process.Start(info);
        using var bytes = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(bytes);
        p.WaitForExit();
        byte[] b = bytes.ToArray();
        var samples = new float[b.Length / 4];
        System.Buffer.BlockCopy(b, 0, samples, 0, samples.Length * 4);
        if (samples.Length == 0) throw new InvalidDataException("could not read " + path);
        return samples;
    }

    private static float[] Slice(float[] s, float from, float to) => s.Skip((int)(from * Rate)).Take((int)((to - from) * Rate)).ToArray();

    private static float Peak(float[] s) => s.Max(Math.Abs);

    /// <summary>The loudest 400 ms, RMS: how loud a sound feels at its loudest, where a peak only says how high it reaches.</summary>
    private static float Loudest(float[] s)
    {
        int w = (int)(0.4f * Rate), hop = w / 4;
        double best = 0;
        for (int i = 0; i + w <= s.Length; i += hop)
        {
            double sum = 0;
            for (int k = i; k < i + w; k++) sum += (double)s[k] * s[k];
            best = Math.Max(best, sum / w);
        }
        return (float)Math.Sqrt(best);
    }

    private static float Db(float x) => 20f * (float)Math.Log10(Math.Max(x, 1e-9f));

    private static void Write(string path, float[] samples)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (float x in samples) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767f));
    }

    /// <summary>A two-pole low-pass, as Unity's on the listener.</summary>
    private sealed class Biquad
    {
        private float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void Set(float cutoff)
        {
            double w = 2 * Math.PI * Math.Min(cutoff, 0.45f * Rate) / Rate, alpha = Math.Sin(w) / (2 * 1.0), cos = Math.Cos(w), a0 = 1 + alpha;
            b0 = (float)((1 - cos) / 2 / a0); b1 = (float)((1 - cos) / a0); b2 = b0;
            a1 = (float)(-2 * cos / a0); a2 = (float)((1 - alpha) / a0);
        }

        public float Run(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }
}
