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
                case LandUse.Meadow: return new Color(0.27f, 0.40f, 0.15f);
                case LandUse.Pasture: return new Color(0.21f, 0.35f, 0.12f);
                case LandUse.Wheat: return new Color(0.55f, 0.50f, 0.26f);
                case LandUse.Hay: return new Color(0.44f, 0.45f, 0.22f);
                case LandUse.Forest: return new Color(0.13f, 0.17f, 0.08f);
                default: return new Color(0.21f, 0.36f, 0.13f);
            }
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
