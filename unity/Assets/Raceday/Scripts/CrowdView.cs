using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Shows or hides the grandstand crowd (every renderer under it drawn with <see cref="Material"/>) with its graphics option, live.</summary>
    public sealed class CrowdView : MonoBehaviour
    {
        public Material Material;

        private readonly List<Renderer> fans = new List<Renderer>();

        private void Start()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial == Material) fans.Add(r);
            GraphicsOptions.Crowd.Changed += Show;
            Show(GraphicsOptions.Crowd);
        }

        private void OnDestroy() => GraphicsOptions.Crowd.Changed -= Show;

        private void Show(GraphicsOption option)
        {
            foreach (Renderer r in fans) r.enabled = option.On;
        }
    }
}
