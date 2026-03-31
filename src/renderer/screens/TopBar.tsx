import { useGameStore, type Screen } from "../state/store";
import type { PlayerTeam } from "../types";

export function TopBar() {
  const game = useGameStore((s) => s.game);
  const screen = useGameStore((s) => s.screen);
  const setScreen = useGameStore((s) => s.setScreen);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam | undefined;
  if (!player) return null;

  return (
    <div className="tab-bar">
      <div className="team-identity">
        <div className="team-logo">
          <svg viewBox="0 0 48 48"><path d="M24 4L40 12v14c0 12-16 18-16 18S8 38 8 26V12z" /></svg>
        </div>
        <span className="team-name">{player.name}</span>
      </div>
      <div className={`tab ${screen === "garage" ? "active" : ""}`} onClick={() => setScreen("garage")}>Garage</div>
      <div className={`tab ${screen === "finances" ? "active" : ""}`} onClick={() => setScreen("finances")}>Finances</div>
      <div className={`tab ${screen === "raceHistory" ? "active" : ""}`} onClick={() => setScreen("raceHistory")}>Race History</div>
      <div className="tab">Standings</div>
      <div className="tab">Scouting Report</div>
      <div className="tab-spacer" />
      <div className="tab-resource">
        <i className="fa-solid fa-wrench res-icon" />
        <span className="res-value">{player.spareParts}</span>
        <div className="res-tooltip">
          <div className="res-tooltip-title">Spare Parts</div>
          <div className="res-tooltip-desc">Fix mechanical issues during races and repair car condition between races. Unspent parts carry over each year.</div>
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
  );
}
