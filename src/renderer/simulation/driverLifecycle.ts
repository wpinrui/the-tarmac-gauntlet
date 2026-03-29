import type { Driver, DriverStats, DriverCurveParams, Contract, ContractLength } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Career curve ---
/**
 * Half-width of the sinusoidal career arc in years.
 * A driver's curve spans peakAge ± HALF_CAREER; outside this window the floor applies.
 */
const HALF_CAREER = 16;
/**
 * Minimum stat fraction at career extremes.
 * An 18-year-old with HALF_CAREER = 16 and peakAge = 30 is ~12 years before peak;
 * STAT_FLOOR ensures they show roughly 15% of their peak potential at entry.
 */
const STAT_FLOOR = 0.15;

// --- Market value ---
/** Minimum market value ($/driver) — even the worst driver has some nominal worth. */
const MIN_MARKET_VALUE = 500;
/** Maximum market value — a theoretically perfect 5×100 driver. */
const MAX_MARKET_VALUE = 500_000;
/** Maximum possible total stat sum (5 stats × 100 each). */
const MAX_TOTAL_STATS = 500;
/** Exponent for the market value curve (>1 concentrates value at the top). */
const MARKET_VALUE_EXPONENT = 2;

// --- Annual salary ---
/** Minimum annual salary (GDD §4: bottom rookies ~$500). */
const MIN_SALARY = 500;
/** Maximum annual salary (GDD §4: elite peak-age ~$150,000). */
const MAX_SALARY = 150_000;
/**
 * Exponent for the salary curve.
 * Higher exponent steepens the curve — elite drivers command much more than midfield.
 * Calibrated so a 200-total-stat midfield driver earns ~$10k and 350-stat earns ~$52k.
 */
const SALARY_EXPONENT = 3;

// --- Contract discounts ---
/** Salary discount applied when signing a multi-year contract (GDD §4). */
const CONTRACT_LENGTH_DISCOUNTS: Record<ContractLength, number> = {
  1: 0,
  2: 0.10, // 10% for two-year contracts
  3: 0.20, // 20% for three-year contracts
};
/** Maximum additional discount from the player's Business skill. */
const MAX_BUSINESS_EXTRA_DISCOUNT = 0.10;
/** Maximum Business skill level (matches PlayerSkills.business in team.ts). */
const MAX_BUSINESS_SKILL = 20;

// --- Buyout ---
/** Buyout cost multiplier on remaining salary (GDD §4: ×1.5). */
const BUYOUT_MULTIPLIER = 1.5;

// --- Rookie generation ---
/** Age of all newly generated rookie drivers (GDD §4). */
const ROOKIE_AGE = 18;
/** Minimum career peak age for generated rookies. */
const ROOKIE_PEAK_AGE_MIN = 28;
/** Inclusive range above the minimum for peak age selection (gives 28–32). */
const ROOKIE_PEAK_AGE_RANGE = 4;
/** Minimum peak-stat value for a generated rookie. */
const ROOKIE_PEAK_STAT_MIN = 40;
/** Inclusive range above the minimum (gives peak stats in [40, 80]). */
const ROOKIE_PEAK_STAT_RANGE = 40;
/** Full range for per-stat phase offset in years; offset is in [−half, +half]. */
const ROOKIE_PHASE_OFFSET_RANGE = 4;

// --- Annual turnover ---
/** Number of drivers retired and replaced each year (GDD §4). */
const ANNUAL_RETIREMENTS = 15;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Specification for a new rookie entry (ID and name supplied by caller). */
export interface RookieSpec {
  id: string;
  name: string;
}

/** Result returned by `advanceDriverYear`. */
export interface DriverYearResult {
  /** Updated full driver pool: aged survivors + new rookies. */
  drivers: Driver[];
  /** IDs of the 15 drivers removed from the pool this year. */
  retiredIds: string[];
  /** The 15 newly generated rookie drivers added to the pool. */
  rookies: Driver[];
}

// ---------------------------------------------------------------------------
// Stat curve
// ---------------------------------------------------------------------------

/**
 * Computes a driver's current stats from their sinusoidal career curve (GDD §4).
 *
 * Each stat peaks at `peakAge + phaseOffset[stat]` then declines symmetrically.
 * The shape function uses a raised cosine bounded by `STAT_FLOOR` so stats never
 * reach zero — even late-career drivers retain STAT_FLOOR × peakStat.
 *
 * Shape formula:
 *   t = age − (peakAge + phaseOffset)
 *   rawShape = max(0, (1 + cos(t × π / HALF_CAREER)) / 2)
 *   shape    = STAT_FLOOR + (1 − STAT_FLOOR) × rawShape
 *   stat     = round(peakStat × shape), clamped to [0, 100]
 */
