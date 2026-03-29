import type { ActiveIssue, IssueTemplate } from "../types";
import type { InstructionMode } from "../types";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

// --- Issue probability modifiers ---
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
const MAX_SAFETY_REDUCTION = 0.5; // skill driver can halve failure risk

/** Per-lap failure probability multipliers by instruction mode. Push is noticeably dangerous. */
const FAILURE_MODE_SCALE: Record<InstructionMode, number> = {
  push: 2.0,
  normal: 1.0,
  conserve: 0.6,
};

/**
 * When a failure occurs, the probability it is a terminal crash (vs. a mechanical failure).
 * The remaining probability (1 − CRASH_PROBABILITY) produces a "mechanical" failure.
 */
const CRASH_PROBABILITY = 0.25;

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
  /** All issue templates to roll against. */
  issueTemplates: IssueTemplate[];
  /**
   * Injectable random source for deterministic testing.
   * Roll result < probability → event occurs.
   * Defaults to Math.random.
   */
  random?: () => number;
}

export interface LapRiskResult {
  /** Issues that newly occurred this lap. Empty if a failure was rolled first. */
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

/** Effective probability for a single issue template given the current car state. */
export function issueEffectiveProbability(
  baseProbability: number,
  condition: number,
  reliability: number,
  mode: InstructionMode,
): number {
  const conditionFactor = 1 + (1 - condition / 100) * MAX_CONDITION_ISSUE_SCALE;
  const reliabilityFactor = 1 - (reliability / 100) * MAX_RELIABILITY_ISSUE_REDUCTION;
  return baseProbability * conditionFactor * reliabilityFactor * ISSUE_MODE_SCALE[mode];
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

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

/**
 * Rolls for mechanical issues and race-ending failures for one lap (GDD §2).
 *
 * Failure is rolled first. If one occurs the function returns immediately with an
 * empty `newIssues` list — a terminal failure takes precedence over minor incidents.
 *
 * For each issue template not already active, an independent roll is made.
 * Multiple issues can trigger in the same lap (their lap-time penalties stack).
 * An already-active issue will not be rolled again, preventing duplicates.
 *
 * @param ctx Roll context containing car state, driver stats, and issue catalogue.
 * @returns Newly occurred issues and/or a failure type (null = no failure).
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
    const failureType: FailureType = random() < CRASH_PROBABILITY ? "crash" : "mechanical";
    return { newIssues: [], failure: failureType };
  }

  // 2. Roll for each issue template that is not already active.
  const activeTemplateIds = new Set(ctx.activeIssues.map((i) => i.templateId));
  const newIssues: ActiveIssue[] = [];

  for (const template of ctx.issueTemplates) {
    if (activeTemplateIds.has(template.id)) continue; // already suffering this issue

    const prob = issueEffectiveProbability(
      template.probabilityPerLap,
      condition,
      reliability,
      instructionMode,
    );
    if (random() < prob) {
      newIssues.push({ templateId: template.id, lapOccurred: ctx.currentLap });
    }
  }

  return { newIssues, failure: null };
}
