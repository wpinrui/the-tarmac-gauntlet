// Matches the track builder's JSON export format.

export interface Point2D {
  x: number;
  y: number;
}

export interface BezierCurve {
  p0: Point2D;   // Start anchor
  cp1: Point2D;  // First control point
  cp2: Point2D;  // Second control point
  p3: Point2D;   // End anchor
}

export interface TrackPath {
  curves: BezierCurve[];
  closed: boolean;
  lengthMetres: number;
}

export interface StartFinish {
  segmentIndex: number;   // Index into track.curves
  t: number;              // Parameter along that segment (0–1)
  position: Point2D;      // World position in metres
}

export interface TrackScale {
  metresPerGridSquare: number;
}

export interface Track {
  version: number;
  scale: TrackScale;
  track: TrackPath;
  pitLane: TrackPath | null;
  startFinish: StartFinish | null;
}
