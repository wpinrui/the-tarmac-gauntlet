using System.Collections.Generic;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The engine sound packages: one per class, and each car as its class's package with a few values changed.
    /// To add or retune a car, change its line in <see cref="Cars"/>. To change a whole class, change its package.
    /// </summary>
    public static class EngineSounds
    {
        /// <summary>The package for a class: the sound every car in it starts from.</summary>
        public static EngineSound Tier(CarClass carClass) => carClass switch
        {
            // Small, tired economy fours: buzzy, thin and well silenced.
            CarClass.F => new EngineSound
            {
                Layout = EngineLayout.Inline4, IdleRpm = 850f, RedlineRpm = 6200f,
                HeaderLength = 0.45f, PipeLength = 2.4f, PipeResonance = 0.45f, Muffler = 0.8f, Brightness = 0.85f,
                PulseWidth = 0.55f, Breath = 0.4f, Roughness = 0.14f, Asymmetry = 0.1f, Drive = 1.4f,
                IntakeLevel = 0.3f, IntakePitch = 380f, MechLevel = 0.14f, Volume = 0.6f,
            },
            // Modern family fours, mostly small turbos: smooth and quiet.
            CarClass.E => new EngineSound
            {
                Layout = EngineLayout.Inline4, IdleRpm = 750f, RedlineRpm = 6500f,
                HeaderLength = 0.5f, PipeLength = 2.6f, PipeResonance = 0.4f, Muffler = 0.85f, Brightness = 0.9f,
                PulseWidth = 0.6f, Breath = 0.3f, Roughness = 0.06f, Asymmetry = 0.04f, Drive = 1.3f,
                IntakeLevel = 0.28f, IntakePitch = 420f, MechLevel = 0.06f, Turbo = 0.35f, TurboPitch = 4600f, Volume = 0.6f,
            },
            // Sports coupes and hot hatches: free-breathing fours with a sporty exhaust.
            CarClass.D => new EngineSound
            {
                Layout = EngineLayout.Inline4, IdleRpm = 900f, RedlineRpm = 7500f,
                HeaderLength = 0.55f, PipeLength = 2.0f, PipeResonance = 0.55f, Muffler = 0.5f, Brightness = 1.05f,
                PulseWidth = 0.5f, Breath = 0.3f, Roughness = 0.07f, Asymmetry = 0.05f, Drive = 1.9f, Crackle = 0.15f,
                IntakeLevel = 0.4f, IntakePitch = 480f, MechLevel = 0.08f, Volume = 0.7f,
            },
            // Performance sixes: the class's middle ground between the saloons and the sports cars.
            CarClass.C => new EngineSound
            {
                Layout = EngineLayout.V6, IdleRpm = 800f, RedlineRpm = 7200f,
                HeaderLength = 0.6f, PipeLength = 2.0f, BankSplit = 1.06f, PipeResonance = 0.55f, Muffler = 0.5f, Brightness = 1f,
                PulseWidth = 0.5f, Breath = 0.28f, Roughness = 0.05f, Asymmetry = 0.04f, Drive = 2f, Crackle = 0.2f,
                IntakeLevel = 0.35f, IntakePitch = 520f, MechLevel = 0.07f, Volume = 0.75f,
            },
            // GT race cars: flat-plane V8s on open race exhausts.
            CarClass.B => new EngineSound
            {
                Layout = EngineLayout.V8Flatplane, IdleRpm = 1100f, RedlineRpm = 9000f,
                HeaderLength = 0.5f, PipeLength = 1.3f, BankSplit = 1.04f, PipeResonance = 0.6f, Muffler = 0.2f, Brightness = 1.15f,
                PulseWidth = 0.45f, Breath = 0.25f, Roughness = 0.05f, Asymmetry = 0.03f, Drive = 2.4f, Crackle = 0.35f,
                IntakeLevel = 0.45f, IntakePitch = 600f, MechLevel = 0.1f, GearWhine = 0.12f, Volume = 0.8f,
            },
            // Hybrid prototypes: twin turbo V6s, a gearbox whine and an electric motor on the front axle.
            CarClass.A => new EngineSound
            {
                Layout = EngineLayout.V6, IdleRpm = 1500f, RedlineRpm = 9000f,
                HeaderLength = 0.45f, PipeLength = 1.2f, BankSplit = 1.05f, PipeResonance = 0.6f, Muffler = 0.25f, Brightness = 1.1f,
                PulseWidth = 0.45f, Breath = 0.3f, Roughness = 0.04f, Asymmetry = 0.02f, Drive = 2.2f, Crackle = 0.2f,
                IntakeLevel = 0.35f, IntakePitch = 650f, MechLevel = 0.1f, GearWhine = 0.18f,
                Turbo = 0.5f, TurboPitch = 5200f, Hybrid = 0.4f, Volume = 0.8f,
            },
            // The Formula One car: a 19,000 rpm V8 shriek.
            _ => new EngineSound
            {
                Layout = EngineLayout.V8Flatplane, IdleRpm = 4000f, RedlineRpm = 19000f,
                HeaderLength = 0.4f, PipeLength = 0.9f, BankSplit = 1.03f, PipeResonance = 0.6f, Muffler = 0.1f, Brightness = 1.3f,
                PulseWidth = 0.4f, Breath = 0.2f, Roughness = 0.03f, Asymmetry = 0.02f, Drive = 2.6f, Crackle = 0.25f,
                IntakeLevel = 0.5f, IntakePitch = 900f, MechLevel = 0.12f, GearWhine = 0.2f, GearWhinePitch = 26f, Volume = 0.85f,
            },
        };

        /// <summary>A car's sound: its class's package with its own changes.</summary>
        public static EngineSound For(string modelId)
        {
            CarClass carClass = ClassOf(modelId);
            EngineSound tier = Tier(carClass);
            return Cars.TryGetValue(modelId, out var change) ? tier.With(change) : tier;
        }

        private static CarClass ClassOf(string modelId) => modelId.Split('-')[0] switch
        {
            "f" => CarClass.F,
            "e" => CarClass.E,
            "d" => CarClass.D,
            "c" => CarClass.C,
            "b" => CarClass.B,
            "a" => CarClass.A,
            _ => CarClass.F1,
        };

        /// <summary>Each car's changes from its class's package.</summary>
        public static readonly Dictionary<string, System.Action<EngineSound>> Cars = new Dictionary<string, System.Action<EngineSound>>
        {
            // F
            ["f-01"] = s => { s.Roughness = 0.22f; s.Asymmetry = 0.16f; s.RedlineRpm = 5800f; s.MechLevel = 0.2f; },          // Nusa Carrier: old and rough
            ["f-02"] = s => { s.PipeLength = 2.0f; s.Brightness = 1f; s.RedlineRpm = 6600f; s.IntakePitch = 430f; },         // Liberty Omaha: small and buzzy
            ["f-03"] = s => { s.Roughness = 0.07f; s.Asymmetry = 0.05f; s.Muffler = 0.9f; s.MechLevel = 0.08f; },            // Tora Daruma: smooth and quiet
            ["f-04"] = s => { s.IdleRpm = 800f; s.RedlineRpm = 5600f; s.PipeLength = 2.8f; s.PulseWidth = 0.65f; s.Brightness = 0.75f; }, // Kaiser Firefly: big lazy four
            ["f-05"] = s => { s.Brightness = 0.95f; s.Muffler = 0.75f; },                                                       // Liberty Collier
            ["f-06"] = s => { s.Muffler = 0.6f; s.RedlineRpm = 6800f; s.Drive = 1.7f; s.IntakeLevel = 0.4f; },               // Nusa Fox: the sporty one

            // E
            ["e-07"] = s => { s.Layout = EngineLayout.Inline3; s.Asymmetry = 0.06f; s.Turbo = 0.45f; s.PipeLength = 2.3f; },  // Liberty Fantasy: small turbo triple
            ["e-08"] = s => { s.Turbo = 0f; s.Muffler = 0.9f; s.Roughness = 0.04f; },                                           // Seorak Hwaeum: plain and smooth
            ["e-09"] = s => { s.Turbo = 0.4f; s.Brightness = 0.95f; },                                                           // Liberty Fortress
            ["e-10"] = s => { s.Turbo = 0.45f; s.TurboPitch = 4900f; s.PipeLength = 2.4f; },                                    // Kaiser Oboe
            ["e-11"] = s => { s.Turbo = 0.3f; s.PipeLength = 3.0f; s.Brightness = 0.8f; s.Muffler = 0.9f; },                  // Liberty Mantle: big and quiet

            // D
            ["d-12"] = s => { s.Layout = EngineLayout.Flat4; s.HeaderSpread = 0.25f; s.RedlineRpm = 7400f; },                  // Seorak Hoeori: boxer
            ["d-13"] = s => { s.Turbo = 0.55f; s.RedlineRpm = 6800f; s.Muffler = 0.6f; s.Crackle = 0.3f; },                     // Kaiser Cappuccino: turbo four, pops
            ["d-14"] = s => { s.Layout = EngineLayout.Flat4; s.HeaderSpread = 0.9f; s.Turbo = 0.6f; s.RedlineRpm = 7000f; s.Brightness = 0.9f; }, // Seorak Rally: the boxer rumble
            ["d-15"] = s => { s.RedlineRpm = 9000f; s.Brightness = 1.15f; s.IntakeLevel = 0.5f; s.IntakePitch = 560f; },        // Tora Hanabi: screaming four

            // C
            ["c-16"] = s => { s.Layout = EngineLayout.V8Crossplane; s.IdleRpm = 700f; s.RedlineRpm = 7000f; s.PipeLength = 2.2f; s.BankSplit = 1.12f; s.Drive = 2.4f; s.Crackle = 0.35f; s.Turbo = 0.3f; }, // Archer Crescendo: V8 burble
            ["c-17"] = s => { s.Turbo = 0.45f; s.Muffler = 0.75f; s.RedlineRpm = 6500f; s.Crackle = 0.05f; },                 // Kaiser Elevate: refined turbo V6
            ["c-18"] = s => { s.RedlineRpm = 8000f; s.Brightness = 1.1f; s.IntakeLevel = 0.45f; },                              // Tora Zero: high-revving V6
            ["c-19"] = s => { s.Layout = EngineLayout.Flat6; s.RedlineRpm = 9000f; s.Brightness = 1.15f; s.IntakePitch = 620f; s.MechLevel = 0.1f; }, // Kaiser Klassik: flat six
            ["c-20"] = s => { s.Turbo = 0.65f; s.TurboPitch = 4400f; s.RedlineRpm = 7000f; s.Brightness = 0.9f; },            // Tora Giga: twin turbo V6

            // B
            ["b-21"] = s => { s.Brightness = 1.25f; s.IntakePitch = 680f; },                                                    // Feretti Vortice: flat-plane V8 wail
            ["b-22"] = s => { s.Turbo = 0.55f; s.RedlineRpm = 8500f; s.Brightness = 1.0f; },                                    // Archer Tachyon: twin turbo V8
            ["b-23"] = s => { s.Layout = EngineLayout.V8Crossplane; s.IdleRpm = 900f; s.RedlineRpm = 7200f; s.BankSplit = 1.12f; s.PipeLength = 1.6f; s.Drive = 2.8f; }, // Archer Jet Black: big crossplane V8
            ["b-24"] = s => { s.Layout = EngineLayout.V10; s.RedlineRpm = 8400f; s.Brightness = 1.3f; s.PipeLength = 1.1f; },  // Kaiser Scream: V10
            ["b-25"] = s => { s.Layout = EngineLayout.V6; s.Turbo = 0.7f; s.RedlineRpm = 7000f; s.Brightness = 0.95f; s.Drive = 2.6f; }, // Liberty Bounty: twin turbo V6

            // A
            ["a-26"] = s => { s.Layout = EngineLayout.V8Flatplane; s.Turbo = 0f; s.RedlineRpm = 9150f; s.Brightness = 1.25f; s.Hybrid = 0.3f; }, // Kaiser Sceptre: NA V8 hybrid
            ["a-27"] = s => { s.Layout = EngineLayout.V8Flatplane; s.Turbo = 0.6f; s.RedlineRpm = 8500f; s.Hybrid = 0.3f; },  // Archer Hypernova: twin turbo V8 hybrid
            ["a-28"] = s => { s.IdleRpm = 900f; s.RedlineRpm = 5000f; s.PulseWidth = 0.65f; s.Turbo = 0.7f; s.TurboPitch = 3800f; s.Brightness = 0.7f; s.Drive = 1.8f; s.Crackle = 0f; s.Hybrid = 0.5f; }, // Kaiser K24 Dynamo: diesel V6
            ["a-29"] = s => { s.Hybrid = 0.55f; },                                                                               // Seorak Cheonma: the reference prototype
            ["a-30"] = s => { s.BankSplit = 1.09f; s.Brightness = 1.0f; s.Hybrid = 0.45f; s.HybridPitch = 34f; },              // Liberty GE67
        };
    }
}
