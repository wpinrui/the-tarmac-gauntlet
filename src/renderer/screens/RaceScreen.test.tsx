// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { App } from "../App";
import { useGameStore } from "../state/store";
import { initializeGame } from "../simulation/gameInit";
import type { RaceResultFull } from "../simulation/raceLoop";

const stable = () => 0.5;

function stubRaceResult(): RaceResultFull {
  return {
    results: [],
    fastestLap: null,
    lapSnapshots: {},
    positionHistory: Array.from({ length: 48 }, () => []),
    carIndexById: {},
    events: [],
    stints: {},
    modeCounters: {},
  };
}

beforeEach(() => {
  useGameStore.setState({ game: null, screen: "garage", raceSession: null });
});

afterEach(() => {
  cleanup();
  useGameStore.setState({ game: null, screen: "garage", raceSession: null });
});

describe("phase='race' route", () => {
  it("renders RaceScreen with stub raceSession", () => {
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
      raceSession: { result: stubRaceResult(), currentLap: 7, status: "running" },
    });

    render(<App />);

    expect(screen.getByText("Race in progress…")).toBeTruthy();
    // currentLap=7 of totalLaps=48 over 24min → 7/48 × 1440s = 210s = 03:30
    expect(screen.getByText("03:30")).toBeTruthy();
    expect(screen.getByText("24:00")).toBeTruthy();
    expect(screen.getByText("Finish race")).toBeTruthy();
  });
});
