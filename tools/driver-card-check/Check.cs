using System;
using System.Collections.Generic;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static class Program
{
    static int fails;
    static void Check(string name, bool ok, string detail = "") { Console.WriteLine((ok ? "ok   " : "FAIL ") + name + (ok ? "" : " " + detail)); if (!ok) fails++; }
    static int Main()
    {
        var game = new GameState
        {
            Drivers = new List<Driver> { new Driver { Id = "d1", Name = "Kenji Tanaka", Nationality = "jp" }, new Driver { Id = "d2", Name = "Pelé", Nationality = "br" } },
            Teams = new List<BaseTeam> { new PlayerTeam { PlayerName = "Ivan Wong", Nationality = "sg", Skills = new PlayerSkills { Driver = 12 } } },
        };
        var d1 = new RaceDriver { Id = "d1", Stats = new DriverStats { Pace = 84, Consistency = 76, Stamina = 71, Safety = 80, Smoothness = 79 } };
        var d2 = new RaceDriver { Id = "d2", Stats = new DriverStats { Pace = 50.5, Consistency = 50, Stamina = 50, Safety = 50, Smoothness = 50 } };
        var you = new RaceDriver { Id = RaceEntry.PlayerCharacterDriverId, Stats = new DriverStats { Pace = 60, Consistency = 60, Stamina = 60, Safety = 60, Smoothness = 60 } };

        var f = DriverFacts.Of(d1, game);
        Check("name", f.Initial == "K." && f.Surname == "TANAKA", f.Initial + f.Surname);
        Check("nation", f.Nation == "jp");
        Check("ovr", f.Ovr == 78, f.Ovr.ToString());
        Check("stats", string.Join(",", f.Stats) == "84,76,71,80,79");
        var p = DriverFacts.Of(d2, game);
        Check("one word name", p.Initial == "" && p.Surname == "PELÉ", p.Initial + "|" + p.Surname);
        Check("rounded stat", p.Stats[0] == 51 && p.Ovr == 50, p.Stats[0] + " " + p.Ovr);
        var y = DriverFacts.Of(you, game);
        Check("player", y.Initial == "I." && y.Surname == "WONG" && y.Nation == "sg" && y.Ovr == 60, y.Initial + y.Surname + y.Nation + y.Ovr);
        var u = DriverFacts.Of(new RaceDriver { Id = "nobody" }, game);
        Check("unknown driver", u.Nation == "" && u.Surname == "NOBODY");
        Check("no game", DriverFacts.Of(d1, null).Surname == "D1");

        var log = new CarRaceLog(new GridCar { Lineup = new[] { d1, you, d2 } }, 0);
        var w = new NextDriverWatch();
        w.Step(0, false, log);
        Check("on track: nobody", w.Next(0, log) == null);
        w.Step(0, true, log);
        Check("in pit lane: next", w.Next(0, log)?.Id == RaceEntry.PlayerCharacterDriverId);
        log.NextDriverIndex = 2;
        Check("follows the pick", w.Next(0, log)?.Id == "d2");
        log.NextDriverIndex = 0;
        Check("staying in: nobody", w.Next(0, log) == null);
        log.NextDriverIndex = 1;
        log.Pit(false);
        w.Step(0, true, log);
        Check("after the stop: nobody", w.Next(0, log) == null && log.Driver.Id == RaceEntry.PlayerCharacterDriverId);
        w.Step(0, false, log);
        w.Step(0, true, log);
        Check("next visit: next", w.Next(0, log)?.Id == "d2");
        Check("other car untracked", w.Next(1, log) == null);
        Console.WriteLine(fails == 0 ? "all passed" : fails + " failed");
        return fails == 0 ? 0 : 1;
    }
}
