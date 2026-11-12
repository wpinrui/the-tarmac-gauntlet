using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The AI debug menu, on Tab. It lists the set-ups of the racecraft spec (<see cref="Situations"/>)
    /// by rule. Picking one puts the race away and places that set-up's cars at their spot and speed,
    /// each on its tuned line, and they race at once. The menu also picks a car to drive, or none to
    /// watch, switches slow motion (T) and restarts the set-up (Backspace). While a set-up runs, each
    /// AI car shows the path it is steering for and, above it, why. The AI version and set-up are shown
    /// small in the bottom left corner at all times, e.g. "v2 T2", so feedback can name what it was about.
    /// </summary>
    public sealed class SituationMenu : MonoBehaviour
    {
        private const float SlowMotion = 0.25f;
        /// <summary>How far ahead the path is drawn, seconds at the car's speed.</summary>
        private const float PathTime = 3f;
        private static readonly Color Chosen = new Color(0.3f, 1f, 0.4f);

        public Circuit Circuit;
        public KeyboardState Keys;
        public RaceCameras Cameras;
        public PhysicsRange Range;
        /// <summary>The race's own objects, put away while a set-up runs.</summary>
        public GameObject[] Race;

        /// <summary>True once a set-up has been picked: the race is put away.</summary>
        public bool Running => situation != null;

        private Situation situation;
        private bool open, slow;
        /// <summary>The set-up's car the player drives, or -1 to watch.</summary>
        private int drive = -1;
        private string version = "v?";
        private Task<AiPilot>[] building;
        private readonly List<AiDriver> cars = new List<AiDriver>();
        private Vector2 scroll;
        private Material lineMaterial;
        private GUIStyle carLabel, corner;

        private void Start()
        {
            TextAsset v = Resources.Load<TextAsset>("AiVersion");
            if (v != null) version = v.text.Trim();
        }

        private void Update()
        {
            if (Keys == null) return;
            if (Keys.Pressed(KeyCode.Tab)) open = !open;
            if (Running && Keys.Pressed(KeyCode.Backspace)) Pick(situation);
            if (Running && Keys.Pressed(KeyCode.T)) slow = !slow;
            if (building != null) Build();
            if (Running)
            {
                float scale = slow ? SlowMotion : 1f;
                if (Time.timeScale != scale) Time.timeScale = scale;
            }
        }

        /// <summary>Puts the race away, or the last set-up's cars, and starts building this one's pilots.</summary>
        private void Pick(Situation s)
        {
            situation = s;
            if (Race != null)
                foreach (GameObject g in Race)
                    if (g != null) g.SetActive(false);
            // Every car of a set-up runs full physics, as in the test suite.
            if (Range != null) Range.enabled = false;
            foreach (AiDriver d in cars)
                if (d != null) Destroy(d.gameObject);
            cars.Clear();
            if (drive >= s.Cars.Length) drive = -1;

            // Each pilot on its car's tuned line, built off the main thread. Not on the tuner's thread,
            // which may still be busy with the race's field: a line not tuned yet is tuned here instead.
            building = new Task<AiPilot>[s.Cars.Length];
            Circuit circuit = Circuit;
            for (int i = 0; i < s.Cars.Length; i++)
            {
                SituationCar c = s.Cars[i];
                CarSpec spec = CarSpec.For(c.Model);
                Task<RacingLine> tuned = LineTuner.Line(circuit, spec);
                float pace = c.Pace, consistency = c.Consistency, safety = c.Safety;
                int seed = c.Seed;
                building[i] = Task.Run(() =>
                {
                    RacingLine line = tuned.Status == TaskStatus.RanToCompletion ? tuned.Result : new RacingLine(circuit, spec);
                    return new AiPilot(line, spec, pace, consistency, safety, seed);
                });
            }
        }

        /// <summary>Once every pilot is built, places the cars and lets them go.</summary>
        private void Build()
        {
            foreach (Task<AiPilot> t in building)
            {
                if (!t.IsCompleted) return;
                if (t.Status != TaskStatus.RanToCompletion)
                {
                    Debug.LogException(t.Exception);
                    building = null;
                    return;
                }
            }
            Situation s = situation;
            var pilots = new AiPilot[building.Length];
            for (int i = 0; i < pilots.Length; i++) pilots[i] = building[i].Result;
            building = null;

            var controllers = new CarController[s.Cars.Length];
            for (int i = 0; i < s.Cars.Length; i++)
            {
                SituationCar c = s.Cars[i];
                AiPilot p = pilots[i];
                c.Spot(s, Circuit, pilots[0].Line, p.Line, p.Profile, out int k, out float lateral, out float speed);
                bool driven = i == drive;
                CarController car = CarBuilder.Spawn(c.Model, Circuit, driven ? Keys : null, CircuitScene.PaintFor(c.Model));
                car.PlaceAt(Circuit.Surface(k, lateral), Quaternion.Euler(0f, c.Turned, 0f) * Circuit.Forward[k]);
                if (speed > 0f) car.Launch(speed);
                car.Supplies = Consumables.For(car.Spec, p.Profile, 60f);
                car.Supplies.Wear = c.Wear;

                RaceCar race = car.gameObject.AddComponent<RaceCar>();
                race.FirstLap = -c.LapsDown;
                AiDriver d = car.gameObject.AddComponent<AiDriver>();
                d.Line = p.Line;
                d.Seed = c.Seed;
                d.Pace = c.Pace;
                d.Consistency = c.Consistency;
                d.Safety = c.Safety;
                d.Given = p;
                if (c.Spun)
                {
                    d.Prepare();
                    d.Racer.Rejoin.Spin();
                }
                if (driven)
                {
                    // Raced by the others as any AI car is, driven by the player.
                    d.Prepare();
                    d.enabled = false;
                }
                cars.Add(d);
                controllers[i] = car;
            }
            if (Cameras != null)
            {
                Cameras.Cars = controllers;
                Cameras.Watch(controllers[drive >= 0 ? drive : 0]);
            }
        }

        // ---------------------------------------------------------------- menu and labels

        private void OnGUI()
        {
            float scale = Screen.height / 1080f;
            if (corner == null)
            {
                corner = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerLeft };
                carLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold };
            }
            corner.fontSize = Mathf.RoundToInt(18 * scale);
            carLabel.fontSize = Mathf.RoundToInt(18 * scale);

            string label = version + (situation != null ? $" {situation.Id}" : "");
            Shadowed(new Rect(12f * scale, Screen.height - 40f * scale, 400f * scale, 30f * scale), label, corner, scale);
            if (Running && Event.current.type == EventType.Repaint) CarLabels(scale);
            if (open) Menu(scale);
        }

        private void Menu(float scale)
        {
            float w = 460f * scale, h = Screen.height * 0.8f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), GUI.skin.box);
            int size = Mathf.RoundToInt(18 * scale);
            var button = new GUIStyle(GUI.skin.button) { fontSize = size, alignment = TextAnchor.MiddleLeft };
            var on = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            on.normal.textColor = on.hover.textColor = Chosen;

            if (situation != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Watch", drive < 0 ? on : button)) Drive(-1);
                for (int i = 0; i < situation.Cars.Length; i++)
                    if (GUILayout.Button(CarSpec.For(situation.Cars[i].Model).Name, drive == i ? on : button)) Drive(i);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Restart", button)) { Pick(situation); open = false; }
                if (GUILayout.Button("Slow motion", slow ? on : button)) slow = !slow;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8f * scale);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (Situation s in Situations.All)
            {
                if (!s.Ready) continue;
                if (GUILayout.Button(s.Name, s == situation ? on : button))
                {
                    Pick(s);
                    open = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Drive(int i)
        {
            if (drive == i) return;
            drive = i;
            Pick(situation);
        }

        /// <summary>Why each AI car is doing what it does, above it.</summary>
        private void CarLabels(float scale)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (AiDriver d in cars)
            {
                if (d == null) continue;
                string why = Reason(d);
                if (why == null) continue;
                Vector3 s = cam.WorldToScreenPoint(d.transform.position + Vector3.up * 2f);
                if (s.z <= 0f) continue;
                var r = new Rect(s.x - 150f * scale, Screen.height - s.y - 30f * scale, 300f * scale, 30f * scale);
                Shadowed(r, why, carLabel, scale);
            }
        }

        /// <summary>What racecraft is doing for the car this step, or null when it is free on its line.</summary>
        private static string Reason(AiDriver d)
        {
            AiRacer r = d.Racer;
            if (r == null || !d.enabled) return null;
            if (r.Ghost) return "ghost";
            if (r.Rejoin.Busy) return "rejoining";
            Racecraft c = r.Craft;
            if (c.Braking) return "braking for the car ahead";
            if (c.SideSteering) return "owes room alongside";
            if (c.HoldingLine) return "holding the line";
            if (c.Defending && !c.Exempt)
                return c.MovePhase == 1 ? "defending move" : c.MovePhase == 2 ? "defending move used" : c.MayDefend ? "defending" : "no defending here";
            if (c.PullingOut) return "pulling out";
            if (c.State == Racecraft.Mode.Overtaking) return "passing";
            if (c.FrontGap < 2f * Mathf.Max(d.Car.Speed, 10f)) return "following";
            return null;
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, float scale)
        {
            Color was = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1.5f * scale, r.y + 1.5f * scale, r.width, r.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
            style.normal.textColor = was;
        }

        // ---------------------------------------------------------------- paths

        /// <summary>The path each AI car steers for: from where it runs now across to racecraft's offset, at the rate it moves across.</summary>
        private void OnRenderObject()
        {
            if (!Running || cars.Count == 0) return;
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
            }
            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            GL.Color(Chosen);
            foreach (AiDriver d in cars)
            {
                if (d == null || !d.enabled || d.Racer == null) continue;
                AiPilot p = d.Racer.Active;
                if (p.Index < 0) continue;
                float speed = Mathf.Max(d.Car.Speed, 10f);
                int steps = Mathf.CeilToInt(speed * PathTime / (4f * Circuit.Step));
                Vector3 last = d.transform.position + Vector3.up * 0.3f;
                for (int i = 1; i <= steps; i++)
                {
                    int k = Circuit.Wrap(p.Index + 4 * i);
                    float t = 4f * i * Circuit.Step / speed;
                    float offset = Mathf.MoveTowards(p.Offset, d.Racer.Craft.Target, Endurance.OvertakeOffsetRate * t);
                    Vector3 at = p.LanePoint(k, offset) + Vector3.up * 0.3f;
                    GL.Vertex(last);
                    GL.Vertex(at);
                    last = at;
                }
            }
            GL.End();
            GL.PopMatrix();
        }
    }
}
