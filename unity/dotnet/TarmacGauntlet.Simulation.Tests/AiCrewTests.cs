#nullable enable
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class AiCrewTests
    {
        private static AITeam Ai(string id, double prestige, int crew = 0) => new AITeam
        {
            Id = id, Name = id, Prestige = prestige, PrestigeHistory = new[] { prestige }, CrewSize = crew,
        };

        [Test]
        public void ForPercentile_RunsFrom4At0To16At1()
        {
            Assert.That(AiCrew.ForPercentile(0), Is.EqualTo(4));
            Assert.That(AiCrew.ForPercentile(0.5), Is.EqualTo(10));
            Assert.That(AiCrew.ForPercentile(1), Is.EqualTo(16));
        }

        [Test]
        public void ForPercentile_RoundsToWholePeople()
        {
            Assert.That(AiCrew.ForPercentile(0.125), Is.EqualTo(6)); // 5.5 rounds up
            Assert.That(AiCrew.ForPercentile(0.1), Is.EqualTo(5));   // 5.2
        }

        [Test]
        public void Assign_LowestGets4HighestGets16()
        {
            var teams = Enumerable.Range(0, 99).Select(i => (BaseTeam)Ai($"t{i}", i * 0.9)).ToArray();
            var crews = AiCrew.Assign(teams).ToDictionary(t => t.Id, t => t.CrewSize);
            Assert.That(crews["t0"], Is.EqualTo(4));
            Assert.That(crews["t98"], Is.EqualTo(16));
            Assert.That(crews["t49"], Is.EqualTo(10));
            Assert.That(crews.Values, Is.All.InRange(4, 16));
        }

        [Test]
        public void Assign_FollowsPrestigeOrderNotListOrder()
        {
            var teams = new BaseTeam[] { Ai("high", 90), Ai("low", 10), Ai("mid", 50) };
            var crews = AiCrew.Assign(teams).ToDictionary(t => t.Id, t => t.CrewSize);
            Assert.That(crews["low"], Is.EqualTo(4));
            Assert.That(crews["mid"], Is.EqualTo(10));
            Assert.That(crews["high"], Is.EqualTo(16));
        }

        [Test]
        public void Assign_EqualPrestigeGetsEqualCrew()
        {
            var teams = new BaseTeam[] { Ai("a", 20), Ai("b", 20), Ai("c", 20) };
            Assert.That(AiCrew.Assign(teams).Select(t => t.CrewSize), Is.All.EqualTo(10));
        }

        [Test]
        public void Assign_OverwritesAiCrewAndLeavesThePlayerAlone()
        {
            var player = YearAdvanceTests.MakePlayerTeam("player") with { CrewSize = 0, Prestige = 100 };
            var teams = new BaseTeam[] { player, Ai("a", 10, crew: 12), Ai("b", 30, crew: 1) };
            var result = AiCrew.Assign(teams);
            Assert.That(result.Select(t => t.Id), Is.EqualTo(new[] { "player", "a", "b" }));
            Assert.That(result[0], Is.SameAs(player));
            Assert.That(result[1].CrewSize, Is.EqualTo(4));
            Assert.That(result[2].CrewSize, Is.EqualTo(16));
        }

        [Test]
        public void Assign_LoneAiTeamCountsAsTheTop()
        {
            Assert.That(AiCrew.Assign(new BaseTeam[] { Ai("a", 5) })[0].CrewSize, Is.EqualTo(16));
        }
    }
}
