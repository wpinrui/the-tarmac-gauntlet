import { describe, it, expect } from "vitest";
import { simulateRace } from "./raceLoop";
import type { CarEntry, RaceDriver } from "./raceLoop";
import type { CarModel, CarInstance } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const baseStats = {
  power: 50,
  handling: 50,
  fuelEfficiency: 50,
  tyreDurability: 50,
  comfort: 50,
  reliability: 80,
  pitStopTime: 30,
  fuelCapacity: 200,
};

const testModel: CarModel = {
  id: "test-model",
  name: "Test Car",
  carClass: "C",
  tier: "trackCar",
  price: 50_000,
  baseStats,
  potentialStats: baseStats,
  upgradePacks: [
    { type: "power", cost: 5_000 },
    { type: "handling", cost: 5_000 },
    { type: "comfort", cost: 5_000 },
  ],
};

const baseInstance: CarInstance = {
  id: "car-1",
  modelId: "test-model",
  age: 0,
  condition: 100,
  installedUpgrades: { power: false, handling: false, comfort: false },
};

const safeDriver: RaceDriver = {
  id: "driver-1",
  stats: {
    pace: 50,
    consistency: 50,
    stamina: 80,
    safety: 100, // never crashes
    smoothness: 50,
  },
};

function makeEntry(overrides: Partial<CarEntry> = {}): CarEntry {
  return {
    carId: "car-1",
    instance: { ...baseInstance },
    model: testModel,
    drivers: [safeDriver],
    startingDriverId: "driver-1",
    startingFuel: 200, // full tank — never runs out during a short test race
    tyreSetsAvailable: 10,
    sparePartsAvailable: 50,
    crewSize: 8,
    engineerSkill: 10,
    ...overrides,
  };
}

/** Random that never triggers any probability check (rolls always = 1). */
const neverFire = () => 1;
/** Random that always triggers every probability check (rolls always = 0). */
const alwaysFire = () => 0;

// ---------------------------------------------------------------------------
// Single car completes a race
// ---------------------------------------------------------------------------

describe("single car completes a race", () => {
  it("car with no failures runs all laps and finishes", () => {
    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random: neverFire,
    });
    expect(result.results).toHaveLength(1);
    const car = result.results[0];
    expect(car.lapsCompleted).toBe(48);
    expect(car.retired).toBe(false);
    expect(car.retirementLap).toBeNull();
    expect(car.retirementReason).toBeNull();
    expect(car.finalPosition).toBe(1);
  });

  it("total time is positive and greater than minimum possible (48 × ~26s)", () => {
    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random: neverFire,
    });
    expect(result.results[0].totalTime).toBeGreaterThan(48 * 26);
  });

  it("fastestLap is recorded", () => {
    const result = simulateRace([makeEntry()], {
      totalLaps: 5,
      random: neverFire,
    });
    expect(result.fastestLap).not.toBeNull();
    expect(result.fastestLap!.carId).toBe("car-1");
    expect(result.fastestLap!.lap).toBeGreaterThanOrEqual(1);
    expect(result.fastestLap!.time).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// Pit stops trigger correctly
// ---------------------------------------------------------------------------

describe("pit stops", () => {
  it("player pit decider is called and pit stop duration is added to total time", () => {
    let pitCalled = false;
    const entry = makeEntry({
      pitDecider: (lap) => {
        if (lap === 3) {
          pitCalled = true;
          return {
            fuelToAdd: 50,
            changeTyres: false,
            nextDriverId: null,
            issueIdsToFix: [],
          };
        }
        return null;
      },
    });

    // Baseline: same car, no pit
    const noPitResult = simulateRace([makeEntry()], {
      totalLaps: 5,
      random: neverFire,
    });
    const withPitResult = simulateRace([entry], {
      totalLaps: 5,
      random: neverFire,
    });

    expect(pitCalled).toBe(true);
    // Car that pitted has higher total time (pit stop duration added)
    expect(withPitResult.results[0].totalTime).toBeGreaterThan(
      noPitResult.results[0].totalTime,
    );
  });

  it("forced pit triggers when fuel runs out", () => {
    // Start with only enough fuel for ~1 lap (base consumption ≈ 10 litres at fuelEfficiency=50)
    const entry = makeEntry({
      startingFuel: 5, // well below one lap's consumption
      pitDecider: () => null, // player never pits intentionally
    });

    const result = simulateRace([entry], {
      totalLaps: 5,
      random: neverFire,
    });

    // Car must have pitted for fuel at some point — it wouldn't complete 5 laps otherwise
    expect(result.results[0].lapsCompleted).toBe(5);
    expect(result.results[0].retired).toBe(false);
  });

  it("tyre wear resets to 0 after a tyre change pit stop", () => {
    // Track tyre wear at pit time via snapshot
    let tyreWearAtPit: number | null = null;
    let tyreWearAfterPit: number | null = null;
    let lapAfterPit = false;

    const entry = makeEntry({
      pitDecider: (lap, snapshot) => {
        if (lap === 2) {
          tyreWearAtPit = snapshot.tyreWear;
          return {
            fuelToAdd: 0,
            changeTyres: true,
            nextDriverId: null,
            issueIdsToFix: [],
          };
        }
        if (lap === 3 && tyreWearAtPit !== null && !lapAfterPit) {
          tyreWearAfterPit = snapshot.tyreWear;
          lapAfterPit = true;
        }
        return null;
      },
    });

    simulateRace([entry], { totalLaps: 5, random: neverFire });

    expect(tyreWearAtPit).not.toBeNull();
    expect(tyreWearAtPit).toBeGreaterThan(0); // some wear built up by lap 2
    // After pit + lap 3, tyre wear should be low (fresh tyres + 1 lap of wear)
    expect(tyreWearAfterPit).not.toBeNull();
    expect(tyreWearAfterPit!).toBeLessThan(tyreWearAtPit!);
  });
});

// ---------------------------------------------------------------------------
// Failure stops a car
// ---------------------------------------------------------------------------

describe("failure", () => {
  it("car retires when failure roll fires on lap 1", () => {
    // alwaysFire: first random() call = 0 → failure triggers
    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random: alwaysFire,
    });
    const car = result.results[0];
    expect(car.retired).toBe(true);
    expect(car.retirementLap).toBe(1);
    expect(car.retirementReason).not.toBeNull();
  });

  it("retired car still counts laps completed up to retirement", () => {
    // Fire failure on lap 3 only: skip failure on laps 1-2 (return 1), fire on lap 3 (return 0)
    let callCount = 0;
    const random = () => {
      callCount++;
      // Each lap uses several calls; we want the failure roll (1st per lap) to return 1 for laps 1-2
      // and 0 for lap 3. Track total calls to approximate.
      // Simpler: just count total invocations and fire the 3rd failure-roll.
      // But we don't know exactly which call is the failure roll.
      // Use a lap-based approach by firing after ~10 calls (laps 1-2 each use a few calls).
      return callCount > 10 ? 0 : 1;
    };

    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random,
    });
    const car = result.results[0];
    expect(car.retired).toBe(true);
    expect(car.lapsCompleted).toBeGreaterThanOrEqual(1);
    expect(car.lapsCompleted).toBeLessThan(48);
  });

  it("failure type is 'mechanical' or 'crash'", () => {
    // alwaysFire guarantees failure; all calls return 0, so type roll = 0 < CRASH_PROBABILITY → "crash".
    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random: alwaysFire,
    });
    expect(["mechanical", "crash"]).toContain(result.results[0].retirementReason);
  });

  it("retired car has no more laps added after retirement", () => {
    const result = simulateRace([makeEntry()], {
      totalLaps: 48,
      random: alwaysFire,
    });
    const car = result.results[0];
    expect(car.lapsCompleted).toBe(car.retirementLap);
  });
});

