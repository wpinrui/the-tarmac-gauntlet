using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Synthesises what one car's tyres sound like on what they roll over, sample by sample: the squeal and scrub of a
    /// slide on tarmac, the rumble of kerb ridges, gravel grinding, the swish and
    /// thump of grass, and a faint road roar. Every layer follows its inputs smoothly, so a slide grows into a squeal and
    /// a wheel dropping off the kerb fades its rumble rather than cutting it.
    ///
    /// Plain C# with no Unity types: <see cref="TyreAudio"/> runs it on the audio thread, and the audition tool renders it
    /// to files. The game thread only writes the public fields.
    /// </summary>
    public sealed class TyreSynth
    {
        private const float RidgeSpacing = 0.75f;   // m between a kerb's ridges, on average
        private const int MaxGrains = 64;
        /// <summary>A slide must pass this, 0 to 1, for the tyres to squeal rather than hiss.</summary>
        private const float SquealFrom = 0.35f;

        /// <summary>How hard the tyres slide on tarmac or a kerb, 0 to 1, the hardest of the four (<see cref="CarController.Skid"/>).</summary>
        public float Slide;
        /// <summary>Road speed, m/s.</summary>
        public float Speed;
        /// <summary>Share of the four wheels on a kerb, on gravel and on grass, each 0 to 1.</summary>
        public float Kerb, Gravel, Grass;
        /// <summary>Share of the four wheels touching anything, 0 to 1.</summary>
        public float Grounded = 1f;
        /// <summary>How wet the track is, 0 to 1. Water quiets a squeal to a hiss.</summary>
        public float Wet;

        private readonly float fs;
        private readonly float squealPitch, roarLevel, holdSamples;
        private readonly float lp2500, lpSqueal, lp180, lp260, lp350, lpRoarSpan, lp600;
        private uint seed;

        // Smoothed inputs.
        private float slide, speed, kerb, gravel, grass, grounded, wet;

        // Squeal: three resonances over a wandering pitch, and the scrub under them.
        private float held, drift, driftTarget, driftTimer, warble, warbleTarget, chirp;
        private float r1Low, r1Band, r2Low, r2Band, r3Low, r3Band, scrubLow, scrubBand, squealLow;

        // Kerb: uneven hits from the front and rear tyres, each a thud and a rattle.
        private readonly float hitDecay;
        private float frontGap, rearGap = 0.5f, frontHit, rearHit, thudLow, thud2, rattleLow, rattleBand;

        // Gravel: dense grains of noise that blur into a grind, and the roar of the car ploughing.
        private struct Grain { public int Left; public float Amp, Decay; }
        private readonly Grain[] grains = new Grain[MaxGrains];
        private int grainCount;
        private float grainOwed, roughLow, roughBand, crunchLow, crunchBand, crunch2Low, crunch2Band, ploughLow, plough2, dullLow;

        // Grass: a rustle that comes and goes, and the thump of the ground's dips.
        private float rustleLow, rustleBand, swishLow, swishBand, rustleEnv, rustleTarget, rustleTimer, bump, bumpLow, bumpBand;

        // Road roar, and the shared tail.
        private float roarLow, roar2, dcIn, dcOut;

        /// <param name="tyreSize">0 for a narrow road tyre, 1 for a wide racing slick: the wider, the lower the squeal.</param>
        public TyreSynth(int sampleRate, float tyreSize, int randomSeed = 1)
        {
            fs = sampleRate;
            seed = (uint)randomSeed * 2246822519u + 7u;
            float size = Clamp(tyreSize, 0f, 1f);
            squealPitch = Lerp(920f, 635f, size) * (0.95f + 0.1f * Uniform());
            roarLevel = Lerp(0.8f, 1.2f, size);
            lpSqueal = LowPassStep(3760f);
            holdSamples = 0.14f * fs;   // seconds a slide is held before it squeals
            lp2500 = LowPassStep(2500f);
            hitDecay = 1f - 160f / fs;
            lp180 = LowPassStep(180f);
            lp260 = LowPassStep(260f);
            lp350 = LowPassStep(350f);
            lpRoarSpan = LowPassStep(650f) - lp350;
            lp600 = LowPassStep(600f);
            driftTarget = 0f;
            rustleTarget = 0.5f;
        }

        /// <summary>Fills <paramref name="output"/> from 0 to <paramref name="count"/> with mono samples, about -1 to 1.</summary>
        public void Render(float[] output, int count)
        {
            float tSlide = Clamp(Slide, 0f, 1f), tSpeed = Math.Max(0f, Speed);
            float tKerb = Clamp(Kerb, 0f, 1f), tGravel = Clamp(Gravel, 0f, 1f), tGrass = Clamp(Grass, 0f, 1f);
            float tGround = Clamp(Grounded, 0f, 1f), tWet = Clamp(Wet, 0f, 1f);

            // Nothing to play and nothing still fading: silence, cheaply.
            if (tSpeed < 0.3f && tSlide <= 0f && speed < 0.3f && slide < 0.001f && grainCount == 0)
            {
                Array.Clear(output, 0, count);
                return;
            }

            float fast = 1f - (float)Math.Exp(-1.0 / (0.03 * fs));
            float slow = 1f - (float)Math.Exp(-1.0 / (0.08 * fs));
            for (int n = 0; n < count; n++)
            {
                // A slide builds a little slower than it lets go, as a tyre takes a moment to start squealing.
                slide += (tSlide - slide) * (tSlide > slide ? slow : fast);
                speed += (tSpeed - speed) * fast;
                kerb += (tKerb - kerb) * fast;
                gravel += (tGravel - gravel) * fast;
                grass += (tGrass - grass) * fast;
                grounded += (tGround - grounded) * fast;
                wet += (tWet - wet) * slow;

                float pace = Clamp(speed / 30f, 0f, 1f);
                float y = Squeal(pace) + Kerbs(pace) + Gravels(pace) + Grasses(pace) + Roar(pace);

                // Remove any steady push the thumps leave, and keep the peaks round.
                float d = y - dcIn + 0.995f * dcOut;
                dcIn = y;
                dcOut = d;
                output[n] = (float)Math.Tanh(d * 1.1f);
            }
        }

        private float Squeal(float pace)
        {
            float noise = Noise();
            // Scrub: the hiss of rubber dragging, there from the first sign of a slide, under the tone.
            float scrubOn = Clamp(slide * 2.3f, 0f, 1f) * (0.3f + 0.7f * pace);
            float scrub = Band(noise, 1380f + 1050f * slide, 0.9f, ref scrubLow, ref scrubBand) * scrubOn * 0.23f;

            // The tone itself only on a real, held slide: the anti-lock's scrub in a braking zone, a gearchange chirp
            // or a tyre at the limit through a corner stays a soft hiss, or every corner would squeal. It wanders: the
            // pitch drifts as the contact patch heats and stretches, and warbles as the rubber sticks and lets go.
            if (slide > SquealFrom) held = Math.Min(held + 1f, holdSamples * 2f);
            else held = Math.Max(held - 2f, 0f);
            float tone = Clamp((slide - SquealFrom) / 0.465f, 0f, 1f) * Clamp(held / holdSamples - 1f, 0f, 1f);
            if (tone <= 0f && chirp < 0.001f) return scrub * (1f - 0.5f * wet) * grounded;
            if (--driftTimer <= 0f)
            {
                driftTarget = Noise() * 0.09f;
                driftTimer = fs * (0.08f + 0.25f * Uniform());
                // Now and then the slide catches and the squeal chirps up for a moment.
                if (Uniform() < 0.16f * tone) chirp = 0.6f + 0.4f * Uniform();
            }
            drift += (driftTarget - drift) * (14f / fs);
            warbleTarget += (Noise() * 0.5f - warbleTarget) * (60f / fs);
            warble += (warbleTarget - warble) * (120f / fs);
            chirp *= 1f - 12f / fs;

            float pitch = squealPitch * (1f + drift + 0.05f * warble + 0.15f * chirp) * (0.94f + 0.08f * slide) * (0.92f + 0.1f * pace);
            // Kept low and soft: the upper harmonics and a hard drive make it shrill.
            float r = Band(noise, pitch, 40f, ref r1Low, ref r1Band)
                      + 0.375f * Band(noise, pitch * 2.02f, 35f, ref r2Low, ref r2Band)
                      + 0.13f * Band(noise, pitch * 3.05f, 30f, ref r3Low, ref r3Band);
            float shaped = (float)Math.Tanh(r * 0.092f * (1f + 1.3f * slide));
            squealLow += (shaped - squealLow) * lpSqueal;
            float level = (tone * tone * (3f - 2f * tone) + 0.5f * chirp * tone) * (0.25f + 0.75f * Math.Min(1f, speed / 12f));
            // On a wet track the tyres mostly hiss through the water instead.
            return (0.21f * squealLow * level * (1f - 0.85f * wet) + scrub * (1f - 0.5f * wet)) * grounded;
        }

        private float Kerbs(float pace)
        {
            if (kerb < 0.001f) return 0f;
            // Front and rear tyres each drum over the ridges. The ridges are never quite even and the tyres hop, so
            // every gap is a little different: an even train of hits would be heard as a pitch, like an engine.
            float rate = speed / RidgeSpacing / fs;
            Hit(ref frontGap, ref frontHit, rate);
            Hit(ref rearGap, ref rearHit, rate);
            frontHit *= hitDecay;
            rearHit *= hitDecay;

            // Each hit is a burst of noise, not a ringing tone: a deep thud through the body and a rattle on top.
            float burst = Noise() * frontHit + Noise() * rearHit;
            thudLow += (burst - thudLow) * lp180;
            thud2 += (thudLow - thud2) * lp180;
            // The rattle stays low and dull: bright noise here reads as static, not rumble.
            float rattle = Band(burst, 220f + 80f * pace, 1.2f, ref rattleLow, ref rattleBand);
            float level = kerb * Clamp(speed / 6f, 0f, 1f) * (0.5f + 0.5f * pace);
            return (8.5f * thud2 + 1.3f * rattle) * level;
        }

        /// <summary>Counts down to a tyre's next ridge and, on it, starts a hit of uneven strength.</summary>
        private void Hit(ref float gap, ref float hit, float rate)
        {
            gap -= rate;
            if (gap > 0f) return;
            gap += 0.6f + 0.8f * Uniform();
            hit = Math.Max(hit, 0.4f + 0.6f * Uniform());
        }

        private float Gravels(float pace)
        {
            float level = gravel * Clamp(speed / 3f, 0f, 1f);
            if (level < 0.001f && grainCount == 0) return 0f;

            // Grains: stones grinding against each other under the tyres, each a few milliseconds of noise. So many
            // overlap at speed that they blur into a grind, and they are kept low and dull: bright, sparse grains
            // sound like glass breaking.
            grainOwed += level * (1500f + 6000f * pace) / fs;
            while (grainOwed >= 1f)
            {
                grainOwed -= 1f;
                if (grainCount < MaxGrains)
                    grains[grainCount++] = new Grain
                    {
                        Left = (int)(fs * (0.003f + 0.008f * Uniform())),
                        Amp = 0.2f + 0.8f * Uniform() * Uniform(),
                        Decay = 1f - (150f + 350f * Uniform()) / fs,
                    };
            }
            float g = 0f;
            for (int i = 0; i < grainCount; i++)
            {
                ref Grain grain = ref grains[i];
                g += grain.Amp * Noise();
                grain.Amp *= grain.Decay;
                if (--grain.Left <= 0) grains[i--] = grains[--grainCount];
            }
            float crunch = Band(g, 1100f, 0.7f, ref crunchLow, ref crunchBand) + 0.9f * Band(g, 420f, 0.9f, ref crunch2Low, ref crunch2Band);
            dullLow += (crunch - dullLow) * lp2500;

            // The car ploughs through the bed: a deep, rough roar that surges as the bed piles up and gives.
            ploughLow += (Noise() - ploughLow) * lp180;
            plough2 += (ploughLow - plough2) * lp260;
            float rough = Band(Noise(), 450f, 0.8f, ref roughLow, ref roughBand) * (0.5f + 4f * Math.Abs(plough2));
            return (0.6f * dullLow + 0.9f * plough2 + 0.12f * rough) * level * (0.3f + 0.7f * pace);
        }

        private float Grasses(float pace)
        {
            float level = grass * Clamp(speed / 3f, 0f, 1f);
            if (level < 0.001f) return 0f;
            // The rustle swells and fades as the tyres run through thicker and thinner grass.
            if (--rustleTimer <= 0f)
            {
                rustleTarget = 0.35f + 0.65f * Uniform();
                rustleTimer = fs * (0.04f + 0.12f * Uniform());
            }
            rustleEnv += (rustleTarget - rustleEnv) * (25f / fs);
            float rustle = Band(Noise(), 700f + 900f * pace, 0.7f, ref rustleLow, ref rustleBand) * rustleEnv;

            // Dips and tufts in the ground thump through the suspension.
            if (Uniform() < (2f + 8f * pace) / fs) bump = 0.3f + 0.7f * Uniform();
            bump *= 1f - 60f / fs;
            float thud = Band(bump * (1f + 0.5f * Noise()), 45f, 2f, ref bumpLow, ref bumpBand);
            float swish = Band(Noise(), 2600f + 1600f * pace, 0.6f, ref swishLow, ref swishBand) * rustleEnv * rustleEnv;
            // Kept subtle: on grass the road roar falls away, and only a soft swish and the bumps are left.
            return (0.15f * rustle * (0.4f + 0.6f * pace) + 0.07f * swish * pace + 0.3f * thud) * level;
        }

        private float Roar(float pace)
        {
            // Rubber rolling on tarmac: a low roar that grows with speed, under everything else.
            float on = grounded * (1f - gravel) * (1f - grass);
            roarLow += (Noise() - roarLow) * (lp350 + lpRoarSpan * pace);
            roar2 += (roarLow - roar2) * lp600;
            return 0.12f * roarLevel * roar2 * pace * pace * on * (1f + 0.6f * wet);
        }

        /// <summary>Band pass by a state variable filter.</summary>
        private float Band(float x, float hz, float q, ref float low, ref float band)
        {
            float f = 2f * (float)Math.Sin(Math.PI * Math.Min(hz, fs * 0.2f) / fs);
            float high = x - low - band / q;
            band += f * high;
            low += f * band;
            return band;
        }

        private float LowPassStep(float hz) => 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Min(hz, fs * 0.45f) / fs);

        /// <summary>Uniform noise from -1 to 1.</summary>
        private float Noise() => Uniform() * 2f - 1f;

        private float Uniform()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed >> 8) / 16777216f;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
