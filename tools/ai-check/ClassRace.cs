// "bvsa": 50 B-class cars and one A-class car (the quickest, standing in for the player) racing from a
// grid ordered as the game's, the A car at the back. It measures how the field races in traffic:
//   lap times      every car's laps and the A car's, leaving out lap 1, laps with a rejoin or ghost,
//                  and laps under 97% of the car's own solo lap or over 130% of the slowest B car's
//   held up        seconds a lap the A car runs slower than it would unheld with a car within 2 s ahead
//   following      with a car within 2 s ahead: how far off its line a car runs, and how much of that
//                  time it is in the tow
//   tow before a pass  how long a car was in the tow before it pulled out
//   braking with room  seconds the braking filter fired while the passing side had room
//   contacts, rear-ends over 2 m/s, pile-ups
// Seeds: BVSA_SEEDS (default 11 12 13 14), race length BVSA_MINUTES (default 15).
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Raceday;

static partial class Check
{
    static readonly string[] BModels = { "b-25", "b-24", "b-23", "b-22", "b-21" };

    static List<Entry> ClassField(int seed)
    {
        var rng = new Random(seed);
        GridSlot[] slots = circuit.Data.grid.slots;
        var entries = new List<Entry>();
        for (int i = 0; i < 51; i++)
        {
            float d, lateral;
            if (i < slots.Length) { d = slots[i].d; lateral = slots[i].lateral; }
            else
            {
                int last = slots.Length - 1;
                d = slots[last].d - (i - last) * (slots[last - 1].d - slots[last].d);
                lateral = slots[i % 2].lateral;
            }
            entries.Add(new Entry
            {
                Model = i == 50 ? "a-29" : BModels[i % BModels.Length],
                Start = d - 2.6f,
                Lateral = lateral,
                Pace = 70f + 30f * (float)rng.NextDouble(),
                Consistency = 60f + 40f * (float)rng.NextDouble(),
                Safety = 80f + 20f * (float)rng.NextDouble(),
                Seed = i + 1,
            });
        }
        return entries;
    }

    sealed class ClassWatch
    {
        public readonly Dictionary<Racer, List<float>> Clean = new Dictionary<Racer, List<float>>();
        readonly Dictionary<Racer, bool> dirty = new Dictionary<Racer, bool>();
        readonly Dictionary<Racer, int> seen = new Dictionary<Racer, int>();
        readonly Dictionary<Racer, float> spell = new Dictionary<Racer, float>();
        readonly Dictionary<Racer, bool> wasPulling = new Dictionary<Racer, bool>();
        public float FollowTime, OffLine, TowTime, BrakeRoom, BrakeRoomPulling, HeldA, Dropped;
        static readonly int TraceId = int.TryParse(Environment.GetEnvironmentVariable("BVSA_TRACE"), out int ti) ? ti : -1;
        static readonly float TraceFrom = float.TryParse(Environment.GetEnvironmentVariable("BVSA_TRACE_FROM"), out float tf) ? tf : 0f;
        static readonly bool BrakeLog = Environment.GetEnvironmentVariable("BVSA_LOG") != null;
        public readonly List<float> TowSpells = new List<float>();
        public float Cut;

