using System;
using System.Collections.Generic;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The running order for the timing tower, with no Unity objects so it can be tested outside Unity.
    /// Laps are counted in fake laps: a tenth of the real lap, because the race runs at 10:1. Every
    /// car shows the fake laps it has completed, from 0. The tower shows every class leader, the cars
    /// round the player and, in the rows left, the fastest class in race order. A car that has taken the
    /// chequered flag keeps the place it finished in (<see cref="Finish"/>) while it drives its cooldown lap.
    /// </summary>
    public static class Standings
    {
        /// <summary>A fake lap is this share of the real lap.</summary>
        public const float FakeLapShare = 0.1f;
        /// <summary>The tower shows this many cars ahead of the player and this many behind.</summary>
        public const int AroundPlayer = 4;

        public sealed class Car
        {
            public int Id;
            public string Team;
            public CarClass Class;
            public bool Player;
            /// <summary>Distance covered since the start, m. Set before each <see cref="Rank"/>.</summary>
            public float Distance;

            public int Position;
            public int ClassPosition;
            public int FakeLaps;
            /// <summary>True once the car has taken the chequered flag.</summary>
            public bool Finished;
            /// <summary>The order the car took the flag in, from 1, while <see cref="Finished"/>.</summary>
            public int FinishOrder;
        }

        /// <summary>
        /// The car takes the chequered flag with <paramref name="laps"/> laps done, the
        /// <paramref name="order"/>th across the line. From here it ranks on the line it crossed, so its
        /// cooldown lap moves it nowhere, and among cars that crossed the same line in the order they crossed it.
        /// </summary>
        public static void Finish(Car c, int laps, float lap, int order)
        {
            if (c.Finished) return;
            c.Finished = true;
            c.FinishOrder = order;
            c.Distance = laps * lap + LineMargin;
        }

        /// <summary>How far past the line a finished car is ranked, m: enough that its fake laps don't round down.</summary>
        private const float LineMargin = 1f;

        public struct Row
        {
            public Car Car;
            /// <summary>True when the row opens the player's group after a jump in positions.</summary>
            public bool BreakBefore;
        }

        /// <summary>Fake laps completed after this distance, from 0.</summary>
        public static int FakeLaps(float distance, float lap) =>
            distance <= 0f ? 0 : (int)Math.Floor(distance / (lap * FakeLapShare));

        /// <summary>
        /// Puts the cars in race order, furthest first, and fills in their positions and fake laps.
        /// Before the start (<paramref name="racing"/> false) the order is the grid's, by Id, with no laps:
        /// on the formation lap the cars run nose to tail and would swap places every frame.
        /// </summary>
        public static void Rank(List<Car> cars, float lap, bool racing = true)
        {
            if (racing) cars.Sort(Compare);
            else cars.Sort((a, b) => a.Id.CompareTo(b.Id));
            var inClass = new Dictionary<CarClass, int>();
            for (int i = 0; i < cars.Count; i++)
            {
                Car c = cars[i];
                c.Position = i + 1;
                inClass.TryGetValue(c.Class, out int n);
                c.ClassPosition = inClass[c.Class] = n + 1;
                c.FakeLaps = racing ? FakeLaps(c.Distance, lap) : 0;
            }
        }

        private static int Compare(Car a, Car b)
        {
            if (a.Distance != b.Distance) return b.Distance.CompareTo(a.Distance);
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished && a.FinishOrder != b.FinishOrder) return a.FinishOrder.CompareTo(b.FinishOrder);
            return a.Id.CompareTo(b.Id);
        }

        /// <summary>
        /// The quickest class in the field. The F1 car is the player's trophy and never fills the tower,
        /// so it counts only when nothing else is racing.
        /// </summary>
        public static CarClass FastestClass(List<Car> cars)
        {
            CarClass best = CarClass.F;
            bool any = false;
            foreach (Car c in cars)
            {
                if (c.Class == CarClass.F1) continue;
                if (!any || c.Class > best) best = c.Class;
                any = true;
            }
            return any ? best : CarClass.F1;
        }

        /// <summary>
        /// The tower's rows, in race order, into rows: every class leader and the cars within
        /// <see cref="AroundPlayer"/> of the player, then the fastest class until fit rows are full.
        /// </summary>
        public static void Rows(List<Car> ranked, int fit, List<Row> rows)
        {
            rows.Clear();
            if (ranked.Count == 0) return;
            var shown = new bool[ranked.Count];
            int count = 0;
            int player = ranked.FindIndex(c => c.Player);
            int first = -1;
            if (player >= 0)
            {
                first = Math.Max(0, player - AroundPlayer);
                int last = Math.Min(ranked.Count - 1, player + AroundPlayer);
                for (int i = first; i <= last; i++) { shown[i] = true; count++; }
            }
            for (int i = 0; i < ranked.Count; i++)
                if (ranked[i].ClassPosition == 1 && !shown[i]) { shown[i] = true; count++; }
            CarClass fastest = FastestClass(ranked);
            for (int i = 0; i < ranked.Count && count < fit; i++)
                if (ranked[i].Class == fastest && !shown[i]) { shown[i] = true; count++; }

            int previous = -1;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (!shown[i]) continue;
                rows.Add(new Row { Car = ranked[i], BreakBefore = i == first && previous >= 0 && previous != i - 1 });
                previous = i;
            }
        }
    }
}
