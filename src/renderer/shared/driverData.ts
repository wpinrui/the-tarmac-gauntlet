import type { DriverStats } from "../types";

/** The 5 driver stats with human-readable labels. */
export const DRIVER_STAT_KEYS: (keyof DriverStats)[] = ["pace", "consistency", "stamina", "safety", "smoothness"];

export const DRIVER_STAT_LABELS: Record<string, string> = {
  pace: "Pace",
  consistency: "Consistency",
  stamina: "Stamina",
  safety: "Safety",
  smoothness: "Smoothness",
};
