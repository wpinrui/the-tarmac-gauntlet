import type { CarInstance } from "./car";
import type { Contract } from "./driver";
import type { TransactionRecord } from "./economy";

// --- Player Skills ---

export interface PlayerSkills {
  driver: number;   // 0–20, sets player character driver stats (level × 5)
  engineer: number; // 0–20, reduces pit duration + slows condition degradation
  business: number; // 0–20, discounts on purchases, improves sale prices
}

// --- Base Team (shared between player and AI) ---

export interface BaseTeam {
  id: string;
  name: string;
  budget: number;
  prestige: number;
  prestigeHistory: number[]; // prestige snapshot per year (index 0 = year 1)
  crewSize: number;          // 0–16
  cars: CarInstance[];
  contracts: Contract[];
  enteredCarId: string | null; // Which car is entered for the next/current race
}

// --- Player Team ---

export interface PlayerTeam extends BaseTeam {
  kind: "player";
  playerName: string;
  logo: string | null;       // Data URL or asset path
  skills: PlayerSkills;
  spareParts: number;        // Consumable units carried over
  tyreSets: number;          // Tyre sets carried over
  transactions: TransactionRecord[];
}

// --- AI Team ---

export interface AITeam extends BaseTeam {
  kind: "ai";
  spareParts: number;
  tyreSets: number;
}

export type Team = PlayerTeam | AITeam;
