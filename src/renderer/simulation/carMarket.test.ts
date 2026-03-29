import { describe, it, expect } from "vitest";
import {
  getNewCarListings,
  calculateCarMarketValue,
  calculateSalePrice,
  calculatePurchasePrice,
  generateUsedListing,
  generateUsedInventory,
  DEFAULT_USED_INVENTORY_COUNT,
} from "./carMarket";
import type { CarModel, CarInstance } from "../types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeModel(id: string, price: number): CarModel {
  return {
    id,
    name: `Car ${id}`,
    carClass: "B",
    tier: "gtRaceCar",
    price,
    baseStats: {
      power: 50, handling: 50, fuelEfficiency: 50, tyreDurability: 50,
      comfort: 50, reliability: 50, pitStopTime: 50, fuelCapacity: 100,
    },
    potentialStats: {
      power: 80, handling: 80, fuelEfficiency: 80, tyreDurability: 80,
      comfort: 80, reliability: 80, pitStopTime: 30, fuelCapacity: 100,
    },
    upgradePacks: [
      { type: "power",   cost: 10_000 },
      { type: "handling", cost: 8_000 },
      { type: "comfort",  cost: 5_000 },
    ],
  };
}

function makeInstance(
  modelId: string,
  age = 0,
  condition = 100,
  upgrades = { power: false, handling: false, comfort: false },
): CarInstance {
  return { id: "c1", modelId, age, condition, installedUpgrades: upgrades };
}

const model = makeModel("m1", 100_000);

// ---------------------------------------------------------------------------
// getNewCarListings
// ---------------------------------------------------------------------------

