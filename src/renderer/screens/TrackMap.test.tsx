// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { TrackMap } from "./TrackMap";
import type { RaceResultFull } from "../simulation/raceLoop";
import { snap } from "../simulation/testFixtures";

function stubResult(): RaceResultFull {
  return {
    results: [
      { carId: "A", finalPosition: 1, lapsCompleted: 2, totalTime: 60, retired: false, retirementLap: null, retirementReason: null },
      { carId: "B", finalPosition: 2, lapsCompleted: 2, totalTime: 65, retired: false, retirementLap: null, retirementReason: null },
    ],
    fastestLap: { carId: "A", lap: 1, time: 30 },
    lapSnapshots: {
      A: [snap(1, 30), snap(2, 60)],
      B: [snap(1, 32), snap(2, 65)],
    },
    positionHistory: [
      [1, 2],
      [1, 2],
    ],
    carIndexById: { A: 0, B: 1 },
    events: [],
    stints: { A: [], B: [] },
    modeCounters: {
      A: { push: 0, normal: 2, conserve: 0 },
      B: { push: 0, normal: 2, conserve: 0 },
    },
  };
}

beforeEach(() => {
  // jsdom lacks ResizeObserver — the component falls back to a single read
  // of getBoundingClientRect, but we still need the constructor to exist
  // for the `typeof ResizeObserver === "undefined"` guard to pass through
  // cleanly across both runtime and test contexts.
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  // jsdom's getBoundingClientRect returns zeros by default. Stub a non-zero
  // size so the redraw effect actually runs through the canvas path.
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 600,
    bottom: 360,
    width: 600,
    height: 360,
    toJSON: () => ({}),
  });
  // jsdom logs "Not implemented: HTMLCanvasElement's getContext()" on every
  // call, which floods test stderr. The component's `if (!ctx) return`
  // already handles a null context; mocking it explicitly silences the
  // log without changing behaviour.
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("TrackMap", () => {
  it("mounts with a stub race result without crashing", () => {
    const { container } = render(
      <TrackMap
        result={stubResult()}
        elapsedSec={0}
        playerCarId="A"
        carTeamIds={{ A: "team-A", B: "team-B" }}
      />,
    );
    expect(container.querySelector("canvas")).not.toBeNull();
  });

  it("does not crash when playerCarId is null", () => {
    const { container } = render(
      <TrackMap
        result={stubResult()}
        elapsedSec={15}
        playerCarId={null}
        carTeamIds={{ A: "team-A", B: "team-B" }}
      />,
    );
    expect(container.querySelector("canvas")).not.toBeNull();
  });

  it("does not crash when a carId has no entry in carTeamIds (falls back to carId)", () => {
    const { container } = render(
      <TrackMap
        result={stubResult()}
        elapsedSec={15}
        playerCarId="A"
        carTeamIds={{}}
      />,
    );
    expect(container.querySelector("canvas")).not.toBeNull();
  });
});
