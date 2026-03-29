import type {
  AITeam,
  CarInstance,
  CarModel,
  Contract,
  ContractLength,
  Driver,
  InstalledUpgrades,
  UsedCarListing,
} from "../types";
import { calculateDriverStats, totalDriverStats, calculateAnnualSalary } from "./driverLifecycle";

// ---------------------------------------------------------------------------
// Tunable constants
// ---------------------------------------------------------------------------

/** AI teams always aim to field exactly this many drivers. */
const AI_TARGET_DRIVER_COUNT = 3;
/** Crew cost per member per year (matches GDD §5). */
const CREW_COST_PER_MEMBER = 2_000;
/** Target crew size AI will work toward when budget permits. */
const AI_TARGET_CREW_SIZE = 8;
/** Number of spare parts AI buys per spending iteration. */
const AI_SPARE_PARTS_PER_BUY = 10;
/** Number of tyre sets AI buys per spending iteration. */
const AI_TYRE_SETS_PER_BUY = 5;
/** Cost per spare part unit for AI teams. */
const AI_SPARE_PART_COST = 100;
/** Cost per tyre set for AI teams (average across tiers). */
const AI_TYRE_SET_COST = 500;
/** Maximum spare parts an AI team will accumulate. */
const AI_MAX_SPARE_PARTS = 50;
/** Maximum tyre sets an AI team will accumulate. */
const AI_MAX_TYRE_SETS = 30;
/** Score bonus for filling a mandatory empty driver slot (ensures it ranks above upgrades). */
const EMPTY_SLOT_BONUS = 10_000;

// ---------------------------------------------------------------------------
// Context and result types
// ---------------------------------------------------------------------------

/** All data an AI team needs to make spending decisions. */
export interface AiSpendingContext {
  /** The AI team making spending decisions. */
  team: AITeam;
  /** All active contracts across all teams (used to identify free agents). */
  allContracts: Contract[];
  /** Full driver pool (used to look up free agents). */
  allDrivers: Driver[];
  /** Full car model catalogue. */
  carModels: CarModel[];
  /** Current year's used car inventory. */
  usedListings: UsedCarListing[];
  /** Generates a unique ID for a newly purchased car instance. */
  newCarId: () => string;
}

