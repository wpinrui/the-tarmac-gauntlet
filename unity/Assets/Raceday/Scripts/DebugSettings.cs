using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race set-up the debug menu picks (<see cref="DebugMenu"/>): how the race starts, the kind of weather and
    /// the start hour, how many cars race and what your car is. Kept between runs, so a retest is one click. The scene reads it only in the editor and in
    /// development builds.
    /// </summary>
    public static class DebugSettings
    {
        private const string StartKey = "debug.start", WeatherKey = "debug.weather", HourKey = "debug.hour",
            FieldKey = "debug.field", CarKey = "debug.car";

        /// <summary>How the race starts, or null for the scene's own choice.</summary>
        public static CircuitScene.StartMode? Start
        {
            get => Get(StartKey) is int i ? (CircuitScene.StartMode)i : (CircuitScene.StartMode?)null;
            set => Set(StartKey, (int?)value);
        }

        /// <summary>The kind of weather, or null to roll it.</summary>
        public static WeatherKind? Weather
        {
            get => Get(WeatherKey) is int i ? (WeatherKind)i : (WeatherKind?)null;
            set => Set(WeatherKey, (int?)value);
        }

        /// <summary>Local time when the race starts, hours.</summary>
        public static float StartHour
        {
            get => PlayerPrefs.GetFloat(HourKey, TimeOfDay.DefaultStartHour);
            set
            {
                PlayerPrefs.SetFloat(HourKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Cars in the race, yours and the ones nearest the front, or 0 for the whole field.</summary>
        public static int Field
        {
            get => PlayerPrefs.GetInt(FieldKey, 0);
            set
            {
                PlayerPrefs.SetInt(FieldKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The model your car races as, or null for your own (<see cref="DebugActions.Field"/>).</summary>
        public static string Car
        {
            get
            {
                string id = PlayerPrefs.GetString(CarKey, "");
                return id.Length > 0 ? id : null;
            }
            set
            {
                PlayerPrefs.SetString(CarKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        private static int? Get(string key)
        {
            int v = PlayerPrefs.GetInt(key, -1);
            return v < 0 ? (int?)null : v;
        }

        private static void Set(string key, int? value)
        {
            PlayerPrefs.SetInt(key, value ?? -1);
            PlayerPrefs.Save();
        }
    }
}
