// Deterministic team colour for the Phase 3 track map. Liveries (issue tbd)
// will eventually replace this with a real per-team palette; until then we
// hash the teamId so two teams produce visually distinct discs without
// widening the team schema.

const HUE_BUCKETS = 360;
const SATURATION = 70;
const LIGHTNESS = 55;

// FNV-1a 32-bit. Picked because it's tiny, branchless, and gives a clean
// spread on short ASCII keys like "team-7".
function fnv1a(input: string): number {
  let hash = 0x811c9dc5;
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return hash >>> 0;
}

export function teamColourFromId(teamId: string): string {
  const hue = fnv1a(teamId) % HUE_BUCKETS;
  return `hsl(${hue}, ${SATURATION}%, ${LIGHTNESS}%)`;
}
