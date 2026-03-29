import { describe, it, expect } from "vitest";
import { advanceYear } from "./yearAdvance";
import type {
  AITeam, CarModel, CarInstance, Contract, Driver, PlayerTeam, Team,
} from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

let idSeq = 0;
function nextId(): string {
  return `gen-${++idSeq}`;
}

function makeModel(id: string, price: number): CarModel {
  return {
    id,
    name: `Car ${id}`,
    carClass: "B",
    tier: "gtRaceCar",
    price,
    baseStats: {
      power: 50, handling: 50, fuelEfficiency: 50, tyreDurability: 50,
      comfort: 50, reliability: 50, pitStopTime: 50, fuelCapacity: 100,
    },
    potentialStats: {
      power: 70, handling: 70, fuelEfficiency: 70, tyreDurability: 70,
      comfort: 70, reliability: 50, pitStopTime: 30, fuelCapacity: 100,
    },
    upgradePacks: [
      { type: "power",    cost: 5_000 },
      { type: "handling", cost: 4_000 },
      { type: "comfort",  cost: 3_000 },
    ],
  };
}

function makeCarInstance(id: string, modelId: string, age = 0): CarInstance {
  return {
    id, modelId, age, condition: 100,
    installedUpgrades: { power: false, handling: false, comfort: false },
  };
}

function makeDriver(id: string, totalStats: number): Driver {
  const s = totalStats / 5;
  return {
    id,
    name: `Driver ${id}`,
    age: 30,
    curveParams: {
      peakAge: 30,
      peakStats: { pace: s, consistency: s, stamina: s, safety: s, smoothness: s },
      phaseOffsets: { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 },
    },
    marketValue: totalStats * 100,
  };
}

function makeAiTeam(id: string, budget: number, car?: CarInstance): AITeam {
  return {
    kind: "ai", id, name: `Team ${id}`, budget,
    prestige: 0, crewSize: 0,
    cars: car ? [car] : [],
    contracts: [],
    enteredCarId: car?.id ?? null,
    spareParts: 0, tyreSets: 0,
  };
}

function makePlayerTeam(id: string): PlayerTeam {
  const car = makeCarInstance("player-car", "m1", 0);
  return {
    kind: "player", id, name: "Player Team", budget: 10_000,
    prestige: 0, crewSize: 2,
    cars: [car], contracts: [], enteredCarId: car.id,
    playerName: "Ivan", logo: null,
    skills: { driver: 5, engineer: 5, business: 5 },
    spareParts: 10, tyreSets: 5,
  };
}

/** Returns 15 driver specs for rookie generation. */
function rookieSpecs() {
  return Array.from({ length: 15 }, (_, i) => ({ id: `rookie-${i}`, name: `Rookie ${i}` }));
}

/** Deterministic random: always returns 0.5. */
const stable = () => 0.5;

// ---------------------------------------------------------------------------
// Driver pool advancement
// ---------------------------------------------------------------------------

