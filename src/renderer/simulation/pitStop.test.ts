import { describe, it, expect } from "vitest";
import { executePitStop } from "./pitStop";
import type { PitStopContext } from "./pitStop";
import type { IssueTemplate } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const noIssueTemplates: IssueTemplate[] = [];

/** Reference context: mid-tier car, half-worn tyres, 40 L fuel, crew 8, skill 10. */
function makeCtx(overrides: Partial<PitStopContext> = {}): PitStopContext {
  return {
    pitStopTime: 20,
    fuelCapacity: 100,
    currentFuel: 40,
    currentTyreWear: 50,
    tyreSetsAvailable: 5,
    sparePartsAvailable: 10,
    currentDriverId: "driver-A",
    driverFatigue: { "driver-A": 60, "driver-B": 0 },
    activeIssues: [],
    issueTemplates: noIssueTemplates,
    crewSize: 8,
    engineerSkill: 10,
    ...overrides,
  };
}

/** Minimal config: no refuel, no tyre change, keep current driver. */
const emptyConfig = { fuelToAdd: 0, changeTyres: false, nextDriverId: null };

// ---------------------------------------------------------------------------
// Duration — base
// ---------------------------------------------------------------------------

describe("executePitStop — duration", () => {
  it("returns a positive duration for any stop", () => {
    const { duration } = executePitStop(makeCtx(), emptyConfig);
    expect(duration).toBeGreaterThan(0);
  });

  it("adding tasks (refuel, tyre change, driver swap) increases duration", () => {
    const baseDuration = executePitStop(makeCtx(), emptyConfig).duration;
    const fullConfig = { fuelToAdd: 60, changeTyres: true, nextDriverId: "driver-B" };
    const fullDuration = executePitStop(makeCtx(), fullConfig).duration;
    expect(fullDuration).toBeGreaterThan(baseDuration);
  });

  it("larger refuel adds more time than small refuel", () => {
    const small = executePitStop(makeCtx(), { ...emptyConfig, fuelToAdd: 10 }).duration;
    const large = executePitStop(makeCtx(), { ...emptyConfig, fuelToAdd: 60 }).duration;
    expect(large).toBeGreaterThan(small);
  });

  // --- Crew ---

  it("more crew produces a faster (shorter) stop than less crew", () => {
    const bigCrew = executePitStop(makeCtx({ crewSize: 16 }), emptyConfig).duration;
    const smallCrew = executePitStop(makeCtx({ crewSize: 2 }), emptyConfig).duration;
    expect(bigCrew).toBeLessThan(smallCrew);
  });

  it("0 crew (solo) is slower than any crew", () => {
    const solo = executePitStop(makeCtx({ crewSize: 0 }), emptyConfig).duration;
    const oneCrew = executePitStop(makeCtx({ crewSize: 1 }), emptyConfig).duration;
    expect(solo).toBeGreaterThan(oneCrew);
  });

  it("full crew (16) gives the fastest base stop", () => {
    const full = executePitStop(makeCtx({ crewSize: 16 }), emptyConfig).duration;
    const half = executePitStop(makeCtx({ crewSize: 8 }), emptyConfig).duration;
    expect(full).toBeLessThan(half);
  });

  // --- Engineer skill ---

  it("higher Engineer skill reduces stop duration", () => {
    const highSkill = executePitStop(makeCtx({ engineerSkill: 20 }), emptyConfig).duration;
    const noSkill = executePitStop(makeCtx({ engineerSkill: 0 }), emptyConfig).duration;
    expect(highSkill).toBeLessThan(noSkill);
  });

  it("lower car Pit Stop Time stat reduces stop duration", () => {
    const fast = executePitStop(makeCtx({ pitStopTime: 10 }), emptyConfig).duration;
    const slow = executePitStop(makeCtx({ pitStopTime: 40 }), emptyConfig).duration;
    expect(fast).toBeLessThan(slow);
  });
});

// ---------------------------------------------------------------------------
// Fuel
// ---------------------------------------------------------------------------

