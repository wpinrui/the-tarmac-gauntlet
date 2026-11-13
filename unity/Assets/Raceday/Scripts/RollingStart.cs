using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The rolling start, with no Unity objects so it can be tested outside Unity. The field waits on
    /// the grid, then follows the pace car round a formation lap two by two, in grid order. The pace
    /// car peels off into the pit lane, the leader holds the formation speed, and the race goes green
    /// as the leader crosses the start line. Each car races from the moment it crosses the line.
    ///
    /// Distances are race distances, as in <see cref="TrafficCar.Distance"/>. The formation runs to a
    /// speed for each point of the lap that every car in the field can hold, so the whole field slows
    /// and speeds up at the same places and keeps its spacing. Each car's place is measured from the
    /// leader's, so a gap opening in the middle of the field does not ripple back through it.
    /// </summary>
    public sealed class RollingStart
    {
        public enum Phase { Grid, Formation, Green }

        /// <summary>Seconds on the grid before the pace car moves off.</summary>
        public const float GridWait = 3f;
        /// <summary>Formation speed, m/s.</summary>
        public const float Speed = 33f;
        /// <summary>Distance from one row of two to the next in the formation, m: room to stop behind the row ahead at formation speed.</summary>
        public const float RowGap = 24f;
        /// <summary>Distance the leader keeps behind the pace car, m.</summary>
        public const float PaceGap = 30f;
        /// <summary>Offset of each column from the track centre, m: odd grid slots on the right.</summary>
        public const float Column = 3f;
        /// <summary>The pace car peels off this far before the pit entry, m.</summary>
        public const float PeelBefore = 380f;
        /// <summary>Share of the slowest car's own speeds the formation runs at, so every car has grip and power to spare.</summary>
        private const float Margin = 0.85f;
        private const float Gain = 0.6f;          // speed per metre out of place, 1/s
        private const float Catch = 6f;           // most a car may run over the formation speed to get back in place
        private const float Follow = 10f;         // closest a car runs behind the one ahead of it in its column, m
        private const float Headway = 0.3f;       // and that much more per m/s
        /// <summary>The last part of the formation lap starts with the leader this far short of the last corner, m.</summary>
        public const float FinalApproach = 200f;
        /// <summary>Gap the player's limiter keeps to the car before it in grid order, m: just behind a car beside it, the follow gap to one a row ahead.</summary>
        private const float Beside = 3f;
        /// <summary>Speed over the player's limit at which the limiter starts to brake, m/s; the power is cut from the limit itself.</summary>
        public const float LimiterGrace = 0.5f;
        /// <summary>Speed further over at which the limiter brakes fully, m/s.</summary>
        private const float LimiterBand = 2f;

        /// <summary>Formation speed at each track sample, m/s.</summary>
        public readonly float[] Speeds;
        private readonly Circuit circuit;

        /// <summary>
        /// A rolling start for a field: the formation speed at each point is the formation speed
        /// or less, so the slowest car in the field can hold it with grip to spare.
        /// </summary>
        public RollingStart(Circuit circuit, IEnumerable<CarSpec> field)
        {
            this.circuit = circuit;
            Speeds = new float[circuit.Count];
            for (int k = 0; k < Speeds.Length; k++) Speeds[k] = Speed;
            var columns = new[] { new RacingLine(circuit, ColumnOf(0)), new RacingLine(circuit, ColumnOf(1)) };
            var seen = new HashSet<string>();
            foreach (CarSpec spec in field)
            {
                if (!seen.Add(spec.Name)) continue;
                foreach (RacingLine column in columns)
                {
                    var p = new SpeedProfile(column, spec, 1f);
                    for (int k = 0; k < Speeds.Length; k++) Speeds[k] = Mathf.Min(Speeds[k], Margin * p.Speed[k]);
                }
            }
        }

        /// <summary>Formation speed at a race distance.</summary>
        public float SpeedAt(float distance) => Speeds[circuit.IndexOf(Mathf.Repeat(distance, circuit.Lap))];

        public Phase State { get; private set; } = Phase.Grid;
        public float Time { get; private set; }
        /// <summary>True once the pace car has peeled off.</summary>
        public bool PaceCarIn { get; private set; }

        public static float ColumnOf(int slot) => slot % 2 == 0 ? Column : -Column;

        public void Tick(float dt)
        {
            Time += dt;
            if (State == Phase.Grid && Time > GridWait) State = Phase.Formation;
        }

        public void PeelOff() => PaceCarIn = true;

        /// <summary>
        /// Cuts to the last part of the formation lap: the pace car is in and the field follows the
        /// leader at the formation speed to the green.
        /// </summary>
        public void CutToFinal()
        {
            if (State == Phase.Grid) State = Phase.Formation;
            PaceCarIn = true;
        }

        /// <summary>Race distance of the leader as the last part of the formation lap starts: just short of the last corner, on the formation lap.</summary>
        public static float FinalStart(Circuit circuit)
        {
            float last = 0f;
            foreach (CornerData corner in circuit.Data.corners) last = Mathf.Max(last, corner.s);
            return last - FinalApproach;
        }

        /// <summary>Where a grid slot runs in the formation, as a race distance, with the leader at <c>leader</c>.</summary>
        public static float PlaceOf(float leader, int slot) => leader - (slot / 2) * RowGap;

        public void Green() => State = Phase.Green;

        /// <summary>
        /// True while a car keeps its place in the formation: until the green, then until it crosses
        /// the line, so nobody races from the middle of a corner. <c>laps</c> counts from the green.
        /// </summary>
        public bool Holds(int laps) => State != Phase.Green || laps < 0;

        /// <summary>The pace car's speed limit on top of <see cref="Speeds"/>: still on the grid, then none.</summary>
        public float PaceCarCap => State == Phase.Grid ? 0f : float.PositiveInfinity;

        /// <summary>
        /// The leader's speed limit: its place behind the pace car, then the formation speed once the
        /// pace car is in.
        /// </summary>
        public float LeaderCap(float leader, float paceCar, float paceCarSpeed)
        {
            if (State == Phase.Grid) return 0f;
            float here = SpeedAt(leader);
            if (PaceCarIn) return here;
            float error = paceCar - PaceGap - leader;
            return Mathf.Clamp(here + Gain * error, 0f, here + Catch);
        }

        /// <summary>
        /// A car's speed limit in the formation: back to its place behind the leader, and never closer
        /// than it could stop behind the car ahead in its column.
        /// </summary>
        public float Cap(int slot, float mine, float speed, float leader, float ahead, float aheadSpeed, float aheadBraking, float braking)
        {
            if (State == Phase.Grid) return 0f;
            float target = leader - (slot / 2) * RowGap;
            float here = SpeedAt(mine);
            float cap = Mathf.Clamp(here + Gain * (target - mine), 0f, here + Catch);
            if (!float.IsNaN(ahead))
            {
                // Safe even if the car ahead stopped as hard as it can and this car brakes less hard.
                float room = ahead - mine - Follow - Headway * speed;
                float ratio = Mathf.Min(braking / Mathf.Max(aheadBraking, 1f), 1f);
                float safe = room > 0f ? Mathf.Sqrt(aheadSpeed * aheadSpeed * ratio + 2f * braking * room) : 0f;
                cap = Mathf.Min(cap, safe);
            }
            return cap;
        }

        /// <summary>
        /// The limiter on a car the player drives in the formation: its formation limit (<c>cap</c>),
        /// and never past the car before it in grid order (<c>before</c>, a race distance, or NaN for
        /// pole). It stays just behind a car beside it and the follow gap behind one a row ahead.
        /// </summary>
        public static float Limiter(int slot, float cap, float mine, float before, float beforeSpeed)
        {
            if (slot == 0 || float.IsNaN(before)) return cap;
            float keep = slot % 2 == 1 ? Beside : Follow;
            return Mathf.Min(cap, Mathf.Max(0f, beforeSpeed + Gain * (before - keep - mine)));
        }

        /// <summary>Brake the limiter applies to a car going at <c>speed</c> with its brakes set to come on above <c>above</c>, 0 to 1.</summary>
        public static float LimiterBrake(float speed, float above) => speed > above ? Mathf.Clamp01((speed - above) / LimiterBand) : 0f;
    }
}
