import { useCallback } from "react";
import { useGameStore } from "./state/store";
import { NewGameScreen } from "./screens/NewGameScreen";
import { GarageScreen } from "./screens/GarageScreen";
import { NewCarDealerScreen } from "./screens/NewCarDealerScreen";
import { SecondHandDealerScreen } from "./screens/SecondHandDealerScreen";
import { CarWorkshopScreen } from "./screens/CarWorkshopScreen";
import { DriverMarketScreen } from "./screens/DriverMarketScreen";
import { TeamRosterScreen } from "./screens/TeamRosterScreen";
import { CrewHiringScreen } from "./screens/CrewHiringScreen";
import { initializeGame } from "./simulation/gameInit";

export function App() {
  const game = useGameStore((s) => s.game);
  const screen = useGameStore((s) => s.screen);
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

  switch (screen) {
    case "newCarDealer":
      return <NewCarDealerScreen />;
    case "secondHandDealer":
      return <SecondHandDealerScreen />;
    case "carWorkshop":
      return <CarWorkshopScreen />;
    case "driverMarket":
      return <DriverMarketScreen />;
    case "teamRoster":
      return <TeamRosterScreen />;
    case "crewHiring":
      return <CrewHiringScreen />;
    case "garage":
    default:
      return <GarageScreen />;
  }
}
