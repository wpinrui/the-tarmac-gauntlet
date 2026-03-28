import { describe, it, expect } from "vitest";
import { calculateEffectiveStats } from "./effectiveStats";
import type { CarInstance, CarModel } from "../types";

// A model whose baseStats and potentialStats are easy to reason about.
const model: CarModel = {
  id: "test-model",
  name: "Test Car",
  tier: "trackCar",
  price: 10000,
  baseStats: {
    power: 100,
    handling: 80,
    fuelEfficiency: 60,
    tyreDurability: 70,
    comfort: 50,
    reliability: 90,
    pitStopTime: 40, // lower is better
    fuelCapacity: 120,
  },
  potentialStats: {
    power: 120,
    handling: 100,
    fuelEfficiency: 75,
    tyreDurability: 90,
    comfort: 70,
    reliability: 90, // no pack — equals base
    pitStopTime: 40, // no pack — equals base
    fuelCapacity: 120, // no pack — equals base
  },
  upgradePacks: [
    { type: "power", cost: 5000 },
    { type: "handling", cost: 4000 },
    { type: "comfort", cost: 3000 },
  ],
};

function makeInstance(
  overrides: Partial<CarInstance> = {},
): CarInstance {
  return {
    id: "car-1",
    modelId: "test-model",
    age: 0,
    condition: 100,
    installedUpgrades: { power: false, handling: false, comfort: false },
    ...overrides,
  };
}

