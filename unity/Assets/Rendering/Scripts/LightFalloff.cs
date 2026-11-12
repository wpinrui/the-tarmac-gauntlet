using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// URP's point and spot lights fall off with the square of the distance, where the built-in pipeline's faded out over the
    /// light's range and stayed much brighter far from it. The game's lamps were set for the built-in falloff.
    /// </summary>
    public static class LightFalloff
    {
        /// <summary>
        /// The built-in falloff a third of the way out to the range, over URP's there for a range of 1 m:
        /// (1 / (1 + 25 / 9)) / (9 * (1 - 1 / 81)^2).
        /// </summary>
        private const float Match = 0.0301f;

        /// <summary>
        /// The URP intensity for a light set at <paramref name="builtIn"/> for the built-in pipeline with this range, m: as
        /// bright as it was a third of the way out, brighter nearer the lamp and dimmer near the edge of its range.
        /// </summary>
        public static float Urp(float builtIn, float range) => builtIn * Match * range * range;

        /// <summary>The share of its intensity a URP point or spot light of this range gives at this distance, m.</summary>
        public static float At(float distance, float range)
        {
            float d2 = Mathf.Max(distance * distance, 1e-4f), f = d2 / (range * range);
            float smooth = Mathf.Clamp01(1f - f * f);
            return smooth * smooth / d2;
        }

        /// <summary>
        /// <see cref="Urp"/> for Linear colour space: a lamp set at <paramref name="builtIn"/> while the game rendered in
        /// Gamma, as bright as it was there at <paramref name="distance"/>, m, where it mostly lights things.
        /// </summary>
        public static float UrpLinear(Color colour, float builtIn, float range, float distance) =>
            LinearLight.Lamp(colour, Urp(builtIn, range), At(distance, range));
    }
}
