#nullable enable

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS <c>AiTeamEntry</c> from simulation/aiTeamNames.ts. The roster is in Data/AiTeamRoster.cs.</summary>
    public sealed record AiTeamEntry
    {
        public string Name { get; init; } = "";
        public string Descriptor { get; init; } = "";
        /// <summary>The one car model this team ever runs.</summary>
        public string ModelId { get; init; } = "";
        public CarClass CarClass { get; init; }
    }
}
