using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public enum FloodColour { Cool, Neutral, Warm }
    public enum HeadlampColour { White, Yellow }

    /// <summary>The look of the circuit at night: the floodlights' colour, the cars' headlamps and how bright each glows.</summary>
    public static class NightLook
    {
        public const FloodColour Flood = FloodColour.Neutral;
        public const HeadlampColour Headlamps = HeadlampColour.White;

        /// <summary>How much a floodlight's pool brightens the ground at its middle: 1 doubles it.</summary>
        public const float PoolBrightening = 7f;
        /// <summary>Light a pool adds to the blackest ground at its middle.</summary>
        public const float PoolLift = 0.035f;
        /// <summary>The same for a headlamp beam.</summary>
        public const float BeamBrightening = 5f, BeamLift = 0.03f;
        /// <summary>A floodlight lens's glow, and a headlamp's.</summary>
        public const float LensGlow = 2f, HeadlampGlow = 2.2f;
        /// <summary>Tail lamps at night, and brake lamps whenever the brakes are on.</summary>
        public const float TailGlow = 0.9f, BrakeGlow = 3f;
        /// <summary>The stands' seats and crowd glow this much at night, lit by the roof floodlights.</summary>
        public const float StandGlow = 0.22f;

        public static readonly Color Red = new Color(1f, 0.06f, 0.03f);

        public static Color FloodTint(FloodColour c) => c switch
        {
            FloodColour.Cool => new Color(0.84f, 0.92f, 1f),
            FloodColour.Warm => new Color(1f, 0.76f, 0.48f),
            _ => new Color(1f, 0.95f, 0.86f),
        };

        public static Color HeadlampTint(HeadlampColour c) => c switch
        {
            HeadlampColour.Yellow => new Color(1f, 0.85f, 0.42f),
            _ => new Color(0.9f, 0.95f, 1f),
        };

        /// <summary>How far the floodlights and headlamps are on for a night amount from <see cref="SkyClock"/>: on from first dusk.</summary>
        public static float On(float night) => SkyClock.Smooth(0f, 0.12f, night);
    }
}
