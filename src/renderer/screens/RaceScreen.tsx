import { useCallback, useEffect, useMemo } from "react";
import { useGameStore } from "../state/store";
import { useRaceClock } from "../hooks/useRaceClock";
import { processPostRaceFinancials } from "../simulation/postRaceFinancials";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { TOTAL_RACE_SECONDS, leaderLapAt } from "../simulation/raceClock";
import type { CarClass, GameState, PlayerTeam, RaceHistoryEntry } from "../types";
import type { CarLapSnapshot } from "../simulation/raceLoop";
import { TrackMap } from "./TrackMap";
import "./RaceScreen.scss";

export function RaceScreen() {
  const game = useGameStore((s) => s.game);
  const raceSession = useGameStore((s) => s.raceSession);
  const finishRaceSession = useGameStore((s) => s.finishRaceSession);
  const clearRaceSession = useGameStore((s) => s.clearRaceSession);
  const setPhase = useGameStore((s) => s.setPhase);
  const pushRaceHistory = useGameStore((s) => s.pushRaceHistory);
  const awardPrizeMoney = useGameStore((s) => s.awardPrizeMoney);
  const deductFuelCost = useGameStore((s) => s.deductFuelCost);

  const elapsedSec = useRaceClock(
    raceSession?.status === "running",
    TOTAL_RACE_SECONDS,
  );

  const leaderLap = raceSession ? leaderLapAt(raceSession.result, elapsedSec) : 0;
  const remainingSec = Math.max(0, TOTAL_RACE_SECONDS - elapsedSec);

  const carIndex = useMemo(() => (game ? buildCarIndex(game) : new Map<string, CarRef>()), [game]);
  const carTeamIds = useMemo(() => {
    const out: Record<string, string> = {};
    for (const [carId, ref] of carIndex) out[carId] = ref.teamId;
    return out;
  }, [carIndex]);
  const playerCarId =
    (game?.teams.find((t) => t.kind === "player") as PlayerTeam | undefined)?.enteredCarId ?? null;

  const handleFinish = useCallback(() => {
    if (!game || !raceSession) return;

    const player = game.teams.find((t) => t.kind === "player") as PlayerTeam | undefined;
    const playerFuelConsumed = playerCarId
      ? approximatePlayerFuelConsumed(
          raceSession.result.lapSnapshots[playerCarId] ?? [],
          playerStartingFuelLitres(game, player!, playerCarId),
        )
      : 0;

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
      carIndexById: raceSession.result.carIndexById,
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

    finishRaceSession();
    clearRaceSession();
    setPhase("postRace");
  }, [
    game,
    raceSession,
    playerCarId,
    carIndex,
    pushRaceHistory,
    awardPrizeMoney,
    deductFuelCost,
    finishRaceSession,
    clearRaceSession,
    setPhase,
  ]);

  // Auto-finish at 24:00 wall-clock. The `status === "running"` guard prevents
  // a double-fire: handleFinish flips status to "finished", so the next render
  // skips the branch even though elapsedSec is still pinned at the cap.
  useEffect(() => {
    if (
      raceSession?.status === "running" &&
      game &&
      elapsedSec >= TOTAL_RACE_SECONDS
    ) {
      handleFinish();
    }
  }, [elapsedSec, raceSession, game, handleFinish]);

  if (!game || !raceSession) return null;

  return (
    <div className="race-root">
      <div className="race-left">
        <div className="race-trackmap">
          <TrackMap
            result={raceSession.result}
            elapsedSec={elapsedSec}
            playerCarId={playerCarId}
            carTeamIds={carTeamIds}
          />
        </div>
        <div className="standings-placeholder">Standings — Phase 4</div>
      </div>
      <div className="race-panel">
        <div className="race-title">The 24h Tarmac Gauntlet</div>
        <div className="race-status">
          {raceSession.status === "running" ? "Race in progress…" : "Race complete"}
        </div>
        <div className="race-clock">
          <span className="clock-elapsed">{formatMmSs(elapsedSec)}</span>
          <span className="clock-divider">/</span>
          <span className="clock-total">24:00</span>
        </div>
        <div className="race-laps">
          Lap <span className="laps-leader">{leaderLap}</span>
        </div>
        <div className="race-eta">ETA {formatMmSs(remainingSec)}</div>
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

function formatMmSs(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
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
// snapshot), but is deterministic and tied to sim data. A later phase
// replaces this with real per-lap fuel accounting once the snapshot record
// carries pre-pit consumption.
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
