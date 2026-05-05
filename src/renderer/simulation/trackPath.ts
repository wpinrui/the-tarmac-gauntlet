// Phase 3 track-map geometry: parses the cubic Bézier loop in `track.json` and
// builds two precomputed LUTs that drive disc rendering on the map.
//
//  - **Path LUT** maps arc-length s ∈ [0, L] to world (x, y) and tangent angle.
//  - **Pacing LUT** maps lap-fraction f ∈ [0, 1) to arc-length s, where the
//    relationship is non-linear: high-curvature sections consume more f per
//    metre, so a disc moving at constant `df/dt` (which is what the simulation
//    gives us — laps are timed, not metered) visibly slows into corners and
//    accelerates on straights. The loop's f=0 anchor is the start/finish line.
//
// The pit lane is intentionally ignored — Issue #30 defers it to Phase 5.

import type { BezierCurve, Point2D, Track } from "../types/track";
import type { CarLapSnapshot } from "./raceLoop";

// ---------------------------------------------------------------------------
// Tunables. Phase-3 "feel" knobs. Top-of-file so retuning is a one-line edit.
// ---------------------------------------------------------------------------

/**
 * Dense Bézier-t samples per cubic segment, used only to measure the curve.
 * 128 × 48 ≈ 6k raw samples — plenty for accurate arc-length integration on a
 * 9km loop. These are NOT what we compute curvature against; see
 * `RESAMPLE_SPACING_M` below.
 */
const RAW_SAMPLES_PER_CURVE = 128;
/**
 * Uniform arc-length spacing for the resampled path LUT. Cubic Bézier curves
 * sampled at uniform t cluster near anchor points, which makes
 * central-difference tangents biased and produces phantom "sharp bends" on
 * actually-straight sections. Resampling uniformly along arc length kills
 * that bias: every Δs is the same, so κ = |Δθ| / Δs reflects true curvature.
 */
const RESAMPLE_SPACING_M = 3;
/** Buckets in the pacing LUT (lap fraction → arc length). Half-degree resolution. */
const PACING_BUCKETS = 720;
/**
 * Curvature-to-slowdown coefficient. v = clamp(1 / (1 + α·κ), v_min, 1).
 * α = 75 puts a ~50m hairpin (κ = 0.02 /m) at v ≈ 0.4. Retune by eye if a
 * future track has tighter corners or longer straights.
 */
const CURVATURE_ALPHA = 75;
/** Floor on the speed multiplier so hairpins don't crawl to a halt. */
const CURVATURE_MIN_SPEED = 0.35;
/**
 * Half-window (in samples) for curvature smoothing. With 3m uniform spacing,
 * ±5 samples = ±15m smoothing radius — wide enough to kill any residual
 * single-sample noise without bleeding corners onto neighbouring straights.
 */
const CURVATURE_SMOOTH_HALF = 5;

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface TrackPathLUT {
  /** Total arc length of the closed loop in metres. */
  totalLength: number;
  /** Cumulative arc length at each sample. Length === points.length. Closes back to totalLength. */
  s: number[];
  /** World coordinates at each sample. */
  points: Point2D[];
  /** Tangent angle (radians, atan2-based) at each sample, central-difference around the loop. */
  tangents: number[];
  /** World-space bounding box of the path. */
  bbox: { minX: number; maxX: number; minY: number; maxY: number };
  /** Arc length at the start/finish line. Used as the f=0 offset on the loop. */
  startFinishS: number;
}

export interface PacingLUT {
  /**
   * Arc-length samples indexed by uniform lap-fraction buckets.
   * sArray[i] = arc length at f = i / (sArray.length - 1).
   * sArray[0] === 0 and sArray[last] === totalLength so wrapping is exact.
   */
  sArray: number[];
}

export interface TrackPath {
  path: TrackPathLUT;
  pacing: PacingLUT;
}

// ---------------------------------------------------------------------------
// Bézier evaluation
// ---------------------------------------------------------------------------

function bezierPoint(c: BezierCurve, t: number): Point2D {
  const u = 1 - t;
  const a = u * u * u;
  const b = 3 * u * u * t;
  const d = 3 * u * t * t;
  const e = t * t * t;
  return {
    x: a * c.p0.x + b * c.cp1.x + d * c.cp2.x + e * c.p3.x,
    y: a * c.p0.y + b * c.cp1.y + d * c.cp2.y + e * c.p3.y,
  };
}

// ---------------------------------------------------------------------------
// Build path LUT from cubic Béziers
// ---------------------------------------------------------------------------

