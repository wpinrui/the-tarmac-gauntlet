using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What the weather does to the sky's light (<see cref="SkyClock"/>): cloud spreads over the sky,
    /// greys it and takes the sun's direct light and its shadows; rain darkens the cloud base and
    /// draws the haze in close. Both bring the lights on earlier (<see cref="Lights"/>). Once
    /// the floodlights are on their glow lifts the light, more under cloud.
    /// </summary>
    public static class WeatherSky
    {
        /// <summary>Colour of the floodlights' glow on the night sky.</summary>
        private static readonly Color Floodlit = new Color(0.52f, 0.5f, 0.46f);
        /// <summary>How much the floodlights' glow lifts the night: on a clear night, and more under cloud.</summary>
        public const float NightGlow = 0.08f, CloudGlow = 0.2f;

        /// <summary>Cloud density threshold for the sky shader, from the share of the sky under cloud: lower is more cloud.</summary>
        public static float Cover(float cloud) => Mathf.Lerp(0.8f, 0.1f, cloud);

        /// <summary>How hard the rain falls, 0 to 1: a shower of 3 mm an hour is about half way.</summary>
        public static float Heavy(float rain) => 1f - Mathf.Exp(-rain / 4f);

        /// <summary>Distance the haze hides everything at, m: the clear day's haze, closing in to under a kilometre in a downpour.</summary>
        public static float Haze(float clear, WeatherState w)
        {
            float grey = Mathf.Lerp(clear, clear * 0.55f, Overcast(w.Cloud));
            return Mathf.Lerp(grey, Downpour, Heavy(w.Rain));
        }

        /// <summary>Distance the haze hides everything at in the heaviest rain, m.</summary>
        public const float Downpour = 900f;

        /// <summary>How overcast the sky is, 0 to 1: fair weather cumulus up to about a third of the sky leaves the clear day's look alone.</summary>
        public static float Overcast(float cloud) => SkyClock.Smooth(0.35f, 1f, cloud);

        /// <summary>Share of the sun's direct light that gets through the cloud.</summary>
        public static float Direct(WeatherState w) => Mathf.Clamp01(1f - 0.88f * Overcast(w.Cloud) - 0.1f * Heavy(w.Rain));

        /// <summary>
        /// Degrees a full overcast, and a downpour on top, bring dusk forward for the lights: under a thick grey deck the
        /// floodlights and headlamps come on around sunset, and in heavy rain from late afternoon.
        /// </summary>
        public const float CloudDusk = 9f, RainDusk = 5f;

        /// <summary>
        /// Race seconds the lights take to settle as the weather darkens, and as it brightens again: they come on within minutes
        /// of the gloom and stay on through a passing gap in the cloud.
        /// </summary>
        public const float DuskDarkens = 600f, DuskClears = 2700f;

        /// <summary>Degrees this weather brings dusk forward by for the lights: none in fair weather.</summary>
        public static float Dusk(WeatherState w) => CloudDusk * Overcast(w.Cloud) + RainDusk * Heavy(w.Rain);

        /// <summary>The weather's dusk for the lights (<see cref="Dusk"/>) eased this many race seconds on towards the weather now.</summary>
        public static float Settle(float dusk, WeatherState w, float raceSeconds)
        {
            float target = Dusk(w);
            float settle = target > dusk ? DuskDarkens : DuskClears;
            return Mathf.Lerp(dusk, target, 1f - Mathf.Exp(-Mathf.Max(raceSeconds, 0f) / settle));
        }

        /// <summary>
        /// How dark it is for the lights, 0 to 1 (<see cref="SkyState.Lights"/>): the clear sky's darkness with the sun this
        /// many degrees of settled weather dusk lower (<see cref="Settle"/>).
        /// </summary>
        public static float Lights(float sunElevation, float dusk) => SkyClock.Dark(sunElevation - dusk);

        /// <summary>How much brighter the sky must get under weather before lights it brought on go off again.</summary>
        public const float Play = 0.15f;

        /// <summary>
        /// The lights' darkness kept from <paramref name="held"/> where it can: it rises with <paramref name="lights"/> at
        /// once, but under weather falls only once the sky is <see cref="Play"/> brighter, so a sunrise under a shower never
        /// switches them on and off again. Under a clear sky it is <paramref name="lights"/> exactly.
        /// </summary>
        public static float Hold(float held, float lights, float dusk) =>
            Mathf.Clamp(held, lights, lights + Play * Mathf.Clamp01(dusk / 3f));

        /// <summary>Greys and darkens the sky, its light and its clouds under this weather, with the lights' darkness under it (<see cref="Hold"/>).</summary>
        public static void Apply(ref SkyState s, WeatherState w, float lights)
        {
            float o = Overcast(w.Cloud);
            float heavy = Heavy(w.Rain);
            float daylight = s.Horizon.grayscale;
            float baseLight = daylight * Mathf.Lerp(0.9f, 0.45f, heavy);
            Color baseGrey = new Color(baseLight, baseLight * 1.01f, baseLight * 1.04f);

            float direct = Direct(w);
            s.LightIntensity *= direct;
            s.Glow *= 1f - o;
            s.Zenith = Color.Lerp(s.Zenith, baseGrey * 0.92f, o * 0.9f);
            s.Horizon = Color.Lerp(s.Horizon, baseGrey, o * 0.85f);
            s.CloudLit = Color.Lerp(s.CloudLit, baseGrey, o);
            // An overcast deck is one soft grey: little light and shade between its clouds.
            s.CloudShade = Color.Lerp(s.CloudShade, baseGrey * 0.8f, o);
            // Overcast light comes from the whole sky: the sky and the horizon light the scene evenly.
            Color skyLight = baseGrey * Mathf.Lerp(1f, 1.25f, o * (1f - heavy));
            s.AmbientSky = Color.Lerp(s.AmbientSky, skyLight, o);
            s.AmbientEquator = Color.Lerp(s.AmbientEquator, skyLight * 0.85f, o);
            s.Fog = Color.Lerp(s.Fog, baseGrey * 0.95f, Mathf.Max(o * 0.8f, heavy));
            // At night the circuit's floodlights light the haze and the underside of the cloud, so a
            // cloudy night is brighter than a clear one and the track never goes black.
            s.Lights = lights;
            float skyglow = s.Lights * (NightGlow + CloudGlow * o);
            s.AmbientSky += Floodlit * (skyglow * 1.5f);
            s.AmbientEquator += Floodlit * (skyglow * 1.3f);
            s.AmbientGround += Floodlit * (skyglow * 0.6f);
            s.Horizon += Floodlit * (skyglow * 0.8f);
            s.CloudLit += Floodlit * (skyglow * 1.6f);
            s.CloudShade += Floodlit * (skyglow * 1.2f);
            s.Fog += Floodlit * (skyglow * 0.8f);
            s.Zenith.a = s.Horizon.a = s.CloudLit.a = s.CloudShade.a = s.AmbientSky.a = s.AmbientEquator.a = s.AmbientGround.a = s.Fog.a = 1f;
        }
    }
}
