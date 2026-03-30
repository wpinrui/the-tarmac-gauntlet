import { describe, it, expect } from "vitest";
import {
  rollLapRisks,
  issueEffectiveProbability,
  failureEffectiveProbability,
  crashSurvivalProbability,
  weightedPick,
} from "./riskRolls";
import type { LapRiskContext } from "./riskRolls";
import type { IssueTemplate } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const looseWheel: IssueTemplate = {
  id: "loose-wheel",
  description: "Loose wheel nut",
  severity: "minor",
  category: "mechanical",
  statDebuffs: { handling: 0.08, reliability: 0.10 },
  weight: 10,
  sparePartsCost: 2,
  workUnits: 14,
};

const brakeFade: IssueTemplate = {
  id: "brake-fade",
  description: "Brake fade",
  severity: "medium",
  category: "mechanical",
  statDebuffs: { handling: 0.18, tyreDurability: 0.10 },
  weight: 4,
  sparePartsCost: 2,
  workUnits: 30,
};

const frontWingDamage: IssueTemplate = {
  id: "front-wing-damage",
  description: "Front wing damage",
  severity: "major",
  category: "crash",
  statDebuffs: { handling: 0.25, fuelEfficiency: 0.15 },
  weight: 3,
  sparePartsCost: 4,
  workUnits: 50,
};

/** Baseline "healthy car, safe driver, Normal mode" context. */
function makeCtx(overrides: Partial<LapRiskContext> = {}): LapRiskContext {
  return {
    currentLap: 10,
    condition: 100,
    carAge: 0,
    reliability: 80,
    driverSafety: 80,
    instructionMode: "normal",
    activeIssues: [],
    issueTemplates: [looseWheel, brakeFade],
    crashTemplates: [frontWingDamage],
    random: () => 0.5, // mid-range default; overridden per test
    ...overrides,
  };
}

/** Forces every probability roll to succeed (roll = 0 < any positive prob). */
const alwaysFire = () => 0;
/** Forces every probability roll to fail (roll = 1 ≥ any prob ≤ 1). */
const neverFire = () => 1;

// ---------------------------------------------------------------------------
// issueEffectiveProbability
// ---------------------------------------------------------------------------

describe("issueEffectiveProbability", () => {
  it("returns base probability at perfect condition, zero reliability, Normal mode", () => {
    const p = issueEffectiveProbability(100, 0, "normal");
    expect(p).toBeCloseTo(0.05);
  });

  it("lower condition increases effective probability", () => {
    const good = issueEffectiveProbability(100, 50, "normal");
    const poor = issueEffectiveProbability(0, 50, "normal");
    expect(poor).toBeGreaterThan(good);
  });

  it("higher reliability reduces effective probability", () => {
    const lowRel = issueEffectiveProbability(80, 0, "normal");
    const highRel = issueEffectiveProbability(80, 100, "normal");
    expect(highRel).toBeLessThan(lowRel);
  });

  it("Push mode raises probability; Conserve mode lowers it vs Normal", () => {
    const push = issueEffectiveProbability(80, 50, "push");
    const normal = issueEffectiveProbability(80, 50, "normal");
    const conserve = issueEffectiveProbability(80, 50, "conserve");
    expect(push).toBeGreaterThan(normal);
    expect(normal).toBeGreaterThan(conserve);
  });
});

// ---------------------------------------------------------------------------
// failureEffectiveProbability
// ---------------------------------------------------------------------------

