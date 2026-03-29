import type { ActiveIssue, IssueTemplate, PitStopConfig } from "../types";
import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

/** Seconds added to a stop when any fuel is being added (fixed overhead). */
const REFUEL_BASE_SECONDS = 2.0;
/** Additional seconds per litre added. */
const REFUEL_SECONDS_PER_LITRE = 0.05;
/** Seconds added to change a set of tyres. */
const TYRE_CHANGE_SECONDS = 8.0;
/** Seconds added for a driver swap. */
const DRIVER_SWAP_SECONDS = 5.0;

/**
 * Duration multiplier when crew size is 0 (player alone).
 * Crew size 16 → multiplier 1.0; crew size 0 → this value.
 */
const SOLO_CREW_MULTIPLIER = 2.5;
/** Maximum crew size (matches GDD §5 and game constants). */
const MAX_CREW_SIZE = 16;

/** Maximum Engineer skill level (matches PlayerSkills.engineer in team.ts). */
const MAX_ENGINEER_SKILL = 20;
/** Maximum fractional reduction in pit stop duration from Engineer skill. */
const MAX_ENGINEER_PIT_REDUCTION = 0.25; // skill 20 → 25% faster

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * All inputs required to process a pit stop.
 * Intentionally flat so it can be constructed from any source in tests or the race loop.
 */
export interface PitStopContext {
  /** Car's effective Pit Stop Time stat (seconds, lower = faster). */
  pitStopTime: number;
  /** Car's maximum fuel capacity (litres). */
  fuelCapacity: number;
  /** Current fuel level (litres). */
  currentFuel: number;
  /** Current tyre wear (0 = fresh, 100 = fully worn). */
  currentTyreWear: number;
  /** Tyre sets available in team inventory. */
  tyreSetsAvailable: number;
  /** Spare parts units available. */
  sparePartsAvailable: number;
  /** ID of the driver currently in the car. */
  currentDriverId: string;
  /**
   * Fatigue for all drivers on the team (keyed by driver ID, 0–100).
   * Missing entries are treated as 0.
   */
  driverFatigue: Record<string, number>;
  /** Issues currently affecting the car. */
  activeIssues: ActiveIssue[];
  /**
   * Full issue template catalogue — used to look up spare parts cost
   * and fix duration for issues being repaired.
   */
  issueTemplates: IssueTemplate[];
  /** Pit crew headcount (0–16). */
  crewSize: number;
  /** Player's Engineer skill level (0–20; pass 0 for AI teams). */
  engineerSkill: number;
}

/** The outcome of a pit stop: updated resource values and the stop duration. */
export interface PitStopResult {
  /** Total stop duration in seconds. */
  duration: number;
  /** Fuel level after refuelling (litres). */
  fuelLevel: number;
  /** Tyre wear after the stop (0 if tyres changed, otherwise unchanged). */
  tyreWear: number;
  /** Tyre sets remaining after the stop. */
  tyreSetsRemaining: number;
  /** Spare parts remaining after any issue fixes. */
  sparePartsRemaining: number;
  /** Driver ID at the end of the stop (may be same as before if no swap). */
  currentDriverId: string;
  /** Updated fatigue map. Incoming driver's fatigue is reset to 0. */
  driverFatigue: Record<string, number>;
  /** Active issues remaining after any fixes. */
  activeIssues: ActiveIssue[];
  /** Always "normal" — instruction mode resets on every pit stop (GDD §2). */
  instructionMode: Extract<InstructionMode, "normal">;
}

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

