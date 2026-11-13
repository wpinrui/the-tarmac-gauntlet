namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The end of the formation lap: it cuts to the leader just short of the last corner, the pace car gone
    /// into the pit lane and the field behind two by two in grid order (<see cref="RaceStart.CutToFinal"/>).
    /// It is done at the green, as the leader crosses the line. A car the player drives keeps its place on a
    /// limiter until it crosses the line itself (<see cref="RollingStart.Limiter"/>). It is the start of the
    /// race, so it can't be skipped.
    /// </summary>
    public sealed class FinalFormation : SkippableStage
    {
        public RaceStart Formation;
        public RaceCameras Cameras;

        /// <summary>True once the stage has begun: the player may take the wheel.</summary>
        public bool Begun { get; private set; }

        public override string Name => "formation lap";

        public override bool Done => Formation.Racing;

        public override bool Skippable => false;

        public override void Begin()
        {
            Begun = true;
            if (Formation.Racing) return;
            Formation.CutToFinal(RollingStart.FinalStart(Formation.PaceCar.Circuit));
            if (Cameras != null) Cameras.Cut();
        }

        /// <summary>Never called: the stage can't be skipped.</summary>
        public override void Skip() { }
    }
}
