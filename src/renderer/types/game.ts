import type { CarModel } from "./car";
import type { Driver, Contract } from "./driver";
import type { Team } from "./team";
import type { RaceState } from "./race";
import type { CarMarket, EconomyConfig } from "./economy";
import type { Track } from "./track";

// --- Race History ---

export interface RaceResult {
  teamId: string;
  position: number;
  lapsCompleted: number;
  prizeMoney: number;
  retired: boolean;
}

export interface RaceHistoryEntry {
  year: number;
  results: RaceResult[];
  fastestLap: { teamId: string; time: number } | null;
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

  // Race
  race: RaceState | null;     // Non-null only during race phase

  // History
  raceHistory: RaceHistoryEntry[];
}
