using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Works out the tuned racing lines and pilots one at a time on a single low-priority thread, so
    /// the game keeps the rest of the machine while a full field is tuned. Cars of the same model
    /// share one tuned line, which is only worked out once.
    /// </summary>
    public static class LineTuner
    {
        private static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private static readonly Dictionary<string, Task<RacingLine>> lines = new Dictionary<string, Task<RacingLine>>();
        private static Thread worker;

        /// <summary>The line tuned for this model on this circuit, worked out once and shared.</summary>
        public static Task<RacingLine> Line(Circuit circuit, CarSpec spec)
        {
            lock (lines)
            {
                // A spec comes from its model alone (CarSpec.For), so the model name picks the line.
                string key = spec.Name;
                if (!lines.TryGetValue(key, out Task<RacingLine> line) || line.IsFaulted)
                {
                    line = Run(() => new RacingLine(circuit, spec));
                    lines[key] = line;
                }
                return line;
            }
        }

        /// <summary>Runs the work on the tuner thread, after everything queued before it.</summary>
        public static Task<T> Run<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>();
            queue.Add(() =>
            {
                try { done.SetResult(work()); }
                catch (Exception e) { done.SetException(e); }
            });
            Start();
            return done.Task;
        }

        private static void Start()
        {
            lock (queue)
            {
                if (worker != null) return;
                worker = new Thread(() => { foreach (Action a in queue.GetConsumingEnumerable()) a(); })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "Line tuner",
                };
                worker.Start();
            }
        }
    }
}
