using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>The weather over the circuit at one moment, and what it does to the track.</summary>
    public struct WeatherState
    {
        /// <summary>Share of the sky under cloud, 0 to 1.</summary>
        public float Cloud;
        /// <summary>Rain falling on the circuit, mm an hour.</summary>
        public float Rain;
        /// <summary>Depth of the water film on the track, mm.</summary>
        public float Water;
        /// <summary>Wind strength, 1 for a normal breeze.</summary>
        public float Wind;

        public static readonly WeatherState Dry = new WeatherState { Cloud = 0.3f, Wind = 1f };

        /// <summary>Share of dry grip the tyres find on the track: 1 dry, about 0.75 fully wet, 0.7 in standing water.</summary>
        public float Grip => RaceWeather.GripFor(Water);

        /// <summary>How wet the track races, 0 dry to 1 fully wet, from the grip lost.</summary>
        public float Wet => Mathf.Clamp01((1f - Grip) / (1f - RaceWeather.WetGrip));
    }

    /// <summary>
    /// The weather the race is running in now. <see cref="WeatherView"/> sets it every frame from the
    /// race's <see cref="WeatherPlan"/>; the tyres read it, so a wet track slows every car, driven or
    /// gliding, and the drivers make more mistakes on it. Dry until a plan runs.
    /// </summary>
    public static class RaceWeather
    {
        public static WeatherState Now = WeatherState.Dry;

        /// <summary>Weather the debug menu holds over the circuit in place of the race's own, or null.</summary>
        public static WeatherState? Forced;

        /// <summary>Grip on a fully wet track, before it floods.</summary>
        public const float WetGrip = 0.75f;

        /// <summary>
        /// Share of dry grip on a water film this deep, mm. A damp track loses a little, a wet one a
        /// quarter, and standing water a little more again.
        /// </summary>
        public static float GripFor(float water)
        {
            if (water <= 0f) return 1f;
            float film = 1f - Mathf.Exp(-water / 0.4f);
            float standing = Mathf.Clamp01((water - 1f) / 1.5f);
            return 1f - (1f - WetGrip) * 1.08f * film - 0.05f * standing;
        }
    }
}
