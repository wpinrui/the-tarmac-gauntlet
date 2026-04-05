import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { ClassBadge } from "../shared/ClassBadge";
import { ordinal } from "../shared/raceDisplay";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { calculateDriverStats, totalDriverStats } from "../simulation/driverLifecycle";
import type { PlayerTeam, CarClass, Team, CarModel, Driver } from "../types";
import "./ScoutingReport.scss";

const CLASS_ORDER: CarClass[] = ["A", "B", "C", "D", "E", "F"];

function getTeamClass(team: Team, carModels: CarModel[]): CarClass | null {
  const car = team.cars.find((c) => c.id === team.enteredCarId);
  if (!car) return null;
  return carModels.find((m) => m.id === car.modelId)?.carClass ?? null;
}

interface TeamRow {
  team: Team;
  carClass: CarClass;
  carName: string;
  carAge: number;
  condition: number;
  pace: number;
  reliability: number;
  durability: number;
  efficiency: number;
  crew: number;
  bestDriverOvr: number;
  drivers: { name: string; nationality: string }[];
  prestige: number;
  trend: number | null;
  lastRace: number | null;
  isPlayer: boolean;
}

type SortKey = "pace" | "reliability" | "durability" | "efficiency" | "condition" | "crew" | "prestige" | "lastRace";

