// --- Car Stats ---

export interface CarStats {
  power: number;          // Straight-line speed (higher = faster)
  handling: number;       // Cornering ability (higher = faster)
  fuelEfficiency: number; // Fuel consumed per lap (higher = less fuel)
  tyreDurability: number; // Tyre wear per lap (higher = less wear)
  comfort: number;        // Driver fatigue rate (higher = slower fatigue)
  reliability: number;    // Issue/failure resistance (higher = fewer problems)
  pitStopTime: number;    // Base pit stop duration (LOWER = faster)
  fuelCapacity: number;   // Max litres of fuel (higher = more fuel)
}

// --- Upgrade Packs ---

export type UpgradePackType = "power" | "handling" | "comfort";

export interface UpgradePackDefinition {
  type: UpgradePackType;
  cost: number;
}

// --- Car Classes ---

export type CarClass = "F" | "E" | "D" | "C" | "B" | "A" | "F1";

/** @deprecated Use CarClass instead. Kept for economy config compatibility. */
export type CarTier =
  | "junkyard"
  | "dailyDriver"
  | "usedSports"
  | "trackCar"
  | "gtRaceCar"
  | "supercar"
  | "hypercar";

// --- Car Model (template) ---

export interface CarModel {
  id: string;
  name: string;
  carClass: CarClass;
  tier: CarTier;
  price: number;
  baseStats: CarStats;
  potentialStats: CarStats;
  upgradePacks: UpgradePackDefinition[];
}

// --- Car Instance (owned by a team) ---

export interface InstalledUpgrades {
  power: boolean;
  handling: boolean;
  comfort: boolean;
}

export interface CarInstance {
  id: string;
  modelId: string;
  age: number;              // Years since purchase (0 = new)
  condition: number;        // 0–100%
  installedUpgrades: InstalledUpgrades;
}
