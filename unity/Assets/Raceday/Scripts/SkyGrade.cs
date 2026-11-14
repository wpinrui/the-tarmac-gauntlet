using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race's colour grade for the sky it is under: warm and rich while the sun is low, cool and quiet in the blue hour, cool,
    /// muted and a little lifted at night, flat and grey under heavy cloud and rain. On a day of broken cloud the picture
    /// flattens and cools a little in a cloud's shadow, and lifts, brightens and warms as the sun breaks through.
    /// </summary>
    public static class SkyGrade
    {
        /// <param name="overcast">How grey the sky is, 0 to 1 (<see cref="WeatherSky.Overcast"/>).</param>
        /// <param name="rain">How hard it rains, 0 to 1 (<see cref="WeatherSky.Heavy"/>).</param>
        /// <param name="shade">How deep in a cloud's shadow the camera's spot is, 0 in full sun to 1 (<see cref="CloudMap.Shading"/>).</param>
        /// <param name="patchy">How broken the cloud is, 0 to 1 (<see cref="Patchy"/>): how much the sun breaking through stands out.</param>
        public static PostEffects.Grade For(SkyState s, float overcast, float rain, float shade = 0f, float patchy = 0f)
        {
            float e = s.SunElevation;
            float grey = Mathf.Clamp01(Mathf.Max(overcast, rain));
            float golden = Golden(e, grey);
            // The sun from 8 degrees below the horizon to just above it.
            float blue = SkyClock.Smooth(-8f, -3f, e) * (1f - SkyClock.Smooth(-1f, 2f, e));
            float night = s.Night;
            // By day, out of the haze of a grey sky: under a cloud's shadow, or in the sun between broken clouds.
            float day = SkyClock.Smooth(-1f, 6f, e) * (1f - grey);
            float shaded = Mathf.Clamp01(shade) * day;
            float sunny = (1f - Mathf.Clamp01(shade)) * Mathf.Clamp01(patchy) * day;
            return new PostEffects.Grade
            {
                // The tone curve presses the dark down: the night is lifted so the floodlit track and the verges read.
                Exposure = NightBudget.Exposure * night + 0.12f * sunny - 0.04f * shaded,
                Contrast = 8f + 4f * golden + 6f * night - 10f * grey + 5f * sunny - 6f * shaded,
                Saturation = 6f + 10f * golden - 6f * blue - 22f * night - 18f * grey + 5f * sunny - 7f * shaded,
                Temperature = 14f * golden - 10f * blue - 8f * night - 6f * grey + 4f * sunny - 4f * shaded,
                Tint = 3f * golden - 2f * blue,
                // At night only the lamps reach the bloom's threshold: more of it gives each lamp its halo.
                Bloom = NightBudget.Bloom * night,
            };
        }

        /// <summary>How golden the low sun is, 0 to 1: from just below the horizon to 18 degrees up, gone behind a grey sky.</summary>
        public static float Golden(float sunElevation, float grey) =>
            SkyClock.Smooth(-2f, 3f, sunElevation) * (1f - SkyClock.Smooth(6f, 18f, sunElevation)) * (1f - Mathf.Clamp01(grey));

        /// <summary>
        /// How broken the cloud is, 0 to 1, for this share of the sky under cloud and this overcast: none in a clear sky with
        /// nothing to break through, most with a third to a half of the sky cloudy, none once it closes into a grey deck.
        /// </summary>
        public static float Patchy(float cloud, float overcast) => SkyClock.Smooth(0.15f, 0.4f, cloud) * (1f - overcast);
    }
}
