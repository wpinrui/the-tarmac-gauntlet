namespace TarmacGauntlet.Raceday
{
    /// <summary>What a car hit: a hard barrier (armco, the pit wall, garages), a tyre wall, or another car.</summary>
    public enum HitKind { Hard, Tyres, Car }

    /// <summary>The race sounds' banks: each a folder of takes under Resources/RaceSfx.</summary>
    public enum SfxBank { Gun, Air, JackUp, JackDrop, FuelClunk, FuelFlow, HitLight, HitHeavy, HitSoft, Scrape }

    /// <summary>
    /// The sound of a hit or a scrape from how fast the car met what it hit: which bank, how loud and how high.
    /// No Unity in here, so the tools can check it.
    /// </summary>
    public static class Impacts
    {
        /// <summary>Closing speed, m/s, under which a touch makes no sound.</summary>
        public const float Quietest = 1f;
        /// <summary>Closing speed, m/s, from which a hit crumples panels rather than knocking them.</summary>
        public const float Heavy = 6f;
        /// <summary>Closing speed, m/s, at which a hit is as loud as it gets.</summary>
        public const float Loudest = 22f;
        /// <summary>Sliding speed, m/s, under which rubbing along something makes no scrape.</summary>
        public const float ScrapeFrom = 1.5f;
        /// <summary>Sliding speed, m/s, at which a scrape is as loud and as high as it gets.</summary>
        public const float ScrapeTop = 30f;

        public readonly struct Hit
        {
            public readonly SfxBank Bank;
            public readonly float Volume;
            public Hit(SfxBank bank, float volume) { Bank = bank; Volume = volume; }
        }

        /// <summary>The hit for a closing speed into something, m/s; false for a touch too soft to hear.</summary>
        public static bool Of(HitKind kind, float closing, out Hit hit)
        {
            hit = default;
            if (closing < Quietest) return false;
            float strength = Clamp01((closing - Quietest) / (Loudest - Quietest));
            float volume = 0.25f + 0.75f * (float)System.Math.Sqrt(strength);
            if (kind == HitKind.Tyres) hit = new Hit(SfxBank.HitSoft, volume);
            else if (closing < Heavy) hit = new Hit(SfxBank.HitLight, volume);
            else hit = new Hit(SfxBank.HitHeavy, volume);
            // Two cars touching are bodywork on bodywork: a little softer than a car into steel.
            if (kind == HitKind.Car) hit = new Hit(hit.Bank, hit.Volume * 0.85f);
            return true;
        }

        /// <summary>
        /// The scrape for sliding along something at a speed, m/s: its volume and pitch; false when there is none.
        /// Tyre walls give way rather than scrape.
        /// </summary>
        public static bool Scrape(HitKind kind, float sliding, out float volume, out float pitch)
        {
            volume = pitch = 0f;
            if (kind == HitKind.Tyres || sliding < ScrapeFrom) return false;
            float s = Clamp01((sliding - ScrapeFrom) / (ScrapeTop - ScrapeFrom));
            volume = 0.3f + 0.7f * s;
            pitch = 0.85f + 0.35f * s;
            // Paint on paint: quieter and duller than steel.
            if (kind == HitKind.Car) { volume *= 0.6f; pitch *= 0.85f; }
            return true;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
