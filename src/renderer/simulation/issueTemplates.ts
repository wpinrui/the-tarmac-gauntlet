import type { IssueTemplate } from "../types";

/**
 * Complete mechanical issue catalogue.
 *
 * Issues use a two-step roll: first the sim rolls whether *any* issue occurs
 * (base probability modified by condition, reliability, mode), then picks
 * *which* issue via a weighted random draw from this catalogue.
 *
 * Crash issues are drawn separately when a non-terminal crash occurs.
 */

// ---------------------------------------------------------------------------
// Mechanical issues — minor
// ---------------------------------------------------------------------------

const MECHANICAL_MINOR: IssueTemplate[] = [
  {
    id: "engine-misfire",
    description: "Engine misfire",
    severity: "minor", category: "mechanical",
    statDebuffs: { power: 0.10 },
    weight: 10, sparePartsCost: 1, workUnits: 15,
  },
  {
    id: "worn-brake-pads",
    description: "Worn brake pads",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.08 },
    weight: 10, sparePartsCost: 1, workUnits: 18,
  },
  {
    id: "sensor-glitch",
    description: "Sensor glitch",
    severity: "minor", category: "mechanical",
    statDebuffs: { fuelEfficiency: 0.10 },
    weight: 10, sparePartsCost: 1, workUnits: 10,
  },
  {
    id: "radiator-leak",
    description: "Minor radiator leak",
    severity: "minor", category: "mechanical",
    statDebuffs: { fuelEfficiency: 0.08, reliability: 0.10 },
    weight: 8, sparePartsCost: 1, workUnits: 18,
  },
  {
    id: "loose-exhaust-bracket",
    description: "Loose exhaust bracket",
    severity: "minor", category: "mechanical",
    statDebuffs: { power: 0.08 },
    weight: 10, sparePartsCost: 1, workUnits: 14,
  },
  {
    id: "sticky-synchro",
    description: "Sticky gear synchro",
    severity: "minor", category: "mechanical",
    statDebuffs: { power: 0.05, pitStopTime: 0.10 },
    weight: 8, sparePartsCost: 1, workUnits: 18,
  },
  {
    id: "suspension-knock",
    description: "Suspension knock",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.10, comfort: 0.10 },
    weight: 8, sparePartsCost: 1, workUnits: 20,
  },
  {
    id: "loose-wheel-nut",
    description: "Loose wheel nut",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.08, reliability: 0.10 },
    weight: 10, sparePartsCost: 1, workUnits: 14,
  },
  {
    id: "loose-bodywork",
    description: "Loose bodywork panel",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.05, fuelEfficiency: 0.05 },
    weight: 10, sparePartsCost: 1, workUnits: 10,
  },
  {
    id: "steering-play",
    description: "Steering play",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.10 },
    weight: 8, sparePartsCost: 1, workUnits: 18,
  },
  {
    id: "fuel-pump-stutter",
    description: "Fuel pump stutter",
    severity: "minor", category: "mechanical",
    statDebuffs: { fuelEfficiency: 0.12, power: 0.05 },
    weight: 8, sparePartsCost: 1, workUnits: 18,
  },
  {
    id: "clutch-slip",
    description: "Slight clutch slip",
    severity: "minor", category: "mechanical",
    statDebuffs: { power: 0.06, tyreDurability: 0.05 },
    weight: 8, sparePartsCost: 1, workUnits: 16,
  },
  {
    id: "sticky-throttle",
    description: "Sticky throttle",
    severity: "minor", category: "mechanical",
    statDebuffs: { power: 0.08, fuelEfficiency: 0.06 },
    weight: 8, sparePartsCost: 1, workUnits: 12,
  },
  {
    id: "mirror-damage",
    description: "Mirror damage",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.04, comfort: 0.06 },
    weight: 10, sparePartsCost: 1, workUnits: 8,
  },
  {
    id: "loose-heat-shielding",
    description: "Loose heat shielding",
    severity: "minor", category: "mechanical",
    statDebuffs: { reliability: 0.08, comfort: 0.05 },
    weight: 8, sparePartsCost: 1, workUnits: 14,
  },
  {
    id: "headlight-flicker",
    description: "Headlight flicker",
    severity: "minor", category: "mechanical",
    statDebuffs: { handling: 0.06 },
    weight: 10, sparePartsCost: 1, workUnits: 10,
  },
];

// ---------------------------------------------------------------------------
// Mechanical issues — medium
// ---------------------------------------------------------------------------

const MECHANICAL_MEDIUM: IssueTemplate[] = [
  {
    id: "oil-pressure-drop",
    description: "Oil pressure drop",
    severity: "medium", category: "mechanical",
    statDebuffs: { power: 0.20, reliability: 0.15 },
    weight: 4, sparePartsCost: 2, workUnits: 35,
  },
  {
    id: "brake-fade",
    description: "Brake fade",
    severity: "medium", category: "mechanical",
    statDebuffs: { handling: 0.18, tyreDurability: 0.10 },
    weight: 4, sparePartsCost: 2, workUnits: 30,
  },
  {
    id: "gearbox-grinding",
    description: "Gearbox grinding",
    severity: "medium", category: "mechanical",
    statDebuffs: { power: 0.15, handling: 0.10 },
    weight: 4, sparePartsCost: 3, workUnits: 40,
  },
  {
    id: "ecu-fault",
    description: "ECU fault",
    severity: "medium", category: "mechanical",
    statDebuffs: { power: 0.15, fuelEfficiency: 0.15 },
    weight: 4, sparePartsCost: 2, workUnits: 28,
  },
  {
    id: "shock-absorber-failure",
    description: "Shock absorber failure",
    severity: "medium", category: "mechanical",
    statDebuffs: { handling: 0.20, comfort: 0.20, tyreDurability: 0.15 },
    weight: 3, sparePartsCost: 3, workUnits: 40,
  },
  {
    id: "driveshaft-vibration",
    description: "Driveshaft vibration",
    severity: "medium", category: "mechanical",
    statDebuffs: { handling: 0.12, comfort: 0.15 },
    weight: 4, sparePartsCost: 2, workUnits: 35,
  },
  {
    id: "splitter-damage",
    description: "Splitter damage",
    severity: "medium", category: "mechanical",
    statDebuffs: { handling: 0.15, fuelEfficiency: 0.10 },
    weight: 4, sparePartsCost: 2, workUnits: 28,
  },
];