/**
 * Processes a pit stop and returns the resulting car/driver state.
 *
 * Duration formula (GDD §2):
 *   (pitStopTime + taskSeconds) × crewMultiplier × engineerMultiplier
 *
 * Task seconds are accumulated for each operation actually performed:
 *   - Refuel: base overhead + per-litre rate (only if fuelToAdd > 0)
 *   - Tyre change: flat cost (only if changeTyres and tyreSetsAvailable > 0)
 *   - Driver swap: flat cost (only if nextDriverId differs from current)
 *   - Issue fix: each fixed issue adds its template's fixDuration
 *
 * Crew multiplier: `1 + (1 − crewSize / MAX_CREW_SIZE) × (SOLO_CREW_MULTIPLIER − 1)`
 *   → crew 16 = 1.0×, crew 0 = SOLO_CREW_MULTIPLIER (very slow).
 *
 * Engineer multiplier: `1 − (skill / MAX_ENGINEER_SKILL) × MAX_ENGINEER_PIT_REDUCTION`
 *   → skill 20 = 25% faster, skill 0 = no benefit.
 *
 * Design note — driver fatigue on swap:
 *   When a new driver takes over, their fatigue resets to 0 regardless of any fatigue
 *   accumulated in earlier stints. This keeps the system simple and predictable.
 *   See Implementer → PM handoff 2026-03-29 if a "carry-over fatigue" model is preferred.
 *
 * @param ctx    Current car, driver, and team state.
 * @param config What to do during the stop. `config.issueIdsToFix` lists the template IDs
 *               of issues to attempt to repair; each is skipped silently if parts are insufficient.
 */
export function executePitStop(
  ctx: PitStopContext,
  config: PitStopConfig,
): PitStopResult {
  const issueIdsToFix = config.issueIdsToFix ?? [];
  let taskSeconds = 0;
  let tyreSetsRemaining = ctx.tyreSetsAvailable;
  let sparePartsRemaining = ctx.sparePartsAvailable;
  let newFuel = ctx.currentFuel;
  let newTyreWear = ctx.currentTyreWear;
  let newDriverId = ctx.currentDriverId;
  const newFatigue = { ...ctx.driverFatigue };
  let newActiveIssues = [...ctx.activeIssues];

  // 1. Refuel
  if (config.fuelToAdd > 0) {
    const actualFuel = Math.min(config.fuelToAdd, ctx.fuelCapacity - ctx.currentFuel);
    if (actualFuel > 0) {
      newFuel = ctx.currentFuel + actualFuel;
      taskSeconds += REFUEL_BASE_SECONDS + actualFuel * REFUEL_SECONDS_PER_LITRE;
    }
  }

  // 2. Tyre change — only possible if at least one set is available
  if (config.changeTyres && tyreSetsRemaining > 0) {
    newTyreWear = 0;
    tyreSetsRemaining -= 1;
    taskSeconds += TYRE_CHANGE_SECONDS;
  }

  // 3. Driver swap — only if a different driver is specified
  const swapTarget = config.nextDriverId;
  if (swapTarget !== null && swapTarget !== ctx.currentDriverId) {
    newDriverId = swapTarget;
    newFatigue[swapTarget] = 0;
    taskSeconds += DRIVER_SWAP_SECONDS;
  }

  // 4. Issue fixes — process in order, stop if parts run out
  for (const templateId of issueIdsToFix) {
    const issueIndex = newActiveIssues.findIndex((i) => i.templateId === templateId);
    if (issueIndex === -1) continue; // issue not present on this car

    const template = ctx.issueTemplates.find((t) => t.id === templateId);
    if (!template) continue; // template not found (data inconsistency, skip)

    if (sparePartsRemaining < template.sparePartsCost) continue; // insufficient parts

    sparePartsRemaining -= template.sparePartsCost;
    taskSeconds += template.workUnits;
    newActiveIssues = [
      ...newActiveIssues.slice(0, issueIndex),
      ...newActiveIssues.slice(issueIndex + 1),
    ];
  }

  // 5. Duration calculation
  const crewMultiplier =
    1 + (1 - ctx.crewSize / MAX_CREW_SIZE) * (SOLO_CREW_MULTIPLIER - 1);
  const engineerMultiplier =
    1 - (ctx.engineerSkill / MAX_ENGINEER_SKILL) * MAX_ENGINEER_PIT_REDUCTION;
  const duration =
    (ctx.pitStopTime + taskSeconds) * crewMultiplier * engineerMultiplier;

  return {
    duration,
    fuelLevel: newFuel,
    tyreWear: newTyreWear,
    tyreSetsRemaining,
    sparePartsRemaining,
    currentDriverId: newDriverId,
    driverFatigue: newFatigue,
    activeIssues: newActiveIssues,
    instructionMode: "normal",
  };
}
