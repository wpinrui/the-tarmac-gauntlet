namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The end of the formation lap: it cuts to the leader just short of the last corner, the pace car gone
    /// into the pit lane and the field behind two by two in grid order (<see cref="RaceStart.CutToFinal"/>).
    /// It is done at the green, as the leader crosses the line. A car the player drives keeps its place on a
    /// limiter until it crosses the line itself (<see cref="RollingStart.Limiter"/>). Skipped, it cuts to the
    /// leader at the line.
    /// </summary>
    public sealed class FinalFormation : SkippableStage
    {
        /// <summary>Skipped, the leader is put this far short of the line, m: the green comes on the next steps.</summary>
        private const float SkipShort = 3f;

        public RaceStart Start;
        public RaceCameras Cameras;

        /// <summary>True once the stage has begun: the player may take the wheel.</summary>
        public bool Begun { get; private set; }

        private bool skipped;

        public override string Name => "formation lap";

        public override bool Done => skipped || Start.Racing;

        public override void Begin()
        {
            Begun = true;
            if (Start.Racing) return;
            Start.CutToFinal(RollingStart.FinalStart(Start.PaceCar.Circuit));
            if (Cameras != null) Cameras.Cut();
        }

        public override void Skip()
        {
            skipped = true;
            if (Start.Racing) return;
            Start.CutToFinal(Start.PaceCar.Circuit.Lap - SkipShort);
            if (Cameras != null) Cameras.Cut();
        }
    }
}
