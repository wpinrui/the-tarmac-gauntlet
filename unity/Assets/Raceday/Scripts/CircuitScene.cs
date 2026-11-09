using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit, the sun, the car on pole and the cameras when the scene starts. The AI
    /// drives the car from the start; I hands it to you and back.
    /// </summary>
    public sealed class CircuitScene : MonoBehaviour
    {
        private KeyboardState keys;
        private AiDriver ai;

        private const string PlayerCar = "a-29";
        private const string PlayerModel = "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj";

        private void Awake()
        {
            Time.fixedDeltaTime = 0.01f;
            var circuit = new Circuit(CircuitData.Load());
            var track = new GameObject("Circuit");
            foreach (CircuitPart part in new CircuitBuilder(circuit, new CircuitMaterials()).Build())
            {
                var go = new GameObject(part.Name) { isStatic = true };
                go.transform.SetParent(track.transform, false);
                Mesh mesh = part.Kit.ToMesh(part.Name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = part.Material;
                if (part.Solid)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    go.AddComponent<TrackSurface>().Kind = TrackSurface.ForPart(part.Name);
                }
            }
            Sun();

            keys = new GameObject("Keyboard").AddComponent<KeyboardState>();
            CarController car = CarBuilder.Spawn(PlayerCar, PlayerModel, circuit, keys);
            GridSlot pole = circuit.Data.grid.slots[0];
            car.PlaceAt(circuit.IndexOf(pole.d - 2.6f), pole.lateral);   // front wheels just behind the line
            ai = car.gameObject.AddComponent<AiDriver>();
            ai.Line = new RacingLine(circuit);
            Camera(circuit, car, keys);
        }

        private void Update()
        {
            if (keys != null && ai != null && keys.Pressed(KeyCode.I)) ai.enabled = !ai.enabled;
        }

        private static void Sun()
        {
            var go = new GameObject("Sun");
            Light sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.27f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.7f, 0.76f, 0.84f);
            RenderSettings.fogStartDistance = 1500f;
            RenderSettings.fogEndDistance = 7000f;
            QualitySettings.shadowDistance = 400f;
        }

        private static void Camera(Circuit circuit, CarController car, KeyboardState keys)
        {
            Camera cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 8000f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // Above the grid, behind the start line, looking down the start straight.
            int k = circuit.IndexOf(circuit.Lap - 60f);
            Vector3 p = circuit.Centre[k] + circuit.Left[k] * 18f + Vector3.up * 9f;
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(circuit.Centre[circuit.IndexOf(40f)] - p);
            cam.gameObject.AddComponent<FreeCamera>();
            RaceCameras views = cam.gameObject.AddComponent<RaceCameras>();
            views.Car = car;
            views.Keys = keys;
            cam.gameObject.AddComponent<DriveHud>().Car = car;
        }
    }
}
