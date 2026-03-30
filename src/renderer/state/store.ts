import { create } from "zustand";
import type {
  GameState,
  GamePhase,
  CarInstance,
  Contract,
  ContractLength,
  PlayerTeam,
  InstalledUpgrades,
  TransactionCategory,
  TransactionRecord,
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
  | "teamRoster"
  | "crewHiring"
  | "finances";

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
  buyCar: (car: CarInstance, cost: number, modelName: string) => void;
  sellCar: (carId: string, salePrice: number, modelName: string) => void;
  enterCar: (carId: string) => void;
  installUpgrade: (carId: string, packType: UpgradePackType, cost: number, modelName: string) => void;
  repairCar: (carId: string, partsUsed: number, conditionGain: number) => void;

  // Driver actions — hireDriver does NOT deduct annual salary (yearAdvance handles that)
  hireDriver: (driverId: string, driverName: string, length: ContractLength, annualSalary: number) => void;
  releaseDriver: (driverId: string) => void;
  buyoutDriver: (driverId: string, driverName: string, buyoutCost: number) => void;

  // Crew — setCrewSize does NOT deduct annual cost (yearAdvance handles that)
  setCrewSize: (size: number) => void;

  // Consumables
  buySpares: (quantity: number, totalCost: number) => void;
  buyTyres: (quantity: number, totalCost: number) => void;

  // Post-race financial actions
  awardPrizeMoney: (teamId: string, position: number, amount: number) => void;
  deductFuelCost: (totalCost: number) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getYear(state: { game: GameState | null }): number {
  return state.game?.currentYear ?? 1;
}

function tx(
  year: number,
  category: TransactionCategory,
  amount: number,
  description: string,
): TransactionRecord {
  return { year, category, amount, description };
}

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

  buyCar: (car, cost, modelName) =>
    set((state) => {
      const year = getYear(state);
      return updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - cost,
        cars: [...p.cars, car],
        enteredCarId: p.enteredCarId ?? car.id,
        transactions: [...p.transactions, tx(year, "carPurchase", -cost, `Bought ${modelName}`)],
      }));
    }),

  sellCar: (carId, salePrice, modelName) =>
    set((state) => {
      const year = getYear(state);
      return updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget + salePrice,
        cars: p.cars.filter((c) => c.id !== carId),
        enteredCarId: p.enteredCarId === carId ? null : p.enteredCarId,
        transactions: [...p.transactions, tx(year, "carSale", salePrice, `Sold ${modelName}`)],
      }));
    }),

  enterCar: (carId) =>
    set((state) =>
      updatePlayer(state, (p) => ({ ...p, enteredCarId: carId })),
    ),

  installUpgrade: (carId, packType, cost, modelName) =>
    set((state) => {
      const year = getYear(state);
      const packLabel = packType.charAt(0).toUpperCase() + packType.slice(1);
      return updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - cost,
        cars: p.cars.map((c) =>
          c.id === carId
            ? { ...c, installedUpgrades: { ...c.installedUpgrades, [packType]: true } as InstalledUpgrades }
            : c,
        ),
        transactions: [...p.transactions, tx(year, "upgrade", -cost, `${packLabel} Pack on ${modelName}`)],
      }));
    }),

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
    set((state) => {
      const year = getYear(state);
      return updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - totalCost,
        spareParts: p.spareParts + quantity,
        transactions: [...p.transactions, tx(year, "spareParts", -totalCost, `Bought ${quantity} spare parts`)],
      }));
    }),

  buyTyres: (quantity, totalCost) =>
    set((state) => {
      const year = getYear(state);
      return updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - totalCost,
        tyreSets: p.tyreSets + quantity,
        transactions: [...p.transactions, tx(year, "tyres", -totalCost, `Bought ${quantity} tyre sets`)],
      }));
    }),

  // --- Driver actions ---
  // Note: hireDriver does NOT deduct annual salary. Salaries are deducted at yearAdvance.

  hireDriver: (driverId, driverName, length, annualSalary) =>
    set((state) => {
      if (!state.game) return state;
      const contract: Contract = {
        driverId,
        teamId: "player",
        length,
        remainingYears: length,
        annualSalary,
      };
      const result = updatePlayer(state, (p) => ({
        ...p,
        contracts: [...p.contracts, contract],
      }));
      if (result.game) {
        result.game = { ...result.game, contracts: [...result.game.contracts, contract] };
      }
      return result;
    }),

  releaseDriver: (driverId) =>
    set((state) => {
      if (!state.game) return state;
      const result = updatePlayer(state, (p) => ({
        ...p,
        contracts: p.contracts.filter((c) => c.driverId !== driverId),
      }));
      if (result.game) {
        result.game = {
          ...result.game,
          contracts: result.game.contracts.filter(
            (c) => !(c.driverId === driverId && c.teamId === "player"),
          ),
        };
      }
      return result;
    }),

  buyoutDriver: (driverId, driverName, buyoutCost) =>
    set((state) => {
      if (!state.game) return state;
      const year = getYear(state);
      const updatedContracts = state.game.contracts.filter(
        (c) => c.driverId !== driverId,
      );
      const result = updatePlayer(state, (p) => ({
        ...p,
        budget: p.budget - buyoutCost,
        transactions: [...p.transactions, tx(year, "driverBuyout", -buyoutCost, `Bought out ${driverName}`)],
      }));
      if (result.game) {
        result.game = { ...result.game, contracts: updatedContracts };
      }
      return result;
    }),

  // --- Crew ---
  // Note: setCrewSize does NOT deduct annual cost. Crew costs are deducted at yearAdvance.

  setCrewSize: (size) =>
    set((state) =>
      updatePlayer(state, (p) => ({ ...p, crewSize: size })),
    ),

  // --- Post-race financial actions ---

  awardPrizeMoney: (teamId, position, amount) =>
    set((state) => {
      if (!state.game) return state;
      const year = getYear(state);
      const teams = state.game.teams.map((t) => {
        if (t.id !== teamId) return t;
        const updated = { ...t, budget: t.budget + amount };
        if (t.kind === "player") {
          (updated as PlayerTeam).transactions = [
            ...(t as PlayerTeam).transactions,
            tx(year, "prizeMoney", amount, `P${position} prize money`),
          ];
        }
        return updated;
      });
      return { game: { ...state.game, teams } };
    }),

  deductFuelCost: (totalCost) =>
    set((state) => {
      const year = getYear(state);
      return updatePlayer(state, (p) => {
        const actualCost = Math.min(p.budget, totalCost);
        return {
          ...p,
          budget: p.budget - actualCost,
          transactions: [
            ...p.transactions,
            tx(year, "fuelCost", -actualCost,
              actualCost < totalCost
                ? `Fuel cost (written off — $${(totalCost - actualCost).toLocaleString()} unpaid)`
                : `Fuel cost`),
          ],
        };
      });
    }),
}));