function buildPathLUT(
  curves: BezierCurve[],
  startSegmentIndex: number,
  startSegmentT: number,
): TrackPathLUT {
  // Pass 1: dense Bézier-t sampling. Used only to measure total arc length
  // and the start/finish offset. Discarded after resampling because the
  // sample density is non-uniform — Bézier curves with control points
  // pulling toward anchors bunch samples near anchors, which biases
  // central-difference tangents and produces phantom curvature spikes on
  // sections that are actually straight.
  const rawPoints: Point2D[] = [];
  for (const c of curves) {
    for (let i = 0; i < RAW_SAMPLES_PER_CURVE; i++) {
      rawPoints.push(bezierPoint(c, i / RAW_SAMPLES_PER_CURVE));
    }
  }
  const rawCumS = new Array<number>(rawPoints.length);
  rawCumS[0] = 0;
  for (let i = 1; i < rawPoints.length; i++) {
    const dx = rawPoints[i].x - rawPoints[i - 1].x;
    const dy = rawPoints[i].y - rawPoints[i - 1].y;
    rawCumS[i] = rawCumS[i - 1] + Math.hypot(dx, dy);
  }
  const closingD = Math.hypot(
    rawPoints[0].x - rawPoints[rawPoints.length - 1].x,
    rawPoints[0].y - rawPoints[rawPoints.length - 1].y,
  );
  const totalLength = rawCumS[rawCumS.length - 1] + closingD;

  // Start/finish arc length: sample inside the start/finish segment whose
  // Bézier-t is closest to the configured t. Resolved against the raw
  // (Bézier-t-spaced) array because that's where (segmentIndex, t) maps
  // cleanly to an index.
  const startRawIdx =
    startSegmentIndex * RAW_SAMPLES_PER_CURVE +
    Math.round(startSegmentT * RAW_SAMPLES_PER_CURVE);
  const startFinishS = rawCumS[startRawIdx % rawPoints.length];

  // Pass 2: uniform arc-length resample. Walks the raw cumulative arc length
  // monotonically, interpolating between raw anchors (and into the closing
  // chord) so each output sample sits at a fixed arc-length offset.
  const N = Math.max(2, Math.round(totalLength / RESAMPLE_SPACING_M));
  const points = new Array<Point2D>(N);
  const s = new Array<number>(N);
  let cursor = 0;
  for (let i = 0; i < N; i++) {
    const targetS = (i / N) * totalLength;
    while (
      cursor < rawCumS.length - 1 &&
      rawCumS[cursor + 1] <= targetS
    ) {
      cursor++;
    }
    let a: Point2D;
    let b: Point2D;
    let sLo: number;
    let sHi: number;
    if (cursor < rawCumS.length - 1) {
      a = rawPoints[cursor];
      b = rawPoints[cursor + 1];
      sLo = rawCumS[cursor];
      sHi = rawCumS[cursor + 1];
    } else {
      // Past the last raw sample → walk the closing chord rawPoints[last] → rawPoints[0].
      a = rawPoints[rawPoints.length - 1];
      b = rawPoints[0];
      sLo = rawCumS[rawCumS.length - 1];
      sHi = totalLength;
    }
    const span = sHi - sLo;
    const t = span > 0 ? (targetS - sLo) / span : 0;
    points[i] = { x: a.x + t * (b.x - a.x), y: a.y + t * (b.y - a.y) };
    s[i] = targetS;
  }

  // Tangents via central difference on the *uniformly-spaced* samples. Now
  // that Δs is constant, the tangent is unbiased and curvature is clean.
  const tangents = new Array<number>(N);
  for (let i = 0; i < N; i++) {
    const next = points[(i + 1) % N];
    const prev = points[(i - 1 + N) % N];
    tangents[i] = Math.atan2(next.y - prev.y, next.x - prev.x);
  }

  // Bounding box.
  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  for (const p of points) {
    if (p.x < minX) minX = p.x;
    if (p.x > maxX) maxX = p.x;
    if (p.y < minY) minY = p.y;
    if (p.y > maxY) maxY = p.y;
  }

  return {
    totalLength,
    s,
    points,
    tangents,
    bbox: { minX, maxX, minY, maxY },
    startFinishS,
  };
}

// ---------------------------------------------------------------------------
// Build pacing LUT (lap-fraction → arc-length, shaped by curvature)
// ---------------------------------------------------------------------------

