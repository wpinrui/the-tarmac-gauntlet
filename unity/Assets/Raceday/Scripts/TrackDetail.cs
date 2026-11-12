using System;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Keeps the circuit's paved surfaces in step with the graphics options while the race runs: tarmac detail, the
    /// rubbered line and kerb wear switch at once when changed in the settings.
    /// </summary>
    public sealed class TrackDetail : MonoBehaviour
    {
        [NonSerialized] public CircuitMaterials Materials;

        private static GraphicsOption[] Options => new[] { GraphicsOptions.TarmacDetail, GraphicsOptions.RubberedLine, GraphicsOptions.KerbWear };

        private void Start()
        {
            foreach (GraphicsOption o in Options) o.Changed += OnOption;
        }

        private void OnDestroy()
        {
            foreach (GraphicsOption o in Options) o.Changed -= OnOption;
        }

        private void OnOption(GraphicsOption option) => Materials?.ShowOptions();
    }
}
