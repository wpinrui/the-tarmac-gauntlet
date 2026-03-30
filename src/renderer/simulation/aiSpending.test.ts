import { describe, it, expect } from "vitest";
import { runAiSpending } from "./aiSpending";
import type { AITeam, CarModel, CarInstance, Driver, Contract, UsedCarListing } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

let idCounter = 0;
function nextId(): string {
  return `gen-${++idCounter}`;
}

function makeModel(id: string, price: number, power = 50, handling = 50): CarModel {
  return {
    id,
    name: `Car ${id}`,
    carClass: "B",
    tier: "gtRaceCar",
    price,
    baseStats: {
      power, handling, fuelEfficiency: 50, tyreDurability: 50,
      comfort: 50, reliability: 50, pitStopTime: 50, fuelCapacity: 100,
    },
    potentialStats: {
      power: power + 20, handling: handling + 20, fuelEfficiency: 70, tyreDurability: 70,
      comfort: 70, reliability: 50, pitStopTime: 30, fuelCapacity: 100,
    },
    upgradePacks: [
      { type: "power",    cost: 5_000 },
      { type: "handling", cost: 4_000 },
      { type: "comfort",  cost: 3_000 },
    ],
  };
}

function makeInstance(id: string, modelId: string, power = 50, handling = 50): CarInstance {
  return {
    id,
    modelId,
    age: 0,
    condition: 100,
    installedUpgrades: { power: false, handling: false, comfort: false },
  };
}

function makeAiTeam(
  id: string,
  budget: number,
  cars: CarInstance[] = [],
  crewSize = 0,
  spareParts = 0,
  tyreSets = 0,
): AITeam {
  return {
    kind: "ai",
    id,
    name: `Team ${id}`,
    budget,
    prestige: 0,
    crewSize,
    cars,
    contracts: [],
    enteredCarId: cars[0]?.id ?? null,
    spareParts,
    tyreSets,
  };
}

function makeDriver(id: string, totalStatSum: number): Driver {
  const perStat = totalStatSum / 5;
  return {
    id,
    name: `Driver ${id}`,
    nationality: "gb",
    age: 30, // peak age → stats ≈ perStat
    curveParams: {
      peakAge: 30,
      peakStats: { pace: perStat, consistency: perStat, stamina: perStat, safety: perStat, smoothness: perStat },
      phaseOffsets: { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 },
    },
    marketValue: totalStatSum * 100,
  };
}

function makeContract(driverId: string, teamId: string): Contract {
  return { driverId, teamId, length: 1, remainingYears: 1, annualSalary: 5_000 };
}

// ---------------------------------------------------------------------------
// AI hires drivers to fill 3 slots
// ---------------------------------------------------------------------------

describe("AI driver hiring", () => {
  it("hires drivers to fill empty slots up to 3", () => {
    const team = makeAiTeam("t1", 1_000_000);
    const drivers = [
      makeDriver("d1", 200),
      makeDriver("d2", 150),
      makeDriver("d3", 180),
    ];
    const model = makeModel("m1", 50_000);
    const car = makeInstance("car1", "m1");

    const result = runAiSpending({
      team: { ...team, cars: [car] },
      allContracts: [],
      allDrivers: drivers,
      carModels: [model],
      usedListings: [],
      newCarId: nextId,
    });

    const hiredIds = result.newContracts.map((c) => c.driverId);
    expect(hiredIds).toHaveLength(3);
  });

  it("does not hire more than 3 drivers", () => {
    const team = makeAiTeam("t1", 10_000_000);
    const drivers = Array.from({ length: 10 }, (_, i) => makeDriver(`d${i}`, 200));
    const model = makeModel("m1", 5_000);
    const car = makeInstance("car1", "m1");

    const result = runAiSpending({
      team: { ...team, cars: [car] },
      allContracts: [],
      allDrivers: drivers,
      carModels: [model],
      usedListings: [],
      newCarId: nextId,
    });

    const teamContractedCount = result.newContracts.filter(
      (c) => c.teamId === "t1" && c.remainingYears > 0,
    ).length;
    expect(teamContractedCount).toBeLessThanOrEqual(3);
  });

  it("does not hire when team already has 3 drivers and no budget remains for upgrades", () => {
    const contract1 = makeContract("d1", "t1");
    const contract2 = makeContract("d2", "t1");
    const contract3 = makeContract("d3", "t1");
    const team = makeAiTeam("t1", 0); // no budget

    const result = runAiSpending({
      team,
      allContracts: [contract1, contract2, contract3],
      allDrivers: [makeDriver("d4", 300)],
      carModels: [],
      usedListings: [],
      newCarId: nextId,
    });

    expect(result.newContracts).toHaveLength(0);
  });

  it("budget is reduced by hired drivers' salaries", () => {
    const team = makeAiTeam("t1", 500_000);
    const drivers = [makeDriver("d1", 100)]; // low stats = low salary
    const model = makeModel("m1", 5_000);
    const car = makeInstance("car1", "m1");

    const result = runAiSpending({
      team: { ...team, cars: [car] },
      allContracts: [],
      allDrivers: drivers,
      carModels: [model],
      usedListings: [],
      newCarId: nextId,
    });

    expect(result.updatedTeam.budget).toBeLessThan(500_000);
  });
});

