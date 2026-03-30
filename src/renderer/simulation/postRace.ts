import type { PrizeScheduleEntry } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Prize money (hardcoded table, total = $15,000,000) ---
/** Prize amounts indexed by position (0 = P1, 99 = P100). */
export const PRIZE_TABLE: readonly number[] = [
  750000,700000,675000,650000,620000,595000,570000,545000,520000,500000,
  480000,460000,440000,420000,400000,380000,365000,345000,330000,315000,
  300000,285000,270000,260000,245000,230000,220000,210000,195000,180000,
  175000,165000,155000,145000,135000,130000,120000,115000,105000,100000,
  95000,90000,83000,77000,71000,66000,62000,59000,54000,50000,
  46000,42000,39000,35000,32000,30000,27000,25000,22000,19500,
  19000,17000,16000,14000,13000,11000,10000,9000,8000,7000,
  6500,5500,5000,4000,3500,3000,3000,2500,2000,2000,
  1500,1500,1000,1000,1000,800,750,650,600,600,
  550,550,500,500,500,500,500,500,500,500,
];

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
 * Returns the prize schedule from the hardcoded PRIZE_TABLE.
 * The table has exactly 100 entries totalling $15,000,000.
 *
 * @param numCars  Total number of cars in the race (typically 100). Entries beyond the table length get $0.
 */
export function buildPrizeSchedule(numCars: number): PrizeScheduleEntry[] {
  const schedule: PrizeScheduleEntry[] = [];
  for (let pos = 1; pos <= numCars; pos++) {
    const amount = pos <= PRIZE_TABLE.length ? PRIZE_TABLE[pos - 1] : 0;
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
