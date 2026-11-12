using TarmacGauntlet.Audio;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race's one voice budget at each quality level, Low to Ultra: the engines, the tyres, the recorded sounds
    /// (<see cref="SfxVoices"/>) and the ambience (<see cref="AmbienceBudget"/>), all inside Unity's real voices with
    /// room left for the music and the UI. The synthesised voices (engines, tyres, wet road, wind) cost the most, as each
    /// runs its synth on the audio thread; Medium's are sized for the Intel Arc B390's CPU to hold 60 fps. No Unity in
    /// here, so the tools can check it.
    /// </summary>
    public static class RaceVoices
    {
        /// <summary>Cars whose engines play, the nearest first, Low to Ultra.</summary>
        public static readonly int[] Engines = { 6, 8, 10, 12 };
        /// <summary>Cars whose tyres play, the nearest first, Low to Ultra.</summary>
        public static readonly int[] Tyres = { 2, 4, 6, 8 };

        public static int EnginesAt(int level) => Engines[Clamp(level, Engines.Length)];
        public static int TyresAt(int level) => Tyres[Clamp(level, Tyres.Length)];

        /// <summary>Every race voice a level can play at once.</summary>
        public static int Total(int level) =>
            EnginesAt(level) + TyresAt(level) + SfxVoices.OneShotsAt(level) + SfxVoices.LoopsAt(level) + AmbienceMix.Budget(level).Voices;

        /// <summary>The voices that run a synth on the audio thread: engines, tyres, wet road and the wind.</summary>
        public static int Synthesised(int level) => EnginesAt(level) + TyresAt(level) + AmbienceMix.Budget(level).WetCars + 1;

        /// <summary>Whether a level fits inside Unity's real voices with the music and UI's kept free.</summary>
        public static bool Fits(int level) => Total(level) + Mix.ReservedVoices <= Mix.RealVoices;

        private static int Clamp(int level, int n) => level < 0 ? 0 : level >= n ? n - 1 : level;
    }
}
