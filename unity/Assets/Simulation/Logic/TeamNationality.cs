#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A team's nationality as an ISO 3166-1 alpha-2 code. The player picks theirs. AI teams take theirs from
    /// AiTeamRoster by name, so nothing extra is stored in saves. A name the roster doesn't know falls back to a
    /// stable hash of the name.
    /// </summary>
    public static class TeamNationality
    {
        private static readonly Dictionary<string, string> ByName =
            AiTeamRoster.All.ToDictionary(e => e.Name, e => e.Nationality);

        public static string Of(BaseTeam team)
        {
            if (team is PlayerTeam p && p.Nationality.Length > 0) return p.Nationality;
            return ByName.TryGetValue(team.Name, out var code) ? code : FromName(team.Name);
        }

        /// <summary>FNV-1a over the name, so it is stable across runs and platforms.</summary>
        public static string FromName(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return GameInit.Nationalities[hash % (uint)GameInit.Nationalities.Length];
        }
    }
}
