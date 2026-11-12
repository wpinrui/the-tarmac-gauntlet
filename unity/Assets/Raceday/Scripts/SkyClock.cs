using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The colours of one sky through the day: what the zenith and horizon look like by day, in
    /// twilight and at night, the sunset glow, how much cloud there is, how far the haze reaches and
    /// how bright the moon lights the track.
    /// </summary>
    public struct SkyLook
    {
        public Color DayZenith, DayHorizon, TwilightZenith, TwilightHorizon, NightZenith, NightHorizon, Sunset;
        /// <summary>Cloud density threshold: higher is less cloud.</summary>
        public float Cover;
        /// <summary>Distance where the haze hides everything (all but 2% of it) in the afternoon, m.</summary>
        public float Haze;
        /// <summary>Share of the haze distance the morning mist takes away round sunrise.</summary>
        public float Mist;
        /// <summary>Moonlight intensity with the moon high and the sun well down.</summary>
        public float Moon;
    }

    /// <summary>The sky and its light at one moment.</summary>
    public struct SkyState
    {
        /// <summary>Unit vectors towards the sun and the moon, +Z north, +X east.</summary>
        public Vector3 SunDir, MoonDir;
        /// <summary>Sun height above the horizon, degrees.</summary>
        public float SunElevation;
        /// <summary>Unit vector towards whichever of the sun or moon lights the scene now.</summary>
        public Vector3 LightDir;
        public Color LightColour;
        public float LightIntensity;
        public Color Zenith, Horizon, Glow;
        public Color AmbientSky, AmbientEquator, AmbientGround, Fog;
        public Color CloudLit, CloudShade;
        /// <summary>Distance where the haze hides everything (all but 2% of it), m: closer in the morning mist.</summary>
        public float Haze;
        /// <summary>0 by day, 1 when the stars are fully out.</summary>
        public float Night;
        /// <summary>
        /// How dark it is for the circuit's lights, 0 to 1, on the same scale as <see cref="Night"/>: the same under a
        /// clear sky, earlier under cloud and rain (<see cref="WeatherSky.Lights"/>).
        /// </summary>
        public float Lights;
        /// <summary>How far the clear day's deeper palette is on (<see cref="SkyPalette"/>), 0 to 1: none at night or under a grey sky.</summary>
        public float Deep;
        /// <summary>How golden the low sun's light is (<see cref="SkyPalette"/>), 0 to 1.</summary>
        public float Golden;
    }

    /// <summary>
    /// The sun and moon over a circuit at 50 degrees north in late October, after the clocks go back, and the sky's colours
    /// as the sun climbs and sets. Times are local standard time, hours from midnight. The sun sets at
    /// about 17:25, the sky is fully dark by about 18:35, and the sun rises at about 07:10.
    /// </summary>
    public static class SkyClock
    {
        public const float Latitude = 50f;
        /// <summary>The sun's declination in late October, degrees.</summary>
        public const float Declination = -12f;
        /// <summary>The moon's declination: near full, it sits opposite the sun, high in the south.</summary>
        public const float MoonDeclination = 12f;
        /// <summary>Local time of the sun's highest point, hours: standard time at about 6 degrees east.</summary>
        public const float SolarNoon = 12.3f;
        /// <summary>When the morning mist is thickest, local time, and how many hours it takes to come and go.</summary>
        public const float MistHour = 7.6f, MistHours = 2.5f;
        /// <summary>The haze's optical depth at <see cref="SkyState.Haze"/>, ln 50: it hides everything there but 2% of the light.</summary>
        public const float HazeDepth = 3.912f;

        /// <summary>The look of the race's sky.</summary>
        public static readonly SkyLook Look = new SkyLook
        {
            DayZenith = new Color(0.26f, 0.47f, 0.76f),
            DayHorizon = new Color(0.78f, 0.84f, 0.89f),
            TwilightZenith = new Color(0.10f, 0.16f, 0.36f),
            TwilightHorizon = new Color(0.55f, 0.45f, 0.48f),
            NightZenith = new Color(0.02f, 0.035f, 0.08f),
            NightHorizon = new Color(0.05f, 0.07f, 0.12f),
            Sunset = new Color(1f, 0.48f, 0.18f),
            Cover = 0.58f,
            Haze = 6000f,
            Mist = 0.45f,
            Moon = 0.18f,
        };

        private static readonly Color MoonColour = new Color(0.62f, 0.72f, 1f);
        private static readonly Color GroundTint = new Color(0.36f, 0.36f, 0.24f);

        /// <summary>Unit vector towards a body of this declination at this local time.</summary>
        public static Vector3 Direction(float hour, float declination)
        {
            float h = (hour - SolarNoon) * 15f * Mathf.Deg2Rad;
            float phi = Latitude * Mathf.Deg2Rad, dec = declination * Mathf.Deg2Rad;
            // Equatorial to horizon frame: east, north and up components.
            float east = -Mathf.Cos(dec) * Mathf.Sin(h);
            float north = Mathf.Sin(dec) * Mathf.Cos(phi) - Mathf.Cos(dec) * Mathf.Cos(h) * Mathf.Sin(phi);
            float up = Mathf.Sin(dec) * Mathf.Sin(phi) + Mathf.Cos(dec) * Mathf.Cos(h) * Mathf.Cos(phi);
            return new Vector3(east, up, north);
        }

        /// <summary>The sky at this local time, in hours; any number of days on wraps round.</summary>
        public static SkyState At(float hour, SkyLook look)
        {
            hour = Mathf.Repeat(hour, 24f);
            var s = new SkyState
            {
                SunDir = Direction(hour, Declination),
                MoonDir = Direction(hour - 12f, MoonDeclination),
            };
            float e = Mathf.Asin(Mathf.Clamp(s.SunDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float moonE = Mathf.Asin(Mathf.Clamp(s.MoonDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            s.SunElevation = e;

            float sun = 1.25f * Smooth(-1.5f, 4f, e) * (0.35f + 0.65f * Smooth(0f, 35f, e));
            float moon = look.Moon * (1f - Smooth(-8f, -2f, e)) * Smooth(-2f, 8f, moonE);
            if (sun > 0f || e > -2f)
            {
                s.LightDir = s.SunDir;
                s.LightIntensity = sun;
                s.LightColour = SunColour(e);
            }
            else
            {
                s.LightDir = s.MoonDir;
                s.LightIntensity = moon;
                s.LightColour = MoonColour;
            }

            float twilight = Smooth(-16f, -5f, e), day = Smooth(-5f, 12f, e);
            s.Zenith = Mix(Mix(look.NightZenith, look.TwilightZenith, twilight), look.DayZenith, day);
            s.Horizon = Mix(Mix(look.NightHorizon, look.TwilightHorizon, twilight), look.DayHorizon, day);
            s.Glow = look.Sunset * (Smooth(-8f, -1f, e) * (1f - Smooth(4f, 20f, e)));

            s.AmbientSky = s.Zenith * 0.4f + s.Horizon * 0.6f;
            s.AmbientEquator = s.Horizon * 0.72f + s.Glow * 0.25f;
            s.AmbientGround = Multiply(s.Horizon, GroundTint);
            s.Fog = s.Horizon * 0.95f + s.Glow * 0.15f;
            s.CloudLit = s.LightColour * (s.LightIntensity * 0.75f) + s.AmbientSky * 0.55f + s.Glow * 0.6f;
            s.CloudShade = s.AmbientEquator * 0.9f + s.Glow * 0.2f;
            s.Haze = look.Haze * (1f - look.Mist * Misty(hour));
            s.Night = Dark(e);
            s.Lights = s.Night;
            Opaque(ref s);
            return s;
        }

        /// <summary>
        /// Exponential fog density, per m, for a haze that hides everything but 2% at this distance: the share of the light
        /// it lets through falls by the same fraction every metre, so the far trees fade layer by layer into the air.
        /// </summary>
        public static float Density(float haze) => HazeDepth / haze;

        /// <summary>How thick the morning mist is at this local time, 0 to 1: it settles before dawn and burns off by late morning.</summary>
        public static float Misty(float hour)
        {
            float d = Mathf.Repeat(hour - MistHour + 12f, 24f) - 12f;
            d /= MistHours;
            return Mathf.Exp(-d * d);
        }

        /// <summary>How dark the sky is with the sun this many degrees up: 0 by day, 1 when the stars are fully out.</summary>
        public static float Dark(float sunElevation) => 1f - Smooth(-12f, -4f, sunElevation);

        /// <summary>Deep orange at the horizon, warm white once the sun is high.</summary>
        private static Color SunColour(float e)
        {
            Color a = new Color(1f, 0.42f, 0.18f), b = new Color(1f, 0.68f, 0.42f), c = new Color(1f, 0.88f, 0.72f), d = new Color(1f, 0.97f, 0.92f);
            if (e < 5f) return Mix(a, b, Mathf.Clamp01(e / 5f));
            if (e < 15f) return Mix(b, c, (e - 5f) / 10f);
            return Mix(c, d, Mathf.Clamp01((e - 15f) / 25f));
        }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static Color Mix(Color a, Color b, float t) =>
            new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);

        private static Color Multiply(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        private static void Opaque(ref SkyState s)
        {
            s.LightColour.a = s.Zenith.a = s.Horizon.a = s.Glow.a = 1f;
            s.AmbientSky.a = s.AmbientEquator.a = s.AmbientGround.a = s.Fog.a = 1f;
            s.CloudLit.a = s.CloudShade.a = 1f;
        }
    }
}

namespace TarmacGauntlet.Raceday
{
    /// <summary>Tileable value noise for the clouds.</summary>
    public static class CloudField
    {
        /// <summary>
        /// A square of fractal noise, 0 to 1, that wraps at its edges: <paramref name="octaves"/>
        /// layers of value noise, the first with <paramref name="cells"/> lattice cells across, each
        /// next twice as fine and half as strong.
        /// </summary>
        public static float[] Fbm(int size, int cells, int octaves, int seed)
        {
            var field = new float[size * size];
            float total = 0f, amp = 1f;
            for (int o = 0; o < octaves; o++, cells *= 2, amp *= 0.5f)
            {
                total += amp;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x * cells / size, fy = (float)y * cells / size;
                        int ix = (int)fx, iy = (int)fy;
                        float tx = Fade(fx - ix), ty = Fade(fy - iy);
                        int x1 = (ix + 1) % cells, y1 = (iy + 1) % cells;
                        float a = Hash(ix, iy, seed + o), b = Hash(x1, iy, seed + o), c = Hash(ix, y1, seed + o), d = Hash(x1, y1, seed + o);
                        field[y * size + x] += amp * (a + (b - a) * tx + (c - a + (a - b - c + d) * tx) * ty);
                    }
            }
            for (int i = 0; i < field.Length; i++) field[i] /= total;
            return field;
        }

        private static float Fade(float t) => t * t * (3f - 2f * t);

        /// <summary>A repeatable random number from 0 to 1 for a lattice point.</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }
    }
}
