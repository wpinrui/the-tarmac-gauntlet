using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Where a pit crew works round a car in its garage's box. Kept apart from <see cref="PitComplex"/> so the
    /// circuit's geometry check needs no pit lane routing.
    /// </summary>
    public sealed class PitStopSpots
    {
        private readonly PitComplex complex;
        private readonly PitLane lane;

        public PitStopSpots(PitComplex complex, PitLane lane)
        {
            this.complex = complex;
            this.lane = lane;
        }

        /// <summary>The middle of the garage's box on the lane and the way a car stands in it, nose first.</summary>
        public CrewSpot Box(int garage)
        {
            float i = lane.BoxLane[garage];
            Vector3 a = lane.LanePoint(i - 1f, lane.BoxLateral), b = lane.LanePoint(i + 1f, lane.BoxLateral);
            return new CrewSpot(lane.BoxPoint(garage), Flat(b - a).normalized);
        }

        /// <summary>Jobs at a stop, each with its own spot round the car in the box.</summary>
        public enum StopJob { Lollipop, FrontJack, RearJack, FrontLeft, FrontRight, RearLeft, RearRight, Fuel }

        /// <summary>
        /// Where a crew member stands for a job at a stop, round a car of this length and width in the garage's
        /// box, facing the car. Left and right are the car's own. Fuel goes in on the garage side, by the rear wheel.
        /// </summary>
        public CrewSpot Stop(int garage, StopJob job, float carLength, float carWidth)
        {
            CrewSpot box = Box(garage);
            Vector3 fwd = box.Facing;
            var left = new Vector3(-fwd.z, 0f, fwd.x);
            Vector3 c = box.Position;
            float l = carLength * 0.5f, w = carWidth * 0.5f + 0.55f, axle = carLength * 0.32f;
            Vector3 P(float ahead, float toLeft) => c + fwd * ahead + left * toLeft;
            Vector3 garageSide = Vector3.Dot(complex.Frame(garage).Out, left) < 0f ? left : -left;
            switch (job)
            {
                case StopJob.Lollipop: return new CrewSpot(P(l + 1.6f, 0f), -fwd);
                case StopJob.FrontJack: return new CrewSpot(P(l + 0.6f, 0f), -fwd);
                case StopJob.RearJack: return new CrewSpot(P(-l - 0.6f, 0f), fwd);
                case StopJob.FrontLeft: return new CrewSpot(P(axle, w), -left);
                case StopJob.FrontRight: return new CrewSpot(P(axle, -w), left);
                case StopJob.RearLeft: return new CrewSpot(P(-axle, w), -left);
                case StopJob.RearRight: return new CrewSpot(P(-axle, -w), left);
                default: return new CrewSpot(c - fwd * (axle * 0.6f) + garageSide * w, -garageSide);
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
