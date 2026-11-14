using System;
using TarmacGauntlet.Raceday;

// Cars 4.6 m long, so half 2.3 m. ahead is metres from your middle to theirs; their nose is 2.3 m ahead of that.
static class Check
{
    static int failed;
    const float Half = 2.3f;

    static void Is(string name, int want, int got)
    {
        bool ok = want == got;
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name + (ok ? "" : $": want {want}, got {got}"));
    }

    static void Near(string name, float want, float got)
    {
        bool ok = Math.Abs(want - got) < 0.01f;
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name + (ok ? "" : $": want {want}, got {got}"));
    }

    // Their nose this many metres behind your rear bumper (negative: past it).
    static float Behind(float gap) => -(gap + 2f * Half);

    static int Level(float side, float gap, float closing) => Proximity.Level(side, Behind(gap), 0f, 1f, Half, Half, closing);

    static int Main()
    {
        Is("alongside, nose past your bumper: keep straight", 3, Level(-3.5f, -1f, 5f));
        Is("middle level with yours: ahead, gone", 0, Proximity.Level(-3.5f, 0f, 0f, 1f, Half, Half, 5f));
        Is("middle just ahead: gone", 0, Proximity.Level(3.5f, 0.1f, 0f, 1f, Half, Half, 5f));
        Is("4 m back, slow: swipe", 2, Level(3.5f, 4f, 4f));
        Is("6 m back, slow: get ready", 1, Level(3.5f, 6f, 4f));
        Is("14 m back, slow: get ready", 1, Level(3.5f, 14f, 4f));
        Is("16 m back, slow: nothing", 0, Level(3.5f, 16f, 4f));
        // 80 km/h faster: level 2 from 0.8 s, about 17.8 m; level 1 from 2 s, about 44.4 m.
        float fast = 80f / 3.6f;
        Is("17 m back at 80 km/h: swipe", 2, Level(3.5f, 17f, fast));
        Is("19 m back at 80 km/h: get ready", 1, Level(3.5f, 19f, fast));
        Is("44 m back at 80 km/h: get ready", 1, Level(3.5f, 44f, fast));
        Is("45 m back at 80 km/h: nothing", 0, Level(3.5f, 45f, fast));
        Is("pulling away: metres only", 1, Level(3.5f, 10f, -20f));
        Is("straight behind still shows", 2, Level(0f, 3f, 4f));
        Is("too far to the side", 0, Level(7f, 3f, 4f));
        Is("on a bridge above", 0, Proximity.Level(0f, Behind(3f), 5f, 1f, Half, Half, 4f));
        Is("pointing the other way", 0, Proximity.Level(3f, Behind(3f), 0f, -1f, Half, Half, 4f));

        Near("straight behind: 0 degrees", 0f, Proximity.Bearing(0f, -10f));
        Near("left or right alike", Proximity.Bearing(-3f, -5f), Proximity.Bearing(3f, -5f));
        Near("at 45 degrees", 45f, Proximity.Bearing(4f, -4f));
        Near("alongside: 90 degrees", 90f, Proximity.Bearing(3.5f, 0f));
        Near("never past alongside", 90f, Proximity.Bearing(3.5f, 1f));

        Console.WriteLine(failed == 0 ? "All checks pass." : failed + " checks FAIL.");
        return failed == 0 ? 0 : 1;
    }
}
