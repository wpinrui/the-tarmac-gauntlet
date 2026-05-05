import { describe, it, expect } from "vitest";
import {
  loadTrackPath,
  pointAtLapFraction,
  tangentAtLapFraction,
  lapFractionForCar,
  samplePolyline,
} from "./trackPath";
import type { Track, BezierCurve } from "../types/track";
import { snap } from "./testFixtures";
import trackJson from "../assets/track.json";

const realTrack = trackJson as unknown as Track;

// ---------------------------------------------------------------------------
// Synthetic track helpers
// ---------------------------------------------------------------------------

/**
 * 4-segment cubic-Bézier square approximating a 100×100 loop. Each segment is
 * a side; control points sit at 1/3 and 2/3 along the side for a near-linear
 * stretch.
 */
function squareTrack(): Track {
  const side = 100;
  const mk = (
    p0: [number, number],
    cp1: [number, number],
    cp2: [number, number],
    p3: [number, number],
  ): BezierCurve => ({
    p0: { x: p0[0], y: p0[1] },
    cp1: { x: cp1[0], y: cp1[1] },
    cp2: { x: cp2[0], y: cp2[1] },
    p3: { x: p3[0], y: p3[1] },
  });
  return {
    version: 1,
    scale: { metresPerGridSquare: 1 },
    track: {
      curves: [
        mk([0, 0], [side / 3, 0], [(2 * side) / 3, 0], [side, 0]),
        mk([side, 0], [side, side / 3], [side, (2 * side) / 3], [side, side]),
        mk([side, side], [(2 * side) / 3, side], [side / 3, side], [0, side]),
        mk([0, side], [0, (2 * side) / 3], [0, side / 3], [0, 0]),
      ],
      closed: true,
      lengthMetres: side * 4,
    },
    pitLane: null,
    startFinish: { segmentIndex: 0, t: 0, position: { x: 0, y: 0 } },
  };
}

// ---------------------------------------------------------------------------
// Geometry: synthetic square
// ---------------------------------------------------------------------------

describe("trackPath: synthetic square loop", () => {
  const tp = loadTrackPath(squareTrack());

  it("totalLength matches the perimeter within 0.5%", () => {
    expect(tp.path.totalLength).toBeGreaterThan(400 * 0.995);
    expect(tp.path.totalLength).toBeLessThan(400 * 1.005);
  });

  it("bounding box covers the corners", () => {
    expect(tp.path.bbox.minX).toBeCloseTo(0, 3);
    expect(tp.path.bbox.minY).toBeCloseTo(0, 3);
    expect(tp.path.bbox.maxX).toBeCloseTo(100, 3);
    expect(tp.path.bbox.maxY).toBeCloseTo(100, 3);
  });

  it("pointAtLapFraction wraps cleanly around the loop", () => {
    const a = pointAtLapFraction(tp, 0);
    const b = pointAtLapFraction(tp, 1);
    expect(b.x).toBeCloseTo(a.x, 1);
    expect(b.y).toBeCloseTo(a.y, 1);
  });

  it("pointAtLapFraction at f=0 sits at the start/finish corner", () => {
    const p = pointAtLapFraction(tp, 0);
    expect(p.x).toBeCloseTo(0, 1);
    expect(p.y).toBeCloseTo(0, 1);
  });

  it("traverses each side roughly in turn at f=0.25, 0.5, 0.75", () => {
    // The four sides are traversed in order p0→p3→p3→p0. Pacing-LUT
    // distortion is small for a near-square, so quarter-loops land near
    // each remaining corner.
    const cornerAt = (f: number) => pointAtLapFraction(tp, f);
    const c1 = cornerAt(0.25);
    const c2 = cornerAt(0.5);
    const c3 = cornerAt(0.75);
    // Loose tolerances — the pacing LUT slows into the elbows so quarters
    // don't land exactly on corners.
    expect(c1.x).toBeGreaterThan(50);
    expect(c2.x).toBeGreaterThan(50);
    expect(c2.y).toBeGreaterThan(50);
    expect(c3.y).toBeGreaterThan(50);
    expect(c3.x).toBeLessThan(50);
  });

  it("tangentAtLapFraction returns a unit vector", () => {
    const t = tangentAtLapFraction(tp, 0.1);
    expect(Math.hypot(t.x, t.y)).toBeCloseTo(1, 4);
  });

  it("pacing LUT is monotonic and pinned at endpoints", () => {
    const arr = tp.pacing.sArray;
    expect(arr[0]).toBe(0);
    expect(arr[arr.length - 1]).toBeCloseTo(tp.path.totalLength, 3);
    for (let i = 1; i < arr.length; i++) {
      expect(arr[i]).toBeGreaterThanOrEqual(arr[i - 1]);
    }
  });

  it("samplePolyline at coarse spacing returns a closed-ish polyline", () => {
    const poly = samplePolyline(tp, 20);
    expect(poly.length).toBeGreaterThan(20);
    const first = poly[0];
    const last = poly[poly.length - 1];
    expect(Math.hypot(last.x - first.x, last.y - first.y)).toBeLessThan(1);
  });
});

