using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Career.Tests
{
    public class CareerTests
    {
        private string root = "";

        private static readonly NewGameOptions Options = new NewGameOptions
        {
            PlayerName = "Ivan Test",
            Nationality = "sg",
            TeamName = "Backfire Racing",
            Skills = new PlayerSkills { Driver = 5, Engineer = 5, Business = 5 },
        };

        private static Func<double> Seeded(int seed)
        {
            var r = new Random(seed);
            return r.NextDouble;
        }

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "tg-careers-" + Guid.NewGuid().ToString("N"));
            GameSession.Configure(new CareerStore(root));
        }

        [TearDown]
        public void TearDown()
        {
            GameSession.Close();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        /// <summary>A game with every optional part filled in, so the round trip covers them.</summary>
        private static GameState RichGame()
        {
            GameState game = GameInit.InitializeGame(Options, Seeded(1));
            game = GameActions.BuyCar(game, new CarInstance { Id = "car-p1", ModelId = "f-01", Condition = 82 }, 4_500, "Nusa Carrier");
            game = GameActions.EnterCar(game, "car-p1");
            game = GameActions.HireDriver(game, "driver-300", "Driver 300", 2, 1_500);
            game = GameActions.SetLineup(game, new[] { "driver-300", RaceEntry.PlayerCharacterDriverId });
            return GameActions.PushRaceHistory(game, new RaceHistoryEntry
            {
                Year = 1,
                Results = new[] { new RaceResult { TeamId = "player", CarId = "car-p1", CarClass = CarClass.F, Position = 3, LapsCompleted = 210, PrizeMoney = 1_250.5 } },
                FastestLap = new FastestLap { TeamId = "ai-4", Time = 101.234567 },
                PositionHistory = new[] { new[] { 1, 2 }, new[] { 2, 1 } },
                CarIndexById = new System.Collections.Generic.Dictionary<string, int> { ["car-p1"] = 0 },
                Events = new[] { new RaceEvent { Lap = 12, Type = RaceEventType.PitStop, Text = "Pit stop", CarId = "car-p1", TeamId = "player" } },
                Stints = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<Stint>>
                {
                    ["car-p1"] = new[] { new Stint { DriverId = "driver-300", StartLap = 1, EndLap = 60 } },
                },
                ModeCounters = new System.Collections.Generic.Dictionary<string, ModeCounter> { ["car-p1"] = new ModeCounter { Push = 3, Normal = 200, Conserve = 7 } },
            });
        }

        [Test]
        public void ARoundTripKeepsEveryValue()
        {
            GameState game = RichGame();
            string json = SaveJson.Serialize(game);
            GameState back = SaveJson.Deserialize(json);

            Assert.That(SaveJson.Serialize(back), Is.EqualTo(json));
            Assert.That(back.Teams[0], Is.InstanceOf<PlayerTeam>());
            Assert.That(back.Teams.Skip(1), Is.All.InstanceOf<AITeam>());
            Assert.That(back.Teams.Count, Is.EqualTo(100));
            Assert.That(back.Drivers.Count, Is.EqualTo(game.Drivers.Count));
            Assert.That(((PlayerTeam)back.Teams[0]).Skills, Is.EqualTo(Options.Skills));
            Assert.That(back.RaceHistory[0].FastestLap!.Time, Is.EqualTo(101.234567));
            Assert.That(back.RaceHistory[0].Stints!["car-p1"][0].EndLap, Is.EqualTo(60));
            Assert.That(DriverLineup.Of(back), Is.EqualTo(new[] { "driver-300", RaceEntry.PlayerCharacterDriverId }));
        }

        [Test]
        public void ASaveFromANewerGameIsRefused()
        {
            string json = SaveJson.Serialize(RichGame() with { Meta = new SaveMetadata { Version = SaveJson.Version + 1 } });
            Assert.Throws<InvalidOperationException>(() => SaveJson.Deserialize(json));
        }

        [Test]
        public void ANewCareerIsSavedAndContinued()
        {
            Assert.That(GameSession.HasSave, Is.False);
            GameSession.NewCareer(Options, Seeded(2));
            string id = GameSession.CareerId!;

            GameSession.Configure(new CareerStore(root));
            Assert.That(GameSession.ContinueLatest(), Is.True);
            Assert.That(GameSession.CareerId, Is.EqualTo(id));
            Assert.That(GameSession.State!.Teams.OfType<PlayerTeam>().Single().Name, Is.EqualTo("Backfire Racing"));
            Assert.That(GameSession.State.Meta.Version, Is.EqualTo(SaveJson.Version));
        }

        [Test]
        public void EveryChangeIsAutosaved()
        {
            GameSession.NewCareer(Options, Seeded(3));
            for (int i = 1; i <= 20; i++) GameSession.Apply(g => GameActions.SetCrewSize(g, i % 17));
            GameSession.Apply(g => GameActions.BuySpares(g, 2, 100));
            GameSession.Flush();

            GameState saved = GameSession.Store.Load(GameSession.CareerId!);
            var player = saved.Teams.OfType<PlayerTeam>().Single();
            Assert.That(player.CrewSize, Is.EqualTo(20 % 17));
            Assert.That(player.SpareParts, Is.EqualTo(2));
        }

        [Test]
        public void CareersKeepTheirOwnAutosaves()
        {
            var store = new CareerStore(root);
            GameState a = GameInit.InitializeGame(Options with { TeamName = "Alpha" }, Seeded(4));
            GameState b = GameInit.InitializeGame(Options with { TeamName = "Bravo" }, Seeded(5));
            store.Save("career-a", a, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            store.Save("career-b", b, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            store.Save("career-a", GameActions.SetCrewSize(a, 4), new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

            Assert.That(store.List().Select(c => c.TeamName), Is.EqualTo(new[] { "Alpha", "Bravo" }));
            Assert.That(store.Latest()!.CareerId, Is.EqualTo("career-a"));
            Assert.That(store.Load("career-b").Teams.OfType<PlayerTeam>().Single().CrewSize, Is.EqualTo(0));
            Assert.That(store.Load("career-a").Teams.OfType<PlayerTeam>().Single().CrewSize, Is.EqualTo(4));
        }

        [Test]
        public void ANewGameReplacesTheOldCareer()
        {
            GameSession.NewCareer(Options with { TeamName = "First" }, Seeded(6));
            string first = GameSession.CareerId!;
            GameSession.Apply(g => GameActions.SetCrewSize(g, 3));
            GameSession.NewCareer(Options with { TeamName = "Second" }, Seeded(7));

            Assert.That(GameSession.CareerId, Is.Not.EqualTo(first));
            Assert.That(GameSession.Store.List().Select(c => c.TeamName), Is.EqualTo(new[] { "Second" }));
            Assert.That(Directory.Exists(Path.Combine(root, first)), Is.False);
        }

        [Test]
        public void ADamagedInfoFileHidesOnlyThatCareer()
        {
            var store = new CareerStore(root);
            store.Save("good", GameInit.InitializeGame(Options, Seeded(8)));
            store.Save("bad", GameInit.InitializeGame(Options, Seeded(9)));
            File.WriteAllText(Path.Combine(root, "bad", "info.json"), "{ not json");
            Assert.That(store.List().Select(c => c.CareerId), Is.EqualTo(new[] { "good" }));
        }

        [Test]
        public void CareerIdsCannotLeaveTheSaveFolder()
        {
            var store = new CareerStore(root);
            Assert.Throws<ArgumentException>(() => store.Load("../x"));
            Assert.Throws<ArgumentException>(() => store.Delete(""));
        }
    }
}
