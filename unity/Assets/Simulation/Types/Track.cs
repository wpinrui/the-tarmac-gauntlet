#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    // Matches the track builder's JSON export format.

    public sealed record Point2D
    {
        public double X { get; init; }
        public double Y { get; init; }
    }

    public sealed record BezierCurve
    {
        public Point2D P0 { get; init; } = new Point2D();  // Start anchor
        public Point2D Cp1 { get; init; } = new Point2D(); // First control point
        public Point2D Cp2 { get; init; } = new Point2D(); // Second control point
        public Point2D P3 { get; init; } = new Point2D();  // End anchor
    }

    public sealed record TrackPath
    {
        public IReadOnlyList<BezierCurve> Curves { get; init; } = Array.Empty<BezierCurve>();
        public bool Closed { get; init; }
        public double LengthMetres { get; init; }
    }

    public sealed record StartFinish
    {
        public int SegmentIndex { get; init; }                  // Index into track.curves
        public double T { get; init; }                          // Parameter along that segment (0 to 1)
        public Point2D Position { get; init; } = new Point2D(); // World position in metres
    }

    public sealed record TrackScale
    {
        public double MetresPerGridSquare { get; init; }
    }

    public sealed record Track
    {
        public int Version { get; init; }
        public TrackScale Scale { get; init; } = new TrackScale();
        /// <summary>TS <c>track.track</c>. Renamed: C# forbids a member named after its type.</summary>
        public TrackPath Path { get; init; } = new TrackPath();
        public TrackPath? PitLane { get; init; }
        public StartFinish? StartFinish { get; init; }
    }
}
