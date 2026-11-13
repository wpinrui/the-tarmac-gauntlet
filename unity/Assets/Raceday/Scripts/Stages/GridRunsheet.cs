using System.Collections.Generic;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The running order of the grid presentation, with no Unity objects so it can be tested outside Unity.
    /// The front row first, pole then second, then the pole of each other class, quickest class first,
    /// then your car if it has not been on yet. Then the whole grid on the board, a page of
    /// <see cref="PageSize"/> at a time.
    /// </summary>
    public static class GridRunsheet
    {
        public enum Kind { Pole, FrontRow, ClassPole, Yours, Board }

        /// <summary>One shot of the presentation: a car, or a page of the board.</summary>
        public readonly struct Beat
        {
            public readonly Kind Kind;
            /// <summary>The car's place on the grid from 0, or the board's first place on the page.</summary>
            public readonly int Car;

            public Beat(Kind kind, int car) => (Kind, Car) = (kind, car);
        }

        /// <summary>Places on one page of the board: ten rows of two.</summary>
        public const int PageSize = 20;

        /// <summary>The beats for a grid of these classes, pole first, with your car at <paramref name="player"/> or -1 for none.</summary>
        public static List<Beat> For(IReadOnlyList<CarClass> grid, int player)
        {
            var beats = new List<Beat>();
            int n = grid.Count;
            if (n == 0) return beats;
            var shown = new HashSet<int>();
            void Car(Kind kind, int i)
            {
                if (!shown.Add(i)) return;
                beats.Add(new Beat(kind, i));
            }

            Car(Kind.Pole, 0);
            if (n > 1) Car(Kind.FrontRow, 1);
            for (int c = (int)CarClass.F1; c >= (int)CarClass.F; c--)
            {
                int first = FirstOf(grid, (CarClass)c);
                // A class led from the front row has had its pole on already.
                if (first > 1) Car(Kind.ClassPole, first);
            }
            if (player >= 0 && player < n) Car(Kind.Yours, player);
            for (int page = 0; page < n; page += PageSize) beats.Add(new Beat(Kind.Board, page));
            return beats;
        }

        /// <summary>The first place on the grid held by a car of this class, or -1.</summary>
        public static int FirstOf(IReadOnlyList<CarClass> grid, CarClass c)
        {
            for (int i = 0; i < grid.Count; i++) if (grid[i] == c) return i;
            return -1;
        }

        /// <summary>True if the car at place i is the first of its class on the grid.</summary>
        public static bool LeadsClass(IReadOnlyList<CarClass> grid, int i) => i >= 0 && i < grid.Count && FirstOf(grid, grid[i]) == i;
    }
}
