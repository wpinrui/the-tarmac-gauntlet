using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race's colour grade for the sky it is under: warm and rich while the sun is low, cool and quiet in the blue hour, cool,
    /// muted and a little lifted at night, flat and grey under heavy cloud and rain.
    /// </summary>
    public static class SkyGrade
    {
        /// <param name="overcast">How grey the sky is, 0 to 1 (<see cref="WeatherSky.Overcast"/>).</param>
        /// <param name="rain">How hard it rains, 0 to 1 (<see cref="WeatherSky.Heavy"/>).</param>
        public static PostEffects.Grade For(SkyState s, float overcast, float rain)
        {
            float e = s.SunElevation;
            float grey = Mathf.Clamp01(Mathf.Max(overcast, rain));
            // The sun from just below the horizon to 18 degrees up, gone behind a grey sky.
            float golden = SkyClock.Smooth(-2f, 3f, e) * (1f - SkyClock.Smooth(6f, 18f, e)) * (1f - grey);
            // The sun from 8 degrees below the horizon to just above it.
            float blue = SkyClock.Smooth(-8f, -3f, e) * (1f - SkyClock.Smooth(-1f, 2f, e));
            float night = s.Night;
            return new PostEffects.Grade
            {
                // The tone curve presses the dark down: the night is lifted a little so the floodlit track still reads.
                Exposure = 0.25f * night,
                Contrast = 8f + 4f * golden + 6f * night - 10f * grey,
                Saturation = 6f + 10f * golden - 6f * blue - 22f * night - 18f * grey,
                Temperature = 14f * golden - 10f * blue - 8f * night - 6f * grey,
                Tint = 3f * golden - 2f * blue,
            };
        }
    }
}
