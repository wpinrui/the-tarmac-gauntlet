#nullable enable

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS <c>AiTeamEntry</c> from simulation/aiTeamNames.ts. The roster itself is ported later.</summary>
    public sealed record AiTeamEntry
    {
        public string Name { get; init; } = "";
        public string Descriptor { get; init; } = "";
        public CarClass CarClass { get; init; }
    }
}
