using System.Collections;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
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
    /// until the car ahead of the player's is about to leave. Tab opens the debug menu (<see cref="DebugMenu"/>), whose race set-up (<see cref="DebugSettings"/>) picks the start, the weather, the start hour, the field and your car. You drive your own car from the green when you start the race, and Delete hands it to the AI and
    /// back. = rides with the car behind the one you watch and - with the car ahead. While you drive, P opens the plan for your next stop (<see cref="PitPanel"/>), and your box is marked once you are in the pit lane (<see cref="BoxMarker"/>). F steps
    /// the race through normal speed, 4x and 16x while the AI drives your car, and ends the pit lane
    /// start's fast forward. Cars away from the camera glide without physics (<see cref="PhysicsRange"/>).
    /// The field is shown on the timing tower (<see cref="RaceTower"/>). T opens live timing while the AI drives
    /// your car, with the controls for its next stop (<see cref="LiveTiming"/>). While a teammate drives, N
    /// (or the button beside live timing's way back) fast forwards at 32x to your next stint and hands you the
    /// wheel once you are back on track. A stop that puts a teammate in hands the car back to the AI. Past the
    /// chequered flag the AI takes your car round its cooldown lap and into its box, and Delete still takes it back.
    /// A race saved part way (<see cref="RaceWeekend.Resuming"/>) carries on from the save: every car where it was, on its
    /// lap, fuel and tyres, the clocks and the weather as they were. A car that was in the pit lane goes back on track
    /// beside where it was, at the edge on the pit side. Saved before the green, the race starts again. The stages before the
    /// green and after the flag run in turn (<see cref="StageSequence"/>), and holding Enter skips the one on screen.
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
        private DebugMenu debug;
        private RaceDay day;
        private RaceDirector director;
        /// <summary>The stages before the green (<see cref="StageSequence"/>), played once the scene is built.</summary>
        private StageSequence intro;
        /// <summary>The stages after the chequered flag, played once the race's time is up.</summary>
        private StageSequence outro;
        private LiveTiming timing;
        /// <summary>The player's car in the race, or -1 for none.</summary>
        private int playerIndex = -1;
        private RaceCar[] racingCars;
        /// <summary>True while the race fast forwards to your next stint.</summary>
        private bool stintSkip;
        private string lastDriver;
        /// <summary>True once the race has handed you the wheel at the start.</summary>
        private bool handedOver;
        /// <summary>True once the AI has been given your car for its cooldown lap.</summary>
        private bool cooldownHanded;
        private RaceTower raceTower;
        private CarController[] carControllers;
        private Circuit circuit;
        private int weatherRoll;
        private WeatherKind? weatherKind;

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

        /// <summary>Cars spawned a frame while the scene builds, so the loading screen keeps moving.</summary>
        private const int CarsPerFrame = 3;
        /// <summary>True once the scene is built and the race can run.</summary>
        private bool built;
        private readonly System.Diagnostics.Stopwatch buildWatch = new System.Diagnostics.Stopwatch();
        private readonly List<(string Stage, long Ms)> buildTimes = new List<(string, long)>();

        // The scene builds over several frames behind the loading screen (SceneLoader), which stays up until it is done.
        private void Awake()
        {
            SceneLoader.Hold();
            StartCoroutine(Build());
        }

        /// <summary>Notes how long a stage of the build took, adding to the stage's time if it ran before.</summary>
        private void Lap(string stage)
        {
            long ms = buildWatch.ElapsedMilliseconds;
            buildWatch.Restart();
            int at = buildTimes.FindIndex(t => t.Stage == stage);
            if (at >= 0) buildTimes[at] = (stage, buildTimes[at].Ms + ms);
            else buildTimes.Add((stage, ms));
        }

        private IEnumerator Build()
        {
            buildWatch.Start();
            Time.fixedDeltaTime = 0.01f;
            RaceWeather.Now = WeatherState.Dry;
            RaceWeather.Forced = null;
            RaceSave resume = RaceWeekend.Resuming;
            if (resume != null && (RaceWeekend.Setup == null || resume.Cars.Count != resume.Setup.Grid.Count || resume.Day.Cars.Count != resume.Setup.Grid.Count))
                resume = null;
            TimeOfDay.StartHour = resume != null ? resume.StartHour : DebugSettings.StartHour;
            if (DebugSettings.Start is StartMode chosen) start = chosen;
            bool resumed = resume != null && resume.Started;
            if (resumed) start = StartMode.Grid;   // no start: the cars are put where they were
            circuit = new Circuit(CircuitData.Load());
            var track = new GameObject("Circuit");
            var materials = new CircuitMaterials();
            var builder = new CircuitBuilder(circuit, materials);
            List<CircuitPart> parts = builder.Build();
            Lap("circuit");
            yield return null;
            var standMaterials = new StandMaterials(StandLook.Colours);
            var stands = new Grandstands(circuit, builder, standMaterials, StandLook.Main, StandLook.Corners);
            parts.AddRange(stands.Build());
            foreach (CircuitPart part in parts) AddPart(track.transform, part);
            track.AddComponent<CrowdView>().Material = standMaterials.Crowd;
            Lap("grandstands");
            yield return null;
            IReadOnlyList<TreeSpot> trees = Landscape.Build(circuit, builder, stands.Footprints, materials, LandscapeLooks.Current);
            Lap("landscape");
            yield return null;
            Complex = new PitComplex(circuit, builder.GarageFloors);
            var pitMaterials = new PitMaterials();
            var buildings = new GameObject("Pit complex");
            foreach (CircuitPart part in new PitComplexBuilder(Complex, pitMaterials).Build())
                AddPart(buildings.transform, part);
            Lap("pit complex");
            yield return null;
            TimeOfDay sky = new GameObject("Sky").AddComponent<TimeOfDay>();
            float ground = 0f;
            foreach (Vector3 c in circuit.Centre) ground += c.y;
            sky.Ground = circuit.Centre.Length > 0 ? ground / circuit.Centre.Length : 0f;
            PitComplexView view = buildings.AddComponent<PitComplexView>();
            view.Sky = sky;
            view.Materials = pitMaterials;
            Trackside trackside = Trackside.Build(circuit, builder, stands.Footprints, trees);
            var plainArmco = new List<Renderer>();
            foreach (Transform child in track.transform)
                if (child.name == "Armco" && child.TryGetComponent(out Renderer drawn)) plainArmco.Add(drawn);
            trackside.SetPlainArmco(plainArmco.ToArray());
            Lap("trackside");
            yield return null;
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
            Lap("floodlights");
            yield return null;

            keys = new GameObject("Keyboard").AddComponent<KeyboardState>();
            var line = new RacingLine(circuit);
            materials.LayRubber(circuit, line);
            track.AddComponent<TrackDetail>().Materials = materials;
            Lap("racing line");
            yield return null;
            var pits = new PitLane(circuit);
            bool career = RaceWeekend.Setup != null;
            RaceSetup setup = RaceWeekend.Setup ?? RaceWeekend.Begin(TestCareer());
            if (resume == null) setup = DebugActions.Field(setup, DebugSettings.Field, DebugSettings.Car);
            day = new RaceDay(setup);
            if (resumed) day.Load(resume.Day);
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
            if (starter != null) starter.enabled = false;   // its start waits for the whole field
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
                // Built cars wait switched off, out of the physics, until the whole field is on the grid.
                car.gameObject.SetActive(false);
                if ((i + 1) % CarsPerFrame == 0 && i + 1 < field.Length)
                {
                    Lap("cars");
                    yield return null;
                }
            }
            // The rest is wired up in this frame, so the start and the cars all start together on the next.
            if (starter != null) starter.enabled = true;
            foreach (CarController car in cars) car.gameObject.SetActive(true);
            Lap("cars");
            playerIndex = player;
            racingCars = racing;
            carControllers = cars;
            if (resumed) PlaceSaved(resume, cars, racing, drivers, pits);
            new GameObject("Skid marks").AddComponent<SkidMarks>().Cars = cars;
            CarEffects effects = new GameObject("Car effects").AddComponent<CarEffects>();
            effects.Cars = cars;
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
            BroadcastDirector locked = views.gameObject.AddComponent<BroadcastDirector>();
            locked.Circuit = circuit;
            locked.Cars = racing;
            locked.Drivers = drivers;
            locked.Player = player;
            views.Locked = locked;
            // Run on its own the scene is for AI testing, so it starts on the chase camera.
            if (!career) views.StartInChase();
            RaceTower tower = Tower(circuit, racing, field, teams, watched, starter);
            raceTower = tower;
            if (day != null) tower.Flagged = i => day.Cars[i].Flagged;
            if (resumed) tower.SetRaceTime(resume.RaceTime);
            views.Tower = tower;
            if (player >= 0 && TeamColour(setup.Game, grid[player].TeamId) is Color own) tower.Colours[player] = own;
            view.Fit(Complex, tower.Colours, pits.Garages);
            // Each team's hired crew wait in its garage and work its stops.
            var crewSizes = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++) crewSizes[i] = grid[i].CrewSize;
            PitCrews crews = new GameObject("Pit crews").AddComponent<PitCrews>();
            crews.Build(Complex, pits, racing, drivers, tower.Colours, crewSizes);
            // The crews handle the kit at their stops: the fuel hose, the guns, the tyres, the jacks and the lollipop.
            PitKitWork kitWork = crews.gameObject.AddComponent<PitKitWork>();
            kitWork.Build(Complex, crews, view.Kit, racing);
            // The guns, jacks and fuel rig sound at the stops near the camera.
            crews.gameObject.AddComponent<PitAudio>().Build(kitWork);
            tv.Classes = tower.Classes;
            locked.Classes = tower.Classes;
            sky.RaceSeconds = () => tower.RaceTime;
            // Every race rolls its own weather (WeatherPlan), played on the race clock.
            int seed = resume != null ? resume.WeatherSeed : weatherSeed != 0 ? weatherSeed : new System.Random().Next(1, int.MaxValue);
            weatherRoll = seed;
            weatherKind = resume != null ? (WeatherKind?)resume.WeatherKind : DebugSettings.Weather;
            var weather = new WeatherPlan(seed, TimeOfDay.StartHour, weatherKind);
            Debug.Log($"Weather {seed}: {weather.Kind}");
            WeatherView rain = sky.gameObject.AddComponent<WeatherView>();
            rain.Plan = weather;
            rain.RaceSeconds = () => tower.RaceTime;
            rain.Materials = materials;
            rain.Cars = racing;
            rain.Night = () => sky.Lights;
            effects.Night = () => sky.Lights;
            sky.Weather = () => RaceWeather.Forced ?? weather.At(tower.RaceTime);
            trackside.Cars = racing;
            trackside.Racing = tower.Racing;
            if (starter is RaceStart formation) trackside.PaceCarOut = () => !formation.Racing;
            director = new GameObject("Race director").AddComponent<RaceDirector>();
            director.Day = day;
            director.Cars = racing;
            director.Drivers = drivers;
            director.Tower = tower;
            if (resumed) director.Resume(resume);
            RaceWeekend.Snapshot = Save;
            // The crowd, PA, rain, thunder, countryside and wind round the race.
            RaceAmbience ambience = new GameObject("Ambience").AddComponent<RaceAmbience>();
            ambience.Circuit = circuit;
            ambience.Stands = stands;
            ambience.Cars = racing;
            ambience.Tower = tower;
            ambience.Director = director;
            ambience.Cameras = views;
            ambience.Broadcast = tv;
            ambience.Sky = sky;
            ambience.Thundery = weather.Kind == WeatherKind.Thundery;
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

                // The player's stops follow the team's plan, the crew's call wherever it is left open.
                CarController mine = cars[player];
                CarRaceLog log = day.Cars[player];
                AiDriver ownDriver = drivers[player];
                mine.Supplies.Planner = new StopPlanner(mine.Supplies, mine.Spec,
                    () => Mathf.Max(0f, RaceTower.RaceLength - tower.RaceTime) / RaceTower.TimeCompression,
                    () => LapSeconds(log, ownDriver));
                PitPanel panel = new GameObject("Pit panel").AddComponent<PitPanel>();
                panel.Car = mine;
                panel.Keys = keys;
                panel.Planner = mine.Supplies.Planner;
                panel.Game = setup.Game;
                panel.Hud = views.Hud;
                BoxMarker marker = new GameObject("Box marker").AddComponent<BoxMarker>();
                marker.Car = mine;
                marker.Race = racing[player];
                marker.Pits = pits;
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
            debug = new GameObject("Debug menu").AddComponent<DebugMenu>();
            debug.Keys = keys;
            debug.Cameras = views;
            debug.Tower = tower;
            debug.Director = director;
            debug.Game = setup.Game;
            if (start == StartMode.PitLane)
            {
                var pitStart = (PitStart)starter;
                pitStart.Grid = racing;
                pitStart.Queue = pitLaneStart;
                pitStart.Pits = pits;
                skipping = true;
            }
            if (resumed)
            {
                handedOver = true;   // the save says who drives
                if (resume.PlayerDriving && player >= 0) ai.enabled = false;
            }
            if (start == StartMode.Rolling)
            {
                // The pace car waits ahead of pole, in pole's column.
                CarController paceCar = CarBuilder.Spawn(PaceCarModel, circuit, null, new Color(0.95f, 0.95f, 0.95f));
                paceCar.name = "Pace car";
                paceCar.PlaceAt(circuit.IndexOf(circuit.Data.grid.slots[0].d + 40f), RollingStart.ColumnOf(0));
                var rolling = (RaceStart)starter;
                rolling.Grid = racing;
                rolling.PaceCar = paceCar;
                rolling.Pits = pits;
            }
            // The stages before the green and after the flag, each skipped by holding Enter. Stages are added here in order.
            intro = new GameObject("Intro").AddComponent<StageSequence>();
            intro.Keys = keys;
            GridPresentation gridShow = new GameObject("Grid presentation").AddComponent<GridPresentation>();
            gridShow.Cars = racing;
            gridShow.Teams = teams;
            gridShow.Classes = tower.Classes;
            gridShow.Colours = tower.Colours;
            gridShow.Models = graphics.Models;
            gridShow.Day = day;
            gridShow.Game = setup.Game;
            gridShow.Player = player;
            gridShow.Tower = tower;
            gridShow.Hud = views.Hud;
            gridShow.Camera = views.GetComponent<Camera>();
            intro.Add(gridShow);
            outro =new GameObject("Outro").AddComponent<StageSequence>();
            outro.Keys = keys;
            Lap("race");
            built = true;
            SceneLoader.Release();
            if (resumed) intro.MarkPlayed();   // a race saved after the green has had its intro
            else intro.Play();
            var report = new System.Text.StringBuilder("Raceday built in ");
            long total = 0;
            foreach ((string Stage, long Ms) t in buildTimes) total += t.Ms;
            report.Append(total).Append(" ms:");
            foreach ((string Stage, long Ms) t in buildTimes) report.Append(' ').Append(t.Stage).Append(' ').Append(t.Ms).Append(" ms,");
            Debug.Log(report.ToString().TrimEnd(','));
        }

        private void OnDestroy()
        {
            if (!built) SceneLoader.Release();
            RaceWeekend.Snapshot = null;
        }

        // ---------------------------------------------------------------- race saves

        /// <summary>The race as it stands, for the pause menu's save.</summary>
        private RaceSave Save()
        {
            bool started = director.Started && !director.Finished;
            var cars = new CarSave[racingCars.Length];
            for (int i = 0; i < cars.Length; i++)
            {
                RaceCar race = racingCars[i];
                CarController car = carControllers[i];
                Consumables supplies = car.Supplies;
                TrafficCar t = race.Traffic;
                Transform at = car.transform;
                cars[i] = new CarSave
                {
                    Distance = Mathf.Repeat(t.Distance, circuit.Lap),
                    Lateral = t.Lateral,
                    Speed = Mathf.Max(t.Speed, 0f),
                    Laps = race.Laps,
                    InPitLane = race.InPitLane,
                    SinceLine = director.SinceLine(i),
                    Fuel = supplies.Fuel,
                    Wear = supplies.Wear,
                    Map = (int)supplies.Map,
                    PitRequested = supplies.Orders.Requested,
                    PitLaps = supplies.Orders.Laps,
                    PitTyres = supplies.Orders.Tyres,
                    PitRepairs = supplies.Orders.Repairs,
                    Position = new[] { at.position.x, at.position.y, at.position.z },
                    Forward = new[] { at.forward.x, at.forward.y, at.forward.z },
                };
            }
            return new RaceSave
            {
                Setup = day.Setup,
                Started = started,
                RaceTime = raceTower.RaceTime,
                Elapsed = director.Elapsed,
                SinceTimeUp = director.SinceTimeUp,
                StartHour = TimeOfDay.StartHour,
                WeatherSeed = weatherRoll,
                WeatherKind = (int?)weatherKind,
                PlayerDriving = playerIndex >= 0 && !ai.enabled,
                Day = day.Save(),
                Cars = cars,
            };
        }

        /// <summary>
        /// Puts every car back where a race save had it: on its lap, at its spot and speed, with its fuel, tyres and
        /// orders. A car in the pit lane goes on track beside where it was, at the edge on the pit side. A car out of
        /// the race stands where it stood, or in its box, out of everyone's traffic.
        /// </summary>
        private void PlaceSaved(RaceSave save, CarController[] cars, RaceCar[] racing, AiDriver[] drivers, PitLane pits)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                CarSave s = save.Cars[i];
                CarController car = cars[i];
                CarRaceLog log = day.Cars[i];
                Consumables supplies = car.Supplies;
                supplies.Fuel = Mathf.Clamp(s.Fuel, 0f, supplies.Capacity);
                supplies.Wear = Mathf.Clamp01(s.Wear);
                supplies.Map = (EngineMap)Mathf.Clamp(s.Map, 0, 2);
                supplies.Orders.Requested = s.PitRequested;
                supplies.Orders.Laps = s.PitLaps;
                supplies.Orders.Tyres = s.PitTyres;
                supplies.Orders.Repairs = s.PitRepairs;
                supplies.Effects = log.Effects;
                racing[i].Resume(s.Laps);

                if (log.Retired)
                {
                    if (log.RetirementReason == FailureType.Crash && s.Position is { Length: 3 } p && s.Forward is { Length: 3 } f)
                        car.PlaceAt(new Vector3(p[0], p[1], p[2]), new Vector3(f[0], f[1], f[2]));
                    else
                    {
                        Vector3 toGarage = pits.LaneLeft(pits.BoxLane[racing[i].Garage]);
                        car.PlaceAt(pits.BoxPoint(racing[i].Garage), new Vector3(toGarage.z, 0f, -toGarage.x));
                    }
                    drivers[i].enabled = false;
                    racing[i].enabled = false;   // out of every other car's traffic
                    car.Hold = true;
                    car.InputEnabled = false;
                    continue;
                }

                int k = circuit.IndexOf(Mathf.Repeat(s.Distance, circuit.Lap));
                float edge = Mathf.Max(circuit.HalfWidth(k) - 1.5f, 0f);
                float lateral = s.InPitLane ? Mathf.Sign(s.Lateral) * edge : Mathf.Clamp(s.Lateral, -edge, edge);
                car.PlaceAt(k, lateral);
                car.Launch(s.InPitLane ? Mathf.Min(s.Speed, pits.LimitSpeed) : s.Speed);
            }
        }

        /// <summary>
        /// Seconds a lap of yours takes, for planning stops: the quickest of the last three, so a lap with a stop
        /// in it doesn't count, or the AI's lap at the limit before there is one.
        /// </summary>
        private static float LapSeconds(CarRaceLog log, AiDriver driver)
        {
            IReadOnlyList<double> laps = log.LapTimes;
            double best = double.PositiveInfinity;
            for (int i = Mathf.Max(0, laps.Count - 3); i < laps.Count; i++)
                if (laps[i] > 0 && laps[i] < best) best = laps[i];
            if (!double.IsPositiveInfinity(best)) return (float)best;
            return driver.Pilot != null ? driver.Pilot.Profile.LapTime : 120f;
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
            if (!built || keys == null || ai == null) return;
            if (director.SinceTimeUp.HasValue) outro.Play();
            if (RaceWeekend.Paused) return;   // the pause menu holds the clock
            if (director.Finished) return;   // the race summary is up, at normal speed
            if (keys.Pressed(KeyCode.Delete))
            {
                ai.enabled = !ai.enabled;
                handedOver = true;   // your call from here on
            }
            if (keys.Pressed(KeyCode.F))
            {
                if (skipping) skipping = false;
                else if (stintSkip) stintSkip = false;
                else speed = (speed + 1) % Speeds.Length;
            }
            if (keys.Pressed(KeyCode.N) && CanSkip()) stintSkip = true;
            TakeTheStart();
            Stints();
            Cooldown();
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
        /// <summary>
        /// When you start the race in your own car, you drive it from the green, or from the pit exit after a pit lane
        /// start. The AI only has it for the formation lap and the queue. Delete still hands it back and forth.
        /// </summary>
        private void TakeTheStart()
        {
            if (handedOver || playerIndex < 0) return;
            if (day.Cars[playerIndex].Driver.Id != RaceEntry.PlayerCharacterDriverId)
            {
                handedOver = true;   // a teammate starts: the AI drives until your stint
                return;
            }
            bool green = raceTower.Racing == null || raceTower.Racing();
            if (!green || racingCars[playerIndex].InPitLane) return;
            handedOver = true;
            ai.enabled = false;
        }

        /// <summary>Past the flag, the AI drives your car round its cooldown lap and into its box, once.</summary>
        private void Cooldown()
        {
            if (cooldownHanded || playerIndex < 0 || day == null || !day.Cars[playerIndex].Flagged) return;
            cooldownHanded = true;
            stintSkip = false;
            ai.enabled = true;
        }

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
                // A team with a livery shows its base paint; the rest keep a random colour for now.
                Color random = RaceTower.TeamColours[paint.Next(RaceTower.TeamColours.Length)];
                string livery = CarArt.LiveryPath(teams[i]);
                tower.Colours[i] = livery != null && CarLivery.BaseColour(livery) is Color own ? own : random;
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
            views.Own = cars[watched];
            views.Keys = keys;
            views.Hud = cam.gameObject.AddComponent<DriveHud>();
            views.Hud.Car = cars[watched];
            cam.gameObject.AddComponent<FpsCounter>();
            cam.gameObject.AddComponent<CarReflections>();
            return views;
        }
    }
}