// ---------------------------------------------------------------------------
// AI buys a better car
// ---------------------------------------------------------------------------

describe("AI car purchasing", () => {
  it("buys a better car when it can afford one (new car market)", () => {
    const weakModel = makeModel("weak", 1_000, 30, 30);
    const strongModel = makeModel("strong", 50_000, 80, 80);
    const weakCar = makeInstance("car1", "weak");
    const team = makeAiTeam("t1", 200_000, [weakCar]);
    team.enteredCarId = "car1";

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: [],
      carModels: [weakModel, strongModel],
      usedListings: [],
      newCarId: nextId,
    });

    const newCarModelIds = result.updatedTeam.cars
      .filter((c) => c.id !== "car1")
      .map((c) => c.modelId);
    expect(newCarModelIds).toContain("strong");
  });

  it("does not buy a car if budget is insufficient", () => {
    const cheapModel = makeModel("cheap", 500, 50, 50);
    const expensiveModel = makeModel("expensive", 1_000_000, 90, 90);
    const car = makeInstance("car1", "cheap");
    const team = makeAiTeam("t1", 100, [car]);

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: [],
      carModels: [cheapModel, expensiveModel],
      usedListings: [],
      newCarId: nextId,
    });

    expect(result.updatedTeam.cars).toHaveLength(1);
  });

  it("buys from used car market when used is affordable but new is not", () => {
    const weakModel = makeModel("weak", 1_000, 20, 20);
    const strongModel = makeModel("strong", 500_000, 80, 80);
    const weakCar = makeInstance("car1", "weak");
    const team = makeAiTeam("t1", 30_000, [weakCar]);

    const usedListing: UsedCarListing = {
      id: "used1",
      modelId: "strong",
      age: 5,
      condition: 60,
      installedUpgrades: { power: false, handling: false, comfort: false },
      price: 25_000,
    };

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: [],
      carModels: [weakModel, strongModel],
      usedListings: [usedListing],
      newCarId: nextId,
    });

    const usedCarBought = result.updatedTeam.cars.some(
      (c) => c.modelId === "strong",
    );
    expect(usedCarBought).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// AI applies upgrade packs
// ---------------------------------------------------------------------------

describe("AI upgrade pack purchasing", () => {
  it("applies an affordable upgrade pack to its current car", () => {
    const model = makeModel("m1", 50_000, 50, 50);
    const car = makeInstance("car1", "m1");
    const team = makeAiTeam("t1", 50_000, [car]);

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: [],
      carModels: [model],
      usedListings: [],
      newCarId: nextId,
    });

    const updatedCar = result.updatedTeam.cars.find((c) => c.id === "car1")!;
    const anyUpgradeApplied =
      updatedCar.installedUpgrades.power ||
      updatedCar.installedUpgrades.handling ||
      updatedCar.installedUpgrades.comfort;
    expect(anyUpgradeApplied).toBe(true);
  });

  it("does not apply an upgrade it cannot afford", () => {
    const expensiveModel = makeModel("m1", 50_000);
    // Override pack costs to be very expensive
    expensiveModel.upgradePacks[0] = { type: "power", cost: 1_000_000 };
    expensiveModel.upgradePacks[1] = { type: "handling", cost: 1_000_000 };
    expensiveModel.upgradePacks[2] = { type: "comfort", cost: 1_000_000 };
    const car = makeInstance("car1", "m1");
    const team = makeAiTeam("t1", 100, [car]); // barely any budget

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: [],
      carModels: [expensiveModel],
      usedListings: [],
      newCarId: nextId,
    });

    const updatedCar = result.updatedTeam.cars.find((c) => c.id === "car1")!;
    expect(updatedCar.installedUpgrades.power).toBe(false);
    expect(updatedCar.installedUpgrades.handling).toBe(false);
    expect(updatedCar.installedUpgrades.comfort).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Budget constraint
// ---------------------------------------------------------------------------

describe("AI budget", () => {
  it("never spends more than its budget", () => {
    const model = makeModel("m1", 10_000);
    const car = makeInstance("car1", "m1");
    const team = makeAiTeam("t1", 25_000, [car]);
    const drivers = Array.from({ length: 5 }, (_, i) => makeDriver(`d${i}`, 250));

    const result = runAiSpending({
      team,
      allContracts: [],
      allDrivers: drivers,
      carModels: [model],
      usedListings: [],
      newCarId: nextId,
    });

    expect(result.updatedTeam.budget).toBeGreaterThanOrEqual(0);
  });
});
