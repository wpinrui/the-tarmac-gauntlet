// Checks how the race ends (RaceEnd) outside Unity, on a real RaceDay run lap by lap: for each way your race can end and
// each view you can be in, live timing shuts once your race is over, the cooldown lap ends, and the camera follows
// the right car.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static class Check
{
    static int failures;

    static void Expect(bool ok, string name, string detail = "")
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name + (detail.Length > 0 ? ": " + detail : ""));
        if (!ok) failures++;
    }

    /// <summary>How your race ends.</summary>
    enum Ending { YouTakeFlag, TeammateTakesFlag, RetiredMidRace, RetiredOnLastLap }

    /// <summary>
    /// The view you are in as the race ends. RaceEnd reads none of it, and that is the point: each view must end the
    /// same. Live timing is open whenever it is allowed.
    /// </summary>
    enum View { Driving, Broadcast, LiveTiming }

    /// <summary>Race length, s, and each car's lap, s: car i laps a little slower than car i - 1.</summary>
    const float RaceLength = 900f, BaseLap = 100f, Spread = 0.7f, Step = 0.25f;
    /// <summary>A cooldown lap at 60% of racing speed, then into the box, s.</summary>
    static float Cooldown(float lap) => lap / 0.6f + 20f;
    /// <summary>The crew wave the car home this long once it is parked, s (PitCrews.WelcomeTime).</summary>
    const float Welcome = 6f;

    static int Main()
    {
        foreach (Ending e in Enum.GetValues(typeof(Ending)))
            foreach (View v in Enum.GetValues(typeof(View)))
                Run(e, v);
        NoCarOfYours();
        Console.WriteLine(failures == 0 ? "all checks pass" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static GameState Career(out string teammate)
    {
        GameState game = GameInit.InitializeGame(new NewGameOptions { TeamName = "Your Team", Skills = new PlayerSkills { Driver = 16 } });
        game = GameActions.BuyCar(game, new CarInstance { Id = "test-car", ModelId = "a-29", Condition = 100 }, 0, "");
        game = GameActions.BuyTyres(game, 12, 0);
        game = GameActions.BuySpares(game, 12, 0);
        game = GameActions.SetCrewSize(game, 8);
        teammate = DebugActions.FreeAgents(game).First().Driver.Id;
        return DebugActions.Hire(game, teammate);
    }

    static void Run(Ending ending, View view)
    {
        string name = ending + ", " + view;
        GameState game = Career(out string teammate);
        // A teammate at the wheel to the flag, else you.
        string[] lineup = ending == Ending.TeammateTakesFlag
            ? new[] { teammate, RaceEntry.PlayerCharacterDriverId }
            : new[] { RaceEntry.PlayerCharacterDriverId, teammate };
        RaceSetup setup = RaceWeekend.Begin(game, lineup);
        var day = new RaceDay(setup, () => 0.999);
        CarRaceLog mine = RaceEnd.Mine(day);
        Expect(mine != null, name + ": your car is in the race");
        if (mine == null) return;
        bool retires = ending == Ending.RetiredMidRace || ending == Ending.RetiredOnLastLap;
        if (ending == Ending.TeammateTakesFlag)
            Expect(mine.Driver.Id != RaceEntry.PlayerCharacterDriverId, name + ": a teammate drives your car");

        int n = day.Cars.Count;
        float[] lap = new float[n], next = new float[n], parkedAt = new float[n];
        for (int i = 0; i < n; i++)
        {
            lap[i] = BaseLap + Spread * i;
            next[i] = lap[i];
            parkedAt[i] = float.MaxValue;
        }
        float retireAt = ending == Ending.RetiredMidRace ? RaceLength * 0.5f
            : ending == Ending.RetiredOnLastLap ? RaceLength + lap[mine.Index] * 0.5f : float.MaxValue;

        float t = 0f, fieldFlaggedAt = -1f, lastFlag = -1f, overAt = -1f;
        bool timingWrongMidRace = false, timingOpenAfterEnd = false, shownWrong = false;
        string shownDetail = "";
        bool timingOpen = false;
        while (t < RaceLength + 3000f)
        {
            t += Step;
            bool timeUp = t >= RaceLength;
            float? since = timeUp ? t - RaceLength : (float?)null;
            if (t >= retireAt && !mine.Retired && !mine.Flagged) mine.Retire(t, FailureType.Mechanical);
            for (int i = 0; i < n; i++)
            {
                CarRaceLog log = day.Cars[i];
                if (log.Retired || log.Flagged || t < next[i]) continue;
                day.CompleteLap(i, t, lap[i], InstructionMode.Normal, timeUp);
                next[i] += lap[i];
                if (log.Flagged)
                {
                    parkedAt[i] = t + Cooldown(lap[i]);
                    lastFlag = t;
                }
            }
            bool field = RaceEnd.FieldFlagged(day, since);
            if (field && fieldFlaggedAt < 0f) fieldFlaggedAt = t;

            // Live timing: open whenever allowed in that view, and it must be allowed all race until your race is over.
            bool allowed = RaceEnd.TimingAllowed(day, since);
            bool mineOver = timeUp && (mine.Flagged || mine.Retired);
            if (!mineOver && !allowed) timingWrongMidRace = true;
            timingOpen = view == View.LiveTiming && allowed;
            if (mineOver && timingOpen) timingOpenAfterEnd = true;

            // The camera after the time is up: your car while it runs, else a running car at the front.
            int show = RaceEnd.ShowAfterFlag(day, since);
            if (timeUp)
            {
                int want = !mine.Retired ? mine.Index : day.Classify().First(c => !c.Retired).Index;
                if (show != want && !shownWrong)
                {
                    shownWrong = true;
                    shownDetail = "showed car " + show + ", wanted " + want + " at " + t.ToString("0.0") + " s";
                }
            }

            bool received = !mine.Retired && t >= parkedAt[mine.Index] + Welcome;
            if (RaceEnd.CooldownOver(day, since, received, field ? t - fieldFlaggedAt : (float?)null))
            {
                overAt = t;
                break;
            }
        }

        Expect(!timingWrongMidRace, name + ": live timing can open until your race is over");
        Expect(!timingOpenAfterEnd, name + ": live timing shuts once your race is over");
        Expect(!shownWrong, name + ": the camera follows your car, or the leader with yours out", shownDetail);
        Expect(overAt >= 0f, name + ": the cooldown lap ends", overAt >= 0f ? "at " + (overAt - RaceLength).ToString("0") + " s after time up" : "it never did");
        if (overAt < 0f) return;
        Expect(day.AllFlagged, name + ": every car running has taken the flag before the race is settled");
        float due = retires ? lastFlag + RaceEnd.RetiredHold : Math.Max(lastFlag, parkedAt[mine.Index] + Welcome);
        Expect(overAt <= due + Step * 2f, name + ": it ends as soon as it should",
            "ended " + (overAt - RaceLength).ToString("0.0") + " s after time up, due " + (due - RaceLength).ToString("0.0"));
        Expect(overAt - RaceLength < RaceDirector_FlagWaitPlusCooldown, name + ": well inside the race director's own limit");
    }

    /// <summary>RaceDirector.FlagWait + CooldownWait: the limit that settled a race nothing else ended.</summary>
    const float RaceDirector_FlagWaitPlusCooldown = RaceEnd.FlagWait + 700f;

    /// <summary>A race with no car of yours still ends, a moment after the last car takes the flag.</summary>
    static void NoCarOfYours()
    {
        GameState game = Career(out _);
        RaceSetup setup = RaceWeekend.Begin(game);
        var grid = setup.Grid.Where(g => !g.Player).ToArray();
        setup = setup with { Grid = grid };
        var day = new RaceDay(setup, () => 0.999);
        Expect(RaceEnd.Mine(day) == null, "no car of yours: none in the race");
        int n = day.Cars.Count;
        float t = 0f, fieldAt = -1f, overAt = -1f;
        var next = Enumerable.Range(0, n).Select(i => BaseLap + Spread * i).ToArray();
        while (t < RaceLength + 3000f)
        {
            t += Step;
            bool timeUp = t >= RaceLength;
            float? since = timeUp ? t - RaceLength : (float?)null;
            for (int i = 0; i < n; i++)
            {
                if (day.Cars[i].Flagged || t < next[i]) continue;
                day.CompleteLap(i, t, BaseLap + Spread * i, InstructionMode.Normal, timeUp);
                next[i] += BaseLap + Spread * i;
            }
            bool field = RaceEnd.FieldFlagged(day, since);
            if (field && fieldAt < 0f) fieldAt = t;
            if (RaceEnd.CooldownOver(day, since, false, field ? t - fieldAt : (float?)null)) { overAt = t; break; }
        }
        Expect(overAt >= 0f && day.AllFlagged, "no car of yours: the cooldown lap ends once every car has the flag");
    }
}
