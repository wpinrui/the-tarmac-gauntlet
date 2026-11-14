using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The night's exposure budget: every number that sets how bright the circuit's lamps are, in one place. The lamps are
    /// real URP spot lights (<see cref="FloodlightRig"/>, <see cref="HeadlampRig"/>), and every lit shader shades them
    /// through Rendering/Shaders/Lamps.hlsl, which enforces the caps below.
    ///
    /// Light here is irradiance in URP's units: a matte surface of albedo a lit with E gives back a times E of linear light,
    /// before the night's exposure (<see cref="Exposure"/> stops over the tone curve's own) and the ACES tone curve. The bloom
    /// takes light over 1.2 (PostEffects).
    /// - The floodlights light the track where they aim with <see cref="Floodlit"/>: the asphalt reads a clear dark grey and
    ///   the white lines a bright grey. Their intensities are set so the pools overlap evenly (<see cref="FloodlightLayout"/>).
    /// - One lamp gives any surface at most <see cref="LampMost"/>, however near: under what the floodlights give the track
    ///   together, so a headlamp's pool shows under them without outshining them, and lights the tail of the car in front
    ///   no brighter than that.
    /// - All lamps together give any surface at most <see cref="AllMost"/>, however many overlap: a white car under the
    ///   floodlights with the whole grid's headlamps on it gives back under 0.85 x 1.4, under the bloom's threshold.
    /// - A lamp-lit pixel gives back at most <see cref="PixelMost"/>, its sheen included: a glint can glow a little, a
    ///   surface can't flood the screen.
    /// LampLight holds the maths, and tools/night-budget checks these against the shader and sweeps them over the real circuit and a full grid.
    /// </summary>
    public static class NightBudget
    {
        /// <summary>The colour grade's exposure at full night, stops (SkyGrade).</summary>
        public const float Exposure = 1f;
        /// <summary>
        /// How much more bloom the night has than the day's 0.5: nothing lamp-lit reaches its threshold (the caps below), so
        /// it only gives the lamps themselves their halo.
        /// </summary>
        public const float Bloom = 0.5f;
                /// <summary>The floodlights' light on the track where they aim.</summary>
        public const float Floodlit = 1f;
        /// <summary>The most one lamp gives any surface, the most all of them together give, and the most a lamp-lit pixel gives back.</summary>
        public const float LampMost = 0.8f, AllMost = 1.4f, PixelMost = 1.6f;

        /// <summary>
        /// A headlamp light's intensity: it gives the road the most one lamp may give from a few metres ahead out to about
        /// 20 m, then falls away, lighting the road out to about 60 m.
        /// </summary>
        public const float HeadlampIntensity = 16000f;
        /// <summary>How far a headlamp light reaches, m, its cone and the full-strength core inside it, degrees.</summary>
        public const float HeadlampRange = 90f, HeadlampCone = 80f, HeadlampCore = 40f;
        /// <summary>A headlamp light sits this far ahead of the car's bumper, m, and aims this far below level, degrees.</summary>
        public const float HeadlampAhead = 0.3f, HeadlampDip = 2f;

        /// <summary>The floodlights' colour, a neutral white a touch warm, and the headlamps', a cool white, as Ivan picked.</summary>
        public static readonly Color Flood = new Color(1f, 0.95f, 0.86f), Headlamp = new Color(0.9f, 0.95f, 1f);
        public static readonly Color Red = new Color(1f, 0.06f, 0.03f);
        /// <summary>
        /// How bright the lamps themselves glow, linear light in their brightest channel (<see cref="Glow"/>): the floodlights'
        /// lenses, the headlamps and the tail lamps at night, the brake lamps whenever the brakes are on, and the pit
        /// building's brightest windows at night. All but the windows sit well over the bloom's threshold (1.2), so each
        /// lamp shows a halo; the brightest windows only reach it, so the halo round them is faint.
        /// </summary>
        public const float FloodLens = 10f, HeadlampGlow = 8f, TailGlow = 3f, BrakeGlow = 8f, WindowGlow = 1.6f;
        /// <summary>
        /// The pit building's glass smoothness at night, against its 0.92 by day: the floodlights on its roof light the
        /// glass from just above, and a sharp sheen there glared and bloomed over the windows' own light.
        /// </summary>
        public const float WindowSheen = 0.45f;
        /// <summary>
        /// A glow of this colour, linear, its brightest channel at <paramref name="level"/>. Material.SetColor takes sRGB,
        /// so a material is given its <c>.gamma</c>.
        /// </summary>
        public static Color Glow(Color tint, float level)
        {
            var c = new Color(LinearLight.Linear(tint.r), LinearLight.Linear(tint.g), LinearLight.Linear(tint.b), 1f);
            float most = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return new Color(c.r * level / most, c.g * level / most, c.b * level / most, 1f);
        }
        /// <summary>The stands' seats and crowd glow this much at night, lit by the lamps under their roofs.</summary>
        public const float StandGlow = 0.22f;

        /// <summary>
        /// How far the lamps are switched on for how dark it is for the lights (<see cref="TimeOfDay.Lights"/>): on from
        /// the first dusk, so the lenses and lamps glow as the sky goes.
        /// </summary>
        public static float On(float lights) => SkyClock.Smooth(0f, 0.12f, lights);
        /// <summary>
        /// How much of their light the floodlights throw for the sun's height, degrees: none while the sun is up, a little
        /// in the first dusk, all of it once the sky is dark, so they never wash out a daylit track.
        /// </summary>
        public static float FloodDark(float sunElevation) => SkyClock.Smooth(3f, 13f, -sunElevation);
        /// <summary>The same for the headlamps' light on the road.</summary>
        public static float HeadlampDark(float sunElevation) => SkyClock.Smooth(2f, 12f, -sunElevation);

        /// <summary>
        /// A light's colour as Lamps.hlsl weighs it against the caps: the luminance of the linear colour URP hands the shader.
        /// A light's intensity is divided by this, so its light is what the budget says.
        /// </summary>
        public static float Luma(Color c) =>
            0.2126f * LinearLight.Linear(c.r) + 0.7152f * LinearLight.Linear(c.g) + 0.0722f * LinearLight.Linear(c.b);

        /// <summary>
        /// How grass takes the lamps' light: its albedo this bright and with this much of its colour, against its own (1).
        /// A floodlit verge then sits near the road's brightness, a deep green, rather than glaring beside it.
        /// </summary>
        public const float GrassLamp = 0.45f, GrassColour = 0.6f;

        /// <summary>Grass's response as Lamps.hlsl reads it.</summary>
        public static Vector4 ShaderGrass => new Vector4(GrassLamp, GrassColour, 0f, 0f);

        /// <summary>The budget as Lamps.hlsl reads it: on, and the three caps.</summary>
        public static Vector4 ShaderBudget => new Vector4(1f, LampMost, AllMost, PixelMost);
    }
}
