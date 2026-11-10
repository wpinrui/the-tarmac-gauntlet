using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs a start for the AI cars: the rolling start (<see cref="RaceStart"/>) or the pit lane start
    /// (<see cref="PitStart"/>). Each AI car asks it every step how to drive until the start is over.
    /// </summary>
    public abstract class Starter : MonoBehaviour
    {
        /// <summary>Puts an AI car in or out of its start and passes it its speed limit. Called by the car each step.</summary>
        public abstract void Brief(AiDriver d);

        /// <summary>True once this AI car is racing and may make pit stops.</summary>
        public abstract bool Released(AiDriver d);
    }
}
