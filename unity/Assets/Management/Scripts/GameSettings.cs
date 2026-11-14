using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Audio;
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
        private const string VSyncKey = "settings.vsync", DisplayModeKey = "settings.displaymode",
            WidthKey = "settings.width", HeightKey = "settings.height";

        public static readonly (FullScreenMode Mode, string Name)[] DisplayModes =
        {
            (FullScreenMode.ExclusiveFullScreen, "Fullscreen"),
            (FullScreenMode.FullScreenWindow, "Borderless"),
            (FullScreenMode.Windowed, "Windowed"),
        };

        /// <summary>
        /// Applies the remembered display mode, resolution, v-sync and volume as the game starts, whichever scene it starts
        /// in. The game saves the display mode and resolution itself rather than trusting Unity to. The quality level applies
        /// itself (<see cref="QualityLevels"/>), and each graphics option is read from its key when an effect asks.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            if (PlayerPrefs.HasKey(VSyncKey)) QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSyncKey) != 0 ? 1 : 0;
            foreach (VolumeSlider slider in Sliders) Mix.SetPosition(slider, PlayerPrefs.GetFloat(Key(slider), Mix.Default(slider)));
            AudioListener.volume = Mix.Master;

            FullScreenMode mode = PlayerPrefs.HasKey(DisplayModeKey)
                ? DisplayModes[Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey), 0, DisplayModes.Length - 1)].Mode
                : Screen.fullScreenMode;
            var size = new Vector2Int(PlayerPrefs.GetInt(WidthKey, 0), PlayerPrefs.GetInt(HeightKey, 0));
            // A size the screen no longer offers, after a monitor change, keeps the size the game opened at.
            if (size.x > 0 && size.y > 0 && Resolutions.Contains(size)) Screen.SetResolution(size.x, size.y, mode);
            else if (mode != Screen.fullScreenMode) Screen.fullScreenMode = mode;
        }

        public static int DisplayMode
        {
            get
            {
                int i = Array.FindIndex(DisplayModes, m => m.Mode == Screen.fullScreenMode);
                return i < 0 ? 1 : i;
            }
            set
            {
                int i = Mathf.Clamp(value, 0, DisplayModes.Length - 1);
                Screen.fullScreenMode = DisplayModes[i].Mode;
                PlayerPrefs.SetInt(DisplayModeKey, i);
                PlayerPrefs.Save();
            }
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
            set
            {
                Screen.SetResolution(value.x, value.y, Screen.fullScreenMode);
                PlayerPrefs.SetInt(WidthKey, value.x);
                PlayerPrefs.SetInt(HeightKey, value.y);
                PlayerPrefs.Save();
            }
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
        /// The race settings: the gearbox, steering help, traction control, opposite lock assist and invulnerability for the car you drive, the track map,
        /// proximity arrows and rear mirror on screen, and the racing line drawn on the track (0 off, 1 corners only, 2 the whole lap). The race
        /// reads them from the same keys (DriveSettings in Raceday).
        /// </summary>
        public const string AutomaticKey = "settings.automatic", SteeringHelpKey = "settings.steeringhelp", TrackMapKey = "settings.trackmap";
        public const string TractionControlKey = "settings.tractioncontrol", OppositeLockKey = "settings.oppositelock", RacingLineKey = "settings.racingline";
        public const string ProximityArrowsKey = "settings.proximityarrows", RearMirrorKey = "settings.rearmirror";
        public const string InvulnerabilityKey = "settings.invulnerability";

        /// <summary>The invulnerability steps, in order: the race reads the index (Invulnerability in Raceday).</summary>
        public static readonly string[] InvulnerabilityNames = { "Off", "Ignore crashes with cars", "Full invulnerability" };

        public static int Invulnerability
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(InvulnerabilityKey, 0), 0, InvulnerabilityNames.Length - 1);
            set
            {
                PlayerPrefs.SetInt(InvulnerabilityKey, Mathf.Clamp(value, 0, InvulnerabilityNames.Length - 1));
                PlayerPrefs.Save();
            }
        }

        public static bool OppositeLock
        {
            get => PlayerPrefs.GetInt(OppositeLockKey, 0) != 0;
            set => SetFlag(OppositeLockKey, value);
        }

        /// <summary>The traction control steps, in order: the race reads the index (TractionAssist in Raceday).</summary>
        public static readonly string[] TractionControlNames = { "Off", "Medium", "Full" };

        public static int TractionControl
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(TractionControlKey, 0), 0, TractionControlNames.Length - 1);
            set
            {
                PlayerPrefs.SetInt(TractionControlKey, value);
                PlayerPrefs.Save();
            }
        }

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

        public static int RacingLine
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(RacingLineKey, 0), 0, 2);
            set
            {
                PlayerPrefs.SetInt(RacingLineKey, Mathf.Clamp(value, 0, 2));
                PlayerPrefs.Save();
            }
        }

        public static bool ProximityArrows
        {
            get => PlayerPrefs.GetInt(ProximityArrowsKey, 1) != 0;
            set => SetFlag(ProximityArrowsKey, value);
        }

        public static bool RearMirror
        {
            get => PlayerPrefs.GetInt(RearMirrorKey, 1) != 0;
            set => SetFlag(RearMirrorKey, value);
        }

        private static void SetFlag(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The volume sliders, in the order the settings show them: master, then one for each part of the mix (tyres go
        /// with the engines, as the car). The master and music keep the keys they had before the mixer.
        /// </summary>
        public static readonly VolumeSlider[] Sliders =
            { VolumeSlider.Master, VolumeSlider.Music, VolumeSlider.Cars, VolumeSlider.Effects, VolumeSlider.Ambience, VolumeSlider.Ui };

        private static string Key(VolumeSlider slider) => slider switch
        {
            VolumeSlider.Master => "settings.volume",
            VolumeSlider.Music => "settings.music",
            VolumeSlider.Cars => "settings.cars",
            VolumeSlider.Effects => "settings.effects",
            VolumeSlider.Ambience => "settings.ambience",
            _ => "settings.ui",
        };

        /// <summary>A volume slider's position, 0 to 1. The <see cref="Mix"/> turns it into a gain.</summary>
        public static float Level(VolumeSlider slider) => Mix.Position(slider);

        public static void SetLevel(VolumeSlider slider, float value)
        {
            Mix.SetPosition(slider, value);
            if (slider == VolumeSlider.Master) AudioListener.volume = Mix.Master;
            PlayerPrefs.SetFloat(Key(slider), Mix.Position(slider));
            PlayerPrefs.Save();
        }

        /// <summary>The master volume's slider, 0 to 1.</summary>
        public static float Volume
        {
            get => Level(VolumeSlider.Master);
            set => SetLevel(VolumeSlider.Master, value);
        }

        /// <summary>The menu music's slider, 0 to 1, under <see cref="Volume"/> (<see cref="MenuMusic"/>).</summary>
        public static float Music
        {
            get => Level(VolumeSlider.Music);
            set => SetLevel(VolumeSlider.Music, value);
        }
    }
}
