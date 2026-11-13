using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The first stage after the flag: the cooldown lap into the box (<see cref="RaceDirector"/>), driven by whoever has
    /// your car's wheel, and shown by the race's own cameras. It starts when the race time runs out. It ends once your
    /// car is parked in its box and its crew have waved it home (<see cref="PitCrews.Received"/>) and every car has
    /// taken the flag, so the results stand, and the race is settled there. With no car of yours, or a car that never
    /// comes home, it ends when the race director settles the race. It can be skipped only once your race is over
    /// (<see cref="RaceWeekend.CanSkipToEnd"/>), and skipping it settles the race from where every car is
    /// (<see cref="RaceWeekend.SkipToEnd"/>).
    /// </summary>
    public sealed class CooldownStage : SkippableStage
    {
        public RaceDirector Director;
        public PitCrews Crews;
        /// <summary>Index of your car, or -1 for none.</summary>
        public int Player = -1;

        public override string Name => "cool-down lap";

        public override void Begin() { }

        public override bool Done => Director == null || Director.Finished;

        public override bool Skippable => RaceWeekend.CanSkipToEnd;

        public override void Skip() => RaceWeekend.SkipToEnd();

        private void Update()
        {
            if (Director == null || Director.Finished || Player < 0 || Crews == null) return;
            if (Crews.Received(Player) && Director.FieldFlagged) Director.FinishNow();
        }
    }
}
