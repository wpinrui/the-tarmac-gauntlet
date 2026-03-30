import { describe, it, expect } from "vitest";
import {
  calculateDriverStats,
  totalDriverStats,
  calculateMarketValue,
  calculateAnnualSalary,
  calculateContractSalary,
  calculateBuyoutCost,
  isDriverFreeAgent,
  generateRookie,
  advanceDriverYear,
} from "./driverLifecycle";
import type { Driver, Contract } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/** A driver with a symmetric curve (no phase offsets) peaking at age 30 with all stats = 80. */
function makeDriver(
  id: string,
  age: number,
  peakStats = 80,
  peakAge = 30,
): Driver {
  const peak = {
    pace: peakStats,
    consistency: peakStats,
    stamina: peakStats,
    safety: peakStats,
    smoothness: peakStats,
  };
  const zero = { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 };
  return {
    id,
    name: `Driver ${id}`,
    nationality: "gb",
    age,
    curveParams: { peakAge, peakStats: peak, phaseOffsets: zero },
    marketValue: 0,
  };
}

const neverFire = () => 0;

// ---------------------------------------------------------------------------
// calculateDriverStats — stat curve
// ---------------------------------------------------------------------------

describe("calculateDriverStats", () => {
  it("stat equals peakStat at peak age (no phase offset)", () => {
    const driver = makeDriver("d1", 30, 80);
    const stats = calculateDriverStats(driver);
    expect(stats.pace).toBe(80);
    expect(stats.consistency).toBe(80);
  });

  it("stat curve peaks around age 30", () => {
    const atPeak = calculateDriverStats(makeDriver("d", 30));
    const before = calculateDriverStats(makeDriver("d", 25));
    const after = calculateDriverStats(makeDriver("d", 35));
    expect(atPeak.pace).toBeGreaterThan(before.pace);
    expect(atPeak.pace).toBeGreaterThan(after.pace);
  });

  it("stats are lower at age 18 than at peak", () => {
    const young = calculateDriverStats(makeDriver("d", 18));
    const peak = calculateDriverStats(makeDriver("d", 30));
    expect(young.pace).toBeLessThan(peak.pace);
  });

  it("stats are lower at age 45 than at peak", () => {
    const old = calculateDriverStats(makeDriver("d", 45));
    const peak = calculateDriverStats(makeDriver("d", 30));
    expect(old.pace).toBeLessThan(peak.pace);
  });

  it("stats never go below zero or above 100", () => {
    for (const age of [10, 18, 25, 30, 40, 50, 60]) {
      const stats = calculateDriverStats(makeDriver("d", age));
      for (const v of Object.values(stats)) {
        expect(v).toBeGreaterThanOrEqual(0);
        expect(v).toBeLessThanOrEqual(100);
      }
    }
  });

  it("phase offsets shift the individual stat peak", () => {
    const zeroPeak = { pace: 80, consistency: 80, stamina: 80, safety: 80, smoothness: 80 };
    const noOffset: Driver = {
      id: "a",
      name: "A",
      nationality: "gb",
      age: 30,
      curveParams: {
        peakAge: 30,
        peakStats: zeroPeak,
        phaseOffsets: { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 },
      },
      marketValue: 0,
    };
    const withOffset: Driver = {
      ...noOffset,
      id: "b",
      // pace offset of +3: pace peaks at age 33 instead of 30
      curveParams: {
        ...noOffset.curveParams,
        phaseOffsets: { pace: 3, consistency: 0, stamina: 0, safety: 0, smoothness: 0 },
      },
    };
    const statsA = calculateDriverStats(noOffset);
    const statsB = calculateDriverStats(withOffset);
    // At age 30: driver A is at peak pace, driver B is slightly before peak (age 30 < 33)
    expect(statsA.pace).toBeGreaterThan(statsB.pace);
    // consistency is unchanged (both have 0 offset)
    expect(statsA.consistency).toBe(statsB.consistency);
  });

  it("two same-age drivers with different phase offsets have different stats", () => {
    const base = { pace: 80, consistency: 80, stamina: 80, safety: 80, smoothness: 80 };
    const driverA: Driver = {
      id: "a", name: "A", nationality: "gb", age: 28,
      curveParams: {
        peakAge: 30, peakStats: base,
        phaseOffsets: { pace: -1, consistency: 2, stamina: 0, safety: -2, smoothness: 1 },
      },
      marketValue: 0,
    };
    const driverB: Driver = {
      id: "b", name: "B", nationality: "gb", age: 28,
      curveParams: {
        peakAge: 30, peakStats: base,
        phaseOffsets: { pace: 2, consistency: -1, stamina: 1, safety: 1, smoothness: -2 },
      },
      marketValue: 0,
    };
    const statsA = calculateDriverStats(driverA);
    const statsB = calculateDriverStats(driverB);
    // At least one stat should differ between the two drivers at the same age
    const anyDiff = (Object.keys(statsA) as (keyof typeof statsA)[]).some(
      (k) => statsA[k] !== statsB[k],
    );
    expect(anyDiff).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// calculateMarketValue
// ---------------------------------------------------------------------------

describe("calculateMarketValue", () => {
  it("market value correlates with total stats (higher stats = higher value)", () => {
    const highStats = { pace: 90, consistency: 90, stamina: 90, safety: 90, smoothness: 90 };
    const lowStats = { pace: 20, consistency: 20, stamina: 20, safety: 20, smoothness: 20 };
    expect(calculateMarketValue(highStats)).toBeGreaterThan(calculateMarketValue(lowStats));
  });

  it("market value is positive even for very low stats", () => {
    const lowStats = { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 };
    expect(calculateMarketValue(lowStats)).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// calculateAnnualSalary
// ---------------------------------------------------------------------------

describe("calculateAnnualSalary", () => {
  it("salary is within the GDD-specified range ($500–$150,000)", () => {
    const worstStats = { pace: 0, consistency: 0, stamina: 0, safety: 0, smoothness: 0 };
    const bestStats = { pace: 100, consistency: 100, stamina: 100, safety: 100, smoothness: 100 };
    expect(calculateAnnualSalary(worstStats)).toBeGreaterThanOrEqual(500);
    expect(calculateAnnualSalary(bestStats)).toBeLessThanOrEqual(150_000);
  });

  it("higher stats earn higher salary", () => {
    const elite = { pace: 90, consistency: 90, stamina: 90, safety: 90, smoothness: 90 };
    const poor = { pace: 20, consistency: 20, stamina: 20, safety: 20, smoothness: 20 };
    expect(calculateAnnualSalary(elite)).toBeGreaterThan(calculateAnnualSalary(poor));
  });
});

// ---------------------------------------------------------------------------
// calculateContractSalary
// ---------------------------------------------------------------------------

describe("calculateContractSalary", () => {
  it("1-year contract with no business skill applies no discount", () => {
    expect(calculateContractSalary(10_000, 1, 0)).toBe(10_000);
  });

  it("2-year contract applies 10% discount", () => {
    expect(calculateContractSalary(10_000, 2, 0)).toBe(9_000);
  });

  it("3-year contract applies 20% discount", () => {
    expect(calculateContractSalary(10_000, 3, 0)).toBe(8_000);
  });

  it("business skill provides additional discount on top of contract length discount", () => {
    const withBusiness = calculateContractSalary(10_000, 1, 20); // max business = 10% extra
    const withoutBusiness = calculateContractSalary(10_000, 1, 0);
    expect(withBusiness).toBeLessThan(withoutBusiness);
  });

  it("3-year + max business skill gives the largest discount", () => {
    const best = calculateContractSalary(10_000, 3, 20);
    const worst = calculateContractSalary(10_000, 1, 0);
    expect(best).toBeLessThan(worst);
  });

  it("discounted salary is always positive", () => {
    expect(calculateContractSalary(500, 3, 20)).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// calculateBuyoutCost
// ---------------------------------------------------------------------------

describe("calculateBuyoutCost", () => {
  it("buyout cost = annualSalary × remainingYears × 1.5", () => {
    const contract: Contract = {
      driverId: "d1",
      teamId: "t1",
      length: 3,
      remainingYears: 2,
      annualSalary: 10_000,
    };
    expect(calculateBuyoutCost(contract)).toBe(30_000); // 10_000 × 2 × 1.5
  });

  it("buyout cost with 1 remaining year", () => {
    const contract: Contract = {
      driverId: "d1",
      teamId: "t1",
      length: 2,
      remainingYears: 1,
      annualSalary: 20_000,
    };
    expect(calculateBuyoutCost(contract)).toBe(30_000); // 20_000 × 1 × 1.5
  });
});

// ---------------------------------------------------------------------------
// isDriverFreeAgent
// ---------------------------------------------------------------------------

describe("isDriverFreeAgent", () => {
  it("driver with no contract is a free agent", () => {
    expect(isDriverFreeAgent("d1", [])).toBe(true);
  });

  it("driver with active contract is not a free agent", () => {
    const contracts: Contract[] = [
      { driverId: "d1", teamId: "t1", length: 2, remainingYears: 1, annualSalary: 5_000 },
    ];
    expect(isDriverFreeAgent("d1", contracts)).toBe(false);
  });

  it("driver with expired contract (remainingYears = 0) is a free agent", () => {
    const contracts: Contract[] = [
      { driverId: "d1", teamId: "t1", length: 1, remainingYears: 0, annualSalary: 5_000 },
    ];
    expect(isDriverFreeAgent("d1", contracts)).toBe(true);
  });

  it("other drivers' contracts do not affect free-agent status", () => {
    const contracts: Contract[] = [
      { driverId: "d2", teamId: "t1", length: 2, remainingYears: 2, annualSalary: 5_000 },
    ];
    expect(isDriverFreeAgent("d1", contracts)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// generateRookie
// ---------------------------------------------------------------------------

describe("generateRookie", () => {
  it("rookie is always age 18", () => {
    const r = generateRookie({ id: "r1", name: "Rex", nationality: "gb" }, neverFire);
    expect(r.age).toBe(18);
  });

  it("rookie has low stats (well below peak potential)", () => {
    const r = generateRookie({ id: "r1", name: "Rex", nationality: "gb" }, () => 0.5);
    const stats = calculateDriverStats(r);
    // At age 18 the stat should be substantially below peak
    const total = totalDriverStats(stats);
    const peakTotal = totalDriverStats({
      pace: r.curveParams.peakStats.pace,
      consistency: r.curveParams.peakStats.consistency,
      stamina: r.curveParams.peakStats.stamina,
      safety: r.curveParams.peakStats.safety,
      smoothness: r.curveParams.peakStats.smoothness,
    });
    expect(total).toBeLessThan(peakTotal * 0.6); // at 18, under 60% of peak
  });

  it("rookie ID and name are set from spec", () => {
    const r = generateRookie({ id: "rookie-99", name: "Sam Speed", nationality: "us" }, neverFire);
    expect(r.id).toBe("rookie-99");
    expect(r.name).toBe("Sam Speed");
  });

  it("rookie market value is positive", () => {
    const r = generateRookie({ id: "r1", name: "Rex", nationality: "gb" }, () => 0.5);
    expect(r.marketValue).toBeGreaterThan(0);
  });

  it("different random values produce different rookies", () => {
    const r1 = generateRookie({ id: "r1", name: "A", nationality: "gb" }, () => 0.1);
    const r2 = generateRookie({ id: "r2", name: "B", nationality: "jp" }, () => 0.9);
    const stats1 = calculateDriverStats(r1);
    const stats2 = calculateDriverStats(r2);
    expect(totalDriverStats(stats1)).not.toBe(totalDriverStats(stats2));
  });
});

// ---------------------------------------------------------------------------
// advanceDriverYear
// ---------------------------------------------------------------------------

describe("advanceDriverYear", () => {
  it("all drivers age by exactly 1 year", () => {
    const drivers = [makeDriver("d1", 25), makeDriver("d2", 30), makeDriver("d3", 40)];
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));
    const result = advanceDriverYear(drivers, rookieSpecs, () => 0.5);
    const survivors = result.drivers.filter((d) => !result.rookies.includes(d));
    for (const d of survivors) {
      const original = drivers.find((o) => o.id === d.id)!;
      expect(d.age).toBe(original.age + 1);
    }
  });

  it("retires exactly 15 drivers (the lowest-stat ones)", () => {
    // Create 20 drivers: 15 with very low peak stats and 5 with high peak stats
    const weak = Array.from({ length: 15 }, (_, i) =>
      makeDriver(`weak-${i}`, 30, 10), // total stats ≈ 10×5 = 50 each
    );
    const strong = Array.from({ length: 5 }, (_, i) =>
      makeDriver(`strong-${i}`, 30, 90), // total stats ≈ 90×5 = 450 each
    );
    const drivers = [...weak, ...strong];
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));

    const result = advanceDriverYear(drivers, rookieSpecs, () => 0.5);

    expect(result.retiredIds).toHaveLength(15);
    // All retired should be the weak drivers
    for (const id of result.retiredIds) {
      expect(id).toMatch(/^weak-/);
    }
    // All strong drivers should survive
    for (const s of strong) {
      expect(result.retiredIds).not.toContain(s.id);
    }
  });

  it("adds exactly 15 new rookies", () => {
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 30));
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));
    const result = advanceDriverYear(drivers, rookieSpecs, () => 0.5);
    expect(result.rookies).toHaveLength(15);
  });

  it("new pool size = original size (15 removed + 15 added)", () => {
    const drivers = Array.from({ length: 310 }, (_, i) => makeDriver(`d${i}`, 30));
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));
    const result = advanceDriverYear(drivers, rookieSpecs, () => 0.5);
    expect(result.drivers).toHaveLength(310);
  });

  it("rookies are always age 18", () => {
    const drivers = Array.from({ length: 20 }, (_, i) => makeDriver(`d${i}`, 30));
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));
    const result = advanceDriverYear(drivers, rookieSpecs, () => 0.5);
    for (const r of result.rookies) {
      expect(r.age).toBe(18);
    }
  });

  it("market value is updated on aging drivers", () => {
    // A driver at age 30 (peak) should have higher market value than the same driver at 45
    const atPeak = makeDriver("d1", 29); // will be 30 after advancement
    const old = makeDriver("d2", 44);    // will be 45 after advancement
    // Give both high peakStats so the difference is visible
    const highPeak = { pace: 90, consistency: 90, stamina: 90, safety: 90, smoothness: 90 };
    const withPeak = {
      ...atPeak,
      curveParams: { ...atPeak.curveParams, peakStats: highPeak },
    };
    const withOld = {
      ...old,
      curveParams: { ...old.curveParams, peakStats: highPeak },
    };

    // Need 15 low-stat fillers so d1 and d2 (high stats) survive retirement
    const fillers = Array.from({ length: 15 }, (_, i) => makeDriver(`filler${i}`, 50, 10));
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({ id: `r${i}`, name: `R${i}`, nationality: "gb" }));
    const result = advanceDriverYear([withPeak, withOld, ...fillers], rookieSpecs, () => 0.5);

    const peakResult = result.drivers.find((d) => d.id === "d1")!;
    const oldResult = result.drivers.find((d) => d.id === "d2")!;

    // d1 is now at peak (30), d2 is post-peak (45) — peak should be worth more
    expect(peakResult.marketValue).toBeGreaterThan(oldResult.marketValue);
  });
});
