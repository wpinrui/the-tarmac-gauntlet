using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race settings from the settings menu (GameSettings in Management, which this assembly can't see), read
    /// from the same keys: the gearbox, steering help, traction control, opposite lock assist and invulnerability for the car you drive, the track map,
    /// proximity arrows and rear mirror on screen, and the racing line drawn on the track.
    /// </summary>
    public static class DriveSettings
    {
        public const string AutomaticKey = "settings.automatic", SteeringHelpKey = "settings.steeringhelp", TrackMapKey = "settings.trackmap";
        public const string TractionControlKey = "settings.tractioncontrol", OppositeLockKey = "settings.oppositelock", RacingLineKey = "settings.racingline";
        public const string ProximityArrowsKey = "settings.proximityarrows", RearMirrorKey = "settings.rearmirror";
        public const string InvulnerabilityKey = "settings.invulnerability";

        public static bool Automatic => PlayerPrefs.GetInt(AutomaticKey, 1) != 0;
        public static bool SteeringHelp => PlayerPrefs.GetInt(SteeringHelpKey, 1) != 0;
        public static bool TrackMap => PlayerPrefs.GetInt(TrackMapKey, 0) != 0;
        public static bool ProximityArrows => PlayerPrefs.GetInt(ProximityArrowsKey, 1) != 0;
        public static bool RearMirror => PlayerPrefs.GetInt(RearMirrorKey, 1) != 0;
        public static bool OppositeLock => PlayerPrefs.GetInt(OppositeLockKey, 0) != 0;
        public static TractionAssist TractionControl => (TractionAssist)Mathf.Clamp(PlayerPrefs.GetInt(TractionControlKey, 0), 0, 2);
        /// <summary>The racing line drawn on the track for the car you drive: off, through the corners only, or the whole lap.</summary>
        public static RacingLineShown RacingLine => (RacingLineShown)Mathf.Clamp(PlayerPrefs.GetInt(RacingLineKey, 0), 0, 2);
        /// <summary>What can't hurt the car you drive: nothing, other cars, or anything at all.</summary>
        public static Invulnerability Invulnerability => (Invulnerability)Mathf.Clamp(PlayerPrefs.GetInt(InvulnerabilityKey, 0), 0, 2);
    }

    /// <summary>Traction control for the car you drive, in the settings menu's order.</summary>
    public enum TractionAssist { Off, Medium, Full }

    /// <summary>
    /// Invulnerability for the car you drive, in the settings menu's order. Cars still bump and barriers still stop
    /// them; what's ignored does no damage, and with Full a hard barrier hit doesn't stop the car dead either.
    /// </summary>
    public enum Invulnerability { Off, Cars, Full }

    /// <summary>How much of the racing line shows, in the settings menu's order.</summary>
    public enum RacingLineShown { Off, Corners, Full }
}
