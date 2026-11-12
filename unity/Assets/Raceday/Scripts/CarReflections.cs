using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What the cars reflect: a reflection probe at the watched car that draws the scene round it without the cars. It
    /// draws one face a frame and starts again as soon as it is done, so the reflections follow the car, the sky and the
    /// time of day every few frames. It covers every car in sight. Without it (<see cref="GraphicsOptions.CarReflections"/> off) the
    /// cars reflect the sky alone. Sits on the race camera beside <see cref="RaceCameras"/>.
    /// </summary>
    [RequireComponent(typeof(RaceCameras))]
    public sealed class CarReflections : MonoBehaviour
    {
        /// <summary>A face's size, pixels.</summary>
        private const int Resolution = 128;
        /// <summary>How far round the car it reaches, and how far it sees, m.</summary>
        private const float Reach = 4000f, Sight = 1500f;
        /// <summary>The probe's height above the car's origin: about the middle of its body, m.</summary>
        private const float Height = 0.8f;

        private RaceCameras views;
        private ReflectionProbe probe;
        private int rendering = -1;
        /// <summary>True until the probe has been drawn once since it was put on: that one is drawn whole, at once.</summary>
        private bool first;

        private void Awake()
        {
            views = GetComponent<RaceCameras>();
            var go = new GameObject("Car reflections");
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.resolution = Resolution;
            probe.hdr = false;
            probe.size = Vector3.one * Reach;
            probe.boxProjection = false;
            probe.importance = 100;
            probe.cullingMask = ~(1 << CarBuilder.CarLayer);
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = Sight;
            GraphicsOptions.CarReflections.Changed += Toggled;
            Toggled(GraphicsOptions.CarReflections);
        }

        private void OnDestroy()
        {
            GraphicsOptions.CarReflections.Changed -= Toggled;
            if (probe != null) Destroy(probe.gameObject);
        }

        private void Toggled(GraphicsOption option)
        {
            probe.enabled = option.On;
            rendering = -1;
            first = true;
        }

        private void LateUpdate()
        {
            if (!probe.enabled) return;
            if (rendering >= 0 && !probe.IsFinishedRendering(rendering)) return;
            CarController car = views.Car;
            Vector3 at = car != null ? car.transform.position + Vector3.up * Height : transform.position;
            probe.transform.position = at;
            probe.timeSlicingMode = first ? ReflectionProbeTimeSlicingMode.NoTimeSlicing : ReflectionProbeTimeSlicingMode.IndividualFaces;
            first = false;
            rendering = probe.RenderProbe();
        }
    }
}
