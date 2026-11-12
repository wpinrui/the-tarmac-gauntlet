using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Synthesises one engine from an <see cref="EngineSound"/>, sample by sample. Each cylinder firing sends an
    /// exhaust pulse down its header into its bank's pipe, a delay line that rings at the pipe's resonances.
    /// The banks are mixed, filtered by the muffler and saturated. Induction roar, valvetrain clatter, gear whine,
    /// turbo and electric motor are layered on top. Overrun pops, the rev limiter and shift cuts drop or
    /// boost single firings.
    ///
    /// Plain C# with no Unity types: <see cref="EngineAudio"/> runs it on the audio thread, and the audition
    /// tool renders it to files. The game thread only writes <see cref="Rpm"/>, <see cref="Load"/> and
    /// <see cref="Speed"/>, and calls <see cref="Shift"/>.
    /// </summary>
    public sealed class EngineSynth
    {
        private const float SoundSpeed = 480f;   // m/s, in hot exhaust gas
        private const int MaxPulses = 64;

        /// <summary>Engine speed the sound heads for, rpm.</summary>
        public float Rpm;
        /// <summary>Throttle, 0 to 1.</summary>
        public float Load;
        /// <summary>Road speed, m/s, for the gear and motor whine.</summary>
        public float Speed;

        private readonly EngineSound s;
        private readonly float fs;
        private readonly float[] fireAt, cylinderGain;
        private readonly int[] bankOf, headerDelay;
        private readonly int cylinders;
        private readonly float intakeStep, breathStep, topStep, gateStep;

        private float rpm, load;
        private double cycles, cycleBase;
        private int next;
        private uint seed;

        private struct Pulse
        {
            public int Wait, Length, Pos, Bank;
            public float Amp, Breath, Skew;
        }
        private readonly Pulse[] pulses = new Pulse[MaxPulses];
        private int pulseCount;

        private readonly float[][] pipe = new float[2][];
        private readonly int[] pipeLength = new int[2];
        private readonly int[] pipePos = new int[2];
        private readonly float[] pipeDamp = new float[2];

        private float low1, low2, dcIn, dcOut, popLow, envelope;
        private float intakeGate, breathLow, topLow1, topLow2, intakeLp1, intakeLp2, intakeLow, intakeBand, mechLow, hissLow, hissBand, bovLow;
        private double whinePhase, turboPhase, motorPhase;
        private float boost, blowOff, overrun, lastLoad, idleBlend;
        private bool blowOffArmed;
        private int cutSamples, firings;

        public EngineSynth(EngineSound sound, int sampleRate, int randomSeed = 1)
        {
            s = sound;
            fs = sampleRate;
            seed = (uint)randomSeed * 2654435761u + 1u;
            s.Firing(out fireAt, out bankOf);
            cylinders = fireAt.Length;

            cylinderGain = new float[cylinders];
            headerDelay = new int[cylinders];
            for (int i = 0; i < cylinders; i++)
            {
                cylinderGain[i] = 1f + s.Asymmetry * Noise();
                // Unequal headers: every other firing arrives late, so pulses bunch into pairs.
                float extra = i % 2 == 1 ? s.HeaderSpread * 0.003f : 0f;
                headerDelay[i] = (int)((s.HeaderLength / SoundSpeed + extra) * fs);
            }
            for (int b = 0; b < 2; b++)
            {
                float length = s.PipeLength * (b == 1 ? s.BankSplit : 1f);
                pipeLength[b] = Math.Max(2, (int)(2f * length / SoundSpeed * fs));
                pipe[b] = new float[pipeLength[b]];
            }
            rpm = s.IdleRpm;
            Rpm = s.IdleRpm;
            intakeStep = LowPassStep(1800f);
            breathStep = LowPassStep(1500f);
            topStep = LowPassStep(6500f);
            gateStep = LowPassStep(400f);
        }

        /// <summary>A gear change: the ignition cuts for a moment.</summary>
        public void Shift() => cutSamples = (int)(0.07f * fs);

        /// <summary>Fills <paramref name="output"/> from 0 to <paramref name="count"/> with mono samples, about -1 to 1.</summary>
        public void Render(float[] output, int count)
        {
            float targetRpm = Clamp(Rpm, s.IdleRpm * 0.9f, s.RedlineRpm * 1.01f);
            float targetLoad = Clamp(Load, 0f, 1f);
            float speed = Math.Max(0f, Speed);
            float rpmStep = 1f - (float)Math.Exp(-1.0 / (0.035 * fs));
            float loadStep = 1f - (float)Math.Exp(-1.0 / (0.04 * fs));

            for (int n = 0; n < count; n++)
            {
                rpm += (targetRpm - rpm) * rpmStep;
                load += (targetLoad - load) * loadStep;
                float rev = Clamp((rpm - s.IdleRpm) / (s.RedlineRpm - s.IdleRpm), 0f, 1f);
                idleBlend = rpm < s.IdleRpm * 1.25f ? 1f - (rpm - s.IdleRpm) / (s.IdleRpm * 0.25f) : 0f;
                idleBlend = Clamp(idleBlend, 0f, 1f);

                Overrun(rev);
                Turbo(rev);

                // Firings this sample. Four strokes: one cycle every two turns.
                cycles += rpm / 120.0 / fs;
                while (cycles >= cycleBase + fireAt[next])
                {
                    Fire(next, rev);
                    if (++next == cylinders) { next = 0; cycleBase += 1.0; }
                }
                if (cutSamples > 0) cutSamples--;

                // Pulses travel down their headers into the bank pipes.
                float in0 = 0f, in1 = 0f, pop = 0f;
                // The breath in each pulse is dull noise: white noise here hisses through the open muffler of a loud car.
                breathLow += (Noise() - breathLow) * breathStep;
                float breath = 3f * breathLow;
                for (int i = 0; i < pulseCount; i++)
                {
                    ref Pulse p = ref pulses[i];
                    if (p.Wait > 0) { p.Wait--; continue; }
                    float x = (p.Pos + 0.5f) / p.Length;
                    float shape = (float)Math.Sin(Math.PI * Math.Pow(x, p.Skew));
                    float v = p.Amp * shape * shape * (1f - p.Breath + p.Breath * breath);
                    if (p.Bank == 2) pop += v;
                    else if (p.Bank == 1) in1 += v;
                    else in0 += v;
                    if (++p.Pos >= p.Length) pulses[i--] = pulses[--pulseCount];
                }
                float raw = in0 + in1;
                envelope += (Math.Abs(raw) - envelope) * (Math.Abs(raw) > envelope ? 0.05f : 0.002f);

                float exhaust = Pipe(0, in0) + 0.92f * Pipe(1, in1);

                // Muffler: two low passes, opening up with the throttle and the revs.
                float cutoff = s.Brightness * Lerp(5500f, 900f, s.Muffler) * (0.75f + 0.25f * load) * (0.8f + 0.5f * rev);
                float a = LowPassStep(cutoff);
                low1 += (exhaust - low1) * a;
                low2 += (low1 - low2) * a;
                float body = low2 + 0.25f * (1f - s.Muffler) * (low1 - low2);

                // Pops skip most of the muffler, so they crack.
                popLow += (pop - popLow) * LowPassStep(3500f);

                // Induction roar: band-passed noise in time with the firings.
                float intakeHz = s.IntakePitch * (0.7f + 1.6f * rev);
                float roar = Band(Noise(), intakeHz, 2.5f, ref intakeLow, ref intakeBand);
                // Kept below the hiss band: the band pass alone lets enough through above it to hiss once saturated.
                intakeLp1 += (roar - intakeLp1) * intakeStep;
                intakeLp2 += (intakeLp1 - intakeLp2) * intakeStep;
                // It rides on each firing rather than running on between them, so it reads as part of the note and not
                // as a steady hiss that only stops when the throttle lifts.
                float firing = Clamp(Math.Abs(raw) / (1.5f * envelope + 1e-4f), 0f, 1f);
                intakeGate += (firing - intakeGate) * gateStep;
                float intake = 1.1f * intakeLp2 * intakeGate
                               * s.IntakeLevel * (0.03f + (float)Math.Pow(load, 1.3)) * Math.Min(1f, envelope * 4f);

                // Valvetrain: short ticks of high-passed noise, two per firing, as loud as the engine is working.
                float noise = Noise();
                mechLow += (noise - mechLow) * LowPassStep(2500f);
                float tick = 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * cycles * cylinders * 2.0);
                tick *= tick * tick;
                float mech = (noise - mechLow) * 0.3f * s.MechLevel * (0.25f + 0.75f * rev) * (0.1f + 0.9f * load) * tick * tick;

                float engine = s.Drive * (body + intake + mech) * 0.9f;
                float shaped = (float)Math.Tanh(engine) / (float)Math.Sqrt(s.Drive) + 0.6f * popLow;
                // Saturating noise spreads it into a hiss far above the engine's note, so the top is rolled off after.
                topLow1 += (shaped - topLow1) * topStep;
                topLow2 += (topLow1 - topLow2) * topStep;
                shaped = topLow2;

                // Straight-cut gears whine with road speed.
                whinePhase += speed * s.GearWhinePitch / fs;
                float whine = s.GearWhine * 0.1f * Math.Min(1f, speed / 25f) * (0.35f + 0.65f * load)
                              * (float)(Math.Sin(2.0 * Math.PI * whinePhase) + 0.3 * Math.Sin(4.0 * Math.PI * whinePhase));

                // Turbo whistle, hiss and the blow-off on a lift.
                turboPhase += s.TurboPitch * (0.3f + 0.7f * boost) / fs;
                float hiss = Band(noise, 2800f + 1500f * boost, 1.5f, ref hissLow, ref hissBand);
                bovLow += (noise - bovLow) * LowPassStep(1200f);
                float turbo = s.Turbo * (0.05f * boost * boost * (float)Math.Sin(2.0 * Math.PI * turboPhase)
                                         + 0.12f * boost * hiss
                                         + 0.35f * blowOff * (noise - bovLow));

                // Electric motor: a whine with road speed, louder under power or regenerating.
                motorPhase += speed * s.HybridPitch / fs;
                double m = 2.0 * Math.PI * motorPhase;
                float motor = s.Hybrid * 0.07f * Math.Min(1f, speed / 20f) * (0.4f + 0.6f * Math.Max(load, 0.7f * (1f - load)))
                              * (float)(Math.Sin(m) + 0.5 * Math.Sin(2.0 * m) + 0.25 * Math.Sin(3.01 * m));

                float mix = shaped + whine + turbo + motor;
                // Remove the pulses' steady push.
                float y = mix - dcIn + 0.997f * dcOut;
                dcIn = mix;
                dcOut = y;
                output[n] = s.Volume * (float)Math.Tanh(y * 1.2f);
                lastLoad = load;
            }
        }

        private void Fire(int cylinder, float rev)
        {
            firings++;
            // Off the throttle the engine keeps the same tone, a little softer.
            float fuel = Math.Max(load, 0.4f);
            float amp = 0.12f + 0.88f * fuel;
            amp *= cylinderGain[cylinder] * (1f + s.Roughness * Noise());

            bool limiter = rpm > s.RedlineRpm * 0.985f && firings % 2 == 0;
            bool cut = cutSamples > 0 || limiter;
            if (cut) amp *= 0.1f;

            float popChance = s.Crackle * (overrun * 0.35f + (cutSamples > 0 ? 0.4f : 0f) + (limiter ? 0.2f : 0f));
            if (popChance > 0f && Uniform() < popChance)
                Add(new Pulse
                {
                    Wait = headerDelay[cylinder], Bank = 2, Breath = 0.45f, Skew = 0.5f,
                    Length = (int)(fs * (0.003f + 0.006f * Uniform())),
                    Amp = 0.6f + 0.9f * Uniform(),
                });

            float gap = 120f / Math.Max(rpm, 1f) / cylinders;
            Add(new Pulse
            {
                Wait = headerDelay[cylinder],
                Length = Math.Max(4, (int)(fs * Math.Max(0.0003f, s.PulseWidth * gap))),
                Bank = bankOf[cylinder],
                Amp = amp * (0.8f + 0.4f * rev),
                Breath = s.Breath * (0.4f + 0.6f * fuel),
                Skew = 0.6f,
            });
        }

        /// <summary>A bank's pipe: the pulse goes in, and the far end reflects it back inverted and damped.</summary>
        private float Pipe(int b, float x)
        {
            float[] line = pipe[b];
            int pos = pipePos[b];
            float back = line[pos];
            pipeDamp[b] += (back - pipeDamp[b]) * 0.3f;
            float y = x - 0.3f * s.PipeResonance * pipeDamp[b];
            line[pos] = y;
            pipePos[b] = pos + 1 == pipeLength[b] ? 0 : pos + 1;
            return y;
        }

        private void Overrun(float rev)
        {
            // Lifting off at high revs starts a spell of pops while unburnt fuel lights in the exhaust.
            if (lastLoad > 0.4f && load < 0.1f && rev > 0.35f) overrun = 1f;
            if (load > 0.15f || rev < 0.15f) overrun = 0f;
            overrun = Math.Max(0f, overrun - 0.7f / fs);
        }

        private void Turbo(float rev)
        {
            if (s.Turbo <= 0f) return;
            float target = load * Clamp((rev - 0.1f) / 0.5f, 0f, 1f);
            boost += (target - boost) / ((target > boost ? 0.8f : 0.3f) * fs);
            if (load > 0.5f && boost > 0.4f) blowOffArmed = true;
            if (blowOffArmed && load < 0.1f)
            {
                blowOff = boost;
                boost *= 0.4f;
                blowOffArmed = false;
            }
            blowOff *= 1f - 1f / (0.3f * fs);
        }

        private void Add(Pulse p)
        {
            if (pulseCount < MaxPulses) pulses[pulseCount++] = p;
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
