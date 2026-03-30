import { useState } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { DISPLAY_STATS, SHITBOX_MODEL_ID, nextCarId } from "../shared/dealerData";
import { ClassBadge } from "../shared/ClassBadge";
import type { CarInstance, PlayerTeam, UsedCarListing } from "../types";
import backdropUrl from "../assets/secondhand-backdrop.jpg";
import "./DealerShared.scss";

export function SecondHandDealerScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const buyCar = useGameStore((s) => s.buyCar);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [compareMode, setCompareMode] = useState<"new" | "current">("new");

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
  const selectedEffective = selected && selectedModel
    ? calculateEffectiveStats(
        { id: "", modelId: selected.modelId, age: selected.age, condition: selected.condition, installedUpgrades: selected.installedUpgrades },
        selectedModel,
      )
    : null;

  // Player's current entered car for comparison
  const enteredCar = player.cars.find((c) => c.id === player.enteredCarId);
  const enteredModel = enteredCar ? models.find((m) => m.id === enteredCar.modelId) : undefined;
  const enteredEffective = enteredCar && enteredModel ? calculateEffectiveStats(enteredCar, enteredModel) : null;

  const isShitbox = (listing: UsedCarListing) => listing.modelId === SHITBOX_MODEL_ID;
  const canBeg = (listing: UsedCarListing) => isShitbox(listing) && player.cars.length === 0;

  const handleBuy = () => {
    if (!selected || !selectedModel) return;
    const beg = canBeg(selected);
    if (!beg && player.budget < selected.price) return;
    const cost = beg ? Math.min(player.budget, selected.price) : selected.price;
    const newCar: CarInstance = {
      id: nextCarId(),
      modelId: selected.modelId,
      age: selected.age,
      condition: selected.condition,
      installedUpgrades: selected.installedUpgrades,
    };
    buyCar(newCar, cost);
    setScreen("garage");
  };

  const upgradeCount = (u: UsedCarListing["installedUpgrades"]) =>
    [u.power, u.handling, u.comfort].filter(Boolean).length;

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />

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
                        {model && <ClassBadge carClass={model.carClass} />}{" "}
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
            {selected && selectedModel && selectedEffective ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name">{selectedModel.name}</div>
                    <div className="detail-meta">
                      <ClassBadge carClass={selectedModel.carClass} />
                      {" "}&middot; Used &middot; Age {selected.age}
                    </div>
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
                  <div className="stats-section-title">
                    Performance{" "}
                    <span
                      onClick={() => setCompareMode("new")}
                      style={{ fontWeight: 400, color: compareMode === "new" ? "#e8ecf4" : "#4a6a88", cursor: "pointer", transition: "color .15s" }}
                    >
                      vs new
                    </span>
                    {enteredModel && (
                      <>
                        {" "}&middot;{" "}
                        <span
                          onClick={() => setCompareMode("current")}
                          style={{ fontWeight: 400, color: compareMode === "current" ? "#e8ecf4" : "#4a6a88", cursor: "pointer", transition: "color .15s" }}
                        >
                          vs your {enteredModel.name}
                        </span>
                      </>
                    )}
                  </div>
                  {DISPLAY_STATS.map(({ key, label }) => {
                    const val = selectedEffective[key];
                    const ref = compareMode === "new"
                      ? selectedModel.baseStats[key]
                      : enteredEffective ? enteredEffective[key] : val;
                    const delta = Math.round(val - ref);
                    return (
                      <div className="stat-row" key={key}>
                        <span className="stat-name">{label}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${val}%` }} />
                        </div>
                        <span className="stat-value">{Math.round(val)}</span>
                        <span className={`stat-delta ${delta < 0 ? "down" : delta > 0 ? "up" : "neutral"}`}>
                          {delta < 0 ? `${delta}` : delta > 0 ? `+${delta}` : "\u2014"}
                        </span>
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
                    disabled={!canBeg(selected) && player.budget < selected.price}
                    onClick={handleBuy}
                  >
                    {canBeg(selected) && player.budget < selected.price
                      ? `Beg — $${player.budget.toLocaleString()}`
                      : `Buy — $${selected.price.toLocaleString()}`}
                  </button>
                </div>
              </>
            ) : (
              <div className="detail-empty">SELECT A CAR TO VIEW DETAILS</div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
