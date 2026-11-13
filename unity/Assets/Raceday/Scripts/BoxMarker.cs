using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Your box, marked while you drive in the pit lane: a glowing amber frame on the ground just inside its
    /// painted lines, so it can be found among the garages. Stop in it and the crew work the stop (<see cref="RaceCar"/>).
    /// </summary>
    public sealed class BoxMarker : MonoBehaviour
    {
        public CarController Car;
        public RaceCar Race;
        public PitLane Pits;

        private static readonly Color Amber = new Color(1f, 0.75f, 0.2f);
        private const float Line = 0.3f, Inset = 0.3f, Lift = 0.04f;

        private GameObject frame;
        private Material material;

        private void Start()
        {
            if (Pits == null || Race == null || Race.Garage < 0) return;
            float lane = Pits.BoxLane[Race.Garage];
            Vector3 centre = Pits.BoxPoint(Race.Garage) + Vector3.up * Lift;
            Vector3 across = Pits.LaneLeft(lane);
            var along = new Vector3(across.z, 0f, -across.x);
            // The painted box, less its lines and a little more.
            float depth = Pits.Data.laneWidth - Pits.Data.fastLane - 0.6f - 2f * Inset;
            float length = Pits.Data.garageWidth - 1.2f - 2f * Inset;

            var kit = new MeshKit();
            var size = new Vector3(Line, 0.02f, length);
            kit.Box(centre + across * (depth - Line) * 0.5f, across, along, size);
            kit.Box(centre - across * (depth - Line) * 0.5f, across, along, size);
            size = new Vector3(depth - 2f * Line, 0.02f, Line);
            kit.Box(centre + along * (length - Line) * 0.5f, across, along, size);
            kit.Box(centre - along * (length - Line) * 0.5f, across, along, size);

            frame = new GameObject("Your box");
            frame.transform.SetParent(transform, false);
            frame.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("Your box");
            var renderer = frame.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            material = new Material(Lit.Shader) { name = "Your box", color = Amber };
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.SetColor("_EmissionColor", Amber * 1.5f);
            renderer.sharedMaterial = material;
            frame.SetActive(false);
        }

        private void Update()
        {
            if (frame == null) return;
            // After the flag it shows the way home until the car is parked there for good; a retired car isn't yours to drive.
            bool home = Car != null && Car.Supplies != null && Car.Supplies.RaceOver;
            bool show = Car != null && !Car.AiDriving && Race.InPitLane && (!home || !Car.Supplies.Retired && !Car.Hold);
            if (frame.activeSelf != show) frame.SetActive(show);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
