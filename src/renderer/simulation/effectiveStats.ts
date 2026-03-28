import type { CarInstance, CarModel, CarStats } from "../types";

// Age reduces all stats gradually. 2% per year, capped at 50% (reached at age 25).
const AGE_PENALTY_PER_YEAR = 0.02;
const MAX_AGE_PENALTY = 0.5;

// Prevents pitStopTime from reaching infinity when condition approaches 0.
const MIN_COMBINED_MULTIPLIER = 0.1;

/**
 * Returns the effective stats for a car at a given age and condition.
 *
 * Calculation chain (per stat):
 *   1. Start with base value; use potential if the relevant upgrade pack is installed.
 *   2. Multiply by the age fraction (1 - agePenalty), capped.
 *   3. Multiply by condition fraction (condition / 100).
 *
 * pitStopTime is inverted (lower = better), so the combined multiplier is applied as a
 * divisor instead — lower condition and greater age both increase the pit stop duration.
 *
 * Upgrade pack → stat mapping (GDD §4):
 *   Power Pack    : power, fuelEfficiency
 *   Handling Pack : handling, tyreDurability
 *   Comfort Pack  : comfort
 *   (reliability, pitStopTime, fuelCapacity have no upgrade pack — potential equals base)
 */
export function calculateEffectiveStats(
  instance: CarInstance,
  model: CarModel,
): CarStats {
  const b = model.baseStats;
  const p = model.potentialStats;
  const u = instance.installedUpgrades;

  // Step 1: resolve upgrade-adjusted values
  const power = u.power ? p.power : b.power;
  const fuelEfficiency = u.power ? p.fuelEfficiency : b.fuelEfficiency;
  const handling = u.handling ? p.handling : b.handling;
  const tyreDurability = u.handling ? p.tyreDurability : b.tyreDurability;
  const comfort = u.comfort ? p.comfort : b.comfort;
  const reliability = b.reliability;
  const pitStopTime = b.pitStopTime;
  const fuelCapacity = b.fuelCapacity;

  // Step 2: combined multiplier from age and condition
  const agePenalty = Math.min(
    MAX_AGE_PENALTY,
    instance.age * AGE_PENALTY_PER_YEAR,
  );
  const ageFraction = 1 - agePenalty;
  const conditionFraction = instance.condition / 100;
  const combined = conditionFraction * ageFraction;

  // Step 3: apply to each stat
  return {
    power: power * combined,
    fuelEfficiency: fuelEfficiency * combined,
    handling: handling * combined,
    tyreDurability: tyreDurability * combined,
    comfort: comfort * combined,
    reliability: reliability * combined,
    // pitStopTime is inverted: degraded cars take longer in the pits.
    pitStopTime: pitStopTime / Math.max(MIN_COMBINED_MULTIPLIER, combined),
    fuelCapacity: fuelCapacity * combined,
  };
}
