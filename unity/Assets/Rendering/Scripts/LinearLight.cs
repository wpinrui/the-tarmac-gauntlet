using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The game's light intensities were set while it rendered in Gamma colour space. There a light added to the sRGB
    /// value of what it lit, and the post stack decoded the sum to linear light; in Linear the light adds in linear light.
    /// Unity converts every colour itself, but not an intensity: these give the Linear intensity that leaves a surface as
    /// bright as the Gamma one did.
    /// </summary>
    public static class LinearLight
    {
        private static readonly Vector3 Luma = new Vector3(0.2126f, 0.7152f, 0.0722f);

        /// <summary>
        /// The Linear intensity for a light of this colour set at <paramref name="gammaIntensity"/> in Gamma, over this
        /// ambient light, for a surface that faces it at <paramref name="facing"/> (the cosine): it lights that surface as
        /// brightly as before. Nearer face on it lights more, and the shadows keep their ambient light, so the light falls
        /// off over the surface as it does in the real world.
        /// </summary>
        public static float Match(Color colour, float gammaIntensity, Color ambient, float facing)
        {
            if (gammaIntensity <= 0f) return 0f;
            facing = Mathf.Clamp(facing, 0.05f, 1f);
            float a = Y(Linear(ambient));
            float want = Y(Linear(colour * (gammaIntensity * facing) + ambient)) - a;
            float per = facing * Y(Linear(colour));
            return per > 1e-6f ? Mathf.Max(want, 0f) / per : 0f;
        }

        /// <summary>
        /// The same for a lamp at night, where the ambient light hardly counts: <paramref name="falloff"/> is the share of
        /// its intensity the lamp gives where it should match, at the distance it mostly lights things from.
        /// </summary>
        public static float Lamp(Color colour, float gammaIntensity, float falloff)
        {
            if (falloff <= 0f) return 0f;
            return Match(colour, gammaIntensity * falloff, Color.black, 1f) / falloff;
        }

        /// <summary>sRGB to linear light, the sRGB curve carried on past 1.</summary>
        public static float Linear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        public static Color Linear(Color c) => new Color(Linear(c.r), Linear(c.g), Linear(c.b), c.a);

        private static float Y(Color c) => c.r * Luma.x + c.g * Luma.y + c.b * Luma.z;
    }
}
