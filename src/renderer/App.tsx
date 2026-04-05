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
import { FinancesScreen } from "./screens/FinancesScreen";
import { PostRaceSummaryScreen } from "./screens/PostRaceSummaryScreen";
import { RaceHistoryScreen } from "./screens/RaceHistoryScreen";
import { StandingsScreen } from "./screens/StandingsScreen";
import { ScoutingReportScreen } from "./screens/ScoutingReportScreen";
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

  // Post-race phase overrides screen routing
  if (game.phase === "postRace") {
    return <PostRaceSummaryScreen />;
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
    case "finances":
      return <FinancesScreen />;
    case "raceHistory":
      return <RaceHistoryScreen />;
    case "standings":
      return <StandingsScreen />;
    case "scoutingReport":
      return <ScoutingReportScreen />;
    case "garage":
    default:
      return <GarageScreen />;
  }
}
