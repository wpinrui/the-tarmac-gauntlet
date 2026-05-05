import type { CarStats } from "./car";

// --- Instruction Mode ---

export type InstructionMode = "push" | "normal" | "conserve";

// --- Mechanical Issues ---

export type IssueSeverity = "minor" | "medium" | "major";
export type IssueCategory = "mechanical" | "crash";

export interface IssueTemplate {
  id: string;
  description: string;
  severity: IssueSeverity;
  category: IssueCategory;
  /** Fractional stat debuffs while active, e.g. { power: 0.10 } = -10% power. */
  statDebuffs: Partial<Record<keyof CarStats, number>>;
  /** Relative probability weight for weighted pick when an issue triggers. */
  weight: number;
  /** Spare parts consumed to fix. */
  sparePartsCost: number;
  /** Base work units to fix — scaled by crew size and engineer skill in pit stop. */
  workUnits: number;
}

export interface ActiveIssue {
  templateId: string;
  lapOccurred: number;
}

// --- Pit Stop Configuration ---

export interface PitStopConfig {
  fuelToAdd: number;              // Litres to add (0 = no refuel)
  changeTyres: boolean;           // Whether to change tyres (consumes 1 tyre set)
  nextDriverId: string | null;    // Driver to swap to (null = keep current)
  issueIdsToFix?: string[];       // Template IDs of issues to fix this stop (omit or [] = fix nothing)
}
