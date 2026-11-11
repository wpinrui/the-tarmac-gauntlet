#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class NewspaperTests
    {
        private static Func<double> Seeded(int seed)
        {
            var rng = new Random(seed);
            return rng.NextDouble;
        }

        /// <summary>A new career with the player in a class F car with one hired driver.</summary>
        private static GameState Career(string model = "f-01")
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions
            {
                PlayerName = "Ann Owner",
                TeamName = "Us",
                Skills = new PlayerSkills { Driver = 10, Engineer = 5 },
            }, Seeded(1));
            game = GameActions.BuyCar(game, new CarInstance { Id = "mine", ModelId = model, Condition = 100 }, 0, "car");
            Driver free = game.Drivers.First(d => !game.Contracts.Any(c => c.DriverId == d.Id));
            return GameActions.HireDriver(game, free.Id, free.Name, 1, 0);
        }

        /// <summary>
        /// Runs a race of <paramref name="laps"/> leader laps. Cars run in grid order, except that <paramref name="pace"/>
        /// can hand a car fewer laps, and <paramref name="retire"/> takes cars out at a lap.
        /// </summary>
        private static RaceDay Race(GameState game, int laps = 20, Func<CarRaceLog, int>? pace = null, Func<CarRaceLog, int?>? retire = null)
        {
            var race = new RaceDay(RaceWeekend.BuildSetup(game), () => 0.999);
            for (int lap = 1; lap <= laps; lap++)
                foreach (CarRaceLog c in race.Cars)
                {
                    int carLaps = pace?.Invoke(c) ?? laps;
                    if (lap > carLaps) continue;
                    if (retire?.Invoke(c) == lap)
                    {
                        race.Cars[c.Index].Retire(lap * 100 + c.Index, FailureType.Mechanical);
                        continue;
                    }
                    race.CompleteLap(c.Index, lap * 100 + c.Index, 100 + c.Index * 0.01, InstructionMode.Normal, lap == carLaps);
                }
            return race;
        }

        [Test]
        public void SettlingPrintsThePaper()
        {
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(Race(Career()));
            Newspaper paper = after.Newspaper!;
            Assert.That(paper.Year, Is.EqualTo(1));
            Assert.That(paper.Lead.TeamId, Is.EqualTo(summary.Classification[0].TeamId));
            Assert.That(paper.Lead.Headline, Does.Contain(summary.Classification[0].TeamName));
            Assert.That(paper.Lead.Paragraphs, Is.Not.Empty);
        }

        [Test]
        public void TheSameRacePrintsTheSamePaper()
        {
            GameState game = Career();
            (GameState a, _) = RaceWeekend.Settle(Race(game));
            (GameState b, _) = RaceWeekend.Settle(Race(game));
            Assert.That(b.Newspaper!.Lead.Headline, Is.EqualTo(a.Newspaper!.Lead.Headline));
            Assert.That(b.Newspaper.Feature?.Headline, Is.EqualTo(a.Newspaper.Feature?.Headline));
            Assert.That(b.Newspaper.PlayerStory?.Paragraphs, Is.EqualTo(a.Newspaper.PlayerStory?.Paragraphs));
        }

        [Test]
        public void PodiumsAreTheTopThreeFinishersOfEachClassQuickestClassFirst()
        {
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(Race(Career(), retire: c => c.Index == 0 ? 5 : null));
            Newspaper paper = after.Newspaper!;
            Assert.That(paper.Podiums.Select(p => p.CarClass), Is.Ordered.Using<CarClass>((x, y) =>
                NewspaperWriter.ClassOrder.ToList().IndexOf(x).CompareTo(NewspaperWriter.ClassOrder.ToList().IndexOf(y))));
            foreach (ClassPodium podium in paper.Podiums)
            {
                var expected = summary.Classification.Where(c => c.CarClass == podium.CarClass && !c.Retired).Take(3).Select(c => c.TeamId);
                Assert.That(podium.Places.Select(p => p.TeamId), Is.EqualTo(expected));
            }
        }

        [Test]
        public void CoverageGrowsWithPrestige()
        {
            Assert.That(NewspaperWriter.CoverageFor(0), Is.EqualTo(PlayerCoverage.Footnote));
            Assert.That(NewspaperWriter.CoverageFor(NewspaperWriter.ColumnPrestige), Is.EqualTo(PlayerCoverage.Column));
            Assert.That(NewspaperWriter.CoverageFor(NewspaperWriter.FrontPrestige), Is.EqualTo(PlayerCoverage.Front));
        }

        [Test]
        public void ALowPrestigeTeamGetsAOneLineFootnote()
        {
            GameState game = Career();
            RaceDay race = Race(game);
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(race);
            Newspaper paper = NewspaperWriter.Write(game, after, after.RaceHistory.Last(), summary with { PrestigeAfter = 5 });
            // The player starts last on 0 prestige and finishes there, so there's no strong result to lift it.
            Assert.That(paper.Coverage, Is.EqualTo(PlayerCoverage.Footnote));
            Assert.That(paper.PlayerStory!.Headline, Is.Empty);
            Assert.That(paper.PlayerStory.Paragraphs, Has.Count.EqualTo(1));
            Assert.That(paper.PlayerStory.Paragraphs[0], Does.StartWith("Us "));
        }

        [Test]
        public void AFrontPageStoryHasAHeadlineAndStandfirst()
        {
            GameState game = Career();
            (GameState after, RaceSummary summary) = RaceWeekend.Settle(Race(game));
            Newspaper paper = NewspaperWriter.Write(game, after, after.RaceHistory.Last(), summary with { PrestigeAfter = 90 });
            Assert.That(paper.Coverage, Is.EqualTo(PlayerCoverage.Front));
            Assert.That(paper.PlayerStory!.Headline, Does.StartWith("Us "));
            Assert.That(paper.PlayerStory.Standfirst, Is.Not.Empty);
            Assert.That(paper.PlayerStory.Paragraphs.Count, Is.GreaterThan(1));
        }

        [Test]
        public void WhenThePlayerWinsTheLeadIsTheirs()
        {
            GameState game = Career();
            // Everyone else does one lap fewer.
            RaceDay race = Race(game, pace: c => c.Car.Player ? 20 : 19);
            (GameState after, _) = RaceWeekend.Settle(race);
            Newspaper paper = after.Newspaper!;
            Assert.That(paper.Coverage, Is.EqualTo(PlayerCoverage.Lead));
            Assert.That(paper.Lead.TeamId, Is.EqualTo(GameActions.PlayerTeamId));
            Assert.That(paper.PlayerStory, Is.Null);
        }

        [Test]
        public void TheFeatureIsNeverAboutTheWinnerOrThePlayer()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                GameState game = Career();
                var rng = new Random(seed);
                var laps = Enumerable.Range(0, 100).Select(_ => 10 + rng.Next(11)).ToArray();
                var outs = Enumerable.Range(0, 100).Select(_ => rng.Next(4) == 0 ? 4 + rng.Next(6) : (int?)null).ToArray();
                (GameState after, RaceSummary summary) = RaceWeekend.Settle(Race(game, pace: c => laps[c.Index], retire: c => outs[c.Index]));
                Newspaper paper = after.Newspaper!;
                if (paper.Feature == null) continue;
                Assert.That(paper.Feature.TeamId, Is.Not.EqualTo(summary.Classification[0].TeamId));
                Assert.That(paper.Feature.TeamId, Is.Not.EqualTo(GameActions.PlayerTeamId));
                Assert.That(paper.FeatureKind, Is.Not.Null);
            }
        }

        [Test]
        public void WordsReadNaturally()
        {
            Assert.That(NewspaperWriter.Ordinal(1), Is.EqualTo("1st"));
            Assert.That(NewspaperWriter.Ordinal(12), Is.EqualTo("12th"));
            Assert.That(NewspaperWriter.Ordinal(23), Is.EqualTo("23rd"));
            Assert.That(NewspaperWriter.Nth(3), Is.EqualTo("third"));
            Assert.That(NewspaperWriter.Count(1, "lap"), Is.EqualTo("one lap"));
            Assert.That(NewspaperWriter.Count(14, "lap"), Is.EqualTo("14 laps"));
            Assert.That(NewspaperWriter.List(new[] { "A", "B", "C" }), Is.EqualTo("A, B and C"));
            Assert.That(NewspaperWriter.LapTimeText(208.3314), Is.EqualTo("3:28.331"));
        }
    }
}
