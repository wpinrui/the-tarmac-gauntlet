import { create } from "zustand";
import type {
  GameState,
  GamePhase,
  CarInstance,
  CarModel,
  PlayerTeam,
  InstalledUpgrades,
  UpgradePackType,
} from "../types";

// ---------------------------------------------------------------------------
// Navigation state
// ---------------------------------------------------------------------------

export type Screen =
  | "garage"
  | "newCarDealer"
  | "secondHandDealer"
  | "carWorkshop"
  | "driverMarket"
  | "crewHiring";

// ---------------------------------------------------------------------------
// Store interface
// ---------------------------------------------------------------------------

interface GameStore {
  game: GameState | null;
  screen: Screen;

  // Lifecycle
  setGame: (game: GameState) => void;
  setPhase: (phase: GamePhase) => void;
  clearGame: () => void;

  // Navigation
  setScreen: (screen: Screen) => void;

  // Car actions
  buyCar: (car: CarInstance, cost: number) => void;
  sellCar: (carId: string, salePrice: number) => void;
  enterCar: (carId: string) => void;
  installUpgrade: (carId: string, packType: UpgradePackType, cost: number) => void;
  repairCar: (carId: string, partsUsed: number, conditionGain: number) => void;

  // Consumables
  buySpares: (quantity: number, totalCost: number) => void;
  buyTyres: (quantity: number, totalCost: number) => void;
}

// ---------------------------------------------------------------------------
// Helper: update the player team within game state
// ---------------------------------------------------------------------------

function updatePlayer(
  state: { game: GameState | null },
  updater: (player: PlayerTeam) => PlayerTeam,
): Partial<{ game: GameState | null }> {
  if (!state.game) return state;
  const teams = state.game.teams.map((t) =>
    t.kind === "player" ? updater(t as PlayerTeam) : t,
  );
  return { game: { ...state.game, teams } };
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useGameStore = create<GameStore>()((set) => ({
  game: null,
  screen: "garage" as Screen,

  setGame: (game) => set({ game, screen: "garage" }),
  setPhase: (phase) =>
    set((state) => {
      if (!state.game) return state;
      return { game: { ...state.game, phase } };
    }),
  clearGame: () => set({ game: null, screen: "garage" }),

  setScreen: (screen) => set({ screen }),

  // --- Car actions ---

  buyCar: (car, cost) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - cost,
        cars: [...p.cars, car],
        enteredCarId: p.enteredCarId ?? car.id,
      })),
    ),

  sellCar: (carId, salePrice) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget + salePrice,
        cars: p.cars.filter((c) => c.id !== carId),
        enteredCarId: p.enteredCarId === carId ? null : p.enteredCarId,
      })),
    ),

  enterCar: (carId) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        enteredCarId: carId,
      })),
    ),

  installUpgrade: (carId, packType, cost) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - cost,
        cars: p.cars.map((c) =>
          c.id === carId
            ? {
                ...c,
                installedUpgrades: {
                  ...c.installedUpgrades,
                  [packType]: true,
                } as InstalledUpgrades,
              }
            : c,
        ),
      })),
    ),

  repairCar: (carId, partsUsed, conditionGain) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        spareParts: p.spareParts - partsUsed,
        cars: p.cars.map((c) =>
          c.id === carId
            ? { ...c, condition: Math.min(100, c.condition + conditionGain) }
            : c,
        ),
      })),
    ),

  // --- Consumables ---

  buySpares: (quantity, totalCost) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - totalCost,
        spareParts: p.spareParts + quantity,
      })),
    ),

  buyTyres: (quantity, totalCost) =>
    set((state) =>
      updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - totalCost,
        tyreSets: p.tyreSets + quantity,
      })),
    ),
}));