export function ScoutingReportScreen() {
  const game = useGameStore((s) => s.game);
  const [expandedClasses, setExpandedClasses] = useState<Set<CarClass>>(new Set());
  const [sortKey, setSortKey] = useState<SortKey>("pace");

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const playerClass = getTeamClass(player, game.carModels);
  const hasEnteredCar = !!player.enteredCarId;

  // Auto-expand player's class
  const effectiveExpanded = useMemo(() => {
    const set = new Set(expandedClasses);
    if (playerClass && hasEnteredCar) set.add(playerClass);
    return set;
  }, [expandedClasses, playerClass, hasEnteredCar]);

  const toggleClass = (cls: CarClass) => {
    setExpandedClasses((prev) => {
      const next = new Set(prev);
      if (next.has(cls)) next.delete(cls);
      else next.add(cls);
      return next;
    });
  };

  // Build team rows
  const allRows = useMemo((): TeamRow[] => {
    return game.teams
      .map((team): TeamRow | null => {
        const car = team.cars.find((c) => c.id === team.enteredCarId);
        if (!car) return null;
        const model = game.carModels.find((m) => m.id === car.modelId);
        if (!model) return null;

        const stats = calculateEffectiveStats(car, model);
        const teamContracts = game.contracts.filter((c) => c.teamId === team.id && c.remainingYears > 0);
        const teamDrivers: { name: string; nationality: string; ovr: number }[] = [];

        // Player character
        if (team.kind === "player") {
          const pcOvr = (team as PlayerTeam).skills.driver * 5;
          teamDrivers.push({ name: (team as PlayerTeam).playerName, nationality: "sg", ovr: pcOvr });
        }

        // Contracted drivers
        for (const c of teamContracts) {
          const d = game.drivers.find((dr) => dr.id === c.driverId);
          if (d) {
            const ds = calculateDriverStats(d);
            const ovr = Math.round(totalDriverStats(ds) / 5);
            teamDrivers.push({ name: d.name, nationality: d.nationality, ovr });
          }
        }

        const bestDriverOvr = teamDrivers.length > 0 ? Math.max(...teamDrivers.map((d) => d.ovr)) : 0;

        const history = team.prestigeHistory;
        const prevPrestige = history.length >= 2 ? history[history.length - 2] : null;
        const trend = prevPrestige !== null ? team.prestige - prevPrestige : null;

        const lastEntry = game.raceHistory.length > 0 ? game.raceHistory[game.raceHistory.length - 1] : null;
        const lastResult = lastEntry?.results.find((r) => r.teamId === team.id);
        const lastRace = lastResult?.position ?? null;

        return {
          team,
          carClass: model.carClass,
          carName: model.name,
          carAge: car.age,
          condition: car.condition,
          pace: Math.round(stats.power + stats.handling),
          reliability: Math.round(stats.reliability),
          durability: Math.round(stats.tyreDurability),
          efficiency: Math.round(stats.fuelEfficiency),
          crew: team.crewSize,
          bestDriverOvr,
          drivers: teamDrivers.map((d) => ({ name: d.name, nationality: d.nationality })),
          prestige: team.prestige,
          trend,
          lastRace,
          isPlayer: team.id === "player",
        };
      })
      .filter((r): r is TeamRow => r !== null);
  }, [game.teams, game.carModels, game.contracts, game.drivers, game.raceHistory]);

  // Group by class
  const classGroups = useMemo(() => {
    return CLASS_ORDER.map((cls) => {
      const rows = allRows.filter((r) => r.carClass === cls);
      // Sort
      rows.sort((a, b) => {
        switch (sortKey) {
          case "pace": return b.pace - a.pace;
          case "reliability": return b.reliability - a.reliability;
          case "durability": return b.durability - a.durability;
          case "efficiency": return b.efficiency - a.efficiency;
          case "condition": return b.condition - a.condition;
          case "crew": return b.crew - a.crew;
          case "prestige": return b.prestige - a.prestige;
          case "lastRace": return (a.lastRace ?? 999) - (b.lastRace ?? 999);
          default: return 0;
        }
      });
      const leader = rows[0];
      return { cls, rows, leader };
    }).filter((g) => g.rows.length > 0);
  }, [allRows, sortKey]);

  // Strength thresholds for colour coding
  function strClass(val: number, classAvg: number): string {
    if (val >= classAvg * 1.1) return "str-high";
    if (val <= classAvg * 0.85) return "str-low";
    return "str-mid";
  }

  function condClass(cond: number): string {
    if (cond >= 80) return "cond-good";
    if (cond >= 50) return "cond-fair";
    return "cond-poor";
  }

  // Player comparison data
  const playerRow = allRows.find((r) => r.isPlayer);
  const playerClassRows = playerClass ? allRows.filter((r) => r.carClass === playerClass) : [];
  const classAvgPace = playerClassRows.length > 0 ? Math.round(playerClassRows.reduce((s, r) => s + r.pace, 0) / playerClassRows.length) : 0;
  const classAvgReliability = playerClassRows.length > 0 ? Math.round(playerClassRows.reduce((s, r) => s + r.reliability, 0) / playerClassRows.length) : 0;
  const classAvgCrew = playerClassRows.length > 0 ? Math.round(playerClassRows.reduce((s, r) => s + r.crew, 0) / playerClassRows.length) : 0;
  const classAvgBestDriver = playerClassRows.length > 0 ? Math.round(playerClassRows.reduce((s, r) => s + r.bestDriverOvr, 0) / playerClassRows.length) : 0;

  return (
    <div className="scouting-root">
      <div className="scouting-app">
        <TopBar />
        <div className="scouting-main">
          {!hasEnteredCar && (
            <div className="no-car-prompt">Enter a car in the workshop to see your class position</div>
          )}

          {classGroups.map(({ cls, rows, leader }) => {
            const isPlayerClass = cls === playerClass;
            const isExpanded = effectiveExpanded.has(cls);

            // Class averages for colour coding
            const avgPace = rows.reduce((s, r) => s + r.pace, 0) / rows.length;
            const avgReliab = rows.reduce((s, r) => s + r.reliability, 0) / rows.length;
            const avgDurab = rows.reduce((s, r) => s + r.durability, 0) / rows.length;
            const avgEffic = rows.reduce((s, r) => s + r.efficiency, 0) / rows.length;

            return (
              <div key={cls} className={`class-group ${isPlayerClass ? "player-class" : ""} ${isExpanded ? "expanded" : ""}`}>
                <div className="class-group-header" onClick={() => toggleClass(cls)}>
                  <ClassBadge carClass={cls} />
                  <span className="class-group-title">
                    Class {cls}{isPlayerClass ? " — Your Class" : ""}
                  </span>
                  <span className="class-group-count">{rows.length} team{rows.length !== 1 ? "s" : ""}</span>
                  {leader && (
                    <span className="class-group-leader">Leader: <strong>{leader.team.name}</strong></span>
                  )}
                  <i className="fa-solid fa-chevron-down class-chevron" />
                </div>

                {isExpanded && (
                  <>
                    {/* Comparison bar (player class only) */}
                    {isPlayerClass && playerRow && (
                      <div className="comparison-bar">
                        <span className="comparison-label">Your pace</span>
                        <span className="comparison-stat">{playerRow.pace} <span className="vs">vs</span> <span className="avg">avg {classAvgPace}</span></span>
                        <span className="comparison-label" style={{ marginLeft: 16 }}>Reliability</span>
                        <span className="comparison-stat">{playerRow.reliability} <span className="vs">vs</span> <span className="avg">avg {classAvgReliability}</span></span>
                        <span className="comparison-label" style={{ marginLeft: 16 }}>Crew</span>
                        <span className="comparison-stat">{playerRow.crew} <span className="vs">vs</span> <span className="avg">avg {classAvgCrew}</span></span>
                        <span className="comparison-label" style={{ marginLeft: 16 }}>Best driver</span>
                        <span className="comparison-stat">{playerRow.bestDriverOvr} <span className="vs">vs</span> <span className="avg">avg {classAvgBestDriver}</span></span>
                      </div>
                    )}

                    <div className="class-group-body">
                      <table className="team-table">
                        <thead>
                          <tr>
                            <th style={{ width: 40 }}>#</th>
                            <th>Team</th>
                            <th>Car</th>
                            <th>Drivers</th>
                            <th className={`center ${sortKey === "pace" ? "sorted" : ""}`} onClick={() => setSortKey("pace")}>Pace</th>
                            <th className={`center ${sortKey === "reliability" ? "sorted" : ""}`} onClick={() => setSortKey("reliability")}>Reliab.</th>
                            <th className={`center ${sortKey === "durability" ? "sorted" : ""}`} onClick={() => setSortKey("durability")}>Durab.</th>
                            <th className={`center ${sortKey === "efficiency" ? "sorted" : ""}`} onClick={() => setSortKey("efficiency")}>Effic.</th>
                            <th className={`center ${sortKey === "condition" ? "sorted" : ""}`} onClick={() => setSortKey("condition")}>Cond.</th>
                            <th className={`center ${sortKey === "crew" ? "sorted" : ""}`} onClick={() => setSortKey("crew")}>Crew</th>
                            <th className={`right ${sortKey === "prestige" ? "sorted" : ""}`} onClick={() => setSortKey("prestige")}>Prestige</th>
                            <th className="right">Trend</th>
                            <th className={`right ${sortKey === "lastRace" ? "sorted" : ""}`} onClick={() => setSortKey("lastRace")}>Last Race</th>
                          </tr>
                        </thead>
                        <tbody>
                          {rows.map((r, i) => (
                            <tr key={r.team.id} className={r.isPlayer ? "player-row" : ""}>
                              <td>{i + 1}</td>
                              <td className="team-name-cell">{r.team.name}</td>
                              <td className="car-cell">{r.carName} &middot; Age {r.carAge}</td>
                              <td className="drivers-cell">
                                {r.drivers.map((d, j) => (
                                  <span key={j}>
                                    {j > 0 && ", "}
                                    <span className={`fi fi-${d.nationality} driver-flag`} />
                                    {d.name.includes(" ") ? `${d.name[0]}. ${d.name.split(" ").slice(1).join(" ")}` : d.name}
                                  </span>
                                ))}
                                {r.drivers.length === 0 && "—"}
                              </td>
                              <td className={`center str-cell ${strClass(r.pace, avgPace)}`}>{r.pace}</td>
                              <td className={`center str-cell ${strClass(r.reliability, avgReliab)}`}>{r.reliability}</td>
                              <td className={`center str-cell ${strClass(r.durability, avgDurab)}`}>{r.durability}</td>
                              <td className={`center str-cell ${strClass(r.efficiency, avgEffic)}`}>{r.efficiency}</td>
                              <td className={`center condition-cell ${condClass(r.condition)}`}>{r.condition}%</td>
                              <td className="center crew-cell">{r.crew}</td>
                              <td className="right prestige-cell">{r.prestige.toFixed(1)}</td>
                              <td className="right">
                                {r.trend !== null ? (
                                  <div className="trend-cell">
                                    <i className={`fa-solid fa-caret-${r.trend > 0 ? "up" : r.trend < 0 ? "down" : "right"} trend-arrow ${r.trend > 0 ? "up" : r.trend < 0 ? "down" : ""}`} />
                                    <span className={`trend-delta ${r.trend > 0 ? "up" : r.trend < 0 ? "down" : "flat"}`}>
                                      {r.trend > 0 ? `+${r.trend.toFixed(1)}` : r.trend < 0 ? r.trend.toFixed(1) : "\u2014"}
                                    </span>
                                  </div>
                                ) : (
                                  <span className="trend-delta flat">&mdash;</span>
                                )}
                              </td>
                              <td className="right last-result">{r.lastRace !== null ? ordinal(r.lastRace) : "\u2014"}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
