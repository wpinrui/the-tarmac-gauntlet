using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How hot a car's brake discs run, for their glow (<see cref="CarEffects"/>). Each axle's discs take their share of the
    /// braking power and shed heat to the air, faster at speed. A hard stop from a top speed heats a quick car's front discs
    /// to a dull red and a long one to orange; the next straight cools them. Only for the look: the brakes never fade.
    /// </summary>
    public static class BrakeHeat
    {
        /// <summary>The air round the discs, and a disc at rest, °C.</summary>
        public const float Ambient = 25f;
        /// <summary>
        /// Heat that warms one disc a degree, J/°C for each kg of car: a heavier car has bigger discs, so how hot a stop
        /// gets them follows how fast it was going and how hard it stops. Lighter than real discs, so a hard stop shows.
        /// </summary>
        public const float CapacityPerKg = 1f;
        /// <summary>Share of the heat over the air a disc sheds a second standing still, and more for each m/s.</summary>
        public const float CoolStill = 0.04f, CoolPerSpeed = 0.004f;
        /// <summary>Glow begins at this temperature and is at its brightest at the next, °C.</summary>
        public const float GlowFrom = 420f, GlowFull = 850f;
        /// <summary>The hottest a disc gets, °C.</summary>
        public const float Hottest = 1000f;

        /// <summary>
        /// A disc's temperature after <paramref name="dt"/> seconds. <paramref name="brake"/> is the pedal, 0 to 1,
        /// <paramref name="decel"/> the car's full braking in g, <paramref name="share"/> this disc's share of the braking
        /// and <paramref name="speed"/> the car's speed in m/s. The car's mass drops out: its discs are sized to it.
        /// </summary>
        public static float Step(float temperature, float dt, float brake, float decel, float share, float speed)
        {
            if (dt <= 0f) return temperature;
            speed = Mathf.Max(speed, 0f);
            // Braking power for each kg of car, W/kg.
            float power = Mathf.Clamp01(brake) * decel * 9.81f * speed * share;
            float warmed = temperature + power * dt / CapacityPerKg;
            float cool = CoolStill + CoolPerSpeed * speed;
            // Cooling settles exactly, so a long frame never overshoots the air.
            warmed = Ambient + (warmed - Ambient) * Mathf.Exp(-cool * dt);
            return Mathf.Clamp(warmed, Ambient, Hottest);
        }

        /// <summary>How brightly a disc at this temperature glows, 0 to 1.</summary>
        public static float Glow(float temperature)
        {
            float t = Mathf.Clamp01((temperature - GlowFrom) / (GlowFull - GlowFrom));
            return t * t;
        }

        /// <summary>
        /// The glow's colour at a brightness from <see cref="Glow"/>, in linear HDR: a deep red first, then orange, then a
        /// yellowing orange at the hottest, as hot iron goes.
        /// </summary>
        public static Color Colour(float glow)
        {
            glow = Mathf.Clamp01(glow);
            var red = new Color(0.9f, 0.05f, 0.005f);
            var orange = new Color(1f, 0.32f, 0.04f);
            var yellow = new Color(1f, 0.62f, 0.2f);
            Color c = glow < 0.6f ? Color.Lerp(red, orange, glow / 0.6f) : Color.Lerp(orange, yellow, (glow - 0.6f) / 0.4f);
            return c * (glow * 6f);
        }
    }
}
