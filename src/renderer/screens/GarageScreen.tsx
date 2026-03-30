import { useState } from "react";
import { useGameStore, type Screen } from "../state/store";
import { TopBar } from "./TopBar";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { calculateDriverStats, totalDriverStats } from "../simulation/driverLifecycle";
import type { PlayerTeam, CarInstance, CarModel, Driver } from "../types";
import garageBackdropUrl from "../assets/garage-backdrop.jpg";
import carBackdropF from "../assets/car-backdrop-f.jpg";
// Placeholder imports for other classes — add the actual images when available:
// import carBackdropE from "../assets/car-backdrop-e.jpg";
// import carBackdropD from "../assets/car-backdrop-d.jpg";
// import carBackdropC from "../assets/car-backdrop-c.jpg";
// import carBackdropB from "../assets/car-backdrop-b.jpg";
// import carBackdropA from "../assets/car-backdrop-a.jpg";
// import carBackdropF1 from "../assets/car-backdrop-f1.jpg";

import type { CarClass } from "../types";

const CAR_BACKDROPS: Partial<Record<CarClass, string>> = {
  F: carBackdropF,
  // E: carBackdropE,
  // D: carBackdropD,
  // C: carBackdropC,
  // B: carBackdropB,
  // A: carBackdropA,
  // F1: carBackdropF1,
};
import { ClassBadge } from "../shared/ClassBadge";
import { DISPLAY_STATS } from "../shared/dealerData";
import { SKILL_TOOLTIPS } from "../shared/skillData";
import "./DealerShared.scss";
import "./GarageScreen.scss";

const PersonIcon = () => (
  <svg viewBox="0 0 24 24">
    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z" />
  </svg>
);

