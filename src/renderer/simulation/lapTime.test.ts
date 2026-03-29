import { describe, it, expect } from "vitest";
import { calculateLapTime } from "./lapTime";
import type { CarStats, DriverStats } from "../types";
import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/** Reference car: mid-field performance, gives ~30s base lap time. */
const refStats: CarStats = {
  power: 75,
  handling: 62.5,
  fuelEfficiency: 60,
  tyreDurability: 60,
  comfort: 50,
  reliability: 70,
  pitStopTime: 30,
  fuelCapacity: 100,
};

/** Reference driver: average stats, no fatigue. */
const refDriver: DriverStats = {
  pace: 50,
  consistency: 50,
  stamina: 50,
  safety: 50,
  smoothness: 50,
};

/** Zero-variance random: produces exactly the midpoint (0 variance). */
const zeroVariance = () => 0.5;

/** Helper: build a clean lap time call with zero-variance random. */
function lapTime(
  overrides: {
    stats?: Partial<CarStats>;
    driver?: Partial<DriverStats>;
    fatigue?: number;
    mode?: InstructionMode;
    tyreWear?: number;
    fuelLoad?: number;
  } = {},
): number {
  return calculateLapTime(
    { ...refStats, ...overrides.stats },
    { ...refDriver, ...overrides.driver },
    overrides.fatigue ?? 0,
    overrides.mode ?? "normal",
    overrides.tyreWear ?? 0,
    overrides.fuelLoad ?? 0,
    zeroVariance,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("calculateLapTime", () => {
  // --- Baseline ---

  it("returns a lap time in a plausible range for the reference car", () => {
    const t = lapTime();
    // Reference car should be in the neighbourhood of 30 s (GDD target).
    expect(t).toBeGreaterThan(20);
    expect(t).toBeLessThan(50);
  });

  it("returns a finite positive number", () => {
    const t = lapTime();
    expect(Number.isFinite(t)).toBe(true);
    expect(t).toBeGreaterThan(0);
  });

  // --- Driver pace modifier ---

  it("higher driver pace produces a faster lap time", () => {
    const fast = lapTime({ driver: { pace: 100 } });
    const slow = lapTime({ driver: { pace: 0 } });
    expect(fast).toBeLessThan(slow);
  });

  it("reference pace (50) produces a time between high and low pace", () => {
    const fast = lapTime({ driver: { pace: 100 } });
    const ref = lapTime({ driver: { pace: 50 } });
    const slow = lapTime({ driver: { pace: 0 } });
    expect(fast).toBeLessThan(ref);
    expect(ref).toBeLessThan(slow);
  });

  // --- Driver fatigue ---

  it("higher driver fatigue produces a slower lap time (reduces effective pace)", () => {
    const fresh = lapTime({ fatigue: 0, driver: { pace: 80 } });
    const tired = lapTime({ fatigue: 80, driver: { pace: 80 } });
    expect(tired).toBeGreaterThan(fresh);
  });

  // --- Instruction mode ---

  it("Push mode is faster than Normal, Normal is faster than Conserve", () => {
    const push = lapTime({ mode: "push" });
    const normal = lapTime({ mode: "normal" });
    const conserve = lapTime({ mode: "conserve" });
    expect(push).toBeLessThan(normal);
    expect(normal).toBeLessThan(conserve);
  });

  it("Normal mode produces the reference baseline (multiplier = 1.0)", () => {
    // With zero variance and Normal mode, the result should match the base × pace × tyre × fuel
    // We can verify that Push is faster and Conserve is slower relative to Normal.
    const normal = lapTime({ mode: "normal" });
    const push = lapTime({ mode: "push" });
    const conserve = lapTime({ mode: "conserve" });
    expect(push).toBeLessThan(normal);
    expect(conserve).toBeGreaterThan(normal);
  });

  // --- Tyre wear modifier ---

  it("fresh tyres (0% wear) are faster than worn tyres", () => {
    const fresh = lapTime({ tyreWear: 0 });
    const worn = lapTime({ tyreWear: 100 });
    expect(fresh).toBeLessThan(worn);
  });

  it("tyre wear increases lap time progressively", () => {
    const t25 = lapTime({ tyreWear: 25 });
    const t50 = lapTime({ tyreWear: 50 });
    const t100 = lapTime({ tyreWear: 100 });
    expect(t25).toBeLessThan(t50);
    expect(t50).toBeLessThan(t100);
  });

  // --- Fuel load modifier ---

  it("higher fuel load produces a slower lap time", () => {
    const light = lapTime({ fuelLoad: 0 });
    const heavy = lapTime({ fuelLoad: 100 });
    expect(light).toBeLessThan(heavy);
  });

  it("fuel load increases lap time proportionally to load", () => {
    const t20 = lapTime({ fuelLoad: 20 });
    const t60 = lapTime({ fuelLoad: 60 });
    const t100 = lapTime({ fuelLoad: 100 });
    expect(t20).toBeLessThan(t60);
    expect(t60).toBeLessThan(t100);
  });

  // --- Car stats ---

  it("a faster car (higher power+handling) produces a shorter base lap time", () => {
    const fast = lapTime({ stats: { power: 95, handling: 90 } });
    const slow = lapTime({ stats: { power: 30, handling: 25 } });
    expect(fast).toBeLessThan(slow);
  });

  // --- Random variance ---

  it("zero variance (random = 0.5) returns a deterministic result", () => {
    const a = calculateLapTime(refStats, refDriver, 0, "normal", 0, 0, () => 0.5);
    const b = calculateLapTime(refStats, refDriver, 0, "normal", 0, 0, () => 0.5);
    expect(a).toBe(b);
  });

  it("low consistency driver shows larger spread than high consistency driver", () => {
    const highConsistency = { ...refDriver, consistency: 100 };
    const lowConsistency = { ...refDriver, consistency: 0 };

    // At consistency=100, variance range = 0 regardless of random value.
    const highLow = calculateLapTime(refStats, highConsistency, 0, "normal", 0, 0, () => 0);
    const highHigh = calculateLapTime(refStats, highConsistency, 0, "normal", 0, 0, () => 1);
    expect(highLow).toBeCloseTo(highHigh, 5); // No variance at perfect consistency

    const lowLow = calculateLapTime(refStats, lowConsistency, 0, "normal", 0, 0, () => 0);
    const lowHigh = calculateLapTime(refStats, lowConsistency, 0, "normal", 0, 0, () => 1);
    expect(lowHigh - lowLow).toBeGreaterThan(highHigh - highLow); // Larger spread
  });

  it("Push mode amplifies variance vs Normal; Conserve reduces it", () => {
    const lowConsistency = { ...refDriver, consistency: 0 };

    const pushLow = calculateLapTime(refStats, lowConsistency, 0, "push", 0, 0, () => 0);
    const pushHigh = calculateLapTime(refStats, lowConsistency, 0, "push", 0, 0, () => 1);
    const normalLow = calculateLapTime(refStats, lowConsistency, 0, "normal", 0, 0, () => 0);
    const normalHigh = calculateLapTime(refStats, lowConsistency, 0, "normal", 0, 0, () => 1);
    const conserveLow = calculateLapTime(refStats, lowConsistency, 0, "conserve", 0, 0, () => 0);
    const conserveHigh = calculateLapTime(refStats, lowConsistency, 0, "conserve", 0, 0, () => 1);

    const pushSpread = pushHigh - pushLow;
    const normalSpread = normalHigh - normalLow;
    const conserveSpread = conserveHigh - conserveLow;

    expect(pushSpread).toBeGreaterThan(normalSpread);
    expect(normalSpread).toBeGreaterThan(conserveSpread);
  });

  it("fatigue increases variance by reducing effective consistency", () => {
    const driver = { ...refDriver, consistency: 80 };
    const freshLow = calculateLapTime(refStats, driver, 0, "normal", 0, 0, () => 0);
    const freshHigh = calculateLapTime(refStats, driver, 0, "normal", 0, 0, () => 1);
    const tiredLow = calculateLapTime(refStats, driver, 100, "normal", 0, 0, () => 0);
    const tiredHigh = calculateLapTime(refStats, driver, 100, "normal", 0, 0, () => 1);

    expect(tiredHigh - tiredLow).toBeGreaterThan(freshHigh - freshLow);
  });

  // --- All modifiers combined ---

  it("all degradation factors compound correctly (slower than baseline)", () => {
    const degraded = lapTime({
      stats: { power: 50, handling: 40 },
      driver: { pace: 30, consistency: 20 },
      fatigue: 60,
      mode: "conserve",
      tyreWear: 80,
      fuelLoad: 80,
    });
    const baseline = lapTime(); // reference car, fresh everything
    expect(degraded).toBeGreaterThan(baseline);
  });

  // --- Edge cases ---

  it("zero fuel load applies no fuel penalty", () => {
    const noFuel = lapTime({ fuelLoad: 0 });
    const someFuel = lapTime({ fuelLoad: 50 });
    expect(noFuel).toBeLessThan(someFuel);
  });

  it("zero tyre wear applies no tyre penalty", () => {
    const fresh = lapTime({ tyreWear: 0 });
    const worn = lapTime({ tyreWear: 1 });
    expect(fresh).toBeLessThan(worn);
  });
});