export interface AiSpendingResult {
  /** Updated AI team after all spending decisions. */
  updatedTeam: AITeam;
  /** New contracts created by this team during the spending round. */
  newContracts: Contract[];
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Returns the IDs of drivers currently under contract with a given team. */
function contractedDriverIds(teamId: string, contracts: Contract[]): string[] {
  return contracts
    .filter((c) => c.teamId === teamId && c.remainingYears > 0)
    .map((c) => c.driverId);
}

/** Returns drivers not under contract with any team (free agents). */
function freeAgents(allDrivers: Driver[], allContracts: Contract[]): Driver[] {
  const contracted = new Set(
    allContracts
      .filter((c) => c.remainingYears > 0)
      .map((c) => c.driverId),
  );
  return allDrivers.filter((d) => !contracted.has(d.id));
}

/**
 * Performance score for a car: sum of power + handling from effective stats
 * at a notional full-condition / new-car state, used only for comparison between cars.
 * We score on upgrade-adjusted base stats (not degraded by age/condition) so AI
 * compares cars' raw performance ceilings rather than their current wear state.
 */
function carScore(model: CarModel, upgrades: InstalledUpgrades): number {
  const u = upgrades;
  const p = model.potentialStats;
  const b = model.baseStats;
  const power    = u.power    ? p.power    : b.power;
  const handling = u.handling ? p.handling : b.handling;
  return power + handling;
}

/** Total stats for a driver at their current age. */
function driverScore(driver: Driver): number {
  return totalDriverStats(calculateDriverStats(driver));
}

/**
 * Returns the best car the AI team currently owns (highest carScore).
 * Returns null if the team has no cars.
 */
function bestOwnedCar(
  team: AITeam,
  carModels: CarModel[],
): { instance: CarInstance; model: CarModel; score: number } | null {
  if (team.cars.length === 0) return null;
  const modelMap = new Map(carModels.map((m) => [m.id, m]));
  let best: { instance: CarInstance; model: CarModel; score: number } | null = null;
  for (const instance of team.cars) {
    const model = modelMap.get(instance.modelId);
    if (!model) continue;
    const score = carScore(model, instance.installedUpgrades);
    if (best === null || score > best.score) {
      best = { instance, model, score };
    }
  }
  return best;
}

// ---------------------------------------------------------------------------
// Spending option types (discriminated union)
// ---------------------------------------------------------------------------

type SpendingOption =
  | { kind: "hireDriver";    value: number; cost: number; driver: Driver; contractLength: ContractLength }
  | { kind: "upgradePack";   value: number; cost: number; carId: string; packType: "power" | "handling" | "comfort" }
  | { kind: "buyNewCar";     value: number; cost: number; modelId: string }
  | { kind: "buyUsedCar";    value: number; cost: number; listing: UsedCarListing }
  | { kind: "hireCrew";      value: number; cost: number; newSize: number }
  | { kind: "buySpares";     value: number; cost: number }
  | { kind: "buyTyres";      value: number; cost: number };

// ---------------------------------------------------------------------------
// Option enumeration
// ---------------------------------------------------------------------------

function enumerateOptions(
  team: AITeam,
  contractedIds: string[],
  agents: Driver[],
  carModels: CarModel[],
  usedListings: UsedCarListing[],
): SpendingOption[] {
  const options: SpendingOption[] = [];
  const modelMap = new Map(carModels.map((m) => [m.id, m]));
  const current = bestOwnedCar(team, carModels);
  const currentScore = current?.score ?? 0;
  const driverCount = contractedIds.length;

  // AI only hires to fill empty slots — replacement happens via contract expiry next year
  if (driverCount < AI_TARGET_DRIVER_COUNT) {
    for (const agent of agents) {
      const agentScore = driverScore(agent);
      const annualSalary = calculateAnnualSalary(calculateDriverStats(agent));
      options.push({
        kind: "hireDriver",
        value: EMPTY_SLOT_BONUS + agentScore,
        cost: annualSalary,
        driver: agent,
        contractLength: 1,
      });
    }
  }

  // --- Upgrade packs on current car ---
  if (current) {
    const packTypes = ["power", "handling", "comfort"] as const;
    for (const packType of packTypes) {
      if (!current.instance.installedUpgrades[packType]) {
        const packDef = current.model.upgradePacks.find((pk) => pk.type === packType);
        if (packDef) {
          const upgradedUpgrades = { ...current.instance.installedUpgrades, [packType]: true };
          const upgradedScore = carScore(current.model, upgradedUpgrades);
          options.push({
            kind: "upgradePack",
            value: upgradedScore - currentScore,
            cost: packDef.cost,
            carId: current.instance.id,
            packType,
          });
        }
      }
    }
  }

  // --- Buy new car ---
  for (const model of carModels) {
    const fullUpgrades: InstalledUpgrades = { power: true, handling: true, comfort: true };
    const potentialScore = carScore(model, fullUpgrades);
    if (potentialScore > currentScore) {
      options.push({
        kind: "buyNewCar",
        value: potentialScore - currentScore,
        cost: model.price,
        modelId: model.id,
      });
    }
  }

  // --- Buy used car ---
  for (const listing of usedListings) {
    const model = modelMap.get(listing.modelId);
    if (!model) continue;
    const listingScore = carScore(model, listing.installedUpgrades);
    if (listingScore > currentScore) {
      options.push({
        kind: "buyUsedCar",
        value: listingScore - currentScore,
        cost: listing.price,
        listing,
      });
    }
  }

  // --- Crew hiring ---
  if (team.crewSize < AI_TARGET_CREW_SIZE) {
    const needed = AI_TARGET_CREW_SIZE - team.crewSize;
    const cost = needed * CREW_COST_PER_MEMBER;
    options.push({
      kind: "hireCrew",
      value: needed * 5, // 5 value points per crew member
      cost,
      newSize: AI_TARGET_CREW_SIZE,
    });
  }

  // --- Consumables ---
  if (team.spareParts < AI_MAX_SPARE_PARTS) {
    options.push({
      kind: "buySpares",
      value: 3,
      cost: AI_SPARE_PARTS_PER_BUY * AI_SPARE_PART_COST,
    });
  }
  if (team.tyreSets < AI_MAX_TYRE_SETS) {
    options.push({
      kind: "buyTyres",
      value: 3,
      cost: AI_TYRE_SETS_PER_BUY * AI_TYRE_SET_COST,
    });
  }

  return options;
}

// ---------------------------------------------------------------------------
// Apply a spending option
// ---------------------------------------------------------------------------

function applyOption(
  team: AITeam,
  option: SpendingOption,
  allContracts: Contract[],
  contractedIds: string[],
  newCarId: () => string,
  newContracts: Contract[],
): { team: AITeam; contractedIds: string[]; allContracts: Contract[] } {
  switch (option.kind) {
    case "hireDriver": {
      const contract: Contract = {
        driverId: option.driver.id,
        teamId: team.id,
        length: option.contractLength,
        remainingYears: option.contractLength,
        annualSalary: option.cost,
      };
      newContracts.push(contract);
      return {
        team: { ...team, budget: team.budget - option.cost, contracts: [...team.contracts, contract] },
        contractedIds: [...contractedIds, option.driver.id],
        allContracts: [...allContracts, contract],
      };
    }

    case "upgradePack": {
      const updatedCars = team.cars.map((c) =>
        c.id === option.carId
          ? { ...c, installedUpgrades: { ...c.installedUpgrades, [option.packType]: true } }
          : c,
      );
      return {
        team: { ...team, budget: team.budget - option.cost, cars: updatedCars },
        contractedIds,
        allContracts,
      };
    }

    case "buyNewCar": {
      const newInstance: CarInstance = {
        id: newCarId(),
        modelId: option.modelId,
        age: 0,
        condition: 100,
        installedUpgrades: { power: false, handling: false, comfort: false },
      };
      return {
        team: {
          ...team,
          budget: team.budget - option.cost,
          cars: [...team.cars, newInstance],
          enteredCarId: team.enteredCarId ?? newInstance.id,
        },
        contractedIds,
        allContracts,
      };
    }

    case "buyUsedCar": {
      const newInstance: CarInstance = {
        id: newCarId(),
        modelId: option.listing.modelId,
        age: option.listing.age,
        condition: option.listing.condition,
        installedUpgrades: option.listing.installedUpgrades,
      };
      return {
        team: {
          ...team,
          budget: team.budget - option.cost,
          cars: [...team.cars, newInstance],
          enteredCarId: team.enteredCarId ?? newInstance.id,
        },
        contractedIds,
        allContracts,
      };
    }

    case "hireCrew": {
      return {
        team: { ...team, budget: team.budget - option.cost, crewSize: option.newSize },
        contractedIds,
        allContracts,
      };
    }

    case "buySpares": {
      return {
        team: {
          ...team,
          budget: team.budget - option.cost,
          spareParts: team.spareParts + AI_SPARE_PARTS_PER_BUY,
        },
        contractedIds,
        allContracts,
      };
    }

    case "buyTyres": {
      return {
        team: {
          ...team,
          budget: team.budget - option.cost,
          tyreSets: team.tyreSets + AI_TYRE_SETS_PER_BUY,
        },
        contractedIds,
        allContracts,
      };
    }
  }
}

// ---------------------------------------------------------------------------
// Main greedy loop
// ---------------------------------------------------------------------------

/**
 * Runs the AI team's annual spending decisions using a greedy algorithm (GDD §8).
 *
 * Each iteration: enumerate all affordable options, score them, apply the highest-value
 * option, repeat until no affordable options remain or budget is exhausted.
 *
 * "Highest value" is intentionally simplistic — AI teams are reasonable but not optimal.
 */
export function runAiSpending(
  ctx: AiSpendingContext,
): AiSpendingResult {
  let team = ctx.team;
  let allContracts = ctx.allContracts;
  const newContracts: Contract[] = [];

  let contractedIds = contractedDriverIds(team.id, allContracts);
  // Track which drivers have already been hired this round to avoid duplicates
  const hiredThisRound = new Set<string>();

  const MAX_ITERATIONS = 100; // Safety limit to prevent infinite loops
  for (let i = 0; i < MAX_ITERATIONS; i++) {
    // Free agents: exclude those already hired this round
    const agents = freeAgents(ctx.allDrivers, allContracts).filter(
      (d) => !hiredThisRound.has(d.id),
    );

    const options = enumerateOptions(
      team,
      contractedIds,
      agents,
      ctx.carModels,
      ctx.usedListings,
    );

    const affordable = options.filter((o) => o.cost <= team.budget);
    if (affordable.length === 0) break;

    // Pick highest-value option; break ties by lowest cost
    affordable.sort((a, b) => b.value - a.value || a.cost - b.cost);
    const best = affordable[0];

    if (best.kind === "hireDriver") {
      hiredThisRound.add(best.driver.id);
    }

    const result = applyOption(
      team,
      best,
      allContracts,
      contractedIds,
      ctx.newCarId,
      newContracts,
    );
    team = result.team;
    contractedIds = result.contractedIds;
    allContracts = result.allContracts;
  }

  return { updatedTeam: team, newContracts };
}