export function calculateDriverStats(driver: Driver): DriverStats {
  const { peakAge, peakStats, phaseOffsets } = driver.curveParams;

  function statAtAge(peakStat: number, phaseOffset: number): number {
    const t = driver.age - (peakAge + phaseOffset);
    const rawShape = Math.max(0, (1 + Math.cos((t * Math.PI) / HALF_CAREER)) / 2);
    const shape = STAT_FLOOR + (1 - STAT_FLOOR) * rawShape;
    return Math.min(100, Math.max(0, Math.round(peakStat * shape)));
  }

  return {
    pace:        statAtAge(peakStats.pace,        phaseOffsets.pace),
    consistency: statAtAge(peakStats.consistency, phaseOffsets.consistency),
    stamina:     statAtAge(peakStats.stamina,      phaseOffsets.stamina),
    safety:      statAtAge(peakStats.safety,       phaseOffsets.safety),
    smoothness:  statAtAge(peakStats.smoothness,   phaseOffsets.smoothness),
  };
}

// ---------------------------------------------------------------------------
// Market value and salary
// ---------------------------------------------------------------------------

/** Returns the sum of all five driver stats (0–500). */
export function totalDriverStats(stats: DriverStats): number {
  return stats.pace + stats.consistency + stats.stamina + stats.safety + stats.smoothness;
}

/**
 * Calculates a driver's market value from their current stats.
 *
 * Uses a power-law curve from MIN_MARKET_VALUE (total=0) to MAX_MARKET_VALUE (total=500).
 * Market value is displayed on the driver market screen and informs salary calculation.
 */
export function calculateMarketValue(stats: DriverStats): number {
  const normalised = totalDriverStats(stats) / MAX_TOTAL_STATS;
  return Math.round(
    MIN_MARKET_VALUE +
      (MAX_MARKET_VALUE - MIN_MARKET_VALUE) * Math.pow(normalised, MARKET_VALUE_EXPONENT),
  );
}

/**
 * Calculates the base annual salary for a driver based on their current stats (GDD §4).
 *
 * Uses a steeper power-law curve than market value to better reflect real salary ranges:
 *   - Total ≈  50 stats → ~$650    (entry-level rookie)
 *   - Total ≈ 200 stats → ~$10k   (solid but unremarkable)
 *   - Total ≈ 350 stats → ~$52k   (strong midfield/front runner)
 *   - Total = 500 stats → $150k   (theoretically perfect driver)
 *
 * Note: these are balance placeholders. Adjust SALARY_EXPONENT and MIN/MAX_SALARY freely.
 */
export function calculateAnnualSalary(stats: DriverStats): number {
  const normalised = totalDriverStats(stats) / MAX_TOTAL_STATS;
  return Math.round(
    MIN_SALARY + (MAX_SALARY - MIN_SALARY) * Math.pow(normalised, SALARY_EXPONENT),
  );
}

// ---------------------------------------------------------------------------
// Contract system
// ---------------------------------------------------------------------------

/**
 * Returns the per-year contract salary after applying length discount and
 * Business-skill amplification (GDD §4).
 *
 * Total discount = contractLengthDiscount + (businessSkill / MAX_BUSINESS_SKILL) × MAX_BUSINESS_EXTRA_DISCOUNT
 *
 * Example: 3-year contract + Business skill 20 → 20% + 10% = 30% off base salary.
 */
export function calculateContractSalary(
  annualSalary: number,
  length: ContractLength,
  businessSkill: number,
): number {
  const lengthDiscount = CONTRACT_LENGTH_DISCOUNTS[length];
  const businessDiscount =
    (businessSkill / MAX_BUSINESS_SKILL) * MAX_BUSINESS_EXTRA_DISCOUNT;
  const totalDiscount = Math.min(1, lengthDiscount + businessDiscount);
  return Math.round(annualSalary * (1 - totalDiscount));
}

/**
 * Calculates the one-time cost to buy out an active contract early (GDD §4).
 *
 * Buyout cost = annualSalary × remainingYears × BUYOUT_MULTIPLIER (1.5×).
 */
export function calculateBuyoutCost(contract: Contract): number {
  return Math.round(contract.annualSalary * contract.remainingYears * BUYOUT_MULTIPLIER);
}

