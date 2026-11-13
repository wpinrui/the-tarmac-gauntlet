#nullable enable
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class StintEstimateTests
    {
        [Test]
        public void KnowsNothingBeforeAFullLap()
        {
            var s = new StintEstimate();
            s.Line(80, 0, clean: true);
            Assert.That(s.FuelPerLap, Is.Null);
            Assert.That(s.WearPerLap, Is.Null);
        }

        [Test]
        public void AveragesTheStintsCleanLaps()
        {
            var s = new StintEstimate();
            s.Line(80, 0.00, true);
            s.Line(77, 0.02, true);
            s.Line(73, 0.05, true);
            Assert.That(s.FuelPerLap, Is.EqualTo(3.5).Within(1e-9));
            Assert.That(s.WearPerLap, Is.EqualTo(0.025).Within(1e-9));
            Assert.That(s.Laps, Is.EqualTo(2));
        }

        [Test]
        public void SkipsALapInThePitLane()
        {
            var s = new StintEstimate();
            s.Line(80, 0.00, true);
            s.Line(77, 0.02, true);
            s.Line(76, 0.03, false);
            s.Line(73, 0.05, true);
            Assert.That(s.FuelPerLap, Is.EqualTo(3).Within(1e-9));
            Assert.That(s.WearPerLap, Is.EqualTo(0.02).Within(1e-9));
        }

        [Test]
        public void RefuellingKeepsTheFuelAverage()
        {
            var s = new StintEstimate();
            s.Line(20, 0.1, true);
            s.Line(16, 0.12, true);
            s.Line(80, 0.13, false);
            s.Line(74, 0.15, true);
            Assert.That(s.FuelPerLap, Is.EqualTo(5).Within(1e-9));
        }

        [Test]
        public void ANewDriverStartsAgain()
        {
            var s = new StintEstimate();
            s.Line(80, 0, true);
            s.Line(77, 0.02, true);
            s.NewStint();
            Assert.That(s.FuelPerLap, Is.Null);
            Assert.That(s.WearPerLap, Is.Null);
            s.Line(60, 0.02, false);
            s.Line(55, 0.05, true);
            Assert.That(s.FuelPerLap, Is.EqualTo(5).Within(1e-9));
        }

        [Test]
        public void NewTyresTurnTheAverageIntoOneLap()
        {
            var s = new StintEstimate();
            s.Line(80, 0.00, true);
            s.Line(77, 0.02, true);
            s.Line(74, 0.06, true);
            s.Line(71, 0.12, true);   // three laps averaging 0.04
            s.NewTyres();
            Assert.That(s.WearPerLap, Is.EqualTo(0.04).Within(1e-9));
            s.Line(80, 0.00, false);
            s.Line(77, 0.02, true);   // one more lap at 0.02 counts the same as the old average
            Assert.That(s.WearPerLap, Is.EqualTo(0.03).Within(1e-9));
            Assert.That(s.FuelPerLap, Is.EqualTo(3).Within(1e-9));
        }

        [Test]
        public void NewTyresBeforeAnyLapKeepTheCrewsEstimate()
        {
            var s = new StintEstimate();
            s.NewTyres();
            Assert.That(s.WearPerLap, Is.Null);
        }
    }
}
