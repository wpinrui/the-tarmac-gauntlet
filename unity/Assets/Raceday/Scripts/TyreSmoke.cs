namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// When a tyre smokes. Only a real, held slide does: a tyre has to slide past <see cref="From"/> for
    /// <see cref="Hold"/> seconds before it puffs, so a wheel that skips over a bump or a gearchange on a straight stays
    /// clean. Past that, the smoke thickens smoothly with the slide.
    /// </summary>
    public static class TyreSmoke
    {
        /// <summary>A tyre sliding less than this makes no smoke, 0 to 1 of <see cref="CarController.Skid"/>.</summary>
        public const float From = 0.35f;
        /// <summary>Seconds a tyre must keep sliding before it smokes.</summary>
        public const float Hold = 0.15f;

        /// <summary>
        /// Advances one tyre's slide by dt and returns how hard it smokes now, 0 to 1. <paramref name="held"/> is the
        /// tyre's own running count of seconds slid, kept by the caller between frames.
        /// </summary>
        public static float Step(ref float held, float skid, float dt)
        {
            if (!(skid > From))
            {
                held = 0f;
                return 0f;
            }
            held += dt;
            if (held < Hold) return 0f;
            float t = (skid - From) / (1f - From);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }
    }
}
