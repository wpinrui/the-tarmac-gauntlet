import type { GameState, RaceHistoryEntry, CarClass } from "../types";
import { processPostRaceFinancials } from "./postRaceFinancials";

const DNF_PROBABILITY = 0.12;

/**
 * Generates placeholder race results with random finishing positions and ~12% DNFs.
 * Delegates financial processing to processPostRaceFinancials.
 */
export function runPlaceholderRace(game: GameState): {
  raceHistory: RaceHistoryEntry;
  prizeMoney: Record<string, number>;
  fuelCost: number;
} {
  const teams = game.teams;
  const year = game.currentYear;

  // Generate random positions
  const shuffled = teams.map((t) => {
    const car = t.cars.find((c) => c.id === t.enteredCarId);
    const model = car ? game.carModels.find((m) => m.id === car.modelId) : null;
    const retired = Math.random() < DNF_PROBABILITY;
    const laps = retired ? Math.floor(Math.random() * 47) + 1 : 48;
    return {
      teamId: t.id,
      carId: car?.id ?? "",
      carClass: (model?.carClass ?? "F") as CarClass,
      laps,
      retired,
      time: retired ? laps * 30 + Math.random() * 10 : 48 * 30 + Math.random() * 60 - 30,
    };
  });

  // Sort: laps desc, time asc
  shuffled.sort((a, b) => {
    if (b.laps !== a.laps) return b.laps - a.laps;
    return a.time - b.time;
  });

  // Delegate to post-race pipeline
  const results = shuffled.map((r, i) => ({
    teamId: r.teamId,
    carId: r.carId,
    carClass: r.carClass,
    position: i + 1,
    lapsCompleted: r.laps,
    retired: r.retired,
  }));

  const fuelConsumed = Math.round(50 + Math.random() * 100); // placeholder litres
  const { prizeMoney, playerFuelCost, raceHistoryEntry } = processPostRaceFinancials({
    results,
    playerFuelConsumed: fuelConsumed,
    fuelCostPerLitre: game.economyConfig.fuelConfig.costPerLitre,
    year,
  });

  return { raceHistory: raceHistoryEntry, prizeMoney, fuelCost: playerFuelCost };
}
