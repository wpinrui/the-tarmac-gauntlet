import type { CarModel } from "../types";

/**
 * Maps CarClass to the legacy CarTier for economy config compatibility.
 */
function tierForClass(carClass: string): CarModel["tier"] {
  switch (carClass) {
    case "F":  return "junkyard";
    case "E":  return "dailyDriver";
    case "D":  return "usedSports";
    case "C":  return "trackCar";
    case "B":  return "gtRaceCar";
    case "A":  return "supercar";
    case "F1": return "hypercar";
    default:   return "junkyard";
  }
}

/**
 * Upgrade pack cost placeholders scaled by class.
 */
const PACK_COSTS: Record<string, { power: number; handling: number; comfort: number }> = {
  F:  { power: 800,     handling: 700,      comfort: 500 },
  E:  { power: 2_000,   handling: 1_800,    comfort: 1_200 },
  D:  { power: 5_000,   handling: 4_500,    comfort: 3_000 },
  C:  { power: 15_000,  handling: 13_000,   comfort: 8_000 },
  B:  { power: 40_000,  handling: 35_000,   comfort: 20_000 },
  A:  { power: 100_000, handling: 90_000,   comfort: 50_000 },
  F1: { power: 0,       handling: 0,        comfort: 0 },
};

interface RawCar {
  id: string;
  name: string;
  carClass: CarModel["carClass"];
  price: number;
  base: [number, number, number, number, number, number, number, number]; // pow, hnd, fe, td, cmf, rel, pst, fc
  pot: {
    power?: number;
    fuelEfficiency?: number;
    handling?: number;
    tyreDurability?: number;
    comfort?: number;
  };
}

function buildModel(r: RawCar): CarModel {
  const [pow, hnd, fe, td, cmf, rel, pst, fc] = r.base;
  const baseStats = { power: pow, handling: hnd, fuelEfficiency: fe, tyreDurability: td, comfort: cmf, reliability: rel, pitStopTime: pst, fuelCapacity: fc };
  const potentialStats = {
    power:          r.pot.power          ?? pow,
    handling:       r.pot.handling       ?? hnd,
    fuelEfficiency: r.pot.fuelEfficiency ?? fe,
    tyreDurability: r.pot.tyreDurability ?? td,
    comfort:        r.pot.comfort        ?? cmf,
    reliability:    rel,
    pitStopTime:    pst,
    fuelCapacity:   fc,
  };

  const costs = PACK_COSTS[r.carClass];
  const packs: CarModel["upgradePacks"] = [];
  if (r.pot.power !== undefined)   packs.push({ type: "power",    cost: costs.power });
  if (r.pot.handling !== undefined) packs.push({ type: "handling", cost: costs.handling });
  if (r.pot.comfort !== undefined)  packs.push({ type: "comfort",  cost: costs.comfort });

  return {
    id: r.id,
    name: r.name,
    carClass: r.carClass,
    tier: tierForClass(r.carClass),
    price: r.price,
    baseStats,
    potentialStats,
    upgradePacks: packs,
  };
}