// ---------------------------------------------------------------------------
// Standings are correct
// ---------------------------------------------------------------------------

describe("standings", () => {
  it("faster car finishes in position 1", () => {
    const fastCar = makeEntry({
      carId: "fast",
      model: {
        ...testModel,
        baseStats: { ...baseStats, power: 90, handling: 90 },
        potentialStats: { ...baseStats, power: 90, handling: 90 },
      },
    });
    const slowCar: CarEntry = {
      ...makeEntry({ carId: "slow" }),
      model: {
        ...testModel,
        baseStats: { ...baseStats, power: 10, handling: 10 },
        potentialStats: { ...baseStats, power: 10, handling: 10 },
      },
    };

    const result = simulateRace([slowCar, fastCar], {
      totalLaps: 5,
      random: neverFire,
    });

    const fastResult = result.results.find((r) => r.carId === "fast")!;
    const slowResult = result.results.find((r) => r.carId === "slow")!;

    expect(fastResult.finalPosition).toBe(1);
    expect(slowResult.finalPosition).toBe(2);
    expect(fastResult.totalTime).toBeLessThan(slowResult.totalTime);
  });

  it("car with more laps beats car that retired early", () => {
    const finisher = makeEntry({ carId: "finisher" });
    const retiree = makeEntry({ carId: "retiree" });

    // Cars are processed in order [finisher, retiree] each lap.
    // Each car uses exactly 2 random calls per lap with empty issueTemplates:
    //   call 1: calculateLapTime variance, call 2: rollLapRisks failure roll.
    // So lap 1 calls: n=1 (finisher lapTime), n=2 (finisher failure),
    //                 n=3 (retiree lapTime),  n=4 (retiree failure) ← fire this one.
    let n = 0;
    const result = simulateRace([finisher, retiree], {
      totalLaps: 5,
      random: () => {
        n++;
        return n === 4 ? 0 : 1; // fire only retiree's lap-1 failure roll
      },
    });

    const finisherResult = result.results.find((r) => r.carId === "finisher")!;
    const retireeResult = result.results.find((r) => r.carId === "retiree")!;

    expect(finisherResult.lapsCompleted).toBeGreaterThan(retireeResult.lapsCompleted);
    expect(finisherResult.finalPosition).toBeLessThan(retireeResult.finalPosition);
  });

  it("positions are unique and contiguous starting from 1", () => {
    const cars = [
      makeEntry({ carId: "a" }),
      makeEntry({ carId: "b" }),
      makeEntry({ carId: "c" }),
    ];
    const result = simulateRace(cars, { totalLaps: 3, random: neverFire });
    const positions = result.results.map((r) => r.finalPosition).sort((a, b) => a - b);
    expect(positions).toEqual([1, 2, 3]);
  });
});

