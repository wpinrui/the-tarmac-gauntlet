import type { RaceHistoryEntry } from "../types";
import { buildPrizeSchedule, distributePrizeMoney, PRIZE_TABLE } from "./postRace";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface PostRaceFinancialInput {
  /** Race results: team positions + laps completed. */
  results: { teamId: string; position: number; lapsCompleted: number }[];
  /** Total fuel consumed during the race (litres). */
  playerFuelConsumed: number;
  /** Fuel cost per litre. */
  fuelCostPerLitre: number;
  /** Current game year. */
  year: number;
}

export interface PostRaceFinancialOutput {
  /** Prize money per team: Record<teamId, amount>. */
  prizeMoney: Record<string, number>;
  /** Player's fuel cost (may be written off if insufficient funds). */
  playerFuelCost: number;
  /** Race history entry to append to game state. */
  raceHistoryEntry: RaceHistoryEntry;
}

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

/**
 * Processes all financial outcomes of a completed race.
 *
 * Call sequence after a race ends:
 *   1. Call this function to compute prize money, fuel cost, and race history
 *   2. Call store.awardPrizeMoney for each team
 *   3. Call store.deductFuelCost for the player
 *   4. Push the raceHistoryEntry to gameState.raceHistory
 *
 * This function is pure — it computes but does not mutate state.
 */
export function processPostRaceFinancials(
  input: PostRaceFinancialInput,
): PostRaceFinancialOutput {
  const { results, playerFuelConsumed, fuelCostPerLitre, year } = input;

  // Prize money
  const schedule = buildPrizeSchedule(results.length);
  const prizeMoney = distributePrizeMoney(results, schedule);

  // Fuel cost
  const playerFuelCost = Math.round(playerFuelConsumed * fuelCostPerLitre);

  // Race history entry
  const raceHistoryEntry: RaceHistoryEntry = {
    year,
    results: results.map((r) => ({
      teamId: r.teamId,
      position: r.position,
      lapsCompleted: r.lapsCompleted,
      prizeMoney: prizeMoney[r.teamId] ?? 0,
      retired: r.lapsCompleted < 48, // Retired if didn't complete full race
    })),
    fastestLap: null, // Populated by the race loop, not here
  };

  return { prizeMoney, playerFuelCost, raceHistoryEntry };
}
