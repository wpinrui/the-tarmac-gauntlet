// Runs whole careers of 24-hour races outside Unity, the way the driven race runs a car far from the camera: the
// mechanical failure roll every few seconds on track (RaceDay.Drive), hits by chance (CarDamage.RollHit), a lap logged
// at the line, and a stop every stint where the crew repair what is worth repairing. Each year the race is settled
// into the career and the career moves on a year, so the field ages and wears as it does in a real game.
// Reports how much of the field finishes and why the rest retire, and fails outside the plan's numbers
// (/mnt/project-files/mechanical-failure/plan.md): about 65% finish, retirements split about evenly between
// mechanical failures and crashes, and worn, old cars and Push well below the average.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;

static class Check
{
    /// <summary>Driven seconds in a 24-hour race (RaceTower.RaceLength over RaceTower.TimeCompression).</summary>
    const double RaceSeconds = 24 * 3600 / 10.0;
    /// <summary>Seconds of driving between rolls. The rolls compound over it, so the size only sets the grain.</summary>
    const double Tick = 2;
    /// <summary>A lap of the circuit, s, and laps in a stint.</summary>
    const double LapSeconds = 120;
    const int StintLaps = 20;

    const int Careers = 12, Years = 8;

    sealed class Tally
    {
        public int Cars, Mechanical, SingleHit, Accumulated;
        public int Out => Mechanical + SingleHit + Accumulated;
        public double Finish => Cars == 0 ? 0 : 1 - (double)Out / Cars;
        public void Add(CarRaceLog c)
        {
            Cars++;
            if (!c.Retired) return;
            if (c.RetirementReason == FailureType.Mechanical) Mechanical++;
            else if (c.Totalled) SingleHit++;
            else Accumulated++;
        }
        public override string ToString() => Cars == 0 ? "no cars" :
            $"{Finish:P1} finish of {Cars,5} | out: mechanical {Pct(Mechanical)}, crash {Pct(SingleHit + Accumulated)} " +
            $"(one hit {Pct(SingleHit)}, damage limit {Pct(Accumulated)})";
        string Pct(int n) => ((double)n / Cars).ToString("P1").PadLeft(6);
    }

    static int failures;

    static void Expect(bool ok, string name, string detail)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + name + ": " + detail);
        if (!ok) failures++;
    }

    static int Main()
    {
        var all = new Tally();
        var push = new Tally();
        var byYear = Enumerable.Range(0, Years).Select(_ => new Tally()).ToArray();
        var byAge = new SortedDictionary<string, Tally>();
        var worn = new Tally();
        var fresh = new Tally();
        for (int career = 0; career < Careers; career++)
        {
            var random = new Random(1000 + career);
            GameState game = Career(random);
            for (int year = 0; year < Years; year++)
            {
                RaceSetup setup = RaceWeekend.Begin(game);
                RaceDay day = Race(setup, InstructionMode.Normal, new Random(random.Next()));
                foreach (CarRaceLog c in day.Cars)
                {
                    all.Add(c);
                    byYear[year].Add(c);
                    int age = c.Car.Car.Age;
                    string band = age <= 2 ? " 0-2 years" : age <= 5 ? " 3-5 years" : age <= 9 ? " 6-9 years" : "10+ years";
                    if (!byAge.TryGetValue(band, out Tally t)) byAge[band] = t = new Tally();
                    t.Add(c);
                    if (c.Car.Car.Condition < 70 || age >= 8) worn.Add(c);
                    if (c.Car.Car.Condition >= 90 && age <= 2) fresh.Add(c);
                }
                // The same field again with every car on Push.
                foreach (CarRaceLog c in Race(setup, InstructionMode.Push, new Random(random.Next())).Cars) push.Add(c);

                RaceWeekend.Finish(day);
                game = Season.AdvanceYear(RaceWeekend.Settled, random.NextDouble);
            }
        }

        Console.WriteLine($"{Careers} careers of {Years} years, every car driven as if far from the camera");
        Console.WriteLine($"all          {all}");
        for (int y = 0; y < Years; y++) Console.WriteLine($"year {y + 1}       {byYear[y]}");
        foreach (var kv in byAge) Console.WriteLine($"{kv.Key}    {kv.Value}");
        Console.WriteLine($"fresh cars   {fresh}   (90%+ condition, 2 years old or less)");
        Console.WriteLine($"worn cars    {worn}   (under 70% condition, or 8 years old or more)");
        Console.WriteLine($"all on Push  {push}");
        Console.WriteLine();

        Expect(Math.Abs(all.Finish - 0.65) <= 0.04, "about 65% of the field finishes", all.Finish.ToString("P1"));
        double mechShare = all.Out == 0 ? 0 : (double)all.Mechanical / all.Out;
        Expect(mechShare >= 0.4 && mechShare <= 0.6, "retirements split about evenly", $"{mechShare:P0} mechanical, {1 - mechShare:P0} crash");
        Expect(worn.Cars == 0 || worn.Finish < all.Finish - 0.05, "worn and old cars finish well below the average", worn.Finish.ToString("P1"));
        Expect(push.Finish < all.Finish - 0.05, "Push finishes well below the average", push.Finish.ToString("P1"));
        Console.WriteLine(failures == 0 ? "all checks pass" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static GameState Career(Random random)
    {
        GameState game = GameInit.InitializeGame(new NewGameOptions { TeamName = "Your Team", Skills = new PlayerSkills { Driver = 16 } }, random.NextDouble);
        game = GameActions.BuyCar(game, new CarInstance { Id = "test-car", ModelId = "d-13", Condition = 100 }, 0, "");
        game = GameActions.BuyTyres(game, 12, 0);
        game = GameActions.BuySpares(game, 12, 0);
        return GameActions.SetCrewSize(game, 6);
    }

    /// <summary>One 24-hour race, every car in <paramref name="mode"/>, to the flag.</summary>
    static RaceDay Race(RaceSetup setup, InstructionMode mode, Random random)
    {
        var day = new RaceDay(setup, random.NextDouble);
        int n = day.Cars.Count;
        var onLap = new double[n];
        // Cars start spread through the lap, so the line isn't crossed by everyone at once.
        for (int i = 0; i < n; i++) onLap[i] = random.NextDouble() * LapSeconds;
        for (double t = 0; !day.AllFlagged; t += Tick)
        {
            bool timeUp = t >= RaceSeconds;
            for (int i = 0; i < n; i++)
            {
                CarRaceLog c = day.Cars[i];
                if (c.Retired || c.Flagged) continue;
                if (day.Drive(i, t, Tick, mode)) continue;
                if (CarDamage.RollHit(Tick, random.NextDouble) is double impact && c.Hit(t, impact, random.NextDouble())) continue;
                onLap[i] += Tick;
                if (onLap[i] < LapSeconds) continue;
                onLap[i] -= LapSeconds;
                day.CompleteLap(i, t, LapSeconds, mode, timeUp);
                if (c.Retired || c.Flagged || c.Laps % StintLaps != 0) continue;
                var stint = new StintOutlook
                {
                    Laps = StintLaps,
                    LapSeconds = LapSeconds,
                    SecondsLeft = Math.Max(0, RaceSeconds - t),
                    CrewMultiplier = PitStop.CrewMultiplier(c.Car.CrewSize, c.Car.EngineerSkill),
                };
                c.Pit(true, null, CarDamage.Target(RepairMode.Crew, c.Damage, stint));
            }
            if (t > RaceSeconds + 10 * LapSeconds) break;
        }
        return day;
    }
}
