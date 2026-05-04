import { useState } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { ClassBadge } from "../shared/ClassBadge";
import { EVENT_ICONS } from "../shared/raceDisplay";
import { RACE_HISTORY_WINDOW } from "../simulation/yearAdvance";
import type { PlayerTeam } from "../types";
import "./DealerShared.scss";
import "./RaceShared.scss";
import "./RaceHistory.scss";

export function RaceHistoryScreen() {
  const game = useGameStore((s) => s.game);
  const history = game?.raceHistory ?? [];
  const [selectedYear, setSelectedYear] = useState<number | null>(
    history.length > 0 ? history[history.length - 1].year : null,
  );

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const entry = selectedYear !== null ? history.find((h) => h.year === selectedYear) : null;
  const isWithinWindow = entry ? game.currentYear - entry.year < RACE_HISTORY_WINDOW : false;
  const hasRichData = entry?.positionHistory && entry.positionHistory.length > 0;
  const events = entry?.events ?? [];

  // Player summary
  const playerResult = entry?.results.find((r) => r.teamId === "player");

  // Lap chart SVG dimensions
  const chartW = 600;
  const chartH = 300;
  const totalLaps = entry?.positionHistory?.length ?? 0;
  const totalCars = entry?.results.length ?? 100;

  function posToY(pos: number): number {
    return (pos / totalCars) * chartH;
  }

  function lapToX(lap: number): number {
    return ((lap + 1) / (totalLaps + 1)) * chartW;
  }

  return (
    <div className="racehistory-root">
      <div className="racehistory-app">
        <TopBar />

        <div className="racehistory-main">
          {history.length === 0 ? (
            <div className="no-history">No races completed yet</div>
          ) : (
            <>
              {/* Year selector */}
              <div className="year-selector">
                {history.map((h) => {
                  const dimmed = game.currentYear - h.year >= RACE_HISTORY_WINDOW;
                  return (
                    <button
                      key={h.year}
                      className={`year-btn ${selectedYear === h.year ? "active" : ""} ${dimmed ? "dimmed" : ""}`}
                      onClick={() => setSelectedYear(h.year)}
                    >
                      Year {h.year}
                    </button>
                  );
                })}
              </div>

              {entry && (
                <>
                  {/* Player summary bar */}
                  {playerResult && (
                    <div className="player-summary-bar">
                      <span className="psb-item"><strong>P{playerResult.position}</strong></span>
                      <span className="psb-item">{player.name}</span>
                      <span className="psb-item">{playerResult.lapsCompleted} laps</span>
                      <span className="psb-item">${playerResult.prizeMoney.toLocaleString()}</span>
                      <span className="psb-item"><ClassBadge carClass={playerResult.carClass} /></span>
                    </div>
                  )}

                  <div className="history-two-col">
                    {/* Left: results table */}
                    <div className="panel results-col">
                      <div className="panel-header"><span className="panel-title">Results</span></div>
                      <div className="results-scroll">
                        <table className="results-table">
                          <thead>
                            <tr><th>Pos</th><th>Team</th><th>Car</th><th>Class</th><th>Laps</th></tr>
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
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      </div>
                    </div>

                    {/* Right: lap chart + events */}
                    <div className="right-col">
                      {/* Lap chart */}
                      <div className="panel chart-panel">
                        <div className="panel-header"><span className="panel-title">Lap Chart</span></div>
                        <div className="chart-body">
                          {isWithinWindow && hasRichData ? (
                            <svg viewBox={`0 0 ${chartW} ${chartH}`} className="lap-chart-svg">
                              {/* Grid lines */}
                              {[1, 10, 20, 50, 100].map((p) => (
                                <line key={p} x1={0} y1={posToY(p)} x2={chartW} y2={posToY(p)} stroke="#1e3450" strokeWidth={0.5} />
                              ))}
                              {/* Car lines */}
                              {entry.results.map((r, carIdx) => {
                                const isPlayer = r.teamId === "player";
                                const isP1 = r.position === 1;
                                const positions = entry.positionHistory!.map((lapPositions) => lapPositions[carIdx]);
                                const points = positions.map((pos, lap) => `${lapToX(lap)},${posToY(pos)}`).join(" ");
                                return (
                                  <polyline
                                    key={r.teamId}
                                    points={points}
                                    fill="none"
                                    stroke={isPlayer ? "#00d4aa" : isP1 ? "#d4a840" : "#1e3450"}
                                    strokeWidth={isPlayer ? 2 : isP1 ? 1.5 : 0.5}
                                    opacity={isPlayer || isP1 ? 1 : 0.4}
                                  />
                                );
                              })}
                            </svg>
                          ) : (
                            <div className="chart-empty">Detailed data not available for races older than {RACE_HISTORY_WINDOW} years</div>
                          )}
                        </div>
                      </div>

                      {/* Key moments */}
                      <div className="panel events-panel">
                        <div className="panel-header"><span className="panel-title">Key Moments</span></div>
                        <div className="events-scroll">
                          {isWithinWindow && events.length > 0 ? events.map((e, i) => {
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
                            <div className="events-empty">
                              {isWithinWindow ? "No events recorded" : `Detailed data not available for races older than ${RACE_HISTORY_WINDOW} years`}
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                </>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
