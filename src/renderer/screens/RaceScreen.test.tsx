// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { App } from "../App";
import { useGameStore } from "../state/store";
import { initializeGame } from "../simulation/gameInit";
import type { RaceResultFull } from "../simulation/raceLoop";
import { snap } from "../simulation/testFixtures";

const stable = () => 0.5;

function stubRaceResult(): RaceResultFull {
  // 48-lap race, P1 finishes at sim t=1440 (each lap = 30s).
  const aSnaps = Array.from({ length: 48 }, (_, i) => snap(i + 1, (i + 1) * 30));
  return {
    results: [
      { carId: "A", finalPosition: 1, lapsCompleted: 48, totalTime: 1440, retired: false, retirementLap: null, retirementReason: null },
    ],
    fastestLap: { carId: "A", lap: 1, time: 30 },
    lapSnapshots: { A: aSnaps },
    positionHistory: Array.from({ length: 48 }, () => [1]),
    carIndexById: { A: 0 },
    events: [],
    stints: { A: [] },
    modeCounters: { A: { push: 0, normal: 48, conserve: 0 } },
  };
}

beforeEach(() => {
  // Stub RAF so the wall-clock hook doesn't tick between render and assertions.
  vi.stubGlobal("requestAnimationFrame", () => 0);
  vi.stubGlobal("cancelAnimationFrame", () => undefined);
  // jsdom lacks ResizeObserver — TrackMap consumes one for its fit-transform.
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  // Non-zero bounding rect so TrackMap's redraw effect runs against the
  // (jsdom-stubbed) canvas API. Width/height are arbitrary.
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    x: 0, y: 0, top: 0, left: 0, right: 600, bottom: 360,
    width: 600, height: 360, toJSON: () => ({}),
  });
  useGameStore.setState({ game: null, screen: "garage", raceSession: null });
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  useGameStore.setState({ game: null, screen: "garage", raceSession: null });
});

describe("phase='race' route", () => {
  it("renders RaceScreen with elapsed=0 at start", () => {
    const game = initializeGame(
      {
        playerName: "Test",
        nationality: "gb",
        teamName: "Test Team",
        logo: null,
        skills: { driver: 5, engineer: 5, business: 5 },
      },
      stable,
    );
    useGameStore.setState({
      game: { ...game, phase: "race" },
      raceSession: { result: stubRaceResult(), status: "running" },
    });

    render(<App />);

    expect(screen.getByText("Race in progress…")).toBeTruthy();
    // Hook has not advanced (RAF stubbed), so elapsed is 0.
    expect(screen.getByText("00:00")).toBeTruthy();
    // Static "/ 24:00" suffix on the wall clock.
    expect(screen.getByText("24:00")).toBeTruthy();
    // Leader's total lap count derived from result.results[0].lapsCompleted.
    expect(screen.getByText("48")).toBeTruthy();
    expect(screen.getByText("Finish race")).toBeTruthy();
    // Phase-3 layout: TrackMap canvas is mounted and the standings slot is
    // a placeholder pending Phase 4.
    expect(document.querySelector(".track-map-canvas")).not.toBeNull();
    expect(screen.getByText("Standings — Phase 4")).toBeTruthy();
  });
});