function buildPacingLUT(path: TrackPathLUT): PacingLUT {
  const n = path.points.length;
  // Per-sample curvature κ ≈ |Δθ| / Δs across one wrapped step.
  const kappa = new Array<number>(n);
  for (let i = 0; i < n; i++) {
    const j = (i + 1) % n;
    let dTheta = path.tangents[j] - path.tangents[i];
    // Unwrap to [-π, π] so a 359° → 1° transition reads as +2° not -358°.
    while (dTheta > Math.PI) dTheta -= 2 * Math.PI;
    while (dTheta < -Math.PI) dTheta += 2 * Math.PI;
    const ds =
      i + 1 < n
        ? path.s[i + 1] - path.s[i]
        : path.totalLength - path.s[i];
    kappa[i] = ds > 0 ? Math.abs(dTheta) / ds : 0;
  }

  // Box-blur smoothing — kills numerical spikes without flattening real
  // corners.
  const smooth = new Array<number>(n);
  const w = CURVATURE_SMOOTH_HALF;
  for (let i = 0; i < n; i++) {
    let sum = 0;
    for (let k = -w; k <= w; k++) sum += kappa[(i + k + n) % n];
    smooth[i] = sum / (2 * w + 1);
  }

  // Speed multiplier per sample.
  const speed = new Array<number>(n);
  for (let i = 0; i < n; i++) {
    const v = 1 / (1 + CURVATURE_ALPHA * smooth[i]);
    speed[i] = v < CURVATURE_MIN_SPEED ? CURVATURE_MIN_SPEED : v;
  }

  // Cumulative pacing-time τ(s). dτ = ds / v.
  // We sample at the path LUT's anchors (s[i]); τ at the closure = total τ.
  const tau = new Array<number>(n + 1);
  tau[0] = 0;
  for (let i = 0; i < n; i++) {
    const ds =
      i + 1 < n
        ? path.s[i + 1] - path.s[i]
        : path.totalLength - path.s[i];
    tau[i + 1] = tau[i] + ds / speed[i];
  }
  const tauTotal = tau[n];

  // Invert: for each evenly-spaced f in [0, 1], find s such that τ(s)/τTotal === f.
  // Walk the τ array once; both axes are monotonic so this is O(n + buckets).
  const sArray = new Array<number>(PACING_BUCKETS + 1);
  let cursor = 0;
  for (let b = 0; b <= PACING_BUCKETS; b++) {
    const targetTau = (b / PACING_BUCKETS) * tauTotal;
    while (cursor < n && tau[cursor + 1] < targetTau) cursor++;
    if (cursor >= n) {
      sArray[b] = path.totalLength;
      continue;
    }
    const tauLo = tau[cursor];
    const tauHi = tau[cursor + 1];
    const sLo = path.s[cursor];
    const sHi = cursor + 1 < n ? path.s[cursor + 1] : path.totalLength;
    const span = tauHi - tauLo;
    const t = span > 0 ? (targetTau - tauLo) / span : 0;
    sArray[b] = sLo + t * (sHi - sLo);
  }
  // Pin endpoints exactly.
  sArray[0] = 0;
  sArray[PACING_BUCKETS] = path.totalLength;

  return { sArray };
}

// ---------------------------------------------------------------------------
// Public API: build, sample, query
// ---------------------------------------------------------------------------

export function loadTrackPath(track: Track): TrackPath {
  const startSegIdx = track.startFinish?.segmentIndex ?? 0;
  const startSegT = track.startFinish?.t ?? 0;
  const path = buildPathLUT(track.track.curves, startSegIdx, startSegT);
  const pacing = buildPacingLUT(path);
  return { path, pacing };
}

/** Normalize lap fraction to [0, 1). Handles negative inputs (wraps) and
 *  values ≥ 1 (modulo). */
function normFraction(f: number): number {
  return ((f % 1) + 1) % 1;
}

/** Resolve lap fraction → arc length (metres) via the pacing LUT, including the start/finish offset. */
function arcLengthAtLapFraction(track: TrackPath, f: number): number {
  const fn = normFraction(f);
  const idx = fn * PACING_BUCKETS;
  const lo = Math.floor(idx);
  const hi = lo + 1;
  const t = idx - lo;
  const sPace =
    hi <= PACING_BUCKETS
      ? track.pacing.sArray[lo] + t * (track.pacing.sArray[hi] - track.pacing.sArray[lo])
      : track.pacing.sArray[PACING_BUCKETS];
  return (sPace + track.path.startFinishS) % track.path.totalLength;
}

/** Binary search the path LUT for the sample bracketing arc-length s. */
function pathSampleAt(path: TrackPathLUT, s: number): { idx: number; t: number } {
  const sArr = path.s;
  // Wrap closure: s ∈ [s[n-1], totalLength] is the closing chord, anchored
  // between the last sample and points[0].
  if (s >= sArr[sArr.length - 1]) {
    const span = path.totalLength - sArr[sArr.length - 1];
    const t = span > 0 ? (s - sArr[sArr.length - 1]) / span : 0;
    return { idx: sArr.length - 1, t };
  }
  let lo = 0;
  let hi = sArr.length - 1;
  while (lo + 1 < hi) {
    const mid = (lo + hi) >> 1;
    if (sArr[mid] <= s) lo = mid;
    else hi = mid;
  }
  const span = sArr[hi] - sArr[lo];
  const t = span > 0 ? (s - sArr[lo]) / span : 0;
  return { idx: lo, t };
}