describe("executePitStop — fuel", () => {
  it("fuel is increased by the configured amount", () => {
    const { fuelLevel } = executePitStop(makeCtx(), { ...emptyConfig, fuelToAdd: 40 });
    expect(fuelLevel).toBe(80); // 40 current + 40 added
  });

  it("fuel is capped at tank capacity", () => {
    const { fuelLevel } = executePitStop(
      makeCtx({ currentFuel: 90, fuelCapacity: 100 }),
      { ...emptyConfig, fuelToAdd: 50 },
    );
    expect(fuelLevel).toBe(100);
  });

  it("fuelToAdd = 0 adds no fuel and no refuel time", () => {
    const { fuelLevel, duration: withoutRefuel } = executePitStop(
      makeCtx({ currentFuel: 40 }),
      emptyConfig,
    );
    expect(fuelLevel).toBe(40);
    const { duration: withRefuel } = executePitStop(
      makeCtx({ currentFuel: 40 }),
      { ...emptyConfig, fuelToAdd: 1 },
    );
    expect(withRefuel).toBeGreaterThan(withoutRefuel);
  });

  it("already-full tank: fuelToAdd does nothing (no task time added)", () => {
    const full = makeCtx({ currentFuel: 100, fuelCapacity: 100 });
    const withAttempt = executePitStop(full, { ...emptyConfig, fuelToAdd: 50 }).duration;
    const without = executePitStop(full, emptyConfig).duration;
    expect(withAttempt).toBe(without);
  });
});

// ---------------------------------------------------------------------------
// Tyres
// ---------------------------------------------------------------------------