const RAW: RawCar[] = [
  // --- Class F ---
  { id: "f-01", name: "Neutrino Sago",       carClass: "F", price: 6500,  base: [5,7,94,92,30,90,96,42],   pot: { power:18, fuelEfficiency:96, handling:20, tyreDurability:94, comfort:44 } },
  { id: "f-02", name: "Bureau 106",           carClass: "F", price: 8000,  base: [8,12,92,88,22,82,95,37],  pot: { power:20, fuelEfficiency:94, handling:24, tyreDurability:92, comfort:36 } },
  { id: "f-03", name: "Ichisan Lanio",        carClass: "F", price: 14000, base: [10,10,90,90,46,92,94,46], pot: { power:22, fuelEfficiency:92, handling:22, tyreDurability:92, comfort:58 } },
  { id: "f-04", name: "Folksbarrow Moth",     carClass: "F", price: 16000, base: [9,11,88,86,52,80,93,55],  pot: { power:20, fuelEfficiency:90, handling:22, tyreDurability:90, comfort:64 } },
  { id: "f-05", name: "Bureau 207",           carClass: "F", price: 15000, base: [13,14,84,82,36,78,92,50], pot: { power:24, fuelEfficiency:88, handling:26, tyreDurability:86, comfort:48 } },
  { id: "f-06", name: "Neutrino Seria Neo",   carClass: "F", price: 12000, base: [15,16,82,80,28,76,91,45], pot: { power:26, fuelEfficiency:86, handling:28, tyreDurability:84, comfort:40 } },

  // --- Class E ---
  { id: "e-07", name: "Fuud Fantasy",         carClass: "E", price: 16500, base: [24,26,82,78,32,86,88,42], pot: { power:34, fuelEfficiency:86, handling:36, tyreDurability:84, comfort:46 } },
  { id: "e-08", name: "Tonata Conara",        carClass: "E", price: 17500, base: [22,24,86,82,62,94,88,50], pot: { power:32, fuelEfficiency:90, handling:34, tyreDurability:86, comfort:74 } },
  { id: "e-09", name: "Fuud Fortress",        carClass: "E", price: 21000, base: [26,26,76,74,50,82,86,53], pot: { power:36, fuelEfficiency:80, handling:38, tyreDurability:80, comfort:62 } },
  { id: "e-10", name: "Folksbarrow Hockey",   carClass: "E", price: 21000, base: [24,28,78,76,56,84,86,55], pot: { power:34, fuelEfficiency:82, handling:40, tyreDurability:82, comfort:68 } },
  { id: "e-11", name: "Fuud Mantle",          carClass: "E", price: 25000, base: [22,24,74,78,72,88,88,60], pot: { power:32, fuelEfficiency:78, handling:34, tyreDurability:82, comfort:80 } },

  // --- Class D ---
  { id: "d-12", name: "Rubasu CSZ",           carClass: "D", price: 30000, base: [38,46,64,60,28,86,76,50], pot: { power:50, fuelEfficiency:70, handling:58, tyreDurability:68, comfort:42 } },
  { id: "d-13", name: "Folksbarrow Crosswind",carClass: "D", price: 28000, base: [40,44,60,56,42,80,78,55], pot: { power:52, fuelEfficiency:66, handling:56, tyreDurability:64, comfort:54 } },
  { id: "d-14", name: "Rubasu WZX",           carClass: "D", price: 26000, base: [42,42,54,50,30,76,78,60], pot: { power:54, fuelEfficiency:60, handling:54, tyreDurability:58, comfort:44 } },
  { id: "d-15", name: "Hando A2000",          carClass: "D", price: 35000, base: [44,48,56,52,22,82,74,50], pot: { power:56, fuelEfficiency:62, handling:60, tyreDurability:60, comfort:36 } },

  // --- Class C ---
  { id: "c-16", name: "Maecides C199",        carClass: "C", price: 39000,  base: [56,58,62,64,78,82,68,59], pot: { power:68, fuelEfficiency:66, handling:70, tyreDurability:70, comfort:86 } },
  { id: "c-17", name: "Nodi N6",              carClass: "C", price: 55000,  base: [58,58,58,62,82,78,66,73], pot: { power:70, fuelEfficiency:64, handling:70, tyreDurability:68, comfort:90 } },
  { id: "c-18", name: "Hando NS-Max",         carClass: "C", price: 90000,  base: [62,66,44,48,34,74,56,65], pot: { power:74, fuelEfficiency:50, handling:78, tyreDurability:56, comfort:46 } },
  { id: "c-19", name: "Rocher 711",           carClass: "C", price: 115000, base: [64,68,38,44,38,76,44,64], pot: { power:76, fuelEfficiency:44, handling:78, tyreDurability:52, comfort:50 } },
  { id: "c-20", name: "Ichisan GT-S",         carClass: "C", price: 115000, base: [66,64,32,38,36,62,48,74], pot: { power:78, fuelEfficiency:38, handling:76, tyreDurability:46, comfort:48 } },

  // --- Class B ---
  { id: "b-21", name: "Feretti 648 Italian",  carClass: "B", price: 240000, base: [76,82,28,36,26,58,36,86], pot: { power:84, fuelEfficiency:34, handling:88, tyreDurability:42, comfort:36 } },
  { id: "b-22", name: "McRaven H.264C",       carClass: "B", price: 240000, base: [78,80,30,34,22,54,32,72], pot: { power:86, fuelEfficiency:36, handling:88, tyreDurability:40, comfort:30 } },
  { id: "b-23", name: "Maecides STS OMG Gold",carClass: "B", price: 275000, base: [80,78,26,32,32,52,38,80], pot: { power:88, fuelEfficiency:32, handling:84, tyreDurability:38, comfort:42 } },
  { id: "b-24", name: "Rocher Cantara GE",    carClass: "B", price: 450000, base: [82,84,22,28,16,46,30,92], pot: { power:90, fuelEfficiency:28, handling:90, tyreDurability:34 } },
  { id: "b-25", name: "Fuud GD",              carClass: "B", price: 500000, base: [84,86,26,30,18,50,24,65], pot: { power:90, fuelEfficiency:32, handling:92, tyreDurability:36 } },

  // --- Class A ---
  { id: "a-26", name: "Rocher 718",           carClass: "A", price: 845000,   base: [90,92,30,28,16,42,22,70], pot: { comfort:24 } },
  { id: "a-27", name: "McRaven #1",           carClass: "A", price: 1150000,  base: [92,92,22,22,10,36,20,72], pot: { comfort:18 } },
  { id: "a-28", name: "Nodi N17 electron",    carClass: "A", price: 3000000,  base: [92,96,34,30,8,40,14,68],  pot: { comfort:16 } },
  { id: "a-29", name: "Tonata TNS50 Hybrid",  carClass: "A", price: 3000000,  base: [94,98,36,32,8,44,12,68],  pot: { comfort:16 } },
  { id: "a-30", name: "Bureau 6X7",           carClass: "A", price: 3000000,  base: [94,96,32,26,8,34,14,68],  pot: { comfort:14 } },

  // --- Class F1 ---
  { id: "f1-31", name: "Feretti F07",         carClass: "F1", price: 15000000, base: [99,99,8,5,2,12,6,30], pot: {} },
];

/**
 * Full car model catalogue — 31 models across 7 classes (F, E, D, C, B, A, F1).
 * All numeric stats are from car_roster.md.
 */
export const CAR_MODELS: CarModel[] = RAW.map(buildModel);
