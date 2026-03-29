import type {
  AITeam,
  CarInstance,
  CarModel,
  Contract,
  ContractLength,
  Driver,
  DriverStats,
  GameState,
  PlayerTeam,
  Team,
  Track,
} from "../types";
import { calculateDriverStats, calculateMarketValue, calculateAnnualSalary } from "./driverLifecycle";
import { CAR_MODELS } from "./carModels";
import { AI_TEAM_ROSTER } from "./aiTeamNames";
import { getNewCarListings, generateUsedInventory } from "./carMarket";

// ---------------------------------------------------------------------------
// Tunable constants
// ---------------------------------------------------------------------------

/** Total drivers in the global pool. */
const DRIVER_POOL_SIZE = 310;
/** Number of AI teams. */
const AI_TEAM_COUNT = 99;
/** Drivers per AI team. */
const DRIVERS_PER_AI_TEAM = 3;
/** Player's starting budget. */
const PLAYER_STARTING_BUDGET = 1_000;
/** Youngest driver in the seeded pool. */
const MIN_DRIVER_AGE = 18;
/** Oldest driver in the seeded pool. */
const MAX_DRIVER_AGE = 42;
/** Each driver has up to this many years of phase offset per stat. */
const PHASE_OFFSET_RANGE = 2;
/** Minimum peak stat value for seeded drivers. */
const PEAK_STAT_MIN = 30;
/** Range of peak stat values (added to min). */
const PEAK_STAT_RANGE = 70;
/** Peak age minimum for seeded drivers. */
const PEAK_AGE_MIN = 28;
/** Range of peak ages (added to min). */
const PEAK_AGE_RANGE = 4;

// ---------------------------------------------------------------------------
// AI team tier distribution
// ---------------------------------------------------------------------------

/**
 * AI teams are spread across the performance spectrum. Each tier entry defines
 * how many AI teams start with a car from that tier and their starting budget range.
 */
interface AiTierSpec {
  modelIds: string[];
  count: number;
  budgetMin: number;
  budgetMax: number;
  crewSize: number;
}

const AI_TIER_SPECS: AiTierSpec[] = [
  { modelIds: ["f-01", "f-02", "f-03", "f-04", "f-05", "f-06"],   count: 20, budgetMin: 500,     budgetMax: 4_000,    crewSize: 0 },
  { modelIds: ["e-07", "e-08", "e-09", "e-10", "e-11"],           count: 20, budgetMin: 4_000,   budgetMax: 10_000,   crewSize: 2 },
  { modelIds: ["d-12", "d-13", "d-14", "d-15"],                   count: 20, budgetMin: 10_000,  budgetMax: 25_000,   crewSize: 4 },
  { modelIds: ["c-16", "c-17", "c-18", "c-19", "c-20"],           count: 15, budgetMin: 25_000,  budgetMax: 60_000,   crewSize: 6 },
  { modelIds: ["b-21", "b-22", "b-23", "b-24", "b-25"],           count: 12, budgetMin: 60_000,  budgetMax: 150_000,  crewSize: 8 },
  { modelIds: ["a-26", "a-27", "a-28", "a-29", "a-30"],           count: 8,  budgetMin: 150_000, budgetMax: 400_000,  crewSize: 12 },
  { modelIds: ["f1-31"],                                           count: 4,  budgetMin: 400_000, budgetMax: 1_000_000, crewSize: 16 },
];

// ---------------------------------------------------------------------------
// First-name pools for procedural driver names
// ---------------------------------------------------------------------------

const FIRST_NAMES = [
  "Alex", "Ben", "Carlos", "Daniel", "Erik", "Felix", "George", "Hugo",
  "Ivan", "James", "Kevin", "Liam", "Marco", "Nico", "Oscar", "Pedro",
  "Quinn", "Rafael", "Stefan", "Tom", "Uwe", "Victor", "Will", "Xavier",
  "Yuki", "Zane", "Adrian", "Boris", "Chris", "David", "Emil", "Fabio",
  "Goran", "Hans", "Igor", "Jan", "Klaus", "Lars", "Max", "Nathan",
  "Oliver", "Paul", "Raul", "Seb", "Theo", "Uri", "Vince", "Wes",
];

