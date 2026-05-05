import type { CarModel, CarClass } from "./car";
import type { Driver, Contract } from "./driver";
import type { Team } from "./team";
import type { CarMarket, EconomyConfig } from "./economy";
import type { Track } from "./track";
import type { CarLapSnapshot } from "../simulation/raceLoop";

// --- Race History ---

export interface RaceResult {
  teamId: string;
  carId: string;
  carClass: CarClass;
  position: number;
  lapsCompleted: number;
  prizeMoney: number;
  retired: boolean;
}

/** A driver stint within a single race. */
export interface Stint {
  driverId: string;
  startLap: number;
  endLap: number;
}

/** Lap counts per instruction mode for a single car. */
export interface ModeCounter {
  push: number;
  normal: number;
  conserve: number;
}

/** An endurance-relevant race event. */
export interface RaceEvent {
  lap: number;
  type: "retirement" | "issue" | "pitStop" | "lapped" | "classLeadChange" | "fastestLap";
  text: string;
  carId: string;
  teamId: string;
}

export interface RaceHistoryEntry {
  year: number;
  results: RaceResult[];
  fastestLap: { teamId: string; time: number } | null;
  /** Optional rich data — stripped after the rolling window (default 3 years). */
  lapSnapshots?: Record<string, CarLapSnapshot[]>;
  positionHistory?: number[][];  // positionHistory[lap][carIndex] = position
  /** Maps carId → its slot in positionHistory[lap]. Required to read a specific car's positions. */
  carIndexById?: Record<string, number>;
  events?: RaceEvent[];
  stints?: Record<string, Stint[]>;
  modeCounters?: Record<string, ModeCounter>;
}

// --- Save Metadata ---

export interface SaveMetadata {
  createdAt: string;     // ISO 8601
  lastSavedAt: string;   // ISO 8601
  version: number;       // Save format version for migration
}

// --- Game Phase ---

export type GamePhase =
  | "newGame"       // Character/team creation
  | "preRace"       // Between-race management screens
  | "race"          // Race in progress
  | "postRace";     // Race summary / newspaper

// --- Game State ---

export interface GameState {
  phase: GamePhase;
  currentYear: number;        // Starts at 1
  meta: SaveMetadata;

  // Entities
  carModels: CarModel[];      // ~20 templates (immutable reference data)
  drivers: Driver[];          // Full pool of 310
  contracts: Contract[];      // All active contracts across all teams
  teams: Team[];              // All 100 teams (1 player + 99 AI)

  // Economy
  economyConfig: EconomyConfig;
  carMarket: CarMarket;

  // Track
  track: Track;

  // History
  raceHistory: RaceHistoryEntry[];
}
