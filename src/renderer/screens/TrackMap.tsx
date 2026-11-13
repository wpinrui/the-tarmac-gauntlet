// Phase 3 track map. Canvas-rendered overhead view of the loop, with one
// disc per car position-derived from the simulation's per-lap snapshots and
// the pacing LUT in `trackPath.ts` (so cars slow into corners and speed up
// on straights). Player car gets a white ring + larger radius until liveries
// land. No internal RAF — redraws on each `elapsedSec` change from the
// parent's `useRaceClock`, which already runs at the project's standard 30 fps.

import { useEffect, useMemo, useRef, useState } from "react";
import type { RaceResultFull } from "../simulation/raceLoop";
import { TOTAL_RACE_SECONDS, wallToSim } from "../simulation/raceClock";
import {
  lapFractionForCar,
  loadTrackPath,
  pointAtLapFraction,
  type TrackPath,
} from "../simulation/trackPath";
import type { Track } from "../types/track";
import trackData from "../assets/track.json";
import { teamColourFromId } from "../shared/teamColour";
import "./TrackMap.scss";

const FIT_PADDING_PX = 20;
const TRACK_STROKE_COLOUR = "#3a5a78";
const TRACK_STROKE_PX = 2.5;
const DISC_RADIUS_PX = 4;
const PLAYER_DISC_RADIUS_PX = 5.5;
const PLAYER_RING_COLOUR = "#ffffff";
const PLAYER_RING_PX = 1.6;

interface Props {
  result: RaceResultFull;
  elapsedSec: number;
  /** Player's entered car. `null` is allowed (no entry / dev fixtures): no ring is drawn. */
  playerCarId: string | null;
  /** carId → teamId. Used for the disc colour hash. */
  carTeamIds: Record<string, string>;
}

export function TrackMap({ result, elapsedSec, playerCarId, carTeamIds }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });

  // Build the track LUTs once. The Bézier sampling + pacing inversion is a few
  // ms; doing it on mount keeps the file pure-functional and trivially testable.
  const track = useMemo<TrackPath>(
    () => loadTrackPath(trackData as unknown as Track),
    [],
  );

  // Track container size for canvas buffer + fit-transform.
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const update = () => {
      const r = el.getBoundingClientRect();
      setSize({ w: Math.round(r.width), h: Math.round(r.height) });
    };
    update();
    if (typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Redraw on every elapsedSec change (and on resize / data swap).
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || size.w === 0 || size.h === 0) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const dpr = typeof window !== "undefined" ? window.devicePixelRatio || 1 : 1;
    canvas.width = Math.round(size.w * dpr);
    canvas.height = Math.round(size.h * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, size.w, size.h);

    // World → screen fit-transform. Padding leaves headroom for the player's
    // larger disc + ring at the loop's outer edges.
    const { bbox } = track.path;
    const bw = bbox.maxX - bbox.minX;
    const bh = bbox.maxY - bbox.minY;
    const scale = Math.min(
      (size.w - 2 * FIT_PADDING_PX) / bw,
      (size.h - 2 * FIT_PADDING_PX) / bh,
    );
    const tx =
      FIT_PADDING_PX +
      (size.w - 2 * FIT_PADDING_PX - bw * scale) / 2 -
      bbox.minX * scale;
    const ty =
      FIT_PADDING_PX +
      (size.h - 2 * FIT_PADDING_PX - bh * scale) / 2 -
      bbox.minY * scale;

    // Track polyline. Drawn each frame — at ~3k points × 30 fps this is well
    // under the rendering budget; offscreen-canvas optimisation can come if
    // a profiler ever flags it.
    ctx.strokeStyle = TRACK_STROKE_COLOUR;
    ctx.lineWidth = TRACK_STROKE_PX;
    ctx.beginPath();
    const pts = track.path.points;
    for (let i = 0; i < pts.length; i++) {
      const sx = pts[i].x * scale + tx;
      const sy = pts[i].y * scale + ty;
      if (i === 0) ctx.moveTo(sx, sy);
      else ctx.lineTo(sx, sy);
    }
    ctx.closePath();
    ctx.stroke();

    const simElapsed = wallToSim(elapsedSec, result, TOTAL_RACE_SECONDS);

    // Discs. Player drawn last so its ring sits above any overlap.
    for (const carId of Object.keys(result.lapSnapshots)) {
      if (carId === playerCarId) continue;
      drawDisc(ctx, carId, false);
    }
    if (playerCarId && result.lapSnapshots[playerCarId]) {
      drawDisc(ctx, playerCarId, true);
    }

    function drawDisc(c2d: CanvasRenderingContext2D, carId: string, isPlayer: boolean) {
      const snaps = result.lapSnapshots[carId];
      if (!snaps) return;
      const { fractionOfLap } = lapFractionForCar(snaps, simElapsed);
      const pos = pointAtLapFraction(track, fractionOfLap);
      const sx = pos.x * scale + tx;
      const sy = pos.y * scale + ty;
      const teamId = carTeamIds[carId] ?? carId;
      c2d.fillStyle = teamColourFromId(teamId);
      const r = isPlayer ? PLAYER_DISC_RADIUS_PX : DISC_RADIUS_PX;
      c2d.beginPath();
      c2d.arc(sx, sy, r, 0, Math.PI * 2);
      c2d.fill();
      if (isPlayer) {
        c2d.strokeStyle = PLAYER_RING_COLOUR;
        c2d.lineWidth = PLAYER_RING_PX;
        c2d.stroke();
      }
    }
  }, [elapsedSec, result, playerCarId, carTeamIds, track, size]);

  return (
    <div className="track-map" ref={containerRef}>
      <canvas ref={canvasRef} className="track-map-canvas" />
    </div>
  );
}
