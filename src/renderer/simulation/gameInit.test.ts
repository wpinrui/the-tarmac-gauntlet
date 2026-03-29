import { describe, it, expect } from "vitest";
import { initializeGame, seedDriverPool } from "./gameInit";

const stable = () => 0.5;

// ---------------------------------------------------------------------------
// seedDriverPool
// ---------------------------------------------------------------------------

describe("seedDriverPool", () => {
  it("generates exactly 310 drivers", () => {
    const drivers = seedDriverPool(stable);
    expect(drivers).toHaveLength(310);
  });

  it("all drivers have ages between 18 and 42", () => {
    const drivers = seedDriverPool(Math.random);
    for (const d of drivers) {
      expect(d.age).toBeGreaterThanOrEqual(18);
      expect(d.age).toBeLessThanOrEqual(42);
    }
  });

  it("age distribution is not uniform — not all 18 (not all rookies)", () => {
    const drivers = seedDriverPool(Math.random);
    const ages = new Set(drivers.map((d) => d.age));
    expect(ages.size).toBeGreaterThan(5);
  });

  it("all drivers have unique IDs", () => {
    const drivers = seedDriverPool(stable);
    const ids = new Set(drivers.map((d) => d.id));
    expect(ids.size).toBe(310);
  });

  it("all drivers have non-empty names", () => {
    const drivers = seedDriverPool(stable);
    for (const d of drivers) {
      expect(d.name.length).toBeGreaterThan(0);
    }
  });

  it("all drivers have positive market values", () => {
    const drivers = seedDriverPool(stable);
    for (const d of drivers) {
      expect(d.marketValue).toBeGreaterThan(0);
    }
  });
});

// ---------------------------------------------------------------------------
// initializeGame
// ---------------------------------------------------------------------------

describe("initializeGame", () => {
  const options = {
    playerName: "Ivan",
    teamName: "Irene Racing",
    logo: null,
    skills: { driver: 5, engineer: 5, business: 5 },
  };

  it("creates a game state with 100 teams", () => {
    const game = initializeGame(options, stable);
    expect(game.teams).toHaveLength(100);
  });

  it("has exactly 1 player team and 99 AI teams", () => {
    const game = initializeGame(options, stable);
    const playerTeams = game.teams.filter((t) => t.kind === "player");
    const aiTeams = game.teams.filter((t) => t.kind === "ai");
    expect(playerTeams).toHaveLength(1);
    expect(aiTeams).toHaveLength(99);
  });

  it("player team has the correct name and player name", () => {
    const game = initializeGame(options, stable);
    const player = game.teams.find((t) => t.kind === "player")!;
    expect(player.name).toBe("Irene Racing");
    expect((player as any).playerName).toBe("Ivan");
  });

  it("player team has $1,000 starting budget", () => {
    const game = initializeGame(options, stable);
    const player = game.teams.find((t) => t.kind === "player")!;
    expect(player.budget).toBe(1_000);
  });

  it("player team has 1 car (plot armour junkyard)", () => {
    const game = initializeGame(options, stable);
    const player = game.teams.find((t) => t.kind === "player")!;
    expect(player.cars).toHaveLength(1);
    expect(player.cars[0].modelId).toBe("f-01");
    expect(player.cars[0].age).toBe(0);
    expect(player.cars[0].condition).toBe(100);
  });

  it("player team has 0 crew, no contracts, no spare parts, no tyre sets", () => {
    const game = initializeGame(options, stable);
    const player = game.teams.find((t) => t.kind === "player")! as any;
    expect(player.crewSize).toBe(0);
    expect(player.contracts).toHaveLength(0);
    expect(player.spareParts).toBe(0);
    expect(player.tyreSets).toBe(0);
  });

  it("player skills are set correctly", () => {
    const game = initializeGame(options, stable);
    const player = game.teams.find((t) => t.kind === "player")! as any;
    expect(player.skills).toEqual({ driver: 5, engineer: 5, business: 5 });
  });

  it("all AI teams have exactly 1 car each", () => {
    const game = initializeGame(options, stable);
    const aiTeams = game.teams.filter((t) => t.kind === "ai");
    for (const team of aiTeams) {
      expect(team.cars).toHaveLength(1);
    }
  });

  it("each AI team has exactly 3 contracted drivers", () => {
    const game = initializeGame(options, stable);
    const aiTeams = game.teams.filter((t) => t.kind === "ai");
    for (const team of aiTeams) {
      const teamContracts = game.contracts.filter((c) => c.teamId === team.id);
      expect(teamContracts).toHaveLength(3);
    }
  });

  it("total contracts = 99 teams × 3 drivers = 297", () => {
    const game = initializeGame(options, stable);
    expect(game.contracts).toHaveLength(297);
  });

  it("driver pool has 310 drivers", () => {
    const game = initializeGame(options, stable);
    expect(game.drivers).toHaveLength(310);
  });

  it("game year is 1", () => {
    const game = initializeGame(options, stable);
    expect(game.currentYear).toBe(1);
  });

  it("all teams start with zero prestige", () => {
    const game = initializeGame(options, stable);
    for (const team of game.teams) {
      expect(team.prestige).toBe(0);
    }
  });

  it("car market has new listings for all models", () => {
    const game = initializeGame(options, stable);
    expect(game.carMarket.newListings.length).toBe(game.carModels.length);
  });

  it("car market has used listings", () => {
    const game = initializeGame(options, stable);
    expect(game.carMarket.usedListings.length).toBeGreaterThan(0);
  });

  it("AI teams span the budget spectrum (some cheap, some expensive)", () => {
    const game = initializeGame(options, stable);
    const aiTeams = game.teams.filter((t) => t.kind === "ai");
    const budgets = aiTeams.map((t) => t.budget);
    const min = Math.min(...budgets);
    const max = Math.max(...budgets);
    expect(min).toBeLessThan(5_000);
    expect(max).toBeGreaterThan(50_000);
  });

  it("race is null at game start", () => {
    const game = initializeGame(options, stable);
    expect(game.race).toBeNull();
  });

  it("race history is empty at game start", () => {
    const game = initializeGame(options, stable);
    expect(game.raceHistory).toHaveLength(0);
  });
});
