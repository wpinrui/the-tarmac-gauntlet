import { describe, it, expect } from "vitest";
import {
  TOTAL_RACE_SECONDS,
  lapsCompletedAtSim,
  leaderLapAt,
  leaderTotalLaps,
  raceSimDuration,
  wallToSim,
} from "./raceClock";
import type { RaceResultFull } from "./raceLoop";
import { snap } from "./testFixtures";

/** Two cars, 4 laps each. A wins, B trails by 8 sim seconds at the line. */
function fixture(): RaceResultFull {
  return {
    results: [
      { carId: "A", finalPosition: 1, lapsCompleted: 4, totalTime: 40, retired: false, retirementLap: null, retirementReason: null },
      { carId: "B", finalPosition: 2, lapsCompleted: 4, totalTime: 48, retired: false, retirementLap: null, retirementReason: null },
    ],
    fastestLap: { carId: "A", lap: 1, time: 10 },
    lapSnapshots: {
      A: [snap(1, 10), snap(2, 20), snap(3, 30), snap(4, 40)],
      B: [snap(1, 12), snap(2, 24), snap(3, 36), snap(4, 48)],
    },
    positionHistory: [],
    carIndexById: { A: 0, B: 1 },
    events: [],
    stints: { A: [], B: [] },
    modeCounters: { A: { push: 0, normal: 4, conserve: 0 }, B: { push: 0, normal: 4, conserve: 0 } },
  };
}

describe("TOTAL_RACE_SECONDS", () => {
  it("matches the GDD contract of 24 real-time minutes", () => {
    expect(TOTAL_RACE_SECONDS).toBe(1440);
  });
});

describe("raceSimDuration", () => {
  it("returns P1's last-lap totalTime", () => {
    expect(raceSimDuration(fixture())).toBe(40);
  });

  it("returns 0 for an empty result", () => {
    const empty: RaceResultFull = {
      results: [],
      fastestLap: null,
      lapSnapshots: {},
      positionHistory: [],
      carIndexById: {},
      events: [],
      stints: {},
      modeCounters: {},
    };
    expect(raceSimDuration(empty)).toBe(0);
  });
});

describe("wallToSim", () => {
  it("scales linearly between 0 and raceSimDuration", () => {
    const r = fixture();
    expect(wallToSim(0, r, 60)).toBe(0);
    expect(wallToSim(30, r, 60)).toBe(20);
    expect(wallToSim(60, r, 60)).toBe(40);
  });

  it("clamps below 0 and above totalRaceSec", () => {
    const r = fixture();
    expect(wallToSim(-5, r, 60)).toBe(0);
    expect(wallToSim(120, r, 60)).toBe(40);
  });

  it("returns 0 when sim duration is 0", () => {
    const empty: RaceResultFull = {
      results: [],
      fastestLap: null,
      lapSnapshots: {},
      positionHistory: [],
      carIndexById: {},
      events: [],
      stints: {},
      modeCounters: {},
    };
    expect(wallToSim(30, empty, 60)).toBe(0);
  });
});

describe("lapsCompletedAtSim", () => {
  it("returns 0 before any lap completes", () => {
    expect(lapsCompletedAtSim([snap(1, 10), snap(2, 20)], 5)).toBe(0);
  });

  it("returns the largest lap whose totalTime ≤ simElapsed", () => {
    const s = [snap(1, 10), snap(2, 20), snap(3, 30)];
    expect(lapsCompletedAtSim(s, 10)).toBe(1);
    expect(lapsCompletedAtSim(s, 19.999)).toBe(1);
    expect(lapsCompletedAtSim(s, 20)).toBe(2);
    expect(lapsCompletedAtSim(s, 1000)).toBe(3);
  });

  it("returns 0 for empty snapshots", () => {
    expect(lapsCompletedAtSim([], 100)).toBe(0);
  });
});

describe("leaderLapAt", () => {
  it("starts at 0 and ends at the leader's total laps", () => {
    const r = fixture();
    expect(leaderLapAt(r, 0, 60)).toBe(0);
    expect(leaderLapAt(r, 60, 60)).toBe(4);
  });

  it("is monotonically non-decreasing across the race", () => {
    const r = fixture();
    let prev = -1;
    for (let t = 0; t <= 60; t += 0.5) {
      const lap = leaderLapAt(r, t, 60);
      expect(lap).toBeGreaterThanOrEqual(prev);
      prev = lap;
    }
  });

  it("picks the car with the most laps at the moment, not P1", () => {
    // Mid-race at sim=20: A done 2, B done 1 → leader = 2
    expect(leaderLapAt(fixture(), 30, 60)).toBe(2);
  });

  it("converges to leaderTotalLaps at the end of the race when there's no tie", () => {
    // The wall-clock display "Lap X / N" must agree once the race is over —
    // otherwise the player sees e.g. "Lap 47 / 48" when the leader has crossed.
    const r = fixture();
    expect(leaderLapAt(r, 60, 60)).toBe(leaderTotalLaps(r));
  });
});

describe("leaderTotalLaps", () => {
  it("returns the standings winner's lap count", () => {
    expect(leaderTotalLaps(fixture())).toBe(4);
  });

  it("returns 0 when results are empty", () => {
    const empty: RaceResultFull = {
      results: [],
      fastestLap: null,
      lapSnapshots: {},
      positionHistory: [],
      carIndexById: {},
      events: [],
      stints: {},
      modeCounters: {},
    };
    expect(leaderTotalLaps(empty)).toBe(0);
  });
});
