using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Draws what grows round the race camera each frame, after the camera has moved: the near trees as meshes
    /// (<see cref="NearTrees"/>) and the grass blades (<see cref="Grass"/>), at their graphics options. Sets the wind
    /// they and the tree impostors sway in (Resources/Landscape/Foliage.hlsl): the race's wind, from the west.
    /// </summary>
    public sealed class Foliage : MonoBehaviour
    {
        public NearTrees Trees;
        public Grass Grass;

        private static readonly int WindId = Shader.PropertyToID("_FoliageWind");
        /// <summary>The way the wind blows across the ground: from the west, as the rain is blown (WeatherView).</summary>
        private static readonly Vector2 WindWay = new Vector2(1f, 0.35f).normalized;

        private void LateUpdate()
        {
            Shader.SetGlobalVector(WindId, new Vector4(WindWay.x, WindWay.y, RaceWeather.Now.Wind, Time.timeSinceLevelLoad));
            Camera cam = Camera.main;
            if (cam == null) return;
            Trees?.Draw(cam.transform.position, GraphicsOptions.NearTrees.Value);
            Grass?.Draw(cam, GraphicsOptions.Grass.Value, GraphicsOptions.Mowing.On);
        }

        private void OnDestroy()
        {
            Trees?.Dispose();
            Grass?.Dispose();
        }
    }
}