describe("yearAdvance — driver pool", () => {
  it("all surviving drivers are 1 year older", () => {
    const drivers = Array.from({ length: 30 }, (_, i) => makeDriver(`d${i}`, 200));
    const model = makeModel("m1", 10_000);
    const result = advanceYear(
      { drivers, contracts: [], teams: [], carModels: [model], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );
    // New rookies are always 18; surviving drivers should all be 31
    const survivors = result.drivers.filter((d) => !d.id.startsWith("rookie-"));
    for (const d of survivors) {
      expect(d.age).toBe(31);
    }
  });

  it("returns 15 retired driver IDs", () => {
    const drivers = Array.from({ length: 30 }, (_, i) => makeDriver(`d${i}`, 200 + i));
    const result = advanceYear(
      { drivers, contracts: [], teams: [], carModels: [], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );
    expect(result.retiredDriverIds).toHaveLength(15);
  });

  it("pool size stays constant (15 retired + 15 added)", () => {
    const drivers = Array.from({ length: 40 }, (_, i) => makeDriver(`d${i}`, 200 + i));
    const result = advanceYear(
      { drivers, contracts: [], teams: [], carModels: [], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );
    expect(result.drivers).toHaveLength(40);
  });
});

// ---------------------------------------------------------------------------
// Contract expiry
// ---------------------------------------------------------------------------

describe("yearAdvance — contract expiry", () => {
  it("contracts with 1 remaining year are removed after advance", () => {
    const expiringContract: Contract = {
      driverId: "d1", teamId: "t1", length: 1, remainingYears: 1, annualSalary: 5_000,
    };
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 200 + i * 5));
    const result = advanceYear(
      { drivers, contracts: [expiringContract], teams: [], carModels: [], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );
    const found = result.contracts.find((c) => c.driverId === "d1" && c.teamId === "t1");
    expect(found).toBeUndefined();
  });

  it("contracts with 2 remaining years have 1 remaining year after advance", () => {
    const longContract: Contract = {
      driverId: "d2", teamId: "t2", length: 2, remainingYears: 2, annualSalary: 10_000,
    };
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 200 + i * 5));
    const result = advanceYear(
      { drivers, contracts: [longContract], teams: [], carModels: [], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );
    const updated = result.contracts.find((c) => c.driverId === "d2");
    expect(updated?.remainingYears).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// Car aging
// ---------------------------------------------------------------------------

describe("yearAdvance — car aging", () => {
  it("all car instances are 1 year older after advance", () => {
    const model = makeModel("m1", 10_000);
    const car = makeCarInstance("car1", "m1", 2); // age 2
    const team = makeAiTeam("t1", 0, car);
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 200 + i * 5));

    const result = advanceYear(
      { drivers, contracts: [], teams: [team], carModels: [model], rookieSpecs: rookieSpecs(), newCarId: nextId },
      stable,
    );

    const updatedTeam = result.teams[0];
    expect(updatedTeam.cars[0].age).toBe(3);
  });
});

// ---------------------------------------------------------------------------
// Used car inventory
// ---------------------------------------------------------------------------

describe("yearAdvance — used car inventory", () => {
  it("second-hand dealer generates valid inventory", () => {
    const models = [makeModel("m1", 20_000), makeModel("m2", 100_000)];
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 200 + i * 5));

    const result = advanceYear(
      {
        drivers, contracts: [], teams: [], carModels: models,
        rookieSpecs: rookieSpecs(), usedInventoryCount: 10, newCarId: nextId,
      },
      stable,
    );

    expect(result.usedListings).toHaveLength(10);
    for (const listing of result.usedListings) {
      expect(listing.age).toBeGreaterThanOrEqual(1);
      expect(listing.condition).toBeGreaterThanOrEqual(30);
      expect(listing.price).toBeGreaterThan(0);
    }
  });
});

// ---------------------------------------------------------------------------
// AI spending integration
// ---------------------------------------------------------------------------

describe("yearAdvance — AI spending", () => {
  it("AI team hires drivers after year advance when it has budget", () => {
    const model = makeModel("m1", 5_000);
    const car = makeCarInstance("ai-car", "m1", 0);
    const aiTeam = makeAiTeam("ai1", 200_000, car);
    const drivers = Array.from({ length: 5 }, (_, i) => makeDriver(`d${i}`, 200));

    const result = advanceYear(
      {
        drivers, contracts: [], teams: [aiTeam], carModels: [model],
        rookieSpecs: rookieSpecs(), newCarId: nextId,
      },
      stable,
    );

    const newAiContracts = result.contracts.filter((c) => c.teamId === "ai1");
    expect(newAiContracts.length).toBeGreaterThan(0);
  });

  it("player team is not modified by AI spending step", () => {
    const model = makeModel("m1", 5_000);
    const playerTeam = makePlayerTeam("player1");
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 200 + i * 5));

    const result = advanceYear(
      {
        drivers, contracts: [], teams: [playerTeam], carModels: [model],
        rookieSpecs: rookieSpecs(), newCarId: nextId,
      },
      stable,
    );

    const resultPlayer = result.teams.find((t) => t.id === "player1")!;
    // Budget should be unchanged (player spending not handled here)
    expect(resultPlayer.budget).toBe(playerTeam.budget);
  });
});

// ---------------------------------------------------------------------------
// Full-field smoke test
// ---------------------------------------------------------------------------

describe("yearAdvance — full 100-team state", () => {
  it("runs without errors on a full 100-team state", () => {
    const models = Array.from({ length: 5 }, (_, i) => makeModel(`m${i}`, (i + 1) * 20_000));
    const drivers = Array.from({ length: 310 }, (_, i) => makeDriver(`d${i}`, 100 + (i % 400)));
    const playerTeam = makePlayerTeam("player");
    const aiTeams: AITeam[] = Array.from({ length: 99 }, (_, i) => {
      const car = makeCarInstance(`car-ai-${i}`, `m${i % 5}`, i % 5);
      return makeAiTeam(`ai-${i}`, 50_000 + i * 1_000, car);
    });
    const teams: Team[] = [playerTeam, ...aiTeams];
    const specs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}` }));

    expect(() => {
      advanceYear(
        { drivers, contracts: [], teams, carModels: models, rookieSpecs: specs, newCarId: nextId },
        stable,
      );
    }).not.toThrow();
  });
});
