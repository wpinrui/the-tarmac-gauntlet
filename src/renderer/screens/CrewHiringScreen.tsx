import { useState } from "react";
import { useGameStore } from "../state/store";
import { TopBar } from "./TopBar";
import { calculateEffectiveStats } from "../simulation/effectiveStats";
import { estimateFullServicePitDuration } from "../simulation/pitStop";
import type { PlayerTeam } from "../types";
import backdropUrl from "../assets/crew-backdrop.jpg";
import "./DealerShared.scss";
import "./CrewHiring.scss";

const CREW_COST_PER_MEMBER = 2_000;
const MAX_CREW = 16;


export function CrewHiringScreen() {
  const game = useGameStore((s) => s.game);
  const setScreen = useGameStore((s) => s.setScreen);
  const setCrewSize = useGameStore((s) => s.setCrewSize);

  if (!game) return null;
  const player = game.teams.find((t) => t.kind === "player") as PlayerTeam;

  const [newSize, setNewSize] = useState(player.crewSize);
  const annualCost = newSize * CREW_COST_PER_MEMBER;
  const costDelta = (newSize - player.crewSize) * CREW_COST_PER_MEMBER;

  const enteredCar = player.cars.find((c) => c.id === player.enteredCarId);
  const enteredModel = enteredCar ? game.carModels.find((m) => m.id === enteredCar.modelId) : undefined;
  const effectiveStats = enteredCar && enteredModel ? calculateEffectiveStats(enteredCar, enteredModel) : null;

  const pitStopTime = effectiveStats?.pitStopTime ?? 0;
  const fuelCapacity = effectiveStats?.fuelCapacity ?? 0;
  const currentPitTime = estimateFullServicePitDuration(pitStopTime, fuelCapacity, player.crewSize, player.skills.engineer);
  const newPitTime = estimateFullServicePitDuration(pitStopTime, fuelCapacity, newSize, player.skills.engineer);
  // Max bar reference: solo (0 crew, 0 skill) pit time
  const maxPitTime = estimateFullServicePitDuration(pitStopTime, fuelCapacity, 0, 0);

  const handleConfirm = () => {
    if (costDelta > player.budget) return;
    setCrewSize(newSize, Math.max(0, costDelta));
    setScreen("garage");
  };

  return (
    <div className="dealer-root" style={{ "--dealer-backdrop": `url(${backdropUrl})` } as React.CSSProperties}>
      <div className="dealer-app">
        <TopBar />
        <div className="breadcrumb">
          <a onClick={() => setScreen("garage")}>&larr; Garage</a>
          <span className="sep">/</span>
          <span className="current">Crew Hiring</span>
        </div>

        <div className="main-content">
          <div className="crew-card">
            <div className="crew-title">Pit Crew</div>
            <div className="crew-subtitle">More crew members mean faster pit stops. Set your headcount for the upcoming year.</div>

            <div className="crew-stepper">
              <button className="stepper-btn" disabled={newSize <= 0} onClick={() => setNewSize((s) => Math.max(0, s - 1))}>&minus;</button>
              <div className="stepper-value">{newSize}</div>
              <button className="stepper-btn" disabled={newSize >= MAX_CREW} onClick={() => setNewSize((s) => Math.min(MAX_CREW, s + 1))}>+</button>
            </div>

            <div className="crew-costs">
              <div className="cost-item">
                <div className="cost-label">Per Member</div>
                <div className="cost-value">${CREW_COST_PER_MEMBER.toLocaleString()}</div>
              </div>
              <div className="cost-item">
                <div className="cost-label">Annual Cost</div>
                <div className="cost-value money">${annualCost.toLocaleString()}</div>
              </div>
              <div className="cost-item">
                <div className="cost-label">Current Crew</div>
                <div className="cost-value">{player.crewSize}</div>
              </div>
            </div>

            {enteredModel && (
              <div className="pit-preview">
                <div className="pit-preview-title">Est. Pit Stop Time</div>
                <div className="pit-note" style={{ marginBottom: 14, marginTop: -8 }}>Full refuel, tyre change, and driver swap (game time)</div>
                <div className="pit-bar-container">
                  <span className="pit-bar-label">Current ({player.crewSize})</span>
                  <div className="pit-bar-track">
                    <div className="pit-bar-fill previous" style={{ width: `${(currentPitTime / maxPitTime) * 100}%` }} />
                  </div>
                  <span className="pit-bar-time">{Math.floor(currentPitTime / 60)}:{String(Math.round(currentPitTime % 60)).padStart(2, "0")}</span>
                </div>
                <div className="pit-bar-container">
                  <span className="pit-bar-label">New ({newSize})</span>
                  <div className="pit-bar-track">
                    <div className="pit-bar-fill current" style={{ width: `${(newPitTime / maxPitTime) * 100}%` }} />
                  </div>
                  <span className="pit-bar-time">{Math.floor(newPitTime / 60)}:{String(Math.round(newPitTime % 60)).padStart(2, "0")}</span>
                </div>
                <div className="pit-note">
                  Based on Engineer skill ({player.skills.engineer}) and {enteredModel.name}
                </div>
              </div>
            )}

            <div className="crew-action">
              <button className="btn-confirm" disabled={costDelta > player.budget || newSize === player.crewSize} onClick={handleConfirm}>
                {costDelta > 0 ? `Confirm — $${costDelta.toLocaleString()}/race` : newSize === player.crewSize ? "No Change" : "Confirm — Free"}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
