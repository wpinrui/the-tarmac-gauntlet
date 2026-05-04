import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { calculateDriverStats, totalDriverStats, calculateAnnualSalary, calculateContractSalary, calculateBuyoutCost } from "../simulation/driverLifecycle";
import { DRIVER_STAT_KEYS, DRIVER_STAT_LABELS } from "../shared/driverData";
import type { PlayerTeam, ContractLength } from "../types";
import backdropUrl from "../assets/driver-market-backdrop.jpg";
import "./DealerShared.scss";
import "./DriverMarket.scss";

type FilterMode = "all" | "free" | "contracted";
type SortKey = "overall" | "age" | "salary" | "pace" | "safety";


export function DriverMarketScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const hireDriver = useGameStore((s) => s.hireDriver);
  const buyoutDriver = useGameStore((s) => s.buyoutDriver);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [filter, setFilter] = useState<FilterMode>("all");
  const [sortKey, setSortKey] = useState<SortKey>("overall");
  const [selectedLength, setSelectedLength] = useState<ContractLength>(1);

  // Build driver list with computed values (hooks must run unconditionally)
  const driverData = useMemo(() => {
    if (!game) return [];
    return game.drivers.map((d) => {
      const stats = calculateDriverStats(d);
      const ovr = Math.round(totalDriverStats(stats) / 5);
      const salary = calculateAnnualSalary(stats);
      const contract = game.contracts.find((c) => c.driverId === d.id && c.remainingYears > 0);
      const isFree = !contract;
      const contractTeam = contract ? game.teams.find((t) => t.id === contract.teamId)?.name ?? contract.teamId : null;
      return { driver: d, stats, ovr, salary, contract, isFree, contractTeam };
    });
  }, [game]);

  const filtered = useMemo(() => {
    switch (filter) {
      case "free": return driverData.filter((d) => d.isFree);
      case "contracted": return driverData.filter((d) => !d.isFree);
      default: return driverData;
    }
  }, [driverData, filter]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    switch (sortKey) {
      case "overall": arr.sort((a, b) => b.ovr - a.ovr); break;
      case "age": arr.sort((a, b) => a.driver.age - b.driver.age); break;
      case "salary": arr.sort((a, b) => b.salary - a.salary); break;
      case "pace": arr.sort((a, b) => b.stats.pace - a.stats.pace); break;
      case "safety": arr.sort((a, b) => b.stats.safety - a.stats.safety); break;
    }
    return arr;
  }, [filtered, sortKey]);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;
  const playerDriverCount = 1 + game.contracts.filter((c) => c.teamId === "player" && c.remainingYears > 0).length;

  const selected = selectedId ? driverData.find((d) => d.driver.id === selectedId) ?? null : null;

  // Contract pricing for selected driver
  const contractPrices = selected ? ([1, 2, 3] as ContractLength[]).map((len) => ({
    length: len,
    annual: calculateContractSalary(selected.salary, len, player.skills.business),
    total: calculateContractSalary(selected.salary, len, player.skills.business) * len,
    discount: len === 1 ? 0 : len === 2 ? 10 : 20,
  })) : [];

  const selectedContract = contractPrices.find((c) => c.length === selectedLength);
  const buyoutCost = selected?.contract ? calculateBuyoutCost(selected.contract) : 0;

  const handleHire = () => {
    if (!selected || !selectedContract || !selected.isFree) return;
    if (playerDriverCount >= 4) return;
    if (player.budget < selectedContract.annual) return;
    hireDriver(selected.driver.id, selected.driver.name, selectedLength, selectedContract.annual);
  };

  const handleBuyout = () => {
    if (!selected || !selected.contract) return;
    if (player.budget < buyoutCost) return;
    buyoutDriver(selected.driver.id, selected.driver.name, buyoutCost);
  };

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />
        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">Driver Market</span>
          <span className="breadcrumb-counter">Your Drivers: {playerDriverCount} / 4</span>
        </div>

        <div className="split">
          {/* Left: driver list */}
          <div className="car-list">
            <div className="list-header">
              <div className="list-title">All Drivers</div>
              <div className="filter-row">
                {(["all", "free", "contracted"] as FilterMode[]).map((f) => (
                  <button key={f} className={`class-btn ${filter === f ? "active" : ""}`} onClick={() => setFilter(f)}>
                    {f === "all" ? "All" : f === "free" ? "Free Agents" : "Under Contract"}
                  </button>
                ))}
              </div>
              <div className="sort-row">
                <span className="sort-label">Sort:</span>
                {(["overall", "age", "salary", "pace", "safety"] as SortKey[]).map((s) => (
                  <button key={s} className={`class-btn ${sortKey === s ? "active" : ""}`} onClick={() => setSortKey(s)}>
                    {s.charAt(0).toUpperCase() + s.slice(1)}
                  </button>
                ))}
              </div>
            </div>
            <div className="list-scroll">
              {sorted.map(({ driver: d, ovr, salary, isFree, contractTeam }) => (
                <div
                  key={d.id}
                  className={`car-list-item ${selectedId === d.id ? "selected" : ""}`}
                  onClick={() => setSelectedId(d.id)}
                >
                  <div className="car-item-info">
                    <div className="car-item-name"><span className={`fi fi-${d.nationality} driver-item-flag`} /> {d.name}</div>
                    <div className="car-item-details">
                      Age {d.age} &middot; {isFree ? "Free Agent" : `${contractTeam}`}
                    </div>
                  </div>
                  <div className="driver-item-right">
                    <div className="driver-item-overall">{ovr}</div>
                    {isFree ? (
                      <div className="driver-item-salary">${salary.toLocaleString()}/race</div>
                    ) : (
                      <span className="contracted-badge">Contracted</span>
                    )}
                  </div>
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
                    <div className="detail-name"><span className={`fi fi-${selected.driver.nationality} driver-item-flag`} /> {selected.driver.name}</div>
                    <div className="detail-meta">
                      Age {selected.driver.age} &middot; {selected.isFree ? "Free Agent" : `Contracted to ${selected.contractTeam}`}
                    </div>
                  </div>
                  <div className="detail-price-block">
                    <div className="detail-price-label">Market Value</div>
                    <div className="detail-price">${selected.driver.marketValue.toLocaleString()}</div>
                  </div>
                </div>

                {/* Stats */}
                <div className="stats-section">
                  <div className="stats-section-title">Driver Stats</div>
                  {DRIVER_STAT_KEYS.map((key) => (
                    <div className="stat-row" key={key}>
                      <span className="stat-name">{DRIVER_STAT_LABELS[key]}</span>
                      <div className="stat-bar-track">
                        <div className="stat-bar-fill" style={{ width: `${selected.stats[key]}%` }} />
                      </div>
                      <span className="stat-value">{Math.round(selected.stats[key])}</span>
                    </div>
                  ))}
                </div>

                {/* Contract options (free agents only) */}
                {selected.isFree && playerDriverCount < 4 && (
                  <div className="contract-section">
                    <div className="stats-section-title">Contract Options</div>
                    <div className="contract-options">
                      {contractPrices.map((cp) => (
                        <div
                          key={cp.length}
                          className={`contract-card ${selectedLength === cp.length ? "selected" : ""}`}
                          onClick={() => setSelectedLength(cp.length)}
                        >
                          <div className="contract-length">{cp.length} Year{cp.length > 1 ? "s" : ""}</div>
                          <div className="contract-salary">${cp.annual.toLocaleString()}/race</div>
                          <div className="contract-total">Total: ${cp.total.toLocaleString()}</div>
                          {cp.discount > 0 && <div className="contract-discount">{cp.discount}% off</div>}
                        </div>
                      ))}
                    </div>
                    <div className="buy-row">
                      <button className="btn-buy" disabled={!selectedContract || player.budget < selectedContract.annual} onClick={handleHire}>
                        Hire — ${selectedContract?.annual.toLocaleString()}/race
                      </button>
                    </div>
                  </div>
                )}

                {/* Buyout (contracted drivers) */}
                {!selected.isFree && selected.contract && (
                  <div className="buyout-section">
                    <div className="stats-section-title">Buyout</div>
                    <div className="buyout-details">
                      <span>Contracted to <strong>{selected.contractTeam}</strong> &middot; {selected.contract.remainingYears} year{selected.contract.remainingYears > 1 ? "s" : ""} remaining</span>
                    </div>
                    <div className="buy-row">
                      <button
                        className="btn-buy"
                        style={{ background: "#d04040" }}
                        disabled={player.budget < buyoutCost || playerDriverCount >= 4}
                        onClick={handleBuyout}
                      >
                        Buy Out — ${buyoutCost.toLocaleString()}
                      </button>
                    </div>
                  </div>
                )}

                {playerDriverCount >= 4 && (
                  <div className="detail-meta" style={{ marginTop: 16, color: "#e17055" }}>
                    Roster full — release a driver before hiring
                  </div>
                )}
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
