using System;

namespace TarmacGauntlet.Audio
{
    /// <summary>The groups every sound in the game plays in.</summary>
    public enum SoundGroup
    {
        /// <summary>The cars' engines.</summary>
        Engines,
        /// <summary>The tyres on what they roll over, and their hiss on a wet road.</summary>
        Tyres,
        /// <summary>The pit stops and the hits and scrapes.</summary>
        Effects,
        /// <summary>The crowd, the PA, the rain and thunder, the countryside and the wind.</summary>
        Ambience,
        /// <summary>The HQ's music.</summary>
        Music,
        /// <summary>The menus' sounds.</summary>
        Ui,
    }

    /// <summary>The volume sliders in the settings. Each group answers to one of them, and all of them to Master.</summary>
    public enum VolumeSlider { Master, Music, Cars, Effects, Ambience, Ui }

    /// <summary>
    /// The game's mixer, as plain arithmetic: how loud each group plays from its slider, its level from the loudness pass,
    /// the race's ducking and the pause. <see cref="SoundMixer"/> ticks it and applies the parts Unity owns (the master
    /// volume, the muffle on the listener, the voice count); every source multiplies its own volume by <see cref="Gain"/>.
    /// No Unity in here, so the tools can check it and render the mix with it.
    /// </summary>
    public static class Mix
    {
        /// <summary>Unity's real voices: the most that play at once. Every quality level's race voices fit inside it
        /// with <see cref="ReservedVoices"/> to spare.</summary>
        public const int RealVoices = 64;
        /// <summary>Voices kept for the music and the two UI players, which play over a race's pause.</summary>
        public const int ReservedVoices = 3;

        /// <summary>
        /// Each group's level from the loudness pass, 0 to 1, measured against a car's engine at full volume (its
        /// loudest 400 ms sit near -12 dBFS). Tyres sit 13 dB under it, a little further than before, so a squeal never
        /// shouts over the car. The ambience sits 3 dB down, and its sources trim themselves further (wind, thunder). The effects sit 2 dB
        /// down so a hit still cuts through. At its default slider the music plays where it did before the mixer, and
        /// the UI's loudest cues (Go, Race, Hire) sit 3 dB under the music's loudest moments.
        /// </summary>
        public static float Trim(SoundGroup group) => group switch
        {
            SoundGroup.Engines => 0.8f,
            SoundGroup.Tyres => 0.75f,
            SoundGroup.Effects => 0.8f,
            SoundGroup.Ambience => 0.7f,
            SoundGroup.Music => 1f,
            _ => 0.7f,
        };

        /// <summary>The slider a group answers to: tyres go with the engines, as the car.</summary>
        public static VolumeSlider SliderOf(SoundGroup group) => group switch
        {
            SoundGroup.Engines => VolumeSlider.Cars,
            SoundGroup.Tyres => VolumeSlider.Cars,
            SoundGroup.Effects => VolumeSlider.Effects,
            SoundGroup.Ambience => VolumeSlider.Ambience,
            SoundGroup.Music => VolumeSlider.Music,
            _ => VolumeSlider.Ui,
        };

        /// <summary>Whether a group is the race's, which the pause muffles and dims.</summary>
        public static bool InRace(SoundGroup group) => group <= SoundGroup.Ambience;

        /// <summary>Where each slider starts on a fresh game. The music's 85% plays it at 0.72, as loud as its old 70%.</summary>
        public static float Default(VolumeSlider slider) => slider == VolumeSlider.Music ? 0.85f : 1f;

        /// <summary>
        /// A slider's position, 0 to 1, as a gain: squared, so halfway sounds about half as loud (-12 dB) rather than
        /// hardly quieter, as a straight line would.
        /// </summary>
        public static float Taper(float position)
        {
            float p = Clamp01(position);
            return p * p;
        }

        private static readonly float[] positions = Defaults();

        private static float[] Defaults()
        {
            var p = new float[Enum.GetValues(typeof(VolumeSlider)).Length];
            for (int i = 0; i < p.Length; i++) p[i] = Default((VolumeSlider)i);
            return p;
        }

        /// <summary>A slider's position, 0 to 1. The settings save it; this holds it for the mix.</summary>
        public static float Position(VolumeSlider slider) => positions[(int)slider];

        public static void SetPosition(VolumeSlider slider, float position) => positions[(int)slider] = Clamp01(position);

        /// <summary>The master volume, as a gain for the listener.</summary>
        public static float Master => Taper(Position(VolumeSlider.Master));

        // ---------------------------------------------------------------- pause

        /// <summary>How long the muffle takes to close as the race pauses and to open as it resumes, real seconds.</summary>
        public const float PauseIn = 0.25f, PauseOut = 0.35f;
        /// <summary>The track's sound under the pause, as a gain (-8 dB), and the muffle's low-pass then, Hz.</summary>
        public const float TrackPausedGain = 0.4f, PausedCutoff = 700f;

