import type { CarStats } from "../types";

/** Descriptions for upgrade pack types. */
export const UPGRADE_DESCS: Record<string, string> = {
  power: "Unlocks power and fuel efficiency potential",
  handling: "Unlocks handling and tyre durability potential",
  comfort: "Unlocks comfort potential",
};

/** The 7 displayable car stats (excludes pitStopTime) with human-readable labels. */
export const DISPLAY_STATS: { key: keyof CarStats; label: string }[] = [
  { key: "power", label: "Power" },
  { key: "handling", label: "Handling" },
  { key: "fuelEfficiency", label: "Fuel Efficiency" },
  { key: "tyreDurability", label: "Tyre Durability" },
  { key: "comfort", label: "Comfort" },
  { key: "reliability", label: "Reliability" },
  { key: "fuelCapacity", label: "Fuel Capacity" },
];

/** Crew cost per member per race (GDD §5: $2,000). */
export const CREW_COST_PER_MEMBER = 2_000;

/** Condition restored per spare part used in repair. */
export const CONDITION_PER_PART = 5;

/** Sale price multiplier — player receives this fraction of the calculated sale value. */
export const SALE_PRICE_MULTIPLIER = 0.5;

/** Shitbox model ID — the designated plot armour car. */
export const SHITBOX_MODEL_ID = "f-01";

/** Generates unique car instance IDs. */
let carIdCounter = 0;
export function nextCarId(): string {
  if (carIdCounter === 0) carIdCounter = Date.now();
  return `car-${++carIdCounter}`;
}
