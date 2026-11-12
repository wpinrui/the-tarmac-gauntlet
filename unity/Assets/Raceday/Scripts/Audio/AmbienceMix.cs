using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>How many ambience voices one quality level may play at once.</summary>
    public readonly struct AmbienceBudget
    {
        /// <summary>Stands whose crowd plays, the nearest first. Each stand is a calm bed and a lively one on top.</summary>
        public readonly int Stands;
        /// <summary>Crowd roars that can sound at once.</summary>
        public readonly int Roars;
        /// <summary>Rain beds: 1 plays the light rain alone and turns it up, 2 crossfades light into heavy.</summary>
        public readonly int RainLayers;
        /// <summary>Countryside beds: 1 plays the one that fits the hour, 2 crossfades day into night.</summary>
        public readonly int CountryLayers;
        /// <summary>Cars whose tyres hiss on a wet road, the nearest first.</summary>
        public readonly int WetCars;

        public AmbienceBudget(int stands, int roars, int rain, int country, int wetCars)
        {
            Stands = stands;
            Roars = roars;
            RainLayers = rain;
            CountryLayers = country;
            WetCars = wetCars;
        }

        /// <summary>Most voices the ambience can hold at once: two a stand, the roars, the PA, the rain, a thunderclap,
        /// the countryside, the wind and the wet tyres.</summary>
        public int Voices => 2 * Stands + Roars + 1 + RainLayers + 1 + CountryLayers + 1 + WetCars;
    }

    /// <summary>
    /// The rules of the race ambience's mix, as plain arithmetic: how loud each bed plays for the race as it stands.
    /// <see cref="RaceAmbience"/> applies them in the game; the audition tool checks them outside Unity.
    /// </summary>
    public static class AmbienceMix
    {
        /// <summary>Low, Medium, High and Ultra. Medium leaves the engines' ten voices and the tyres' four room inside
        /// Unity's 32 real voices.</summary>
        public static readonly AmbienceBudget[] Budgets =
        {
            new AmbienceBudget(2, 1, 1, 1, 0),
            new AmbienceBudget(3, 1, 2, 2, 2),
            new AmbienceBudget(5, 2, 2, 2, 4),
            new AmbienceBudget(5, 2, 2, 2, 6),
        };

        public static AmbienceBudget Budget(int level) => Budgets[Math.Max(0, Math.Min(Budgets.Length - 1, level))];

        /// <summary>
        /// A stand's two crowd beds, 0 to 1: the calm murmur is always there, thinner in the small hours and the rain;
        /// the lively cheering rises over it with the excitement.
        /// </summary>
        /// <param name="excitement">0 for nothing happening in front of the stand, 1 for the start, the finish or a pass.</param>
        /// <param name="night">0 by day to 1 at night: through the small hours half the crowd is asleep.</param>
        /// <param name="rain">Rain falling, mm an hour.</param>
        public static (float calm, float lively) Crowd(float excitement, float night, float rain)
        {
            float wet = Clamp01(rain / 6f);
            float people = (1f - 0.45f * Clamp01(night)) * (1f - 0.35f * wet);
            float e = Clamp01(excitement);
            return (people * (0.75f + 0.25f * e), people * e * e * 0.9f + people * 0.1f * e);
        }

        /// <summary>The light and heavy rain beds, 0 to 1, for rain falling at this many mm an hour. With one layer the
        /// light bed carries it all.</summary>
        public static (float light, float heavy) Rain(float mmPerHour, int layers)
        {
            float on = Clamp01(mmPerHour / 1.5f);
            float heavy = Clamp01((mmPerHour - 2f) / 6f);
            if (layers < 2) return ((float)Math.Sqrt(on) * (0.6f + 0.4f * heavy), 0f);
            return ((float)Math.Sqrt(on) * (1f - 0.5f * heavy), (float)Math.Sqrt(heavy));
        }

        /// <summary>Whether thunder rolls: in a thundery spell's heavier showers, and in any downpour.</summary>
        public static bool Thunder(bool thundery, float mmPerHour) => thundery ? mmPerHour > 2f : mmPerHour > 8f;

        /// <summary>Real seconds to the next thunderclap, from a random number 0 to 1: closer together the harder it rains.</summary>
        public static float ThunderGap(float mmPerHour, float random) =>
            (14f + 50f * random) * (0.6f + 0.4f * (1f - Clamp01((mmPerHour - 2f) / 10f)));

        /// <summary>How far from the crowd the listener is, 0 by a stand to 1 well away from all of them, from the
        /// distance to the nearest stand, m.</summary>
        public static float Away(float metresToStand) => Smooth(Clamp01((metresToStand - 50f) / 200f));

        /// <summary>
        /// The countryside's day and night beds, 0 to 1: heard away from the stands, birds by day and crickets by night,
        /// and both quiet in the rain. With one layer only the one that fits the hour plays.
        /// </summary>
        public static (float day, float night) Country(float away, float night, float mmPerHour, int layers)
        {
            float n = Clamp01(night);
            float level = (0.25f + 0.75f * Clamp01(away)) * (1f - Clamp01(mmPerHour / 3f));
            if (layers < 2) return n < 0.5f ? (level, 0f) : (0f, level);
            return (level * (float)Math.Cos(n * Math.PI / 2), level * (float)Math.Sin(n * Math.PI / 2));
        }

        /// <summary>How the camera meets the rush of air: fully on the bonnet and bumper, part way for the chase camera,
        /// hardly at all for a TV camera.</summary>
        public static float Exposure(bool onboard, bool chase) => onboard ? 1f : chase ? 0.55f : 0.1f;

        /// <summary>
        /// The crowd's excitement in front of a stand, 0 to 1, from the cars passing it: one car lifts it a little, a
        /// bunch together a lot.
        /// </summary>
        public static float Passing(int cars) => Clamp01(1f - (float)Math.Pow(0.72, cars));

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
