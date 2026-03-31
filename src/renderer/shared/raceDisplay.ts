/** Shared constants and utilities for race display (PostRaceSummary + RaceHistory). */

/** Ordinal suffix for a number (1st, 2nd, 3rd, 4th...). */
export function ordinal(n: number): string {
  const s = ["th", "st", "nd", "rd"];
  const v = n % 100;
  return n + (s[(v - 20) % 10] || s[v] || s[0]);
}

/** Icon + color for each race event type (Font Awesome). */
export const EVENT_ICONS: Record<string, { icon: string; color: string }> = {
  retirement: { icon: "fa-solid fa-circle-xmark", color: "#e17055" },
  issue: { icon: "fa-solid fa-bolt", color: "#e0943a" },
  pitStop: { icon: "fa-solid fa-arrows-rotate", color: "#5ab8d8" },
  fastestLap: { icon: "fa-solid fa-stopwatch", color: "#00d4aa" },
  classLeadChange: { icon: "fa-solid fa-trophy", color: "#d4a840" },
  lapped: { icon: "fa-solid fa-circle-down", color: "#7a96b0" },
};
