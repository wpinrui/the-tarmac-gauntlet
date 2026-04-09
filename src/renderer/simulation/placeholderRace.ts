import type { GameState, RaceHistoryEntry, RaceResult, CarClass } from "../types";
import { buildPrizeSchedule, distributePrizeMoney } from "./postRace";

const DNF_PROBABILITY = 0.12;

/**
 * Generates placeholder race results with random finishing positions and ~12% DNFs.
 * Does NOT call simulateRace — just random positions for the core loop.
 */
export function runPlaceholderRace(game: GameState): {
  raceHistory: RaceHistoryEntry;
  prizeMoney: Record<string, number>;
  fuelCost: number;
} {
  const teams = game.teams;
  const year = game.currentYear;

  // Assign random order
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

  // Prize money
  const schedule = buildPrizeSchedule(shuffled.length);
  const resultsForPrize = shuffled.map((r, i) => ({
    teamId: r.teamId,
    position: i + 1,
    lapsCompleted: r.laps,
  }));
  const prizeMoney = distributePrizeMoney(resultsForPrize, schedule);

  // Build race results
  const results: RaceResult[] = shuffled.map((r, i) => ({
    teamId: r.teamId,
    carId: r.carId,
    carClass: r.carClass,
    position: i + 1,
    lapsCompleted: r.laps,
    prizeMoney: prizeMoney[r.teamId] ?? 0,
    retired: r.retired,
  }));

  const raceHistory: RaceHistoryEntry = {
    year,
    results,
    fastestLap: null,
  };

  // Placeholder fuel cost: ~$200 for the player
  const fuelCost = Math.round(100 + Math.random() * 200);

  return { raceHistory, prizeMoney, fuelCost };
}