export function pointAtLapFraction(track: TrackPath, f: number): Point2D {
  const s = arcLengthAtLapFraction(track, f);
  const { idx, t } = pathSampleAt(track.path, s);
  const a = track.path.points[idx];
  const b = track.path.points[(idx + 1) % track.path.points.length];
  return {
    x: a.x + t * (b.x - a.x),
    y: a.y + t * (b.y - a.y),
  };
}

/**
 * Unit-tangent at a lap fraction. Reserved for orienting discs / chevrons in
 * a later phase — no live caller in Phase 3 since current discs are unrotated
 * circles. Tested to lock the contract before consumers exist.
 */
export function tangentAtLapFraction(track: TrackPath, f: number): Point2D {
  const s = arcLengthAtLapFraction(track, f);
  const { idx } = pathSampleAt(track.path, s);
  const theta = track.path.tangents[idx];
  return { x: Math.cos(theta), y: Math.sin(theta) };
}

/**
 * Polyline samples for stroking the track to a canvas. `spacingMetres` defaults
 * to the LUT's native spacing; pass a larger value for cheaper offscreen
 * pre-renders. Reserved for offscreen track-stroke prerendering in a later
 * phase — TrackMap currently strokes `path.points` directly each frame.
 * Tested to lock the contract before consumers exist.
 */
export function samplePolyline(track: TrackPath, spacingMetres = 0): Point2D[] {
  if (spacingMetres <= 0) return [...track.path.points, track.path.points[0]];
  const out: Point2D[] = [];
  const total = track.path.totalLength;
  const steps = Math.max(2, Math.ceil(total / spacingMetres));
  for (let i = 0; i <= steps; i++) {
    const sWorld = (i / steps) * total;
    const { idx, t } = pathSampleAt(track.path, sWorld);
    const a = track.path.points[idx];
    const b = track.path.points[(idx + 1) % track.path.points.length];
    out.push({ x: a.x + t * (b.x - a.x), y: a.y + t * (b.y - a.y) });
  }
  return out;
}

// ---------------------------------------------------------------------------
// Per-car lap fraction from race snapshots
// ---------------------------------------------------------------------------

export interface CarLapPosition {
  /** Number of full laps completed at simElapsed. */
  lapsCompleted: number;
  /** Progress into the current lap, ∈ [0, 1). 0 means at start/finish line. */
  fractionOfLap: number;
}

/**
 * Given a car's per-lap snapshots and a sim-time elapsed, return the car's
 * lap count and linear progress into the current lap. Linear-in-time: a lap is
 * traversed in exactly `(snapshots[N].totalTime - snapshots[N-1].totalTime)`
 * seconds regardless of where on the track the car is. The pacing LUT
 * separately handles geometry-derived speed variation.
 *
 * Cars past their last snapshot freeze at the start/finish line of their last
 * completed lap — the simulation has no later data to interpolate against.
 */
export function lapFractionForCar(
  snapshots: CarLapSnapshot[],
  simElapsed: number,
): CarLapPosition {
  if (snapshots.length === 0) {
    return { lapsCompleted: 0, fractionOfLap: 0 };
  }

  // Largest index whose totalTime ≤ simElapsed. We scan the full array
  // rather than break on the first miss: snapshots are ordered today, but
  // a future scheduling change (retirements, alt branches) could violate
  // that, and silently returning the wrong position would be invisible.
  // Linear cost is fine — snapshot counts are O(laps).
  let lastCompletedIdx = -1;
  for (let i = 0; i < snapshots.length; i++) {
    if (snapshots[i].totalTime <= simElapsed) lastCompletedIdx = i;
  }

  if (lastCompletedIdx === -1) {
    // Doing lap 1: lap-start time = 0.
    const next = snapshots[0];
    const f = next.totalTime > 0 ? clamp01(simElapsed / next.totalTime) : 0;
    return { lapsCompleted: 0, fractionOfLap: f };
  }

  const completed = snapshots[lastCompletedIdx];
  const next = snapshots[lastCompletedIdx + 1];
  if (!next) {
    // Past the last snapshot — race is over for this car. Pin to start/finish.
    return { lapsCompleted: completed.lapsCompleted, fractionOfLap: 0 };
  }
  const span = next.totalTime - completed.totalTime;
  const f = span > 0 ? clamp01((simElapsed - completed.totalTime) / span) : 0;
  return { lapsCompleted: completed.lapsCompleted, fractionOfLap: f };
}

function clamp01(x: number): number {
  if (x < 0) return 0;
  // Strict <1 so wrapping doesn't lap-double. 1e-6 is well below the
  // pacing-LUT's bucket spacing (1/PACING_BUCKETS ≈ 1.4e-3), so the cap is
  // visually indistinguishable from 1 while staying clearly inside [0, 1).
  if (x >= 1) return 0.999999;
  return x;
}
