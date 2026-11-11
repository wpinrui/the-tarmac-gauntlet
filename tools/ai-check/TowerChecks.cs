// Checks the timing tower's running order (Standings.cs): fake laps, gaps, class positions and which rows show.
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;
using TarmacGauntlet.Simulation;

static partial class Check
{
    static void TowerChecks()
    {
        const float lap = 4000f;
        var problems = new List<string>();

        if (Standings.FakeLaps(-50f, lap) != 0) problems.Add("a car behind the line should have 0 fake laps");
        if (Standings.FakeLaps(399f, lap) != 0 || Standings.FakeLaps(400f, lap) != 1) problems.Add("a fake lap should be a tenth of the lap");
        if (Standings.FakeLaps(lap * 3f + 10f, lap) != 30) problems.Add("three laps should be 30 fake laps");

        // 12 A cars, 20 F cars, the player in F at the back of the A pack's gap.
        var cars = new List<Standings.Car>();
        int id = 0;
        for (int i = 0; i < 12; i++) cars.Add(new Standings.Car { Id = id++, Team = $"A{i}", Class = CarClass.A, Distance = 60100f - i * 30f });
        for (int i = 0; i < 6; i++) cars.Add(new Standings.Car { Id = id++, Team = $"B{i}", Class = CarClass.B, Distance = 50000f - i * 300f });
        for (int i = 0; i < 20; i++) cars.Add(new Standings.Car { Id = id++, Team = $"F{i}", Class = CarClass.F, Distance = 30000f - i * 100f, Player = i == 10 });
        cars.Reverse();
        Standings.Rank(cars, lap);

        if (cars[0].Team != "A0" || cars[0].Position != 1) problems.Add("the furthest car should lead");
        if (cars[0].FakeLaps != 150) problems.Add($"the leader should show 150 fake laps, not {cars[0].FakeLaps}");
        Standings.Car b0 = cars.First(c => c.Team == "B0");
        if (b0.ClassPosition != 1 || b0.Position != 13) problems.Add($"B0 should be 13th and first in class, not {b0.Position} and {b0.ClassPosition}");
        if (Standings.FastestClass(cars) != CarClass.A) problems.Add("A should be the fastest class");
        var withF1 = new List<Standings.Car>(cars) { new Standings.Car { Id = 99, Class = CarClass.F1 } };
        if (Standings.FastestClass(withF1) != CarClass.A) problems.Add("the F1 car should not count as the fastest class");

        var rows = new List<Standings.Row>();
        Standings.Rows(cars, 20, rows);
        Standings.Car player = cars.First(c => c.Player);
        var shown = rows.Select(r => r.Car).ToList();
        for (int p = player.Position - Standings.AroundPlayer; p <= player.Position + Standings.AroundPlayer; p++)
            if (!shown.Any(c => c.Position == p)) problems.Add($"P{p} near the player should show");
        foreach (Standings.Car c in cars.Where(c => c.ClassPosition == 1))
            if (!shown.Contains(c)) problems.Add($"class leader {c.Team} should show");
        if (rows.Count != 20) problems.Add($"20 rows should fill, not {rows.Count}");
        if (shown.Count(c => c.Class == CarClass.A) != 9) problems.Add($"the 9 rows left should be A cars, not {shown.Count(c => c.Class == CarClass.A)}");
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Car.Position <= rows[i - 1].Car.Position) problems.Add("rows should be in race order");
        if (rows.Count(r => r.BreakBefore) != 1 || !rows.First(r => r.BreakBefore).Car.Equals(cars[player.Position - 1 - Standings.AroundPlayer]))
            problems.Add("one break should open the player's group");

        Standings.Rows(cars, 60, rows);
        if (rows.Count(r => r.Car.Class == CarClass.A) != 12 || rows.Count != 12 + 1 + 1 + 9)
            problems.Add($"with room to spare, every A car and no more should show, not {rows.Count} rows");

        Report("timing tower", problems);
    }
}
