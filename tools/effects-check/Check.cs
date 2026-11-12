using System;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

/// <summary>
/// Heats every car's brake discs through hard stops and long straights and checks the glow: the quick cars' fronts glow
/// after a stop from their top speed, the slow road cars' barely, every disc cools dark down the next straight, a race of
/// stops stays under the hottest, and a long frame never cools a disc past the air.
/// </summary>
internal static class Check
{
    private const float Dt = 1f / 60f, G = 9.81f;
    private static int failed;

    private static int Main()
    {
        foreach (CarModel model in CarModels.All)
        {
            CarSpec spec = CarSpec.For(model.Id);
            float top = spec.TopSpeed;
            float front = BrakeHeat.Ambient, rear = BrakeHeat.Ambient;
            // Flat out to the corner, then the hardest stop to a third of the speed.
            Straight(spec, ref front, ref rear, top * 0.9f, 8f);
            float peak = Stop(spec, ref front, ref rear, top * 0.9f, top * 0.3f);
            float glow = BrakeHeat.Glow(peak);
            string name = $"{model.CarClass} {model.Id}";
            bool quick = model.CarClass == CarClass.A || model.CarClass == CarClass.B || model.CarClass == CarClass.F1;
            bool slow = model.CarClass == CarClass.F || model.CarClass == CarClass.E;
            if (quick) Expect($"{name}: front discs glow after a stop from {top * 0.9f:0} m/s", glow > 0.1f, $"peak {peak:0} °C, glow {glow:0.00}");
            if (!quick && !slow) Console.WriteLine($"     {name}: peak {peak:0} °C, glow {glow:0.00}");
            if (slow) Expect($"{name}: front discs stay dark or dull", glow < 0.35f, $"peak {peak:0} °C, glow {glow:0.00}");
            Expect($"{name}: rears cooler than fronts", rear < front, $"front {front:0} °C, rear {rear:0} °C");
            Straight(spec, ref front, ref rear, top * 0.7f, 20f);
            Expect($"{name}: dark again down a straight", BrakeHeat.Glow(front) < 0.01f, $"front {front:0} °C");

            // A long race: a heavy stop every twelve seconds.
            float hottest = 0f;
            for (int lap = 0; lap < 40; lap++)
            {
                Straight(spec, ref front, ref rear, top * 0.85f, 12f);
                hottest = Math.Max(hottest, Stop(spec, ref front, ref rear, top * 0.85f, top * 0.35f));
            }
            Expect($"{name}: a race of stops stays at or under the hottest", hottest <= BrakeHeat.Hottest, $"hottest {hottest:0} °C");
        }

        float t = BrakeHeat.Step(900f, 30f, 0f, 1.5f, 0.3f, 80f);
        Expect("a long frame cools a disc no further than the air", t >= BrakeHeat.Ambient && t < 100f, $"{t:0} °C");
        Expect("a cold disc stays cold on a straight", Math.Abs(BrakeHeat.Step(BrakeHeat.Ambient, 1f, 0f, 1.5f, 0.3f, 80f) - BrakeHeat.Ambient) < 1e-3f, "");
        Expect("no glow at the glow's start", BrakeHeat.Glow(BrakeHeat.GlowFrom) == 0f, "");
        Expect("full glow at its end", Math.Abs(BrakeHeat.Glow(BrakeHeat.GlowFull) - 1f) < 1e-4f, "");
        float last = -1f;
        bool brighter = true;
        for (float g = 0f; g <= 1f; g += 0.05f)
        {
            UnityEngine.Color c = BrakeHeat.Colour(g);
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (lum < last) brighter = false;
            last = lum;
        }
        Expect("the glow only brightens as the disc heats", brighter, "");

        Smoke();

        Console.WriteLine(failed == 0 ? "effects-check: all passed" : $"effects-check: {failed} FAILED");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Tyre smoke: never from a blip on a straight, always from a held lockup or wheelspin.</summary>
    private static void Smoke()
    {
        // A wheel skipping over a bump: full slide for a few frames, then grip, again and again down a straight.
        float held = 0f, most = 0f;
        for (int f = 0; f < 600; f++)
            most = Math.Max(most, TyreSmoke.Step(ref held, f % 30 < 4 ? 0.85f : 0f, Dt));
        Expect("a wheel skipping over bumps on a straight never smokes", most == 0f, $"smoke {most:0.00}");

        held = 0f;
        most = 0f;
        for (int f = 0; f < 600; f++) most = Math.Max(most, TyreSmoke.Step(ref held, TyreSmoke.From * 0.95f, Dt));
        Expect("a tyre just under the threshold never smokes", most == 0f, $"smoke {most:0.00}");

        held = 0f;
        float first = -1f, end = 0f;
        for (int f = 0; f < 60; f++)
        {
            end = TyreSmoke.Step(ref held, 0.85f, Dt);
            if (end > 0f && first < 0f) first = f * Dt;
        }
        Expect("a held wheelspin smokes once it has lasted", first >= TyreSmoke.Hold - Dt && first <= TyreSmoke.Hold + 2f * Dt, $"first at {first:0.000} s");
        Expect("a hard slide smokes thickly", end > 0.6f, $"smoke {end:0.00}");
        Expect("grip stops the smoke at once", TyreSmoke.Step(ref held, 0f, Dt) == 0f && held == 0f, $"held {held:0.00}");

        held = 0f;
        float lighter = 0f, harder = 0f;
        for (int f = 0; f < 30; f++) lighter = TyreSmoke.Step(ref held, 0.5f, Dt);
        held = 0f;
        for (int f = 0; f < 30; f++) harder = TyreSmoke.Step(ref held, 0.9f, Dt);
        Expect("a harder slide smokes more", harder > lighter && lighter > 0f, $"{lighter:0.00} then {harder:0.00}");
        held = 0f;
        Expect("a long frame of slide counts in full", TyreSmoke.Step(ref held, 1f, 0.5f) == 1f, "");
    }

    private static void Straight(CarSpec spec, ref float front, ref float rear, float speed, float seconds)
    {
        for (float s = 0f; s < seconds; s += Dt)
        {
            front = BrakeHeat.Step(front, Dt, 0f, spec.BrakeDecel, spec.FrontBrakeShare * 0.5f, speed);
            rear = BrakeHeat.Step(rear, Dt, 0f, spec.BrakeDecel, (1f - spec.FrontBrakeShare) * 0.5f, speed);
        }
    }

    /// <summary>Full brakes from one speed to another; returns the front discs' hottest.</summary>
    private static float Stop(CarSpec spec, ref float front, ref float rear, float from, float to)
    {
        float peak = front;
        for (float v = from; v > to; v -= spec.BrakeDecel * G * Dt)
        {
            front = BrakeHeat.Step(front, Dt, 1f, spec.BrakeDecel, spec.FrontBrakeShare * 0.5f, v);
            rear = BrakeHeat.Step(rear, Dt, 1f, spec.BrakeDecel, (1f - spec.FrontBrakeShare) * 0.5f, v);
            peak = Math.Max(peak, front);
        }
        return peak;
    }

    private static void Expect(string what, bool ok, string detail)
    {
        if (!ok) failed++;
        Console.WriteLine(ok ? $"ok   {what}" : $"FAIL {what}: {detail}");
    }
}