function StatBar({ label, value, max = 100 }: { label: string; value: number; max?: number }) {
  const pct = Math.min(100, Math.max(0, (value / max) * 100));
  return (
    <div className="stat-bar-row">
      <span className="stat-bar-label">{label}</span>
      <div className="stat-bar-track">
        <div className={`stat-bar-fill ${pct < 20 ? "low" : ""}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="stat-bar-val">{Math.round(value)}</span>
    </div>
  );
}



export function GarageScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const [selectedCarIdx, setSelectedCarIdx] = useState(0);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam | undefined;
  if (!player) return null;

  const cars = player.cars;
  const selectedCar: CarInstance | undefined = cars[selectedCarIdx];
  const carModel: CarModel | undefined = selectedCar
    ? game.carModels.find((m) => m.id === selectedCar.modelId)
    : undefined;
  const effectiveStats = selectedCar && carModel ? calculateEffectiveStats(selectedCar, carModel) : null;

  const upgradeCount = selectedCar
    ? [selectedCar.installedUpgrades.power, selectedCar.installedUpgrades.handling, selectedCar.installedUpgrades.comfort].filter(Boolean).length
    : 0;
  const upgradeText = upgradeCount === 0 ? "No upgrades" : `${upgradeCount} upgrade${upgradeCount > 1 ? "s" : ""} installed`;

  const playerDriverContracts = game.contracts.filter((c) => c.teamId === player.id && c.remainingYears > 0);
  const hiredDrivers: (Driver & { salary: number; contractYears: number })[] = playerDriverContracts
    .map((c) => {
      const d = game.drivers.find((dr) => dr.id === c.driverId);
      return d ? { ...d, salary: c.annualSalary, contractYears: c.remainingYears } : null;
    })
    .filter((d): d is Driver & { salary: number; contractYears: number } => d !== null);

  const playerCharOvr = player.skills.driver * 5;
  const emptySlots = Math.max(0, 3 - hiredDrivers.length);

  return (
    <div
      className="garage-root"
      style={{
        "--garage-backdrop": `url(${garageBackdropUrl})`,
        "--car-backdrop": carModel ? `url(${CAR_BACKDROPS[carModel.carClass] ?? ""})` : "none",
      } as React.CSSProperties}
    >
      <div className="garage-app">
        <TopBar />

        {/* MAIN */}
        <div className="garage-main">
          {/* Stat strip — all 4 with action buttons */}
          <div className="stat-strip">
            <div className="stat-chip">
              <div className="chip-icon"><svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="3" /></svg></div>
              <div className="chip-body">
                <div className="chip-text">
                  <div className="chip-label">Spare Parts</div>
                  <div className="chip-value">{player.spareParts} <span className="unit">units</span></div>
                </div>
                <button className="btn-chip">Buy</button>
              </div>
            </div>
            <div className="stat-chip">
              <div className="chip-icon"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" /></svg></div>
              <div className="chip-body">
                <div className="chip-text">
                  <div className="chip-label">Tyre Sets</div>
                  <div className="chip-value">{player.tyreSets} <span className="unit">sets</span></div>
                </div>
                <button className="btn-chip">Buy</button>
              </div>
            </div>
            <div className="stat-chip">
              <div className="chip-icon"><svg viewBox="0 0 24 24"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z" /></svg></div>
              <div className="chip-body">
                <div className="chip-text">
                  <div className="chip-label">Crew Size</div>
                  <div className="chip-value">{player.crewSize} <span className="unit">/ 16</span></div>
                </div>
                <button className="btn-chip">Hire</button>
              </div>
            </div>
            <div className="stat-chip">
              <div className="chip-icon"><svg viewBox="0 0 24 24"><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" /></svg></div>
              <div className="chip-body">
                <div className="chip-text">
                  <div className="chip-label">Drivers</div>
                  <div className="chip-value">{1 + hiredDrivers.length} <span className="unit">/ 4</span></div>
                </div>
                <button className="btn-chip">Recruit</button>
              </div>
            </div>
          </div>

          {/* Card grid */}
          <div className="card-grid">
            {/* COL 1: CAR */}
            <div className="card car-card clickable" onClick={() => setScreen("carWorkshop")}>
              <div className="card-title">
                Cars <span className="badge">{cars.length} Owned</span>
                <span className="nav-hint">Car Workshop &rsaquo;</span>
              </div>
              {cars.length > 0 && (
                <>
                  <div className="car-tabs">
                    {cars.map((car, i) => {
                      const model = game.carModels.find((m) => m.id === car.modelId);
                      const isEntered = car.id === player.enteredCarId;
                      return (
                        <button key={car.id} className={`car-tab ${i === selectedCarIdx ? "active" : ""}`} onClick={() => setSelectedCarIdx(i)}>
                          {model?.name ?? car.modelId}
                          {isEntered && <span className="car-entered-badge">Entered</span>}
                        </button>
                      );
                    })}
                  </div>
                  {selectedCar && carModel && effectiveStats && (
                    <>
                      <div className="car-name">{carModel.name}</div>
                      <div className="car-meta">
                        <ClassBadge carClass={carModel.carClass} />
                        {" "}&middot; Age {selectedCar.age} &middot; {upgradeText}
                      </div>
                      <div className="stat-bars">
                        {DISPLAY_STATS.map(({ key, label }) => (
                          <StatBar key={key} label={label} value={effectiveStats[key]} />
                        ))}
                      </div>
                      <div className="car-condition">
                        <div className="condition-row">
                          <span className="condition-label">Condition</span>
                          <span className={`condition-pct ${selectedCar.condition < 50 ? "warn" : ""}`}>{selectedCar.condition}%</span>
                        </div>
                      </div>
                      <div className="car-actions">
                        <button className="btn-sm">Sell Car</button>
                      </div>
                    </>
                  )}
                </>
              )}
            </div>

            {/* COL 2 TOP: DRIVERS (compact) */}
            <div className="card drivers-card clickable">
              <div className="card-title">Drivers <span className="nav-hint">Team Roster &rsaquo;</span></div>
              <div className="driver-row">
                <div className="driver-avatar you"><PersonIcon /></div>
                <div className="driver-info">
                  <div className="driver-name-text">You</div>
                  <div className="driver-detail">No salary</div>
                </div>
                <div className="driver-overall"><span className="ovr-label">OVR</span>{playerCharOvr}</div>
              </div>
              {hiredDrivers.map((d) => {
                const stats = calculateDriverStats(d);
                const ovr = Math.round(totalDriverStats(stats) / 5);
                return (
                  <div className="driver-row" key={d.id}>
                    <div className="driver-avatar"><PersonIcon /></div>
                    <div className="driver-info">
                      <div className="driver-name-text">{d.name}</div>
                      <div className="driver-detail">Age {d.age} &middot; {d.contractYears}yr &middot; ${d.salary.toLocaleString()}/yr</div>
                    </div>
                    <div className="driver-overall"><span className="ovr-label">OVR</span>{ovr}</div>
                  </div>
                );
              })}
              {emptySlots > 0 && (
                <div className="empty-slots-summary">{emptySlots} empty slot{emptySlots > 1 ? "s" : ""}</div>
              )}
            </div>

            {/* COL 2 BOTTOM: SKILLS (compact) */}
            <div className="card skills-card">
              <div className="card-title">Player Skills</div>
              {(["driver", "engineer", "business"] as const).map((key) => (
                <div className="skill-item" key={key}>
                  <div className="skill-label-group">
                    <span className="skill-label">{key.charAt(0).toUpperCase() + key.slice(1)}</span>
                    <span className="info-dot">i<span className="tip">{SKILL_TOOLTIPS[key]}</span></span>
                  </div>
                  <span className="skill-val">{player.skills[key]} <span className="skill-max">/ 20</span></span>
                </div>
              ))}
            </div>

            {/* COL 3: NEWS */}
            <div className="card news-card clickable">
              <div className="card-title">News <span className="nav-hint">News Room &rsaquo;</span></div>
            </div>

          </div>

          {/* Sub-screen nav */}
          <div className="nav-cards">
            <div className="nav-card" onClick={() => setScreen("newCarDealer")}>
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="13" rx="2" /><line x1="12" y1="16" x2="12" y2="21" /><line x1="8" y1="21" x2="16" y2="21" /></svg></div>
              <span className="nav-card-label">New Car Dealer</span>
            </div>
            <div className="nav-card" onClick={() => setScreen("secondHandDealer")}>
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M9 11l3-3 3 3M12 8v8" /><rect x="3" y="3" width="18" height="18" rx="2" /></svg></div>
              <span className="nav-card-label">Second-Hand Dealer</span>
            </div>
            <div className="nav-card" onClick={() => setScreen("carWorkshop")}>
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" /></svg></div>
              <span className="nav-card-label">Car Workshop</span>
            </div>
            <div className="nav-card" onClick={() => setScreen("driverMarket")}>
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg></div>
              <span className="nav-card-label">Driver Market</span>
            </div>
            <div className="nav-card" onClick={() => setScreen("crewHiring")}>
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><line x1="19" y1="8" x2="19" y2="14" /><line x1="22" y1="11" x2="16" y2="11" /></svg></div>
              <span className="nav-card-label">Crew Hiring</span>
            </div>
          </div>

          {/* Start Race row */}
          <div className="race-row">
            <span className="race-year">Year <strong>{game.currentYear}</strong> &middot; The 24h Tarmac Gauntlet</span>
            <button className="btn-race" disabled={!player.enteredCarId}>Start Race</button>
          </div>
        </div>
      </div>
    </div>
  );
}
