import { useGameStore } from "../state/store";

export function GarageScreen() {
  const game = useGameStore((s) => s.game);

  if (!game) return null;

  const playerTeam = game.teams.find((t) => t.kind === "player");

  return (
    <div
      style={{
        background: "#080d18",
        color: "#d0d8e4",
        fontFamily: "'Inter', sans-serif",
        minHeight: "100vh",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: 16,
      }}
    >
      <h1
        style={{
          fontFamily: "'Oswald', sans-serif",
          fontSize: 36,
          letterSpacing: 3,
          textTransform: "uppercase",
          color: "#fff",
        }}
      >
        Garage
      </h1>
      <p style={{ color: "#6a8098" }}>
        Welcome, {playerTeam?.kind === "player" ? playerTeam.playerName : ""}!
        Your team <strong>{playerTeam?.name}</strong> is ready.
      </p>
      <p style={{ color: "#5a7090", fontSize: 14 }}>
        Year {game.currentYear} &middot; Budget: ${playerTeam?.budget.toLocaleString()} &middot;{" "}
        {game.teams.length} teams &middot; {game.drivers.length} drivers
      </p>
    </div>
  );
}
