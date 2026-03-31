import type {
  CarInstance,
  CarModel,
  CarStats,
  ActiveIssue,
  InstructionMode,
  IssueTemplate,
  PitStopConfig,
  DriverStats,
  RaceEvent,
  Stint,
  ModeCounter,
} from "../types";
import { calculateEffectiveStats } from "./effectiveStats";
import { calculateLapTime } from "./lapTime";
import {
  updateTyreWear,
  updateFuelLevel,
  updateDriverFatigue,
  updateCarCondition,
} from "./degradation";
import { executePitStop } from "./pitStop";
import { rollLapRisks } from "./riskRolls";
import type { FailureType } from "./riskRolls";

// ---------------------------------------------------------------------------
// Tunable constants — balance values, adjust freely without touching logic
// ---------------------------------------------------------------------------

/** Default number of laps per race (GDD §2). */
const DEFAULT_TOTAL_LAPS = 48;

/** AI pits when tyre wear exceeds this threshold. */
const AI_TYRE_PIT_THRESHOLD = 80;

/**
 * AI pits when fuel is below this fraction of the car's effective fuel capacity.
 * e.g. 0.15 = pit when less than 15% remains.
 */
const AI_FUEL_PIT_THRESHOLD_FRACTION = 0.15;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A driver with pre-resolved stats for a race entry. */
export interface RaceDriver {
  id: string;
  stats: DriverStats;
}

/**
 * A snapshot of a car's state after completing a lap, passed to pit and mode deciders.
 * All values reflect the post-lap degradation (before any pit stop is executed).
 */
export interface CarLapSnapshot {
  lapsCompleted: number;
  totalTime: number;
  tyreWear: number;
  fuelRemaining: number;
  /** Effective fuel capacity this lap (condition-adjusted). */
  fuelCapacity: number;
  condition: number;
  currentDriverId: string;
  driverFatigue: Record<string, number>;
  instructionMode: InstructionMode;
  activeIssues: ActiveIssue[];
  tyreSetsAvailable: number;
  sparePartsAvailable: number;
}

/** All inputs needed to enter a car in the race. */
export interface CarEntry {
  carId: string;
  teamId: string;
  instance: CarInstance;
  model: CarModel;
  /** All drivers available for this car; at least one required. */
  drivers: RaceDriver[];
  /** ID of the driver who starts the race. Must be in `drivers`. */
  startingDriverId: string;
  /** Starting fuel load in litres. */
  startingFuel: number;
  /** Tyre sets in team inventory at race start. */
  tyreSetsAvailable: number;
  /** Spare parts units at race start. */
  sparePartsAvailable: number;
  crewSize: number;
  engineerSkill: number;
  /** Starting tyre wear (0 = fresh). Defaults to 0. */
  startingTyreWear?: number;
  /**
   * Pit decision callback.
   * Called after every lap with the post-lap state snapshot.
   * Return a PitStopConfig to pit this stop, or null to continue.
   * AI cars without this callback use the default threshold strategy.
   */
  pitDecider?: (lap: number, snapshot: CarLapSnapshot) => PitStopConfig | null;
  /**
   * Instruction mode callback.
   * Called after every non-pitting lap to set the mode for the next lap.
   * AI cars without this callback always return "normal" (GDD §8).
   * Has no effect when the car pits — pit stop always resets mode to "normal".
   */
  modeDecider?: (lap: number, snapshot: CarLapSnapshot) => InstructionMode;
}

/** Options for the race simulation. */
export interface RaceOptions {
  /** Total laps to simulate. Defaults to 48 (GDD §2). */
  totalLaps?: number;
  /** Injectable random source for deterministic testing. */
  random?: () => number;
  /** Mechanical issue template catalogue. Defaults to [] (no issues). */
  issueTemplates?: IssueTemplate[];
  /** Crash issue template catalogue. Defaults to [] (no crash issues). */
  crashTemplates?: IssueTemplate[];
}

/** Final result for one car. */
export interface CarRaceResult {
  carId: string;
  /** 1-indexed finishing position. */
  finalPosition: number;
  lapsCompleted: number;
  /** Total accumulated time in seconds (includes pit stop durations). */
  totalTime: number;
  retired: boolean;
  /** Lap on which the car retired, or null if it finished. */
  retirementLap: number | null;
  retirementReason: FailureType | null;
}

