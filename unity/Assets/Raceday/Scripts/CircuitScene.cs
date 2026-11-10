using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit, the sun, a grid of 100 AI cars from every class, the pace car and the
    /// cameras when the scene starts. It is a test of the AI, so the race starts from the grid at
    /// once; tick Rolling Start for the full game's pace car lap instead. The grid is reversed, so the
    /// quickest car starts last and has to carve through. I hands it to you and back. V moves the
    /// cameras to the next car, and P to the next car making a pit stop. F steps the race through
    /// normal speed, 4x and 16x while the AI drives your car. Cars away from the camera glide
    /// without physics (<see cref="PhysicsRange"/>).
    /// </summary>
    public sealed class CircuitScene : MonoBehaviour
    {
        /// <summary>Starts behind the pace car, as the full game will, instead of straight from the grid.</summary>
        [SerializeField] private bool rollingStart;

        private KeyboardState keys;
        private AiDriver ai;
        private static readonly float[] Speeds = { 1f, 4f, 16f };
        private int speed;

        private const string PlayerCar = "a-29";
        private const string PlayerModel = "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj";
        private const string PaceCarModel = "c-19";
        /// <summary>How many of the field come from each class, fastest class first: 100 cars.</summary>
        private static readonly (char Class, int Count)[] ClassCounts = { ('a', 17), ('b', 17), ('c', 17), ('d', 17), ('e', 16), ('f', 16) };
        /// <summary>
        /// AI cars start with between this share of a tank and a full one, so some need to stop in
        /// the first laps and the pit stops can be seen early.
        /// </summary>
        private const float StartFuelMin = 0.12f;
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
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = part.Material;
                if (!part.CastsShadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (part.Solid)
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    go.AddComponent<TrackSurface>().Kind = TrackSurface.ForPart(part.Name);
                }
            }
            Sun();

            keys = new GameObject("Keyboard").AddComponent<KeyboardState>();
            var line = new RacingLine(circuit);
            var pits = new PitLane(circuit);
            var rng = new System.Random(11);
            string[] field = Field();
            System.Array.Reverse(field);   // slowest on pole, quickest last
            var cars = new CarController[field.Length];
            var grid = new RaceCar[field.Length];
            var drivers = new AiDriver[field.Length];
            RaceStart start = rollingStart ? new GameObject("Race start").AddComponent<RaceStart>() : null;
            for (int i = 0; i < field.Length; i++)
            {
                bool player = i == field.Length - 1;
                string id = field[i];
                Color paint = ClassPaint[Mathf.Clamp(id[0] - 'a', 0, ClassPaint.Length - 1)];
                CarController car = CarBuilder.Spawn(id, player ? PlayerModel : null, circuit, player ? keys : null, paint);
                GridPlace(circuit, i, out float d, out float lateral);
                car.PlaceAt(circuit.IndexOf(d - 2.6f), lateral);   // front wheels just behind the line

                // Drivers differ: some quicker, some steadier, some more prone to overcooking a corner.
                float pace = 70f + 30f * (float)rng.NextDouble();
                float consistency = 60f + 40f * (float)rng.NextDouble();
                float safety = 80f + 20f * (float)rng.NextDouble();
                float smoothness = 30f + 60f * (float)rng.NextDouble();
                car.Supplies = Consumables.For(car.Spec, new SpeedProfile(line, car.Spec, AiPilot.LimitFor(pace)), smoothness);
                if (!player) car.Supplies.Fuel = car.Supplies.Capacity * Mathf.Lerp(StartFuelMin, 1f, (float)rng.NextDouble());

                int garage = i % pits.Garages;
                RaceCar race = car.gameObject.AddComponent<RaceCar>();
                race.Pits = pits;
                race.Garage = garage;
                AiDriver driver = car.gameObject.AddComponent<AiDriver>();
                driver.Line = line;
                driver.Seed = i + 1;
                driver.Pace = pace;
                driver.Consistency = consistency;
                driver.Safety = safety;
                driver.Pits = pits;
                driver.Garage = garage;
                driver.Starter = start;
                if (player) ai = driver;
                cars[i] = car;
                grid[i] = race;
                drivers[i] = driver;
            }
            PhysicsRange range = new GameObject("Physics range").AddComponent<PhysicsRange>();
            range.Drivers = drivers;
            range.Player = ai.Car;

            Camera(circuit, cars, keys);
            if (!rollingStart) return;

            // The pace car waits ahead of pole, in pole's column.
            CarController paceCar = CarBuilder.Spawn(PaceCarModel, null, circuit, null, new Color(0.95f, 0.95f, 0.95f));
            paceCar.name = "Pace car";
            paceCar.PlaceAt(circuit.IndexOf(circuit.Data.grid.slots[0].d + 40f), RollingStart.ColumnOf(0));
            start.Grid = grid;
            start.PaceCar = paceCar;
            start.Pits = pits;
        }

        /// <summary>The field, quickest first: classes fastest first, each cycling through its models.</summary>
        private static string[] Field()
        {
            var field = new List<string> { PlayerCar };
            foreach ((char cls, int count) in ClassCounts)
            {
                var models = new List<string>();
                foreach (TarmacGauntlet.Simulation.CarModel m in TarmacGauntlet.Simulation.CarModels.All)
                    if (m.Id[0] == cls && m.Id[1] == '-') models.Add(m.Id);
                models.Sort((x, y) => string.CompareOrdinal(y, x));
                int have = cls == PlayerCar[0] ? 1 : 0;
                for (int j = 0; have < count; j++)
                {
                    string id = models[j % models.Count];
                    field.Add(id);
                    have++;
                }
            }
            return field.ToArray();
        }

        /// <summary>
        /// A grid place, pole first: the painted slots, then on down the straight at the same
        /// spacing, two by two.
        /// </summary>
        private static void GridPlace(Circuit circuit, int i, out float d, out float lateral)
        {
            GridSlot[] slots = circuit.Data.grid.slots;
            if (i < slots.Length)
            {
                d = slots[i].d;
                lateral = slots[i].lateral;
                return;
            }
            int last = slots.Length - 1;
            float spacing = slots[last - 1].d - slots[last].d;
            d = slots[last].d - (i - last) * spacing;
            lateral = slots[i % 2].lateral;
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
            views.Car = cars[cars.Length - 1];   // the quickest car, at the back
            views.Keys = keys;
            views.Hud = cam.gameObject.AddComponent<DriveHud>();
            views.Hud.Car = cars[cars.Length - 1];
            cam.gameObject.AddComponent<FpsCounter>();
        }
    }
}
