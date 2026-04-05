import { useEffect } from "react";
import { useGameStore } from "../state/store";
import "./PauseMenu.scss";

interface PauseMenuProps {
  onResume: () => void;
}

export function PauseMenu({ onResume }: PauseMenuProps) {
  const clearGame = useGameStore((s) => s.clearGame);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onResume();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [onResume]);

  const handleMainMenu = () => {
    clearGame();
  };

  const handleQuit = () => {
    window.close();
  };

  return (
    <div className="pause-overlay">
      <div className="pause-card">
        <div className="pause-title">Paused</div>
        <div className="pause-subtitle">The 24h Tarmac Gauntlet</div>

        <div className="pause-actions">
          <button className="pause-btn resume" onClick={onResume}>Resume</button>
          <button className="pause-btn main-menu" onClick={handleMainMenu}>Main Menu</button>
          <button className="pause-btn quit" onClick={handleQuit}>Quit to Desktop</button>
        </div>

        <div className="pause-hint">Press <kbd>Esc</kbd> to resume</div>
      </div>
    </div>
  );
}
