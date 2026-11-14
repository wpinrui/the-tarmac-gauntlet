using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Which car each of the cars' own reflection probes is on, when each is drawn, and how much each car takes of its
    /// probe rather than the shared one (<see cref="CarReflections"/>). Plain C#, so tools/reflection-check can drive it
    /// round the lap with the whole field.
    /// <list type="bullet">
    /// <item>Slot 0 is the watched car's and never leaves it: it is drawn whole every frame, so the car always shows
    /// its own probe, as new as the frame. A cut to another car moves it there.</item>
    /// <item>The other slots go to the other cars nearest the camera. A car takes its probe in over <see cref="Fade"/>
    /// seconds once it is drawn at that car, and gives it back over the same time before the probe moves on, so a car's
    /// paint never jumps between its own probe and the shared one.</item>
    /// <item>The other slots are drawn a face a frame, the car not yet drawn first, then the one waiting longest.</item>
    /// </list>
    /// </summary>
    public sealed class ProbePlan
    {
        /// <summary>Seconds a car takes to take its probe in, or give it back.</summary>
        public const float Fade = 0.5f;
        /// <summary>Cars further from the camera than this give their probes back, m; a car behind the camera counts as this much further off.</summary>
        public const float NearReach = 80f, Behind = 40f;

        public sealed class Slot
        {
            /// <summary>The car the slot is on, or -1.</summary>
            public int Car = -1;
            /// <summary>The car the drawing now was started at.</summary>
            public int Drawing = -1;
            public bool Rendering;
            /// <summary>Drawn whole at the car it is on.</summary>
            public bool Ready;
            /// <summary>Giving its car back.</summary>
            public bool Leaving;
            /// <summary>How much of the car's reflection is this probe's, 0 to 1; the rest is the shared probe's.</summary>
            public float Weight;
            public int Started;
        }

        public readonly Slot[] Slots;
        private readonly List<int> picked = new List<int>();
        private readonly List<float> scores = new List<float>();

        public ProbePlan(int count)
        {
            Slots = new Slot[count];
            for (int i = 0; i < count; i++) Slots[i] = new Slot();
        }

        /// <summary>The slot's probe is to be drawn again before it is shown (its size changed).</summary>
        public void Invalidate(int slot)
        {
            Slots[slot].Ready = false;
            Slots[slot].Weight = 0f;
        }

        /// <summary>The slot on this car, or -1.</summary>
        public int SlotOf(int car)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Car == car) return i;
            return -1;
        }

        /// <summary>
        /// One frame. cars holds each car's place, or null for a car out of the race; watched is the watched car, or -1.
        /// finished says whether a slot's drawing is done. The slots to start drawing go in draws, each whole or a face a
        /// frame, at most busy of the other slots drawing at once.
        /// </summary>
        public void Step(float dt, int frame, Vector3 eye, Vector3 ahead, IReadOnlyList<Vector3?> cars, int watched, int busy,
            Func<int, bool> finished, List<(int slot, bool whole)> draws)
        {
            draws.Clear();
            if (Slots.Length == 0) return;
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot s = Slots[i];
                if (!s.Rendering || !finished(i)) continue;
                s.Rendering = false;
                if (s.Car >= 0 && s.Drawing == s.Car) s.Ready = true;
            }

            // The watched car's own slot.
            if (watched >= 0 && (watched >= cars.Count || cars[watched] == null)) watched = -1;
            Slot own = Slots[0];
            if (own.Car != watched)
            {
                own.Car = watched;
                own.Ready = false;
            }
            own.Weight = own.Ready ? 1f : 0f;

            // The other cars nearest the camera, one for each other slot.
            picked.Clear();
            scores.Clear();
            int room = Slots.Length - 1;
            for (int c = 0; c < cars.Count && room > 0; c++)
            {
                if (c == watched || cars[c] == null) continue;
                Vector3 to = cars[c].Value - eye;
                float score = to.magnitude + (Vector3.Dot(to, ahead) < 0f ? Behind : 0f);
                if (score > NearReach) continue;
                int at = scores.Count;
                while (at > 0 && scores[at - 1] > score) at--;
                if (at >= room) continue;
                picked.Insert(at, c);
                scores.Insert(at, score);
                if (picked.Count > room)
                {
                    picked.RemoveAt(room);
                    scores.RemoveAt(room);
                }
            }

            float step = dt / Fade;
            for (int i = 1; i < Slots.Length; i++)
            {
                Slot s = Slots[i];
                if (s.Car < 0) continue;
                if (s.Car >= cars.Count || cars[s.Car] == null)
                {
                    // Out of the race: nothing left to show it on.
                    Release(s);
                    continue;
                }
                s.Leaving = !picked.Contains(s.Car) || s.Car == watched;
                s.Weight = Mathf.MoveTowards(s.Weight, s.Ready && !s.Leaving ? 1f : 0f, step);
                if (s.Leaving && s.Weight <= 0f && !s.Rendering) Release(s);
            }
            foreach (int car in picked)
            {
                if (SlotOf(car) >= 0) continue;
                for (int i = 1; i < Slots.Length; i++)
                {
                    Slot s = Slots[i];
                    if (s.Car >= 0 || s.Rendering) continue;
                    s.Car = car;
                    s.Ready = false;
                    s.Leaving = false;
                    s.Weight = 0f;
                    break;
                }
            }

            if (own.Car >= 0 && !own.Rendering) Start(own, 0, true, frame, draws);
            int drawing = 0;
            for (int i = 1; i < Slots.Length; i++)
                if (Slots[i].Rendering) drawing++;
            while (drawing < busy)
            {
                int next = -1;
                foreach (int car in picked)
                {
                    int i = SlotOf(car);
                    if (i < 1 || Slots[i].Rendering || Slots[i].Leaving) continue;
                    if (next < 0 || Due(Slots[i], Slots[next])) next = i;
                    if (!Slots[next].Ready) break;
                }
                if (next < 0) return;
                Start(Slots[next], next, false, frame, draws);
                drawing++;
            }
        }

        private static void Start(Slot s, int i, bool whole, int frame, List<(int, bool)> draws)
        {
            s.Drawing = s.Car;
            s.Rendering = true;
            s.Started = frame;
            draws.Add((i, whole));
        }

        private static void Release(Slot s)
        {
            s.Car = -1;
            s.Ready = false;
            s.Leaving = false;
            s.Weight = 0f;
        }

        /// <summary>True if a should be drawn before b: one not yet drawn at its car first, then the one waiting longest.</summary>
        private static bool Due(Slot a, Slot b)
        {
            if (a.Ready != b.Ready) return !a.Ready;
            return a.Started < b.Started;
        }
    }
}
