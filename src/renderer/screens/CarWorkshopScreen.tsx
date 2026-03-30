import { useState } from "react";
import { useGameStore } from "../state/store";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { calculateSalePrice } from "../simulation/carMarket";
import { TopBar } from "./TopBar";
import type { CarInstance, CarModel, PlayerTeam, UpgradePackType } from "../types";
import backdropUrl from "../assets/workshop-backdrop.jpg";
import "./DealerShared.scss";

/** Placeholder: 1 spare part restores this much condition. */
const CONDITION_PER_PART = 5;

const UPGRADE_DESCS: Record<string, string> = {
  power: "Unlocks power and fuel efficiency potential",
  handling: "Unlocks handling and tyre durability potential",
  comfort: "Unlocks comfort potential",
};

export function CarWorkshopScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const enterCar = useGameStore((s) => s.enterCar);
  const installUpgrade = useGameStore((s) => s.installUpgrade);
  const repairCar = useGameStore((s) => s.repairCar);
  const sellCar = useGameStore((s) => s.sellCar);

  const [selectedCarId, setSelectedCarId] = useState<string | null>(null);
  const [repairParts, setRepairParts] = useState(0);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;
  const models = game.carModels;

  const cars = player.cars;
  const selectedCar = selectedCarId ? cars.find((c) => c.id === selectedCarId) ?? null : cars[0] ?? null;
  const selectedModel = selectedCar ? models.find((m) => m.id === selectedCar.modelId) : null;
  const effectiveStats = selectedCar && selectedModel ? calculateEffectiveStats(selectedCar, selectedModel) : null;

  const displaySalePrice = selectedCar && selectedModel
    ? Math.round(calculateSalePrice(selectedCar, selectedModel, player.skills.business) * 0.5)
    : 0;

  const conditionDeficit = selectedCar ? 100 - selectedCar.condition : 0;
  const maxRepairParts = Math.min(player.spareParts, Math.ceil(conditionDeficit / CONDITION_PER_PART));
  const repairGain = Math.min(conditionDeficit, repairParts * CONDITION_PER_PART);
  const resultCondition = selectedCar ? selectedCar.condition + repairGain : 0;

  const handleRepair = () => {
    if (!selectedCar || repairParts <= 0) return;
    repairCar(selectedCar.id, repairParts, repairGain);
    setRepairParts(0);
  };

  const handleInstall = (packType: UpgradePackType, cost: number) => {
    if (!selectedCar || player.budget < cost) return;
    installUpgrade(selectedCar.id, packType, cost);
  };

  const handleSell = () => {
    if (!selectedCar || !selectedModel) return;
    // 50% of the calculated sale price (which factors age, condition, upgrades, Business skill)
    const fullSalePrice = calculateSalePrice(selectedCar, selectedModel, player.skills.business);
    const salePrice = Math.round(fullSalePrice * 0.5);
    sellCar(selectedCar.id, salePrice);
    setSelectedCarId(null);
  };

  const handleEnter = () => {
    if (!selectedCar) return;
    enterCar(selectedCar.id);
  };

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />

        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">Car Workshop</span>
        </div>

        <div className="split">
          {/* Left: owned cars list */}
          <div className="car-list">
            <div className="list-header">
              <div className="list-title">Your Cars</div>
            </div>
            <div className="list-scroll">
              {cars.map((car) => {
                const model = models.find((m) => m.id === car.modelId);
                const isEntered = car.id === player.enteredCarId;
                const isSelected = (selectedCar?.id ?? cars[0]?.id) === car.id;
                return (
                  <div
                    key={car.id}
                    className={`car-list-item ${isSelected ? "selected" : ""}`}
                    onClick={() => { setSelectedCarId(car.id); setRepairParts(0); }}
                  >
                    <div className="car-item-info">
                      <div className="car-item-name">{model?.name ?? car.modelId}</div>
                      <div className="car-item-details">
                        {model && <span className={`class-badge ${model.carClass.toLowerCase()}`}>Class {model.carClass}</span>}{" "}
                        Age {car.age} &middot; {car.condition}% condition
                      </div>
                    </div>
                    {isEntered && <span className="entered-badge">Entered</span>}
                  </div>
                );
              })}
              {cars.length === 0 && (
                <div style={{ padding: 40, textAlign: "center", color: "#5a7a98", fontFamily: "'Oswald', sans-serif" }}>
                  No cars owned — visit a dealer!
                </div>
              )}
            </div>
          </div>

          {/* Right: detail panel */}
          <div className="detail-panel">
            {selectedCar && selectedModel && effectiveStats ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name">{selectedModel.name}</div>
                    <div className="detail-meta">
                      <span className={`class-badge ${selectedModel.carClass.toLowerCase()}`}>Class {selectedModel.carClass}</span>
                      {" "}&middot; Age {selectedCar.age} &middot;{" "}
                      {[selectedCar.installedUpgrades.power && "Power", selectedCar.installedUpgrades.handling && "Handling", selectedCar.installedUpgrades.comfort && "Comfort"].filter(Boolean).join(", ") || "No upgrades"}
                    </div>
                  </div>
                  <div>
                    {selectedCar.id === player.enteredCarId ? (
                      <span className="btn-entered">Entered</span>
                    ) : (
                      <button className="btn-enter" onClick={handleEnter}>Enter for Race</button>
                    )}
                  </div>
                </div>

                {/* Condition & repair */}
                <div className="condition-section">
                  <div>
                    <div className="condition-label">Condition</div>
                    <div className="condition-value">{selectedCar.condition}%</div>
                  </div>
                  {conditionDeficit > 0 && (
                    <div className="repair-controls">
                      <div className="repair-stepper">
                        <button className="stepper-btn" disabled={repairParts <= 0} onClick={() => setRepairParts((p) => Math.max(0, p - 1))}>−</button>
                        <span className="stepper-value">{repairParts}</span>
                        <button className="stepper-btn" disabled={repairParts >= maxRepairParts} onClick={() => setRepairParts((p) => Math.min(maxRepairParts, p + 1))}>+</button>
                      </div>
                      <div className="repair-result">
                        {repairParts > 0 ? (
                          <>{repairParts} parts → <strong>{resultCondition}%</strong></>
                        ) : (
                          <span>Use spare parts to repair</span>
                        )}
                      </div>
                      <button className="btn-repair" disabled={repairParts <= 0} onClick={handleRepair}>Repair</button>
                    </div>
                  )}
                </div>

                {/* Stats with potential */}
                <div className="stats-section">
                  <div className="stats-section-title">Performance</div>
                  {(["power", "handling", "fuelEfficiency", "tyreDurability", "comfort", "reliability", "fuelCapacity"] as const).map((stat) => {
                    const current = effectiveStats[stat];
                    const potential = selectedModel.potentialStats[stat];
                    const hasPotential = potential > selectedModel.baseStats[stat];
                    return (
                      <div className="stat-row" key={stat}>
                        <span className="stat-name">{stat.replace(/([A-Z])/g, " $1")}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${current}%` }} />
                          {hasPotential && (
                            <div className="stat-bar-potential" style={{ left: `${current}%`, width: `${potential - current}%` }} />
                          )}
                        </div>
                        <span className="stat-value">{Math.round(current)}</span>
                        {hasPotential && <span className="stat-potential">→ {Math.round(potential)}</span>}
                      </div>
                    );
                  })}
                </div>

                {/* Upgrade packs */}
                {selectedModel.upgradePacks.length > 0 && (
                  <div className="upgrades-section">
                    <div className="stats-section-title">Upgrade Packs</div>
                    {selectedModel.upgradePacks.map((pack) => {
                      const isInstalled = selectedCar.installedUpgrades[pack.type];
                      return (
                        <div key={pack.type} className={`upgrade-pack ${isInstalled ? "installed" : ""}`}>
                          <div className="upgrade-info">
                            <div className="upgrade-name">{pack.type.charAt(0).toUpperCase() + pack.type.slice(1)} Pack</div>
                            <div className="upgrade-desc">{UPGRADE_DESCS[pack.type]}</div>
                            {!isInstalled && (
                              <div className="upgrade-stats">
                                {pack.type === "power" && `Power → ${selectedModel.potentialStats.power} · FE → ${selectedModel.potentialStats.fuelEfficiency}`}
                                {pack.type === "handling" && `Handling → ${selectedModel.potentialStats.handling} · TD → ${selectedModel.potentialStats.tyreDurability}`}
                                {pack.type === "comfort" && `Comfort → ${selectedModel.potentialStats.comfort}`}
                              </div>
                            )}
                          </div>
                          {isInstalled ? (
                            <span className="installed-tag">Installed</span>
                          ) : (
                            <div className="upgrade-action">
                              <span className="upgrade-price">${pack.cost.toLocaleString()}</span>
                              <button className="btn-install" disabled={player.budget < pack.cost} onClick={() => handleInstall(pack.type, pack.cost)}>Install</button>
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}

                {/* Sell */}
                <div className="buy-row">
                  <button className="btn-buy" style={{ background: "transparent", border: "1px solid #2a4262", color: "#8a9cb4" }} onClick={handleSell}>
                    Sell — ${displaySalePrice.toLocaleString()}
                  </button>
                </div>
              </>
            ) : (
              <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", color: "#5a7a98", fontFamily: "'Oswald', sans-serif", fontSize: 18, letterSpacing: 2 }}>
                {cars.length === 0 ? "NO CARS — VISIT A DEALER" : "SELECT A CAR"}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
