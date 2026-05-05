import type { CarStats, DriverStats } from "../types";
import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

/** Lap time for a car with maximum possible effective stats (seconds). */
const FASTEST_LAP_TIME = 26.0;
/** Additional seconds added as stats approach zero. Defines the spread across the field. */
const LAP_TIME_SPREAD = 20.0;

/** Power contributes 60% and handling 40% to the combined speed score. */
const POWER_WEIGHT = 0.6;
const HANDLING_WEIGHT = 0.4;

/** Driver pace at which the pace modifier equals 1.0 (no adjustment). */
const REFERENCE_PACE = 50;
/**
 * Fractional lap time change per pace point above or below the reference.
 * At 0.002: a 100-Pace driver is 10% faster than a 0-Pace driver.
 */
const PACE_EFFECT_PER_POINT = 0.002;

/** At 100% fatigue, driver loses this fraction of their effective pace. */
const FATIGUE_PACE_LOSS = 0.3;
/** At 100% fatigue, driver loses this fraction of their effective consistency. */
const FATIGUE_CONSISTENCY_LOSS = 0.5;

/** Flat lap time multipliers per instruction mode. */
const MODE_LAP_TIME_MODIFIER: Record<InstructionMode, number> = {
  push: 0.97,
  normal: 1.0,
  conserve: 1.03,
};

/** Maximum fractional lap time penalty from fully worn tyres (100% wear). */
const MAX_TYRE_WEAR_PENALTY = 0.10;

/**
 * Fractional lap time penalty per litre of fuel on board. Calibrated for the
 * time-bounded race: at full ~70 L tank a class-A car runs ~3.5 % slower than
 * empty, which is meaningful but doesn't crowd out the 1440 s lap budget.
 */
const FUEL_PENALTY_PER_LITRE = 0.0005;

/**
 * Maximum lap time variance (± seconds) at 0% consistency, Normal mode, 0% fatigue.
 * Actual range scales down with higher consistency and Conserve mode.
 */
const MAX_VARIANCE_SECONDS = 2.0;

/** Variance multiplier per instruction mode (Push spreads variance; Conserve tightens it). */
const MODE_VARIANCE_SCALE: Record<InstructionMode, number> = {
  push: 1.5,
  normal: 1.0,
  conserve: 0.5,
};

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

/**
 * Calculates a single lap time for a car (GDD §2).
 *
 * Formula:
 *   base_lap_time (from effectiveStats power + handling)
 *   × driver_pace_modifier (pace stat, adjusted for fatigue)
 *   × instruction_mode_modifier (Push < 1.0, Normal = 1.0, Conserve > 1.0)
 *   × tyre_wear_modifier (worn tyres = slower)
 *   × fuel_load_modifier (heavier fuel load = slower)
 *   + random_variance (scaled by consistency and instruction mode)
 *
 * Design note — car_condition_modifier:
 *   The GDD formula includes a car_condition_modifier. Since effectiveStats is
 *   the output of calculateEffectiveStats (Phase 2A), which already applies the
 *   condition fraction to power and handling, the condition effect is captured
 *   implicitly in base_lap_time. Applying it again here would double-count it.
 *   See Implementer → PM handoff 2026-03-29 for PM awareness.
 *
 * @param effectiveStats  Car stats after upgrades, age, and condition (from calculateEffectiveStats).
 * @param driverStats     Driver's base stats (pace, consistency, etc., 0–100).
 * @param driverFatigue   Driver's current fatigue level (0–100).
 * @param instructionMode Current driver instruction mode.
 * @param tyreWear        Current tyre wear (0 = fresh, 100 = fully worn).
 * @param fuelLoad        Fuel remaining in litres.
 * @param random          Injectable random source — pass a deterministic function in tests.
 * @returns Lap time in seconds.
 */
export function calculateLapTime(
  effectiveStats: CarStats,
  driverStats: DriverStats,
  driverFatigue: number,
  instructionMode: InstructionMode,
  tyreWear: number,
  fuelLoad: number,
  random: () => number = Math.random,
): number {
  // 1. Base lap time — higher combined speed score produces a shorter lap time.
  const speedScore =
    effectiveStats.power * POWER_WEIGHT + effectiveStats.handling * HANDLING_WEIGHT;
  const speedFraction = Math.max(0, Math.min(1, speedScore / 100));
  const base = FASTEST_LAP_TIME + (1 - speedFraction) * LAP_TIME_SPREAD;

  // 2. Driver pace modifier — fatigue reduces the effective pace before applying.
  const effectivePace = driverStats.pace * (1 - (driverFatigue / 100) * FATIGUE_PACE_LOSS);
  const paceMod = 1.0 - (effectivePace - REFERENCE_PACE) * PACE_EFFECT_PER_POINT;

  // 3. Instruction mode multiplier.
  const modeMod = MODE_LAP_TIME_MODIFIER[instructionMode];

  // 4. Tyre wear multiplier — increases linearly with wear.
  const tyreMod = 1.0 + (tyreWear / 100) * MAX_TYRE_WEAR_PENALTY;

  // 5. Fuel load multiplier — each litre adds a small fraction.
  const fuelMod = 1.0 + fuelLoad * FUEL_PENALTY_PER_LITRE;

  // 6. Random variance — fatigue reduces effective consistency, widening variance.
  const effectiveConsistency =
    driverStats.consistency * (1 - (driverFatigue / 100) * FATIGUE_CONSISTENCY_LOSS);
  const varianceRange =
    MAX_VARIANCE_SECONDS *
    (1 - effectiveConsistency / 100) *
    MODE_VARIANCE_SCALE[instructionMode];
  const variance = (random() - 0.5) * 2 * varianceRange;

  return base * paceMod * modeMod * tyreMod * fuelMod + variance;
}
