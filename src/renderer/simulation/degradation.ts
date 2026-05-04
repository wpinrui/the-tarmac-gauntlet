import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Instruction mode scale (same across all four systems) ---
// Push = 30% faster degradation; Conserve = 30% slower.
const MODE_DEGRADATION_SCALE: Record<InstructionMode, number> = {
  push: 1.3,
  normal: 1.0,
  conserve: 0.7,
};

// --- Tyre wear ---
// Baseline calibrated for the time-bounded sim (1440 s race budget): a top
// class-A car wears ~6 wear/lap, giving ~13-lap tyre stints and ~3 stops on
// tyres alone — leaving room for ~48 laps inside the budget. Drop this and
// the leader does too few laps; raise it and pit overhead crowds out laps.
/** Wear added per lap (0–100 scale) with baseline stats and Normal mode. */
const BASE_TYRE_WEAR_PER_LAP = 8;
/**
 * Maximum fractional reduction in wear rate from combined Tyre Durability + Smoothness stats.
 * At both stats = 100, wear rate is halved.
 */
const MAX_TYRE_STAT_REDUCTION = 0.5;

// --- Fuel consumption ---
// Baseline calibrated for time-bounded sim. At ~6 L/lap a class-A 70 L tank
// covers ~12 laps, so refuelling is on a similar cadence to tyres rather
// than driving extra stops. Aggressive reductions here also shorten the
// refuel task time (fewer litres × per-litre seconds).
/** Litres consumed per lap with baseline Fuel Efficiency and Normal mode. */
const BASE_FUEL_PER_LAP = 7;
/** Maximum fractional reduction in fuel consumption from Fuel Efficiency. */
const MAX_FUEL_STAT_REDUCTION = 0.5;

// --- Driver fatigue ---
/** Fatigue built up per lap (0–100 scale) with baseline stats and Normal mode. */
const BASE_FATIGUE_PER_LAP = 3;
/** Maximum fractional reduction in fatigue rate from combined Comfort + Stamina stats. */
const MAX_FATIGUE_STAT_REDUCTION = 0.5;

// --- Car condition ---
// Condition multiplies power/handling directly via effectiveStats — at 1.0 %/lap
// a 48-lap race drops a fresh car to 52 %, which compresses peak-class lap
// times by ~25 %. That single knob alone can eat 5+ laps off the leader's
// total. 0.4 keeps a fresh car above 80 % across one race; cars still
// deteriorate meaningfully across the season's multi-race timescale.
/** Condition lost per lap (0–100 scale) at age 0, skill 0, Normal mode. */
const BASE_CONDITION_DECAY_PER_LAP = 0.25;
/** Fractional increase in condition decay rate per year of car age. */
const AGE_CONDITION_RATE = 0.05;
/** Cap on the age multiplier — prevents unbounded decay on very old cars (mirrors effectiveStats.ts). */
const MAX_AGE_FACTOR = 2.0; // reached at age 20, unchanged beyond
/** Maximum of the Engineer skill stat (matches PlayerSkills.engineer in team.ts). */
const MAX_ENGINEER_SKILL = 20;
/** Maximum fractional reduction in decay from Engineer skill. */
const MAX_ENGINEER_REDUCTION = 0.4; // skill 20 → 40% slower condition decay

// ---------------------------------------------------------------------------
// Per-lap update functions
// ---------------------------------------------------------------------------

/**
 * Returns the tyre wear value after one lap (GDD §2).
 *
 * Rate is increased by low Tyre Durability and Smoothness; Push mode accelerates it.
 *
 * @param current       Current tyre wear (0 = fresh, 100 = fully worn).
 * @param tyreDurability Car's Tyre Durability effective stat (0–100).
 * @param smoothness    Driver's Smoothness stat (0–100).
 * @param mode          Current driver instruction mode.
 * @returns New tyre wear, clamped to [0, 100].
 */
export function updateTyreWear(
  current: number,
  tyreDurability: number,
  smoothness: number,
  mode: InstructionMode,
): number {
  const statFactor =
    1 - ((tyreDurability + smoothness) / 200) * MAX_TYRE_STAT_REDUCTION;
  const wearPerLap = BASE_TYRE_WEAR_PER_LAP * statFactor * MODE_DEGRADATION_SCALE[mode];
  return Math.min(100, current + wearPerLap);
}

/**
 * Returns the fuel level after one lap (GDD §2).
 *
 * Consumption is reduced by high Fuel Efficiency; Push mode burns more fuel.
 *
 * @param current        Current fuel level in litres.
 * @param fuelEfficiency Car's Fuel Efficiency effective stat (0–100).
 * @param mode           Current driver instruction mode.
 * @returns New fuel level in litres, clamped to a minimum of 0.
 */
export function updateFuelLevel(
  current: number,
  fuelEfficiency: number,
  mode: InstructionMode,
): number {
  const statFactor = 1 - (fuelEfficiency / 100) * MAX_FUEL_STAT_REDUCTION;
  const consumed = BASE_FUEL_PER_LAP * statFactor * MODE_DEGRADATION_SCALE[mode];
  return Math.max(0, current - consumed);
}

/**
 * Returns the driver fatigue level after one lap (GDD §2).
 *
 * Buildup is reduced by high Comfort (car) and Stamina (driver); Push mode increases it.
 * The fatigue counter resets on a driver swap — that reset belongs to Phase 2D.
 *
 * @param current   Current fatigue level (0 = fresh, 100 = fully fatigued).
 * @param comfort   Car's Comfort effective stat (0–100).
 * @param stamina   Driver's Stamina stat (0–100).
 * @param mode      Current driver instruction mode.
 * @returns New fatigue level, clamped to [0, 100].
 */
export function updateDriverFatigue(
  current: number,
  comfort: number,
  stamina: number,
  mode: InstructionMode,
): number {
  const statFactor = 1 - ((comfort + stamina) / 200) * MAX_FATIGUE_STAT_REDUCTION;
  const fatiguePerLap = BASE_FATIGUE_PER_LAP * statFactor * MODE_DEGRADATION_SCALE[mode];
  return Math.min(100, current + fatiguePerLap);
}

/**
 * Returns the car condition after one lap (GDD §3).
 *
 * Decay increases with car age and is reduced by the Engineer skill; Push accelerates it.
 * Condition cannot be restored mid-race — that is a between-races operation.
 *
 * @param current       Current condition (0–100).
 * @param carAge        Car's age in years (0 = new).
 * @param engineerSkill Player's Engineer skill level (0–20; use 0 for AI teams).
 * @param mode          Current driver instruction mode.
 * @returns New condition, clamped to [0, 100].
 */
export function updateCarCondition(
  current: number,
  carAge: number,
  engineerSkill: number,
  mode: InstructionMode,
): number {
  const ageFactor = Math.min(MAX_AGE_FACTOR, 1 + carAge * AGE_CONDITION_RATE);
  const engineerFactor = 1 - (engineerSkill / MAX_ENGINEER_SKILL) * MAX_ENGINEER_REDUCTION;
  const decayPerLap =
    BASE_CONDITION_DECAY_PER_LAP * ageFactor * engineerFactor * MODE_DEGRADATION_SCALE[mode];
  return Math.max(0, current - decayPerLap);
}
