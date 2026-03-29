import { describe, it, expect } from "vitest";
import {
  updateTyreWear,
  updateFuelLevel,
  updateDriverFatigue,
  updateCarCondition,
} from "./degradation";

// ---------------------------------------------------------------------------
// updateTyreWear
// ---------------------------------------------------------------------------

describe("updateTyreWear", () => {
  it("increases tyre wear each lap on Normal mode", () => {
    const next = updateTyreWear(0, 50, 50, "normal");
    expect(next).toBeGreaterThan(0);
  });

  it("Push mode wears tyres faster than Normal; Conserve wears them slower", () => {
    const push = updateTyreWear(0, 50, 50, "push");
    const normal = updateTyreWear(0, 50, 50, "normal");
    const conserve = updateTyreWear(0, 50, 50, "conserve");
    expect(push).toBeGreaterThan(normal);
    expect(normal).toBeGreaterThan(conserve);
  });

  it("higher Tyre Durability reduces wear rate", () => {
    const highDurability = updateTyreWear(0, 100, 50, "normal");
    const lowDurability = updateTyreWear(0, 0, 50, "normal");
    expect(highDurability).toBeLessThan(lowDurability);
  });

  it("higher driver Smoothness reduces wear rate", () => {
    const smooth = updateTyreWear(0, 50, 100, "normal");
    const rough = updateTyreWear(0, 50, 0, "normal");
    expect(smooth).toBeLessThan(rough);
  });

  it("clamps to 100 when wear would exceed maximum", () => {
    expect(updateTyreWear(95, 0, 0, "push")).toBe(100);
  });

  it("returns current value when already at 100 (fully worn)", () => {
    expect(updateTyreWear(100, 50, 50, "normal")).toBe(100);
  });

  it("wear rate is positive even with maximum stats", () => {
    // Tyres should always wear — even the best car+driver combination degrades them.
    expect(updateTyreWear(0, 100, 100, "conserve")).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// updateFuelLevel
// ---------------------------------------------------------------------------

describe("updateFuelLevel", () => {
  it("decreases fuel level each lap on Normal mode", () => {
    const next = updateFuelLevel(100, 50, "normal");
    expect(next).toBeLessThan(100);
  });

  it("Push mode consumes more fuel than Normal; Conserve consumes less", () => {
    const startFuel = 100;
    const push = updateFuelLevel(startFuel, 50, "push");
    const normal = updateFuelLevel(startFuel, 50, "normal");
    const conserve = updateFuelLevel(startFuel, 50, "conserve");
    expect(push).toBeLessThan(normal);
    expect(normal).toBeLessThan(conserve);
  });

  it("higher Fuel Efficiency reduces consumption per lap", () => {
    const efficient = updateFuelLevel(100, 100, "normal");
    const inefficient = updateFuelLevel(100, 0, "normal");
    expect(efficient).toBeGreaterThan(inefficient);
  });

  it("clamps to 0 when fuel would go below empty", () => {
    expect(updateFuelLevel(5, 0, "push")).toBe(0);
  });

  it("stays at 0 when already empty", () => {
    expect(updateFuelLevel(0, 50, "normal")).toBe(0);
  });

  it("fuel is always consumed even at maximum efficiency", () => {
    // The car always burns some fuel.
    expect(updateFuelLevel(100, 100, "conserve")).toBeLessThan(100);
  });
});

// ---------------------------------------------------------------------------
// updateDriverFatigue
// ---------------------------------------------------------------------------

describe("updateDriverFatigue", () => {
  it("increases fatigue each lap on Normal mode", () => {
    const next = updateDriverFatigue(0, 50, 50, "normal");
    expect(next).toBeGreaterThan(0);
  });

  it("Push mode builds fatigue faster than Normal; Conserve slower", () => {
    const push = updateDriverFatigue(0, 50, 50, "push");
    const normal = updateDriverFatigue(0, 50, 50, "normal");
    const conserve = updateDriverFatigue(0, 50, 50, "conserve");
    expect(push).toBeGreaterThan(normal);
    expect(normal).toBeGreaterThan(conserve);
  });

  it("higher car Comfort reduces fatigue buildup", () => {
    const comfortable = updateDriverFatigue(0, 100, 50, "normal");
    const uncomfortable = updateDriverFatigue(0, 0, 50, "normal");
    expect(comfortable).toBeLessThan(uncomfortable);
  });

  it("higher driver Stamina reduces fatigue buildup", () => {
    const highStamina = updateDriverFatigue(0, 50, 100, "normal");
    const lowStamina = updateDriverFatigue(0, 50, 0, "normal");
    expect(highStamina).toBeLessThan(lowStamina);
  });

  it("clamps to 100 when fatigue would exceed maximum", () => {
    expect(updateDriverFatigue(98, 0, 0, "push")).toBe(100);
  });

  it("stays at 100 when already fully fatigued", () => {
    expect(updateDriverFatigue(100, 50, 50, "normal")).toBe(100);
  });

  it("fatigue builds even with maximum comfort and stamina", () => {
    expect(updateDriverFatigue(0, 100, 100, "conserve")).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// updateCarCondition
// ---------------------------------------------------------------------------

describe("updateCarCondition", () => {
  it("decreases condition each lap on Normal mode", () => {
    const next = updateCarCondition(100, 0, 0, "normal");
    expect(next).toBeLessThan(100);
  });

  it("Push mode degrades condition faster than Normal; Conserve slower", () => {
    const push = updateCarCondition(100, 0, 0, "push");
    const normal = updateCarCondition(100, 0, 0, "normal");
    const conserve = updateCarCondition(100, 0, 0, "conserve");
    expect(push).toBeLessThan(normal);
    expect(normal).toBeLessThan(conserve);
  });

  it("older cars degrade faster", () => {
    const newCar = updateCarCondition(100, 0, 0, "normal");
    const oldCar = updateCarCondition(100, 20, 0, "normal");
    expect(oldCar).toBeLessThan(newCar);
  });

  it("higher Engineer skill slows condition decay", () => {
    const highSkill = updateCarCondition(100, 5, 20, "normal");
    const noSkill = updateCarCondition(100, 5, 0, "normal");
    expect(highSkill).toBeGreaterThan(noSkill);
  });

  it("clamps to 0 when condition would go below zero", () => {
    expect(updateCarCondition(0.5, 20, 0, "push")).toBe(0);
  });

  it("stays at 0 when already fully degraded", () => {
    expect(updateCarCondition(0, 0, 0, "normal")).toBe(0);
  });

  it("condition always decays — even at age 0 with max Engineer skill", () => {
    expect(updateCarCondition(100, 0, 20, "conserve")).toBeLessThan(100);
  });

  it("edge case: age 0, no engineer skill, Normal mode stays in valid range", () => {
    const result = updateCarCondition(100, 0, 0, "normal");
    expect(result).toBeGreaterThanOrEqual(0);
    expect(result).toBeLessThanOrEqual(100);
  });
});
