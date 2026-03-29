import type { PrizeScheduleEntry } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Prize money ---
/** Prize awarded to the last-place finisher (minimum non-zero payout). */
const MIN_PRIZE = 500;
/** Prize awarded to the race winner. GDD §7: ~$400,000–$500,000. */
const MAX_PRIZE = 450_000;
/**
 * Exponent for the prize curve.
 * > 1 concentrates reward at the top — top positions pull away sharply.
 * Exponent 2 (quadratic): P10 ≈ $372k, P50 ≈ $115k, P99 ≈ $546.
 */
const PRIZE_CURVE_EXPONENT = 2;

// --- Prestige ---
/**
 * Number of most-recent races that receive a boosted weight.
 * GDD §8: "recency bias toward the past 5 races."
 */
const RECENT_RACE_COUNT = 5;
/** Weight applied to each of the most-recent races. */
const RECENT_WEIGHT = 2.0;
/** Weight applied to all older races (beyond RECENT_RACE_COUNT). */
const BASE_WEIGHT = 1.0;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * A single past race result for one team, used as input to prestige calculation.
 * Pass results ordered newest-first (index 0 = most recent race).
 */
export interface PastRaceResult {
  /** 1-indexed finishing position. */
  position: number;
  /** Total number of cars in the race (used to normalise the position score). */
  totalCars: number;
}

// ---------------------------------------------------------------------------
// Prize money
// ---------------------------------------------------------------------------

/**
 * Builds a prize schedule for a race with `numCars` cars (GDD §7).
 *
 * The schedule is a continuous curve — top positions earn dramatically more
 * than midfield, reflecting real endurance racing prize structures.
 *
 * Formula:
 *   amount(pos) = MIN_PRIZE + (MAX_PRIZE − MIN_PRIZE) × ((numCars − pos) / (numCars − 1))^EXPONENT
 *
 * Cars with 0 laps completed are excluded from payout entirely by `distributePrizeMoney`;
 * the schedule itself covers all 1..numCars positions.
 *
 * The returned array is the authoritative source shown to the player during
 * race preparation (GDD §7: "The complete prize money schedule is always visible").
 *
 * @param numCars  Total number of cars in the race (typically 100).
 */
export function buildPrizeSchedule(numCars: number): PrizeScheduleEntry[] {
  if (numCars === 1) {
    return [{ position: 1, amount: MAX_PRIZE }];
  }
  const schedule: PrizeScheduleEntry[] = [];
  for (let pos = 1; pos <= numCars; pos++) {
    const fraction = (numCars - pos) / (numCars - 1);
    const amount = Math.round(
      MIN_PRIZE + (MAX_PRIZE - MIN_PRIZE) * Math.pow(fraction, PRIZE_CURVE_EXPONENT),
    );
    schedule.push({ position: pos, amount });
  }
  return schedule;
}

/**
 * Distributes prize money to all teams based on their finishing position (GDD §7).
 *
 * Rules:
 *   - Cars that completed 0 laps receive $0 (GDD §7: "prize money eligibility: all cars with ≥1 lap").
 *   - All other cars receive the prize for their finishing position from the schedule.
 *   - If a position has no matching schedule entry (e.g., fewer cars than schedule entries),
 *     the car receives $0.
 *
 * @param results  Per-car race outcomes. Each entry must have a unique position.
 * @param schedule Prize schedule (from `buildPrizeSchedule` or `economyConfig.prizeSchedule`).
 * @returns        Map of teamId → prize money awarded (in dollars).
 */
export function distributePrizeMoney(
  results: { teamId: string; position: number; lapsCompleted: number }[],
  schedule: PrizeScheduleEntry[],
): Record<string, number> {
  const scheduleMap = new Map(schedule.map((e) => [e.position, e.amount]));
  const prizes: Record<string, number> = {};

  for (const { teamId, position, lapsCompleted } of results) {
    if (lapsCompleted === 0) {
      prizes[teamId] = 0;
    } else {
      prizes[teamId] = scheduleMap.get(position) ?? 0;
    }
  }

  return prizes;
}

// ---------------------------------------------------------------------------
// Team prestige
// ---------------------------------------------------------------------------

/**
 * Calculates a team's prestige score from its full race history (GDD §8).
 *
 * Prestige is a normalised 0–100 score (0 = always last, 100 = always first).
 * It is a weighted average of per-race position scores, where the most recent
 * RECENT_RACE_COUNT races carry extra weight (recency bias).
 *
 * Position score per race:
 *   score = (totalCars − position) / (totalCars − 1) × 100
 *   → 100 for the winner, 0 for last place, linear between.
 *
 * Weight per race (results[0] = most recent):
 *   i < RECENT_RACE_COUNT  → RECENT_WEIGHT
 *   i ≥ RECENT_RACE_COUNT  → BASE_WEIGHT
 *
 * An empty history (year-1 team, no races yet) returns 0.
 *
 * @param results  All past race results for this team, newest-first.
 */
export function calculatePrestige(results: PastRaceResult[]): number {
  if (results.length === 0) return 0;

  let weightedSum = 0;
  let totalWeight = 0;

  for (let i = 0; i < results.length; i++) {
    const { position, totalCars } = results[i];
    const positionScore =
      totalCars > 1 ? ((totalCars - position) / (totalCars - 1)) * 100 : 100;
    const weight = i < RECENT_RACE_COUNT ? RECENT_WEIGHT : BASE_WEIGHT;
    weightedSum += positionScore * weight;
    totalWeight += weight;
  }

  return weightedSum / totalWeight;
}
