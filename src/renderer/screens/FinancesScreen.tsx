import { useState, useMemo } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { PRIZE_TABLE } from "../simulation/postRace";
import type { PlayerTeam, TransactionCategory } from "../types";
import "./DealerShared.scss";
import "./Finances.scss";

// ---------------------------------------------------------------------------
// Category icon mapping (matching mockup)
// ---------------------------------------------------------------------------

const TYRE_SVG = (
  <svg viewBox="0 0 512 512" fill="currentColor"><path d="M256 21A235 235 0 0 0 21 256a235 235 0 0 0 235 235 235 235 0 0 0 235-235A235 235 0 0 0 256 21zm0 82c84.393 0 153 68.607 153 153s-68.607 153-153 153-153-68.607-153-153 68.607-153 153-153zm0 18c-20.417 0-39.757 4.52-57.09 12.602C210.457 166.482 230.218 208 256 208c25.823 0 44.926-41.65 56.752-74.555C295.505 125.462 276.284 121 256 121z" /></svg>
);

interface CatStyle { icon: React.ReactNode; color: string }

const CATEGORY_STYLES: Record<TransactionCategory, CatStyle> = {
  carPurchase:  { icon: <i className="fa-solid fa-car" />,       color: "#d060a0" },
  carSale:      { icon: <i className="fa-solid fa-car" />,       color: "#00d4aa" },
  upgrade:      { icon: <i className="fa-solid fa-arrow-up" />,  color: "#5ab8d8" },
  spareParts:   { icon: <i className="fa-solid fa-wrench" />,    color: "#a0b8d0" },
  tyres:        { icon: <span className="txn-icon">{TYRE_SVG}</span>, color: "#a0b8d0" },
  driverSalary: { icon: <i className="fa-solid fa-user" />,      color: "#e0943a" },
  driverBuyout: { icon: <i className="fa-solid fa-user-xmark" />,color: "#d04040" },
  crewCost:     { icon: <i className="fa-solid fa-users" />,     color: "#e0943a" },
  prizeMoney:   { icon: <i className="fa-solid fa-trophy" />,    color: "#00d4aa" },
  fuelCost:     { icon: <i className="fa-solid fa-gas-pump" />,  color: "#a0785a" },
};

type FilterGroup = "all" | "cars" | "upgrades" | "consumables" | "staff" | "race";

const FILTER_CATEGORIES: Record<FilterGroup, TransactionCategory[] | null> = {
  all: null,
  cars: ["carPurchase", "carSale"],
  upgrades: ["upgrade"],
  consumables: ["spareParts", "tyres"],
  staff: ["driverSalary", "driverBuyout", "crewCost"],
  race: ["prizeMoney", "fuelCost"],
};

