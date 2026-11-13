// Checks the race preview (BroadcastIntro) outside Unity: its camera path over the real circuit, and its figures from a career.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;
using UnityEngine;

static class Check
{
    static int failures;
    static Circuit circuit;
    /// <summary>With "dump" after the circuit, every frame of every shot is written here as CSV, for plotting.</summary>
    static StreamWriter dump;

    static void Expect(bool ok, string name, string detail = "")
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name + (detail.Length > 0 ? ": " + detail : ""));
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        circuit = new Circuit(data);
        if (args.Length > 2 && args[1] == "dump") dump = new StreamWriter(args[2]);
        Path();
        dump?.Dispose();
        Facts();
        Console.WriteLine(failures == 0 ? "all checks pass" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- the camera

    /// <summary>True if the ground hides the target from the camera anywhere between them, more than a metre over the sight line.</summary>
    static bool Hidden(PreviewPath.Pose p)
    {
        for (int i = 1; i < 40; i++)
        {
            Vector3 q = Vector3.Lerp(p.Position, p.Target, i / 40f);
            if (circuit.TerrainHeight(q.x, q.z) > q.y + 1f) return true;
        }
        return false;
    }

    static float Clearance(PreviewPath.Pose p) => p.Position.y - circuit.TerrainHeight(p.Position.x, p.Position.z);

    static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    /// <summary>Runs a shot at 60 fps with the stage's eased aim: least clearance, share of frames hidden, fastest turn and move.</summary>
    static void Shot(string name, Func<float, PreviewPath.Pose> at, float length, Func<float, Vector3> subject = null)
    {
        const float Dt = 1f / 60f, Aim = 3f;
        float least = float.MaxValue, turn = 0f, speed = 0f, turnAt = 0f;
        int hidden = 0, frames = 0;
        Vector3 aim = Vector3.zero, was = Vector3.zero;
        float yaw = 0f;
        for (float t = 0f; t <= length; t += Dt, frames++)
        {
            PreviewPath.Pose p = at(t);
            // The stage eases the camera's heading, not its target, so a target swinging round a hairpin can't spin it.
            Vector3 want = (p.Target - p.Position).normalized;
            aim = frames == 0 ? want : Vector3.Lerp(aim, want, 1f - Mathf.Exp(-Aim * Dt)).normalized;
            float y = Yaw(aim);
            if (frames > 0)
            {
                float rate = Mathf.Abs(Mathf.DeltaAngle(yaw, y)) / Dt;
                if (rate > turn) { turn = rate; turnAt = t; }
                speed = Mathf.Max(speed, (p.Position - was).magnitude / Dt);
            }
            if (dump != null) dump.WriteLine($"{name},{t:0.000},{p.Position.x:0.0},{p.Position.y:0.0},{p.Position.z:0.0},{aim.x:0.0},{aim.z:0.0},{p.Target.x:0.0},{p.Target.z:0.0},{y:0.0}");
            yaw = y;
            was = p.Position;
            least = Mathf.Min(least, Clearance(p));
            if (Hidden(subject == null ? p : new PreviewPath.Pose { Position = p.Position, Target = subject(t) })) { hidden++; if (dump != null) dump.WriteLine($"hidden,{name},{t:0.00}"); }
        }
        float share = hidden / (float)frames;
        Expect(least >= PreviewPath.Clearance - 0.01f, name + " keeps clear of the ground", $"least {least:0.0} m");
        Expect(share <= 0.02f, name + (subject == null ? " sees its target" : " sees the road ahead"), $"{share:P1} of frames hidden");
        Expect(turn <= 60f, name + " turns smoothly", $"fastest turn {turn:0} deg/s at {turnAt:0.00} s, fastest move {speed:0} m/s");
    }

    static void Path()
    {
        var path = new PreviewPath(circuit);
        Expect(Mathf.Abs(path.DistanceAt(0f)) < 0.01f && Mathf.Abs(path.DistanceAt(path.Duration) - circuit.Lap) < 1f, "flyover runs one lap",
            $"{path.DistanceAt(0f):0.0} m to {path.DistanceAt(path.Duration):0.0} m of {circuit.Lap:0.0} m");
        bool rising = true;
        for (float t = 0.05f; t <= path.Duration; t += 0.05f) rising &= path.DistanceAt(t) >= path.DistanceAt(t - 0.05f);
        Expect(rising, "flyover never goes back");
        Console.WriteLine($"     flyover takes {path.Duration:0.0} s");
        Shot("flyover", path.Fly, path.Duration, t => circuit.Centre[circuit.IndexOf(path.DistanceAt(t) + 120f)]);
        Shot("opening shot", t => path.Open(t / 7f), 7f);
        foreach (int c in path.Tightest(4))
            Shot($"turn {circuit.Data.corners[c].number} shot", t => path.Orbit(c, t / 8f), 8f);

        int called = 0, last = -1;
        for (float t = 0f; t <= path.Duration; t += 1f / 60f)
        {
            int c = path.CornerAt(path.DistanceAt(t));
            if (c >= 0 && c != last) called++;
            if (c >= 0) last = c;
        }
        Expect(called == path.Corners, "flyover calls out every turn once", $"{called} of {path.Corners}");
        // How long each turn is on screen, so none flashes by.
        var shown = new float[path.Corners];
        for (float t = 0f; t <= path.Duration; t += 1f / 60f)
        {
            int c = path.CornerAt(path.DistanceAt(t));
            if (c >= 0) shown[c] += 1f / 60f;
        }
        Expect(shown.Min() >= 1.15f, "each turn call stays up long enough to read", "shortest " + shown.Min().ToString("0.0") + " s, longest " + shown.Max().ToString("0.0") + " s");
        Console.WriteLine("     tightest turns for the panel shots: " + string.Join(", ", path.Tightest(4).Select(c => circuit.Data.corners[c].number)));
    }

    // ---------------------------------------------------------------- the figures

    static void Facts()
    {
        Expect(IntroFacts.CalendarYear(1) == 2026 && IntroFacts.CalendarYear(5) == 2030, "year 1 is 2026");
        Expect(IntroFacts.LapText(134.56f) == "2:14.6" && IntroFacts.LapText(59.96f) == "1:00.0" && IntroFacts.LapText(61.04f) == "1:01.0",
            "lap times read m:ss.t", $"{IntroFacts.LapText(134.56f)} {IntroFacts.LapText(59.96f)} {IntroFacts.LapText(61.04f)}");

        GameState game = GameInit.InitializeGame(new NewGameOptions { TeamName = "Your Team", Skills = new PlayerSkills { Driver = 16 } });
        game = GameActions.BuyCar(game, new CarInstance { Id = "test-car", ModelId = "a-29", Condition = 100 }, 0, "");
        game = GameActions.BuyTyres(game, 12, 0);
        game = GameActions.BuySpares(game, 12, 0);
        game = GameActions.SetCrewSize(game, 8);
        RaceSetup setup = RaceWeekend.Begin(game);
        float[] predicted = Predict(setup);

        IntroFacts first = IntroFacts.From(setup, predicted);
        Expect(first.Year == 2026, "first race is 2026", first.Year.ToString());
        Expect(first.Podiums.Count == 0 && first.Winners.Count == 0, "first year has no podiums or winners");
        var classes = setup.Grid.Select(g => g.Model.CarClass).Distinct().ToList();
        Expect(first.Laps.Count == classes.Count && first.Favourites.Count == classes.Count, "a lap time and a favourite for every class on the grid",
            $"{first.Laps.Count} lap times, {first.Favourites.Count} favourites, {classes.Count} classes");
        bool ordered = true;
        for (int i = 1; i < first.Laps.Count; i++) ordered &= first.Laps[i].Seconds > first.Laps[i - 1].Seconds * 0.97f;
        Expect(ordered, "lap times run fastest class first", string.Join(", ", first.Laps.Select(l => $"{l.Class} {IntroFacts.LapText(l.Seconds)}")));
        bool quickest = true;
        foreach (IntroFacts.Favourite f in first.Favourites)
        {
            float best = Enumerable.Range(0, setup.Grid.Count).Where(i => setup.Grid[i].Model.CarClass == f.Class).Min(i => predicted[i]);
            int pick = Enumerable.Range(0, setup.Grid.Count).First(i => setup.Grid[i].Model.CarClass == f.Class && predicted[i] == best);
            quickest &= setup.Grid[pick].TeamName == f.Team;
        }
        Expect(quickest, "each favourite is its class's quickest car", string.Join("; ", first.Favourites.Select(f => $"{f.Class} {f.Team} ({f.Model})")));

        // Three past races, the last with a retired class winner.
        var teams = game.Teams.Take(12).ToList();
        RaceHistoryEntry Race(int year, bool retiredWinner)
        {
            var results = new List<RaceResult>();
            for (int i = 0; i < teams.Count; i++)
                results.Add(new RaceResult { TeamId = teams[(i + year) % teams.Count].Id, CarClass = i < 6 ? CarClass.A : CarClass.D, Position = i + 1, Retired = retiredWinner && i == 0 });
            return new RaceHistoryEntry { Year = year, Results = results };
        }
        GameState later = setup.Game with { CurrentYear = 4, RaceHistory = new[] { Race(1, false), Race(2, false), Race(3, true) } };
        IntroFacts fourth = IntroFacts.From(setup with { Game = later }, predicted);
        Expect(fourth.Year == 2029 && fourth.PodiumYear == 2028, "year 4 is 2029, with 2028's podiums", $"{fourth.Year}, {fourth.PodiumYear}");
        string Name(string id) => teams.First(t => t.Id == id).Name;
        bool podiums = fourth.Podiums.Count == 2 && fourth.Podiums[0].Class == CarClass.A && fourth.Podiums[1].Class == CarClass.D
            && fourth.Podiums[0].Teams.SequenceEqual(new[] { 1, 2, 3 }.Select(i => Name(teams[(i + 3) % teams.Count].Id)))
            && fourth.Podiums[1].Teams.SequenceEqual(new[] { 6, 7, 8 }.Select(i => Name(teams[(i + 3) % teams.Count].Id)));
        Expect(podiums, "podiums are each class's top three finishers, fastest class first", string.Join("; ", fourth.Podiums.Select(p => p.Class + " " + string.Join(", ", p.Teams))));
        Expect(fourth.Winners.Select(w => w.Year).SequenceEqual(new[] { 2028, 2027, 2026 }), "winners run latest first", string.Join(", ", fourth.Winners.Select(w => w.Year + " " + w.Team)));
        Expect(fourth.Winners[0].Team == Name(teams[(1 + 3) % teams.Count].Id), "a retired car doesn't win");
    }

    /// <summary>Each grid car's lap at its starting driver's limit, as the raceday scene predicts it.</summary>
    static float[] Predict(RaceSetup setup)
    {
        var line = new RacingLine(circuit);
        var laps = new float[setup.Grid.Count];
        for (int i = 0; i < laps.Length; i++)
        {
            GridCar g = setup.Grid[i];
            CarSpec spec = CarSpec.For(g.Model.Id);
            spec.Stats = EffectiveStats.CalculateEffectiveStats(g.Car, g.Model);
            laps[i] = new SpeedProfile(line, spec, AiPilot.LimitFor((float)g.Lineup[0].Stats.Pace)).LapTime;
        }
        return laps;
    }
}
