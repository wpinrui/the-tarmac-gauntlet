#nullable enable
using System;

namespace TarmacGauntlet.Simulation
{
    /// <summary>JS Math functions whose C# counterparts behave differently.</summary>
    public static class JsMath
    {
        /// <summary>
        /// JS <c>Math.round</c>: halves round toward +infinity (2.5 gives 3, -2.5 gives -2).
        /// C# <c>Math.Round</c> uses banker's rounding by default, so do not use it for ported code.
        /// </summary>
        public static double Round(double x)
        {
            double r = Math.Floor(x);
            if (x - r >= 0.5) r += 1;
            // JS keeps the sign of zero: Math.round(-0.4) is -0.
            if (r == 0 && (x < 0 || (x == 0 && double.IsNegative(x)))) return -0.0;
            return r;
        }
    }
}