        /// <summary>A race group's gain under the pause: the cars (engines and tyres) cut to silence, as the race has
        /// stopped dead; the effects and the track's ambience play on muffled and low behind the menu.</summary>
        public static float PausedGain(SoundGroup group) =>
            group == SoundGroup.Engines || group == SoundGroup.Tyres ? 0f : TrackPausedGain;
        /// <summary>The low-pass with the muffle open: above hearing, so it does nothing.</summary>
        public const float OpenCutoff = 22000f;

        /// <summary>Whether the race is paused behind its menu.</summary>
        public static bool Paused { get; set; }

        /// <summary>How far the pause's muffle has closed, 0 open to 1 closed.</summary>
        public static float Muffle { get; private set; }

        /// <summary>The listener's low-pass for the muffle, Hz: it falls on a log scale, so it sounds even.</summary>
        public static float Cutoff => (float)(OpenCutoff * Math.Pow(PausedCutoff / OpenCutoff, Smooth(Muffle)));

        // ---------------------------------------------------------------- ducking

        /// <summary>How far the crowd and PA dip under a close engine pass, as a share (-6 dB).</summary>
        public const float DuckDepth = 0.5f;
        /// <summary>How fast the dip comes in and lets go, game seconds: in with the car, out after it has gone.</summary>
        public const float DuckAttack = 0.15f, DuckRelease = 1.2f;

        private static float closest, duck;

        /// <summary>How far the crowd and PA have dipped, 0 to 1 of <see cref="DuckDepth"/>.</summary>
        public static float Duck => duck;

        /// <summary>The crowd's and PA's share under the ducking, on top of <see cref="Gain"/>.</summary>
        public static float CrowdDuck => 1f - DuckDepth * Smooth(duck);

        /// <summary>
        /// How much an engine fills the listener's ear, 0 to 1: its level under the engines' rolloff (full inside 6 m,
        /// halving with each doubling past it), only while the car is moving. A car parked on the grid ducks nothing,
        /// so the start's roar still lands.
        /// </summary>
        public static float Closeness(float metres, float speed)
        {
            float heard = metres <= 6f ? 1f : 6f / metres;
            return heard * Clamp01((speed - 5f) / 25f);
        }

        /// <summary>An engine playing this frame, as close as <see cref="Closeness"/> says. The closest one wins.</summary>
        public static void Engine(float closeness)
        {
            if (closeness > closest) closest = closeness;
        }

        /// <summary>
        /// Moves the ducking and the muffle on a frame. The ducking moves in game time, so it holds still under the
        /// pause; the muffle in real time. The engines report again each frame.
        /// </summary>
        public static void Tick(float dt, float realDt)
        {
            // A moving car within 6 m ducks fully, and one 15 m off or further not at all.
            float target = Clamp01((closest - 0.4f) / 0.6f);
            closest = 0f;
            float rate = target > duck ? 1f / DuckAttack : 1f / DuckRelease;
            duck = MoveTowards(duck, target, rate * Math.Max(0f, dt));
            Muffle = MoveTowards(Muffle, Paused ? 1f : 0f, Math.Max(0f, realDt) / (Paused ? PauseIn : PauseOut));
            race = MoveTowards(race, raceTarget, raceRate * Math.Max(0f, realDt));
        }

        /// <summary>Back to no ducking and no muffle, as a new scene opens.</summary>
        public static void Settle()
        {
            Paused = false;
            Muffle = 0f;
            duck = 0f;
            closest = 0f;
        }

        // ---------------------------------------------------------------- scene changes

        /// <summary>How long the HQ's music and the race's sound take to cross over each other, real seconds.</summary>
        public const float Crossfade = 2.5f;

        private static float race = 1f, raceTarget = 1f, raceRate;

        /// <summary>How far the race's groups are faded in, 0 to 1, for the crossfade with the HQ's music.</summary>
        public static float RaceLevel => race;

        /// <summary>Fades the race's groups to a level over some real seconds; 0 seconds sets it at once.</summary>
        public static void FadeRace(float target, float seconds)
        {
            raceTarget = Clamp01(target);
            if (seconds <= 0f) race = raceTarget;
            raceRate = seconds <= 0f ? 0f : 1f / seconds;
        }

        /// <summary>One side of the crossfade: a fade's share, 0 to 1, as a gain that sums with the other side to a steady
        /// loudness (equal power), so the change never dips in the middle.</summary>
        public static float CrossGain(float share)
        {
            float p = Clamp01(share);
            return (float)Math.Sin(p * Math.PI / 2);
        }

        // ---------------------------------------------------------------- the gain

        /// <summary>
        /// A group's volume now, 0 to 1, under the master volume: its slider, its loudness-pass level, and for the race's
        /// groups the pause (<see cref="PausedGain(SoundGroup)"/>) and the crossfade with the HQ's music (<see cref="RaceLevel"/>). The crowd and PA take <see cref="CrowdDuck"/> on top.
        /// </summary>
        public static float Gain(SoundGroup group)
        {
            float g = Taper(Position(SliderOf(group))) * Trim(group);
            if (InRace(group)) g *= (1f - (1f - PausedGain(group)) * Smooth(Muffle)) * CrossGain(race);
            return g;
        }

        private static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
