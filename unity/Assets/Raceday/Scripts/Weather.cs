using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>The kind of autumn weather the race weekend gets.</summary>
    public enum WeatherKind
    {
        /// <summary>High pressure: sunny spells, a stray shower at most, and low cloud and drizzle can sit in overnight.</summary>
        Settled,
        /// <summary>A rare mild, humid spell: bright mornings, and heavy thundery showers can break out in the afternoon.</summary>
        Thundery,
        /// <summary>Fronts and showers come and go between sunny spells.</summary>
        Changeable,
        /// <summary>A low parked nearby: grey skies, fronts and frequent showers.</summary>
        Unsettled,
    }

    /// <summary>
    /// The weather over the circuit for one race, rolled from a seed when the race starts, so every
    /// race gets its own. A late October day at 50 degrees north, as the sky is (<see cref="SkyClock"/>):
    /// the kind of weekend sets how cloudy it is and how ready the air is to shower. Fronts come
    /// through often, with hours of thickening cloud ahead of a band of steady rain, then clear to
    /// sunshine and showers behind. Showers build most in the early afternoon. Low cloud can bring
    /// drizzle through the night and morning. The track holds a film of water: rain fills it, the
    /// surplus drains off, and the low sun, the wind and the cars dry it, slowly in the cool air
    /// and slower still at night. The race's first hours are worked out from six hours before
    /// the start, so the track at the green follows from the morning's weather. Everything is
    /// tabulated once a minute up front, so the weather is the same at any race speed.
    /// </summary>
    public sealed class WeatherPlan
    {
        /// <summary>Hours of weather worked out before the start.</summary>
        public const float Before = 6f;
        /// <summary>Hours of weather after the start: the race and a little over.</summary>
        public const float After = 25f;
        /// <summary>Table step, hours.</summary>
        public const float Step = 1f / 60f;
        /// <summary>Water film above which the surplus runs off the track, mm.</summary>
        public const float Holds = 0.6f;
        /// <summary>Deepest the water stands on the track, mm.</summary>
        public const float Flooded = 3f;

        public readonly int Seed;
        public readonly WeatherKind Kind;
        /// <summary>Local time at the start, hours.</summary>
        public readonly float StartHour;
        /// <summary>Hours from the start of each front's rain band, and of each shower.</summary>
        public readonly List<float> Fronts = new List<float>(), Showers = new List<float>();

        private readonly float[] cloud, rain, water, wind;
        private readonly System.Random random;

        public WeatherPlan(int seed, float startHour)
        {
            Seed = seed;
            StartHour = startHour;
            random = new System.Random(seed);
            int n = Mathf.CeilToInt((Before + After) / Step) + 1;
            cloud = new float[n];
            rain = new float[n];
            water = new float[n];
            wind = new float[n];

            double roll = random.NextDouble();
            Kind = roll < 0.28 ? WeatherKind.Settled : roll < 0.32 ? WeatherKind.Thundery : roll < 0.73 ? WeatherKind.Changeable : WeatherKind.Unsettled;
            float cover = Kind switch { WeatherKind.Settled => 0.22f, WeatherKind.Thundery => 0.25f, WeatherKind.Changeable => 0.5f, _ => 0.72f };
            float unstable = Kind switch { WeatherKind.Settled => 0.05f, WeatherKind.Thundery => 0.35f, WeatherKind.Changeable => 0.22f, _ => 0.42f };
            float frontsExpected = Kind switch { WeatherKind.Settled => 0.12f, WeatherKind.Thundery => 0.3f, WeatherKind.Changeable => 1f, _ => 1.6f };

            var instability = new float[n];
            Wave baseCloud = new Wave(random, 11f, 5.3f, 2.1f), baseWind = new Wave(random, 9f, 3.7f, 1.3f);
            Wave flicker = new Wave(random, 0.9f, 0.37f, 0.19f);
            for (int i = 0; i < n; i++)
            {
                float h = HourAt(i);
                cloud[i] = Mathf.Clamp01(cover + 0.17f * baseCloud.At(h));
                instability[i] = unstable;
                wind[i] = 0.75f + 0.25f * baseWind.At(h);
            }

            // Fronts: thickening cloud for hours, a band of rain, then a fresh, showery air behind.
            int fronts = Poisson(frontsExpected);
            for (int f = 0; f < fronts; f++) AddFront(Range(-Before + 3f, After - 1f), instability, flicker);

            // Fair weather cumulus bubbles up through the day, more in unstable air.
            for (int i = 0; i < n; i++)
                cloud[i] = Mathf.Clamp01(cloud[i] + 0.35f * instability[i] * (Afternoon(Local(HourAt(i))) - 0.1f));

            // Showers: rolled ten minutes at a time, most likely in the early afternoon and in unstable air.
            const float roll10 = 1f / 6f;
            for (float h = -Before; h < After; h += roll10)
            {
                int i = IndexAt(h);
                float rate = 0.4f * instability[i] * Afternoon(Local(h));
                if (random.NextDouble() < rate * roll10) AddShower(h);
            }

            // Low cloud and drizzle through the long night and the morning.
            if (random.NextDouble() < (Kind == WeatherKind.Settled ? 0.3 : 0.4))
            {
                float local = Range(0f, 8f);
                float start = Mathf.Repeat(local - StartHour, 24f);
                AddDrizzle(start, Range(2f, 6f), flicker);
            }

            for (int i = 0; i < n; i++)
                rain[i] = rain[i] < 0.05f ? 0f : Mathf.Min(rain[i], 40f);
            Dry();
        }

        /// <summary>The weather this many race seconds from the start.</summary>
        public WeatherState At(float raceSeconds)
        {
            float f = Mathf.Clamp((raceSeconds / 3600f + Before) / Step, 0f, cloud.Length - 1.001f);
            int i = (int)f;
            float t = f - i;
            return new WeatherState
            {
                Cloud = Mathf.Lerp(cloud[i], cloud[i + 1], t),
                Rain = Mathf.Lerp(rain[i], rain[i + 1], t),
                Water = Mathf.Lerp(water[i], water[i + 1], t),
                Wind = Mathf.Lerp(wind[i], wind[i + 1], t),
            };
        }

        /// <summary>Hours from the start of a table entry.</summary>
        public static float HourAt(int i) => -Before + i * Step;
        private int IndexAt(float h) => Mathf.Clamp(Mathf.RoundToInt((h + Before) / Step), 0, cloud.Length - 1);
        private float Local(float h) => Mathf.Repeat(StartHour + h, 24f);

        /// <summary>How ready the air is to shower at a local time: some at night, most at about two in the afternoon.</summary>
        private static float Afternoon(float local)
        {
            float d = Mathf.Abs(local - 14f);
            if (d > 12f) d = 24f - d;
            return 0.3f + 0.7f * Mathf.Exp(-(d / 3.5f) * (d / 3.5f));
        }

        private void AddFront(float at, float[] instability, Wave flicker)
        {
            Fronts.Add(at);
            bool cold = random.NextDouble() < 0.5;
            float band = cold ? Range(0.7f, 1.6f) : Range(1.5f, 4f);
            float peak = cold ? Range(2.5f, 7f) : Range(0.8f, 3f);
            float lead = cold ? Range(2f, 4f) : Range(4f, 8f);
            float clearing = Range(1.5f, 3f);
            float start = at, end = at + band;
            for (int i = 0; i < cloud.Length; i++)
            {
                float h = HourAt(i);
                float sheet = h < start ? Smooth(start - lead, start, h) : h <= end ? 1f : 1f - Smooth(end, end + clearing, h);
                cloud[i] = Mathf.Max(cloud[i], 0.97f * sheet);
                if (h >= start && h <= end)
                {
                    // Rain sets in and eases off over a quarter of an hour, and comes and goes in bursts.
                    float edges = Smooth(start, start + 0.25f, h) * (1f - Smooth(end - 0.25f, end, h));
                    // A cold front is heaviest at its leading edge.
                    float shape = cold ? 1.2f - 0.5f * (h - start) / band : 1f;
                    rain[i] += peak * edges * shape * (0.55f + 0.45f * flicker.At(h * 1.7f + at));
                }
                if (h > end) instability[i] += 0.35f * Mathf.Exp(-(h - end) / 6f) * Smooth(end, end + 1f, h);
                // The wind picks up as the front comes through.
                float near = Mathf.Exp(-Mathf.Abs(h - (start + end) * 0.5f) / 2.5f);
                wind[i] += 0.9f * near;
            }
        }

        private void AddShower(float at)
        {
            Showers.Add(at);
            bool storm = Kind == WeatherKind.Thundery || random.NextDouble() < 0.04;
            float length = storm ? Range(0.25f, 0.75f) : Range(0.12f, 0.5f);
            float peak = Mathf.Min((float)(-Math.Log(1.0 - random.NextDouble())) * (storm ? 10f : 3.5f) + 0.8f, 40f);
            float build = Range(0.6f, 1.4f);
            float end = at + length, top = at + 0.25f * length;
            for (int i = IndexAt(at - build); i <= IndexAt(end + 1.2f); i++)
            {
                float h = HourAt(i);
                float tower = h < at ? Smooth(at - build, at, h) : h <= end ? 1f : 1f - Smooth(end, end + 1.2f, h);
                cloud[i] = Mathf.Lerp(cloud[i], Mathf.Max(cloud[i], 0.97f), tower);
                if (h >= at && h <= end)
                    rain[i] += peak * (h < top ? Smooth(at, top, h) : 1f - Smooth(top, end, h));
                wind[i] += (storm ? 1.2f : 0.5f) * tower;
            }
        }

        private void AddDrizzle(float at, float length, Wave flicker)
        {
            float wet = Range(0.1f, 0.35f);
            for (int i = IndexAt(at - 1f); i <= IndexAt(at + length + 1f); i++)
            {
                float h = HourAt(i);
                float deck = Smooth(at - 1f, at, h) * (1f - Smooth(at + length, at + length + 1f, h));
                cloud[i] = Mathf.Max(cloud[i], 0.95f * deck);
                if (h >= at && h <= at + length && flicker.At(h * 2.3f + 7f) > 0.0f) rain[i] += wet * deck;
            }
        }

        /// <summary>
        /// Runs the track's water film through the table: rain fills it, anything above what the
        /// surface holds drains off within minutes, and it dries by evaporation: slowly in the cool
        /// autumn air, faster in sunshine and wind, and with the cars running on it.
        /// </summary>
        private void Dry()
        {
            float w = 0f;
            float dt = Step;
            for (int i = 0; i < water.Length; i++)
            {
                float h = HourAt(i);
                float sun = Mathf.Max(SkyClock.Direction(Local(h), SkyClock.Declination).y, 0f);
                float evaporate = 0.03f + 0.35f * sun * (1f - 0.8f * cloud[i]) + 0.08f * (wind[i] - 0.75f) + (h >= 0f ? 0.3f : 0.03f);
                if (rain[i] > 0f) evaporate *= 0.25f;
                w += rain[i] * dt;
                w -= Mathf.Max(w - Holds, 0f) * (1f - Mathf.Exp(-dt / 0.2f));
                w -= Mathf.Min(w, Mathf.Max(evaporate, 0f) * dt);
                water[i] = w = Mathf.Min(w, Flooded);
            }
        }

        private float Range(float a, float b) => a + (b - a) * (float)random.NextDouble();

        private int Poisson(float mean)
        {
            double limit = Math.Exp(-mean), p = random.NextDouble();
            int k = 0;
            while (p > limit && k < 6) { p *= random.NextDouble(); k++; }
            return k;
        }

        private static float Smooth(float a, float b, float x) => SkyClock.Smooth(a, b, x);

        /// <summary>A smooth wander between -1 and 1: three sine waves of these periods, hours, at random phases.</summary>
        private readonly struct Wave
        {
            private readonly float p0, p1, p2, a, b, c;

            public Wave(System.Random random, float a, float b, float c)
            {
                this.a = a; this.b = b; this.c = c;
                p0 = (float)random.NextDouble() * 6.283f;
                p1 = (float)random.NextDouble() * 6.283f;
                p2 = (float)random.NextDouble() * 6.283f;
            }

            public float At(float h) =>
                (Mathf.Sin(6.283f * h / a + p0) + 0.6f * Mathf.Sin(6.283f * h / b + p1) + 0.4f * Mathf.Sin(6.283f * h / c + p2)) / 2f;
        }
    }
}
