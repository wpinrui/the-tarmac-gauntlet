using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The look of the circuit at night: neutral white floodlights and white headlamps, as Ivan picked, and how bright
    /// each glows. The headlamps are on only at night.
    /// </summary>
    public static class NightLook
    {
        /// <summary>The floodlights' colour: a neutral white, a touch warm.</summary>
        public static readonly Color Flood = new Color(1f, 0.95f, 0.86f);
        /// <summary>The headlamps' colour: a cool white.</summary>
        public static readonly Color Headlamps = new Color(0.9f, 0.95f, 1f);
        public static readonly Color Red = new Color(1f, 0.06f, 0.03f);

        /// <summary>How much a floodlight's pool brightens the ground at its middle: 1 doubles it.</summary>
        public const float PoolBrightening = 7f;
        /// <summary>Light a pool adds to the blackest ground at its middle.</summary>
        public const float PoolLift = 0.035f;
        /// <summary>
        /// The same for a headlamp beam, though it brightens the ground as the night's own light leaves it, before the
        /// floodlights' pools: under a pool it shows less, as real headlamps do under floodlights.
        /// </summary>
        public const float BeamBrightening = 10f, BeamLift = 0.03f;
        /// <summary>
        /// The most light a headlamp beam adds, in linear luminance, eased towards: a brightly lit floor such as the pit
        /// garage's gains at most this, so it never blows out into bloom.
        /// </summary>
        public const float BeamCap = 0.4f;
        /// <summary>A floodlight lens's glow, and a headlamp's.</summary>
        public const float LensGlow = 2f, HeadlampGlow = 2.2f;
        /// <summary>Tail lamps at night, and brake lamps whenever the brakes are on.</summary>
        public const float TailGlow = 0.9f, BrakeGlow = 3f;
        /// <summary>The stands' seats and crowd glow this much at night, lit by the roof floodlights.</summary>
        public const float StandGlow = 0.22f;

        /// <summary>How far the floodlights and headlamps are on for a darkness from <see cref="TimeOfDay.Lights"/>: on from first dusk.</summary>
        public static float On(float night) => SkyClock.Smooth(0f, 0.12f, night);
    }
}