describe("calculateEffectiveStats", () => {
  // --- Base stats only ---

  it("returns base stats at age 0, full condition, no upgrades", () => {
    const result = calculateEffectiveStats(makeInstance(), model);
    expect(result.power).toBe(100);
    expect(result.handling).toBe(80);
    expect(result.fuelEfficiency).toBe(60);
    expect(result.tyreDurability).toBe(70);
    expect(result.comfort).toBe(50);
    expect(result.reliability).toBe(90);
    expect(result.pitStopTime).toBe(40); // 40 / 1.0
    expect(result.fuelCapacity).toBe(120);
  });

  // --- Upgrades ---

  it("applies power pack: upgrades power and fuelEfficiency", () => {
    const result = calculateEffectiveStats(
      makeInstance({ installedUpgrades: { power: true, handling: false, comfort: false } }),
      model,
    );
    expect(result.power).toBe(120);
    expect(result.fuelEfficiency).toBe(75);
    // Unaffected stats remain at base
    expect(result.handling).toBe(80);
    expect(result.tyreDurability).toBe(70);
    expect(result.comfort).toBe(50);
  });

  it("applies handling pack: upgrades handling and tyreDurability", () => {
    const result = calculateEffectiveStats(
      makeInstance({ installedUpgrades: { power: false, handling: true, comfort: false } }),
      model,
    );
    expect(result.handling).toBe(100);
    expect(result.tyreDurability).toBe(90);
    expect(result.power).toBe(100); // unchanged
  });

  it("applies comfort pack: upgrades comfort only", () => {
    const result = calculateEffectiveStats(
      makeInstance({ installedUpgrades: { power: false, handling: false, comfort: true } }),
      model,
    );
    expect(result.comfort).toBe(70);
    expect(result.power).toBe(100); // unchanged
  });

  it("all upgrades installed: all upgradeable stats use potential values", () => {
    const result = calculateEffectiveStats(
      makeInstance({ installedUpgrades: { power: true, handling: true, comfort: true } }),
      model,
    );
    expect(result.power).toBe(120);
    expect(result.fuelEfficiency).toBe(75);
    expect(result.handling).toBe(100);
    expect(result.tyreDurability).toBe(90);
    expect(result.comfort).toBe(70);
    // No-pack stats unchanged
    expect(result.reliability).toBe(90);
    expect(result.pitStopTime).toBe(40);
    expect(result.fuelCapacity).toBe(120);
  });

  // --- Age ---

  it("applies a 2% penalty per year of age", () => {
    // age 5 → agePenalty = 0.10 → ageFraction = 0.90; condition = 100% → combined = 0.90
    const result = calculateEffectiveStats(makeInstance({ age: 5 }), model);
    expect(result.power).toBeCloseTo(100 * 0.9);
    expect(result.handling).toBeCloseTo(80 * 0.9);
    // pitStopTime: 40 / 0.90
    expect(result.pitStopTime).toBeCloseTo(40 / 0.9);
  });

  it("caps age penalty at 50% (age 25+)", () => {
    const result25 = calculateEffectiveStats(makeInstance({ age: 25 }), model);
    const result50 = calculateEffectiveStats(makeInstance({ age: 50 }), model);
    // At age 25, penalty is exactly 50%; any older should give the same result.
    expect(result25.power).toBeCloseTo(result50.power);
    expect(result25.power).toBeCloseTo(100 * 0.5);
  });

  it("age 0 produces no age penalty", () => {
    const result = calculateEffectiveStats(makeInstance({ age: 0 }), model);
    expect(result.power).toBe(100);
  });

  // --- Condition ---

  it("condition acts as a direct multiplier on normal stats", () => {
    // condition 50% → multiplier = 0.50
    const result = calculateEffectiveStats(makeInstance({ condition: 50 }), model);
    expect(result.power).toBeCloseTo(100 * 0.5);
    expect(result.reliability).toBeCloseTo(90 * 0.5);
  });

  it("lower condition increases pitStopTime", () => {
    const full = calculateEffectiveStats(makeInstance({ condition: 100 }), model);
    const degraded = calculateEffectiveStats(makeInstance({ condition: 50 }), model);
    expect(degraded.pitStopTime).toBeGreaterThan(full.pitStopTime);
    expect(degraded.pitStopTime).toBeCloseTo(40 / 0.5);
  });

  // --- Edge cases ---

  it("0% condition: all normal stats are 0", () => {
    const result = calculateEffectiveStats(makeInstance({ condition: 0 }), model);
    expect(result.power).toBe(0);
    expect(result.handling).toBe(0);
    expect(result.fuelEfficiency).toBe(0);
    expect(result.tyreDurability).toBe(0);
    expect(result.comfort).toBe(0);
    expect(result.reliability).toBe(0);
    expect(result.fuelCapacity).toBe(0);
  });

  it("0% condition: pitStopTime is capped (does not reach infinity)", () => {
    const result = calculateEffectiveStats(makeInstance({ condition: 0 }), model);
    // combined = 0, clamped to MIN_COMBINED_MULTIPLIER = 0.1 → pitStopTime = 40 / 0.1 = 400
    expect(result.pitStopTime).toBeCloseTo(40 / 0.1);
    expect(Number.isFinite(result.pitStopTime)).toBe(true);
  });

  // --- All factors combined ---

  it("combines upgrades, age, and condition correctly", () => {
    // age 10 → agePenalty = 0.20 → ageFraction = 0.80
    // condition 75% → conditionFraction = 0.75
    // combined = 0.80 * 0.75 = 0.60
    const instance = makeInstance({
      age: 10,
      condition: 75,
      installedUpgrades: { power: true, handling: true, comfort: true },
    });
    const result = calculateEffectiveStats(instance, model);
    const combined = 0.8 * 0.75;

    expect(result.power).toBeCloseTo(120 * combined);
    expect(result.fuelEfficiency).toBeCloseTo(75 * combined);
    expect(result.handling).toBeCloseTo(100 * combined);
    expect(result.tyreDurability).toBeCloseTo(90 * combined);
    expect(result.comfort).toBeCloseTo(70 * combined);
    expect(result.reliability).toBeCloseTo(90 * combined);
    expect(result.pitStopTime).toBeCloseTo(40 / combined);
    expect(result.fuelCapacity).toBeCloseTo(120 * combined);
  });
});
