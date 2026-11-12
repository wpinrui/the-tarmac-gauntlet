using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race settings from the settings menu (GameSettings in Management, which this assembly can't see), read
    /// from the same keys: the gearbox and steering help for the car you drive, and the track map on screen.
    /// </summary>
    public static class DriveSettings
    {
        public const string AutomaticKey = "settings.automatic", SteeringHelpKey = "settings.steeringhelp", TrackMapKey = "settings.trackmap";

        public static bool Automatic => PlayerPrefs.GetInt(AutomaticKey, 1) != 0;
        public static bool SteeringHelp => PlayerPrefs.GetInt(SteeringHelpKey, 1) != 0;
        public static bool TrackMap => PlayerPrefs.GetInt(TrackMapKey, 0) != 0;
    }
}
