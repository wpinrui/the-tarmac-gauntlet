#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// The order the player's drivers race in. The Drivers screen sets it and the pre-race lineup starts from it.
    /// </summary>
    public static class DriverLineup
    {
        /// <summary>
        /// The player's drivers, starter first: the owner-driver (<see cref="RaceEntry.PlayerCharacterDriverId"/>)
        /// and every driver on an active player contract. The stored order comes first; drivers it doesn't name,
        /// such as a new hire, follow in roster order. Released drivers drop out.
        /// </summary>
        public static IReadOnlyList<string> Of(GameState game)
        {
            List<string> roster = Roster(game);
            PlayerTeam? player = game.Teams.OfType<PlayerTeam>().FirstOrDefault();
            IEnumerable<string> stored = player?.Lineup ?? System.Array.Empty<string>();
            return stored.Where(roster.Contains).Distinct().Concat(roster.Where(id => !stored.Contains(id))).ToArray();
        }

        /// <summary>The owner-driver, then hired drivers in contract order.</summary>
        private static List<string> Roster(GameState game)
        {
            var ids = new List<string> { RaceEntry.PlayerCharacterDriverId };
            foreach (Contract c in game.Contracts)
                if (c.TeamId == GameActions.PlayerTeamId && c.RemainingYears > 0 && !ids.Contains(c.DriverId))
                    ids.Add(c.DriverId);
            return ids;
        }
    }
}