/** Full race results including rich per-lap data. */
export interface RaceResultFull {
  /** All cars sorted by finalPosition (1st = index 0). */
  results: CarRaceResult[];
  /** Fastest single lap across all cars and all laps, or null if no laps ran. */
  fastestLap: { carId: string; lap: number; time: number } | null;
  /** Per-lap snapshots: carId → CarLapSnapshot[]. */
  lapSnapshots: Record<string, CarLapSnapshot[]>;
  /** Position history: positionHistory[lap][carIndex] = position (1-indexed). */
  positionHistory: number[][];
  /** Endurance events logged during the race. */
  events: RaceEvent[];
  /** Driver stints: carId → Stint[]. */
  stints: Record<string, Stint[]>;
  /** Instruction mode counters: carId → ModeCounter. */
  modeCounters: Record<string, ModeCounter>;
}

// ---------------------------------------------------------------------------
// Internal working state
// ---------------------------------------------------------------------------

interface CarWorkingState {
  entry: CarEntry;
  lapsCompleted: number;
  totalTime: number;
  condition: number;
  tyreWear: number;
  fuelRemaining: number;
  currentDriverId: string;
  driverFatigue: Record<string, number>;
  instructionMode: InstructionMode;
  activeIssues: ActiveIssue[];
  tyreSetsAvailable: number;
  sparePartsAvailable: number;
  retired: boolean;
  retirementLap: number | null;
  retirementReason: FailureType | null;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeSnapshot(
  state: CarWorkingState,
  fuelCapacity: number,
  postLapTyreWear: number,
  postLapFuel: number,
): CarLapSnapshot {
  return {
    lapsCompleted: state.lapsCompleted,
    totalTime: state.totalTime,
    tyreWear: postLapTyreWear,
    fuelRemaining: postLapFuel,
    fuelCapacity,
    condition: state.condition,
    currentDriverId: state.currentDriverId,
    driverFatigue: { ...state.driverFatigue },
    instructionMode: state.instructionMode,
    activeIssues: [...state.activeIssues],
    tyreSetsAvailable: state.tyreSetsAvailable,
    sparePartsAvailable: state.sparePartsAvailable,
  };
}

/**
 * Applies percentage-based stat debuffs from all active issues to effective stats.
 * Each debuff is a fractional reduction, e.g. { power: 0.10 } = -10% power.
 * Multiple debuffs on the same stat stack multiplicatively.
 */
function applyIssueDebuffs(
  stats: CarStats,
  activeIssues: ActiveIssue[],
  allTemplates: IssueTemplate[],
): CarStats {
  if (activeIssues.length === 0) return stats;

  const result = { ...stats };
  for (const issue of activeIssues) {
    const template = allTemplates.find((t) => t.id === issue.templateId);
    if (!template) continue;
    for (const [stat, debuff] of Object.entries(template.statDebuffs)) {
      const key = stat as keyof CarStats;
      result[key] = result[key] * (1 - (debuff as number));
    }
  }
  return result;
}

/**
 * Default pit strategy for AI cars (GDD §8: "simple threshold strategy").
 * Pits when tyres are heavily worn or fuel is low.
 * Always refuels to capacity and changes tyres if sets are available.
 * Cycles through the driver roster one step each stop.
 */
function aiPitDecider(
  _lap: number,
  snapshot: CarLapSnapshot,
  entry: CarEntry,
): PitStopConfig | null {
  const tyreLow = snapshot.tyreWear > AI_TYRE_PIT_THRESHOLD;
  const fuelLow =
    snapshot.fuelRemaining < snapshot.fuelCapacity * AI_FUEL_PIT_THRESHOLD_FRACTION;

  if (!tyreLow && !fuelLow) return null;

  // Rotate to the next driver in the roster
  const currentIdx = entry.drivers.findIndex(
    (d) => d.id === snapshot.currentDriverId,
  );
  const nextIdx = (currentIdx + 1) % entry.drivers.length;
  const nextDriver = entry.drivers[nextIdx];
  const nextDriverId =
    nextDriver.id !== snapshot.currentDriverId ? nextDriver.id : null;

  return {
    fuelToAdd: snapshot.fuelCapacity - snapshot.fuelRemaining,
    changeTyres: snapshot.tyreSetsAvailable > 0,
    nextDriverId,
    issueIdsToFix: snapshot.activeIssues.map((i) => i.templateId),
  };
}

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

/**
 * Simulates a complete race for all entered cars (GDD §2).
 *
 * Each lap, for every non-retired car:
 *   1. Calculate effective stats (2A) using current condition.
 *   2. Calculate lap time (2B) — includes active-issue lap-time penalties.
 *   3. Roll for issues and failures (2E).
 *   4. Apply degradation: tyre wear, fuel, fatigue, condition (2C).
 *   5. If failure: retire the car; no pit stop runs.
 *   6. If fuel is exhausted: trigger a forced pit stop.
 *   7. Otherwise: ask the car's pit decider (or AI strategy) whether to pit.
 *   8. Execute pit stop if requested (2D); adds duration to total time.
 *   9. Apply the next-lap instruction mode from the mode decider (no-op if pitting).
 *
 * Standings are ranked by laps completed descending, then total time ascending for ties.
 *
 * All existing Phase 2 functions are called as-is — this orchestrator contains no
 * physics logic of its own.
 *
 * @param cars    One entry per car. Order is arbitrary (standings are computed at the end).
 * @param options Global race settings (laps, randomness, issue catalogue).
 */
export function simulateRace(
  cars: CarEntry[],
  options: RaceOptions = {},
): RaceResultFull {
  const totalLaps = options.totalLaps ?? DEFAULT_TOTAL_LAPS;
  const random = options.random ?? Math.random;
  const issueTemplates = options.issueTemplates ?? [];
  const crashTemplates = options.crashTemplates ?? [];
  /** All templates combined — needed for pit stop issue fix lookups. */
  const allTemplates = [...issueTemplates, ...crashTemplates];

  // Initialize working state for every car
  const states: CarWorkingState[] = cars.map((entry) => ({
    entry,
    lapsCompleted: 0,
    totalTime: 0,
    condition: entry.instance.condition,
    tyreWear: entry.startingTyreWear ?? 0,
    fuelRemaining: entry.startingFuel,
    currentDriverId: entry.startingDriverId,
    driverFatigue: Object.fromEntries(entry.drivers.map((d) => [d.id, 0])),
    instructionMode: "normal",
    activeIssues: [],
    tyreSetsAvailable: entry.tyreSetsAvailable,
    sparePartsAvailable: entry.sparePartsAvailable,
    retired: false,
    retirementLap: null,
    retirementReason: null,
  }));

  let fastestLap: { carId: string; lap: number; time: number } | null = null;

  // Rich data accumulators
  const lapSnapshots: Record<string, CarLapSnapshot[]> = {};
  const positionHistory: number[][] = [];
  const events: RaceEvent[] = [];
  const stints: Record<string, Stint[]> = {};
  const modeCounters: Record<string, ModeCounter> = {};

  for (const entry of cars) {
    lapSnapshots[entry.carId] = [];
    stints[entry.carId] = [{ driverId: entry.startingDriverId, startLap: 1, endLap: -1 }];
    modeCounters[entry.carId] = { push: 0, normal: 0, conserve: 0 };
  }

  // Track previous lap's class leaders for class lead change detection
  let prevClassLeaders: Record<string, string> = {}; // class → carId

  // ---------------------------------------------------------------------------
  // Race loop
  // ---------------------------------------------------------------------------

  for (let lap = 1; lap <= totalLaps; lap++) {
    for (const state of states) {
      if (state.retired) continue;

      const entry = state.entry;

      // 1. Effective stats — recalculated each lap since condition degrades
      const instanceNow: CarInstance = {
        ...entry.instance,
        condition: state.condition,
      };
      const effectiveStats = calculateEffectiveStats(instanceNow, entry.model);

      // 2. Current driver stats + fatigue
      const currentDriver = entry.drivers.find(
        (d) => d.id === state.currentDriverId,
      )!;
      const currentFatigue = state.driverFatigue[state.currentDriverId] ?? 0;

      // 3. Apply active issue stat debuffs to effective stats
      const debuffedStats = applyIssueDebuffs(effectiveStats, state.activeIssues, allTemplates);

      // 4. Lap time using debuffed stats
      const lapTime = calculateLapTime(
        debuffedStats,
        currentDriver.stats,
        currentFatigue,
        state.instructionMode,
        state.tyreWear,
        state.fuelRemaining,
        random,
      );

      // Track fastest lap (total on-track time including issue penalties)
      if (fastestLap === null || lapTime < fastestLap.time) {
        fastestLap = { carId: entry.carId, lap, time: lapTime };
      }

      // 5. Roll for issues and failures
      const riskResult = rollLapRisks({
        currentLap: lap,
        condition: state.condition,
        carAge: entry.instance.age,
        reliability: debuffedStats.reliability,
        driverSafety: currentDriver.stats.safety,
        instructionMode: state.instructionMode,
        activeIssues: state.activeIssues,
        issueTemplates,
        crashTemplates,
        random,
      });

      // 5. Degradation (computed before applying, so failure can still record final state)
      const newTyreWear = updateTyreWear(
        state.tyreWear,
        effectiveStats.tyreDurability,
        currentDriver.stats.smoothness,
        state.instructionMode,
      );
      const newFuel = updateFuelLevel(
        state.fuelRemaining,
        effectiveStats.fuelEfficiency,
        state.instructionMode,
      );
      const newFatigue = updateDriverFatigue(
        currentFatigue,
        effectiveStats.comfort,
        currentDriver.stats.stamina,
        state.instructionMode,
      );
      const newCondition = updateCarCondition(
        state.condition,
        entry.instance.age,
        entry.engineerSkill,
        state.instructionMode,
      );

      // 6. Complete the lap
      state.totalTime += lapTime;
      state.lapsCompleted += 1;

      // 7. Terminal failure — retire; no pit stop runs this lap
      if (riskResult.failure !== null) {
        state.retired = true;
        state.retirementLap = lap;
        state.retirementReason = riskResult.failure;
        state.tyreWear = newTyreWear;
        state.fuelRemaining = newFuel;
        state.driverFatigue = {
          ...state.driverFatigue,
          [state.currentDriverId]: newFatigue,
        };
        state.condition = newCondition;

        // Log retirement event
        events.push({
          lap,
          type: "retirement",
          text: `${entry.carId} retired — ${riskResult.failure}`,
          carId: entry.carId, teamId: entry.teamId,
        });
        // Close the current stint
        const carStints = stints[entry.carId];
        if (carStints.length > 0) carStints[carStints.length - 1].endLap = lap;
        // Count mode for this lap
        modeCounters[entry.carId][state.instructionMode]++;

        continue;
      }

      // 8. Apply degradation and new issues to working state
      state.activeIssues = [...state.activeIssues, ...riskResult.newIssues];
      state.condition = newCondition;
      state.driverFatigue = {
        ...state.driverFatigue,
        [state.currentDriverId]: newFatigue,
      };

      // 9. Snapshot (post-degradation, pre-pit) used by both pit and mode deciders
      const snapshot = makeSnapshot(
        state,
        effectiveStats.fuelCapacity,
        newTyreWear,
        newFuel,
      );

      // --- Rich data capture ---
      lapSnapshots[entry.carId].push(snapshot);
      modeCounters[entry.carId][state.instructionMode]++;

      // Track new issues as events
      for (const issue of riskResult.newIssues) {
        const template = allTemplates.find((t) => t.id === issue.templateId);
        events.push({
          lap,
          type: "issue",
          text: `${entry.carId} — ${template?.description ?? "mechanical issue"}`,
          carId: entry.carId, teamId: entry.teamId,
        });
      }

      // 10. Determine next instruction mode (overridden to "normal" if pitting)
      const nextMode: InstructionMode = entry.modeDecider
        ? entry.modeDecider(lap, snapshot)
        : "normal";

      // 11. Pit decision: forced if fuel exhausted, otherwise ask decider
      const forcedPit = newFuel === 0;
      let pitConfig: PitStopConfig | null;

      if (forcedPit) {
        pitConfig = {
          fuelToAdd: effectiveStats.fuelCapacity,
          changeTyres: state.tyreSetsAvailable > 0,
          nextDriverId: null,
          issueIdsToFix: [],
        };
      } else {
        pitConfig = entry.pitDecider
          ? entry.pitDecider(lap, snapshot)
          : aiPitDecider(lap, snapshot, entry);
      }

      // 12. Execute pit stop or commit degraded tyre/fuel to state
      if (pitConfig !== null) {
        const pitResult = executePitStop(
          {
            pitStopTime: effectiveStats.pitStopTime,
            fuelCapacity: effectiveStats.fuelCapacity,
            currentFuel: newFuel,
            currentTyreWear: newTyreWear,
            tyreSetsAvailable: state.tyreSetsAvailable,
            sparePartsAvailable: state.sparePartsAvailable,
            currentDriverId: state.currentDriverId,
            driverFatigue: state.driverFatigue,
            activeIssues: state.activeIssues,
            issueTemplates: allTemplates,
            crewSize: entry.crewSize,
            engineerSkill: entry.engineerSkill,
          },
          pitConfig,
        );
        state.totalTime += pitResult.duration;
        state.fuelRemaining = pitResult.fuelLevel;
        state.tyreWear = pitResult.tyreWear;
        state.tyreSetsAvailable = pitResult.tyreSetsRemaining;
        state.sparePartsAvailable = pitResult.sparePartsRemaining;
        state.activeIssues = pitResult.activeIssues;
        state.instructionMode = pitResult.instructionMode; // always "normal"

        // Log pit stop event
        const driverSwapped = pitResult.currentDriverId !== state.currentDriverId;
        events.push({
          lap,
          type: "pitStop",
          text: `${entry.carId} pits${driverSwapped ? ` — ${pitResult.currentDriverId} takes over` : ""}`,
          carId: entry.carId, teamId: entry.teamId,
        });

        // Track stint changes on driver swap
        if (driverSwapped) {
          const carStints = stints[entry.carId];
          if (carStints.length > 0) carStints[carStints.length - 1].endLap = lap;
          carStints.push({ driverId: pitResult.currentDriverId, startLap: lap + 1, endLap: -1 });
        }

        state.currentDriverId = pitResult.currentDriverId;
        state.driverFatigue = pitResult.driverFatigue;
      } else {
        state.tyreWear = newTyreWear;
        state.fuelRemaining = newFuel;
        state.instructionMode = nextMode;
      }
    }

    // --- End-of-lap: compute standings and position history ---
    const lapStandings = [...states].sort((a, b) => {
      if (b.lapsCompleted !== a.lapsCompleted) return b.lapsCompleted - a.lapsCompleted;
      return a.totalTime - b.totalTime;
    });

    const lapPositions: number[] = new Array(states.length);
    for (let i = 0; i < lapStandings.length; i++) {
      const stateIdx = states.indexOf(lapStandings[i]);
      lapPositions[stateIdx] = i + 1;
    }
    positionHistory.push(lapPositions);

    // Detect class lead changes
    const classLeaders: Record<string, string> = {};
    for (const s of lapStandings) {
      const cls = s.entry.model.carClass;
      if (!classLeaders[cls]) {
        classLeaders[cls] = s.entry.carId;
      }
    }
    for (const [cls, leadCarId] of Object.entries(classLeaders)) {
      if (prevClassLeaders[cls] && prevClassLeaders[cls] !== leadCarId) {
        events.push({
          lap,
          type: "classLeadChange",
          text: `${leadCarId} takes the Class ${cls} lead`,
          carId: leadCarId,
          teamId: states.find((s) => s.entry.carId === leadCarId)?.entry.teamId ?? "",
        });
      }
    }
    prevClassLeaders = classLeaders;

    // Fastest lap event
    if (fastestLap && fastestLap.lap === lap) {
      const fl = fastestLap;
      events.push({
        lap,
        type: "fastestLap",
        text: `Fastest lap: ${fl.carId} — ${fl.time.toFixed(1)}s`,
        carId: fl.carId,
        teamId: states.find((s) => s.entry.carId === fl.carId)?.entry.teamId ?? "",
      });
    }
  }

  // ---------------------------------------------------------------------------
  // Final standings — laps completed desc, total time asc for equal laps
  // ---------------------------------------------------------------------------

  const sorted = [...states].sort((a, b) => {
    if (b.lapsCompleted !== a.lapsCompleted) return b.lapsCompleted - a.lapsCompleted;
    return a.totalTime - b.totalTime;
  });

  const results: CarRaceResult[] = sorted.map((state, idx) => ({
    carId: state.entry.carId,
    finalPosition: idx + 1,
    lapsCompleted: state.lapsCompleted,
    totalTime: state.totalTime,
    retired: state.retired,
    retirementLap: state.retirementLap,
    retirementReason: state.retirementReason,
  }));

  // Close all open stints
  for (const state of states) {
    const carStints = stints[state.entry.carId];
    if (carStints.length > 0 && carStints[carStints.length - 1].endLap === -1) {
      carStints[carStints.length - 1].endLap = state.lapsCompleted;
    }
  }

  return { results, fastestLap, lapSnapshots, positionHistory, events, stints, modeCounters };
}
