#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Stand-in for TS <c>Math.random</c>, used when a caller passes no random source.
    /// Returns a double in [0, 1).
    /// </summary>
    public static class DefaultRandom
    {
        private static readonly Random Shared = new Random();

        public static readonly Func<double> Next = () => Shared.NextDouble();
    }
}
