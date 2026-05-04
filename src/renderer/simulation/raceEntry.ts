import type { GameState, PlayerTeam, Team } from "../types";
import type { CarEntry, RaceDriver } from "./raceLoop";
import { calculateDriverStats } from "./driverLifecycle";
import { calculateEffectiveStats } from "./effectiveStats";

/**
 * Builds a CarEntry for every team that has an entered car and at least one
 * contracted driver. Teams missing either are skipped (defensive — should not
 * happen in fresh games, but avoids crashing simulateRace mid-season).
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

  const teamContracts = game.contracts.filter(
    (c) => c.teamId === team.id && c.remainingYears > 0,
  );
  if (teamContracts.length === 0) return null;

  const drivers: RaceDriver[] = [];
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
    engineerSkill: team.kind === "player" ? (team as PlayerTeam).skills.engineer : 0,
  };
}