// ---------------------------------------------------------------------------
// Geometry: real track.json
// ---------------------------------------------------------------------------

describe("trackPath: real track.json", () => {
  const tp = loadTrackPath(realTrack);
  const declared = realTrack.track.lengthMetres;

  it("totalLength is within 1% of declared length", () => {
    expect(tp.path.totalLength).toBeGreaterThan(declared * 0.99);
    expect(tp.path.totalLength).toBeLessThan(declared * 1.01);
  });

  it("loop is continuous: pointAt(f=0) ≈ pointAt(f=1)", () => {
    const a = pointAtLapFraction(tp, 0);
    const b = pointAtLapFraction(tp, 1);
    expect(Math.hypot(b.x - a.x, b.y - a.y)).toBeLessThan(1);
  });

  it("f=0 lands at the declared start/finish position", () => {
    const sf = realTrack.startFinish!;
    const p = pointAtLapFraction(tp, 0);
    // Sample-quantised — within a few metres is the design tolerance.
    expect(Math.hypot(p.x - sf.position.x, p.y - sf.position.y)).toBeLessThan(20);
  });

  it("100 evenly-spaced fractions are all inside the bounding box", () => {
    const { bbox } = tp.path;
    for (let i = 0; i < 100; i++) {
      const p = pointAtLapFraction(tp, i / 100);
      expect(p.x).toBeGreaterThanOrEqual(bbox.minX - 1);
      expect(p.x).toBeLessThanOrEqual(bbox.maxX + 1);
      expect(p.y).toBeGreaterThanOrEqual(bbox.minY - 1);
      expect(p.y).toBeLessThanOrEqual(bbox.maxY + 1);
    }
  });
});

// ---------------------------------------------------------------------------
// lapFractionForCar
// ---------------------------------------------------------------------------

describe("lapFractionForCar", () => {
  it("zero snapshots: lap 0, fraction 0", () => {
    expect(lapFractionForCar([], 100)).toEqual({
      lapsCompleted: 0,
      fractionOfLap: 0,
    });
  });

  it("before the first lap: linear into lap 1", () => {
    const snaps = [snap(1, 30), snap(2, 60)];
    const r = lapFractionForCar(snaps, 15);
    expect(r.lapsCompleted).toBe(0);
    expect(r.fractionOfLap).toBeCloseTo(0.5, 4);
  });

  it("at sim-time === snapshot.totalTime: that lap is now completed (f=0 of next lap)", () => {
    const snaps = [snap(1, 30), snap(2, 60)];
    const r = lapFractionForCar(snaps, 30);
    expect(r.lapsCompleted).toBe(1);
    expect(r.fractionOfLap).toBe(0);
  });

  it("midway through lap 2", () => {
    const snaps = [snap(1, 30), snap(2, 60), snap(3, 90)];
    const r = lapFractionForCar(snaps, 45);
    expect(r.lapsCompleted).toBe(1);
    expect(r.fractionOfLap).toBeCloseTo(0.5, 4);
  });

  it("past the last snapshot: pin at start/finish of last lap", () => {
    const snaps = [snap(1, 30), snap(2, 60)];
    const r = lapFractionForCar(snaps, 100);
    expect(r.lapsCompleted).toBe(2);
    expect(r.fractionOfLap).toBe(0);
  });

  it("varying lap lengths interpolate correctly", () => {
    const snaps = [snap(1, 30), snap(2, 90)]; // lap 2 took 60s
    const r = lapFractionForCar(snaps, 60); // halfway through lap 2
    expect(r.lapsCompleted).toBe(1);
    expect(r.fractionOfLap).toBeCloseTo(0.5, 4);
  });
});
