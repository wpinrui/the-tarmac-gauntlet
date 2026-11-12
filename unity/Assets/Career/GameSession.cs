#nullable enable
using System;
using System.Threading.Tasks;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Career
{
    /// <summary>
    /// The live career: the current <see cref="GameState"/> and the career it belongs to. It outlives scene loads.
    /// Every change goes through <see cref="Apply"/>, which autosaves that career in the background.
    /// </summary>
    public static class GameSession
    {
        private static CareerStore? store;
        private static Autosaver? autosaver;

        /// <summary>The game being played, or null before a career is started or continued.</summary>
        public static GameState? State { get; private set; }

        /// <summary>The career <see cref="State"/> belongs to.</summary>
        public static string? CareerId { get; private set; }

        /// <summary>Raised on the calling thread after every change of <see cref="State"/>.</summary>
        public static event Action<GameState>? Changed;

        /// <summary>Raised on a worker thread when an autosave fails to write.</summary>
        public static event Action<Exception>? SaveFailed;

        public static bool IsConfigured => store != null;

        public static CareerStore Store => store ?? throw new InvalidOperationException("GameSession.Configure has not been called.");

        /// <summary>Points the session at the folder that holds the careers. Call once at start-up.</summary>
        public static void Configure(CareerStore careers)
        {
            if (!listeningToRaces)
            {
                RaceWeekend.Finished += SettleRace;
                listeningToRaces = true;
            }
            autosaver?.Flush();
            store = careers;
            autosaver = new Autosaver(careers);
            State = null;
            CareerId = null;
        }

        /// <summary>Whether there is a career to continue.</summary>
        public static bool HasSave => Store.Latest() != null;

        /// <summary>
        /// Starts a new career and saves it. One career at a time for now: the others are deleted once the new
        /// one is safely written.
        /// </summary>
        public static GameState NewCareer(NewGameOptions options, Func<double>? random = null)
        {
            CareerStore careers = Store;
            autosaver!.Flush();
            string id = CareerStore.NewCareerId();
            GameState game = GameInit.InitializeGame(options, random);
            careers.Save(id, game);
            foreach (CareerInfo other in careers.List())
                if (other.CareerId != id) careers.Delete(other.CareerId);
            Set(id, game);
            return game;
        }

        /// <summary>Loads the most recently saved career. False when there is none.</summary>
        public static bool ContinueLatest()
        {
            CareerInfo? latest = Store.Latest();
            if (latest == null) return false;
            Continue(latest.CareerId);
            return true;
        }

        public static void Continue(string careerId)
        {
            autosaver!.Flush();
            Set(careerId, Store.Load(careerId));
        }

        /// <summary>Changes the game and autosaves the career. Use it for every change, between races and after them.</summary>
        public static GameState Apply(Func<GameState, GameState> change)
        {
            if (State == null || CareerId == null) throw new InvalidOperationException("No career is loaded.");
            GameState next = change(State);
            if (ReferenceEquals(next, State)) return State;
            State = next;
            autosaver!.Queue(CareerId, next);
            Changed?.Invoke(next);
            return next;
        }

        /// <summary>Leaves the career, after its last autosave is written.</summary>
        public static void Close()
        {
            Flush();
            State = null;
            CareerId = null;
        }

        /// <summary>Blocks until every queued autosave is on disk. Call it before quitting.</summary>
        public static void Flush() => autosaver?.Flush();

        private static bool listeningToRaces;

        /// <summary>
        /// A finished race replaces the game with its settled result, which autosaves the career. A save of the race
        /// part way is done with.
        /// </summary>
        private static void SettleRace()
        {
            GameState? settled = RaceWeekend.Settled;
            if (settled == null || State == null) return;
            if (CareerId != null) Store.DeleteRace(CareerId);
            Apply(_ => settled);
        }

        /// <summary>Saves the race the career is part way through. Continuing the career races on from it.</summary>
        public static void SaveRace(RaceSave race)
        {
            if (CareerId == null) throw new InvalidOperationException("No career is loaded.");
            Store.SaveRace(CareerId, race);
        }

        /// <summary>Throws away the loaded career's race save, as a new race starts.</summary>
        public static void DropRace()
        {
            if (CareerId != null) Store.DeleteRace(CareerId);
        }

        /// <summary>The race the loaded career is part way through, or null for none.</summary>
        public static RaceSave? SavedRace() => CareerId == null ? null : Store.LoadRace(CareerId);

        private static void Set(string careerId, GameState game)
        {
            CareerId = careerId;
            State = game;
            Changed?.Invoke(game);
        }

        /// <summary>
        /// Writes saves on a worker thread so a change never waits on the disk. Game states are immutable, so the
        /// worker can serialize one while the game moves on. Only the newest queued state of a burst is written.
        /// </summary>
        private sealed class Autosaver
        {
            private readonly CareerStore careers;
            private readonly object gate = new object();
            private (string CareerId, GameState Game)? pending;
            private Task? running;

            public Autosaver(CareerStore careers)
            {
                this.careers = careers;
            }

            public void Queue(string careerId, GameState game)
            {
                lock (gate)
                {
                    pending = (careerId, game);
                    if (running == null) running = Task.Run(Drain);
                }
            }

            public void Flush()
            {
                while (true)
                {
                    Task? task;
                    lock (gate) task = running;
                    if (task == null) return;
                    task.Wait();
                }
            }

            private void Drain()
            {
                while (true)
                {
                    (string CareerId, GameState Game) job;
                    lock (gate)
                    {
                        if (pending == null)
                        {
                            running = null;
                            return;
                        }
                        job = pending.Value;
                        pending = null;
                    }
                    try
                    {
                        careers.Save(job.CareerId, job.Game);
                    }
                    catch (Exception e)
                    {
                        SaveFailed?.Invoke(e);
                    }
                }
            }
        }
    }
}
