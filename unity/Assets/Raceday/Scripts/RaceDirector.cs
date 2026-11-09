using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs raceday: builds the circuit, plays the race back in real time from the simulation,
    /// places every car and takes the player's race calls. The only thing the scene needs is
    /// one GameObject with this component.
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        private const float MapDotHeight = 30f;

        private enum Stage { PickCar, Racing, Finished }

        private Stage stage = Stage.PickCar;
        private TrackLayout track;
        private SpeedProfile profile;
        private Materials materials;
        private RaceCamera raceCamera;
        private RaceHud hud;

        private GameState game;
        private LiveRace live;
        private RacePlayback playback;
        private RaceResultFull playbackOf;
        private readonly Dictionary<string, CarView> cars = new Dictionary<string, CarView>();
        private IReadOnlyList<string> order = Array.Empty<string>();

        private double simTime;
        private float timeScale = 1f;
        private bool paused;
        private string targetCarId;
        private PitPlan draftPit = new PitPlan();

        private sealed class CarView
        {
            public Transform Root;
            public GameObject MapDot;
            public int Slot;
        }

        // --- Read by the HUD ---
        internal bool Paused => paused;
        internal GameState Game => game;
        internal LiveRace Live => live;
        internal RacePlayback Playback => playback;
        internal double SimTime => simTime;
        internal IReadOnlyList<string> Order => order;
        internal string TargetCarId => targetCarId;
        internal PitPlan DraftPit => draftPit;

        private void Start()
        {
            TextAsset json = Resources.Load<TextAsset>("Tracks/gauntlet");
            track = TrackLayout.Load(json.text);
            profile = new SpeedProfile(track.Main);
            materials = new Materials();
            TrackDressing.Build(track, materials);

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f);
            RenderSettings.fogStartDistance = 400f;
            RenderSettings.fogEndDistance = 1600f;

            Camera cam = Camera.main != null ? Camera.main : new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            raceCamera = new RaceCamera(cam, track);
            hud = new RaceHud(this);

            // Until a car is picked, show the circuit from above.
            raceCamera.NextView();
            raceCamera.NextView();
        }

        // --- Commands from the HUD ---

        internal void StartRace(string modelId)
        {
            foreach (CarView v in cars.Values) Destroy(v.Root.gameObject);
            cars.Clear();

            int seed = Environment.TickCount;
            game = TestField.BuildGame(modelId, seed);
            List<CarEntry> grid = TestField.Grid(game);
            live = new LiveRace(grid, TestField.PlayerCarId, seed);
            playback = null;
            playbackOf = null;

            for (int i = 0; i < grid.Count; i++)
            {
                CarEntry e = grid[i];
                bool player = e.CarId == TestField.PlayerCarId;
                GameObject car = CarFactory.Create(e.Model, player, materials);
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dot.transform.SetParent(car.transform, false);
                dot.transform.localPosition = Vector3.up * MapDotHeight;
                dot.transform.localScale = player ? new Vector3(16f, 0.5f, 16f) : new Vector3(9f, 0.5f, 9f);
                dot.GetComponent<Renderer>().sharedMaterial = materials.Get(player ? Color.white : CarFactory.ClassColour(e.Model.CarClass));
                cars[e.CarId] = new CarView { Root = car.transform, MapDot = dot, Slot = i };
            }

            simTime = 0;
            paused = false;
            timeScale = 1f;
            draftPit = new PitPlan();
            targetCarId = TestField.PlayerCarId;
            while (raceCamera.Mode != RaceCamera.View.Chase) raceCamera.NextView();
            stage = Stage.Racing;
            Refresh();
        }

        internal void SetMode(InstructionMode mode) => live.Mode = mode;

        internal void TogglePit() => live.PitPlan = live.PitPlan == null ? draftPit : null;

        internal void EditPit(PitPlan plan)
        {
            draftPit = plan;
            if (live.PitPlan != null) live.PitPlan = plan;
        }

        internal void Follow(string carId) => targetCarId = carId;

        internal void BackToGarage()
        {
            stage = Stage.PickCar;
            foreach (CarView v in cars.Values) Destroy(v.Root.gameObject);
            cars.Clear();
            live = null;
            playback = null;
            game = null;
            while (raceCamera.Mode != RaceCamera.View.Overhead) raceCamera.NextView();
        }

        internal bool IsPickingCar => stage == Stage.PickCar;
        internal bool IsFinished => stage == Stage.Finished;

        // --- Frame loop ---

        private void Update()
        {
            if (stage == Stage.Racing && !paused)
            {
                simTime = Math.Min(RaceClock.TotalRaceSeconds, simTime + Time.deltaTime * timeScale);
                if (simTime >= RaceClock.TotalRaceSeconds) stage = Stage.Finished;
            }
            if (live != null) Refresh();
        }

        private void Refresh()
        {
            live.AdvanceTo(simTime);
            if (!ReferenceEquals(live.Result, playbackOf))
            {
                playbackOf = live.Result;
                playback = new RacePlayback(live.Result, live.PitDurations);
            }
            order = playback.OrderAt(simTime);

            bool overhead = raceCamera.Mode == RaceCamera.View.Overhead;
            foreach (KeyValuePair<string, CarView> kv in cars)
            {
                Place(kv.Value, playback.StateAt(kv.Key, simTime));
                kv.Value.MapDot.SetActive(overhead);
            }
        }

        private void LateUpdate()
        {
            Transform target = targetCarId != null && cars.TryGetValue(targetCarId, out CarView v) ? v.Root : null;
            if (target != null) raceCamera.Update(target, Time.deltaTime);
            else if (stage == Stage.PickCar) raceCamera.Update(transform, Time.deltaTime);
        }

        private void Place(CarView car, CarPlaybackState s)
        {
            float lapLength = track.LapLength;
            float lane = ((car.Slot % 4) - 1.5f) * 3f;
            Vector3 pos, fwd;

            switch (s.Phase)
            {
                case CarPhase.Pitting:
                    pos = track.PitLane.Offset(track.PitBox, track.PitSide * 2f, out fwd);
                    break;

                case CarPhase.Retired:
                {
                    float d = profile.DistanceAt((float)s.LapFraction);
                    float side = (car.Slot % 2 == 0 ? 1 : -1) * (TrackLayout.RoadWidth / 2 + 5f);
                    pos = track.Main.Offset(d, side, out fwd);
                    break;
                }

                default:
                {
                    float d = profile.DistanceAt((float)s.LapFraction);
                    float lateral = lane;
                    if (s.Lap == 1)
                    {
                        // Lap 1 starts from a two-wide grid behind the line.
                        float back = 8f + (car.Slot / 2) * 9f;
                        d = Mathf.Lerp(-back, lapLength, d / lapLength);
                        float gridLane = car.Slot % 2 == 0 ? -2.5f : 2.5f;
                        lateral = Mathf.Lerp(gridLane, lane, Mathf.Clamp01((float)s.LapFraction * 4f));
                    }

                    bool pitIn = track.PitLane != null && s.PitsThisLap && d >= track.PitEntry && track.PitEntry > track.PitExit;
                    bool pitOut = track.PitLane != null && s.PittedLastLap && d <= track.PitExit && track.PitEntry > track.PitExit;
                    if (pitIn)
                    {
                        float u = (d - track.PitEntry) / Mathf.Max(1f, lapLength - track.PitEntry);
                        pos = track.PitLane.Offset(Mathf.Lerp(0, track.PitBox, u), 0, out fwd);
                    }
                    else if (pitOut)
                    {
                        float u = d / Mathf.Max(1f, track.PitExit);
                        pos = track.PitLane.Offset(Mathf.Lerp(track.PitBox, track.PitLane.Length, u), 0, out fwd);
                    }
                    else
                    {
                        pos = track.Main.Offset(d, lateral, out fwd);
                    }
                    break;
                }
            }

            car.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
        }

        // --- Input and HUD ---

        private void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && stage != Stage.PickCar) HandleKey(e);
            hud.Draw();
        }

        private void HandleKey(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (stage == Stage.Racing) paused = !paused;
                    break;
                case KeyCode.C:
                    raceCamera.NextView();
                    break;
                case KeyCode.Q:
                    Cycle(-1);
                    break;
                case KeyCode.E:
                    Cycle(1);
                    break;
                case KeyCode.F:
                    targetCarId = TestField.PlayerCarId;
                    break;
                // Play-test only: speed the race up.
                case KeyCode.Alpha1: timeScale = 1f; break;
                case KeyCode.Alpha2: timeScale = 4f; break;
                case KeyCode.Alpha3: timeScale = 16f; break;
                case KeyCode.Alpha4: timeScale = 64f; break;
                default:
                    return;
            }
            e.Use();
        }

        private void Cycle(int step)
        {
            if (order.Count == 0) return;
            int i = Math.Max(0, order.ToList().IndexOf(targetCarId));
            targetCarId = order[(i + step + order.Count) % order.Count];
        }
    }
}
