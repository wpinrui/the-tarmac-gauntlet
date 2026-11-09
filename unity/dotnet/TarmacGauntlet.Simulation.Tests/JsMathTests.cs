#nullable enable
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class JsMathTests
    {
        [TestCase(0.5, 1)]
        [TestCase(1.5, 2)]
        [TestCase(2.5, 3)]
        [TestCase(-0.5, 0)]
        [TestCase(-1.5, -1)]
        [TestCase(-2.5, -2)]
        [TestCase(2.4, 2)]
        [TestCase(-2.6, -3)]
        [TestCase(0.49999999999999994, 0)]
        public void RoundMatchesJs(double x, double expected)
        {
            Assert.That(JsMath.Round(x), Is.EqualTo(expected));
        }

        [Test]
        public void RoundKeepsNegativeZero()
        {
            Assert.That(double.IsNegative(JsMath.Round(-0.4)), Is.True);
            Assert.That(double.IsNegative(JsMath.Round(0.4)), Is.False);
        }

        [Test]
        public void RoundPassesThroughNonFinite()
        {
            Assert.That(JsMath.Round(double.PositiveInfinity), Is.EqualTo(double.PositiveInfinity));
            Assert.That(JsMath.Round(double.NegativeInfinity), Is.EqualTo(double.NegativeInfinity));
            Assert.That(double.IsNaN(JsMath.Round(double.NaN)), Is.True);
        }
    }
}
