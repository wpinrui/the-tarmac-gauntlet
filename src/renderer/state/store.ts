import { create } from "zustand";
import type { GameState, GamePhase } from "../types";

interface GameStore {
  game: GameState | null;

  // Lifecycle
  setGame: (game: GameState) => void;
  setPhase: (phase: GamePhase) => void;
  clearGame: () => void;
}

export const useGameStore = create<GameStore>()((set) => ({
  game: null,

  setGame: (game) => set({ game }),

  setPhase: (phase) =>
    set((state) => {
      if (!state.game) return state;
      return { game: { ...state.game, phase } };
    }),

  clearGame: () => set({ game: null }),
}));