describe("getNewCarListings", () => {
  it("returns one listing per model", () => {
    const models = [makeModel("a", 10_000), makeModel("b", 20_000)];
    expect(getNewCarListings(models)).toHaveLength(2);
  });

  it("each listing has the model's list price", () => {
    const models = [makeModel("a", 15_000)];
    expect(getNewCarListings(models)[0].price).toBe(15_000);
  });

  it("returns empty array for empty catalogue", () => {
    expect(getNewCarListings([])).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// calculateCarMarketValue
// ---------------------------------------------------------------------------

describe("calculateCarMarketValue", () => {
  it("new car (age 0, 100% condition, no upgrades) is close to list price", () => {
    const car = makeInstance("m1", 0, 100);
    const value = calculateCarMarketValue(car, model);
    // ageFactor=1, condFactor = 0.5 + 0.5*1 = 1.0, so value = 100_000 * 1 * 1 = 100_000
    expect(value).toBe(100_000);
  });

  it("older car is worth less than a newer car", () => {
    const newer = makeInstance("m1", 1, 100);
    const older = makeInstance("m1", 5, 100);
    expect(calculateCarMarketValue(newer, model)).toBeGreaterThan(
      calculateCarMarketValue(older, model),
    );
  });

  it("lower condition reduces value", () => {
    const good = makeInstance("m1", 3, 90);
    const worn = makeInstance("m1", 3, 30);
    expect(calculateCarMarketValue(good, model)).toBeGreaterThan(
      calculateCarMarketValue(worn, model),
    );
  });

  it("installed upgrades add value", () => {
    const base = makeInstance("m1", 3, 80, { power: false, handling: false, comfort: false });
    const upgraded = makeInstance("m1", 3, 80, { power: true, handling: false, comfort: false });
    expect(calculateCarMarketValue(upgraded, model)).toBeGreaterThan(
      calculateCarMarketValue(base, model),
    );
  });

  it("value is always positive", () => {
    const extreme = makeInstance("m1", 100, 0); // extremely old, 0 condition
    expect(calculateCarMarketValue(extreme, model)).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// calculateSalePrice
// ---------------------------------------------------------------------------

describe("calculateSalePrice", () => {
  it("sale price is less than market value (dealer margin)", () => {
    const car = makeInstance("m1", 2, 80);
    const market = calculateCarMarketValue(car, model);
    const sale = calculateSalePrice(car, model, 0);
    expect(sale).toBeLessThan(market);
  });

  it("higher Business skill increases sale price", () => {
    const car = makeInstance("m1", 2, 80);
    const lowBusiness = calculateSalePrice(car, model, 0);
    const highBusiness = calculateSalePrice(car, model, 20);
    expect(highBusiness).toBeGreaterThan(lowBusiness);
  });

  it("sale price scales with Business skill", () => {
    const car = makeInstance("m1", 0, 100);
    const noSkill = calculateSalePrice(car, model, 0);
    const maxSkill = calculateSalePrice(car, model, 20);
    // Max skill gives 15% bonus on top of base resale (60%)
    expect(maxSkill / noSkill).toBeCloseTo(1.15, 1);
  });

  it("sale price is always positive", () => {
    const car = makeInstance("m1", 10, 10);
    expect(calculateSalePrice(car, model, 0)).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// calculatePurchasePrice
// ---------------------------------------------------------------------------

describe("calculatePurchasePrice", () => {
  it("no business skill returns list price unchanged", () => {
    expect(calculatePurchasePrice(100_000, 0)).toBe(100_000);
  });

  it("max business skill gives maximum discount", () => {
    const discounted = calculatePurchasePrice(100_000, 20);
    expect(discounted).toBeLessThan(100_000);
    expect(discounted).toBeGreaterThan(80_000); // max 15% off
  });

  it("higher business skill gives a larger discount", () => {
    const low = calculatePurchasePrice(100_000, 5);
    const high = calculatePurchasePrice(100_000, 15);
    expect(high).toBeLessThan(low);
  });
});

// ---------------------------------------------------------------------------
// generateUsedListing
// ---------------------------------------------------------------------------

describe("generateUsedListing", () => {
  it("age is between 1 and 10", () => {
    for (let i = 0; i < 20; i++) {
      let call = 0;
      const result = generateUsedListing(model, () => {
        call++;
        return call === 1 ? 0.0 : 0.5; // first call → age = 1
      });
      expect(result.age).toBeGreaterThanOrEqual(1);
    }
    const maxAge = generateUsedListing(model, () => 0.99);
    expect(maxAge.age).toBeLessThanOrEqual(10);
  });

  it("condition is between 30 and 90", () => {
    // Force age (call 1), then condition (call 2)
    let call = 0;
    const low = generateUsedListing(model, () => {
      call++;
      return call === 1 ? 0.5 : 0; // condition call → 30
    });
    expect(low.condition).toBeGreaterThanOrEqual(30);

    call = 0;
    const high = generateUsedListing(model, () => {
      call++;
      return call === 1 ? 0.5 : 1; // condition call → 90
    });
    expect(high.condition).toBeLessThanOrEqual(90);
  });

  it("price is positive", () => {
    const listing = generateUsedListing(model, () => 0.5);
    expect(listing.price).toBeGreaterThan(0);
  });

  it("listing price is less than the new car list price", () => {
    // Any used car (age ≥ 1) should be cheaper than new
    const listing = generateUsedListing(model, () => 0.5);
    expect(listing.price).toBeLessThan(model.price);
  });

  it("modelId matches the model", () => {
    const listing = generateUsedListing(model, () => 0.5);
    expect(listing.modelId).toBe(model.id);
  });
});

// ---------------------------------------------------------------------------
// generateUsedInventory
// ---------------------------------------------------------------------------

describe("generateUsedInventory", () => {
  const models = [makeModel("a", 50_000), makeModel("b", 200_000)];

  it("returns the requested count", () => {
    const inventory = generateUsedInventory(models, 5, () => 0.5);
    expect(inventory).toHaveLength(5);
  });

  it("returns DEFAULT_USED_INVENTORY_COUNT when count is omitted", () => {
    const inventory = generateUsedInventory(models, DEFAULT_USED_INVENTORY_COUNT, () => 0.5);
    expect(inventory).toHaveLength(DEFAULT_USED_INVENTORY_COUNT);
  });

  it("all listings have a positive price", () => {
    const inventory = generateUsedInventory(models, 10, () => 0.5);
    for (const listing of inventory) {
      expect(listing.price).toBeGreaterThan(0);
    }
  });

  it("listings come from the provided models", () => {
    const validIds = new Set(models.map((m) => m.id));
    const inventory = generateUsedInventory(models, 10, Math.random);
    for (const listing of inventory) {
      expect(validIds.has(listing.modelId)).toBe(true);
    }
  });
});
