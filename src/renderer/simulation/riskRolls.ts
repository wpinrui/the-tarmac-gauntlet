import type { ActiveIssue, IssueTemplate } from "../types";
import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Issue probability modifiers ---
/** Base chance of any issue occurring per lap under ideal conditions. */
const BASE_ISSUE_PROB = 0.05;

/**
 * At 0% condition this factor is added on top of 1.0, tripling the base issue probability.
 * At 100% condition the modifier is 1.0 (no change).
 */
const MAX_CONDITION_ISSUE_SCALE = 2.0; // condition=0 → ×3.0, condition=100 → ×1.0

/** At Reliability=100 the issue probability is reduced by this fraction. */
const MAX_RELIABILITY_ISSUE_REDUCTION = 0.6; // reliability=100 → ×0.40

/** Per-lap issue probability multipliers by instruction mode. */
const ISSUE_MODE_SCALE: Record<InstructionMode, number> = {
  push: 1.5,
  normal: 1.0,
  conserve: 0.7,
};

// --- Failure probability modifiers ---
/** Base chance of a race-ending failure per lap under ideal conditions. */
const BASE_FAILURE_PROB = 0.003;

/**
 * At 0% condition this factor is added on top of 1.0, quintupling the failure probability.
 * Stronger than the issue scale — condition is a primary failure driver.
 */
const MAX_CONDITION_FAILURE_SCALE = 4.0; // condition=0 → ×5.0, condition=100 → ×1.0

/** Fractional increase in failure probability per year of car age. */
const AGE_FAILURE_RATE = 0.04; // age 10 → ×1.4
/** Cap on the age failure multiplier (mirrors effectiveStats.ts / degradation.ts patterns). */
const MAX_AGE_FAILURE_FACTOR = 2.5; // reached at age ~37.5

/** At Reliability=100 the failure probability is reduced by this fraction. */
const MAX_RELIABILITY_FAILURE_REDUCTION = 0.6;

/** At Safety=100 the failure probability is reduced by this fraction. */
const MAX_SAFETY_REDUCTION = 0.5; // skilled driver can halve failure risk

/** Per-lap failure probability multipliers by instruction mode. Push is noticeably dangerous. */
const FAILURE_MODE_SCALE: Record<InstructionMode, number> = {
  push: 2.0,
  normal: 1.0,
  conserve: 0.6,
};

/**
 * When a failure occurs, the probability it is a crash (vs. a mechanical failure).
 * The remaining probability (1 − CRASH_PROBABILITY) produces a "mechanical" failure.
 */
const CRASH_PROBABILITY = 0.25;

// --- Non-terminal crash modifiers ---
/**
 * Base probability that a crash is survivable (non-terminal).
 * Modified by driver safety — high safety drivers are much more likely to survive.
 */
const BASE_CRASH_SURVIVAL_PROB = 0.20;

/** At Safety=100, crash survival probability is boosted by this much (additive). */
const MAX_SAFETY_SURVIVAL_BONUS = 0.50; // safety 100 → 0.20 + 0.50 = 70% survival

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type FailureType = "mechanical" | "crash";

export interface LapRiskContext {
  /** The lap number being simulated (stored on newly created ActiveIssue). */
  currentLap: number;
  /** Current car condition (0–100). */
  condition: number;
  /** Car age in years. */
  carAge: number;
  /** Effective Reliability stat (0–100). Higher = fewer incidents. */
  reliability: number;
  /** Driver's Safety stat (0–100). Higher = lower failure probability. */
  driverSafety: number;
  /** Current driver instruction mode. */
  instructionMode: InstructionMode;
  /**
   * Issues already active on the car.
   * Templates with an already-active issue are skipped to prevent duplicates.
   */
  activeIssues: ActiveIssue[];
  /** Mechanical issue templates for the weighted draw. */
  issueTemplates: IssueTemplate[];
  /** Crash issue templates for non-terminal crash draws. */
  crashTemplates: IssueTemplate[];
  /**
   * Injectable random source for deterministic testing.
   * Roll result < probability → event occurs.
   * Defaults to Math.random.
   */
  random?: () => number;
}

export interface LapRiskResult {
  /** Issues that newly occurred this lap (mechanical or crash). */
  newIssues: ActiveIssue[];
  /**
   * Race-ending failure type, or null if none occurred.
   * If non-null, the car should be retired — `newIssues` will be empty.
   */
  failure: FailureType | null;
}

// ---------------------------------------------------------------------------
// Internal probability helpers (exported for testing)
// ---------------------------------------------------------------------------

/**
 * Effective probability that any issue occurs this lap.
 * Two-step system: this is the "does something break?" roll.
 */
