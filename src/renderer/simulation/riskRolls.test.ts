import { describe, it, expect } from "vitest";
import {
  rollLapRisks,
  issueEffectiveProbability,
  failureEffectiveProbability,
} from "./riskRolls";
import type { LapRiskContext } from "./riskRolls";
import type { IssueTemplate } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const looseWheel: IssueTemplate = {
  id: "loose-wheel",
  description: "Loose wheel nut",
  lapTimeCost: 3,
  probabilityPerLap: 0.05,
  sparePartsCost: 2,
  fixDuration: 10,
};

const brakeFade: IssueTemplate = {
  id: "brake-fade",
  description: "Overheating brakes",
  lapTimeCost: 5,
  probabilityPerLap: 0.04,
  sparePartsCost: 4,
  fixDuration: 15,
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
  it("returns base probability unmodified at perfect condition, zero reliability, Normal mode", () => {
    const p = issueEffectiveProbability(0.05, 100, 0, "normal");
    expect(p).toBeCloseTo(0.05);
  });

  it("lower condition increases effective probability", () => {
    const good = issueEffectiveProbability(0.05, 100, 50, "normal");
    const poor = issueEffectiveProbability(0.05, 0, 50, "normal");
    expect(poor).toBeGreaterThan(good);
  });

  it("higher reliability reduces effective probability", () => {
    const lowRel = issueEffectiveProbability(0.05, 80, 0, "normal");
    const highRel = issueEffectiveProbability(0.05, 80, 100, "normal");
    expect(highRel).toBeLessThan(lowRel);
  });

  it("Push mode raises probability; Conserve mode lowers it vs Normal", () => {
    const push = issueEffectiveProbability(0.05, 80, 50, "push");
    const normal = issueEffectiveProbability(0.05, 80, 50, "normal");
    const conserve = issueEffectiveProbability(0.05, 80, 50, "conserve");
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
    // Push on a fragile car should be substantially more dangerous than Normal.
    expect(push).toBeGreaterThan(normal * 1.5);
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
// rollLapRisks — issues
// ---------------------------------------------------------------------------

describe("rollLapRisks — issues", () => {
  it("multiple issue templates can trigger in the same lap", () => {
    // Use a two-step random: first call (failure roll) returns 1 (no failure),
    // subsequent calls (issue rolls) return 0 (trigger).
    let callCount = 0;
    const random = () => {
      callCount++;
      return callCount === 1 ? 1 : 0; // skip failure, trigger all issues
    };
    const result = rollLapRisks(makeCtx({ random }));
    expect(result.failure).toBeNull();
    expect(result.newIssues).toHaveLength(2); // both templates fired
  });

  it("already-active issues are not re-rolled (no duplicates)", () => {
    let callCount = 0;
    const random = () => {
      callCount++;
      return callCount === 1 ? 1 : 0; // skip failure, trigger all eligible
    };
    const ctx = makeCtx({
      activeIssues: [{ templateId: "loose-wheel", lapOccurred: 5 }],
      random,
    });
    const result = rollLapRisks(ctx);
    expect(result.newIssues.find((i) => i.templateId === "loose-wheel")).toBeUndefined();
    expect(result.newIssues.find((i) => i.templateId === "brake-fade")).toBeDefined();
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
    const goodCondition = issueEffectiveProbability(looseWheel.probabilityPerLap, 100, 50, "normal");
    const poorCondition = issueEffectiveProbability(looseWheel.probabilityPerLap, 10, 50, "normal");
    expect(poorCondition).toBeGreaterThan(goodCondition);
  });
});

// ---------------------------------------------------------------------------
// rollLapRisks — failures
// ---------------------------------------------------------------------------

describe("rollLapRisks — failures", () => {
  it("failure is returned when first roll fires", () => {
    // First random() call is the failure roll; always fire it.
    let callCount = 0;
    const result = rollLapRisks(
      makeCtx({ random: () => { callCount++; return 0; } }),
    );
    expect(result.failure).not.toBeNull();
  });

  it("failure type is either 'mechanical' or 'crash'", () => {
    for (const val of [0, 0.1, 0.5, 0.9, 0.99]) {
      let callCount = 0;
      const result = rollLapRisks(makeCtx({
        random: () => {
          callCount++;
          return callCount === 1 ? 0 : val; // fire failure, use val for type roll
        },
      }));
      expect(["mechanical", "crash"]).toContain(result.failure);
    }
  });

  it("no new issues are returned when a failure occurs", () => {
    const result = rollLapRisks(makeCtx({ random: alwaysFire }));
    if (result.failure !== null) {
      expect(result.newIssues).toHaveLength(0);
    }
  });

  it("failure probability is higher with Push + low condition + old car", () => {
    const dangerous = failureEffectiveProbability(20, 15, 20, 20, "push");
    const safe = failureEffectiveProbability(100, 0, 100, 100, "conserve");
    expect(dangerous).toBeGreaterThan(safe * 10); // meaningfully more dangerous
  });

  it("driver Safety stat reduces failure probability", () => {
    const unsafeProb = failureEffectiveProbability(70, 5, 50, 0, "normal");
    const safeProb = failureEffectiveProbability(70, 5, 50, 100, "normal");
    expect(safeProb).toBeLessThan(unsafeProb);
  });
});
