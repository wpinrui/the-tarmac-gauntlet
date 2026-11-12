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

        /// <summary>
        /// Depth of field on the broadcast cameras (and any replay camera that asks for it): the background past the car on screen
        /// blurs, more through a long lens. Never while driving. Changes live.
        /// </summary>
        public static readonly GraphicsOption DepthOfField = Toggle("settings.dof", "Depth of field", true);

        /// <summary>The race sky drawn with more care: haze at the horizon, a halo round the sun, finer and better lit clouds. Changes live.</summary>
        public static readonly GraphicsOption SkyDetail = Toggle("settings.skydetail", "Sky detail", true);

        /// <summary>The fans in the grandstands, each one flat card turned to the camera. Changes live.</summary>
        public static readonly GraphicsOption Crowd = Toggle("settings.crowd", "Crowd", true);

        /// <summary>Grain and bumps in the tarmac and kerbs, and broad patches across the tarmac.</summary>
        public static readonly GraphicsOption TarmacDetail = Toggle("settings.tarmacdetail", "Tarmac detail", true);

        /// <summary>A darker band of rubber laid down along the racing line.</summary>
        public static readonly GraphicsOption RubberedLine = Toggle("settings.rubberedline", "Rubbered line", true);

        /// <summary>Rubber, chips and dust on the kerbs.</summary>
        public static readonly GraphicsOption KerbWear = Toggle("settings.kerbwear", "Kerb wear", true);

        /// <summary>Marks a sliding or spinning tyre leaves on the track during the race.</summary>
        public static readonly GraphicsOption SkidMarks = Toggle("settings.skidmarks", "Skid marks", true);

        /// <summary>A wet track darkens, shines with the reflections and dries first along the racing line. Off, it only darkens and shines evenly. Changes live.</summary>
        public static readonly GraphicsOption WetTrack = Toggle("settings.wettrack", "Wet track", true);

        /// <summary>Water standing in the road's dips and along the edge its camber drains to. Needs the wet track. Changes live.</summary>
        public static readonly GraphicsOption Puddles = Toggle("settings.puddles", "Puddles", true);

        /// <summary>Raindrops ringing the puddles and the water on the track. Needs the wet track. Changes live.</summary>
        public static readonly GraphicsOption RainRipples = Toggle("settings.rainripples", "Rain ripples", true);

        /// <summary>Water thrown up behind the cars nearest the camera on a wet track. Changes live.</summary>
        public static readonly GraphicsOption Spray = Toggle("settings.spray", "Spray", true);

        /// <summary>A fine mist hanging in the air behind fast cars on a wet track. Changes live.</summary>
        public static readonly GraphicsOption SprayMist = Toggle("settings.spraymist", "Spray mist", true);

        /// <summary>The Team HQ lit by its bake: lamp light, daylight and every bounce. Off, live lamps and a flat ambient light.</summary>
        public static readonly GraphicsOption BakedLighting = Toggle("settings.hqbakedlighting", "Baked lighting", true);

        /// <summary>The Team HQ's reflections from a baked probe per room. Off, one live probe drawn when the scene loads.</summary>
        public static readonly GraphicsOption BakedReflections = Toggle("settings.hqbakedreflections", "Baked reflections", true);

        /// <summary>Oil, tyre marks and grime on the Team HQ's floor and walls.</summary>
        public static readonly GraphicsOption Wear = Toggle("settings.hqwear", "Wear", true);

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
