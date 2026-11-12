using System.Collections.Generic;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// Every graphics option, one per effect, and the <see cref="Rows"/> the settings menu groups them into. An effect adds
    /// its option with one line here and puts it in a row, then reads <see cref="GraphicsOption.On"/> or
    /// <see cref="GraphicsOption.Value"/> and, to react live, listens to <see cref="GraphicsOption.Changed"/>. The settings
    /// menu shows each row by itself. An option's default is its row's value at Medium, the quality level a new game starts
    /// on.
    /// </summary>
    public static class GraphicsOptions
    {
        private static readonly List<GraphicsOption> all = new List<GraphicsOption>();

        /// <summary>All of them.</summary>
        public static IReadOnlyList<GraphicsOption> All => all;

        /// <summary>Most real lights at night in the race: the rest of the night lighting is drawn glowing. Read at the start of a race.</summary>
        public static readonly GraphicsOption RealLights = Count("settings.reallights", 6, 24);

        /// <summary>A clear coat over the cars' paint. Changes live.</summary>
        public static readonly GraphicsOption Clearcoat = Toggle("settings.clearcoat", true);

        /// <summary>Metallic flakes in the cars' paint that glint in the sun. Changes live.</summary>
        public static readonly GraphicsOption PaintFlake = Toggle("settings.paintflake", true);

        /// <summary>Tinted, glossy car windows and mirrors that show reflections. Changes live.</summary>
        public static readonly GraphicsOption CarGlass = Toggle("settings.carglass", true);

        /// <summary>Clear lamp lenses that glow with the lamps behind them. Changes live.</summary>
        public static readonly GraphicsOption LampLenses = Toggle("settings.lamplenses", true);

        /// <summary>Carbon weave, rubber and metal on the cars' trim, wheels and tyres. Changes live.</summary>
        public static readonly GraphicsOption CarTrims = Toggle("settings.cartrims", true);

        /// <summary>The cars reflect the scene round them, refreshed every few frames. Changes live.</summary>
        public static readonly GraphicsOption CarReflections = Toggle("settings.carreflections", true);

        /// <summary>The filmic tone curve and the colour grade that follows the time of day and the weather.</summary>
        public static readonly GraphicsOption ColourGrading = Toggle("settings.grading", true);

        /// <summary>A glow round the brightest lights, drawn at half resolution.</summary>
        public static readonly GraphicsOption Bloom = Toggle("settings.bloom", true);

        /// <summary>Soft shadow in creases and where things meet the ground, worked out on the screen at half resolution.</summary>
        public static readonly GraphicsOption AmbientOcclusion = Toggle("settings.ssao", true);

        /// <summary>
        /// The sun's shadows worked out on the screen: soft edges that widen with the distance from what casts them, no seam
        /// between cascades, and contact shadows for the fine detail the shadow map misses. Changes live.
        /// </summary>
        public static readonly GraphicsOption SoftShadows = Toggle("settings.softshadows", false);

        /// <summary>Shadows from the real lights at night, the floodlights and headlamps near the camera. Read at the start of a race.</summary>
        public static readonly GraphicsOption LightShadows = Toggle("settings.lightshadows", false);

        /// <summary>
        /// Depth of field on the broadcast cameras (and any replay camera that asks for it): the background past the car on screen
        /// blurs, more through a long lens. Never while driving. Changes live.
        /// </summary>
        public static readonly GraphicsOption DepthOfField = Toggle("settings.dof", true);

        /// <summary>The race sky drawn with more care: haze at the horizon, a halo round the sun, finer and better lit clouds. Changes live.</summary>
        public static readonly GraphicsOption SkyDetail = Toggle("settings.skydetail", true);

        /// <summary>The fans in the grandstands, each one flat card turned to the camera. Changes live.</summary>
        public static readonly GraphicsOption Crowd = Toggle("settings.crowd", true);

        /// <summary>Grain and bumps in the tarmac and kerbs, broad patches across the tarmac, and its sealed cracks and patch repairs.</summary>
        public static readonly GraphicsOption TarmacDetail = Toggle("settings.tarmacdetail", true);

        /// <summary>A darker band of rubber laid down along the racing line, and rubber marks from locked and spinning tyres about it.</summary>
        public static readonly GraphicsOption RubberedLine = Toggle("settings.rubberedline", true);

        /// <summary>Rubber, chips and dust on the kerbs.</summary>
        public static readonly GraphicsOption KerbWear = Toggle("settings.kerbwear", true);

        /// <summary>
        /// The armco drawn as steel rails on posts, with catch fencing behind the tyre walls. Off, a plain band where the
        /// armco stands. Changes live.
        /// </summary>
        public static readonly GraphicsOption ArmcoDetail = Toggle("settings.armcodetail", true);

        /// <summary>Stripes mown into the grass along the track. Changes live.</summary>
        public static readonly GraphicsOption Mowing = Toggle("settings.mowing", true);

        /// <summary>Marks a sliding or spinning tyre leaves on the track during the race.</summary>
        public static readonly GraphicsOption SkidMarks = Toggle("settings.skidmarks", true);

        /// <summary>A wet track darkens, shines with the reflections and dries first along the racing line. Off, it only darkens and shines evenly. Changes live.</summary>
        public static readonly GraphicsOption WetTrack = Toggle("settings.wettrack", true);

        /// <summary>Water standing in the road's dips and along the edge its camber drains to. Needs the wet track. Changes live.</summary>
        public static readonly GraphicsOption Puddles = Toggle("settings.puddles", true);

        /// <summary>Raindrops ringing the puddles and the water on the track. Needs the wet track. Changes live.</summary>
        public static readonly GraphicsOption RainRipples = Toggle("settings.rainripples", true);

        /// <summary>Water thrown up behind the cars nearest the camera on a wet track. Changes live.</summary>
        public static readonly GraphicsOption Spray = Toggle("settings.spray", true);

        /// <summary>A fine mist hanging in the air behind fast cars on a wet track. Changes live.</summary>
        public static readonly GraphicsOption SprayMist = Toggle("settings.spraymist", true);

        /// <summary>Smoke off locked and spinning tyres, lit by the sun and the sky. Changes live.</summary>
        public static readonly GraphicsOption TyreSmoke = Toggle("settings.tyresmoke", true);

        /// <summary>Sparks off a car's underside where it bottoms out at speed. Changes live.</summary>
        public static readonly GraphicsOption Sparks = Toggle("settings.sparks", true);

        /// <summary>Brake discs glowing red and orange after a hard stop. Changes live.</summary>
        public static readonly GraphicsOption BrakeGlow = Toggle("settings.brakeglow", true);

        /// <summary>The air wobbling in the heat behind the cars' exhausts. Changes live.</summary>
        public static readonly GraphicsOption ExhaustHaze = Toggle("settings.exhausthaze", false);

        /// <summary>The air shimmering over far tarmac on a hot, sunny day. Changes live.</summary>
        public static readonly GraphicsOption TarmacShimmer = Toggle("settings.tarmacshimmer", false);

        /// <summary>
        /// Sharp reflections of what is on screen in the wet track, its puddles and the cars' clear coat, traced through the
        /// depth buffer. Where a ray finds nothing the reflection probes show instead. Changes live.
        /// </summary>
        public static readonly GraphicsOption ScreenReflections = Toggle("settings.ssr", false);

        /// <summary>The Team HQ lit by its bake: lamp light, daylight and every bounce. Off, live lamps and a flat ambient light.</summary>
        public static readonly GraphicsOption BakedLighting = Toggle("settings.hqbakedlighting", true);

        /// <summary>The Team HQ's reflections from a baked probe per room. Off, one live probe drawn when the scene loads.</summary>
        public static readonly GraphicsOption BakedReflections = Toggle("settings.hqbakedreflections", true);

        /// <summary>Oil, tyre marks and grime on the Team HQ's floor and walls.</summary>
        public static readonly GraphicsOption Wear = Toggle("settings.hqwear", true);

        private static readonly int[] OffAtLow = { 0, 1, 1, 1 };
        private static readonly int[] OnFromHigh = { 0, 0, 1, 1 };
        private static readonly int[] AlwaysOn = { 1, 1, 1, 1 };

        /// <summary>The settings menu's rows, in menu order. Every option is in one.</summary>
        public static readonly IReadOnlyList<GraphicsRow> Rows = new[]
        {
            new GraphicsRow("Real lights", new[] { 2, 6, 12, 20 }, RealLights),
            new GraphicsRow("Post effects", OffAtLow, ColourGrading, Bloom, AmbientOcclusion),
            new GraphicsRow("Shadow detail", OnFromHigh, SoftShadows, LightShadows),
            new GraphicsRow("Depth of field", OffAtLow, DepthOfField),
            new GraphicsRow("Car detail", OffAtLow, Clearcoat, PaintFlake, CarGlass, LampLenses, CarTrims, CarReflections, BrakeGlow),
            new GraphicsRow("Track detail", AlwaysOn, TarmacDetail, RubberedLine, KerbWear, SkidMarks, Mowing, ArmcoDetail),
            new GraphicsRow("Rain effects", OffAtLow, WetTrack, Puddles, RainRipples, Spray, SprayMist),
            new GraphicsRow("Smoke and sparks", OffAtLow, TyreSmoke, Sparks),
            new GraphicsRow("Heat haze", OnFromHigh, ExhaustHaze, TarmacShimmer),
            new GraphicsRow("Screen-space reflections", OnFromHigh, ScreenReflections),
            new GraphicsRow("Sky detail", OffAtLow, SkyDetail),
            new GraphicsRow("Crowd", AlwaysOn, Crowd),
            new GraphicsRow("HQ lighting", AlwaysOn, BakedLighting, BakedReflections, Wear),
        };

        /// <summary>Sets every row to its value at a quality level, 0 Low to 3 Ultra.</summary>
        public static void ApplyQuality(int level)
        {
            foreach (GraphicsRow row in Rows) row.ApplyQuality(level);
        }

        /// <summary>An option that is on or off.</summary>
        private static GraphicsOption Toggle(string key, bool on) => Add(new GraphicsOption(key, on ? 1 : 0, 1));

        /// <summary>An option that is a count from 0 to <paramref name="most"/>.</summary>
        private static GraphicsOption Count(string key, int defaultValue, int most) => Add(new GraphicsOption(key, defaultValue, most));

        private static GraphicsOption Add(GraphicsOption option)
        {
            all.Add(option);
            return option;
        }
    }
}
