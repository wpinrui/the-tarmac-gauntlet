using System.Collections.Generic;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// Every graphics option, in the order the settings menu lists them under its built-in rows. An effect adds its
    /// option with one line here, then reads <see cref="GraphicsOption.On"/> or <see cref="GraphicsOption.Value"/> and,
    /// to react live, listens to <see cref="GraphicsOption.Changed"/>. The settings menu shows a row for each by itself.
    /// </summary>
    public static class GraphicsOptions
    {
        private static readonly List<GraphicsOption> all = new List<GraphicsOption>();

        /// <summary>All of them, in menu order.</summary>
        public static IReadOnlyList<GraphicsOption> All => all;

        /// <summary>Most real lights at night in the race: the rest of the night lighting is drawn glowing. Read at the start of a race.</summary>
        public static readonly GraphicsOption RealLights = Count("settings.reallights", "Real lights", 6, 24);

        /// <summary>Grain and bumps in the tarmac and kerbs, and broad patches across the tarmac.</summary>
        public static readonly GraphicsOption TarmacDetail = Toggle("settings.tarmacdetail", "Tarmac detail", true);

        /// <summary>A darker band of rubber laid down along the racing line.</summary>
        public static readonly GraphicsOption RubberedLine = Toggle("settings.rubberedline", "Rubbered line", true);

        /// <summary>Rubber, chips and dust on the kerbs.</summary>
        public static readonly GraphicsOption KerbWear = Toggle("settings.kerbwear", "Kerb wear", true);

        /// <summary>Marks a sliding or spinning tyre leaves on the track during the race.</summary>
        public static readonly GraphicsOption SkidMarks = Toggle("settings.skidmarks", "Skid marks", true);

        /// <summary>An option that is on or off.</summary>
        private static GraphicsOption Toggle(string key, string label, bool on) => Add(new GraphicsOption(key, label, on ? 1 : 0, 1));

        /// <summary>An option that is a count from 0 to <paramref name="most"/>, shown as a slider.</summary>
        private static GraphicsOption Count(string key, string label, int defaultValue, int most) => Add(new GraphicsOption(key, label, defaultValue, most));

        private static GraphicsOption Add(GraphicsOption option)
        {
            all.Add(option);
            return option;
        }
    }
}
