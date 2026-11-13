namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The hold of Enter that skips a stage. Held, it fills over <see cref="Seconds"/>; let go early, it drains
    /// back faster. A full hold skips one stage and empties, and the next stage only fills once Enter has been
    /// let go, so one hold never skips two.
    /// </summary>
    public sealed class SkipHold
    {
        /// <summary>How long Enter is held to skip, real seconds.</summary>
        public const float Seconds = 1f;
        /// <summary>How long a full hold takes to drain once let go, real seconds.</summary>
        public const float DrainSeconds = 0.25f;

        /// <summary>How full the hold is, 0 to 1: the sector's share of the circle.</summary>
        public float Fill { get; private set; }

        /// <summary>True once Enter has been up since the last skip or stage change.</summary>
        private bool armed;

        /// <summary>Steps the hold by dt real seconds. True on the step it fills: skip the stage.</summary>
        public bool Step(bool held, float dt)
        {
            if (!held)
            {
                armed = true;
                Fill = System.Math.Max(0f, Fill - dt / DrainSeconds);
                return false;
            }
            if (!armed) return false;
            Fill = System.Math.Min(1f, Fill + dt / Seconds);
            if (Fill < 1f) return false;
            Reset();
            return true;
        }

        /// <summary>Empties the hold, and waits for Enter to be let go before it fills again.</summary>
        public void Reset()
        {
            Fill = 0f;
            armed = false;
        }
    }
}
