using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The first stage after the flag: every car's cooldown lap into its box (<see cref="RaceDirector"/>), shown by the
    /// race's own cameras. It starts when the race time runs out and ends once the race is settled. It can be skipped
    /// only once your race is over (<see cref="RaceWeekend.CanSkipToEnd"/>), and skipping it settles the race from where
    /// every car is (<see cref="RaceWeekend.SkipToEnd"/>).
    /// </summary>
    public sealed class CooldownStage : SkippableStage
    {
        public RaceDirector Director;

        public override string Name => "cool-down lap";

        public override void Begin() { }

        public override bool Done => Director == null || Director.Finished;

        public override bool Skippable => RaceWeekend.CanSkipToEnd;

        public override void Skip() => RaceWeekend.SkipToEnd();
    }
}
