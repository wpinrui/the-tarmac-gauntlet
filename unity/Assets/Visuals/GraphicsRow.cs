using UnityEngine;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// One row of the settings menu's graphics options: a label over one or more <see cref="GraphicsOption"/>s that it sets
    /// together. An on/off row shows On while any of its options is on, and sets them all. A count row has one option. Each
    /// quality level gives the row a value, which picking that level sets.
    /// </summary>
    public sealed class GraphicsRow
    {
        public readonly string Label;
        public readonly GraphicsOption[] Options;
        /// <summary>The row's value at each quality level, Low to Ultra.</summary>
        private readonly int[] byLevel;

        internal GraphicsRow(string label, int[] byLevel, params GraphicsOption[] options)
        {
            Label = label;
            Options = options;
            this.byLevel = byLevel;
        }

        /// <summary>True for an on/off row, false for a count.</summary>
        public bool IsToggle => Options[0].IsToggle;

        /// <summary>The highest value: 1 for an on/off row.</summary>
        public int Most => Options[0].Most;

        public int Value
        {
            get
            {
                if (!IsToggle) return Options[0].Value;
                foreach (GraphicsOption option in Options)
                    if (option.On) return 1;
                return 0;
            }
            set
            {
                foreach (GraphicsOption option in Options) option.Value = value;
            }
        }

        /// <summary>Sets the row to its value at a quality level, 0 Low to 3 Ultra.</summary>
        public void ApplyQuality(int level) => Value = byLevel[Mathf.Clamp(level, 0, byLevel.Length - 1)];
    }
}
