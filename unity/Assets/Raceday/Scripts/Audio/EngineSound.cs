using System;

namespace TarmacGauntlet.Raceday
{
    /// <summary>How the cylinders are arranged, which sets when each one fires and which exhaust bank it feeds.</summary>
    public enum EngineLayout
    {
        Inline3,
        Inline4,
        Flat4,
        Inline5,
        V6,
        Flat6,
        V8Crossplane,
        V8Flatplane,
        V10,
        V12,
    }

    /// <summary>
    /// Everything that shapes one engine's synthesised sound. Each class has a package
    /// (<see cref="EngineSounds.Tier"/>) and each car is that package with a few values changed
    /// (<see cref="EngineSounds.For"/>). Plain C# with no Unity types, so the audition tool can render it.
    /// </summary>
    public sealed class EngineSound
    {
        public EngineLayout Layout = EngineLayout.Inline4;
        public float IdleRpm = 850f;
        public float RedlineRpm = 6500f;

        // Exhaust: a pulse per firing travels down its header into its bank's pipe, then the muffler.
        /// <summary>Header length, m. Longer delays each pulse.</summary>
        public float HeaderLength = 0.5f;
        /// <summary>0 for equal-length headers, up to 1 for very unequal ones (the boxer rumble).</summary>
        public float HeaderSpread;
        /// <summary>Main pipe length, m. Sets the pipe's resonances.</summary>
        public float PipeLength = 1.8f;
        /// <summary>Second bank's pipe length as a share of the first, for V engines.</summary>
        public float BankSplit = 1.07f;
        /// <summary>How strongly the pipe rings, 0 to 0.9.</summary>
        public float PipeResonance = 0.5f;
        /// <summary>0 for an open race exhaust, 1 for a quiet road silencer.</summary>
        public float Muffler = 0.6f;
        /// <summary>Scales the tone's top end. 1 is neutral.</summary>
        public float Brightness = 1f;
        /// <summary>Length of each exhaust pulse as a share of the gap between firings, 0.2 to 0.9.</summary>
        public float PulseWidth = 0.5f;
        /// <summary>Breathy noise inside each pulse, 0 to 1.</summary>
        public float Breath = 0.35f;

        // Character.
        /// <summary>Random strength of each firing, 0 to 0.5.</summary>
        public float Roughness = 0.08f;
        /// <summary>Fixed strength difference between cylinders, 0 to 0.5. Gives the lope of a tired or lumpy engine.</summary>
        public float Asymmetry = 0.05f;
        /// <summary>Saturation, 1 is clean and 4 is gravelly.</summary>
        public float Drive = 1.6f;
        /// <summary>Pops and bangs on the overrun, 0 to 1.</summary>
        public float Crackle;

        // Other sources.
        public float IntakeLevel = 0.25f;
        /// <summary>Centre of the induction roar at idle, Hz. Rises with the revs.</summary>
        public float IntakePitch = 450f;
        /// <summary>Valvetrain and mechanical clatter.</summary>
        public float MechLevel = 0.08f;
        /// <summary>Straight-cut gear whine, 0 for a road gearbox.</summary>
        public float GearWhine;
        /// <summary>Whine pitch, Hz per m/s of road speed.</summary>
        public float GearWhinePitch = 22f;
        /// <summary>Turbo whistle, spool and blow-off, 0 for none.</summary>
        public float Turbo;
        /// <summary>Whistle pitch at full boost, Hz.</summary>
        public float TurboPitch = 4200f;
        /// <summary>Electric motor whine, 0 for none.</summary>
        public float Hybrid;
        /// <summary>Motor whine pitch, Hz per m/s of road speed.</summary>
        public float HybridPitch = 38f;

        public float Volume = 0.7f;

        public int Cylinders => Layout switch
        {
            EngineLayout.Inline3 => 3,
            EngineLayout.Inline4 or EngineLayout.Flat4 => 4,
            EngineLayout.Inline5 => 5,
            EngineLayout.V6 or EngineLayout.Flat6 => 6,
            EngineLayout.V10 => 10,
            EngineLayout.V12 => 12,
            _ => 8,
        };

        /// <summary>
        /// When each cylinder fires, as a share of the four-stroke cycle in firing order, and the exhaust bank it
        /// feeds (0 or 1). An inline engine uses one bank. A crossplane V8 fires its banks unevenly, which is its burble.
        /// </summary>
        public void Firing(out float[] at, out int[] bank)
        {
            int n = Cylinders;
            at = new float[n];
            bank = new int[n];
            for (int i = 0; i < n; i++) at[i] = i / (float)n;
            switch (Layout)
            {
                case EngineLayout.V8Crossplane:
                    // Firing order 1-5-4-2-6-3-7-8, cylinders 1 to 4 on the left bank.
                    int[] banks = { 0, 1, 0, 0, 1, 0, 1, 1 };
                    Array.Copy(banks, bank, n);
                    break;
                case EngineLayout.V6:
                case EngineLayout.Flat4:
                case EngineLayout.Flat6:
                case EngineLayout.V8Flatplane:
                case EngineLayout.V10:
                case EngineLayout.V12:
                    for (int i = 0; i < n; i++) bank[i] = i % 2;
                    break;
            }
        }

        /// <summary>A copy, changed by <paramref name="change"/>. How a car is made from its class's package.</summary>
        public EngineSound With(Action<EngineSound> change)
        {
            var copy = (EngineSound)MemberwiseClone();
            change(copy);
            return copy;
        }
    }
}
