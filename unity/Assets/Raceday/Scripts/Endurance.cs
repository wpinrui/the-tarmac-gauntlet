using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The knobs that make the AI race for the long run rather than the lap: how hard it passes, how
    /// much room it leaves, how readily it defends and how close to the grip it drives. All in one
    /// place to tune. Where a value replaces one of the TORCS tutorial's, the tutorial's is noted.
    /// </summary>
    public static class Endurance
    {
        // Passing.
        /// <summary>How fast a car moves across to pass, m/s: the tutorial's 0.1 m every 0.02 s.</summary>
        public const float OvertakeOffsetRate = 5f;
        /// <summary>
        /// Passing uses the track's width over this either side of the middle. Tutorial: 3. At 3 three
        /// slow cars side by side fill the room, and a quick car at the back of the grid queues behind them.
        /// </summary>
        public const float OvertakeWidthDiv = 2.5f;

        // Spacing, for a driver of middling safety: see MarginScale.
        /// <summary>Room kept to the car ahead on top of its length, m. Tutorial: 2.</summary>
        public const float LengthMargin = 4f;
        /// <summary>
        /// A car ahead nearer than this beside the car's path is in the way, m: the tutorial's. Any more
        /// and a car in the next lane over holds up a car with a clear lane.
        /// </summary>
        public const float WidthMargin = 1f;
        /// <summary>Gap to a car alongside at which a car steers to run parallel to it, m. Tutorial: 2.</summary>
        public const float SideCollisionMargin = 3f;

        // Defending.
        /// <summary>A car behind closing faster than this is not defended against, m/s.</summary>
        public const float NoDefendClosing = 6f;

        // Grip.
        /// <summary>Share of the grip the slowest driver (pace 0) uses.</summary>
        public const float GripAtPace0 = 0.84f;
        /// <summary>Share of the grip the fastest driver (pace 100) uses.</summary>
        public const float GripAtPace100 = 0.95f;

        // Per driver.
        /// <summary>Spacing margins are scaled by this for a driver of safety 0...</summary>
        public const float MarginAtSafety0 = 0.8f;
        /// <summary>...and by this for a driver of safety 100.</summary>
        public const float MarginAtSafety100 = 1.3f;

        /// <summary>Share of the grip a driver of this pace (0 to 100) drives at.</summary>
        public static float GripFor(float pace) => Mathf.Lerp(GripAtPace0, GripAtPace100, pace / 100f);

        /// <summary>How much the spacing margins are scaled for a driver of this safety (0 to 100).</summary>
        public static float MarginScale(float safety) => Mathf.Lerp(MarginAtSafety0, MarginAtSafety100, safety / 100f);
    }
}
