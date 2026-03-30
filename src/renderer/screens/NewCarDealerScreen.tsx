import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import type { CarModel, CarClass, CarInstance, PlayerTeam } from "../types";
import backdropUrl from "../assets/newdealer-backdrop.jpg";
import "./DealerShared.scss";

const CLASS_ORDER: CarClass[] = ["F", "E", "D", "C", "B", "A", "F1"];

const UPGRADE_DESCS: Record<string, string> = {
  power: "Unlocks power and fuel efficiency potential",
  handling: "Unlocks handling and tyre durability potential",
  comfort: "Unlocks comfort potential",
};

let carIdCounter = Date.now();
function nextCarId(): string {
  return `car-${++carIdCounter}`;
}

export function NewCarDealerScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const buyCar = useGameStore((s) => s.buyCar);

  const [classFilter, setClassFilter] = useState<CarClass | "all">("all");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const models = game.carModels;
  const filtered = classFilter === "all" ? models : models.filter((m) => m.carClass === classFilter);

  // Group by class
  const grouped = useMemo(() => {
    const groups: { cls: CarClass; cars: CarModel[] }[] = [];
    for (const cls of CLASS_ORDER) {
      const cars = filtered.filter((m) => m.carClass === cls);
      if (cars.length > 0) groups.push({ cls, cars });
    }
    return groups;
  }, [filtered]);

  const selected = selectedId ? models.find((m) => m.id === selectedId) ?? null : null;

  // Plot armour: if player has no car and can't afford the cheapest, cheapest is still purchasable
  const cheapest = models.reduce((a, b) => (a.price < b.price ? a : b));
  const plotArmourActive = player.cars.length === 0 && player.budget < cheapest.price;

  const canAfford = (price: number) => {
    if (plotArmourActive) return true; // plot armour
    return player.budget >= price;
  };

  const effectiveCost = (price: number) => {
    if (plotArmourActive) return Math.min(player.budget, price);
    return price;
  };

  const handleBuy = () => {
    if (!selected) return;
    const cost = effectiveCost(selected.price);
    const newCar: CarInstance = {
      id: nextCarId(),
      modelId: selected.id,
      age: 0,
      condition: 100,
      installedUpgrades: { power: false, handling: false, comfort: false },
    };
    buyCar(newCar, cost);
    setScreen("garage");
  };

  // Stat comparison with player's entered car
  const enteredCar = player.cars.find((c) => c.id === player.enteredCarId);
  const enteredModel = enteredCar ? models.find((m) => m.id === enteredCar.modelId) : undefined;
  const enteredStats = enteredCar && enteredModel ? calculateEffectiveStats(enteredCar, enteredModel) : null;

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
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
              <div className="list-title">New Cars</div>
              <div className="class-filter">
                <button className={`class-btn ${classFilter === "all" ? "active" : ""}`} onClick={() => setClassFilter("all")}>All</button>
                {CLASS_ORDER.map((cls) => (
                  <button key={cls} className={`class-btn ${classFilter === cls ? "active" : ""}`} onClick={() => setClassFilter(cls)}>
                    {cls === "F1" ? "F1" : `Class ${cls}`}
                  </button>
                ))}
              </div>
            </div>
            <div className="list-scroll">
              {grouped.map(({ cls, cars }) => (
                <div className="car-list-group" key={cls}>
                  <div className="group-label">Class {cls}</div>
                  {cars.map((m) => (
                    <div
                      key={m.id}
                      className={`car-list-item ${selectedId === m.id ? "selected" : ""}`}
                      onClick={() => setSelectedId(m.id)}
                    >
                      <div className="car-item-info">
                        <div className="car-item-name">
                          <span className={`class-badge ${m.carClass.toLowerCase()}`}>{m.carClass}</span>
                          {m.name}
                        </div>
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
                    <div className="detail-name">
                      <span className={`class-badge ${selected.carClass.toLowerCase()}`}>{selected.carClass}</span>
                      {selected.name}
                    </div>
                    <div className="detail-meta">New &middot; Age 0 &middot; 100% condition</div>
                  </div>
                  <div className="detail-price-block">
                    <div className="detail-price-label">MSRP</div>
                    <div className="detail-price">${selected.price.toLocaleString()}</div>
                  </div>
                </div>

                {/* Stats */}
                <div className="stats-section">
                  <div className="stats-section-title">Performance</div>
                  {(["power", "handling", "fuelEfficiency", "tyreDurability", "comfort", "reliability", "fuelCapacity"] as const).map((stat) => {
                    const val = selected.baseStats[stat];
                    const delta = enteredStats ? val - enteredStats[stat] : null;
                    return (
                      <div className="stat-row" key={stat}>
                        <span className="stat-name">{stat.replace(/([A-Z])/g, " $1")}</span>
                        <div className="stat-bar-track">
                          <div className="stat-bar-fill" style={{ width: `${val}%` }} />
                        </div>
                        <span className="stat-value">{Math.round(val)}</span>
                        {delta !== null && (
                          <span className={`stat-delta ${delta > 0 ? "up" : delta < 0 ? "down" : "neutral"}`}>
                            {delta > 0 ? `+${Math.round(delta)}` : delta < 0 ? `${Math.round(delta)}` : "—"}
                          </span>
                        )}
                      </div>
                    );
                  })}
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
                  {plotArmourActive && selected.id === cheapest.id && (
                    <span style={{ color: "#e17055", fontFamily: "'Oswald', sans-serif", fontSize: 14, letterSpacing: 1 }}>
                      PLOT ARMOUR — you need a car to race!
                    </span>
                  )}
                  <button
                    className="btn-buy"
                    disabled={!canAfford(selected.price)}
                    onClick={handleBuy}
                  >
                    Buy — ${effectiveCost(selected.price).toLocaleString()}
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