describe("failureEffectiveProbability", () => {
  it("returns a low probability under ideal conditions", () => {
    const p = failureEffectiveProbability(100, 0, 100, 100, "normal");
    expect(p).toBeLessThan(0.005);
  });

  it("lower condition increases failure probability", () => {
    const good = failureEffectiveProbability(100, 0, 50, 50, "normal");
    const poor = failureEffectiveProbability(0, 0, 50, 50, "normal");
    expect(poor).toBeGreaterThan(good);
  });

  it("older car has higher failure probability", () => {
    const newCar = failureEffectiveProbability(80, 0, 50, 50, "normal");
    const oldCar = failureEffectiveProbability(80, 20, 50, 50, "normal");
    expect(oldCar).toBeGreaterThan(newCar);
  });

  it("age factor is capped — age 50 and age 38 produce the same probability", () => {
    const at38 = failureEffectiveProbability(80, 38, 50, 50, "normal");
    const at50 = failureEffectiveProbability(80, 50, 50, 50, "normal");
    expect(at38).toBeCloseTo(at50, 10);
  });

  it("higher reliability reduces failure probability", () => {
    const lowRel = failureEffectiveProbability(80, 5, 0, 50, "normal");
    const highRel = failureEffectiveProbability(80, 5, 100, 50, "normal");
    expect(highRel).toBeLessThan(lowRel);
  });

  it("higher driver Safety reduces failure probability", () => {
    const unsafeDriver = failureEffectiveProbability(80, 5, 50, 0, "normal");
    const safeDriver = failureEffectiveProbability(80, 5, 50, 100, "normal");
    expect(safeDriver).toBeLessThan(unsafeDriver);
  });

  it("Push mode noticeably raises failure probability vs Normal and Conserve", () => {
    const push = failureEffectiveProbability(50, 10, 30, 30, "push");
    const normal = failureEffectiveProbability(50, 10, 30, 30, "normal");
    const conserve = failureEffectiveProbability(50, 10, 30, 30, "conserve");
    expect(push).toBeGreaterThan(normal);
    expect(normal).toBeGreaterThan(conserve);
    expect(push).toBeGreaterThan(normal * 1.5);
  });
});

// ---------------------------------------------------------------------------
// crashSurvivalProbability
// ---------------------------------------------------------------------------

describe("crashSurvivalProbability", () => {
  it("higher safety means higher survival chance", () => {
    const low = crashSurvivalProbability(0);
    const high = crashSurvivalProbability(100);
    expect(high).toBeGreaterThan(low);
  });

  it("survival probability is between 0 and 1", () => {
    expect(crashSurvivalProbability(0)).toBeGreaterThan(0);
    expect(crashSurvivalProbability(100)).toBeLessThan(1);
  });
});

// ---------------------------------------------------------------------------
// weightedPick
// ---------------------------------------------------------------------------

describe("weightedPick", () => {
  it("returns a template from the list", () => {
    const result = weightedPick([looseWheel, brakeFade], [], () => 0.5);
    expect(result).not.toBeNull();
    expect(["loose-wheel", "brake-fade"]).toContain(result!.id);
  });

  it("skips already-active issues", () => {
    const result = weightedPick(
      [looseWheel, brakeFade],
      [{ templateId: "loose-wheel", lapOccurred: 1 }],
      () => 0,
    );
    expect(result!.id).toBe("brake-fade");
  });

  it("returns null when all templates are active", () => {
    const result = weightedPick(
      [looseWheel],
      [{ templateId: "loose-wheel", lapOccurred: 1 }],
      () => 0,
    );
    expect(result).toBeNull();
  });

  it("higher weight templates are picked more often", () => {
    // looseWheel weight=10, brakeFade weight=4. Total=14.
    // Roll at 0 should pick looseWheel (first in list, higher weight).
    const result = weightedPick([looseWheel, brakeFade], [], () => 0);
    expect(result!.id).toBe("loose-wheel");
  });
});

// ---------------------------------------------------------------------------
// rollLapRisks — no events
// ---------------------------------------------------------------------------

