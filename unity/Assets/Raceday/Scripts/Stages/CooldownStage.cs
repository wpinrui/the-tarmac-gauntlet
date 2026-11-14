using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The first stage after the flag: the cooldown lap into the box (<see cref="RaceDirector"/>), driven by whoever has
    /// your car's wheel, and shown by the race's own cameras, whichever view you were in: live timing closes once your
    /// race is over (<see cref="RaceEnd.TimingAllowed"/>). It starts when the race time runs out, and the race is settled
    /// once it is over (<see cref="RaceEnd.CooldownOver"/>): your car parked and received by its crew
    /// (<see cref="PitCrews.Received"/>) with every car past the flag, or with your car out of the race, a moment after
    /// the last car takes the flag, the camera following the leader home meanwhile (<see cref="RaceEnd.ShowAfterFlag"/>).
    /// The race director's own limits settle a race that gets no further. It can be skipped only once your race is over
    /// (<see cref="RaceWeekend.CanSkipToEnd"/>), and skipping it settles the race from where every car is
    /// (<see cref="RaceWeekend.SkipToEnd"/>).
    /// </summary>
    public sealed class CooldownStage : SkippableStage
    {
        public RaceDirector Director;
        public PitCrews Crews;
        /// <summary>Index of your car, or -1 for none.</summary>
        public int Player = -1;
        public RaceCameras Cameras;
        /// <summary>Every car, in the race day's order.</summary>
        public CarController[] Cars;

        private float fieldFlaggedAt = -1f;
        private int shown = -1;

        public override string Name => "cool-down lap";

        public override void Begin() { }

        public override bool Done => Director == null || Director.Finished;

        public override bool Skippable => RaceWeekend.CanSkipToEnd;

        public override void Skip() => RaceWeekend.SkipToEnd();

        private void Update()
        {
            if (Director == null || Director.Finished) return;
            RaceDay day = Director.Day;
            float? since = Director.SinceTimeUp;
            bool field = RaceEnd.FieldFlagged(day, since);
            if (field && fieldFlaggedAt < 0f) fieldFlaggedAt = Time.time;
            Follow(RaceEnd.ShowAfterFlag(day, since));
            bool received = Crews != null && Player >= 0 && Crews.Received(Player);
            if (RaceEnd.CooldownOver(day, since, received, field ? Time.time - fieldFlaggedAt : (float?)null)) Director.FinishNow();
        }

        /// <summary>With your car out of the race, the camera goes to the car the race now follows.</summary>
        private void Follow(int car)
        {
            if (car < 0 || car == Player || car == shown || Cameras == null || Cameras.Held || Cars == null || car >= Cars.Length || Cars[car] == null) return;
            shown = car;
            Cameras.Watch(Cars[car]);
        }
    }
}
