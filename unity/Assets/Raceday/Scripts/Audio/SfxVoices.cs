namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How many race sounds play at once at each quality level, and which voice a new sound takes when they are all
    /// busy: the quietest, as heard at the listener, if the new sound is louder than it. No Unity in here, so the
    /// tools can check it.
    /// </summary>
    public static class SfxVoices
    {
        /// <summary>One-shot sounds (hits, wheel guns, the jacks) playing at once, Low to Ultra.</summary>
        public static readonly int[] OneShots = { 4, 6, 8, 10 };
        /// <summary>Held sounds (scrapes, fuel running in) playing at once, Low to Ultra.</summary>
        public static readonly int[] Loops = { 2, 3, 3, 4 };

        public static int OneShotsAt(int level) => OneShots[Clamp(level, OneShots.Length)];
        public static int LoopsAt(int level) => Loops[Clamp(level, Loops.Length)];

        /// <summary>
        /// How loud a sound of this volume is at this distance, under a logarithmic rolloff that holds full volume
        /// out to minDistance and is silent past maxDistance.
        /// </summary>
        public static float Heard(float volume, float distance, float minDistance, float maxDistance)
        {
            if (distance >= maxDistance) return 0f;
            return distance <= minDistance ? volume : volume * minDistance / distance;
        }

        /// <summary>
        /// The voice for a new sound heard this loud: a free one if there is one, else the quietest busy one if the
        /// new sound is louder; -1 to drop the new sound. busy and heard are by voice; count is how many may be used.
        /// </summary>
        public static int Choose(bool[] busy, float[] heard, int count, float loudness)
        {
            if (loudness <= 0f) return -1;
            int quietest = -1;
            for (int i = 0; i < count && i < busy.Length; i++)
            {
                if (!busy[i]) return i;
                if (quietest < 0 || heard[i] < heard[quietest]) quietest = i;
            }
            return quietest >= 0 && heard[quietest] < loudness ? quietest : -1;
        }

        /// <summary>A take from count, never the one played last unless it is the only one.</summary>
        public static int Take(int count, int last, System.Random random)
        {
            if (count <= 1) return 0;
            int k = random.Next(count - 1);
            return k >= last && last >= 0 ? k + 1 : k;
        }

        private static int Clamp(int level, int n) => level < 0 ? 0 : level >= n ? n - 1 : level;
    }
}
