import type {
  CarModel,
  Contract,
  Driver,
  Team,
  AITeam,
  UsedCarListing,
} from "../types";
import { advanceDriverYear, type RookieSpec } from "./driverLifecycle";
import { generateUsedInventoryByClass } from "./carMarket";
import { runAiSpending } from "./aiSpending";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface YearAdvanceInput {
  /** Full driver pool before advancement. */
  drivers: Driver[];
  /** All active contracts across all teams before advancement. */
  contracts: Contract[];
  /** All 100 teams (player + AI). */
  teams: Team[];
  /** Full car model catalogue. */
  carModels: CarModel[];
  /** Exactly 15 specs for newly generated rookie drivers. */
  rookieSpecs: RookieSpec[];
  /** Whether the player has won the event before (enables F1 in used market). */
  playerHasWon?: boolean;
  /** Generates unique IDs for newly purchased car instances. Must produce distinct values. */
  newCarId: () => string;
}

export interface YearAdvanceResult {
  /** Updated driver pool: aged survivors + 15 new rookies. */
  drivers: Driver[];
  /** Updated contracts: remaining years decremented; expired (0) removed. */
  contracts: Contract[];
  /** Updated teams: all cars aged +1, AI teams have completed spending. */
  teams: Team[];
  /** New used car inventory for the coming year. */
  usedListings: UsedCarListing[];
  /** IDs of drivers retired this year. */
  retiredDriverIds: string[];
}

// ---------------------------------------------------------------------------
// Year advance orchestrator
// ---------------------------------------------------------------------------

/**
 * Advances the world state by one year (GDD §8).
 *
 * Order of operations:
 *   1. Advance driver pool: age all drivers +1, retire bottom 15, add 15 rookies.
 *   2. Decrement contract remaining years by 1; remove contracts that hit 0.
 *   3. Age all car instances by 1 year (across all teams).
 *   4. Generate new used car inventory.
 *   5. Run AI spending for each AI team (greedy algorithm, highest-value-first).
 *
 * Player spending is NOT handled here — the player acts via the UI before this
 * function is called each year.
 *
 * @param input  All world state needed for the advance.
 * @param random Injectable random source (used for driver generation, used inventory, AI spending).
 */
export function advanceYear(
  input: YearAdvanceInput,
  random: () => number,
): YearAdvanceResult {
  const { drivers, contracts, teams, carModels, rookieSpecs, newCarId } = input;
  const playerHasWon = input.playerHasWon ?? false;

  // 1. Advance driver pool
  const driverResult = advanceDriverYear(drivers, rookieSpecs, random);

  // 2. Expire contracts: decrement remainingYears; drop those that reach 0
  const updatedContracts: Contract[] = contracts
    .map((c) => ({ ...c, remainingYears: c.remainingYears - 1 }))
    .filter((c) => c.remainingYears > 0);

  // 3. Age all car instances by 1 year
  const agedTeams: Team[] = teams.map((team) => ({
    ...team,
    cars: team.cars.map((car) => ({ ...car, age: car.age + 1 })),
  }));

  // 4. Generate new used car inventory
  const usedListings = generateUsedInventoryByClass(carModels, playerHasWon, random);

  // 5. Run AI spending for each AI team
  let activeContracts = updatedContracts;
  const finalTeams: Team[] = agedTeams.map((team) => {
    if (team.kind !== "ai") return team; // skip player team

    const result = runAiSpending({
      team: team as AITeam,
      allContracts: activeContracts,
      allDrivers: driverResult.drivers,
      carModels,
      usedListings,
      newCarId,
    });

    // Accumulate new contracts into the shared pool so subsequent AI teams see them
    activeContracts = [...activeContracts, ...result.newContracts];

    return result.updatedTeam;
  });

  return {
    drivers: driverResult.drivers,
    contracts: activeContracts,
    teams: finalTeams,
    usedListings,
    retiredDriverIds: driverResult.retiredIds,
  };
}
