import { useState } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { calculateDriverStats, totalDriverStats } from "../simulation/driverLifecycle";
import { DRIVER_STAT_KEYS, DRIVER_STAT_LABELS } from "../shared/driverData";
import type { PlayerTeam, Driver, Contract } from "../types";
import backdropUrl from "../assets/roster-backdrop.jpg";
import "./DealerShared.scss";
import "./TeamRoster.scss";


export function TeamRosterScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const releaseDriver = useGameStore((s) => s.releaseDriver);

  const [selectedId, setSelectedId] = useState<string | null>("player");

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const playerContracts = game.contracts.filter((c) => c.teamId === "player" && c.remainingYears > 0);
  const hiredDrivers = playerContracts.map((c) => {
    const d = game.drivers.find((dr) => dr.id === c.driverId);
    return d ? { driver: d, contract: c } : null;
  }).filter((x): x is { driver: Driver; contract: Contract } => x !== null);

  const playerCharOvr = player.skills.driver * 5;
  const emptySlots = Math.max(0, 3 - hiredDrivers.length);
  const totalDrivers = 1 + hiredDrivers.length;

  const isPlayerSelected = selectedId === "player";
  const selectedHired = !isPlayerSelected ? hiredDrivers.find((h) => h.driver.id === selectedId) ?? null : null;

  const handleRelease = () => {
    if (!selectedHired) return;
    releaseDriver(selectedHired.driver.id);
    setSelectedId("player");
  };

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />
        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">Team Roster</span>
          <span className="breadcrumb-counter">Drivers: {totalDrivers} / 4</span>
        </div>

        <div className="split">
          {/* Left: roster list (narrower) */}
          <div className="roster-list">
            <div className="list-header">
              <div className="list-title">Your Drivers</div>
            </div>
            <div className="list-scroll">
              {/* Player character */}
              <div
                className={`roster-item ${selectedId === "player" ? "selected" : ""}`}
                onClick={() => setSelectedId("player")}
              >
                <div className="roster-item-info">
                  <div className="roster-item-name">{player.playerName}</div>
                  <div className="roster-item-meta">Team Owner &middot; You</div>
                </div>
                <div className="roster-item-overall">{playerCharOvr}</div>
              </div>

              {/* Hired drivers */}
              {hiredDrivers.map(({ driver: d, contract: c }) => {
                const stats = calculateDriverStats(d);
                const ovr = Math.round(totalDriverStats(stats) / 5);
                return (
                  <div
                    key={d.id}
                    className={`roster-item ${selectedId === d.id ? "selected" : ""}`}
                    onClick={() => setSelectedId(d.id)}
                  >
                    <div className="roster-item-info">
                      <div className="roster-item-name"><span className={`fi fi-${d.nationality} driver-item-flag`} /> {d.name}</div>
                      <div className="roster-item-meta">Age {d.age} &middot; {c.remainingYears}yr contract</div>
                    </div>
                    <div className="roster-item-overall">{ovr}</div>
                  </div>
                );
              })}

              {/* Empty slots */}
              {Array.from({ length: emptySlots }, (_, i) => (
                <div key={`empty-${i}`} className="roster-item empty" onClick={() => setScreen("driverMarket")}>
                  <div className="roster-item-info">
                    <div className="roster-item-name" style={{ color: "#3a5a78" }}>Empty Slot</div>
                    <div className="roster-item-meta">Browse Driver Market &rsaquo;</div>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Right: detail panel */}
          <div className="detail-panel">
            {isPlayerSelected ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name">{player.playerName}</div>
                    <div className="detail-meta">Team Owner &middot; <span className="you-badge">You</span></div>
                  </div>
                </div>

                <div className="stats-section">
                  <div className="stats-section-title">Driver Stats</div>
                  {DRIVER_STAT_KEYS.map((key) => (
                    <div className="stat-row" key={key}>
                      <span className="stat-name">{DRIVER_STAT_LABELS[key]}</span>
                      <div className="stat-bar-track">
                        <div className="stat-bar-fill" style={{ width: `${playerCharOvr}%` }} />
                      </div>
                      <span className="stat-value">{playerCharOvr}</span>
                    </div>
                  ))}
                </div>

                <div className="contract-info-section">
                  <div className="player-note">No contract — you are the team owner. Your stats are determined by your Driver skill level ({player.skills.driver}/20).</div>
                </div>
              </>
            ) : selectedHired ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name"><span className={`fi fi-${selectedHired.driver.nationality} driver-item-flag`} /> {selectedHired.driver.name}</div>
                    <div className="detail-meta">Age {selectedHired.driver.age}</div>
                  </div>
                  <div className="detail-price-block">
                    <div className="detail-price-label">Market Value</div>
                    <div className="detail-price">${selectedHired.driver.marketValue.toLocaleString()}</div>
                  </div>
                </div>

                <div className="stats-section">
                  <div className="stats-section-title">Driver Stats</div>
                  {DRIVER_STAT_KEYS.map((key) => {
                    const stats = calculateDriverStats(selectedHired.driver);
                    return (
                      <div className="stat-row" key={key}>
                        <span className="stat-name">{DRIVER_STAT_LABELS[key]}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${stats[key]}%` }} />
                        </div>
                        <span className="stat-value">{Math.round(stats[key])}</span>
                      </div>
                    );
                  })}
                </div>

                <div className="contract-info-section">
                  <div className="contract-details">
                    <div><span className="contract-label">Contract:</span> <span className="contract-text">{selectedHired.contract.length} year{selectedHired.contract.length > 1 ? "s" : ""}</span></div>
                    <div><span className="contract-label">Remaining:</span> <span className="contract-text">{selectedHired.contract.remainingYears} year{selectedHired.contract.remainingYears > 1 ? "s" : ""}</span></div>
                    <div><span className="contract-label">Salary:</span> <span className="contract-text">${selectedHired.contract.annualSalary.toLocaleString()}/race</span></div>
                  </div>
                  <div className="buy-row" style={{ marginTop: 16 }}>
                    <button className="btn-release" onClick={handleRelease}>Release Driver</button>
                  </div>
                </div>
              </>
            ) : (
              <div className="detail-empty">SELECT A DRIVER TO VIEW DETAILS</div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