// ---------------------------------------------------------------------------
// AI cars pit on thresholds
// ---------------------------------------------------------------------------

describe("AI pit strategy", () => {
  it("AI car pits when tyre wear exceeds threshold", () => {
    // Start with tyre wear just below threshold — one lap of wear should push it over
    // BASE_TYRE_WEAR_PER_LAP = 20; at stats 50/50 the statFactor ≈ 0.75, so ~15/lap.
    // Start at 70 so one lap → ~85 > threshold of 80.
    const entry = makeEntry({
      carId: "ai-car",
      startingTyreWear: 70,
      tyreSetsAvailable: 3,
    });
    // No pitDecider → AI strategy

    // We can observe the pit occurred by checking that total time is higher (pit stop duration added)
    const noPitEntry = makeEntry({ carId: "no-pit", startingTyreWear: 0 });
    const aiResult = simulateRace([entry], { totalLaps: 3, random: neverFire });
    const noPitResult = simulateRace([noPitEntry], { totalLaps: 3, random: neverFire });

    // AI car pitted (tyre reset) → more time
    expect(aiResult.results[0].totalTime).toBeGreaterThan(noPitResult.results[0].totalTime);
  });

  it("AI car pits when fuel drops below threshold fraction of capacity", () => {
    // fuelCapacity = 200, threshold = 0.15 → pit when < 30 litres
    // Start with 25 litres (below threshold from lap 1)
    const entry = makeEntry({
      carId: "low-fuel",
      startingFuel: 25,
      tyreSetsAvailable: 0, // no tyres — only fuel triggers the pit
      startingTyreWear: 0,
    });

    const baseline = makeEntry({
      carId: "full-fuel",
      startingFuel: 200,
    });

    const lowFuelResult = simulateRace([entry], { totalLaps: 3, random: neverFire });
    const baselineResult = simulateRace([baseline], { totalLaps: 3, random: neverFire });

    // Low-fuel car pits → higher total time
    expect(lowFuelResult.results[0].totalTime).toBeGreaterThan(
      baselineResult.results[0].totalTime,
    );
    // But car completes all laps despite low starting fuel
    expect(lowFuelResult.results[0].lapsCompleted).toBe(3);
  });

  it("AI car rotates drivers at pit stop", () => {
    const driver1: RaceDriver = {
      id: "d1",
      stats: { pace: 50, consistency: 50, stamina: 80, safety: 100, smoothness: 50 },
    };
    const driver2: RaceDriver = {
      id: "d2",
      stats: { pace: 50, consistency: 50, stamina: 80, safety: 100, smoothness: 50 },
    };

    // Start with high tyre wear so AI pits on lap 1, swapping to d2
    let modeAfterPit: string | null = null;
    const entry: CarEntry = {
      ...makeEntry({
        carId: "rotating",
        startingTyreWear: 85, // immediately above threshold
        tyreSetsAvailable: 2,
      }),
      drivers: [driver1, driver2],
      startingDriverId: "d1",
      // Observe the current driver via modeDecider (called post-pit with updated state)
      modeDecider: (_lap, snapshot) => {
        modeAfterPit = snapshot.currentDriverId;
        return "normal";
      },
    };

    simulateRace([entry], { totalLaps: 2, random: neverFire });

    // After pit on lap 1, driver should have rotated to d2
    expect(modeAfterPit).toBe("d2");
  });
});

// ---------------------------------------------------------------------------
// Instruction mode
// ---------------------------------------------------------------------------

describe("instruction mode", () => {
  it("pit stop resets instruction mode to 'normal'", () => {
    let modeAfterPit: string | null = null;
    const entry = makeEntry({
      modeDecider: () => "push",
      pitDecider: (lap, snapshot) => {
        if (lap === 2) {
          return {
            fuelToAdd: 10,
            changeTyres: false,
            nextDriverId: null,
            issueIdsToFix: [],
          };
        }
        // On lap 3: observe mode (which should be normal after pit)
        if (lap === 3) modeAfterPit = snapshot.instructionMode;
        return null;
      },
    });

    simulateRace([entry], { totalLaps: 4, random: neverFire });
    expect(modeAfterPit).toBe("normal");
  });

  it("modeDecider applies to next lap, not current", () => {
    const modesUsed: string[] = [];
    const entry = makeEntry({
      modeDecider: (lap) => (lap === 1 ? "push" : "normal"),
      pitDecider: (_lap, snapshot) => {
        modesUsed.push(snapshot.instructionMode);
        return null;
      },
    });

    simulateRace([entry], { totalLaps: 3, random: neverFire });

    // Lap 1 runs in normal (start), modeDecider returns push → lap 2 runs in push
    // The snapshot at end of lap 2 shows the mode LAP 2 ran in = push
    expect(modesUsed[1]).toBe("push");
  });
});
