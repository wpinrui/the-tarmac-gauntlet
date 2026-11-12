using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Graphics, race and audio settings. Every change applies at once and is remembered between runs. Graphics options
    /// that an effect turns on and off are in <see cref="TarmacGauntlet.Visuals.GraphicsOptions"/>.
    /// </summary>
    public static class GameSettings
    {
        private const string VolumeKey = "settings.volume", VSyncKey = "settings.vsync";

        public static readonly (FullScreenMode Mode, string Name)[] DisplayModes =
        {
            (FullScreenMode.ExclusiveFullScreen, "Fullscreen"),
            (FullScreenMode.FullScreenWindow, "Borderless"),
            (FullScreenMode.Windowed, "Windowed"),
        };

        /// <summary>
        /// Applies the remembered settings. Unity remembers the display mode and resolution itself, and the quality level
        /// applies itself as the game starts (<see cref="QualityLevels"/>).
        /// </summary>
        public static void Load()
        {
            if (PlayerPrefs.HasKey(VSyncKey)) QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSyncKey) != 0 ? 1 : 0;
            AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1);
        }

        public static int DisplayMode
        {
            get
            {
                int i = Array.FindIndex(DisplayModes, m => m.Mode == Screen.fullScreenMode);
                return i < 0 ? 1 : i;
            }
            set => Screen.fullScreenMode = DisplayModes[Mathf.Clamp(value, 0, DisplayModes.Length - 1)].Mode;
        }

        /// <summary>The screen's sizes, smallest first, each once.</summary>
        public static IReadOnlyList<Vector2Int> Resolutions
        {
            get
            {
                List<Vector2Int> sizes = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct()
                    .OrderBy(r => r.x).ThenBy(r => r.y).ToList();
                var current = new Vector2Int(Screen.width, Screen.height);
                if (!sizes.Contains(current)) sizes.Add(current);
                return sizes.OrderBy(r => r.x).ThenBy(r => r.y).ToList();
            }
        }

        public static Vector2Int Resolution
        {
            get => new Vector2Int(Screen.width, Screen.height);
            set => Screen.SetResolution(value.x, value.y, Screen.fullScreenMode);
        }

        public static string[] QualityNames => QualityLevels.Names;

        public static int Quality
        {
            get => QualityLevels.Level;
            set
            {
                QualityLevels.Level = value;
                TarmacGauntlet.Visuals.GraphicsOptions.ApplyQuality(QualityLevels.Level);
            }
        }

        public static bool VSync
        {
            get => QualitySettings.vSyncCount > 0;
            set
            {
                QualitySettings.vSyncCount = value ? 1 : 0;
                PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// The race settings: the gearbox and steering help for the car you drive, and the track map on screen. The race
        /// reads them from the same keys (DriveSettings in Raceday).
        /// </summary>
        public const string AutomaticKey = "settings.automatic", SteeringHelpKey = "settings.steeringhelp", TrackMapKey = "settings.trackmap";

        public static bool Automatic
        {
            get => PlayerPrefs.GetInt(AutomaticKey, 1) != 0;
            set => SetFlag(AutomaticKey, value);
        }

        public static bool SteeringHelp
        {
            get => PlayerPrefs.GetInt(SteeringHelpKey, 1) != 0;
            set => SetFlag(SteeringHelpKey, value);
        }

        public static bool TrackMap
        {
            get => PlayerPrefs.GetInt(TrackMapKey, 0) != 0;
            set => SetFlag(TrackMapKey, value);
        }

        private static void SetFlag(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>0 to 1.</summary>
        public static float Volume
        {
            get => AudioListener.volume;
            set
            {
                AudioListener.volume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
                PlayerPrefs.Save();
            }
        }
    }
}
