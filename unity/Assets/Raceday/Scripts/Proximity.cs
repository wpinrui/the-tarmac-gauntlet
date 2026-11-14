using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How close a car behind is, for the proximity arrows (<see cref="ProximityArrows"/>). Level 1 says get ready to
    /// swipe across, 2 says swipe now, 3 says keep straight: their nose is past your rear bumper. Each level starts at
    /// whichever is further back, a set distance or a set time at the speed they close on you, so a much faster car
    /// lights up from further away. Once their middle draws level with yours they count as ahead, and the arrows go.
    /// All in your car's frame: side is metres to your left (negative) or right, ahead metres from your middle to
    /// theirs along your heading.
    /// </summary>
    public static class Proximity
    {
        /// <summary>Where level 2 and level 1 start, metres from your rear bumper back to their nose, at the least.</summary>
        public const float Level2Metres = 5f, Level1Metres = 15f;
        /// <summary>Where level 2 and level 1 start, seconds before their nose reaches your rear bumper.</summary>
        public const float Level2Seconds = 0.8f, Level1Seconds = 2f;
        /// <summary>Cars further to the side than this, m, or above or below by more than <see cref="MaxRise"/>, are elsewhere on the circuit.</summary>
        public const float MaxSide = 6f, MaxRise = 3f;
        /// <summary>The least cosine between the two headings: a car pointing much elsewhere is not racing you.</summary>
        public const float MinFacing = 0.5f;

        /// <summary>
        /// 0 for none, else 1 to 3. half and theirHalf are half the two cars' lengths, m; closing how fast they gain on
        /// you, m/s; facing the cosine between the headings.
        /// </summary>
        public static int Level(float side, float ahead, float rise, float facing, float half, float theirHalf, float closing)
        {
            if (ahead >= 0f || Math.Abs(side) > MaxSide || Math.Abs(rise) > MaxRise || facing < MinFacing) return 0;
            float gap = -(ahead + half + theirHalf);   // from your rear bumper back to their nose
            if (gap < 0f) return 3;
            float v = Math.Max(0f, closing);
            if (gap < Math.Max(Level2Metres, Level2Seconds * v)) return 2;
            if (gap < Math.Max(Level1Metres, Level1Seconds * v)) return 1;
            return 0;
        }

        /// <summary>Degrees round from straight behind to the car: 0 behind, 90 alongside.</summary>
        public static float Bearing(float side, float ahead)
        {
            double deg = Math.Atan2(Math.Abs(side), -ahead) * 180.0 / Math.PI;
            return (float)Math.Min(90.0, deg);
        }
    }
}
