using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit, the sun, a field of 100 AI cars from every class and the cameras when the
    /// scene starts. It is a test of the AI. The field runs quickest to slowest, except the player's
    /// car, which starts last and has to carve through. Start picks how the race starts: from the
    /// grid at once, behind the pace car, or from the pit lane, where the cars queue in the fast lane
    /// and leave three seconds apart, moving up to the pit exit line (<see cref="PitLaneStart"/>). The pit lane start fast forwards
    /// until the car ahead of the player's is about to leave. I hands the player's car to you and
    /// back. V moves the cameras to the next car, and P to the next car making a pit stop. F steps
    /// the race through normal speed, 4x and 16x while the AI drives your car, and ends the pit lane
    /// start's fast forward. Cars away from the camera glide without physics (<see cref="PhysicsRange"/>).
    /// </summary>
    public sealed class CircuitScene : MonoBehaviour
    {
        public enum StartMode { Grid, Rolling, PitLane }

        /// <summary>
        /// How the race starts: from the grid at once, behind the pace car as the full game will, or
        /// from the pit lane one car at a time.
        /// </summary>
        [SerializeField] private StartMode start = StartMode.PitLane;

        private KeyboardState keys;
        private AiDriver ai;
        private static readonly float[] Speeds = { 1f, 4f, 16f };
        private int speed;
        /// <summary>Speed of the pit lane start's fast forward.</summary>
        private const float SkipSpeed = 32f;
        private PitLaneStart pitLaneStart;
        private bool skipping;

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
            // Grid order, quickest on pole, with the player's car moved from pole to the back.
            string[] quickestFirst = Field();
            var field = new string[quickestFirst.Length];
            System.Array.Copy(quickestFirst, 1, field, 0, quickestFirst.Length - 1);
            field[field.Length - 1] = quickestFirst[0];
            var cars = new CarController[field.Length];
            var grid = new RaceCar[field.Length];
            var drivers = new AiDriver[field.Length];
            if (start == StartMode.PitLane) pitLaneStart = new PitLaneStart(pits, line, field.Length);
            Starter starter = start == StartMode.Rolling ? new GameObject("Race start").AddComponent<RaceStart>()
                : start == StartMode.PitLane ? new GameObject("Pit lane start").AddComponent<PitStart>()
                : (Starter)null;
            for (int i = 0; i < field.Length; i++)
            {
                bool player = i == field.Length - 1;
                string id = field[i];
                Color paint = ClassPaint[Mathf.Clamp(id[0] - 'a', 0, ClassPaint.Length - 1)];
                CarController car = CarBuilder.Spawn(id, player ? PlayerModel : null, circuit, player ? keys : null, paint);
                if (pitLaneStart != null) car.PlaceAt(pitLaneStart.Place(i, out Vector3 forward), forward);
                else
                {
                    GridPlace(circuit, i, out float d, out float lateral);
                    car.PlaceAt(circuit.IndexOf(d - 2.6f), lateral);   // front wheels just behind the line
                }

                // Drivers differ: some quicker, some steadier, some more prone to overcooking a corner.
                float pace = 70f + 30f * (float)rng.NextDouble();
                float consistency = 60f + 40f * (float)rng.NextDouble();
                float safety = 80f + 20f * (float)rng.NextDouble();
                float smoothness = 30f + 60f * (float)rng.NextDouble();
                car.Supplies = Consumables.For(car.Spec, new SpeedProfile(line, car.Spec, AiPilot.LimitFor(pace)), smoothness);
                float share = Mathf.Lerp(StartFuelMin, 1f, (float)rng.NextDouble());
                // From the pit lane every car starts full: the pits stay closed until the last car is out.
                if (!player && pitLaneStart == null) car.Supplies.Fuel = car.Supplies.Capacity * share;

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
                driver.Starter = starter;
                if (player) ai = driver;
                cars[i] = car;
                grid[i] = race;
                drivers[i] = driver;
            }
            PhysicsRange range = new GameObject("Physics range").AddComponent<PhysicsRange>();
            range.Drivers = drivers;
            range.Player = ai.Car;

            Camera(circuit, cars, keys);
            if (start == StartMode.PitLane)
            {
                var pitStart = (PitStart)starter;
                pitStart.Grid = grid;
                pitStart.Queue = pitLaneStart;
                pitStart.Pits = pits;
                skipping = true;
            }
            if (start != StartMode.Rolling) return;

            // The pace car waits ahead of pole, in pole's column.
            CarController paceCar = CarBuilder.Spawn(PaceCarModel, null, circuit, null, new Color(0.95f, 0.95f, 0.95f));
            paceCar.name = "Pace car";
            paceCar.PlaceAt(circuit.IndexOf(circuit.Data.grid.slots[0].d + 40f), RollingStart.ColumnOf(0));
            var rolling = (RaceStart)starter;
            rolling.Grid = grid;
            rolling.PaceCar = paceCar;
            rolling.Pits = pits;
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
            if (keys.Pressed(KeyCode.F))
            {
                if (skipping) skipping = false;
                else speed = (speed + 1) % Speeds.Length;
            }
            // Fast forward is for watching: taking the wheel drops back to normal speed.
            if (!ai.enabled)
            {
                speed = 0;
                skipping = false;
            }
            if (skipping && pitLaneStart.Time >= pitLaneStart.FastForwardTo) skipping = false;
            float scale = skipping ? SkipSpeed : Speeds[speed];
            if (Time.timeScale != scale)
            {
                Time.timeScale = scale;
                // Let a slow frame catch up with all its physics steps rather than fall behind the clock.
                Time.maximumDeltaTime = Mathf.Max(1f / 3f, scale / 30f);
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
            views.Car = cars[cars.Length - 1];   // the player's car, at the back
            views.Keys = keys;
            views.Hud = cam.gameObject.AddComponent<DriveHud>();
            views.Hud.Car = cars[cars.Length - 1];
            cam.gameObject.AddComponent<FpsCounter>();
        }
    }
}
