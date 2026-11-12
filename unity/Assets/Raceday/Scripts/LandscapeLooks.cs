using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>The landscape looks to choose from, and the colours of the land.</summary>
    public static class LandscapeLooks
    {
        /// <summary>Forest close round much of the lap, mixed broadleaf and conifer, on gentle hills.</summary>
        public static readonly LandscapeLook Woodland = new LandscapeLook { Forest = 0.62f, Conifer = 0.35f, Hedges = 0.5f, Copses = 0.35f, Hills = 140f };

        /// <summary>A patchwork of fields with hedges, copses and small woods, on low rolling hills.</summary>
        public static readonly LandscapeLook Farmland = new LandscapeLook { Forest = 0.22f, Conifer = 0.15f, Hedges = 0.85f, Copses = 0.55f, Hills = 100f };

        /// <summary>Dark conifer forest on taller hills, with clearings of pasture.</summary>
        public static readonly LandscapeLook Highland = new LandscapeLook { Forest = 0.58f, Conifer = 0.85f, Hedges = 0.2f, Copses = 0.3f, Hills = 320f };

        /// <summary>The look the race uses.</summary>
        public static LandscapeLook Current = Woodland;

        /// <summary>Ground colour by land use, multiplied over the grass grain.</summary>
        public static Color Ground(LandUse use)
        {
            switch (use)
            {
                case LandUse.Meadow: return new Color(0.30f, 0.38f, 0.21f);
                case LandUse.Pasture: return new Color(0.24f, 0.33f, 0.18f);
                case LandUse.Wheat: return new Color(0.55f, 0.50f, 0.28f);
                case LandUse.Hay: return new Color(0.44f, 0.44f, 0.25f);
                case LandUse.Forest: return new Color(0.14f, 0.165f, 0.10f);
                default: return new Color(0.25f, 0.34f, 0.18f);
            }
        }

        /// <summary>
        /// Ground colour at a point: the land use's, varied in broad patches tens of metres across, greener and darker in
        /// some, drier, yellower and lighter in others, as real grass is.
        /// </summary>
        public static Color Field(LandUse use, float x, float z)
        {
            Color col = Ground(use);
            float light = 0.92f + 0.16f * LandscapePlan.Noise(x / 70f, z / 70f, 154);
            if (use == LandUse.Forest) return col * light;
            float dry = Mathf.Clamp((0.65f * LandscapePlan.Noise(x / 110f, z / 110f, 152) + 0.35f * LandscapePlan.Noise(x / 40f, z / 40f, 153) - 0.5f) * 3f, -1f, 1f);
            if (use == LandUse.Wheat) dry *= 0.4f;
            return new Color(col.r * (1f + 0.2f * dry), col.g * (1f + 0.06f * dry), col.b * (1f - 0.24f * dry)) * light;
        }

        /// <summary>
        /// <see cref="Field"/>, but mown grass left long, beyond where the mower stopped, goes most of the way to meadow.
        /// </summary>
        public static Color Grass(LandUse use, float x, float z, bool uncut)
        {
            Color col = Field(use, x, z);
            if (use != LandUse.Mown || !uncut) return col;
            Color m = Field(LandUse.Meadow, x, z);
            return new Color(col.r + (m.r - col.r) * 0.6f, col.g + (m.g - col.g) * 0.6f, col.b + (m.b - col.b) * 0.6f);
        }

        /// <summary>How tall the grass grows on the land, m: short where it is mown, longer where it is left, none in the woods.</summary>
        public static float GrassHeight(LandUse use, bool uncut)
        {
            switch (use)
            {
                case LandUse.Mown: return uncut ? 0.22f : 0.07f;
                case LandUse.Meadow: return 0.38f;
                case LandUse.Pasture: return 0.14f;
                case LandUse.Hay: return 0.3f;
                case LandUse.Wheat: return 0.55f;
                default: return 0f;
            }
        }

        /// <summary>Leaf tint by kind of tree and hue (<see cref="TreeSpot.Hue"/>), multiplied over the baked colour.</summary>
        public static Color Leaves(TreeKind kind, float hue)
        {
            Color c = Leaves(kind);
            float warm = Mathf.Max(hue, 0f), cool = Mathf.Max(-hue, 0f);
            return new Color(c.r * (1f + 0.2f * warm - 0.12f * cool), c.g * (1f + 0.07f * warm - 0.04f * cool), c.b * (1f - 0.25f * warm + 0.06f * cool));
        }

        /// <summary>Leaf tint by kind of tree, multiplied over the baked colour.</summary>
        public static Color Leaves(TreeKind kind)
        {
            switch (kind)
            {
                case TreeKind.Oak: return new Color(0.82f, 0.98f, 0.72f);
                case TreeKind.Beech: return new Color(0.86f, 1.02f, 0.70f);
                case TreeKind.Birch: return new Color(0.95f, 1.02f, 0.85f);
                case TreeKind.PineA: case TreeKind.PineB: return new Color(0.80f, 0.92f, 0.78f);
                case TreeKind.SpruceA: case TreeKind.SpruceB: return new Color(0.80f, 0.95f, 0.85f);
                default: return new Color(0.78f, 0.98f, 0.70f);
            }
        }
    }
}
