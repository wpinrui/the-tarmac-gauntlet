// --- Instruction Mode ---

export type InstructionMode = "push" | "normal" | "conserve";

// --- Mechanical Issues ---

export interface IssueTemplate {
  id: string;
  description: string;       // e.g. "loose wheel nut", "overheating brakes"
  lapTimeCost: number;        // Per-lap time penalty (seconds) while unresolved
  probabilityPerLap: number;  // Base chance of occurring per lap
  sparePartsCost: number;     // Parts consumed to fix
  fixDuration: number;        // Time added to pit stop to repair (seconds)
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

// --- Per-Car Race State ---

export interface RaceCarState {
  teamId: string;
  carId: string;
  position: number;
  lapsCompleted: number;
  totalRaceTime: number;       // Accumulated race time (seconds)
  lastLapTime: number | null;

  // Resources
  tyreWear: number;            // 0 (fresh) to 100 (fully worn)
  fuelRemaining: number;       // Litres remaining
  condition: number;           // 0–100%

  // Driver state
  currentDriverId: string;
  driverFatigue: Record<string, number>; // driverId → fatigue 0–100

  // Strategy
  instructionMode: InstructionMode;
  activeIssues: ActiveIssue[];

  // Pit
  pitNextLap: boolean;
  pitStopConfig: PitStopConfig;
  pitStopCount: number;

  // Terminal state
  retired: boolean;
  retirementReason: string | null;
}

// --- Commentary ---

export type CommentaryEventType =
  | "overtake"
  | "pitStop"
  | "issue"
  | "failure"
  | "leadChange"
  | "lapRecord"
  | "gapChange"
  | "modeChange"
  | "riskyPush"
  | "hint";

export interface CommentaryEntry {
  type: CommentaryEventType;
  lap: number;
  elapsed: number;        // Race time when event occurred (seconds)
  text: string;
}

// --- Global Race State ---

export interface RaceState {
  inProgress: boolean;
  paused: boolean;
  currentLap: number;          // 0-based, increments as leader completes laps
  elapsedTime: number;         // Total elapsed race time (seconds)
  totalLaps: number;           // 48 per GDD

  cars: RaceCarState[];        // All 100 cars
  standings: string[];         // teamIds sorted by position
  fastestLap: { teamId: string; time: number } | null;

  commentary: CommentaryEntry[];
}
