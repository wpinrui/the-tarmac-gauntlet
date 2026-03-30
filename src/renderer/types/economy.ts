import type { CarInstance, CarTier } from "./car";

// --- Prize Money ---

export interface PrizeScheduleEntry {
  position: number;    // 1–100
  amount: number;      // Prize money in dollars
}

// --- Transactions ---

export type TransactionCategory =
  | "carPurchase"
  | "carSale"
  | "upgrade"
  | "spareParts"
  | "tyres"
  | "driverSalary"
  | "driverBuyout"
  | "crewCost"
  | "prizeMoney"
  | "fuelCost";

export interface TransactionRecord {
  year: number;
  category: TransactionCategory;
  amount: number;       // positive = income, negative = expense
  description: string;
}

// --- Car Markets ---

export interface NewCarListing {
  modelId: string;
  price: number;       // Always list price for new cars
}

export interface UsedCarListing {
  id: string;
  modelId: string;
  age: number;
  condition: number;
  installedUpgrades: CarInstance["installedUpgrades"];
  price: number;       // Discounted based on age/condition/upgrades
}

export interface CarMarket {
  newListings: NewCarListing[];
  usedListings: UsedCarListing[];  // Rotates each year
}

// --- Consumable Costs (scale by car tier) ---

export interface TierCosts {
  tier: CarTier;
  sparePartCost: number;  // Cost per unit of spare parts
  tyreSetCost: number;    // Cost per tyre set
}

// --- Fuel ---

export interface FuelConfig {
  costPerLitre: number;
}

// --- Economy Config (all configurable rates) ---

export interface EconomyConfig {
  tierCosts: TierCosts[];
  fuelConfig: FuelConfig;
  crewCostPerMember: number;     // $2,000 per GDD
  contractDiscounts: {
    twoYear: number;   // 0.10 (10%)
    threeYear: number; // 0.20 (20%)
  };
  buyoutMultiplier: number;      // 1.5 per GDD
}
