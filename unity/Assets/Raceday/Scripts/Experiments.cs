namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// AI experiments shipped behind a switch, so Ivan can try one in the debug menu while the game
    /// runs. Experiment N is E(N), the N-th title here. E0 is none: the AI as merged, whose version
    /// (vN) is in Resources/AiVersion.txt and goes up with each merged behaviour change. The test
    /// suite and its scoreboard use the same ids. Code under trial checks <see cref="Is"/>.
    /// </summary>
    public static class Experiments
    {
        /// <summary>What each experiment tries, E1 first.</summary>
        public static readonly string[] All = { "path choosing" };

        /// <summary>Each experiment's revision, raised each time a better one is merged.</summary>
        public static readonly int[] Revisions = { 7 };

        /// <summary>The experiment switched on, or 0 for none.</summary>
        public static int On;

        public static bool Is(int experiment) => On == experiment;

        /// <summary>The label for the experiment switched on, e.g. "E3.2".</summary>
        public static string Label => On == 0 ? "E0" : $"E{On}.{Revisions[On - 1]}";
    }
}
