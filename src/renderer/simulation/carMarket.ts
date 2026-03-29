import type { CarModel, CarInstance, InstalledUpgrades, NewCarListing, UsedCarListing } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants
// ---------------------------------------------------------------------------

/** Fraction of original value lost per year of age. */
const AGE_DEPRECIATION_PER_YEAR = 0.08;
/** Minimum age factor — a car never drops below this due to age alone. */
const MIN_AGE_FACTOR = 0.10;
/** Minimum condition factor — even a 0%-condition car retains this fraction of aged value. */
const CONDITION_FLOOR = 0.50;
/** Fraction of each installed upgrade pack's cost recovered in the used listing price. */
const UPGRADE_RESALE_FRACTION = 0.50;
/** Fraction of market value the selling team receives (dealer margin). */
const BASE_RESALE_FRACTION = 0.60;
/** Maximum additional sale price from max Business skill (15% above base resale). */
const MAX_SALE_SKILL_BONUS = 0.15;
/** Maximum discount on car purchases at max Business skill. */
const MAX_PURCHASE_DISCOUNT = 0.15;
/** Maximum Business skill level. */
const MAX_BUSINESS_SKILL = 20;
/** Oldest car that can appear in a generated used inventory (years). */
const MAX_USED_AGE = 10;
/** Minimum condition of a generated used car. */
const USED_CONDITION_MIN = 30;
/** Maximum condition of a generated used car. */
const USED_CONDITION_MAX = 90;
/** Per-pack probability that a randomly generated used car has that pack installed. */
const USED_UPGRADE_PROBABILITY = 0.33;
/** Default number of used listings generated per year. */
export const DEFAULT_USED_INVENTORY_COUNT = 20;

// ---------------------------------------------------------------------------
// New car listings
// ---------------------------------------------------------------------------

/** Returns one listing per model, all at list price (age 0, full condition, no upgrades). */
export function getNewCarListings(models: CarModel[]): NewCarListing[] {
  return models.map((m) => ({ modelId: m.id, price: m.price }));
}

// ---------------------------------------------------------------------------
// Market value (intrinsic value of a used car instance)
// ---------------------------------------------------------------------------

/**
 * Calculates the intrinsic market value of a used car.
 *
 * Formula:
 *   ageFactor    = max(MIN_AGE_FACTOR, 1 − age × AGE_DEPRECIATION_PER_YEAR)
 *   condFactor   = CONDITION_FLOOR + (1 − CONDITION_FLOOR) × (condition / 100)
 *   baseValue    = model.price × ageFactor × condFactor
 *   upgradeBonus = sum of (pack.cost × UPGRADE_RESALE_FRACTION) for installed packs
 *   marketValue  = round(baseValue + upgradeBonus)
 */
export function calculateCarMarketValue(car: CarInstance, model: CarModel): number {
  const ageFactor = Math.max(
    MIN_AGE_FACTOR,
    1 - car.age * AGE_DEPRECIATION_PER_YEAR,
  );
  const conditionFactor =
    CONDITION_FLOOR + (1 - CONDITION_FLOOR) * (car.condition / 100);
  const baseValue = model.price * ageFactor * conditionFactor;

  let upgradeBonus = 0;
  for (const pack of model.upgradePacks) {
    if (car.installedUpgrades[pack.type]) {
      upgradeBonus += pack.cost * UPGRADE_RESALE_FRACTION;
    }
  }

  return Math.round(baseValue + upgradeBonus);
}

// ---------------------------------------------------------------------------
// Sale price (what a team receives when selling)
// ---------------------------------------------------------------------------

/**
 * Calculates the sale price a team receives when selling a car.
 *
 * salePrice = marketValue × BASE_RESALE_FRACTION × (1 + skillBonus)
 *
 * @param businessSkill  Seller's Business skill (0 = no bonus; typically 0 for AI teams).
 */
export function calculateSalePrice(
  car: CarInstance,
  model: CarModel,
  businessSkill: number = 0,
): number {
  const marketValue = calculateCarMarketValue(car, model);
  const skillBonus =
    (businessSkill / MAX_BUSINESS_SKILL) * MAX_SALE_SKILL_BONUS;
  return Math.round(marketValue * BASE_RESALE_FRACTION * (1 + skillBonus));
}

// ---------------------------------------------------------------------------
// Purchase price (what a team pays after Business discount)
// ---------------------------------------------------------------------------

/**
 * Applies a Business skill discount to any car purchase (new or used).
 *
 * discount = (businessSkill / MAX_BUSINESS_SKILL) × MAX_PURCHASE_DISCOUNT
 *
 * @param businessSkill  Buyer's Business skill (0 = no discount; typically 0 for AI teams).
 */
export function calculatePurchasePrice(
  listPrice: number,
  businessSkill: number = 0,
): number {
  const discount =
    (businessSkill / MAX_BUSINESS_SKILL) * MAX_PURCHASE_DISCOUNT;
  return Math.round(listPrice * (1 - discount));
}

// ---------------------------------------------------------------------------
// Used car generation
// ---------------------------------------------------------------------------

/**
 * Generates a single used car listing from a model with randomised age, condition,
 * and upgrade state.
 */
export function generateUsedListing(
  model: CarModel,
  random: () => number,
): UsedCarListing {
  const age = 1 + Math.floor(random() * MAX_USED_AGE); // [1, MAX_USED_AGE]
  const condition = Math.round(
    USED_CONDITION_MIN +
      random() * (USED_CONDITION_MAX - USED_CONDITION_MIN),
  ); // [USED_CONDITION_MIN, USED_CONDITION_MAX]
  const installedUpgrades: InstalledUpgrades = {
    power:   random() < USED_UPGRADE_PROBABILITY,
    handling: random() < USED_UPGRADE_PROBABILITY,
    comfort:  random() < USED_UPGRADE_PROBABILITY,
  };

  const tempInstance: CarInstance = {
    id: "",
    modelId: model.id,
    age,
    condition,
    installedUpgrades,
  };
  const price = calculateCarMarketValue(tempInstance, model);
  const id = `used-${model.id}-${Math.floor(random() * 1_000_000_000)}`;

  return { id, modelId: model.id, age, condition, installedUpgrades, price };
}

/**
 * Generates a rotating used car inventory by randomly sampling from all models.
 *
 * @param models  Full car model catalogue.
 * @param count   Number of listings to generate (default: DEFAULT_USED_INVENTORY_COUNT).
 * @param random  Injectable random source.
 */
export function generateUsedInventory(
  models: CarModel[],
  count: number = DEFAULT_USED_INVENTORY_COUNT,
  random: () => number,
): UsedCarListing[] {
  if (models.length === 0) return [];
  return Array.from({ length: count }, () => {
    const model = models[Math.floor(random() * models.length)];
    return generateUsedListing(model, random);
  });
}
