import { useState } from "react";
import { useGameStore } from "../state/store";
import type { CarInstance, PlayerTeam, UsedCarListing } from "../types";
import backdropUrl from "../assets/secondhand-backdrop.jpg";
import "./DealerShared.scss";

let carIdCounter = Date.now() + 100_000;
function nextCarId(): string {
  return `car-${++carIdCounter}`;
}

export function SecondHandDealerScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const buyCar = useGameStore((s) => s.buyCar);

  const [selectedId, setSelectedId] = useState<string | null>(null);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;
  const models = game.carModels;

  // F1 car only appears in used market if:
  // 1. Player has won the event before (position 1 in any past race)
  // 2. Player can afford it
  // 3. The inventory happened to include one (random ~3% from model pool, treated as the 20% gate)
  const hasWon = game.raceHistory.some((r) =>
    r.results.some((res) => res.teamId === player.id && res.position === 1),
  );
  const f1Model = models.find((m) => m.carClass === "F1");
  const canAffordF1 = f1Model ? player.budget >= f1Model.price : false;
  const showF1 = hasWon && canAffordF1;

  const listings = game.carMarket.usedListings
    .filter((l) => {
      const model = models.find((m) => m.id === l.modelId);
      if (model?.carClass === "F1" && !showF1) return false;
      return true;
    })
    .sort((a, b) => a.price - b.price);

  const selected = selectedId ? listings.find((l) => l.id === selectedId) ?? null : null;
  const selectedModel = selected ? models.find((m) => m.id === selected.modelId) : null;

  const handleBuy = () => {
    if (!selected || !selectedModel) return;
    if (player.budget < selected.price) return;
    const newCar: CarInstance = {
      id: nextCarId(),
      modelId: selected.modelId,
      age: selected.age,
      condition: selected.condition,
      installedUpgrades: selected.installedUpgrades,
    };
    buyCar(newCar, selected.price);
    setScreen("garage");
  };

  const upgradeCount = (u: UsedCarListing["installedUpgrades"]) =>
    [u.power, u.handling, u.comfort].filter(Boolean).length;

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        {/* Tab bar */}
        <div className="tab-bar">
          <div className="team-identity">
            <div className="team-logo">
              <svg viewBox="0 0 48 48"><path d="M24 4L40 12v14c0 12-16 18-16 18S8 38 8 26V12z" /></svg>
            </div>
            <span className="team-name">{player.name}</span>
          </div>
          <div className="tab active" onClick={() => setScreen("garage")}>Garage</div>
          <div className="tab">Finances</div>
          <div className="tab">Race History</div>
          <div className="tab">Standings</div>
          <div className="tab">Scouting Report</div>
          <div className="tab-spacer" />
          <div className="tab-resource">
            <i className="fa-solid fa-wrench res-icon" />
            <span className="res-value">{player.spareParts}</span>
            <div className="res-tooltip">
              <div className="res-tooltip-title">Spare Parts</div>
              <div className="res-tooltip-desc">Fix mechanical issues during races and repair car condition between races.</div>
            </div>
          </div>
          <div className="tab-resource money">
            <i className="fa-solid fa-sack-dollar res-icon" />
            <span className="res-value">${player.budget.toLocaleString()}</span>
            <div className="res-tooltip">
              <div className="res-tooltip-title">Money</div>
              <div className="res-tooltip-desc">Your team&apos;s funds. Buy cars, upgrades, spare parts, tyres, and hire drivers and crew.</div>
            </div>
          </div>
        </div>

        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">Second-Hand Dealer</span>
        </div>

        <div className="split">
          {/* Left: inventory list */}
          <div className="car-list">
            <div className="list-header">
              <div className="list-title">Used Inventory</div>
              <div className="list-subtitle">Rotating stock — changes every year. Bargains may appear.</div>
            </div>
            <div className="list-scroll">
              {listings.map((l) => {
                const model = models.find((m) => m.id === l.modelId);
                return (
                  <div
                    key={l.id}
                    className={`car-list-item ${selectedId === l.id ? "selected" : ""}`}
                    onClick={() => setSelectedId(l.id)}
                  >
                    <div className="car-item-info">
                      <div className="car-item-name">{model?.name ?? l.modelId}</div>
                      <div className="car-item-details">
                        {model && <span className={`class-badge ${model.carClass.toLowerCase()}`}>Class {model.carClass}</span>}{" "}
                        Age {l.age} &middot; {l.condition}% &middot; {upgradeCount(l.installedUpgrades)} upgrades
                      </div>
                    </div>
                    <div className="car-item-price">${l.price.toLocaleString()}</div>
                  </div>
                );
              })}
              {listings.length === 0 && (
                <div style={{ padding: 40, textAlign: "center", color: "#5a7a98", fontFamily: "'Oswald', sans-serif" }}>
                  No used cars available this year
                </div>
              )}
            </div>
          </div>

          {/* Right: detail panel */}
          <div className="detail-panel">
            {selected && selectedModel ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name">
                      <span className={`class-badge ${selectedModel.carClass.toLowerCase()}`}>{selectedModel.carClass}</span>
                      {selectedModel.name}
                    </div>
                    <div className="detail-meta">Used &middot; Age {selected.age}</div>
                  </div>
                  <div className="detail-price-block">
                    <div className="detail-price">${selected.price.toLocaleString()}</div>
                    <div className="detail-msrp">MSRP ${selectedModel.price.toLocaleString()}</div>
                  </div>
                </div>

                {/* Info chips */}
                <div className="info-chips">
                  <div className="info-chip">
                    <span className="info-chip-label">Age</span>
                    <span className="info-chip-value">{selected.age} yr</span>
                  </div>
                  <div className="info-chip">
                    <span className="info-chip-label">Condition</span>
                    <span className="info-chip-value">{selected.condition}%</span>
                  </div>
                  <div className="info-chip">
                    <span className="info-chip-label">Upgrades</span>
                    <span className="info-chip-value">{upgradeCount(selected.installedUpgrades)} / {selectedModel.upgradePacks.length}</span>
                  </div>
                </div>

                {/* Stats */}
                <div className="stats-section">
                  <div className="stats-section-title">Performance</div>
                  {(["power", "handling", "fuelEfficiency", "tyreDurability", "comfort", "reliability", "fuelCapacity"] as const).map((stat) => {
                    const val = selectedModel.baseStats[stat];
                    return (
                      <div className="stat-row" key={stat}>
                        <span className="stat-name">{stat.replace(/([A-Z])/g, " $1")}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${val}%` }} />
                        </div>
                        <span className="stat-value">{Math.round(val)}</span>
                      </div>
                    );
                  })}
                </div>

                {/* Upgrade state */}
                {selectedModel.upgradePacks.length > 0 && (
                  <div className="upgrades-section">
                    <div className="stats-section-title">Upgrades</div>
                    {selectedModel.upgradePacks.map((pack) => {
                      const isInstalled = selected.installedUpgrades[pack.type];
                      return (
                        <div key={pack.type} className={`upgrade-pack ${isInstalled ? "installed" : ""}`}>
                          <div className="upgrade-info">
                            <div className="upgrade-name">{pack.type.charAt(0).toUpperCase() + pack.type.slice(1)} Pack</div>
                          </div>
                          <span className={isInstalled ? "installed-tag" : ""} style={isInstalled ? {} : { color: "#5a7a98", fontFamily: "'Oswald', sans-serif", fontSize: 14, letterSpacing: 1.5, textTransform: "uppercase" as const }}>
                            {isInstalled ? "Installed" : "Available after purchase"}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                )}

                {/* Buy */}
                <div className="buy-row">
                  <button
                    className="btn-buy"
                    disabled={player.budget < selected.price}
                    onClick={handleBuy}
                  >
                    Buy — ${selected.price.toLocaleString()}
                  </button>
                </div>
              </>
            ) : (
              <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", color: "#5a7a98", fontFamily: "'Oswald', sans-serif", fontSize: 18, letterSpacing: 2 }}>
                SELECT A CAR TO VIEW DETAILS
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