// ---------------------------------------------------------------------------
// Mechanical issues — major
// ---------------------------------------------------------------------------

const MECHANICAL_MAJOR: IssueTemplate[] = [
  {
    id: "engine-overheating",
    description: "Engine overheating",
    severity: "major", category: "mechanical",
    statDebuffs: { power: 0.35, fuelEfficiency: 0.20 },
    weight: 1, sparePartsCost: 4, workUnits: 55,
  },
  {
    id: "brake-disc-crack",
    description: "Brake disc crack",
    severity: "major", category: "mechanical",
    statDebuffs: { handling: 0.30, reliability: 0.15 },
    weight: 1, sparePartsCost: 4, workUnits: 50,
  },
  {
    id: "gearbox-seizure",
    description: "Gearbox seizure",
    severity: "major", category: "mechanical",
    statDebuffs: { power: 0.30, handling: 0.15 },
    weight: 1, sparePartsCost: 5, workUnits: 60,
  },
  {
    id: "coolant-failure",
    description: "Coolant system failure",
    severity: "major", category: "mechanical",
    statDebuffs: { power: 0.25, reliability: 0.30 },
    weight: 1, sparePartsCost: 4, workUnits: 50,
  },
  {
    id: "cracked-exhaust-manifold",
    description: "Cracked exhaust manifold",
    severity: "major", category: "mechanical",
    statDebuffs: { power: 0.25, fuelEfficiency: 0.15 },
    weight: 1, sparePartsCost: 4, workUnits: 45,
  },
  {
    id: "injector-failure",
    description: "Injector failure",
    severity: "major", category: "mechanical",
    statDebuffs: { power: 0.30, fuelEfficiency: 0.25 },
    weight: 1, sparePartsCost: 4, workUnits: 50,
  },
  {
    id: "broken-spring",
    description: "Broken spring",
    severity: "major", category: "mechanical",
    statDebuffs: { handling: 0.35, comfort: 0.30 },
    weight: 1, sparePartsCost: 4, workUnits: 55,
  },
  {
    id: "differential-wear",
    description: "Differential wear",
    severity: "major", category: "mechanical",
    statDebuffs: { handling: 0.25, power: 0.15, tyreDurability: 0.10 },
    weight: 1, sparePartsCost: 5, workUnits: 55,
  },
  {
    id: "power-steering-failure",
    description: "Power steering failure",
    severity: "major", category: "mechanical",
    statDebuffs: { handling: 0.35, comfort: 0.25 },
    weight: 1, sparePartsCost: 4, workUnits: 50,
  },
  {
    id: "wheel-bearing-damage",
    description: "Wheel bearing damage",
    severity: "major", category: "mechanical",
    statDebuffs: { handling: 0.30, tyreDurability: 0.20 },
    weight: 1, sparePartsCost: 4, workUnits: 55,
  },
];

// ---------------------------------------------------------------------------
// Crash issues (non-terminal)
// ---------------------------------------------------------------------------

const CRASH_ISSUES: IssueTemplate[] = [
  {
    id: "front-wing-damage",
    description: "Front wing damage",
    severity: "major", category: "crash",
    statDebuffs: { handling: 0.25, fuelEfficiency: 0.15 },
    weight: 3, sparePartsCost: 4, workUnits: 50,
  },
  {
    id: "rear-impact-damage",
    description: "Rear impact damage",
    severity: "major", category: "crash",
    statDebuffs: { power: 0.20, handling: 0.20 },
    weight: 3, sparePartsCost: 4, workUnits: 55,
  },
  {
    id: "side-panel-crush",
    description: "Side panel crush",
    severity: "medium", category: "crash",
    statDebuffs: { handling: 0.15, comfort: 0.20, fuelEfficiency: 0.10 },
    weight: 3, sparePartsCost: 3, workUnits: 45,
  },
  {
    id: "bent-suspension-arm",
    description: "Bent suspension arm",
    severity: "major", category: "crash",
    statDebuffs: { handling: 0.35, tyreDurability: 0.15 },
    weight: 1, sparePartsCost: 5, workUnits: 60,
  },
];

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

/** All mechanical issue templates (used for the standard per-lap issue roll). */
export const MECHANICAL_ISSUES: IssueTemplate[] = [
  ...MECHANICAL_MINOR,
  ...MECHANICAL_MEDIUM,
  ...MECHANICAL_MAJOR,
];

/** Crash issue templates (used when a non-terminal crash occurs). */
export const CRASH_ISSUE_TEMPLATES: IssueTemplate[] = CRASH_ISSUES;

/** Complete catalogue — all issues combined. */
export const ALL_ISSUE_TEMPLATES: IssueTemplate[] = [
  ...MECHANICAL_ISSUES,
  ...CRASH_ISSUES,
];