describe("executePitStop — tyres", () => {
  it("tyre change resets wear to 0", () => {
    const { tyreWear } = executePitStop(
      makeCtx({ currentTyreWear: 80 }),
      { ...emptyConfig, changeTyres: true },
    );
    expect(tyreWear).toBe(0);
  });

  it("tyre change consumes one tyre set", () => {
    const { tyreSetsRemaining } = executePitStop(
      makeCtx({ tyreSetsAvailable: 3 }),
      { ...emptyConfig, changeTyres: true },
    );
    expect(tyreSetsRemaining).toBe(2);
  });

  it("no tyre change leaves wear and sets unchanged", () => {
    const { tyreWear, tyreSetsRemaining } = executePitStop(
      makeCtx({ currentTyreWear: 65, tyreSetsAvailable: 4 }),
      emptyConfig,
    );
    expect(tyreWear).toBe(65);
    expect(tyreSetsRemaining).toBe(4);
  });

  it("tyre change is skipped when no sets remain", () => {
    const result = executePitStop(
      makeCtx({ tyreSetsAvailable: 0, currentTyreWear: 70 }),
      { ...emptyConfig, changeTyres: true },
    );
    expect(result.tyreWear).toBe(70);  // unchanged
    expect(result.tyreSetsRemaining).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// Driver swap
// ---------------------------------------------------------------------------

describe("executePitStop — driver swap", () => {
  it("driver swap changes the current driver ID", () => {
    const { currentDriverId } = executePitStop(
      makeCtx(),
      { ...emptyConfig, nextDriverId: "driver-B" },
    );
    expect(currentDriverId).toBe("driver-B");
  });

  it("incoming driver's fatigue is reset to 0", () => {
    const { driverFatigue } = executePitStop(
      makeCtx({ driverFatigue: { "driver-A": 60, "driver-B": 80 } }),
      { ...emptyConfig, nextDriverId: "driver-B" },
    );
    expect(driverFatigue["driver-B"]).toBe(0);
  });

  it("outgoing driver's fatigue is preserved in the map", () => {
    const { driverFatigue } = executePitStop(
      makeCtx({ driverFatigue: { "driver-A": 60, "driver-B": 0 } }),
      { ...emptyConfig, nextDriverId: "driver-B" },
    );
    expect(driverFatigue["driver-A"]).toBe(60);
  });

  it("nextDriverId = null keeps the current driver", () => {
    const { currentDriverId } = executePitStop(makeCtx(), emptyConfig);
    expect(currentDriverId).toBe("driver-A");
  });

  it("nextDriverId = currentDriverId does not count as a swap", () => {
    const withSameDriver = executePitStop(
      makeCtx(),
      { ...emptyConfig, nextDriverId: "driver-A" },
    ).duration;
    const withNoSwap = executePitStop(makeCtx(), emptyConfig).duration;
    expect(withSameDriver).toBe(withNoSwap);
  });
});

// ---------------------------------------------------------------------------
// Issue fixes
// ---------------------------------------------------------------------------

describe("executePitStop — issue fixes", () => {
  const templates: IssueTemplate[] = [
    {
      id: "loose-wheel",
      description: "Loose wheel nut",
      lapTimeCost: 3,
      probabilityPerLap: 0.05,
      sparePartsCost: 2,
      fixDuration: 10,
    },
    {
      id: "brake-fade",
      description: "Overheating brakes",
      lapTimeCost: 5,
      probabilityPerLap: 0.03,
      sparePartsCost: 4,
      fixDuration: 15,
    },
  ];

  const issues = [
    { templateId: "loose-wheel", lapOccurred: 3 },
    { templateId: "brake-fade", lapOccurred: 5 },
  ];

  it("fixed issues are removed from activeIssues", () => {
    const ctx = makeCtx({ activeIssues: issues, issueTemplates: templates, sparePartsAvailable: 10 });
    const { activeIssues } = executePitStop(ctx, { ...emptyConfig, issueIdsToFix: ["loose-wheel"] });
    expect(activeIssues.find((i) => i.templateId === "loose-wheel")).toBeUndefined();
    expect(activeIssues.find((i) => i.templateId === "brake-fade")).toBeDefined();
  });

  it("fixing an issue consumes spare parts", () => {
    const ctx = makeCtx({ activeIssues: issues, issueTemplates: templates, sparePartsAvailable: 10 });
    const { sparePartsRemaining } = executePitStop(ctx, { ...emptyConfig, issueIdsToFix: ["loose-wheel"] });
    expect(sparePartsRemaining).toBe(8); // cost = 2
  });

  it("fixing an issue adds its fixDuration to the stop", () => {
    const ctx = makeCtx({ activeIssues: issues, issueTemplates: templates, sparePartsAvailable: 10 });
    const withFix = executePitStop(ctx, { ...emptyConfig, issueIdsToFix: ["loose-wheel"] }).duration;
    const withoutFix = executePitStop(ctx, emptyConfig).duration;
    expect(withFix).toBeGreaterThan(withoutFix);
  });

  it("issue is skipped if spare parts are insufficient", () => {
    const ctx = makeCtx({ activeIssues: issues, issueTemplates: templates, sparePartsAvailable: 1 });
    const { activeIssues, sparePartsRemaining } = executePitStop(ctx, { ...emptyConfig, issueIdsToFix: ["loose-wheel"] });
    expect(activeIssues.find((i) => i.templateId === "loose-wheel")).toBeDefined(); // not fixed
    expect(sparePartsRemaining).toBe(1); // no parts consumed
  });

  it("unfixed issues remain active", () => {
    const ctx = makeCtx({ activeIssues: issues, issueTemplates: templates, sparePartsAvailable: 10 });
    const { activeIssues } = executePitStop(ctx, emptyConfig); // fix nothing (no issueIdsToFix)
    expect(activeIssues).toHaveLength(2);
  });
});

// ---------------------------------------------------------------------------
// Instruction mode
// ---------------------------------------------------------------------------

describe("executePitStop — instruction mode", () => {
  it("always returns Normal instruction mode", () => {
    const { instructionMode } = executePitStop(makeCtx(), emptyConfig);
    expect(instructionMode).toBe("normal");
  });
});

// ---------------------------------------------------------------------------
// Combined stop
// ---------------------------------------------------------------------------

describe("executePitStop — combined stop", () => {
  it("full stop (refuel + tyres + swap + issue fix) updates all resources correctly", () => {
    const templates: IssueTemplate[] = [
      { id: "loose-wheel", description: "test", lapTimeCost: 2, probabilityPerLap: 0.05, sparePartsCost: 3, fixDuration: 8 },
    ];
    const ctx = makeCtx({
      currentFuel: 20,
      fuelCapacity: 100,
      currentTyreWear: 80,
      tyreSetsAvailable: 2,
      sparePartsAvailable: 5,
      driverFatigue: { "driver-A": 70, "driver-B": 0 },
      activeIssues: [{ templateId: "loose-wheel", lapOccurred: 2 }],
      issueTemplates: templates,
    });
    const config = { fuelToAdd: 60, changeTyres: true, nextDriverId: "driver-B", issueIdsToFix: ["loose-wheel"] };

    const result = executePitStop(ctx, config);

    expect(result.fuelLevel).toBe(80);          // 20 + 60
    expect(result.tyreWear).toBe(0);             // fresh tyres
    expect(result.tyreSetsRemaining).toBe(1);    // 2 → 1
    expect(result.sparePartsRemaining).toBe(2);  // 5 − 3
    expect(result.currentDriverId).toBe("driver-B");
    expect(result.driverFatigue["driver-B"]).toBe(0);
    expect(result.activeIssues).toHaveLength(0); // issue fixed
    expect(result.instructionMode).toBe("normal");
    expect(result.duration).toBeGreaterThan(0);
  });
});
