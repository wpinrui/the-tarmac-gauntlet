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

        /// <summary>A clear coat over the cars' paint. Changes live.</summary>
        public static readonly GraphicsOption Clearcoat = Toggle("settings.clearcoat", "Clearcoat paint", true);

        /// <summary>Metallic flakes in the cars' paint that glint in the sun. Changes live.</summary>
        public static readonly GraphicsOption PaintFlake = Toggle("settings.paintflake", "Metallic flake", true);

        /// <summary>Tinted, glossy car windows and mirrors that show reflections. Changes live.</summary>
        public static readonly GraphicsOption CarGlass = Toggle("settings.carglass", "Car glass", true);

        /// <summary>Clear lamp lenses that glow with the lamps behind them. Changes live.</summary>
        public static readonly GraphicsOption LampLenses = Toggle("settings.lamplenses", "Lamp lenses", true);

        /// <summary>Carbon weave, rubber and metal on the cars' trim, wheels and tyres. Changes live.</summary>
        public static readonly GraphicsOption CarTrims = Toggle("settings.cartrims", "Car trims", true);

        /// <summary>The cars reflect the scene round them, refreshed every few frames. Changes live.</summary>
        public static readonly GraphicsOption CarReflections = Toggle("settings.carreflections", "Car reflections", true);

        /// <summary>The filmic tone curve and the colour grade that follows the time of day and the weather.</summary>
        public static readonly GraphicsOption ColourGrading = Toggle("settings.grading", "Colour grading", true);

        /// <summary>A glow round the brightest lights, drawn at half resolution.</summary>
        public static readonly GraphicsOption Bloom = Toggle("settings.bloom", "Bloom", true);

        /// <summary>Soft shadow in creases and where things meet the ground, worked out on the screen at half resolution.</summary>
        public static readonly GraphicsOption AmbientOcclusion = Toggle("settings.ssao", "Ambient occlusion", true);

        /// <summary>A blur along the camera's own movement.</summary>
        public static readonly GraphicsOption MotionBlur = Toggle("settings.motionblur", "Motion blur", true);

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
