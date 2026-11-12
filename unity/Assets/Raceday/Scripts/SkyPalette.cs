using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The clear day's palette, laid over the sky the clock and the weather made (<see cref="SkyClock"/>, <see cref="WeatherSky"/>):
    /// a deeper blue overhead and a bluer, thinner haze at the horizon, and with the sun low its light on the clouds orange-gold,
    /// their shade a dusky mauve and the sky warm towards it. A grey sky and the night keep their own colours.
    /// The painted sky, the volumetric clouds and the cloud preview all read the colours this leaves.
    /// </summary>
    public static class SkyPalette
    {
        /// <summary>Share of the zenith's red, green and blue the deep palette takes away.</summary>
        private static readonly Vector3 ZenithDeepens = new Vector3(0.55f, 0.4f, 0.15f);
        /// <summary>Share of the horizon's red, green and blue the deep palette takes away: the haze shows the sky's blue.</summary>
        private static readonly Vector3 HorizonDeepens = new Vector3(0.36f, 0.24f, 0.1f);
        /// <summary>The low sun's light on the clouds, and their shade, against the clock's colours.</summary>
        private static readonly Vector3 GoldenLight = new Vector3(1.15f, 0.78f, 0.45f), Dusky = new Vector3(0.95f, 0.78f, 0.9f);
        /// <summary>How strong the warm glow towards a low sun is, over the haze, against the clock's glow.</summary>
        public const float GoldenGlow = 0.8f;

        public static void Apply(ref SkyState s, float overcast)
        {
            float e = s.SunElevation;
            s.Deep = (1f - Mathf.Clamp01(overcast)) * SkyClock.Smooth(-4f, 2f, e);
            s.Golden = s.Deep * SkyClock.Smooth(-2f, 3f, e) * (1f - SkyClock.Smooth(6f, 18f, e));
            s.Zenith = Scale(s.Zenith, Vector3.one - ZenithDeepens * s.Deep);
            s.Horizon = Scale(s.Horizon, Vector3.one - HorizonDeepens * s.Deep);
            s.CloudLit = Scale(s.CloudLit, Vector3.Lerp(Vector3.one, GoldenLight, s.Golden));
            s.CloudShade = Scale(s.CloudShade, Mauve(s));
        }

        /// <summary>The dusky tint the low sun gives the clouds' shade.</summary>
        public static Vector3 Mauve(in SkyState s) => Vector3.Lerp(Vector3.one, Dusky, s.Golden);

        /// <summary>The warm glow towards the sun, laid over the haze.</summary>
        public static Color Glow(in SkyState s)
        {
            Color g = s.Glow * (GoldenGlow * s.Golden);
            g.a = 1f;
            return g;
        }

        /// <summary>
        /// The light inside the clouds that is not the sun's: from the sky above at their tops, and at their bases from the
        /// sunlit ground and the sky round the horizon, tinted with the low sun's mauve. <paramref name="sun"/> is the sun's
        /// light on the ground.
        /// </summary>
        public static void CloudAmbient(in SkyState s, Color sun, out Color above, out Color below)
        {
            Vector3 mauve = Mauve(s);
            Color sky = s.Zenith * 0.7f + s.Horizon * 0.3f;
            Color ground = s.AmbientGround * sun * (Mathf.Max(s.LightDir.y, 0f) * 0.15f);
            above = Scale(sky, mauve);
            below = Scale(sky * 0.4f + ground, mauve);
        }

        private static Color Scale(Color c, Vector3 k) => new Color(c.r * k.x, c.g * k.y, c.b * k.z, 1f);
    }
}