const LAST_NAMES = [
  "Alonso", "Berg", "Costa", "Dumas", "Evans", "Fischer", "Garcia", "Hill",
  "Ivanov", "Jensen", "Kim", "Laurent", "Müller", "Nielsen", "Olsen", "Perez",
  "Quinn", "Rossi", "Santos", "Torres", "Ueda", "Varga", "Weber", "Xu",
  "Yang", "Zhou", "Arnault", "Blake", "Chen", "De Vries", "Esteban", "Falk",
  "Greco", "Hart", "Ito", "Johansson", "Khan", "Lopez", "Mori", "Novak",
  "Ortiz", "Park", "Rivera", "Silva", "Tanaka", "Uribe", "Volkov", "Walsh",
];

// ---------------------------------------------------------------------------
// Driver generation helpers
// ---------------------------------------------------------------------------

function generateDriverName(index: number, random: () => number): string {
  const first = FIRST_NAMES[Math.floor(random() * FIRST_NAMES.length)];
  const last = LAST_NAMES[Math.floor(random() * LAST_NAMES.length)];
  return `${first} ${last}`;
}

function generatePeakStats(random: () => number): DriverStats {
  return {
    pace:        Math.round(PEAK_STAT_MIN + random() * PEAK_STAT_RANGE),
    consistency: Math.round(PEAK_STAT_MIN + random() * PEAK_STAT_RANGE),
    stamina:     Math.round(PEAK_STAT_MIN + random() * PEAK_STAT_RANGE),
    safety:      Math.round(PEAK_STAT_MIN + random() * PEAK_STAT_RANGE),
    smoothness:  Math.round(PEAK_STAT_MIN + random() * PEAK_STAT_RANGE),
  };
}

function generatePhaseOffsets(random: () => number): DriverStats {
  const offset = () => (random() * 2 - 1) * PHASE_OFFSET_RANGE;
  return {
    pace:        offset(),
    consistency: offset(),
    stamina:     offset(),
    safety:      offset(),
    smoothness:  offset(),
  };
}

/**
 * Seeds the full 310-driver pool with a spread of ages across the career curve.
 * Age distribution is roughly uniform across [MIN_DRIVER_AGE, MAX_DRIVER_AGE].
 */
export function seedDriverPool(random: () => number): Driver[] {
  const drivers: Driver[] = [];
  for (let i = 0; i < DRIVER_POOL_SIZE; i++) {
    const age = MIN_DRIVER_AGE + Math.floor(random() * (MAX_DRIVER_AGE - MIN_DRIVER_AGE + 1));
    const peakAge = PEAK_AGE_MIN + Math.floor(random() * (PEAK_AGE_RANGE + 1));
    const peakStats = generatePeakStats(random);
    const phaseOffsets = generatePhaseOffsets(random);

    const driver: Driver = {
      id: `driver-${i}`,
      name: generateDriverName(i, random),
      age,
      curveParams: { peakAge, peakStats, phaseOffsets },
      marketValue: 0,
    };
    // Calculate market value based on current stats
    driver.marketValue = calculateMarketValue(calculateDriverStats(driver));
    drivers.push(driver);
  }
  return drivers;
}

// ---------------------------------------------------------------------------
// AI team generation
// ---------------------------------------------------------------------------

let globalCarId = 0;
function nextCarId(): string {
  return `car-${++globalCarId}`;
}

function createAiTeam(
  index: number,
  teamName: string,
  spec: AiTierSpec,
  drivers: Driver[],
  driverOffset: number,
  random: () => number,
): { team: AITeam; contracts: Contract[] } {
  const teamId = `ai-${index}`;
  const modelId = spec.modelIds[Math.floor(random() * spec.modelIds.length)];
  const car: CarInstance = {
    id: nextCarId(),
    modelId,
    age: 0,
    condition: 100,
    installedUpgrades: { power: false, handling: false, comfort: false },
  };

  const budget = Math.round(
    spec.budgetMin + random() * (spec.budgetMax - spec.budgetMin),
  );

  // Assign 3 drivers from the pool
  const contracts: Contract[] = [];
  for (let d = 0; d < DRIVERS_PER_AI_TEAM; d++) {
    const driver = drivers[driverOffset + d];
    const stats = calculateDriverStats(driver);
    const annualSalary = calculateAnnualSalary(stats);
    const length: ContractLength = (Math.floor(random() * 3) + 1) as ContractLength;
    contracts.push({
      driverId: driver.id,
      teamId,
      length,
      remainingYears: length,
      annualSalary,
    });
  }

  const team: AITeam = {
    kind: "ai",
    id: teamId,
    name: teamName,
    budget,
    prestige: 0,
    crewSize: spec.crewSize,
    cars: [car],
    contracts,
    enteredCarId: car.id,
    spareParts: 5,
    tyreSets: 5,
  };

  return { team, contracts };
}

