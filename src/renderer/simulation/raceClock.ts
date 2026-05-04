import type { CarLapSnapshot, RaceResultFull } from "./raceLoop";

// GDD §2 / Appendix A: 24 real-time minutes per race. Per-lap pacing is
// derived from `raceSimDuration(result) / leader_total_laps`, never a fixed
// 30s/lap. Tests/dev can pass a smaller `totalRaceSec` to compress playback.
export const TOTAL_RACE_SECONDS = 1440;

/**
 * Sim-time at which P1 completes their final lap. Wall-clock 0..TOTAL_RACE_SECONDS
 * maps linearly onto sim 0..raceSimDuration. Returns 0 if the race had no laps.
 */
export function raceSimDuration(result: RaceResultFull): number {
  const leader = result.results[0];
  if (!leader) return 0;
  const snaps = result.lapSnapshots[leader.carId] ?? [];
  if (snaps.length === 0) return 0;
  return snaps[snaps.length - 1].totalTime;
}

/** Maps wall-clock elapsed to sim-time elapsed. Clamped to [0, raceSimDuration]. */
export function wallToSim(
  wallElapsedSec: number,
  result: RaceResultFull,
  totalRaceSec: number = TOTAL_RACE_SECONDS,
): number {
  const sim = raceSimDuration(result);
  if (totalRaceSec <= 0 || sim <= 0) return 0;
  const ratio = Math.min(1, Math.max(0, wallElapsedSec / totalRaceSec));
  return ratio * sim;
}

/** Largest lap-count for a car whose end-of-lap totalTime is ≤ simElapsedSec. */
export function lapsCompletedAtSim(
  snapshots: CarLapSnapshot[],
  simElapsedSec: number,
): number {
  let laps = 0;
  for (const s of snapshots) {
    if (s.totalTime <= simElapsedSec) laps = s.lapsCompleted;
    else break;
  }
  return laps;
}

/**
 * The leader's lap count at wall-clock elapsed time. The "leader" is whichever
 * car has completed the most laps at that sim moment — not necessarily P1
 * mid-race.
 */
export function leaderLapAt(
  result: RaceResultFull,
  wallElapsedSec: number,
  totalRaceSec: number = TOTAL_RACE_SECONDS,
): number {
  const sim = wallToSim(wallElapsedSec, result, totalRaceSec);
  let max = 0;
  for (const carId of Object.keys(result.lapSnapshots)) {
    const laps = lapsCompletedAtSim(result.lapSnapshots[carId], sim);
    if (laps > max) max = laps;
  }
  return max;
}

/** A specific car's lap count at wall-clock elapsed time. */
export function carLapAt(
  result: RaceResultFull,
  carId: string,
  wallElapsedSec: number,
  totalRaceSec: number = TOTAL_RACE_SECONDS,
): number {
  const snaps = result.lapSnapshots[carId];
  if (!snaps) return 0;
  return lapsCompletedAtSim(snaps, wallToSim(wallElapsedSec, result, totalRaceSec));
}

/**
 * 0..1 fractional progress through the in-progress lap for a car at
 * wall-clock elapsed time. Returns 0 if the car has no further laps.
 * Useful for smooth interpolation of bars / track positions between snapshots.
 */
export function carLapProgressAt(
  result: RaceResultFull,
  carId: string,
  wallElapsedSec: number,
  totalRaceSec: number = TOTAL_RACE_SECONDS,
): number {
  const snaps = result.lapSnapshots[carId];
  if (!snaps || snaps.length === 0) return 0;
  const sim = wallToSim(wallElapsedSec, result, totalRaceSec);
  const lapsDone = lapsCompletedAtSim(snaps, sim);
  if (lapsDone >= snaps.length) return 0;
  const lapStartSim = lapsDone === 0 ? 0 : snaps[lapsDone - 1].totalTime;
  const lapEndSim = snaps[lapsDone].totalTime;
  const span = lapEndSim - lapStartSim;
  if (span <= 0) return 0;
  return Math.min(1, Math.max(0, (sim - lapStartSim) / span));
}

/**
 * Final lap count of the standings winner — the displayed "total laps" for
 * the race. Falls back to 0 if results are empty.
 */
export function leaderTotalLaps(result: RaceResultFull): number {
  return result.results[0]?.lapsCompleted ?? 0;
}