describe("rollLapRisks — no events", () => {
  it("returns no issues and no failure when rolls never fire", () => {
    const result = rollLapRisks(makeCtx({ random: neverFire }));
    expect(result.newIssues).toHaveLength(0);
    expect(result.failure).toBeNull();
  });

  it("perfect car at Conserve mode produces no events with neverFire random", () => {
    const ctx = makeCtx({
      condition: 100,
      carAge: 0,
      reliability: 100,
      driverSafety: 100,
      instructionMode: "conserve",
      random: neverFire,
    });
    const result = rollLapRisks(ctx);
    expect(result.newIssues).toHaveLength(0);
    expect(result.failure).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// rollLapRisks — issues (two-step system)
// ---------------------------------------------------------------------------

describe("rollLapRisks — issues", () => {
  it("triggers one issue when issue roll fires", () => {
    // Calls: failure roll (skip), issue roll (fire), weighted pick
    let callCount = 0;
    const random = () => {
      callCount++;
      return callCount === 1 ? 1 : 0; // skip failure, fire issue, pick first
    };
    const result = rollLapRisks(makeCtx({ random }));
    expect(result.failure).toBeNull();
    expect(result.newIssues).toHaveLength(1);
  });

  it("already-active issues are excluded from the weighted pick", () => {
    let callCount = 0;
    const random = () => {
      callCount++;
      return callCount === 1 ? 1 : 0; // skip failure, fire issue, pick first eligible
    };
    const ctx = makeCtx({
      activeIssues: [{ templateId: "loose-wheel", lapOccurred: 5 }],
      random,
    });
    const result = rollLapRisks(ctx);
    expect(result.newIssues.find((i) => i.templateId === "loose-wheel")).toBeUndefined();
    if (result.newIssues.length > 0) {
      expect(result.newIssues[0].templateId).toBe("brake-fade");
    }
  });

  it("new issue records the correct lap number", () => {
    let callCount = 0;
    const random = () => {
      callCount++;
      return callCount === 1 ? 1 : 0;
    };
    const result = rollLapRisks(makeCtx({ currentLap: 17, random }));
    expect(result.failure).toBeNull();
    result.newIssues.forEach((issue) => {
      expect(issue.lapOccurred).toBe(17);
    });
  });

  it("issue probability is higher at low condition (direction test)", () => {
    const goodCondition = issueEffectiveProbability(100, 50, "normal");
    const poorCondition = issueEffectiveProbability(10, 50, "normal");
    expect(poorCondition).toBeGreaterThan(goodCondition);
  });
});

// ---------------------------------------------------------------------------
// rollLapRisks — failures
// ---------------------------------------------------------------------------

describe("rollLapRisks — failures", () => {
  it("failure is returned when first roll fires", () => {
    const result = rollLapRisks(
      makeCtx({ random: alwaysFire }),
    );
    // alwaysFire: failure fires, crash roll fires (< 0.25), survival fires → non-terminal
    // OR terminal depending on crash templates. Either way, something happens.
    expect(result.failure !== null || result.newIssues.length > 0).toBe(true);
  });

  it("terminal mechanical failure when crash roll does not fire", () => {
    // Calls: failure (fire), crash type (>0.25 = mechanical)
    let callCount = 0;
    const random = () => {
      callCount++;
      if (callCount === 1) return 0; // fire failure
      if (callCount === 2) return 0.5; // not a crash → mechanical
      return 0.5;
    };
    const result = rollLapRisks(makeCtx({ random }));
    expect(result.failure).toBe("mechanical");
    expect(result.newIssues).toHaveLength(0);
  });

  it("non-terminal crash produces a crash issue when driver survives", () => {
    // Calls: failure (fire), crash type (<0.25), survival (fire), weighted pick
    let callCount = 0;
    const random = () => {
      callCount++;
      if (callCount === 1) return 0; // fire failure
      if (callCount === 2) return 0; // crash (< 0.25)
      if (callCount === 3) return 0; // survive
      return 0; // pick first crash template
    };
    const result = rollLapRisks(makeCtx({ random }));
    expect(result.failure).toBeNull();
    expect(result.newIssues).toHaveLength(1);
    expect(result.newIssues[0].templateId).toBe("front-wing-damage");
  });

  it("terminal crash when driver does not survive", () => {
    // Calls: failure (fire), crash type (<0.25), survival (fail)
    let callCount = 0;
    const random = () => {
      callCount++;
      if (callCount === 1) return 0; // fire failure
      if (callCount === 2) return 0; // crash
      if (callCount === 3) return 1; // don't survive
      return 0.5;
    };
    const result = rollLapRisks(makeCtx({ random }));
    expect(result.failure).toBe("crash");
    expect(result.newIssues).toHaveLength(0);
  });

  it("failure probability is higher with Push + low condition + old car", () => {
    const dangerous = failureEffectiveProbability(20, 15, 20, 20, "push");
    const safe = failureEffectiveProbability(100, 0, 100, 100, "conserve");
    expect(dangerous).toBeGreaterThan(safe * 10);
  });

  it("driver Safety stat reduces failure probability", () => {
    const unsafeProb = failureEffectiveProbability(70, 5, 50, 0, "normal");
    const safeProb = failureEffectiveProbability(70, 5, 50, 100, "normal");
    expect(safeProb).toBeLessThan(unsafeProb);
  });
});
