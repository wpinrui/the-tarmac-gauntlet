import type { GameState, PlayerTeam, Team } from "../types";
import type { CarEntry, RaceDriver } from "./raceLoop";
import { calculateDriverStats } from "./driverLifecycle";
import { calculateEffectiveStats } from "./effectiveStats";

/** Stable ID for the player-character driver (the player owner-driver). */
export const PLAYER_CHARACTER_DRIVER_ID = "player-character";

/**
 * Builds a CarEntry for every team that has an entered car. AI teams need at
 * least one active contracted driver; the player team always includes the
 * player character (no contract — stats from skills.driver). Teams that can't
 * be raced are skipped — should not happen in fresh games but avoids crashing
 * simulateRace mid-season.
 *
 * Player and AI cars share the same shape; pitDecider/modeDecider are left
 * undefined so simulateRace falls back to its built-in defaults (Phase 5 will
 * wire the player UI).
 */
export function buildCarEntries(game: GameState): CarEntry[] {
  const entries: CarEntry[] = [];

  for (const team of game.teams) {
    const entry = buildEntryForTeam(team, game);
    if (entry) entries.push(entry);
  }

  return entries;
}

function buildEntryForTeam(team: Team, game: GameState): CarEntry | null {
  if (!team.enteredCarId) return null;

  const car = team.cars.find((c) => c.id === team.enteredCarId);
  if (!car) return null;

  const model = game.carModels.find((m) => m.id === car.modelId);
  if (!model) return null;

  const drivers: RaceDriver[] = [];

  if (team.kind === "player") {
    drivers.push(playerCharacterRaceDriver(team));
  }

  const teamContracts = game.contracts.filter(
    (c) => c.teamId === team.id && c.remainingYears > 0,
  );
  for (const contract of teamContracts) {
    const driver = game.drivers.find((d) => d.id === contract.driverId);
    if (!driver) continue;
    drivers.push({ id: driver.id, stats: calculateDriverStats(driver) });
  }
  if (drivers.length === 0) return null;

  const effective = calculateEffectiveStats(car, model);

  return {
    carId: car.id,
    teamId: team.id,
    instance: car,
    model,
    drivers,
    startingDriverId: drivers[0].id,
    startingFuel: effective.fuelCapacity,
    tyreSetsAvailable: team.tyreSets,
    sparePartsAvailable: team.spareParts,
    crewSize: team.crewSize,
    engineerSkill: team.kind === "player" ? team.skills.engineer : 0,
  };
}

function playerCharacterRaceDriver(player: PlayerTeam): RaceDriver {
  // Per GDD/UI convention, player-character OVR = skills.driver × 5. Phase 1
  // collapses all five DriverStats to that single value; Phase 5 may
  // differentiate (e.g., separate consistency/safety from pace).
  const stat = player.skills.driver * 5;
  return {
    id: PLAYER_CHARACTER_DRIVER_ID,
    stats: { pace: stat, consistency: stat, stamina: stat, safety: stat, smoothness: stat },
  };
}
