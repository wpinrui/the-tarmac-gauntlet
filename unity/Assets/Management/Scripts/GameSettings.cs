using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TarmacGauntlet.Management
{
    /// <summary>Graphics and audio settings. Every change applies at once and is remembered between runs.</summary>
    public static class GameSettings
    {
        private const string VolumeKey = "settings.volume", QualityKey = "settings.quality", VSyncKey = "settings.vsync";

        public static readonly (FullScreenMode Mode, string Name)[] DisplayModes =
        {
            (FullScreenMode.ExclusiveFullScreen, "Fullscreen"),
            (FullScreenMode.FullScreenWindow, "Borderless"),
            (FullScreenMode.Windowed, "Windowed"),
        };

        /// <summary>Applies the remembered settings. Unity remembers the display mode and resolution itself.</summary>
        public static void Load()
        {
            if (PlayerPrefs.HasKey(QualityKey))
            {
                int q = PlayerPrefs.GetInt(QualityKey);
                if (q >= 0 && q < QualitySettings.names.Length) QualitySettings.SetQualityLevel(q, true);
            }
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

        public static string[] QualityNames => QualitySettings.names;

        public static int Quality
        {
            get => QualitySettings.GetQualityLevel();
            set
            {
                bool vSync = VSync;
                QualitySettings.SetQualityLevel(Mathf.Clamp(value, 0, QualitySettings.names.Length - 1), true);
                QualitySettings.vSyncCount = vSync ? 1 : 0;
                PlayerPrefs.SetInt(QualityKey, QualitySettings.GetQualityLevel());
                PlayerPrefs.Save();
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
