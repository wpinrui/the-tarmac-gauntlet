using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Synthesises the wind round the camera, in stereo, sample by sample. Standing still it is the weather's breeze:
    /// a soft band of air that gusts. Moving, the air rushes past: a low buffet that grows with the square of the speed,
    /// a whoosh whose band rises as the camera goes faster, and a thin hiss on top at racing speeds. Each side has its own
    /// noise, so the rush is wide.
    ///
    /// Plain C# with no Unity types: <see cref="RaceAmbience"/> runs it on the audio thread, and the audition tool renders
    /// it to files. The game thread only writes the public fields.
    /// </summary>
    public sealed class WindSynth
    {
        /// <summary>How fast the camera moves through the air, m/s.</summary>
        public float Speed;
        /// <summary>How open the camera is to the rush, 0 to 1: a bonnet or bumper view is 1, a TV camera much less.</summary>
        public float Exposure = 1f;
        /// <summary>The weather's wind, 1 for a normal breeze (<see cref="WeatherState.Wind"/>).</summary>
        public float Breeze = 1f;

        private readonly float fs;
        private uint seed;
        private float speed, exposure, breeze;
        private float gust, gustTarget, gustTimer, buffet, buffetTarget, buffetTimer;
        private readonly Side left = new Side(), right = new Side();

        private sealed class Side
        {
            public float BreezeLow, BreezeBand, RushLow, RushBand, BuffetLow, Buffet2, HissLow, Brown;
        }

        public WindSynth(int sampleRate, int randomSeed = 1)
        {
            fs = sampleRate;
            seed = (uint)randomSeed * 2654435761u + 11u;
            gustTarget = 0.5f;
        }

        /// <summary>Fills <paramref name="output"/> with <paramref name="count"/> interleaved frames of
        /// <paramref name="channels"/> samples, about -1 to 1. Channels past the second repeat the first two.</summary>
        public void Render(float[] output, int count, int channels)
        {
            float tSpeed = Math.Max(0f, Speed), tExposure = Clamp(Exposure, 0f, 1f), tBreeze = Clamp(Breeze, 0f, 3f);
            // The camera's speed comes in once a frame: smooth it over a fifth of a second so cuts and frame jitter don't click.
            float glide = 1f - (float)Math.Exp(-1.0 / (0.2 * fs));
            for (int n = 0; n < count; n++)
            {
                speed += (tSpeed - speed) * glide;
                exposure += (tExposure - exposure) * glide;
                breeze += (tBreeze - breeze) * glide;
                Wander();

                // Rush: zero standing, about full at 70 m/s, by the square of the speed as drag is.
                float pace = Clamp(speed / 70f, 0f, 1.4f);
                float rush = pace * pace * exposure;
                float l = Air(left, rush, pace), r = Air(right, rush, pace);
                int k = n * channels;
                output[k] = l;
                if (channels > 1) output[k + 1] = r;
                for (int c = 2; c < channels; c++) output[k + c] = (c & 1) == 0 ? l : r;
            }
        }

        private float Air(Side s, float rush, float pace)
        {
            float white = Noise();
            // Brown noise for the deep parts, leaky so it never wanders off.
            s.Brown = 0.997f * s.Brown + 0.03f * white;

            // The weather's breeze: a soft band round 300 Hz, rising a little in a gust.
            float breezeLevel = 0.45f * breeze * (0.25f + 0.75f * gust);
            float b = Band(white, 220f + 180f * gust, 0.9f, ref s.BreezeLow, ref s.BreezeBand);

            // The rush: a band of air that climbs from 350 Hz to 1.6 kHz with speed, and wavers with the buffet.
            float r = Band(white, 350f + 1250f * Math.Min(pace, 1f), 0.7f, ref s.RushLow, ref s.RushBand);

            // The buffet: the low thump of air on the body, from brown noise.
            s.BuffetLow += (s.Brown - s.BuffetLow) * Step(90f + 60f * pace);
            s.Buffet2 += (s.BuffetLow - s.Buffet2) * Step(140f);

            // The hiss: a thin top at racing speeds.
            s.HissLow += (white - s.HissLow) * Step(3500f);
            float hiss = white - s.HissLow;

            float waver = 0.7f + 0.3f * buffet;
            float y = breezeLevel * b
                    + rush * (0.32f * r * waver + 0.9f * s.Buffet2 * (0.5f + 0.5f * buffet) + 0.05f * hiss * Clamp(pace * 1.4f - 0.4f, 0f, 1f));
            return (float)Math.Tanh(y * 1.2f);
        }

        /// <summary>Moves the slow random controls: the breeze's gusts every few seconds, the buffet many times a second.</summary>
        private void Wander()
        {
            if (--gustTimer <= 0f)
            {
                gustTimer = fs * (1.5f + 4f * Uniform());
                gustTarget = Uniform() * Uniform() * 1.2f + 0.1f;
            }
            gust += (gustTarget - gust) * (1f / (fs * 1.2f));
            if (--buffetTimer <= 0f)
            {
                buffetTimer = fs * (0.04f + 0.12f * Uniform());
                buffetTarget = Uniform();
            }
            buffet += (buffetTarget - buffet) * (1f / (fs * 0.05f));
        }

        private float Band(float x, float hz, float q, ref float low, ref float band)
        {
            float f = 2f * (float)Math.Sin(Math.PI * Math.Min(hz, fs * 0.2f) / fs);
            float high = x - low - band / q;
            band += f * high;
            low += f * band;
            return band;
        }

        private float Step(float hz) => 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Min(hz, fs * 0.45f) / fs);

        private float Noise() => Uniform() * 2f - 1f;

        private float Uniform()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed >> 8) / 16777216f;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
