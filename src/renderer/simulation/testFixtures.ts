import type { CarLapSnapshot } from "./raceLoop";

/**
 * Minimal CarLapSnapshot for tests that only care about lap count and timing.
 * All resource fields are zero-filled — pass real values when a test exercises
 * fuel/wear/condition logic.
 */
export function snap(lapsCompleted: number, totalTime: number): CarLapSnapshot {
  return {
    lapsCompleted,
    totalTime,
    tyreWear: 0,
    fuelRemaining: 0,
    fuelCapacity: 0,
    condition: 100,
    currentDriverId: "d",
    driverFatigue: {},
    instructionMode: "normal",
    activeIssues: [],
    tyreSetsAvailable: 0,
    sparePartsAvailable: 0,
  };
}
