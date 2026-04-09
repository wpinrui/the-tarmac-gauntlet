import { useCallback } from "react";
import { useGameStore } from "../state/store";
import type { PlayerTeam, RaceHistoryEntry } from "../types";
import { ClassBadge } from "../shared/ClassBadge";
import { ordinal, EVENT_ICONS } from "../shared/raceDisplay";
import { calculatePrestige, type PastRaceResult } from "../simulation/postRace";
import { advanceYear } from "../simulation/yearAdvance";
import "./RaceShared.scss";
import "./PostRaceSummary.scss";

let yearAdvanceIdCounter = Date.now() + 500_000;

export function PostRaceSummaryScreen() {
  const game = useGameStore((s) => s.game);
  const setPhase = useGameStore((s) => s.setPhase);
  const setGameState = useGameStore((s) => s.setGameState);

  const handleContinue = useCallback(() => {
    if (!game) return;

    // 1. Recalculate prestige for all teams
    const updatedTeams = game.teams.map((t) => {
      const teamResults: PastRaceResult[] = [];
      for (let i = game.raceHistory.length - 1; i >= 0; i--) {
        const entry = game.raceHistory[i];
        const teamResult = entry.results.find((r) => r.teamId === t.id);
        if (teamResult) {
          teamResults.push({ position: teamResult.position, totalCars: entry.results.length });
        }
      }
      const newPrestige = calculatePrestige(teamResults);
      return { ...t, prestige: newPrestige, prestigeHistory: [...t.prestigeHistory, newPrestige] };
    });

    // 2. Check if player has won
    const playerHasWon = game.raceHistory.some((r) =>
      r.results.some((res) => res.teamId === "player" && res.position === 1),
    );

    // 3. Run year advance
    const rookieSpecs = Array.from({ length: 15 }, (_, i) => ({
      id: `rookie-y${game.currentYear}-${i}`,
      name: `Rookie ${game.currentYear}-${i}`,
      nationality: ["gb", "de", "br", "jp", "us", "fr", "it", "es", "au", "nl", "se", "fi", "kr", "mx", "ar"][i % 15],
    }));

    const yearResult = advanceYear(
      {
        drivers: game.drivers,
        contracts: game.contracts,
        teams: updatedTeams,
        carModels: game.carModels,
        rookieSpecs,
        playerHasWon,
        currentYear: game.currentYear,
        raceHistory: game.raceHistory,
        newCarId: () => `car-${++yearAdvanceIdCounter}`,
      },
      Math.random,
    );

    // 4. Update game state: next year, new drivers, new contracts, etc.
    setGameState((g) => ({
      ...g,
      currentYear: g.currentYear + 1,
      phase: "preRace" as const,
      drivers: yearResult.drivers,
      contracts: yearResult.contracts,
      teams: yearResult.teams,
      carMarket: { ...g.carMarket, usedListings: yearResult.usedListings },
      raceHistory: yearResult.raceHistory,
    }));
  }, [game, setGameState]);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;
  const entry = game.raceHistory[game.raceHistory.length - 1] as RaceHistoryEntry | undefined;

  if (!entry) {
    return (
      <div className="postrace-root">
        <div className="header-bar">
          <div><div className="header-title">No Race Data</div></div>
          <button className="btn-continue" onClick={handleContinue}>Continue</button>
        </div>
      </div>
    );
  }

  const playerResult = entry.results.find((r) => r.teamId === "player");
  const playerPrize = playerResult?.prizeMoney ?? 0;
  const events = entry.events ?? [];
  const modeCounters = entry.modeCounters?.["player"] ?? { push: 0, normal: 0, conserve: 0 };
  const totalModeLaps = modeCounters.push + modeCounters.normal + modeCounters.conserve || 1;

  // Class podiums
  const classes = ["F", "E", "D", "C", "B", "A"] as const;
  const classPodiums = classes.map((cls) => {
    const classResults = entry.results.filter((r) => r.carClass === cls && !r.retired).slice(0, 3);
    return { cls, results: classResults };
  });

  return (
    <div className="postrace-root">
      {/* Header */}
      <div className="header-bar">
        <div>
          <div className="header-title">Race {game.currentYear} Complete</div>
          <div className="header-subtitle">Year {game.currentYear} &middot; 24 Hours &middot; {entry.results.length} Cars</div>
        </div>
        <button className="btn-continue" onClick={handleContinue}>Continue</button>
      </div>

      <div className="postrace-main">
        {/* Player hero */}
        {playerResult && (
          <div className="player-hero">
            <div className="hero-position">
              {playerResult.position}<span className="hero-suffix">{ordinal(playerResult.position).replace(String(playerResult.position), "")}</span>
            </div>
            <div className="hero-info">
              <div className="hero-team">{player.name}</div>
              <div className="hero-details">
                {playerResult.lapsCompleted} laps &middot; {playerResult.retired ? "Retired" : "Finished"}
              </div>
            </div>
            <div className="hero-stats">
              <div className="hero-stat">
                <div className="hero-stat-value prize">${playerPrize.toLocaleString()}</div>
                <div className="hero-stat-label">Prize</div>
              </div>
              <div className="hero-stat">
                <div className="hero-stat-value">{playerResult.lapsCompleted}</div>
                <div className="hero-stat-label">Laps</div>
              </div>
            </div>
            {/* Mode bar */}
            <div>
              <div className="mode-bar">
                <div className="mode-push" style={{ width: `${(modeCounters.push / totalModeLaps) * 100}%` }} />
                <div className="mode-normal" style={{ width: `${(modeCounters.normal / totalModeLaps) * 100}%` }} />
                <div className="mode-conserve" style={{ width: `${(modeCounters.conserve / totalModeLaps) * 100}%` }} />
              </div>
              <div className="mode-legend">
                <span className="mode-legend-item"><span className="mode-dot" style={{ background: "#e17055" }} /> Push {modeCounters.push}</span>
                <span className="mode-legend-item"><span className="mode-dot" style={{ background: "#5ab8d8" }} /> Normal {modeCounters.normal}</span>
                <span className="mode-legend-item"><span className="mode-dot" style={{ background: "#00d4aa" }} /> Conserve {modeCounters.conserve}</span>
              </div>
            </div>
          </div>
        )}

        {/* Class podiums */}
        <div className="podiums-row">
          {classPodiums.map(({ cls, results: cr }) => (
            <div className="podium-card" key={cls}>
              <ClassBadge carClass={cls} />
              {cr.length > 0 ? cr.map((r, i) => (
                <div className="podium-entry" key={r.teamId}>
                  <span className={`podium-pos ${i === 0 ? "p1" : i === 1 ? "p2" : "p3"}`}>{i + 1}</span>
                  <span className={`podium-team ${r.teamId === "player" ? "player" : ""}`}>
                    {game.teams.find((t) => t.id === r.teamId)?.name ?? r.teamId}
                  </span>
                  <span className="podium-overall">P{r.position}</span>
                </div>
              )) : <div className="podium-entry"><span className="podium-team" style={{ color: "#3a5a78" }}>No finishers</span></div>}
            </div>
          ))}
        </div>

        {/* Two-column: results + events */}
        <div className="two-col">
          {/* Results */}
          <div className="panel results-col">
            <div className="panel-header"><span className="panel-title">Results</span></div>
            <div className="results-scroll">
              <table className="results-table">
                <thead>
                  <tr><th>Pos</th><th>Team</th><th>Car</th><th>Class</th><th>Laps</th><th>Prize</th></tr>
                </thead>
                <tbody>
                  {entry.results.map((r) => {
                    const team = game.teams.find((t) => t.id === r.teamId);
                    const isPlayer = r.teamId === "player";
                    const rowCls = isPlayer ? "player-row" : r.retired ? "retired-row" : r.position === 1 ? "gold" : r.position === 2 ? "silver" : r.position === 3 ? "bronze" : "";
                    return (
                      <tr key={r.teamId} className={rowCls}>
                        <td>{r.position}</td>
                        <td>{team?.name ?? r.teamId}</td>
                        <td>{game.carModels.find((m) => m.carClass === r.carClass)?.name ?? ""}</td>
                        <td><ClassBadge carClass={r.carClass} /></td>
                        <td>{r.lapsCompleted}</td>
                        <td>${r.prizeMoney.toLocaleString()}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          {/* Key moments */}
          <div className="panel events-col">
            <div className="panel-header"><span className="panel-title">Key Moments</span></div>
            <div className="events-scroll">
              {events.length > 0 ? events.map((e, i) => {
                const style = EVENT_ICONS[e.type] ?? { icon: "fa-solid fa-circle", color: "#7a96b0" };
                const isPlayerEvent = e.teamId === "player";
                return (
                  <div className={`event-row ${isPlayerEvent ? "player-event" : ""}`} key={i}>
                    <span className="event-lap">L{e.lap}</span>
                    <i className={`${style.icon} event-icon`} style={{ color: style.color }} />
                    <span className="event-text">{e.text}</span>
                  </div>
                );
              }) : (
                <div className="events-empty">No events recorded</div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
