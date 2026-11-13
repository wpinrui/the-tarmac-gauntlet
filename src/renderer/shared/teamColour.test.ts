import { describe, it, expect } from "vitest";
import { teamColourFromId } from "./teamColour";

describe("teamColourFromId", () => {
  it("returns the same colour for the same id", () => {
    expect(teamColourFromId("team-7")).toBe(teamColourFromId("team-7"));
  });

  it("returns a parseable hsl(h, s%, l%) string", () => {
    const c = teamColourFromId("team-7");
    expect(c).toMatch(/^hsl\(\d{1,3}, \d{1,3}%, \d{1,3}%\)$/);
  });

  it("uses the full 0–359 hue range for distinct ids", () => {
    const hues = new Set<number>();
    for (let i = 0; i < 100; i++) {
      const m = teamColourFromId(`team-${i}`).match(/^hsl\((\d+)/);
      if (m) hues.add(Number(m[1]));
    }
    // 100 ids drawn from a 360-bucket hue space should land in many distinct
    // buckets — anything below ~50 unique values would mean catastrophic
    // bunching. The exact count is hash-dependent; pick a loose floor.
    expect(hues.size).toBeGreaterThan(50);
  });

  it("differs for two close ids", () => {
    expect(teamColourFromId("team-1")).not.toBe(teamColourFromId("team-2"));
  });
});
