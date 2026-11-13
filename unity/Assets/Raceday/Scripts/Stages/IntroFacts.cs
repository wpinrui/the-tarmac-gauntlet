using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The figures the race preview shows (<see cref="BroadcastIntro"/>), worked out from the career and the field, with no
    /// Unity objects so it can be checked outside Unity. The season's year, counted from <see cref="FirstYear"/>. Each
    /// class's expected lap time, the median of its cars' predicted laps. Last year's podium in each class and the
    /// overall winners of past years, from the race history. Each class's favourite: its car with the quickest
    /// predicted lap. Classes run fastest first.
    /// </summary>
    public sealed class IntroFacts
    {
        /// <summary>The calendar year of the career's first race.</summary>
        public const int FirstYear = 2026;
        /// <summary>The most past winners the preview lists.</summary>
        public const int WinnersShown = 5;

        public sealed class ClassLap
        {
            public CarClass Class;
            public float Seconds;
        }

        public sealed class Podium
        {
            public CarClass Class;
            /// <summary>Team names, winner first: up to three.</summary>
            public string[] Teams;
        }

        public sealed class Winner
        {
            public int Year;
            public string Team;
            public CarClass Class;
        }

        public sealed class Favourite
        {
            public CarClass Class;
            public string Team;
            public string Model;
        }

        /// <summary>The calendar year being raced.</summary>
        public int Year;
        public List<ClassLap> Laps = new List<ClassLap>();
        /// <summary>Last year's podiums, or empty in the first year.</summary>
        public List<Podium> Podiums = new List<Podium>();
        /// <summary>The year the podiums are from.</summary>
        public int PodiumYear;
        /// <summary>Past overall winners, latest first.</summary>
        public List<Winner> Winners = new List<Winner>();
        public List<Favourite> Favourites = new List<Favourite>();

        /// <summary>A career year as a calendar year: year 1 is <see cref="FirstYear"/>.</summary>
        public static int CalendarYear(int careerYear) => FirstYear - 1 + careerYear;

        /// <summary>The classes from fastest to slowest.</summary>
        private static int Rank(CarClass c) => c == CarClass.F1 ? -1 : -(int)c;

        /// <summary>
        /// The facts for a race. <paramref name="predicted"/> is each grid car's predicted lap, s, in grid order.
        /// </summary>
        public static IntroFacts From(RaceSetup setup, IReadOnlyList<float> predicted)
        {
            GameState game = setup.Game;
            var facts = new IntroFacts { Year = CalendarYear(game.CurrentYear) };
            var names = new Dictionary<string, string>();
            foreach (BaseTeam t in game.Teams) names[t.Id] = t.Name;

            var byClass = new Dictionary<CarClass, List<int>>();
            for (int i = 0; i < setup.Grid.Count; i++)
            {
                CarClass c = setup.Grid[i].Model.CarClass;
                if (!byClass.TryGetValue(c, out List<int> cars)) byClass[c] = cars = new List<int>();
                cars.Add(i);
            }
            foreach (CarClass c in byClass.Keys.OrderBy(Rank))
            {
                List<int> cars = byClass[c];
                float[] laps = cars.Select(i => i < predicted.Count ? predicted[i] : 0f).Where(s => s > 0f).OrderBy(s => s).ToArray();
                if (laps.Length > 0)
                    facts.Laps.Add(new ClassLap { Class = c, Seconds = Median(laps) });
                int best = -1;
                foreach (int i in cars)
                    if (i < predicted.Count && predicted[i] > 0f && (best < 0 || predicted[i] < predicted[best])) best = i;
                if (best >= 0)
                    facts.Favourites.Add(new Favourite { Class = c, Team = setup.Grid[best].TeamName, Model = setup.Grid[best].Model.Name });
            }

            RaceHistoryEntry last = game.RaceHistory.Where(r => r.Year < game.CurrentYear).OrderBy(r => r.Year).LastOrDefault();
            if (last != null)
            {
                facts.PodiumYear = CalendarYear(last.Year);
                foreach (IGrouping<CarClass, RaceResult> g in last.Results.Where(r => !r.Retired).GroupBy(r => r.CarClass).OrderBy(g => Rank(g.Key)))
                {
                    string[] teams = g.OrderBy(r => r.Position).Take(3).Select(r => Name(names, r.TeamId)).Where(n => n != null).ToArray();
                    if (teams.Length > 0) facts.Podiums.Add(new Podium { Class = g.Key, Teams = teams });
                }
            }
            foreach (RaceHistoryEntry race in game.RaceHistory.Where(r => r.Year < game.CurrentYear).OrderByDescending(r => r.Year).Take(WinnersShown))
            {
                RaceResult win = race.Results.Where(r => !r.Retired).OrderBy(r => r.Position).FirstOrDefault();
                string team = win != null ? Name(names, win.TeamId) : null;
                if (team != null) facts.Winners.Add(new Winner { Year = CalendarYear(race.Year), Team = team, Class = win.CarClass });
            }
            return facts;
        }

        private static string Name(Dictionary<string, string> names, string id) => names.TryGetValue(id, out string n) ? n : null;

        private static float Median(float[] sorted)
        {
            int n = sorted.Length;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
        }

        /// <summary>A lap time as minutes, seconds and tenths: 2:14.6.</summary>
        public static string LapText(float seconds)
        {
            int tenths = (int)Math.Round(seconds * 10.0);
            return $"{tenths / 600}:{tenths / 10 % 60:00}.{tenths % 10}";
        }
    }
}
