// --- Driver Stats ---

export interface DriverStats {
  pace: number;        // Lap time modifier (0–100, higher = faster)
  consistency: number; // Lap time variance (0–100, higher = more predictable)
  stamina: number;     // Fatigue buildup rate (0–100, higher = slower fatigue)
  safety: number;      // Crash/failure probability (0–100, higher = fewer incidents)
  smoothness: number;  // Tyre wear rate (0–100, higher = less wear)
}

// --- Sinusoidal Curve Parameters ---
// Each driver's stats follow a sinusoidal curve peaking around age 30.
// Individual stats can have small phase offsets so no two same-age drivers are identical.

export interface DriverCurveParams {
  peakAge: number;                 // Typically ~30
  peakStats: DriverStats;          // Stats at peak
  phaseOffsets: DriverStats;       // Per-stat phase offset (years, small values)
}

// --- Driver ---

export interface Driver {
  id: string;
  name: string;
  nationality: string; // ISO 3166-1 alpha-2 country code (e.g. "br", "jp", "gb")
  age: number;
  curveParams: DriverCurveParams;
  marketValue: number;
}

// --- Contract ---

export type ContractLength = 1 | 2 | 3;

export interface Contract {
  driverId: string;
  teamId: string;
  length: ContractLength;
  remainingYears: number;
  annualSalary: number;
}