// ---------------------------------------------------------------------------
// Player team creation
// ---------------------------------------------------------------------------

function createPlayerTeam(
  playerName: string,
  teamName: string,
  logo: string | null,
  skills: { driver: number; engineer: number; business: number },
): PlayerTeam {
  const car: CarInstance = {
    id: nextCarId(),
    modelId: "f-01", // cheapest junkyard car (plot armour)
    age: 0,
    condition: 100,
    installedUpgrades: { power: false, handling: false, comfort: false },
  };

  return {
    kind: "player",
    id: "player",
    name: teamName,
    budget: PLAYER_STARTING_BUDGET,
    prestige: 0,
    crewSize: 0,
    cars: [car],
    contracts: [],
    enteredCarId: car.id,
    playerName,
    logo,
    skills,
    spareParts: 0,
    tyreSets: 0,
  };
}

// ---------------------------------------------------------------------------
// Placeholder track (one fixed circuit)
// ---------------------------------------------------------------------------

const PLACEHOLDER_TRACK: Track = {
  version: 1,
  scale: { metresPerGridSquare: 10 },
  track: { curves: [], closed: true, lengthMetres: 5000 },
  pitLane: null,
  startFinish: null,
};

// ---------------------------------------------------------------------------
// Full game initialization
// ---------------------------------------------------------------------------

export interface NewGameOptions {
  playerName: string;
  teamName: string;
  logo: string | null;
  skills: { driver: number; engineer: number; business: number };
}

/**
 * Creates a complete initial GameState.
 *
 * - Seeds 310 drivers with age distribution
 * - Creates 99 AI teams across all tiers, each with 1 car and 3 contracted drivers
 * - Creates the player team: $1,000 budget, plot armour car, no drivers, 0 crew
 * - Generates the car market (new + used)
 * - Year 1, newGame phase
 */
export function initializeGame(
  options: NewGameOptions,
  random: () => number,
): GameState {
  // Reset car ID counter for determinism
  globalCarId = 0;

  // 1. Seed driver pool
  const drivers = seedDriverPool(random);

  // 2. Create AI teams: assign drivers in order from the pool
  //    First 99 × 3 = 297 drivers go to AI teams, remainder are free agents
  const allContracts: Contract[] = [];
  const aiTeams: AITeam[] = [];
  let driverOffset = 0;
  let teamIndex = 0;

  for (const spec of AI_TIER_SPECS) {
    for (let i = 0; i < spec.count; i++) {
      const { team, contracts } = createAiTeam(
        teamIndex,
        AI_TEAM_ROSTER[teamIndex].name,
        spec,
        drivers,
        driverOffset,
        random,
      );
      aiTeams.push(team);
      allContracts.push(...contracts);
      driverOffset += DRIVERS_PER_AI_TEAM;
      teamIndex++;
    }
  }

  // 3. Create player team
  const playerTeam = createPlayerTeam(
    options.playerName,
    options.teamName,
    options.logo,
    options.skills,
  );

  // 4. All teams
  const teams: Team[] = [playerTeam, ...aiTeams];

  // 5. Car market
  const carModels = CAR_MODELS;
  const newListings = getNewCarListings(carModels);
  const usedListings = generateUsedInventory(carModels, 20, random);

  return {
    phase: "preRace",
    currentYear: 1,
    meta: {
      createdAt: new Date().toISOString(),
      lastSavedAt: new Date().toISOString(),
      version: 1,
    },
    carModels,
    drivers,
    contracts: allContracts,
    teams,
    economyConfig: {
      prizeSchedule: [], // Built dynamically by buildPrizeSchedule
      tierCosts: [],     // Placeholder — will be defined during economy tuning
      fuelConfig: { costPerLitre: 2 },
      crewCostPerMember: 2_000,
      contractDiscounts: { twoYear: 0.10, threeYear: 0.20 },
      buyoutMultiplier: 1.5,
    },
    carMarket: { newListings, usedListings },
    track: PLACEHOLDER_TRACK,
    race: null,
    raceHistory: [],
  };
}
