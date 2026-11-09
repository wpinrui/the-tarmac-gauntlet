using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TarmacGauntlet.Simulation.Tests
{
    public class LiveRaceTests
    {
        private static LiveRace NewRace(int seed = 7, string model = "c-18") =>
            new LiveRace(TestField.Build(model, seed), TestField.PlayerCarId, seed);

        private static Dictionary<string, List<CarLapSnapshot>> OtherCarsLapsBefore(LiveRace race, double t) =>
            race.Result.LapSnapshots
                .Where(kv => kv.Key != TestField.PlayerCarId)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Where(s => s.TotalTime < t).ToList());

        private static void AssertSameLaps(Dictionary<string, List<CarLapSnapshot>> a, Dictionary<string, List<CarLapSnapshot>> b)
        {
            Assert.That(b.Keys, Is.EquivalentTo(a.Keys));
            foreach (string id in a.Keys)
            {
                Assert.That(b[id].Count, Is.EqualTo(a[id].Count), id);
                for (int i = 0; i < a[id].Count; i++)
                {
                    Assert.That(b[id][i].TotalTime, Is.EqualTo(a[id][i].TotalTime), $"{id} lap {i + 1}");
                    Assert.That(b[id][i].TyreWear, Is.EqualTo(a[id][i].TyreWear), $"{id} lap {i + 1}");
                }
            }
        }

        [Test]
        public void FieldHasHundredCarsWithPlayerLast()
        {
            List<CarEntry> field = TestField.Build("f-01", 1);
            Assert.That(field.Count, Is.EqualTo(100));
            Assert.That(field[field.Count - 1].CarId, Is.EqualTo(TestField.PlayerCarId));
        }

        [Test]
        public void SameSeedGivesSameRace()
        {
            LiveRace a = NewRace();
            LiveRace b = NewRace();
            Assert.That(b.Result.Results.Select(r => r.CarId), Is.EqualTo(a.Result.Results.Select(r => r.CarId)));
            Assert.That(b.Result.Results.Select(r => r.TotalTime), Is.EqualTo(a.Result.Results.Select(r => r.TotalTime)));
        }

        [Test]
        public void LeaderRunsAboutFortyEightLaps()
        {
            LiveRace race = NewRace();
            Assert.That(race.Result.Results[0].LapsCompleted, Is.InRange(35, 60));
        }

        [TestCase(7, "c-18")]
        [TestCase(1, "f-01")]
        [TestCase(2, "a-26")]
        [TestCase(3, "f1-31")]
        [TestCase(4, "d-14")]
        public void PlayerDecisionsLeaveOtherCarsAlone(int seed, string model)
        {
            LiveRace race = NewRace(seed, model);
            race.AdvanceTo(300);
            var before = OtherCarsLapsBefore(race, double.MaxValue);

            race.Mode = InstructionMode.Push;
            AssertSameLaps(before, OtherCarsLapsBefore(race, double.MaxValue));

            race.PitPlan = new PitPlan();
            AssertSameLaps(before, OtherCarsLapsBefore(race, double.MaxValue));

            race.AdvanceTo(900);
            race.Mode = InstructionMode.Conserve;
            AssertSameLaps(before, OtherCarsLapsBefore(race, double.MaxValue));
        }

        [Test]
        public void ResimulatingIsQuick()
        {
            LiveRace race = NewRace();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) race.Mode = i % 2 == 0 ? InstructionMode.Push : InstructionMode.Normal;
            TestContext.Progress.WriteLine($"resim ms: {watch.ElapsedMilliseconds / 5.0}");
            Assert.That(watch.ElapsedMilliseconds / 5.0, Is.LessThan(250));
        }

        [Test]
        public void ResultHasNoPacer()
        {
            LiveRace race = NewRace();
            Assert.That(race.Result.Results.Count, Is.EqualTo(100));
            Assert.That(race.Result.Results.Select(r => r.FinalPosition), Is.EqualTo(Enumerable.Range(1, 100)));
            Assert.That(race.Result.PositionHistory.All(l => l.Count == 100 && l.OrderBy(p => p).SequenceEqual(Enumerable.Range(1, 100))), Is.True);
            Assert.That(race.Result.Events.Any(e => e.CarId == "__pacer"), Is.False);
            Assert.That(race.Result.FastestLap!.CarId, Is.Not.EqualTo("__pacer"));
        }

        [Test]
        public void PassedLapsKeepTheirDecisions()
        {
            LiveRace race = NewRace();
            race.Mode = InstructionMode.Push;
            race.AdvanceTo(600);
            int locked = race.LockedLaps;
            Assert.That(locked, Is.GreaterThan(3));
            List<CarLapSnapshot> pushed = race.Result.LapSnapshots[TestField.PlayerCarId].Take(locked).ToList();

            race.Mode = InstructionMode.Conserve;
            List<CarLapSnapshot> after = race.Result.LapSnapshots[TestField.PlayerCarId].Take(locked).ToList();

            for (int i = 0; i < locked; i++) Assert.That(after[i].TotalTime, Is.EqualTo(pushed[i].TotalTime), $"lap {i + 1}");
        }

        [Test]
        public void ModeTakesEffectFromTheNextLap()
        {
            LiveRace race = NewRace();
            race.AdvanceTo(200);
            int locked = race.LockedLaps;
            race.Mode = InstructionMode.Push;
            IReadOnlyList<CarLapSnapshot> snaps = race.Result.LapSnapshots[TestField.PlayerCarId];
            // Snapshot N records the mode lap N ran in. The lap in progress keeps its mode.
            Assert.That(snaps[locked].InstructionMode, Is.EqualTo(InstructionMode.Normal));
            Assert.That(snaps[locked + 1].InstructionMode, Is.EqualTo(InstructionMode.Push));
        }

        [Test]
        public void PitCallStopsAtTheEndOfTheCurrentLapThenClears()
        {
            LiveRace race = NewRace();
            race.AdvanceTo(100);
            int lap = race.LockedLaps + 1;
            race.Mode = InstructionMode.Push;
            string coDriver = race.PlayerEntry.Drivers[1].Id;
            race.PitPlan = new PitPlan { ChangeTyres = true, Refuel = true, NextDriverId = coDriver };
            Assert.That(race.PlayerPittedOnLap(lap), Is.True);

            double lapEnd = race.Result.LapSnapshots[TestField.PlayerCarId][lap - 1].TotalTime;
            race.AdvanceTo(lapEnd + 0.01);

            Assert.That(race.PitPlan, Is.Null);
            Assert.That(race.Mode, Is.EqualTo(InstructionMode.Normal));
            CarLapSnapshot next = race.Result.LapSnapshots[TestField.PlayerCarId][lap];
            Assert.That(next.CurrentDriverId, Is.EqualTo(coDriver));
        }

        [Test]
        public void PlaybackFollowsLapTimes()
        {
            LiveRace race = NewRace();
            var playback = new RacePlayback(race.Result);
            CarLapSnapshot first = race.Result.LapSnapshots[TestField.PlayerCarId][0];

            CarPlaybackState mid = playback.StateAt(TestField.PlayerCarId, first.TotalTime / 2);
            Assert.That(mid.Lap, Is.EqualTo(1));
            Assert.That(mid.LapFraction, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(playback.StateAt(TestField.PlayerCarId, first.TotalTime + 0.01).Lap, Is.EqualTo(2).Or.EqualTo(1));
        }

        [Test]
        public void PlaybackShowsPitStopsAndRetirements()
        {
            LiveRace race = NewRace();
            var playback = new RacePlayback(race.Result);
            RaceEvent pit = race.Result.Events.First(e => e.Type == RaceEventType.PitStop);
            double lapEnd = race.Result.LapSnapshots[pit.CarId!][pit.Lap - 1].TotalTime;
            Assert.That(playback.StateAt(pit.CarId!, lapEnd + 0.01).Phase, Is.EqualTo(CarPhase.Pitting));

            CarRaceResult? retired = race.Result.Results.FirstOrDefault(r => r.Retired);
            if (retired != null)
            {
                Assert.That(playback.StateAt(retired.CarId, retired.TotalTime + 1).Phase, Is.EqualTo(CarPhase.Retired));
                Assert.That(playback.OrderAt(retired.TotalTime + 1).Last(), Is.Not.EqualTo(race.Result.Results[0].CarId));
            }
        }

        [Test]
        public void PitStopLastsItsSimulatedDuration()
        {
            LiveRace race = NewRace();
            var playback = new RacePlayback(race.Result, race.PitDurations);
            RaceEvent pit = race.Result.Events.First(e => e.Type == RaceEventType.PitStop);
            double lapEnd = race.Result.LapSnapshots[pit.CarId!][pit.Lap - 1].TotalTime;
            double stop = race.PitDurations[pit.CarId!][pit.Lap];
            Assert.That(stop, Is.GreaterThan(0));
            Assert.That(playback.StateAt(pit.CarId!, lapEnd + stop - 0.01).Phase, Is.EqualTo(CarPhase.Pitting));
            CarPlaybackState after = playback.StateAt(pit.CarId!, lapEnd + stop + 0.01);
            Assert.That(after.Phase, Is.EqualTo(CarPhase.Running));
            Assert.That(after.PittedLastLap, Is.True);
        }

        [Test]
        public void GaugesStartFullAndShowFreshTyresAfterAStop()
        {
            LiveRace race = NewRace();
            PlayerGauges start = race.GaugesAt(0);
            Assert.That(start.TyreWear, Is.EqualTo(0));
            Assert.That(start.Fuel, Is.EqualTo(start.FuelCapacity));
            Assert.That(start.Issues, Is.Empty);

            race.AdvanceTo(200);
            int lap = race.LockedLaps + 1;
            race.PitPlan = new PitPlan { ChangeTyres = true, Refuel = true };
            double lapEnd = race.Result.LapSnapshots[TestField.PlayerCarId][lap - 1].TotalTime;
            Assert.That(race.GaugesAt(lapEnd - 0.01).TyreWear, Is.GreaterThan(0));
            race.AdvanceTo(lapEnd + 0.01);
            PlayerGauges after = race.GaugesAt(lapEnd + 0.01);
            Assert.That(after.TyreWear, Is.EqualTo(0));
            Assert.That(after.Fuel, Is.EqualTo(after.FuelCapacity).Within(1e-9));
        }

        [Test]
        public void LapTimeOverrideReplacesTheComputedTime()
        {
            CarEntry entry = TestField.Build("c-18", 1).Last() with
            {
                LapTimeOverride = (lap, driver, computed) => 40,
                PitDecider = (lap, snap) => null,
            };
            RaceResultFull result = RaceLoop.SimulateRace(new[] { entry }, new RaceOptions { TotalLaps = 3, Random = () => 0.5 });
            Assert.That(result.LapSnapshots[entry.CarId].Select(s => s.TotalTime), Is.EqualTo(new[] { 40.0, 80.0, 120.0 }));
        }

        [Test]
        public void OrderAtEndMatchesFinalResults()
        {
            LiveRace race = NewRace();
            var playback = new RacePlayback(race.Result);
            IReadOnlyList<string> order = playback.OrderAt(RaceClock.TotalRaceSeconds);
            Assert.That(order.Take(10), Is.EqualTo(race.Result.Results.Take(10).Select(r => r.CarId)));
        }
    }
}
