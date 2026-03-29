import { useCallback } from "react";
import { useGameStore } from "./state/store";
import { NewGameScreen } from "./screens/NewGameScreen";
import { GarageScreen } from "./screens/GarageScreen";
import { initializeGame } from "./simulation/gameInit";

export function App() {
  const game = useGameStore((s) => s.game);
  const setGame = useGameStore((s) => s.setGame);

  const handleNewGame = useCallback(
    (data: {
      playerName: string;
      teamName: string;
      logo: string | null;
      skills: { driver: number; engineer: number; business: number };
    }) => {
      const gameState = initializeGame(data, Math.random);
      setGame(gameState);
    },
    [setGame],
  );

  if (!game) {
    return <NewGameScreen onStart={handleNewGame} />;
  }

  return <GarageScreen />;
}
