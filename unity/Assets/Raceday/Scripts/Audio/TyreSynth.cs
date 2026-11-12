using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Synthesises what one car's tyres sound like on what they roll over, sample by sample: the squeal and scrub of a
    /// slide on tarmac, the buzz of kerb ridges, gravel grains crunching and stones pinging off the floor, the swish and
    /// thump of grass, and a faint road roar. Every layer follows its inputs smoothly, so a slide grows into a squeal and
    /// a wheel dropping off the kerb fades its rumble rather than cutting it.
    ///
    /// Plain C# with no Unity types: <see cref="TyreAudio"/> runs it on the audio thread, and the audition tool renders it
    /// to files. The game thread only writes the public fields.
    /// </summary>
    public sealed class TyreSynth
    {
        private const float RidgeSpacing = 0.4f;   // m between a kerb's ridges
        private const int MaxGrains = 24;
        private const int MaxPings = 6;

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
        private readonly float squealPitch, roarLevel;
        private readonly float lp6000, lp180, lp260, lp350, lpRoarSpan, lp600;
        private uint seed;

        // Smoothed inputs.
        private float slide, speed, kerb, gravel, grass, grounded, wet;

        // Squeal: three resonances over a wandering pitch, and the scrub under them.
        private float drift, driftTarget, driftTimer, warble, warbleTarget, chirp;
        private float r1Low, r1Band, r2Low, r2Band, r3Low, r3Band, scrubLow, scrubBand, squealLow;

        // Kerb: one thump per ridge into the body's resonance, and the rattle on top.
        private double ridgePhase;
        private float thump, slap, click, carcassLow, carcassBand, thumpLow, thumpBand, rattleLow, rattleBand, rattle2Low, rattle2Band;

        // Gravel: short grains of noise, the roar of the car ploughing, stones pinging off the floor.
        private struct Grain { public int Left; public float Amp, Decay; }
        private readonly Grain[] grains = new Grain[MaxGrains];
        private int grainCount;
        private float grainOwed, roughLow, roughBand, crunchLow, crunchBand, crunch2Low, crunch2Band, ploughLow, plough2;
        private struct Ping { public double Phase; public float Step, Amp, Decay; }
        private readonly Ping[] pings = new Ping[MaxPings];
        private int pingCount;
        private float pingOwed;

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
            squealPitch = Lerp(1150f, 760f, size) * (0.95f + 0.1f * Uniform());
            roarLevel = Lerp(0.8f, 1.2f, size);
            lp6000 = LowPassStep(6000f);
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
            if (tSpeed < 0.3f && tSlide <= 0f && speed < 0.3f && slide < 0.001f && grainCount == 0 && pingCount == 0)
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
            float scrubOn = Clamp(slide * 3f, 0f, 1f) * (0.3f + 0.7f * pace);
            float scrub = Band(noise, 1800f + 1400f * slide, 0.9f, ref scrubLow, ref scrubBand) * scrubOn * 0.35f;

            // The tone itself only past a real slide. It wanders: the pitch drifts as the contact patch heats and
            // stretches, and warbles as the rubber sticks and lets go.
            float tone = Clamp((slide - 0.12f) / 0.5f, 0f, 1f);
            if (tone <= 0f && chirp < 0.001f) return scrub * (1f - 0.5f * wet) * grounded;
            if (--driftTimer <= 0f)
            {
                driftTarget = Noise() * 0.09f;
                driftTimer = fs * (0.08f + 0.25f * Uniform());
                // Now and then the slide catches and the squeal chirps up for a moment.
                if (Uniform() < 0.25f * tone) chirp = 0.6f + 0.4f * Uniform();
            }
            drift += (driftTarget - drift) * (14f / fs);
            warbleTarget += (Noise() * 0.5f - warbleTarget) * (60f / fs);
            warble += (warbleTarget - warble) * (120f / fs);
            chirp *= 1f - 12f / fs;

            float pitch = squealPitch * (1f + drift + 0.05f * warble + 0.15f * chirp) * (0.94f + 0.08f * slide) * (0.92f + 0.1f * pace);
            float r = Band(noise, pitch, 40f, ref r1Low, ref r1Band)
                      + 0.55f * Band(noise, pitch * 2.02f, 35f, ref r2Low, ref r2Band)
                      + 0.25f * Band(noise, pitch * 3.05f, 30f, ref r3Low, ref r3Band);
            float shaped = (float)Math.Tanh(r * 0.12f * (1f + 2f * slide));
            squealLow += (shaped - squealLow) * lp6000;
            float level = (tone * tone * (3f - 2f * tone) + 0.5f * chirp * tone) * (0.25f + 0.75f * Math.Min(1f, speed / 12f));
            // On a wet track the tyres mostly hiss through the water instead.
            return (0.32f * squealLow * level * (1f - 0.85f * wet) + scrub * (1f - 0.5f * wet)) * grounded;
        }

        private float Kerbs(float pace)
        {
            if (kerb < 0.001f) return 0f;
            ridgePhase += speed / RidgeSpacing / fs;
            if (ridgePhase >= 1.0)
            {
                ridgePhase -= Math.Floor(ridgePhase);
                thump = 0.7f + 0.3f * Uniform();
                slap = thump;
                click = thump;
            }
            thump *= 1f - 400f / fs;
            slap *= 1f - 150f / fs;
            click *= 1f - 2500f / fs;
            // The tyre's carcass rings as it hits each ridge: a harmonic buzz at the ridge rate.
            float carcass = Band(click, 240f + 80f * pace, 2.5f, ref carcassLow, ref carcassBand);
            float body = Band(thump * (1f + 0.3f * Noise()), 70f + 30f * pace, 3f, ref thumpLow, ref thumpBand);
            // The tyre slaps each ridge too: a burst of mid noise in time with the thumps.
            // The tyre slaps each ridge too, and the car rattles: mid noise in time with the thumps, which is what
            // carries the rumble on small speakers.
            float noise = Noise();
            float rattle = Band(noise, 320f + 200f * pace, 1.5f, ref rattleLow, ref rattleBand)
                           + 0.5f * Band(noise, 1100f + 400f * pace, 2f, ref rattle2Low, ref rattle2Band);
            float level = kerb * Clamp(speed / 6f, 0f, 1f) * (0.5f + 0.5f * pace);
            return (0.35f * body + 1.2f * carcass + 2.5f * rattle * slap * slap) * level;
        }

        private float Gravels(float pace)
        {
            float level = gravel * Clamp(speed / 3f, 0f, 1f);
            if (level < 0.001f && grainCount == 0 && pingCount == 0) return 0f;

            // Grains: each a tiny stone shifting under a tyre, a millisecond or two of noise. More of them the faster.
            grainOwed += level * (600f + 4400f * pace) / fs;
            while (grainOwed >= 1f)
            {
                grainOwed -= 1f;
                if (grainCount < MaxGrains)
                    grains[grainCount++] = new Grain
                    {
                        Left = (int)(fs * (0.001f + 0.004f * Uniform())),
                        Amp = 0.3f + 0.7f * Uniform() * Uniform(),
                        Decay = 1f - (300f + 900f * Uniform()) / fs,
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
            float crunch = Band(g, 2600f, 0.8f, ref crunchLow, ref crunchBand) + 0.7f * Band(g, 900f, 1.1f, ref crunch2Low, ref crunch2Band);

            // The car ploughs through the bed: a deep, rough roar.
            ploughLow += (Noise() - ploughLow) * lp180;
            plough2 += (ploughLow - plough2) * lp260;

            // Stones flicked up ping off the floor and the arches.
            pingOwed += level * (2f + 22f * pace) / fs;
            if (pingOwed >= 1f)
            {
                pingOwed -= 1f;
                if (pingCount < MaxPings)
                    pings[pingCount++] = new Ping
                    {
                        Step = (2200f + 3800f * Uniform()) / fs,
                        Amp = 0.1f + 0.25f * Uniform(),
                        Decay = 1f - (60f + 90f * Uniform()) / fs,
                    };
            }
            float p = 0f;
            for (int i = 0; i < pingCount; i++)
            {
                ref Ping ping = ref pings[i];
                ping.Phase += ping.Step;
                p += ping.Amp * (float)Math.Sin(2.0 * Math.PI * ping.Phase);
                ping.Amp *= ping.Decay;
                if (ping.Amp < 0.002f) pings[i--] = pings[--pingCount];
            }
            float rough = Band(Noise(), 450f, 0.8f, ref roughLow, ref roughBand) * (0.5f + 0.5f * Math.Abs(plough2) * 8f);
            return (0.95f * crunch + 0.9f * plough2 + 0.12f * rough) * level * (0.3f + 0.7f * pace) + p;
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
            return (0.5f * rustle * (0.4f + 0.6f * pace) + 0.25f * swish * pace + 0.45f * thud) * level;
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