function ordinal(n: number): string {
  const s = ["th", "st", "nd", "rd"];
  const v = n % 100;
  return n + (s[(v - 20) % 10] || s[v] || s[0]);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function FinancesScreen() {
  const game = useGameStore((s) => s.game);

  const [filterGroup, setFilterGroup] = useState<FilterGroup>("all");
  const [yearFilter, setYearFilter] = useState<number | "all">("all");

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;
  const currentYear = game.currentYear;

  // --- Year summary (last 3 years) ---
  const yearCards = useMemo(() => {
    const years: number[] = [];
    for (let y = currentYear; y >= Math.max(1, currentYear - 2); y--) {
      years.push(y);
    }
    years.sort((a, b) => a - b);

    return years.map((y) => {
      const yearTxns = player.transactions.filter((t) => t.year === y);
      const income = yearTxns.filter((t) => t.amount > 0).reduce((s, t) => s + t.amount, 0);
      const expenses = yearTxns.filter((t) => t.amount < 0).reduce((s, t) => s + t.amount, 0);
      const net = income + expenses;
      return { year: y, income, expenses, net, isCurrent: y === currentYear };
    });
  }, [player.transactions, currentYear]);

  // --- Transaction filtering ---
  const allYears = useMemo(() => {
    const ys = [...new Set(player.transactions.map((t) => t.year))].sort((a, b) => b - a);
    return ys;
  }, [player.transactions]);

  const filteredTxns = useMemo(() => {
    let txns = [...player.transactions].reverse(); // most recent first
    if (yearFilter !== "all") {
      txns = txns.filter((t) => t.year === yearFilter);
    }
    const cats = FILTER_CATEGORIES[filterGroup];
    if (cats) {
      txns = txns.filter((t) => cats.includes(t.category));
    }
    return txns;
  }, [player.transactions, yearFilter, filterGroup]);

  return (
    <div className="finances-root">
      <div className="finances-app">
        <TopBar />

        <div className="finances-main">
          {/* Left column */}
          <div className="left-col">
            {/* Budget overview */}
            <div className="panel">
              <div className="panel-header">
                <span className="panel-title">Budget Overview</span>
              </div>
              <div className="panel-body">
                <div className="balance-row">
                  <span className="balance-label">Current Balance</span>
                  <span className="balance-amount">${player.budget.toLocaleString()}</span>
                </div>
                <div className="year-summary">
                  {yearCards.map((yc) => (
                    <div key={yc.year} className={`year-card ${yc.isCurrent ? "current" : ""}`}>
                      <div className="year-card-year">
                        Year {yc.year}{yc.isCurrent ? " (Current)" : ""}
                      </div>
                      <div className="year-line">
                        <span className="year-line-label">Income</span>
                        {yc.income > 0 ? (
                          <span className="year-line-value income">+${yc.income.toLocaleString()}</span>
                        ) : yc.isCurrent ? (
                          <span className="year-line-value empty-income">After next race</span>
                        ) : (
                          <span className="year-line-value">$0</span>
                        )}
                      </div>
                      <div className="year-line">
                        <span className="year-line-label">Expenses</span>
                        <span className={`year-line-value ${yc.expenses < 0 ? "expense" : ""}`}>
                          {yc.expenses < 0 ? `\u2212$${Math.abs(yc.expenses).toLocaleString()}` : "$0"}
                        </span>
                      </div>
                      <div className="year-line net">
                        <span className="year-line-label">Net</span>
                        <span className={`year-line-value ${yc.net >= 0 ? "net-pos" : "net-neg"}`}>
                          {yc.net >= 0 ? `+$${yc.net.toLocaleString()}` : `\u2212$${Math.abs(yc.net).toLocaleString()}`}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            {/* Transaction log */}
            <div className="panel txn-panel">
              <div className="panel-header">
                <span className="panel-title">Transaction History</span>
              </div>
              <div className="panel-body">
                <div className="txn-filters">
                  {(["all", "cars", "upgrades", "consumables", "staff", "race"] as FilterGroup[]).map((g) => (
                    <button key={g} className={`txn-filter-btn ${filterGroup === g ? "active" : ""}`} onClick={() => setFilterGroup(g)}>
                      {g.charAt(0).toUpperCase() + g.slice(1)}
                    </button>
                  ))}
                  <span style={{ flex: 1 }} />
                  <select
                    className="txn-year-select"
                    value={yearFilter}
                    onChange={(e) => setYearFilter(e.target.value === "all" ? "all" : Number(e.target.value))}
                  >
                    <option value="all">All Years</option>
                    {allYears.map((y) => (
                      <option key={y} value={y}>Year {y}</option>
                    ))}
                  </select>
                </div>
                <div className="txn-scroll">
                  {filteredTxns.length === 0 ? (
                    <div className="txn-empty">No transactions to show</div>
                  ) : (
                    filteredTxns.map((t, i) => {
                      const style = CATEGORY_STYLES[t.category];
                      return (
                        <div className="txn-row" key={i}>
                          <span className="txn-year">Y{t.year}</span>
                          <span className="txn-icon" style={{ color: style.color }}>{style.icon}</span>
                          <span className="txn-desc">{t.description}</span>
                          <span className={`txn-amount ${t.amount >= 0 ? "positive" : "negative"}`}>
                            {t.amount >= 0 ? `+$${t.amount.toLocaleString()}` : `\u2212$${Math.abs(t.amount).toLocaleString()}`}
                          </span>
                        </div>
                      );
                    })
                  )}
                </div>
              </div>
            </div>
          </div>

          {/* Right column: Prize schedule */}
          <div className="right-col">
            <div className="panel prize-panel">
              <div className="panel-header">
                <span className="panel-title">Prize Schedule</span>
              </div>
              <div className="panel-body">
                <div className="prize-scroll">
                  <table className="prize-table">
                    <thead>
                      <tr><th>Position</th><th>Prize</th></tr>
                    </thead>
                    <tbody>
                      {PRIZE_TABLE.map((amount, i) => {
                        const pos = i + 1;
                        const cls = pos === 1 ? "gold" : pos === 2 ? "silver" : pos === 3 ? "bronze" : "";
                        return (
                          <tr key={pos} className={cls}>
                            <td>{ordinal(pos)}</td>
                            <td>${amount.toLocaleString()}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
                <div className="prize-total">
                  <span className="prize-total-label">Total Fund</span>
                  <span className="prize-total-value">$15,000,000</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
