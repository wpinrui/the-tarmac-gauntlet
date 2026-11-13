using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// One stage of a <see cref="StageSequence"/>: the TV preview, the grid, the drive out of the pit lane, the end
    /// of the formation lap, and after the flag the cooldown lap and the podium. The sequence runs its stages in
    /// order. A stage is switched off until its turn, switched on just before <see cref="Begin"/>, and left to
    /// itself once it is <see cref="Done"/>. A stage owns everything it shows while it runs, the camera included,
    /// and gives it back when it is done or skipped.
    /// </summary>
    public abstract class SkippableStage : MonoBehaviour
    {
        /// <summary>The stage in lower case, as the hint ends: "to skip formation lap".</summary>
        public abstract string Name { get; }

        /// <summary>Starts the stage. Called once, on its turn.</summary>
        public abstract void Begin();

        /// <summary>True once the stage is over, by itself or by <see cref="Skip"/>. The next stage starts on the next frame.</summary>
        public abstract bool Done { get; }

        /// <summary>Jumps to the stage's end state at once: <see cref="Done"/> is true after it. Called when a hold of Enter fills.</summary>
        public abstract void Skip();

        /// <summary>False while the stage can't be skipped: the hint is hidden and holding Enter does nothing.</summary>
        public virtual bool Skippable => true;
    }
}
