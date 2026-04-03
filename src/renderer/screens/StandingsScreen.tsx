import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { ClassBadge } from "../shared/ClassBadge";
import { ordinal } from "../shared/raceDisplay";
import type { PlayerTeam, CarClass, Team } from "../types";
import "./DealerShared.scss";
import "./Standings.scss";

const CLASS_ORDER: (CarClass | "all")[] = ["all", "F", "E", "D", "C", "B", "A"];

function getTeamClass(team: Team, carModels: { id: string; carClass: CarClass }[]): CarClass | null {
  const enteredCar = team.cars.find((c) => c.id === team.enteredCarId);
  if (!enteredCar) return null;
  const model = carModels.find((m) => m.id === enteredCar.modelId);
  return model?.carClass ?? null;
}

export function StandingsScreen() {
  const game = useGameStore((s) => s.game);
  const [classFilter, setClassFilter] = useState<CarClass | "all">("all");

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  // Build rankings sorted by prestige desc
  const rankings = useMemo(() => {
    const teams = game.teams.map((t) => {
      const cls = getTeamClass(t, game.carModels);
      const history = t.prestigeHistory;
      const prevPrestige = history.length >= 2 ? history[history.length - 2] : null;
      return { team: t, carClass: cls, prevPrestige };
    });
    teams.sort((a, b) => b.team.prestige - a.team.prestige);
    return teams.map((t, i) => ({ ...t, rank: i + 1 }));
  }, [game.teams, game.carModels]);

  // Apply class filter
  const filtered = classFilter === "all"
    ? rankings
    : rankings.filter((r) => r.carClass === classFilter);

  // Player data
  const playerRanking = rankings.find((r) => r.team.id === "player");
  const playerRank = playerRanking?.rank ?? 100;
  const playerPrevPrestige = playerRanking?.prevPrestige;
  const playerTrend = playerPrevPrestige !== null && playerPrevPrestige !== undefined
    ? (() => {
        const prevRank = rankings.filter((r) => (r.prevPrestige ?? 0) > (playerPrevPrestige ?? 0)).length + 1;
        return prevRank - playerRank;
      })()
    : null;

  return (
    <div className="standings-root">
      <div className="standings-app">
        <TopBar />
        <div className="standings-main">
          {/* Player highlight */}
          <div className="player-bar">
            <div className="pb-rank">
              {playerRank}<span className="pb-suffix">{ordinal(playerRank).replace(String(playerRank), "")}</span>
            </div>
            <div className="pb-info">
              <div className="pb-team">{player.name}</div>
              <div className="pb-details">
                Year {game.currentYear}
                {playerRanking?.carClass && <> &middot; Class {playerRanking.carClass}</>}
              </div>
            </div>
            <div className="pb-stat">
              <div className="pb-stat-value">{player.prestige.toFixed(1)}</div>
              <div className="pb-stat-label">Prestige</div>
            </div>
            {playerTrend !== null && (
              <div className="pb-trend">
                <i className={`fa-solid fa-caret-${playerTrend > 0 ? "up" : playerTrend < 0 ? "down" : "right"}`} style={{ color: playerTrend > 0 ? "#00d4aa" : playerTrend < 0 ? "#e17055" : "#5a7a98" }} />
                <div className={`pb-trend-value ${playerTrend > 0 ? "up" : playerTrend < 0 ? "down" : ""}`}>
                  {playerTrend > 0 ? `+${playerTrend}` : playerTrend < 0 ? `${playerTrend}` : "\u2014"}
                </div>
                <div className="pb-trend-label">YoY</div>
              </div>
            )}
          </div>

          {/* Rankings panel */}
          <div className="standings-panel">
            <div className="standings-panel-header">
              <span className="standings-panel-title">Standings</span>
              <div className="standings-class-filter">
                {CLASS_ORDER.map((cls) => (
                  <button
                    key={cls}
                    className={`class-btn ${classFilter === cls ? "active" : ""}`}
                    onClick={() => setClassFilter(cls)}
                  >
                    {cls === "all" ? "All" : `Class ${cls}`}
                  </button>
                ))}
              </div>
            </div>
            <div className="standings-scroll">
              <table className="standings-table">
                <thead>
                  <tr>
                    <th style={{ width: 60 }}>Rank</th>
                    <th>Team</th>
                    <th className="center" style={{ width: 80 }}>Class</th>
                    <th className="right" style={{ width: 100 }}>Prestige</th>
                    <th className="right" style={{ width: 100 }}>Trend</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((r) => {
                    const isPlayer = r.team.id === "player";
                    const history = r.team.prestigeHistory;
                    const prevPrestige = history.length >= 2 ? history[history.length - 2] : null;
                    const delta = prevPrestige !== null ? r.team.prestige - prevPrestige : null;

                    return (
                      <tr key={r.team.id} className={isPlayer ? "player-row" : ""}>
                        <td><span className={`rank-num ${r.rank <= 3 ? "top3" : ""}`}>{r.rank}</span></td>
                        <td className="team-cell">{r.team.name}</td>
                        <td className="center">
                          {r.carClass && <ClassBadge carClass={r.carClass} />}
                        </td>
                        <td className="right"><span className="prestige-score">{r.team.prestige.toFixed(1)}</span></td>
                        <td className="right">
                          {delta !== null ? (
                            <span className="trend">
                              <i className={`fa-solid fa-caret-${delta > 0 ? "up" : delta < 0 ? "down" : "right"} trend-arrow ${delta > 0 ? "up" : delta < 0 ? "down" : "flat"}`} />
                              <span className={`trend-delta ${delta > 0 ? "up" : delta < 0 ? "down" : "flat"}`}>
                                {delta > 0 ? `+${delta.toFixed(1)}` : delta < 0 ? delta.toFixed(1) : "\u2014"}
                              </span>
                            </span>
                          ) : (
                            <span className="trend-delta flat">&mdash;</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
