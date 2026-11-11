#nullable enable

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// A team's nationality as an ISO 3166-1 alpha-2 code. The player picks theirs. AI teams have none stored, so each
    /// gets a fixed one from its name: the same team always shows the same flag, with no change to saves or random draws.
    /// </summary>
    public static class TeamNationality
    {
        public static string Of(BaseTeam team)
        {
            if (team is PlayerTeam p && p.Nationality.Length > 0) return p.Nationality;
            return FromName(team.Name);
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
