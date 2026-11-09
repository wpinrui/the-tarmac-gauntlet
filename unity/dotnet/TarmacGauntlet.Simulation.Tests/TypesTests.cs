#nullable enable
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class TypesTests
    {
        [Test]
        public void WithCopiesLikeTsSpread()
        {
            var car = new CarInstance { Id = "c1", ModelId = "f-01", Age = 3, Condition = 80 };
            var older = car with { Age = car.Age + 1 };
            Assert.That(older.Age, Is.EqualTo(4));
            Assert.That(car.Age, Is.EqualTo(3));
            Assert.That(older.Id, Is.EqualTo("c1"));
        }

        [Test]
        public void CarStatsKeyedAccess()
        {
            var stats = new CarStats { Power = 50, PitStopTime = 10 };
            Assert.That(stats.Get(CarStatKey.Power), Is.EqualTo(50));
            var next = stats.With(CarStatKey.Power, 45);
            Assert.That(next.Power, Is.EqualTo(45));
            Assert.That(next.PitStopTime, Is.EqualTo(10));
            Assert.That(stats.Power, Is.EqualTo(50));
        }

        [Test]
        public void DriverStatsKeyedAccess()
        {
            var stats = new DriverStats { Pace = 70 };
            foreach (var key in DriverData.DriverStatKeys)
                Assert.That(stats.With(key, 1).Get(key), Is.EqualTo(1));
            Assert.That(stats.Get(DriverStatKey.Pace), Is.EqualTo(70));
        }

        [Test]
        public void TeamUnionUsesTypePatterns()
        {
            BaseTeam player = new PlayerTeam { Id = "p", PlayerName = "Ivan" };
            BaseTeam ai = new AITeam { Id = "a", SpareParts = 3 };
            Assert.That(player is PlayerTeam, Is.True);
            Assert.That(ai is AITeam { SpareParts: 3 }, Is.True);
        }

        [Test]
        public void SnapFixture()
        {
            var s = TestFixtures.Snap(12, 345.6);
            Assert.That(s.LapsCompleted, Is.EqualTo(12));
            Assert.That(s.TotalTime, Is.EqualTo(345.6));
            Assert.That(s.Condition, Is.EqualTo(100));
            Assert.That(s.CurrentDriverId, Is.EqualTo("d"));
            Assert.That(s.InstructionMode, Is.EqualTo(InstructionMode.Normal));
        }
    }
}
