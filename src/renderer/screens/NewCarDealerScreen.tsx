import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { TopBar } from "./TopBar";
import { UPGRADE_DESCS, DISPLAY_STATS, nextCarId } from "../shared/dealerData";
import { ClassBadge } from "../shared/ClassBadge";
import type { CarModel, CarClass, CarInstance, PlayerTeam } from "../types";
import backdropUrl from "../assets/newdealer-backdrop.jpg";
import "./DealerShared.scss";

const CLASS_ORDER: CarClass[] = ["F", "E", "D", "C", "B", "A"];

export function NewCarDealerScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const buyCar = useGameStore((s) => s.buyCar);

  const [classFilter, setClassFilter] = useState<CarClass | "all">("all");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const models = game.carModels.filter((m) => m.carClass !== "F1");
  const filtered = classFilter === "all" ? models : models.filter((m) => m.carClass === classFilter);

  const grouped = useMemo(() => {
    const groups: { cls: CarClass; cars: CarModel[] }[] = [];
    for (const cls of CLASS_ORDER) {
      const cars = filtered.filter((m) => m.carClass === cls).sort((a, b) => a.price - b.price);
      if (cars.length > 0) groups.push({ cls, cars });
    }
    return groups;
  }, [filtered]);

  const selected = selectedId ? models.find((m) => m.id === selectedId) ?? null : null;

  const handleBuy = () => {
    if (!selected || player.budget < selected.price) return;
    const newCar: CarInstance = {
      id: nextCarId(),
      modelId: selected.id,
      age: 0,
      condition: 100,
      installedUpgrades: { power: false, handling: false, comfort: false },
    };
    buyCar(newCar, selected.price, selected.name);
    setScreen("garage");
  };

  // Stat comparison
  const enteredCar = player.cars.find((c) => c.id === player.enteredCarId);
  const enteredModel = enteredCar ? models.find((m) => m.id === enteredCar.modelId) : undefined;
  const enteredStats = enteredCar && enteredModel ? calculateEffectiveStats(enteredCar, enteredModel) : null;

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />

        {/* Breadcrumb */}
        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">New Car Dealer</span>
        </div>

        {/* Split view */}
        <div className="split">
          {/* Left: car list */}
          <div className="car-list">
            <div className="list-header">
              <div className="list-title">Cars</div>
              <div className="class-filter">
                <button className={`class-btn ${classFilter === "all" ? "active" : ""}`} onClick={() => setClassFilter("all")}>All</button>
                {CLASS_ORDER.map((cls) => (
                  <button key={cls} className={`class-btn ${classFilter === cls ? "active" : ""}`} onClick={() => setClassFilter(cls)}>
                    Class {cls}
                  </button>
                ))}
              </div>
            </div>
            <div className="list-scroll">
              {grouped.map(({ cls, cars }) => (
                <div className="car-list-group" key={cls}>
                  {cars.map((m) => (
                    <div
                      key={m.id}
                      className={`car-list-item ${selectedId === m.id ? "selected" : ""}`}
                      onClick={() => setSelectedId(m.id)}
                    >
                      <div className="car-item-info">
                        <div className="car-item-name">{m.name}</div>
                        <ClassBadge carClass={m.carClass} />
                      </div>
                      <div className="car-item-price">${m.price.toLocaleString()}</div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </div>

          {/* Right: detail panel */}
          <div className="detail-panel">
            {selected ? (
              <>
                <div className="detail-header">
                  <div>
                    <div className="detail-name">{selected.name}</div>
                    <div className="detail-meta">
                      <ClassBadge carClass={selected.carClass} />
                      {" "}&middot; New &middot; Age 0
                    </div>
                  </div>
                  <div className="detail-price-block">
                    <div className="detail-price-label">MSRP</div>
                    <div className="detail-price">${selected.price.toLocaleString()}</div>
                  </div>
                </div>

                {/* Stats with delta comparison */}
                <div className="stats-section">
                  <div className="stats-section-title">
                    Performance Stats
                    {enteredModel && (
                      <span style={{ fontWeight: 400, color: "#4a6a88" }}> vs your {enteredModel.name}</span>
                    )}
                  </div>
                  {DISPLAY_STATS.map(({ key, label }) => {
                    const val = selected.baseStats[key];
                    const delta = enteredStats ? Math.round(val - enteredStats[key]) : null;
                    return (
                      <div className="stat-row" key={key}>
                        <span className="stat-name">{label}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${val}%` }} />
                        </div>
                        <span className="stat-value">{Math.round(val)}</span>
                        {delta !== null && (
                          <span className={`stat-delta ${delta > 0 ? "up" : delta < 0 ? "down" : "neutral"}`}>
                            {delta > 0 ? `+${delta}` : delta < 0 ? `${delta}` : "\u2014"}
                          </span>
                        )}
                      </div>
                    );
                  })}
                </div>

                {/* Pit stop estimate */}
                <div className="pit-estimate">
                  <span className="pit-label">Pit Stop Time</span>
                  <span className="pit-value">
                    {selected.baseStats.pitStopTime}<span className="pit-unit">s base</span>
                  </span>
                </div>

                {/* Upgrade packs */}
                {selected.upgradePacks.length > 0 && (
                  <div className="upgrades-section">
                    <div className="stats-section-title">Available Upgrades</div>
                    {selected.upgradePacks.map((pack) => (
                      <div className="upgrade-pack" key={pack.type}>
                        <div className="upgrade-info">
                          <div className="upgrade-name">{pack.type.charAt(0).toUpperCase() + pack.type.slice(1)} Pack</div>
                          <div className="upgrade-desc">{UPGRADE_DESCS[pack.type]}</div>
                          <div className="upgrade-stats">
                            {pack.type === "power" && `Power ${selected.baseStats.power} → ${selected.potentialStats.power} · FE ${selected.baseStats.fuelEfficiency} → ${selected.potentialStats.fuelEfficiency}`}
                            {pack.type === "handling" && `Handling ${selected.baseStats.handling} → ${selected.potentialStats.handling} · TD ${selected.baseStats.tyreDurability} → ${selected.potentialStats.tyreDurability}`}
                            {pack.type === "comfort" && `Comfort ${selected.baseStats.comfort} → ${selected.potentialStats.comfort}`}
                          </div>
                        </div>
                        <div className="upgrade-price">${pack.cost.toLocaleString()}</div>
                      </div>
                    ))}
                  </div>
                )}

                {/* Buy */}
                <div className="buy-row">
                  <button className="btn-buy" disabled={player.budget < selected.price} onClick={handleBuy}>
                    Buy — ${selected.price.toLocaleString()}
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
