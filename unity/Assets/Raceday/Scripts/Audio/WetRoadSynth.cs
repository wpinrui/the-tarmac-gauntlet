using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Synthesises the hiss of one car's tyres on a wet road, sample by sample: water torn off the tread as a bright hiss
    /// that grows with speed, a softer swish under it, and in standing water the slap and gurgle of the tyres ploughing
    /// through. It is its own layer beside the <see cref="TyreSynth"/>, which it never touches.
    ///
    /// Plain C# with no Unity types: <see cref="WetRoadAudio"/> runs it on the audio thread, and the audition tool renders
    /// it to files. The game thread only writes the public fields.
    /// </summary>
    public sealed class WetRoadSynth
    {
        /// <summary>Road speed, m/s.</summary>
        public float Speed;
        /// <summary>How wet the track is, 0 to 1 (<see cref="WeatherState.Wet"/>).</summary>
        public float Wet;
        /// <summary>Depth of the water on the track, mm (<see cref="WeatherState.Water"/>): past 1 mm it stands in sheets.</summary>
        public float Water;
        /// <summary>Share of the four wheels touching the ground, 0 to 1.</summary>
        public float Grounded = 1f;

        private readonly float fs, tone;
        private uint seed;
        private float speed, wet, standing, grounded;
        private float hissLow, hissBand, hissTop, swishLow, swishBand, slapLow, slapBand, slap, slapTimer, tread, treadTarget, treadTimer, dcIn, dcOut;

        /// <param name="randomSeed">Gives each car its own pattern and a slightly different colour.</param>
        public WetRoadSynth(int sampleRate, int randomSeed = 1)
        {
            fs = sampleRate;
            seed = (uint)randomSeed * 2246822519u + 101u;
            tone = 0.9f + 0.2f * Uniform();
        }

        /// <summary>Fills <paramref name="output"/> from 0 to <paramref name="count"/> with mono samples, about -1 to 1.</summary>
        public void Render(float[] output, int count)
        {
            float tSpeed = Math.Max(0f, Speed), tWet = Clamp(Wet, 0f, 1f), tGround = Clamp(Grounded, 0f, 1f);
            float tStanding = Clamp((Water - 1f) / 1.5f, 0f, 1f);
            if (tWet <= 0f && wet < 0.001f)
            {
                Array.Clear(output, 0, count);
                return;
            }
            float fast = 1f - (float)Math.Exp(-1.0 / (0.04 * fs));
            float slow = 1f - (float)Math.Exp(-1.0 / (0.5 * fs));
            float top = Step(2500f * tone);
            for (int n = 0; n < count; n++)
            {
                speed += (tSpeed - speed) * fast;
                grounded += (tGround - grounded) * fast;
                wet += (tWet - wet) * slow;
                standing += (tStanding - standing) * slow;

                // The tread throws water in uneven sheets: a quick random flutter on the level.
                if (--treadTimer <= 0f)
                {
                    treadTimer = fs * (0.01f + 0.03f * Uniform());
                    treadTarget = 0.6f + 0.4f * Uniform();
                }
                tread += (treadTarget - tread) * (1f / (fs * 0.008f));

                float pace = Clamp(speed / 60f, 0f, 1.2f);
                float level = wet * grounded * Clamp(speed / 4f, 0f, 1f);
                float white = Noise();

                // The hiss: bright, its band rising with speed, the top of it above 2.5 kHz.
                float hiss = Band(white, (2200f + 2600f * pace) * tone, 0.8f, ref hissLow, ref hissBand);
                hissTop += (white - hissTop) * top;
                float bright = white - hissTop;
                // The swish: softer air and water under it.
                float swish = Band(white, (500f + 500f * pace) * tone, 0.7f, ref swishLow, ref swishBand);
                float y = level * (0.5f + 0.5f * pace) * pace * tread * (0.35f * hiss + 0.08f * bright + 0.3f * swish);

                // Standing water: slaps of water, more of them the faster the car goes.
                if (--slapTimer <= 0f)
                {
                    slapTimer = fs * (0.02f + 0.1f * Uniform()) / (0.3f + pace);
                    slap = Math.Max(slap, 0.3f + 0.7f * Uniform());
                }
                slap *= 1f - 40f / fs;
                float gurgle = Band(white * slap, (280f + 260f * pace) * tone, 2.5f, ref slapLow, ref slapBand);
                y += level * standing * (0.25f + 0.75f * pace) * 0.9f * gurgle;

                float d = y - dcIn + 0.995f * dcOut;
                dcIn = y;
                dcOut = d;
                output[n] = (float)Math.Tanh(d * 1.4f);
            }
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