export function issueEffectiveProbability(
  condition: number,
  reliability: number,
  mode: InstructionMode,
): number {
  const conditionFactor = 1 + (1 - condition / 100) * MAX_CONDITION_ISSUE_SCALE;
  const reliabilityFactor = 1 - (reliability / 100) * MAX_RELIABILITY_ISSUE_REDUCTION;
  return BASE_ISSUE_PROB * conditionFactor * reliabilityFactor * ISSUE_MODE_SCALE[mode];
}

/** Effective failure probability given the current car and driver state. */
export function failureEffectiveProbability(
  condition: number,
  carAge: number,
  reliability: number,
  driverSafety: number,
  mode: InstructionMode,
): number {
  const conditionFactor = 1 + (1 - condition / 100) * MAX_CONDITION_FAILURE_SCALE;
  const ageFactor = Math.min(MAX_AGE_FAILURE_FACTOR, 1 + carAge * AGE_FAILURE_RATE);
  const reliabilityFactor = 1 - (reliability / 100) * MAX_RELIABILITY_FAILURE_REDUCTION;
  const safetyFactor = 1 - (driverSafety / 100) * MAX_SAFETY_REDUCTION;
  return (
    BASE_FAILURE_PROB *
    conditionFactor *
    ageFactor *
    reliabilityFactor *
    safetyFactor *
    FAILURE_MODE_SCALE[mode]
  );
}

/**
 * Probability that a crash is survivable (non-terminal).
 * Higher driver safety = more likely to limp back to pits.
 */
export function crashSurvivalProbability(driverSafety: number): number {
  return BASE_CRASH_SURVIVAL_PROB + (driverSafety / 100) * MAX_SAFETY_SURVIVAL_BONUS;
}

// ---------------------------------------------------------------------------
// Weighted pick helper
// ---------------------------------------------------------------------------

/**
 * Picks one template from the list via weighted random selection.
 * Templates already active on the car are excluded.
 * Returns null if no eligible templates remain.
 */
export function weightedPick(
  templates: IssueTemplate[],
  activeIssues: ActiveIssue[],
  random: () => number,
): IssueTemplate | null {
  const activeIds = new Set(activeIssues.map((i) => i.templateId));
  const eligible = templates.filter((t) => !activeIds.has(t.id));
  if (eligible.length === 0) return null;

  const totalWeight = eligible.reduce((sum, t) => sum + t.weight, 0);
  let roll = random() * totalWeight;
  for (const template of eligible) {
    roll -= template.weight;
    if (roll <= 0) return template;
  }
  return eligible[eligible.length - 1]; // floating point safety
}

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

/**
 * Rolls for mechanical issues and race-ending failures for one lap (GDD §2).
 *
 * Two-step issue system:
 *   1. Roll base issue probability (modified by condition, reliability, mode).
 *   2. If triggered, weighted pick from the mechanical issue catalogue.
 *
 * Failure system:
 *   1. Roll for terminal failure (modified by condition, age, reliability, safety, mode).
 *   2. If failure: determine crash vs mechanical.
 *   3. If crash: roll for survival (modified by safety).
 *      - Survived: pick a crash issue from crash templates.
 *      - Fatal: terminal crash retirement.
 *
 * Failure is rolled first. Terminal failures take precedence.
 */
export function rollLapRisks(ctx: LapRiskContext): LapRiskResult {
  const random = ctx.random ?? Math.random;
  const { condition, carAge, reliability, driverSafety, instructionMode } = ctx;

  // 1. Check for terminal failure first.
  const failureProb = failureEffectiveProbability(
    condition,
    carAge,
    reliability,
    driverSafety,
    instructionMode,
  );
  if (random() < failureProb) {
    const isCrash = random() < CRASH_PROBABILITY;

    if (isCrash) {
      // Roll for crash survival — driver safety determines if they limp back
      const survivalProb = crashSurvivalProbability(driverSafety);
      if (random() < survivalProb) {
        // Non-terminal crash — pick a crash issue
        const crashIssue = weightedPick(ctx.crashTemplates, ctx.activeIssues, random);
        if (crashIssue) {
          return {
            newIssues: [{ templateId: crashIssue.id, lapOccurred: ctx.currentLap }],
            failure: null,
          };
        }
      }
      // Terminal crash
      return { newIssues: [], failure: "crash" };
    }

    // Terminal mechanical failure
    return { newIssues: [], failure: "mechanical" };
  }

  // 2. Two-step issue roll: does something break?
  const issueProb = issueEffectiveProbability(condition, reliability, instructionMode);
  const newIssues: ActiveIssue[] = [];

  if (random() < issueProb) {
    // Weighted pick from mechanical catalogue
    const picked = weightedPick(ctx.issueTemplates, ctx.activeIssues, random);
    if (picked) {
      newIssues.push({ templateId: picked.id, lapOccurred: ctx.currentLap });
    }
  }

  return { newIssues, failure: null };
}
