using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the circuit, the country round it (<see cref="Landscape"/>), the trackside furniture (<see cref="Trackside"/>), the pit crews (<see cref="PitCrews"/>), the sky and its sun (<see cref="TimeOfDay"/>), the race's weather (<see cref="WeatherPlan"/>), the floodlights
    /// that come on at night (<see cref="NightLights"/>), the career's field and the cameras when the scene starts. The
    /// field is every team with a car, from <see cref="RaceWeekend.Setup"/>, gridded by prestige: each
    /// car on a full tank and new tyres, its starting driver at the wheel. Run on its own, the scene
    /// makes a new career in which the player owns the Cheonma. The <see cref="RaceDirector"/> keeps
    /// the race for the career and settles it at the flag. Start picks how the race starts: from the
    /// grid at once, behind the pace car, or from the pit lane, where the cars queue in the fast lane
    /// and leave three seconds apart, moving up to the pit exit line (<see cref="PitLaneStart"/>). The pit lane start fast forwards
    /// until the car ahead of the player's is about to leave. In the editor and development builds Tab opens the debug menu (<see cref="DebugMenu"/>), whose race set-up (<see cref="DebugSettings"/>) picks the start, the weather and the start hour. Delete hands the player's car to you and
    /// back. = rides with the car behind the one you watch, - with the car ahead, and P with the next car making a pit stop. F steps
    /// the race through normal speed, 4x and 16x while the AI drives your car, and ends the pit lane
    /// start's fast forward. Cars away from the camera glide without physics (<see cref="PhysicsRange"/>).
    /// The field is shown on the timing tower (<see cref="RaceTower"/>). T opens live timing while the AI drives
    /// your car, with the controls for its next stop (<see cref="LiveTiming"/>). While a teammate drives, N
    /// (or the button beside live timing's way back) fast forwards at 32x to your next stint and hands you the
    /// wheel once you are back on track. A stop that puts a teammate in hands the car back to the AI.
    /// </summary>
    public sealed class CircuitScene : MonoBehaviour
    {
        public enum StartMode { Grid, Rolling, PitLane }

        /// <summary>
        /// How the race starts: from the grid at once, behind the pace car as the full game will, or
        /// from the pit lane one car at a time.
        /// </summary>
        [SerializeField] private StartMode start = StartMode.Rolling;

        /// <summary>Seed for the race's weather; 0 rolls a new one each race.</summary>
        [SerializeField] private int weatherSeed;

        private KeyboardState keys;
        private AiDriver ai;
        private static readonly float[] Speeds = { 1f, 4f, 16f };
        private int speed;
        /// <summary>Speed of the pit lane start's fast forward.</summary>
        private const float SkipSpeed = 32f;
        private PitLaneStart pitLaneStart;
        private bool skipping;
        private SituationMenu lab;
        private DebugMenu debug;
        private RaceDay day;
        private RaceDirector director;
        private LiveTiming timing;
        /// <summary>The player's car in the race, or -1 for none.</summary>
        private int playerIndex = -1;
        private RaceCar[] racingCars;
        /// <summary>True while the race fast forwards to your next stint.</summary>
        private bool stintSkip;
        private string lastDriver;

        private const string PlayerCar = "a-29";
        private const string PaceCarModel = "c-19";
        /// <summary>The player's team name when the scene runs on its own.</summary>
        private const string PlayerTeam = "Your Team";
        /// <summary>Paint by class, so the classes can be told apart on track.</summary>
        private static readonly Color[] ClassPaint =
        {
            new Color(0.85f, 0.15f, 0.12f), new Color(0.95f, 0.55f, 0.1f), new Color(0.95f, 0.85f, 0.2f),
            new Color(0.25f, 0.7f, 0.3f), new Color(0.2f, 0.45f, 0.9f), new Color(0.6f, 0.3f, 0.8f),
        };

        /// <summary>A model's paint, by its class.</summary>
        internal static Color PaintFor(string id) => ClassPaint[Mathf.Clamp(id[0] - 'a', 0, ClassPaint.Length - 1)];

        private void Awake()
        {
            Time.fixedDeltaTime = 0.01f;
            RaceWeather.Now = WeatherState.Dry;
            RaceWeather.Forced = null;
            bool testing = Debug.isDebugBuild;
            TimeOfDay.StartHour = testing ? DebugSettings.StartHour : TimeOfDay.DefaultStartHour;
            if (testing && DebugSettings.Start is StartMode chosen) start = chosen;
            var circuit = new Circuit(CircuitData.Load());
            var track = new GameObject("Circuit");
            var materials = new CircuitMaterials();
            var builder = new CircuitBuilder(circuit, materials);
            List<CircuitPart> parts = builder.Build();
            var standMaterials = new StandMaterials(StandLook.Colours);
            var stands = new Grandstands(circuit, builder, standMaterials, StandLook.Main, StandLook.Corners);
            parts.AddRange(stands.Build());
            foreach (CircuitPart part in parts) AddPart(track.transform, part);
            Landscape.Build(circuit, builder, materials, LandscapeLooks.Current);
            Complex = new PitComplex(circuit, builder.GarageFloors);
            var pitMaterials = new PitMaterials();
            var buildings = new GameObject("Pit complex");
            foreach (CircuitPart part in new PitComplexBuilder(Complex, pitMaterials).Build())
                AddPart(buildings.transform, part);
            TimeOfDay sky = new GameObject("Sky").AddComponent<TimeOfDay>();
            PitComplexView view = buildings.AddComponent<PitComplexView>();
            view.Sky = sky;
            view.Materials = pitMaterials;
            Trackside trackside = Trackside.Build(circuit, builder, stands.Footprints);
            var floodMaterials = new FloodMaterials();
            var floods = new Floodlights(circuit, Complex, stands.RoofLamps, builder.PitWallRuns, parts, GantrySpan.Of(circuit, trackside.Placed));
            var lit = new GameObject("Floodlights");
            foreach (CircuitPart part in floods.Build(floodMaterials.Steel, floodMaterials.Lens, floodMaterials.Pool))
                AddPart(lit.transform, part);
            NightLights night = lit.AddComponent<NightLights>();
            night.Sky = sky;
            night.Layout = floods;
            night.Materials = floodMaterials;
            night.Stands = standMaterials;

            keys = new GameObject("Keyboard").AddComponent<KeyboardState>();
            var line = new RacingLine(circuit);
            var pits = new PitLane(circuit);
            bool career = RaceWeekend.Setup != null;
            RaceSetup setup = RaceWeekend.Setup ?? RaceWeekend.Begin(TestCareer());
            day = new RaceDay(setup);
            IReadOnlyList<GridCar> grid = setup.Grid;
            int player = -1;
            for (int i = 0; i < grid.Count; i++) if (grid[i].Player) player = i;
            // With no car of the player's in the race, the cameras start on the last car.
            int watched = player >= 0 ? player : grid.Count - 1;
            var field = new string[grid.Count];
            var teams = new string[grid.Count];
            for (int i = 0; i < grid.Count; i++) (field[i], teams[i]) = (grid[i].Model.Id, grid[i].TeamName);
            var cars = new CarController[field.Length];
            var racing = new RaceCar[field.Length];
            var drivers = new AiDriver[field.Length];
            if (start == StartMode.PitLane) pitLaneStart = new PitLaneStart(pits, line, field.Length);
            Starter starter = start == StartMode.Rolling ? new GameObject("Race start").AddComponent<RaceStart>()
                : start == StartMode.PitLane ? new GameObject("Pit lane start").AddComponent<PitStart>()
                : (Starter)null;
            for (int i = 0; i < field.Length; i++)
            {
                bool mine = i == player;
                string id = field[i];
                Color paint = PaintFor(id);
                CarController car = CarBuilder.Spawn(id, circuit, mine ? keys : null, paint, grid[i].TeamName, grid[i].RaceNumber);
                if (pitLaneStart != null) car.PlaceAt(pitLaneStart.Place(i, out Vector3 forward), forward);
                else
                {
                    GridPlace(circuit, i, out float d, out float lateral);
                    car.PlaceAt(circuit.IndexOf(d - 2.6f), lateral);   // front wheels just behind the line
                }

                // The career car's stats, worn by age and condition and lifted by upgrades, set its fuel, tyres and stops.
                CarStats stats = EffectiveStats.CalculateEffectiveStats(grid[i].Car, grid[i].Model);
                CarStats model = grid[i].Model.BaseStats;
                car.Spec.Stats = stats;
                DriverStats starter0 = grid[i].Lineup[0].Stats;
                float pace = (float)starter0.Pace;
                car.Supplies = Consumables.For(car.Spec, new SpeedProfile(line, car.Spec, AiPilot.LimitFor(pace)), (float)starter0.Smoothness);
                car.Supplies.Log = day.Cars[i];
                car.Supplies.PowerTrim = model.Power > 0 ? (float)(stats.Power / model.Power) : 1f;
                car.Supplies.GripTrim = (float)((1.0 + 0.006 * stats.Handling) / (1.0 + 0.006 * model.Handling));
                // Every car starts on a full tank and new tyres.
                car.Supplies.Fuel = Mathf.Min(car.Supplies.Capacity, (float)grid[i].StartingFuel);

                int garage = i % pits.Garages;
                RaceCar race = car.gameObject.AddComponent<RaceCar>();
                race.Pits = pits;
                race.Garage = garage;
                AiDriver driver = car.gameObject.AddComponent<AiDriver>();
                driver.Line = line;
                driver.Seed = i + 1;
                driver.Pace = pace;
                driver.Consistency = (float)starter0.Consistency;
                driver.Safety = (float)starter0.Safety;
                driver.Pits = pits;
                driver.Garage = garage;
                driver.Starter = starter;
                if (i == watched) ai = driver;
                cars[i] = car;
                racing[i] = race;
                drivers[i] = driver;
            }
            playerIndex = player;
            racingCars = racing;
            PhysicsRange range = new GameObject("Physics range").AddComponent<PhysicsRange>();
            range.Drivers = drivers;
            range.Player = ai.Car;

            RaceCameras views = Camera(circuit, cars, watched, keys);
            BroadcastDirector tv = views.gameObject.AddComponent<BroadcastDirector>();
            tv.Circuit = circuit;
            tv.Cars = racing;
            tv.Drivers = drivers;
            tv.Player = player;
            views.Director = tv;
            // Run on its own the scene is for AI testing, so it starts on the chase camera.
            if (!career) views.StartInChase();
            RaceTower tower = Tower(circuit, racing, field, teams, watched, starter);
            views.Tower = tower;
            if (player >= 0 && TeamColour(setup.Game, grid[player].TeamId) is Color own) tower.Colours[player] = own;
            view.Fit(Complex, tower.Colours, pits.Garages);
            // Each team's hired crew wait in its garage and work its stops.
            var crewSizes = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++) crewSizes[i] = grid[i].CrewSize;
            PitCrews crews = new GameObject("Pit crews").AddComponent<PitCrews>();
            crews.Build(Complex, pits, racing, drivers, tower.Colours, crewSizes);
            // The crews handle the kit at their stops: the fuel hose, the guns, the tyres, the jacks and the lollipop.
            crews.gameObject.AddComponent<PitKitWork>().Build(Complex, crews, view.Kit, racing);
            tv.Classes = tower.Classes;
            sky.RaceSeconds = () => tower.RaceTime;
            // Every race rolls its own weather (WeatherPlan), played on the race clock.
            int seed = weatherSeed != 0 ? weatherSeed : new System.Random().Next(1, int.MaxValue);
            var weather = new WeatherPlan(seed, TimeOfDay.StartHour, testing ? DebugSettings.Weather : null);
            Debug.Log($"Weather {seed}: {weather.Kind}");
            WeatherView rain = sky.gameObject.AddComponent<WeatherView>();
            rain.Plan = weather;
            rain.RaceSeconds = () => tower.RaceTime;
            rain.Materials = materials;
            rain.Cars = racing;
            rain.Night = () => SkyClock.At(sky.Hour, SkyClock.Look).Night;
            sky.Weather = () => RaceWeather.Forced ?? weather.At(tower.RaceTime);
            trackside.Cars = racing;
            trackside.Racing = tower.Racing;
            if (starter is RaceStart formation) trackside.PaceCarOut = () => !formation.Racing;
            director = new GameObject("Race director").AddComponent<RaceDirector>();
            director.Day = day;
            director.Cars = racing;
            director.Drivers = drivers;
            director.Tower = tower;
            var hidden = new List<GameObject> { tower.gameObject, director.gameObject };
            if (player >= 0)
            {
                timing = new GameObject("Live timing").AddComponent<LiveTiming>();
                timing.Cars = racing;
                timing.Drivers = drivers;
                timing.Teams = teams;
                timing.Classes = tower.Classes;
                timing.Colours = tower.Colours;
                timing.Player = player;
                timing.Lap = circuit.Lap;
                timing.Circuit = circuit;
                timing.Driver = ai;
                timing.Tower = tower;
                timing.Hud = views.Hud;
                timing.Keys = keys;
                timing.Game = setup.Game;
                timing.CanSkip = CanSkip;
                timing.Skip = () => stintSkip = true;
                hidden.Add(timing.gameObject);
            }
            BroadcastGraphics graphics = new GameObject("Broadcast graphics").AddComponent<BroadcastGraphics>();
            graphics.Director = tv;
            graphics.Cars = racing;
            graphics.Drivers = drivers;
            graphics.Teams = teams;
            graphics.Classes = tower.Classes;
            graphics.Colours = tower.Colours;
            graphics.Models = new string[grid.Count];
            for (int i = 0; i < grid.Count; i++) graphics.Models[i] = grid[i].Model.Name;
            graphics.Day = day;
            graphics.Game = setup.Game;
            graphics.Circuit = circuit;
            graphics.Player = player;
            graphics.Tower = tower;
            graphics.Keys = keys;
            hidden.Add(graphics.gameObject);
            foreach (CarController c in cars) hidden.Add(c.gameObject);
            if (starter != null) hidden.Add(starter.gameObject);
            if (testing)
            {
                var menu = new GameObject("Debug menu");
                lab = menu.AddComponent<SituationMenu>();
                lab.Circuit = circuit;
                lab.Keys = keys;
                lab.Cameras = views;
                lab.Range = range;
                lab.Race = hidden.ToArray();
                debug = menu.AddComponent<DebugMenu>();
                debug.Keys = keys;
                debug.Cameras = views;
                debug.Tower = tower;
                debug.Director = director;
                debug.Lab = lab;
                debug.Game = setup.Game;
            }
            if (start == StartMode.PitLane)
            {
                var pitStart = (PitStart)starter;
                pitStart.Grid = racing;
                pitStart.Queue = pitLaneStart;
                pitStart.Pits = pits;
                skipping = true;
            }
            if (start != StartMode.Rolling) return;

            // The pace car waits ahead of pole, in pole's column.
            CarController paceCar = CarBuilder.Spawn(PaceCarModel, circuit, null, new Color(0.95f, 0.95f, 0.95f));
            paceCar.name = "Pace car";
            paceCar.PlaceAt(circuit.IndexOf(circuit.Data.grid.slots[0].d + 40f), RollingStart.ColumnOf(0));
            var rolling = (RaceStart)starter;
            rolling.Grid = racing;
            rolling.PaceCar = paceCar;
            hidden.Add(paceCar.gameObject);
            if (lab != null) lab.Race = hidden.ToArray();
            rolling.Pits = pits;
        }

        /// <summary>The pit complex's layout: garages, the pit building's stations and where the crews stand.</summary>
        public PitComplex Complex { get; private set; }

        private static void AddPart(Transform parent, CircuitPart part)
        {
            var go = new GameObject(part.Name) { isStatic = true };
            go.transform.SetParent(parent, false);
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

        /// <summary>The team's own colour, picked at new game, or null for a team without one.</summary>
        private static Color? TeamColour(GameState game, string teamId)
        {
            foreach (BaseTeam t in game.Teams)
                if (t.Id == teamId && t is PlayerTeam p && ColorUtility.TryParseHtmlString(p.TeamColour, out Color c)) return c;
            return null;
        }

        /// <summary>
        /// A new career for running the scene on its own, without the menus: the player owns the car
        /// the scene has a model for, drives it alone, and has a crew of eight and a dozen tyre sets and spares.
        /// </summary>
        private static GameState TestCareer()
        {
            GameState game = GameInit.InitializeGame(new NewGameOptions { TeamName = PlayerTeam, Skills = new PlayerSkills { Driver = 16 } });
            game = GameActions.BuyCar(game, new CarInstance { Id = "test-car", ModelId = PlayerCar, Condition = 100 }, 0, "");
            game = GameActions.BuyTyres(game, 12, 0);
            game = GameActions.BuySpares(game, 12, 0);
            return GameActions.SetCrewSize(game, 8);
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
            if (keys == null || ai == null || lab != null && lab.Running) return;   // a debug set-up runs instead of the race
            if (director.Finished) return;   // the race summary is up, at normal speed
            if (keys.Pressed(KeyCode.Delete)) ai.enabled = !ai.enabled;
            if (keys.Pressed(KeyCode.F))
            {
                if (skipping) skipping = false;
                else if (stintSkip) stintSkip = false;
                else speed = (speed + 1) % Speeds.Length;
            }
            if (keys.Pressed(KeyCode.N) && CanSkip()) stintSkip = true;
            Stints();
            // Fast forward is for watching: taking the wheel drops back to normal speed.
            if (!ai.enabled)
            {
                speed = 0;
                skipping = false;
                stintSkip = false;
            }
            if (skipping && pitLaneStart.Time >= pitLaneStart.FastForwardTo) skipping = false;
            if (debug != null && debug.Speed.HasValue) return;   // the debug menu holds the speed
            // Live timing runs the race at the top speed behind it.
            float scale = skipping || stintSkip ? SkipSpeed : timing != null && timing.Open ? Speeds[Speeds.Length - 1] : Speeds[speed];
            if (Time.timeScale != scale)
            {
                Time.timeScale = scale;
                // Let a slow frame catch up with all its physics steps rather than fall behind the clock.
                Time.maximumDeltaTime = Mathf.Max(1f / 3f, scale / 30f);
            }
        }

        /// <summary>
        /// True while a teammate drives your car and you have a stint to come: the race can skip to it.
        /// </summary>
        private bool CanSkip()
        {
            if (playerIndex < 0 || !ai.enabled || director.Finished) return false;
            CarRaceLog mine = day.Cars[playerIndex];
            if (mine.Retired || mine.Driver.Id == RaceEntry.PlayerCharacterDriverId) return false;
            foreach (RaceDriver d in mine.Car.Lineup) if (d.Id == RaceEntry.PlayerCharacterDriverId) return true;
            return false;
        }

        /// <summary>
        /// Hands your car over as the drivers change. Skipping to your stint ends once the stop that puts
        /// you in is over and the car is back on track: you take the wheel. A stop that puts a teammate in
        /// while you drive gives the car to the AI once you have driven out of the pit lane.
        /// </summary>
        private void Stints()
        {
            if (playerIndex < 0) return;
            CarRaceLog mine = day.Cars[playerIndex];
            bool you = mine.Driver.Id == RaceEntry.PlayerCharacterDriverId;
            if (stintSkip && you && !ai.Pitting && !racingCars[playerIndex].InPitLane)
            {
                stintSkip = false;
                speed = 0;
                ai.enabled = false;
            }
            if (lastDriver == RaceEntry.PlayerCharacterDriverId && !you && !ai.enabled)
            {
                if (racingCars[playerIndex].InPitLane) return;   // drive out first: lastDriver stays yours until then
                ai.enabled = true;
            }
            lastDriver = mine.Driver.Id;
        }

        /// <summary>The timing tower, its clock running from the green, or from the first car out of the pit lane.</summary>
        private RaceTower Tower(Circuit circuit, RaceCar[] grid, string[] field, string[] teams, int player, Starter starter)
        {
            RaceTower tower = new GameObject("Timing tower").AddComponent<RaceTower>();
            tower.Cars = grid;
            tower.Teams = teams;
            tower.Classes = new TarmacGauntlet.Simulation.CarClass[field.Length];
            tower.Colours = new Color[field.Length];
            var paint = new System.Random(23);
            for (int i = 0; i < field.Length; i++)
            {
                string id = field[i];
                tower.Classes[i] = System.Linq.Enumerable.First(TarmacGauntlet.Simulation.CarModels.All, m => m.Id == id).CarClass;
                tower.Colours[i] = RaceTower.TeamColours[paint.Next(RaceTower.TeamColours.Length)];
            }
            tower.Player = player;
            tower.Lap = circuit.Lap;
            if (starter is RaceStart rolling) tower.Racing = () => rolling.Racing;
            else if (pitLaneStart != null) tower.Racing = () => pitLaneStart.Gone(0);
            return tower;
        }

        private static RaceCameras Camera(Circuit circuit, CarController[] cars, int watched, KeyboardState keys)
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
            RaceCameras views = cam.gameObject.AddComponent<RaceCameras>();
            views.Cars = cars;
            views.Car = cars[watched];   // the player's car
            views.Keys = keys;
            views.Hud = cam.gameObject.AddComponent<DriveHud>();
            views.Hud.Car = cars[watched];
            cam.gameObject.AddComponent<FpsCounter>();
            return views;
        }
    }
}
