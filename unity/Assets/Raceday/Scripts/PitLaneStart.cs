using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit lane start, with no Unity objects so it can be tested outside Unity. The field waits in
    /// single file in the pit lane's fast lane, the first car's nose at the end of the limiter zone, and
    /// leaves one car every <see cref="Interval"/> seconds, front first. Each car drives the fast lane at
    /// the limiter, down the exit road and onto the track, and races from where its route meets its
    /// racing line. Pit stops stay closed until every car has left the pit lane, so nobody pits into
    /// the queue.
    /// </summary>
    public sealed class PitLaneStart
    {
        /// <summary>Seconds between one car leaving and the next.</summary>
        public const float Interval = 10f;
        /// <summary>Seconds before the first car leaves.</summary>
        public const float FirstGo = 5f;
        /// <summary>From one car's middle to the next one's in the queue, m.</summary>
        public const float Spacing = 8f;
        /// <summary>The first car's middle stands this far short of the end of the limiter zone, m.</summary>
        private const float FrontBack = 3f;
        /// <summary>Fast forward stops this long before the car ahead of the last one leaves, seconds.</summary>
        public const float Lead = 3f;

        /// <summary>The way every car drives out: the fast lane, the exit road, and onto the racing line.</summary>
        public readonly RacingLine Route;
        public readonly int Cars;
        public float Time { get; private set; }

        private readonly PitLane pits;
        private readonly int front, rejoin;

        public PitLaneStart(PitLane pits, RacingLine racingLine, int cars)
        {
            this.pits = pits;
            Cars = cars;
            Route = pits.Route(racingLine, -1);
            Circuit c = pits.Circuit;
            // The track sample whose route point is at the end of the limiter zone.
            front = -1;
            float best = float.MaxValue;
            for (int k = 0; k < c.Count; k++)
            {
                if (!pits.InRange(k)) continue;
                float gap = Mathf.Abs(pits.LaneAt(k) - pits.LimitEndLane);
                if (gap < best) { best = gap; front = k; }
            }
            rejoin = pits.RejoinedSample();
        }

        public void Tick(float dt) => Time += dt;

        /// <summary>When a place in the queue leaves, seconds from the start. Place 0 is the front.</summary>
        public static float LeavesAt(int place) => FirstGo + place * Interval;

        public bool Gone(int place) => Time >= LeavesAt(place);

        /// <summary>Race time to fast forward to: just before the car ahead of the last one leaves.</summary>
        public float FastForwardTo => Cars < 2 ? 0f : LeavesAt(Cars - 2) - Lead;

        /// <summary>Where a place in the queue is: on the route, facing along it.</summary>
        public Vector3 Place(int place, out Vector3 forward)
        {
            Circuit c = pits.Circuit;
            float back = FrontBack + place * Spacing;
            int k = front;
            for (int guard = 0; guard < c.Count; guard++)
            {
                int prev = c.Wrap(k - 1);
                float segment = Mathf.Max(Route.Segment[prev], 0.01f);
                if (back <= segment)
                {
                    forward = Route.Tangent[k];
                    return Vector3.Lerp(Route.Point[k], Route.Point[prev], back / segment);
                }
                back -= segment;
                k = prev;
            }
            forward = Route.Tangent[k];
            return Route.Point[k];
        }

        /// <summary>
        /// True once a car on the route at track sample k has rejoined its racing line: it races from here.
        /// Every place in the queue is behind the front, so a car still waiting is never out.
        /// </summary>
        public bool Out(int k)
        {
            Circuit c = pits.Circuit;
            int past = c.Wrap(k - front);
            return past < c.Count / 2 && past >= c.Wrap(rejoin - front);
        }
    }
}