/**
 * Returns true if the driver currently has no active contract with any team.
 *
 * A driver with remainingYears = 0 is treated as expired and therefore a free agent —
 * the caller is responsible for decrementing remainingYears at year-end.
 */
export function isDriverFreeAgent(driverId: string, contracts: Contract[]): boolean {
  return !contracts.some((c) => c.driverId === driverId && c.remainingYears > 0);
}

// ---------------------------------------------------------------------------
// Rookie generation
// ---------------------------------------------------------------------------

/**
 * Generates a new 18-year-old rookie driver with randomised career potential (GDD §4).
 *
 * Peak stats are drawn uniformly from [ROOKIE_PEAK_STAT_MIN, ROOKIE_PEAK_STAT_MIN + ROOKIE_PEAK_STAT_RANGE].
 * At age 18 (≈12 years before a typical peak of 30), actual stats are roughly 25–35% of peak —
 * naturally "low starting stats" without needing a separate formula.
 *
 * @param spec    ID and name for the new driver.
 * @param random  Injectable random source.
 */
export function generateRookie(spec: RookieSpec, random: () => number): Driver {
  const peakAge =
    ROOKIE_PEAK_AGE_MIN + Math.floor(random() * (ROOKIE_PEAK_AGE_RANGE + 1));

  function randomPeakStat(): number {
    return Math.round(ROOKIE_PEAK_STAT_MIN + random() * ROOKIE_PEAK_STAT_RANGE);
  }

  function randomPhaseOffset(): number {
    return (random() - 0.5) * ROOKIE_PHASE_OFFSET_RANGE;
  }

  const peakStats: DriverStats = {
    pace:        randomPeakStat(),
    consistency: randomPeakStat(),
    stamina:     randomPeakStat(),
    safety:      randomPeakStat(),
    smoothness:  randomPeakStat(),
  };

  const phaseOffsets: DriverStats = {
    pace:        randomPhaseOffset(),
    consistency: randomPhaseOffset(),
    stamina:     randomPhaseOffset(),
    safety:      randomPhaseOffset(),
    smoothness:  randomPhaseOffset(),
  };

  const curveParams: DriverCurveParams = { peakAge, peakStats, phaseOffsets };
  const stub: Driver = {
    id: spec.id,
    name: spec.name,
    age: ROOKIE_AGE,
    curveParams,
    marketValue: 0,
  };
  const stats = calculateDriverStats(stub);
  return { ...stub, marketValue: calculateMarketValue(stats) };
}

// ---------------------------------------------------------------------------
// Annual lifecycle
// ---------------------------------------------------------------------------

/**
 * Advances the driver pool by one year (GDD §4):
 *
 *   1. Ages every driver by 1 year.
 *   2. Recalculates market value for each (stats change with age).
 *   3. Retires the ANNUAL_RETIREMENTS (15) drivers with the lowest total stat sum.
 *      Retirement applies regardless of contract status — the caller handles contract cleanup.
 *   4. Adds the provided rookies (generated externally so IDs and names are caller-controlled).
 *
 * @param drivers     Current full driver pool.
 * @param rookieSpecs Exactly ANNUAL_RETIREMENTS entries — new rookies to add.
 * @param random      Injectable random source (used only by generateRookie internally).
 */
export function advanceDriverYear(
  drivers: Driver[],
  rookieSpecs: RookieSpec[],
  random: () => number,
): DriverYearResult {
  // 1. Age every driver and recalculate market value
  const aged: Driver[] = drivers.map((d) => {
    const older = { ...d, age: d.age + 1 };
    const stats = calculateDriverStats(older);
    return { ...older, marketValue: calculateMarketValue(stats) };
  });

  // 2. Sort by total stat sum ascending; retire the bottom ANNUAL_RETIREMENTS
  const sorted = [...aged].sort((a, b) => {
    const statsA = calculateDriverStats(a);
    const statsB = calculateDriverStats(b);
    return totalDriverStats(statsA) - totalDriverStats(statsB);
  });

  const retirees = sorted.slice(0, ANNUAL_RETIREMENTS);
  const retiredIds = retirees.map((d) => d.id);
  const survivors = aged.filter((d) => !retiredIds.includes(d.id));

  // 3. Generate rookies from provided specs
  const rookies = rookieSpecs.map((spec) => generateRookie(spec, random));

  return {
    drivers: [...survivors, ...rookies],
    retiredIds,
    rookies,
  };
}
