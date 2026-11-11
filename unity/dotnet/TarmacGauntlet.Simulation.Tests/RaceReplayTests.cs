#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class RaceReplayTests
    {
        /// <summary>A career whose player has a car and two drivers, so stops change drivers.</summary>
        private static GameState Career()
        {
            var rng = new Random(1);
            GameState game = GameInit.InitializeGame(new NewGameOptions
            {
                PlayerName = "Ann Owner",
                TeamName = "Us",
                Skills = new PlayerSkills { Driver = 10, Engineer = 5 },
            }, rng.NextDouble);
            game = GameActions.BuyCar(game, new CarInstance { Id = "mine", ModelId = "d-12", Condition = 90 }, 0, "car");
            Driver free = game.Drivers.First(d => !game.Contracts.Any(c => c.DriverId == d.Id));
            game = GameActions.HireDriver(game, free.Id, free.Name, 1, 0);
            return game with
            {
                Teams = game.Teams.Select(t => t is PlayerTeam p ? p with { TyreSets = 4, SpareParts = 6, CrewSize = 4, Budget = 100_000 } : t).ToArray(),
            };
        }

        /// <summary>60 laps at a risky pace, the player stopping every 15, then settled.</summary>
        private static (GameState After, RaceSummary Summary) Race(int seed)
        {
            var rng = new Random(seed);
            var race = new RaceDay(RaceWeekend.BuildSetup(Career()), rng.NextDouble);
            CarRaceLog me = race.Cars.First(c => c.Car.Player);
            for (int lap = 1; lap <= 60; lap++)
            {
                foreach (CarRaceLog c in race.Cars) race.CompleteLap(c.Index, lap * 100 + c.Index, 100, InstructionMode.Push, lap == 60);
                if (lap % 15 == 0 && !me.Retired)
                {
                    me.Pit(lap % 30 == 0);
                    me.PitDone(30, 40);
                }
            }
            return RaceWeekend.Settle(race);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void HistoryGivesBackTheSummary(int seed)
        {
            (GameState after, RaceSummary real) = Race(seed);
            RaceSummary replay = RaceReplay.FromHistory(after, after.RaceHistory.Last());

            Assert.That(replay.Classification.Select(c => (c.TeamName, c.Position, c.ClassPosition, c.Laps, c.Retired, c.PrizeMoney)),
                Is.EqualTo(real.Classification.Select(c => (c.TeamName, c.Position, c.ClassPosition, c.Laps, c.Retired, c.PrizeMoney))));
            Assert.That(replay.Player!.Position, Is.EqualTo(real.Player!.Position));
            Assert.That(replay.PositionByLap, Is.EqualTo(real.PositionByLap));
            Assert.That(replay.Stints.Select(s => (s.DriverId, s.DriverName, s.StartLap, s.EndLap)),
                Is.EqualTo(real.Stints.Select(s => (s.DriverId, s.DriverName, s.StartLap, s.EndLap))));
            Assert.That(replay.PitStops.Select(s => (s.Lap, s.DriverOut, s.DriverIn)),
                Is.EqualTo(real.PitStops.Select(s => (s.Lap, s.DriverOut, s.DriverIn))));
            Assert.That(replay.PitStops.Any(s => s.DriverIn != s.DriverOut), Is.True);

            var issues = real.Issues.Select(i => (i.Lap, Name: IssueTemplates.AllIssueTemplates.First(t => t.Id == i.TemplateId).Description, i.DriverId));
            Assert.That(replay.Issues.Where(i => i.FixedLap != null).Select(i => (i.Lap, Name: i.TemplateId, i.DriverId)),
                Is.EquivalentTo(issues.Select(i => (i.Lap, char.ToUpperInvariant(i.Name[0]) + i.Name.Substring(1), i.DriverId))));
            Assert.That(replay.Issues.Count(i => i.FixedLap == null), Is.EqualTo(real.Player.Retired ? 1 : 0));
        }

        [Test]
        public void ARaceOutsideTheWindowKeepsOnlyItsResults()
        {
            (GameState after, _) = Race(1);
            RaceHistoryEntry old = after.RaceHistory.Last() with { PositionHistory = null, CarIndexById = null, Events = null, Stints = null };
            RaceSummary replay = RaceReplay.FromHistory(after, old);
            Assert.That(replay.Classification, Has.Count.EqualTo(100));
            Assert.That(replay.Player, Is.Not.Null);
            Assert.That(replay.PositionByLap, Is.Empty);
            Assert.That(replay.Stints, Is.Empty);
        }
    }
}
