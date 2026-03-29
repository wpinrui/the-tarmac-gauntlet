import type { CarModel } from "../types";

/**
 * Placeholder car model catalogue (~20 models) spanning the full price spectrum.
 * All numeric stats are balance placeholders.
 */
export const CAR_MODELS: CarModel[] = [
  // --- Junkyard shitboxes ($750–$1,500) ---
  {
    id: "junk-rustbucket",
    name: "Rustbucket 1.3",
    tier: "junkyard",
    price: 750,
    baseStats:      { power: 8,  handling: 6,  fuelEfficiency: 12, tyreDurability: 10, comfort: 4,  reliability: 5,  pitStopTime: 80, fuelCapacity: 40 },
    potentialStats: { power: 14, handling: 10, fuelEfficiency: 16, tyreDurability: 14, comfort: 8,  reliability: 5,  pitStopTime: 80, fuelCapacity: 40 },
    upgradePacks: [{ type: "power", cost: 200 }, { type: "handling", cost: 180 }, { type: "comfort", cost: 120 }],
  },
  {
    id: "junk-banger",
    name: "Banger V4",
    tier: "junkyard",
    price: 1200,
    baseStats:      { power: 10, handling: 8,  fuelEfficiency: 14, tyreDurability: 12, comfort: 6,  reliability: 8,  pitStopTime: 75, fuelCapacity: 42 },
    potentialStats: { power: 18, handling: 14, fuelEfficiency: 18, tyreDurability: 16, comfort: 10, reliability: 8,  pitStopTime: 75, fuelCapacity: 42 },
    upgradePacks: [{ type: "power", cost: 300 }, { type: "handling", cost: 250 }, { type: "comfort", cost: 180 }],
  },
  {
    id: "junk-heap",
    name: "Scrapyard Special",
    tier: "junkyard",
    price: 1500,
    baseStats:      { power: 12, handling: 10, fuelEfficiency: 15, tyreDurability: 13, comfort: 7,  reliability: 10, pitStopTime: 72, fuelCapacity: 44 },
    potentialStats: { power: 20, handling: 16, fuelEfficiency: 19, tyreDurability: 17, comfort: 12, reliability: 10, pitStopTime: 72, fuelCapacity: 44 },
    upgradePacks: [{ type: "power", cost: 400 }, { type: "handling", cost: 350 }, { type: "comfort", cost: 250 }],
  },

  // --- Old daily drivers ($2,000–$5,000) ---
  {
    id: "daily-sedan",
    name: "Commuter Sedan",
    tier: "dailyDriver",
    price: 2500,
    baseStats:      { power: 18, handling: 16, fuelEfficiency: 22, tyreDurability: 20, comfort: 14, reliability: 18, pitStopTime: 65, fuelCapacity: 48 },
    potentialStats: { power: 28, handling: 24, fuelEfficiency: 26, tyreDurability: 24, comfort: 20, reliability: 18, pitStopTime: 65, fuelCapacity: 48 },
    upgradePacks: [{ type: "power", cost: 600 }, { type: "handling", cost: 500 }, { type: "comfort", cost: 400 }],
  },
  {
    id: "daily-hatch",
    name: "Hot Hatch GTI",
    tier: "dailyDriver",
    price: 4000,
    baseStats:      { power: 22, handling: 22, fuelEfficiency: 20, tyreDurability: 18, comfort: 12, reliability: 20, pitStopTime: 62, fuelCapacity: 46 },
    potentialStats: { power: 34, handling: 32, fuelEfficiency: 24, tyreDurability: 22, comfort: 16, reliability: 20, pitStopTime: 62, fuelCapacity: 46 },
    upgradePacks: [{ type: "power", cost: 900 }, { type: "handling", cost: 800 }, { type: "comfort", cost: 500 }],
  },
  {
    id: "daily-wagon",
    name: "Estate Touring",
    tier: "dailyDriver",
    price: 5000,
    baseStats:      { power: 24, handling: 20, fuelEfficiency: 24, tyreDurability: 22, comfort: 18, reliability: 22, pitStopTime: 60, fuelCapacity: 52 },
    potentialStats: { power: 36, handling: 30, fuelEfficiency: 28, tyreDurability: 26, comfort: 24, reliability: 22, pitStopTime: 60, fuelCapacity: 52 },
    upgradePacks: [{ type: "power", cost: 1100 }, { type: "handling", cost: 900 }, { type: "comfort", cost: 700 }],
  },

  // --- Used sports cars ($8,000–$20,000) ---
  {
    id: "sport-roadster",
    name: "Roadster MX",
    tier: "usedSports",
    price: 8000,
    baseStats:      { power: 30, handling: 34, fuelEfficiency: 26, tyreDurability: 24, comfort: 14, reliability: 24, pitStopTime: 55, fuelCapacity: 50 },
    potentialStats: { power: 44, handling: 46, fuelEfficiency: 30, tyreDurability: 28, comfort: 18, reliability: 24, pitStopTime: 55, fuelCapacity: 50 },
    upgradePacks: [{ type: "power", cost: 2000 }, { type: "handling", cost: 1800 }, { type: "comfort", cost: 1000 }],
  },
  {
    id: "sport-coupe",
    name: "Sport Coupé SR",
    tier: "usedSports",
    price: 14000,
    baseStats:      { power: 38, handling: 36, fuelEfficiency: 28, tyreDurability: 26, comfort: 16, reliability: 26, pitStopTime: 52, fuelCapacity: 54 },
    potentialStats: { power: 52, handling: 48, fuelEfficiency: 32, tyreDurability: 30, comfort: 22, reliability: 26, pitStopTime: 52, fuelCapacity: 54 },
    upgradePacks: [{ type: "power", cost: 3500 }, { type: "handling", cost: 3000 }, { type: "comfort", cost: 1800 }],
  },
  {
    id: "sport-turbo",
    name: "Turbo Z",
    tier: "usedSports",
    price: 20000,
    baseStats:      { power: 44, handling: 38, fuelEfficiency: 24, tyreDurability: 26, comfort: 16, reliability: 28, pitStopTime: 50, fuelCapacity: 56 },
    potentialStats: { power: 60, handling: 50, fuelEfficiency: 28, tyreDurability: 30, comfort: 22, reliability: 28, pitStopTime: 50, fuelCapacity: 56 },
    upgradePacks: [{ type: "power", cost: 5000 }, { type: "handling", cost: 4200 }, { type: "comfort", cost: 2500 }],
  },

  // --- Dedicated track cars ($30,000–$75,000) ---
  {
    id: "track-clubsport",
    name: "Clubsport 350",
    tier: "trackCar",
    price: 35000,
    baseStats:      { power: 50, handling: 52, fuelEfficiency: 30, tyreDurability: 32, comfort: 18, reliability: 34, pitStopTime: 45, fuelCapacity: 60 },
    potentialStats: { power: 66, handling: 66, fuelEfficiency: 34, tyreDurability: 36, comfort: 24, reliability: 34, pitStopTime: 45, fuelCapacity: 60 },
    upgradePacks: [{ type: "power", cost: 8000 }, { type: "handling", cost: 7000 }, { type: "comfort", cost: 4000 }],
  },
  {
    id: "track-proto",
    name: "Proto Cup",
    tier: "trackCar",
    price: 55000,
    baseStats:      { power: 56, handling: 58, fuelEfficiency: 32, tyreDurability: 34, comfort: 20, reliability: 36, pitStopTime: 42, fuelCapacity: 64 },
    potentialStats: { power: 72, handling: 74, fuelEfficiency: 36, tyreDurability: 38, comfort: 26, reliability: 36, pitStopTime: 42, fuelCapacity: 64 },
    upgradePacks: [{ type: "power", cost: 12000 }, { type: "handling", cost: 10000 }, { type: "comfort", cost: 6000 }],
  },
  {
    id: "track-enduro",
    name: "Enduro RS",
    tier: "trackCar",
    price: 75000,
    baseStats:      { power: 60, handling: 62, fuelEfficiency: 36, tyreDurability: 38, comfort: 24, reliability: 40, pitStopTime: 40, fuelCapacity: 68 },
    potentialStats: { power: 76, handling: 78, fuelEfficiency: 40, tyreDurability: 42, comfort: 30, reliability: 40, pitStopTime: 40, fuelCapacity: 68 },
    upgradePacks: [{ type: "power", cost: 16000 }, { type: "handling", cost: 14000 }, { type: "comfort", cost: 8000 }],
  },

  // --- GT race cars ($150,000–$350,000) ---
  {
    id: "gt-am",
    name: "GT-Am 488",
    tier: "gtRaceCar",
    price: 160000,
    baseStats:      { power: 68, handling: 66, fuelEfficiency: 38, tyreDurability: 40, comfort: 28, reliability: 46, pitStopTime: 36, fuelCapacity: 72 },
    potentialStats: { power: 82, handling: 80, fuelEfficiency: 42, tyreDurability: 44, comfort: 34, reliability: 46, pitStopTime: 36, fuelCapacity: 72 },
    upgradePacks: [{ type: "power", cost: 30000 }, { type: "handling", cost: 26000 }, { type: "comfort", cost: 16000 }],
  },
  {
    id: "gt-pro",
    name: "GT-Pro 911",
    tier: "gtRaceCar",
    price: 250000,
    baseStats:      { power: 74, handling: 72, fuelEfficiency: 40, tyreDurability: 42, comfort: 30, reliability: 50, pitStopTime: 34, fuelCapacity: 76 },
    potentialStats: { power: 88, handling: 86, fuelEfficiency: 44, tyreDurability: 46, comfort: 36, reliability: 50, pitStopTime: 34, fuelCapacity: 76 },
    upgradePacks: [{ type: "power", cost: 45000 }, { type: "handling", cost: 40000 }, { type: "comfort", cost: 24000 }],
  },
  {
    id: "gt-evo",
    name: "GT Evo LM",
    tier: "gtRaceCar",
    price: 350000,
    baseStats:      { power: 78, handling: 76, fuelEfficiency: 42, tyreDurability: 44, comfort: 32, reliability: 52, pitStopTime: 32, fuelCapacity: 80 },
    potentialStats: { power: 92, handling: 90, fuelEfficiency: 46, tyreDurability: 48, comfort: 38, reliability: 52, pitStopTime: 32, fuelCapacity: 80 },
    upgradePacks: [{ type: "power", cost: 60000 }, { type: "handling", cost: 52000 }, { type: "comfort", cost: 32000 }],
  },

  // --- Supercars ($500,000–$1,000,000) ---
  {
    id: "super-veloce",
    name: "Veloce Stradale",
    tier: "supercar",
    price: 550000,
    baseStats:      { power: 84, handling: 80, fuelEfficiency: 36, tyreDurability: 40, comfort: 30, reliability: 48, pitStopTime: 30, fuelCapacity: 84 },
    potentialStats: { power: 96, handling: 92, fuelEfficiency: 40, tyreDurability: 44, comfort: 36, reliability: 48, pitStopTime: 30, fuelCapacity: 84 },
    upgradePacks: [{ type: "power", cost: 90000 }, { type: "handling", cost: 78000 }, { type: "comfort", cost: 45000 }],
  },
  {
    id: "super-presto",
    name: "Presto GTR",
    tier: "supercar",
    price: 800000,
    baseStats:      { power: 88, handling: 84, fuelEfficiency: 34, tyreDurability: 38, comfort: 28, reliability: 46, pitStopTime: 28, fuelCapacity: 88 },
    potentialStats: { power: 98, handling: 94, fuelEfficiency: 38, tyreDurability: 42, comfort: 34, reliability: 46, pitStopTime: 28, fuelCapacity: 88 },
    upgradePacks: [{ type: "power", cost: 130000 }, { type: "handling", cost: 110000 }, { type: "comfort", cost: 65000 }],
  },

  // --- Hypercars ($1,500,000–$3,000,000) ---
  {
    id: "hyper-apex",
    name: "Apex One",
    tier: "hypercar",
    price: 1600000,
    baseStats:      { power: 92, handling: 88, fuelEfficiency: 32, tyreDurability: 36, comfort: 26, reliability: 44, pitStopTime: 26, fuelCapacity: 92 },
    potentialStats: { power: 99, handling: 96, fuelEfficiency: 36, tyreDurability: 40, comfort: 32, reliability: 44, pitStopTime: 26, fuelCapacity: 92 },
    upgradePacks: [{ type: "power", cost: 250000 }, { type: "handling", cost: 210000 }, { type: "comfort", cost: 120000 }],
  },
  {
    id: "hyper-zenith",
    name: "Zenith Hyperion",
    tier: "hypercar",
    price: 2200000,
    baseStats:      { power: 95, handling: 92, fuelEfficiency: 30, tyreDurability: 34, comfort: 24, reliability: 42, pitStopTime: 24, fuelCapacity: 96 },
    potentialStats: { power: 100, handling: 98, fuelEfficiency: 34, tyreDurability: 38, comfort: 30, reliability: 42, pitStopTime: 24, fuelCapacity: 96 },
    upgradePacks: [{ type: "power", cost: 350000 }, { type: "handling", cost: 300000 }, { type: "comfort", cost: 170000 }],
  },
  {
    id: "hyper-monarch",
    name: "Monarch LMH",
    tier: "hypercar",
    price: 3000000,
    baseStats:      { power: 98, handling: 96, fuelEfficiency: 28, tyreDurability: 32, comfort: 22, reliability: 40, pitStopTime: 22, fuelCapacity: 100 },
    potentialStats: { power: 100, handling: 100, fuelEfficiency: 32, tyreDurability: 36, comfort: 28, reliability: 40, pitStopTime: 22, fuelCapacity: 100 },
    upgradePacks: [{ type: "power", cost: 480000 }, { type: "handling", cost: 420000 }, { type: "comfort", cost: 240000 }],
  },
];
