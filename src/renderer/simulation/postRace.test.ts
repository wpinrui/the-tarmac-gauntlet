import { describe, it, expect } from "vitest";
import {
  buildPrizeSchedule,
  distributePrizeMoney,
  calculatePrestige,
} from "./postRace";
import type { PastRaceResult } from "./postRace";

// ---------------------------------------------------------------------------
// buildPrizeSchedule
// ---------------------------------------------------------------------------

describe("buildPrizeSchedule", () => {
  it("returns exactly numCars entries", () => {
    expect(buildPrizeSchedule(100)).toHaveLength(100);
    expect(buildPrizeSchedule(10)).toHaveLength(10);
  });

  it("positions are 1-indexed and contiguous", () => {
    const schedule = buildPrizeSchedule(100);
    schedule.forEach((entry, idx) => {
      expect(entry.position).toBe(idx + 1);
    });
  });

  it("winner (P1) receives approximately the maximum prize", () => {
    const schedule = buildPrizeSchedule(100);
    expect(schedule[0].amount).toBeGreaterThanOrEqual(400_000);
    expect(schedule[0].amount).toBeLessThanOrEqual(500_000);
  });

  it("last place (P100) receives approximately the minimum prize", () => {
    const schedule = buildPrizeSchedule(100);
    expect(schedule[99].amount).toBeGreaterThanOrEqual(400);
    expect(schedule[99].amount).toBeLessThanOrEqual(1_000);
  });

  it("prizes are strictly decreasing from P1 to P100", () => {
    const schedule = buildPrizeSchedule(100);
    for (let i = 1; i < schedule.length; i++) {
      expect(schedule[i - 1].amount).toBeGreaterThan(schedule[i].amount);
    }
  });

  it("all amounts are positive integers", () => {
    const schedule = buildPrizeSchedule(100);
    for (const entry of schedule) {
      expect(entry.amount).toBeGreaterThan(0);
      expect(Number.isInteger(entry.amount)).toBe(true);
    }
  });

  it("single-car field returns one entry with MAX_PRIZE (no division by zero)", () => {
    const schedule = buildPrizeSchedule(1);
    expect(schedule).toHaveLength(1);
    expect(schedule[0].position).toBe(1);
    expect(Number.isFinite(schedule[0].amount)).toBe(true);
    expect(schedule[0].amount).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// distributePrizeMoney
// ---------------------------------------------------------------------------

describe("distributePrizeMoney", () => {
  const schedule = buildPrizeSchedule(100);

  it("winner receives the maximum prize", () => {
    const results = [
      { teamId: "winner", position: 1, lapsCompleted: 48 },
      { teamId: "last", position: 100, lapsCompleted: 1 },
    ];
    const prizes = distributePrizeMoney(results, schedule);
    expect(prizes["winner"]).toBe(schedule[0].amount);
  });

  it("last place receives the minimum prize (≥1 lap)", () => {
    const results = [{ teamId: "last", position: 100, lapsCompleted: 1 }];
    const prizes = distributePrizeMoney(results, schedule);
    expect(prizes["last"]).toBe(schedule[99].amount);
  });

  it("car with 0 laps completed receives nothing", () => {
    const results = [
      { teamId: "dnf", position: 99, lapsCompleted: 0 },
      { teamId: "other", position: 1, lapsCompleted: 48 },
    ];
    const prizes = distributePrizeMoney(results, schedule);
    expect(prizes["dnf"]).toBe(0);
    expect(prizes["other"]).toBeGreaterThan(0);
  });

  it("all 100 cars get a non-negative prize (0-lap cars get $0, others get > $0)", () => {
    const results = Array.from({ length: 100 }, (_, i) => ({
      teamId: `team-${i + 1}`,
      position: i + 1,
      lapsCompleted: i === 99 ? 0 : 48, // last car completed 0 laps
    }));
    const prizes = distributePrizeMoney(results, schedule);
    for (let i = 0; i < 99; i++) {
      expect(prizes[`team-${i + 1}`]).toBeGreaterThan(0);
    }
    expect(prizes["team-100"]).toBe(0);
  });

  it("higher position always pays more than lower position (for non-zero lap cars)", () => {
    const results = Array.from({ length: 100 }, (_, i) => ({
      teamId: `team-${i + 1}`,
      position: i + 1,
      lapsCompleted: 1,
    }));
    const prizes = distributePrizeMoney(results, schedule);
    for (let pos = 1; pos < 100; pos++) {
      expect(prizes[`team-${pos}`]).toBeGreaterThan(prizes[`team-${pos + 1}`]);
    }
  });

  it("returns an entry for every input team", () => {
    const results = [
      { teamId: "a", position: 1, lapsCompleted: 48 },
      { teamId: "b", position: 2, lapsCompleted: 48 },
      { teamId: "c", position: 3, lapsCompleted: 0 },
    ];
    const prizes = distributePrizeMoney(results, schedule);
    expect(Object.keys(prizes)).toHaveLength(3);
    expect(prizes).toHaveProperty("a");
    expect(prizes).toHaveProperty("b");
    expect(prizes).toHaveProperty("c");
  });
});

// ---------------------------------------------------------------------------
// calculatePrestige
// ---------------------------------------------------------------------------

describe("calculatePrestige", () => {
  it("returns 0 for a team with no race history (year 1)", () => {
    expect(calculatePrestige([])).toBe(0);
  });

  it("a winner always gets prestige close to 100", () => {
    const alwaysWin: PastRaceResult[] = Array.from({ length: 10 }, () => ({
      position: 1,
      totalCars: 100,
    }));
    expect(calculatePrestige(alwaysWin)).toBeCloseTo(100, 5);
  });

  it("a permanent last-place team has prestige of 0", () => {
    const alwaysLast: PastRaceResult[] = Array.from({ length: 10 }, () => ({
      position: 100,
      totalCars: 100,
    }));
    expect(calculatePrestige(alwaysLast)).toBe(0);
  });

  it("prestige increases after a good result", () => {
    const baseline: PastRaceResult[] = Array.from({ length: 5 }, () => ({
      position: 50,
      totalCars: 100,
    }));
    const withWin: PastRaceResult[] = [
      { position: 1, totalCars: 100 }, // newest: win
      ...baseline,
    ];
    expect(calculatePrestige(withWin)).toBeGreaterThan(calculatePrestige(baseline));
  });

  it("prestige decreases after a poor result", () => {
    const baseline: PastRaceResult[] = Array.from({ length: 5 }, () => ({
      position: 50,
      totalCars: 100,
    }));
    const withLoss: PastRaceResult[] = [
      { position: 100, totalCars: 100 }, // newest: last place
      ...baseline,
    ];
    expect(calculatePrestige(withLoss)).toBeLessThan(calculatePrestige(baseline));
  });

  it("recency bias: recent wins outweigh recent losses (team A > team B)", () => {
    // Team A: won recent 5 races, finished last in older 5
    const teamA: PastRaceResult[] = [
      ...Array.from({ length: 5 }, () => ({ position: 1, totalCars: 100 })),   // recent
      ...Array.from({ length: 5 }, () => ({ position: 100, totalCars: 100 })), // old
    ];
    // Team B: finished last in recent 5, won older 5 (mirror of A)
    const teamB: PastRaceResult[] = [
      ...Array.from({ length: 5 }, () => ({ position: 100, totalCars: 100 })), // recent
      ...Array.from({ length: 5 }, () => ({ position: 1, totalCars: 100 })),   // old
    ];
    expect(calculatePrestige(teamA)).toBeGreaterThan(calculatePrestige(teamB));
  });

  it("prestige is between 0 and 100 inclusive for any realistic input", () => {
    const mixed: PastRaceResult[] = [
      { position: 1, totalCars: 100 },
      { position: 50, totalCars: 100 },
      { position: 100, totalCars: 100 },
      { position: 25, totalCars: 100 },
    ];
    const p = calculatePrestige(mixed);
    expect(p).toBeGreaterThanOrEqual(0);
    expect(p).toBeLessThanOrEqual(100);
  });

  it("a single-car race winner gets full prestige", () => {
    // Edge case: totalCars = 1 → only possible position is 1
    const result: PastRaceResult[] = [{ position: 1, totalCars: 1 }];
    expect(calculatePrestige(result)).toBeCloseTo(100, 5);
  });
});
