using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit, the sun, a grid of AI cars from every class and the cameras when the scene
    /// starts, and the race begins. I hands the car on pole to you and back. V moves the cameras to
    /// the next car. F steps the race through normal speed, 4x and 16x while the AI drives your car.
    /// Cars away from the camera glide without physics (<see cref="PhysicsRange"/>).
    /// </summary>
    public sealed class CircuitScene : MonoBehaviour
    {
        private KeyboardState keys;
        private AiDriver ai;
        private static readonly float[] Speeds = { 1f, 4f, 16f };
        private int speed;

        private const string PlayerCar = "a-29";
        private const string PlayerModel = "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj";
        /// <summary>The grid, pole first: three of each of the top classes, two of the rest.</summary>
        private static readonly string[] Field =
        {
            "a-29", "a-27", "a-26", "b-24", "b-22", "b-21", "c-19", "c-18",
            "c-16", "d-15", "d-13", "e-09", "e-07", "f-06", "f-03", "f-01",
        };
        /// <summary>Paint for the stand-in boxes, by class, so the classes can be told apart on track.</summary>
        private static readonly Color[] ClassPaint =
        {
            new Color(0.85f, 0.15f, 0.12f), new Color(0.95f, 0.55f, 0.1f), new Color(0.95f, 0.85f, 0.2f),
            new Color(0.25f, 0.7f, 0.3f), new Color(0.2f, 0.45f, 0.9f), new Color(0.6f, 0.3f, 0.8f),
        };

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
            var line = new RacingLine(circuit);
            var rng = new System.Random(11);
            var cars = new CarController[Field.Length];
            var drivers = new AiDriver[Field.Length];
            for (int i = 0; i < Field.Length; i++)
            {
                bool player = i == 0;
                string id = Field[i];
                Color paint = ClassPaint[Mathf.Clamp(id[0] - 'a', 0, ClassPaint.Length - 1)];
                CarController car = CarBuilder.Spawn(id, player ? PlayerModel : null, circuit, player ? keys : null, paint);
                GridSlot slot = circuit.Data.grid.slots[i];
                car.PlaceAt(circuit.IndexOf(slot.d - 2.6f), slot.lateral);   // front wheels just behind the line
                car.gameObject.AddComponent<RaceCar>();
                AiDriver driver = car.gameObject.AddComponent<AiDriver>();
                driver.Line = line;
                driver.Seed = i + 1;
                // Drivers differ: some quicker, some steadier, some more prone to overcooking a corner.
                driver.Pace = 70f + 30f * (float)rng.NextDouble();
                driver.Consistency = 60f + 40f * (float)rng.NextDouble();
                driver.Safety = 80f + 20f * (float)rng.NextDouble();
                if (player) ai = driver;
                cars[i] = car;
                drivers[i] = driver;
            }
            PhysicsRange range = new GameObject("Physics range").AddComponent<PhysicsRange>();
            range.Drivers = drivers;
            range.Player = cars[0];
            Camera(circuit, cars, keys);
        }

        private void Update()
        {
            if (keys == null || ai == null) return;
            if (keys.Pressed(KeyCode.I)) ai.enabled = !ai.enabled;
            if (keys.Pressed(KeyCode.F)) speed = (speed + 1) % Speeds.Length;
            // Fast forward is for watching: taking the wheel drops back to normal speed.
            if (!ai.enabled) speed = 0;
            if (Time.timeScale != Speeds[speed])
            {
                Time.timeScale = Speeds[speed];
                // Let a slow frame catch up with all its physics steps rather than fall behind the clock.
                Time.maximumDeltaTime = Mathf.Max(1f / 3f, Speeds[speed] / 30f);
            }
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

        private static void Camera(Circuit circuit, CarController[] cars, KeyboardState keys)
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
            views.Cars = cars;
            views.Car = cars[0];
            views.Keys = keys;
            views.Hud = cam.gameObject.AddComponent<DriveHud>();
            views.Hud.Car = cars[0];
        }
    }
}
