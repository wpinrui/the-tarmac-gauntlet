import { useCallback, useEffect } from "react";
import { useGameStore } from "../state/store";
import { processPostRaceFinancials } from "../simulation/postRaceFinancials";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import type { CarClass, GameState, PlayerTeam, RaceHistoryEntry } from "../types";
import type { CarLapSnapshot } from "../simulation/raceLoop";
import "./RaceScreen.scss";

const LAP_TICK_MS = 500;

export function RaceScreen() {
  const game = useGameStore((s) => s.game);
  const raceSession = useGameStore((s) => s.raceSession);
  const advanceRaceLap = useGameStore((s) => s.advanceRaceLap);
  const clearRaceSession = useGameStore((s) => s.clearRaceSession);
  const setPhase = useGameStore((s) => s.setPhase);
  const pushRaceHistory = useGameStore((s) => s.pushRaceHistory);
  const awardPrizeMoney = useGameStore((s) => s.awardPrizeMoney);
  const deductFuelCost = useGameStore((s) => s.deductFuelCost);

  const totalLaps = raceSession?.result.positionHistory.length ?? 0;

  useEffect(() => {
    if (!raceSession || raceSession.status !== "running") return;
    const id = setInterval(advanceRaceLap, LAP_TICK_MS);
    return () => clearInterval(id);
  }, [raceSession, advanceRaceLap]);

  const handleFinish = useCallback(() => {
    if (!game || !raceSession) return;

    const player = game.teams.find((t) => t.kind === "player") as PlayerTeam | undefined;
    const playerCarId = player?.enteredCarId ?? null;
    const playerFuelConsumed = playerCarId
      ? approximatePlayerFuelConsumed(
          raceSession.result.lapSnapshots[playerCarId] ?? [],
          playerStartingFuelLitres(game, player!, playerCarId),
        )
      : 0;

    const carIndex = buildCarIndex(game);
    const results = raceSession.result.results.map((r) => {
      const ref = carIndex.get(r.carId);
      return {
        teamId: ref?.teamId ?? "",
        carId: r.carId,
        carClass: ref?.carClass ?? ("F" as CarClass),
        position: r.finalPosition,
        lapsCompleted: r.lapsCompleted,
        retired: r.retired,
      };
    });

    const { prizeMoney, playerFuelCost, raceHistoryEntry } = processPostRaceFinancials({
      results,
      playerFuelConsumed,
      fuelCostPerLitre: game.economyConfig.fuelConfig.costPerLitre,
      year: game.currentYear,
    });

    const enriched: RaceHistoryEntry = {
      ...raceHistoryEntry,
      fastestLap: raceSession.result.fastestLap
        ? {
            teamId: carIndex.get(raceSession.result.fastestLap.carId)?.teamId ?? "",
            time: raceSession.result.fastestLap.time,
          }
        : null,
      lapSnapshots: raceSession.result.lapSnapshots,
      positionHistory: raceSession.result.positionHistory,
      events: raceSession.result.events,
      stints: raceSession.result.stints,
      modeCounters: raceSession.result.modeCounters,
    };

    pushRaceHistory(enriched);
    for (const r of results) {
      const amount = prizeMoney[r.teamId] ?? 0;
      if (amount > 0) awardPrizeMoney(r.teamId, r.position, amount);
    }
    deductFuelCost(playerFuelCost);

    clearRaceSession();
    setPhase("postRace");
  }, [
    game,
    raceSession,
    pushRaceHistory,
    awardPrizeMoney,
    deductFuelCost,
    clearRaceSession,
    setPhase,
  ]);

  if (!game || !raceSession) return null;

  return (
    <div className="race-root">
      <div className="race-panel">
        <div className="race-title">The 24h Tarmac Gauntlet</div>
        <div className="race-status">
          {raceSession.status === "running" ? "Race in progress…" : "Race complete"}
        </div>
        <div className="race-lap-counter">
          <span className="lap-current">{raceSession.currentLap}</span>
          <span className="lap-divider">/</span>
          <span className="lap-total">{totalLaps}</span>
        </div>
        <button className="btn-finish" onClick={handleFinish}>
          Finish race
        </button>
      </div>
    </div>
  );
}

interface CarRef {
  teamId: string;
  carClass: CarClass;
}

function buildCarIndex(game: { teams: { id: string; cars: { id: string; modelId: string }[] }[]; carModels: { id: string; carClass: CarClass }[] }): Map<string, CarRef> {
  const modelClass = new Map<string, CarClass>();
  for (const m of game.carModels) modelClass.set(m.id, m.carClass);
  const out = new Map<string, CarRef>();
  for (const team of game.teams) {
    for (const car of team.cars) {
      out.set(car.id, { teamId: team.id, carClass: modelClass.get(car.modelId) ?? "F" });
    }
  }
  return out;
}

function playerStartingFuelLitres(game: GameState, player: PlayerTeam, carId: string): number {
  const car = player.cars.find((c) => c.id === carId);
  if (!car) return 0;
  const model = game.carModels.find((m) => m.id === car.modelId);
  if (!model) return 0;
  return calculateEffectiveStats(car, model).fuelCapacity;
}

// Phase 1 approximation: walks per-lap snapshots and counts negative
// fuelRemaining deltas as consumption. Understates total burn for laps that
// ended with a pit stop (in-lap consumption is hidden by the post-pit
// snapshot), but is deterministic and tied to sim data. Phase 2/3 replaces
// this with real per-lap fuel accounting.
function approximatePlayerFuelConsumed(
  snapshots: CarLapSnapshot[],
  startingFuel: number,
): number {
  if (snapshots.length === 0) return 0;
  let consumed = 0;
  let prev = startingFuel;
  for (const snap of snapshots) {
    const delta = snap.fuelRemaining - prev;
    if (delta < 0) consumed += -delta;
    prev = snap.fuelRemaining;
  }
  return Math.round(consumed);
}
