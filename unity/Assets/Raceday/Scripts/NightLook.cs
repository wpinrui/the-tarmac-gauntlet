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

        /// <summary>
        /// The floodlights' light on flat ground where their map (<see cref="FloodField"/>) holds 1, per unit of the
        /// ground's colour: the asphalt reads clearly lit and the white lines stay short of white.
        /// </summary>
        public const float FloodGround = 1.5f;
        /// <summary>
        /// How far the floodlights light things for the sun's height, degrees: none while the sun is up, faint at an
        /// overcast dusk with the lights just on, all of it once the sky is dark. The lenses glow whenever they are on.
        /// </summary>
        public static float FloodDark(float sunElevation) => SkyClock.Smooth(3f, 13f, -sunElevation);
        /// <summary>
        /// The most linear light the circuit's lamps give back from any surface, floodlights and headlamps together
        /// (NightLit.hlsl): with the night's exposure a white line under them comes out a little short of white.
        /// </summary>
        public const float NightMost = 0.5f;
        /// <summary>The most linear light the floodlights' beams add in the mist, however near a lens and however many overlap.</summary>
        public const float BeamMost = 0.06f;
        /// <summary>
        /// The light a car's headlamps throw on the ground at their brightest, 9 to 15 m ahead, per unit of the ground's
        /// colour (<see cref="HeadlampGround"/>): with no circuit lights the road reads clearly lit and the white lines
        /// stay short of white. It fades to nothing at the reach, m, lighting the road out to about 60 m.
        /// </summary>
        public const float HeadlampGround = 5f, HeadlampReach = 75f;
        /// <summary>
        /// The share of that light the ground takes from a car under the floodlights (<see cref="HeadlampGround"/>): there the
        /// headlamps are a subtle extra on the road just ahead, and the floodlights' pools brighten it again.
        /// </summary>
        public const float HeadlampUnderFloods = 0.01f;
        /// <summary>
        /// How far the headlamps light the ground for the sun's height, degrees: from none at the first dusk to all of it
        /// once the sky is dark, so they do not stand out against the twilight.
        /// </summary>
        public static float HeadlampDark(float sunElevation) => SkyClock.Smooth(4f, 16f, -sunElevation);
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
