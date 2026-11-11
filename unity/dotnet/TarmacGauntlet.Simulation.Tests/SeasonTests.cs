#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class SeasonTests
    {
        private static Func<double> Seeded(int seed)
        {
            var rng = new Random(seed);
            return rng.NextDouble;
        }

        private static GameState AfterRace()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { PlayerName = "Ann", TeamName = "Us" }, Seeded(1));
            return game with { Phase = GamePhase.PostRace };
        }

        [Test]
        public void TheYearAdvancesToTheNextPreRace()
        {
            GameState game = AfterRace();
            GameState next = Season.AdvanceYear(game, Seeded(2));
            Assert.That(next.CurrentYear, Is.EqualTo(2));
            Assert.That(next.Phase, Is.EqualTo(GamePhase.PreRace));
            Assert.That(next.Drivers, Has.Count.EqualTo(game.Drivers.Count));
            Assert.That(next.Drivers.Count(d => d.Id.StartsWith("rookie-y1-")), Is.EqualTo(DriverLifecycle.AnnualRetirements));
            Assert.That(next.Drivers.Select(d => d.Id), Is.Unique);
            Assert.That(next.CarMarket.UsedListings, Is.Not.EqualTo(game.CarMarket.UsedListings));
        }

        [Test]
        public void ItAdvancesOnlyOncePerRace()
        {
            GameState once = Season.AdvanceYear(AfterRace(), Seeded(2));
            Assert.That(Season.AdvanceYear(once, Seeded(3)), Is.SameAs(once));
        }

        [Test]
        public void ItDoesNothingBeforeTheRace()
        {
            GameState game = AfterRace() with { Phase = GamePhase.PreRace };
            Assert.That(Season.AdvanceYear(game, Seeded(2)), Is.SameAs(game));
        }

        [Test]
        public void RetiredDriversLoseTheirContracts()
        {
            GameState next = Season.AdvanceYear(AfterRace(), Seeded(2));
            var ids = next.Drivers.Select(d => d.Id).ToHashSet();
            Assert.That(next.Contracts.All(c => ids.Contains(c.DriverId)), Is.True);
        }

        [Test]
        public void RookiesGetNewIdsEachYear()
        {
            GameState two = Season.AdvanceYear(AfterRace(), Seeded(2));
            GameState three = Season.AdvanceYear(two with { Phase = GamePhase.PostRace }, Seeded(3));
            Assert.That(three.CurrentYear, Is.EqualTo(3));
            Assert.That(three.Drivers.Select(d => d.Id), Is.Unique);
            Assert.That(three.Drivers.Count(d => d.Id.StartsWith("rookie-y2-")), Is.EqualTo(DriverLifecycle.AnnualRetirements));
        }
    }
}
