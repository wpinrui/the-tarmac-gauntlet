import { useState } from "react";
import { useGameStore } from "../state/store";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { calculateDriverStats, totalDriverStats } from "../simulation/driverLifecycle";
import type { PlayerTeam, CarInstance, CarModel, Driver } from "../types";
import garageBackdropUrl from "../assets/garage-backdrop.jpg";
import carBackdropUrl from "../assets/car-backdrop.jpg";
import { SKILL_TOOLTIPS } from "../shared/skillData";
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


const PRE_SEASON_NEWS = [
  { time: "Pre-season \u00b7 Today", headline: "100 Teams Line Up for Inaugural 24h Tarmac Gauntlet", snippet: "From factory hypercars to junkyard shitboxes \u2014 the full field is confirmed for the first-ever running." },
  { time: "Pre-season \u00b7 2 days ago", headline: "Prestige Rankings: Apex Motorsport Tops Early Grid Predictions", snippet: "With a $2.8M hypercar and three elite drivers, Apex enters as the clear favourite." },
  { time: "Pre-season \u00b7 4 days ago", headline: "Rookie Watch: 15 New Faces Join the Driver Pool", snippet: "A fresh crop of 18-year-old talent enters the market. Cheap contracts, unproven pace." },
  { time: "Pre-season \u00b7 1 week ago", headline: "", snippet: "" },
  { time: "Pre-season \u00b7 2 weeks ago", headline: "Circuit Revealed: Organisers Unveil the Gauntlet Layout", snippet: "Long straights, tight hairpins, and a fearsome sweeping complex. 48 laps in 24 hours." },
];

export function GarageScreen() {
  const game = useGameStore((s) => s.game);
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

  // Personalise news item 4
  const news = PRE_SEASON_NEWS.map((n, i) =>
    i === 3
      ? { ...n, headline: `${player.name} Registers for the Gauntlet`, snippet: `A one-car outfit with no crew and no co-drivers. The ${carModel?.name ?? "starter car"} has seen better days.` }
      : n,
  );

  return (
    <div
      className="garage-root"
      style={{ "--garage-backdrop": `url(${garageBackdropUrl})`, "--car-backdrop": `url(${carBackdropUrl})` } as React.CSSProperties}
    >
      <div className="garage-app">
        {/* TAB BAR */}
        <div className="tab-bar">
          <div className="team-identity">
            <div className="team-logo-icon">
              <svg viewBox="0 0 48 48"><path d="M24 4L40 12v14c0 12-16 18-16 18S8 38 8 26V12z" /></svg>
            </div>
            <span className="team-name-label">{player.name}</span>
          </div>
          <div className="tab active">Garage</div>
          <div className="tab">Finances</div>
          <div className="tab">Race History</div>
          <div className="tab">Standings</div>
          <div className="tab">Scouting Report</div>
          <div className="tab-spacer" />
          <div className="tab-budget">
            <span className="budget-label">Budget</span>
            <span className="budget-value">${player.budget.toLocaleString()}</span>
          </div>
        </div>

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
            <div className="card car-card clickable">
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
                      <div className="car-meta">Age {selectedCar.age} &middot; {upgradeText}</div>
                      <div className="stat-bars">
                        <StatBar label="Power" value={effectiveStats.power} />
                        <StatBar label="Handling" value={effectiveStats.handling} />
                        <StatBar label="Fuel Efficiency" value={effectiveStats.fuelEfficiency} />
                        <StatBar label="Tyre Durability" value={effectiveStats.tyreDurability} />
                        <StatBar label="Comfort" value={effectiveStats.comfort} />
                        <StatBar label="Reliability" value={effectiveStats.reliability} />
                        <StatBar label="Fuel Capacity" value={effectiveStats.fuelCapacity} />
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

            {/* COL 3: NEWS FEED */}
            <div className="card news-card">
              <div className="card-title">News</div>
              {news.map((item, i) => (
                <div className="news-item" key={i}>
                  <div className="news-time">{item.time}</div>
                  <div className="news-headline">{item.headline}</div>
                  <div className="news-snippet">{item.snippet}</div>
                </div>
              ))}
            </div>
          </div>

          {/* Sub-screen nav */}
          <div className="nav-cards">
            <div className="nav-card">
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="13" rx="2" /><line x1="12" y1="16" x2="12" y2="21" /><line x1="8" y1="21" x2="16" y2="21" /></svg></div>
              <span className="nav-card-label">New Car Dealer</span>
            </div>
            <div className="nav-card">
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M9 11l3-3 3 3M12 8v8" /><rect x="3" y="3" width="18" height="18" rx="2" /></svg></div>
              <span className="nav-card-label">Second-Hand Dealer</span>
            </div>
            <div className="nav-card">
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z" /></svg></div>
              <span className="nav-card-label">Car Workshop</span>
            </div>
            <div className="nav-card">
              <div className="nav-card-icon"><svg viewBox="0 0 24 24"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg></div>
              <span className="nav-card-label">Driver Market</span>
            </div>
            <div className="nav-card">
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