        public void Step(Race r)
        {
            foreach (Racer x in r.Racers)
            {
                if (!seen.ContainsKey(x)) { seen[x] = 0; dirty[x] = false; Clean[x] = new List<float>(); spell[x] = 0f; wasPulling[x] = false; }
                if (x.Rejoin.Busy || x.Rejoin.Ghost) dirty[x] = true;
                while (seen[x] < x.LapTimes.Count)
                {
                    float t = x.LapTimes[seen[x]];
                    // The first lap time is lap 2's: the standing start lap is never timed.
                    bool ok = !dirty[x] && t > 0.97f * x.Pilot.Profile.LapTime && t < Cut;
                    if (ok) Clean[x].Add(t); else Dropped++;
                    seen[x]++;
                    dirty[x] = false;
                }
                if (r.Time < 15f || x.Rejoin.Busy) continue;
                float v = x.Car.Speed;
                Racecraft k = x.Craft;
                bool following = v > 10f && k.FrontGap < 2f * v;
                if (x.Id == TraceId && r.Time > TraceFrom && r.Time < TraceFrom + 4f && (int)Math.Round(r.Time / Race.Dt) % 5 == 0)
                    Console.WriteLine($"TRACE {Race.Clock(r.Time)} {v * 3.6f:0} km/h free {x.Pilot.FreeSpeed * 3.6f:0} gap {k.FrontGap:0.0} front #{k.FrontId} {(k.Braking ? $"BRK #{k.BrakeFor} {k.BrakeGap:0.0} short {k.BrakeSide:0.0} beside" : "")} pull {k.PullingOut} off {x.Pilot.Offset:0.0} lat {x.Car.Lateral:0.0} room-brake {k.BrakedWithRoom}");
                if (k.BrakedWithRoom)
                {
                    BrakeRoom += Race.Dt;
                    if (k.PullingOut) BrakeRoomPulling += Race.Dt;
                    if (BrakeLog) Console.WriteLine($"BRKROOM {Race.Clock(r.Time)} {r.Name(x)} {x.Car.TrackIndex * circuit.Step:0} m {v * 3.6f:0} km/h free {x.Pilot.FreeSpeed * 3.6f:0} gap {k.FrontGap:0.0} front #{k.FrontId} brake for #{k.BrakeFor} {k.BrakeGap:0.0} m short, {k.BrakeSide:0.0} m beside, pulling {k.PullingOut} off {x.Pilot.Offset:0.0} lat {x.Car.Lateral:0.0}");
                }
                bool towed = x.Car.DragScale < 0.97f;
                if (following)
                {
                    FollowTime += Race.Dt;
                    OffLine += Math.Abs(x.Pilot.Offset) * Race.Dt;
                    if (towed) TowTime += Race.Dt;
                    if (x.Entry.Model[0] == 'a' && v < x.Pilot.FreeSpeed - 1f) HeldA += Race.Dt;
                }
                if (k.PullingOut && !wasPulling[x] && spell[x] > 0f) TowSpells.Add(spell[x]);
                spell[x] = towed && !k.PullingOut ? spell[x] + Race.Dt : k.PullingOut ? spell[x] : 0f;
                if (!k.PullingOut && wasPulling[x]) spell[x] = 0f;
                wasPulling[x] = k.PullingOut;
            }
        }
    }

    static void ClassRace()
    {
        int[] seeds = (Environment.GetEnvironmentVariable("BVSA_SEEDS") ?? "11 12 13 14").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        float minutes = float.TryParse(Environment.GetEnvironmentVariable("BVSA_MINUTES"), out float m) ? m : 15f;
        foreach (int seed in seeds)
        {
            var watch = new ClassWatch();
            var piles = new PileWatch($"seed {seed} ");
            Race race = RunRace(ClassField(seed), r =>
            {
                if (watch.Cut == 0f) watch.Cut = 1.3f * r.Racers.Where(x => x.Entry.Model[0] == 'b').Max(x => x.Pilot.Profile.LapTime);
                watch.Step(r);
                piles.Step(r);
                return false;
            }, minutes * 60f);
            List<float> all = watch.Clean.Values.SelectMany(l => l).ToList();
            Racer a = race.Racers.First(x => x.Entry.Model[0] == 'a');
            List<float> aLaps = watch.Clean[a];
            float bSolo = race.Racers.Where(x => x.Entry.Model[0] == 'b').Average(x => x.Pilot.Profile.LapTime);
            Console.WriteLine($"seed {seed}: field lap {(all.Count > 0 ? all.Average() : 0):0.00} s over {all.Count} laps ({watch.Dropped} left out, cut {watch.Cut:0.0} s), B solo {bSolo:0.00} s, "
                + $"A lap {(aLaps.Count > 0 ? aLaps.Average() : 0):0.00} s over {aLaps.Count} laps (solo {a.Pilot.Profile.LapTime:0.00} s), A laps {a.Laps}, A held up {watch.HeldA / Math.Max(a.Laps, 1):0.0} s a lap, "
                + $"A place {race.Racers.Count(x => x.Distance > a.Distance) + 1}, "
                + $"following {watch.FollowTime:0} s, off line {watch.OffLine / Math.Max(watch.FollowTime, 1e-3f):0.00} m, in tow {100f * watch.TowTime / Math.Max(watch.FollowTime, 1e-3f):0}%, "
                + $"tow before a pull-out {(watch.TowSpells.Count > 0 ? watch.TowSpells.Average() : 0):0.0} s ({watch.TowSpells.Count} pull-outs), braking with room {watch.BrakeRoom:0.0} s ({watch.BrakeRoomPulling:0.0} s while pulling out), "
                + $"{race.Contacts.Count} contacts, {race.Contacts.Count(c => c.RearEnd && c.Speed > RearHit)} rear-ends, {piles.PileUps.Count} pile-ups, {race.PassCount} passes");
        }
    }
}
