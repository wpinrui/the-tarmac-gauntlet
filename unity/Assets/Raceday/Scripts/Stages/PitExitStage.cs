namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The drive out of the pit lane: the field leaves its garages together and lines up in grid order behind the
    /// pace car (<see cref="RaceStart"/>, <see cref="GarageStart"/>). Until it begins the race holds, every car
    /// parked in its garage. It is done once every car the AI drives is out on the track in formation. A skip stops
    /// the drive out where it is, and the formation lap that follows puts the field in place.
    /// </summary>
    public sealed class PitExitStage : SkippableStage
    {
        public RaceStart Race;

        private bool skipped;

        public override string Name => "pit exit";

        public override void Begin()
        {
            if (Race != null) Race.Holding = false;
        }

        public override bool Done => Race == null || skipped || Race.PitOutDone;

        public override void Skip()
        {
            if (Race != null) Race.EndPitOut();
            skipped = true;
        }
    }
}
