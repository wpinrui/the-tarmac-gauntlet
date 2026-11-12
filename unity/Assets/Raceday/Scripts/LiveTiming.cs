using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The full screen live timing page, on T while the AI drives your car. The race runs at 16x behind
    /// it. Left: every car in race order, filtered by class, your row in white. Right: your car's
    /// onboard and the track map, then your car's driver, fuel, tyres, condition and the driver's fatigue,
    /// fuel and tyres with the laps they last on the engine map in use (the one that runs out first in amber), the engine map, and
    /// the next stop: litres, tyres, next driver and Pit now (<see cref="PitOrders"/>). Left alone the
    /// car still pits and chooses for itself. The red button top right, or T, returns to the race, and
    /// the button beside it skips to your next stint, while a teammate drives.
    /// Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class LiveTiming : MonoBehaviour
    {
        public RaceCar[] Cars;
        public AiDriver[] Drivers;
        public string[] Teams;
        public CarClass[] Classes;
        public Color[] Colours;
        /// <summary>Index of the player's car in <see cref="Cars"/>.</summary>
        public int Player;
        /// <summary>Length of the lap, m.</summary>
        public float Lap;
        public Circuit Circuit;
        /// <summary>The AI driver of the player's car.</summary>
        public AiDriver Driver;
        public RaceTower Tower;
        public DriveHud Hud;
        public KeyboardState Keys;
        /// <summary>The career, for the drivers' names.</summary>
        public GameState Game;
        /// <summary>True when skipping to your next stint can be offered: the button left of the way back to the race.</summary>
        public System.Func<bool> CanSkip;
        /// <summary>Skips to your next stint.</summary>
        public System.Action Skip;

        public bool Open { get; private set; }

        private const float FuelStep = 5f;
        /// <summary>Where the timing columns start along a row, px on the 1920 layout.</summary>
        private const float ClassX = 560f, LapX = 676f, PitX = 740f;
        private const int PipWidth = 880, PipHeight = 496, MapWidth = 1040, MapHeight = 496;

        private static readonly Color Ground = new Color32(0x0b, 0x0b, 0x0d, 0xff);
        private static readonly Color Panel = new Color32(0x1a, 0x1a, 0x1c, 0xe6);
        private static readonly Color Rule = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color Edge = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color Red = RaceTower.Hex(0xe10600);
        private static readonly Color White = RaceTower.Hex(0xf2f2f2);
        private static readonly Color Ink = RaceTower.Hex(0x111111);
        private static readonly Color Amber = RaceTower.Hex(0xffb020);
        private static readonly Color ConserveColour = RaceTower.Hex(0x3ddc84);
        private static readonly Color PushColour = RaceTower.Hex(0xff8a1f);
        private static readonly CarClass?[] Filters = { null, CarClass.A, CarClass.B, CarClass.C, CarClass.D, CarClass.E, CarClass.F };

        private readonly List<Standings.Car> order = new List<Standings.Car>();
        private readonly List<Standings.Car> shown = new List<Standings.Car>();
        private Standings.Car[] entries;
        private CarClass? filter;
        private Vector2 scroll;
        private bool centre;
        private float k, offset;

        private Font bold, semiBold;
        private GUIStyle big, text, small, right;
        private Texture2D slash, disc, arrow;
        private TrackMap map;
        private RenderTexture pip;
        private Camera onboard;

        private CarController Car => Driver.Car;
        private Consumables Supplies => Car.Supplies;
        private CarRaceLog Log => Supplies.Log;

        private void Start()
        {
            entries = new Standings.Car[Cars.Length];
            for (int i = 0; i < Cars.Length; i++)
                entries[i] = new Standings.Car { Id = i, Team = Teams[i].ToUpperInvariant(), Class = Classes[i], Player = i == Player };
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            // The tower's slash: a bar leaning at 24 degrees.
            float lean = Mathf.Tan(24f * Mathf.Deg2Rad);
            slash = RaceTower.Shape(0.42f + 1.25f * lean, 1.25f, (x, y) => x >= y * lean && x <= y * lean + 0.42f);
            arrow = RaceTower.Shape(0.72f, 1f, (x, y) => x <= 0.72f * (1f - 2f * Mathf.Abs(y - 0.5f)));
            disc = RaceTower.Shape(1f, 1f, (x, y) => (x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f) < 0.25f);
            map = new TrackMap(Circuit, MapWidth, MapHeight);
            pip = new RenderTexture(PipWidth, PipHeight, 24) { hideFlags = HideFlags.DontSave };
            onboard = new GameObject("Onboard camera").AddComponent<Camera>();
            onboard.transform.SetParent(transform, false);
            onboard.targetTexture = pip;
            onboard.fieldOfView = 70f;
            onboard.nearClipPlane = 0.1f;
            onboard.farClipPlane = 8000f;
            onboard.enabled = false;
        }

        private void Update()
        {
            if (entries == null || Driver == null) return;
            bool can = Driver.enabled && Supplies != null && Log != null;
            if (Open && !can) Show(false);
            else if (Keys != null && Keys.Pressed(KeyCode.T) && (Open || can)) Show(!Open);
            if (!Open) return;
            order.Clear();
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null) continue;
                entries[i].Distance = Cars[i].Traffic.Distance;
                order.Add(entries[i]);
            }
            Standings.Rank(order, Lap);
            shown.Clear();
            foreach (Standings.Car c in order) if (filter == null || c.Class == filter) shown.Add(c);
        }

        private void Show(bool on)
        {
            Open = on;
            if (Tower != null) Tower.Hidden = on;
            if (Hud != null) Hud.enabled = !on;
            if (onboard != null) onboard.enabled = on;
            centre = on;
        }

        private void LateUpdate()
        {
            if (!Open || Car == null) return;
            Transform t = Car.transform;
            onboard.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.2f, 0.3f)), t.rotation);
        }

        private void OnGUI()
        {
            if (!Open || DebugMenu.Clean || entries == null || Supplies == null || Log == null) return;
            k = Screen.height / 1080f;
            Style();
            bool paint = Event.current.type == EventType.Repaint;
            offset = (Screen.width / k - 1920f) / 2f;
            if (paint) Fill(new Rect(0f, 0f, Screen.width, Screen.height), Ground, 0f);

            // Race time left, and the way back to the race.
            Rect clock = R(40f, 40f, 0f, 44f);
            clock.width = big.CalcSize(new GUIContent(Tower != null ? Tower.TimeLeft : "")).x + 40f * k;
            if (paint)
            {
                Fill(clock, new Color(18f / 255f, 18f / 255f, 20f / 255f, 0.95f), 9f);
                Label(clock, Tower != null ? Tower.TimeLeft : "", big, Color.white, TextAnchor.MiddleCenter);
            }
            if (paint && Tower != null)
            {
                string hour = Tower.TimeOfDayText;
                Rect day = new Rect(clock.xMax + 8f * k, clock.y, big.CalcSize(new GUIContent(hour)).x + 40f * k, clock.height);
                Fill(day, new Color(18f / 255f, 18f / 255f, 20f / 255f, 0.95f), 9f);
                Label(day, hour, big, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
            }
            Rect live = R(1824f, 40f, 56f, 44f);
            if (paint)
            {
                Fill(live, Red, 9f);
                Rect screen = new Rect(live.center.x - 12f * k, live.center.y - 8f * k, 24f * k, 17f * k);
                GUI.DrawTexture(screen, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Color.white, Vector4.one * 2.2f * k, Vector4.one * 3f * k);
            }
            if (Click(live)) Show(false);
            if (CanSkip != null && CanSkip())
            {
                Rect skip = R(1756f, 40f, 56f, 44f);
                if (paint)
                {
                    Fill(skip, new Color(1f, 1f, 1f, 0.1f), 9f);
                    float h = 18f * k, w = 13f * k;
                    GUI.DrawTexture(new Rect(skip.center.x - w, skip.center.y - h / 2f, w, h), arrow, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
                    GUI.DrawTexture(new Rect(skip.center.x, skip.center.y - h / 2f, w, h), arrow, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
                }
                if (Click(skip))
                {
                    Show(false);
                    Skip?.Invoke();
                }
            }

            Timing(R(40f, 104f, 820f, 936f), paint);

            Rect pipRect = R(900f, 104f, 440f, 248f);
            if (paint)
            {
                GUI.DrawTexture(pipRect, pip, ScaleMode.ScaleAndCrop, false, 0f, Color.white, 0f, 9f * k);
                Standings.Car me = entries[Player];
                Rect tag = new Rect(pipRect.x + 12f * k, pipRect.y + 12f * k, 0f, 26f * k);
                tag.width = 30f * k + text.CalcSize(new GUIContent(me.Position.ToString())).x;
                Fill(tag, new Color(18f / 255f, 18f / 255f, 20f / 255f, 0.85f), 6f);
                GUI.DrawTexture(new Rect(tag.x + 8f * k, tag.y + 5f * k, 10f * k, 16f * k), slash, ScaleMode.StretchToFill, true, 0f, White, 0f, 0f);
                Label(new Rect(tag.x + 22f * k, tag.y, tag.width - 22f * k, tag.height), me.Position.ToString(), text, Color.white, TextAnchor.MiddleLeft);
            }
            Rect mapRect = R(1360f, 104f, 520f, 248f);
            if (paint) Map(mapRect);

            Status(R(900f, 372f, 480f, 668f), paint);
            Stop(R(1400f, 372f, 480f, 668f), paint);
        }

        /// <summary>The class filter and every car in race order, scrolled to your car when the page opens.</summary>
        private void Timing(Rect panel, bool paint)
        {
            if (paint) Fill(panel, Panel, 9f);
            float x = panel.x + 16f * k;
            for (int i = 0; i < Filters.Length; i++)
            {
                CarClass? f = Filters[i];
                string label = f == null ? "ALL" : RaceTower.ClassLetter(f.Value);
                float w = Mathf.Max(44f * k, small.CalcSize(new GUIContent(label)).x + 28f * k);
                var pill = new Rect(x, panel.y + 14f * k, w, 44f * k);
                Color colour = f == null ? Color.white : RaceTower.ClassColour(f.Value);
                bool on = filter == f;
                if (paint)
                {
                    if (on) Fill(pill, new Color(1f, 1f, 1f, 0.08f), 8f);
                    Outline(pill, on ? colour : Edge, 8f);
                    Label(pill, label, small, colour, TextAnchor.MiddleCenter);
                }
                if (Click(pill))
                {
                    filter = f;
                    centre = true;
                }
                x += w + 8f * k;
            }

            float top = panel.y + 72f * k;
            float row = 34f * k;
            Rect header = new Rect(panel.x + 16f * k, top, panel.width - 32f * k, 30f * k);
            if (paint)
            {
                Label(Col(header, ClassX, 64f), "CLASS", small, Dim, TextAnchor.MiddleCenter);
                Label(Col(header, LapX, 64f), "LAP", small, Dim, TextAnchor.MiddleRight);
                Label(Col(header, PitX, 48f), "PIT", small, Dim, TextAnchor.MiddleRight);
            }

            var view = new Rect(panel.x, top + 30f * k, panel.width, panel.yMax - top - 30f * k - 8f * k);
            var content = new Rect(0f, 0f, view.width - 14f * k, shown.Count * row);
            if (centre)
            {
                int mine = shown.FindIndex(c => c.Player);
                scroll.y = mine < 0 ? 0f : Mathf.Max(0f, (mine + 0.5f) * row - view.height / 2f);
                centre = false;
            }
            scroll = GUI.BeginScrollView(view, scroll, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (paint)
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / row));
                int last = Mathf.Min(shown.Count - 1, Mathf.CeilToInt((scroll.y + view.height) / row));
                for (int i = first; i <= last; i++) Row(shown[i], new Rect(16f * k, i * row, content.width - 16f * k, row));
            }
            GUI.EndScrollView();
        }

        private void Row(Standings.Car c, Rect r)
        {
            bool mine = c.Player;
            if (mine) Fill(new Rect(r.x - 16f * k, r.y, r.width + 16f * k, r.height), White, 0f);
            else Fill(new Rect(r.x - 16f * k, r.y, r.width + 16f * k, 1f), Rule, 0f);
            Color ink = mine ? Ink : Color.white;
            Rect pos = new Rect(r.x, r.y + 1f, 50f * k, r.height - 2f);
            bool leader = c.ClassPosition == 1;
            if (leader) Fill(pos, Red, 0f);
            Label(new Rect(pos.x, pos.y, pos.width - 10f * k, pos.height), c.Position.ToString(), text, leader ? Color.white : ink, TextAnchor.MiddleRight);
            GUI.DrawTexture(new Rect(r.x + 60f * k, r.y + 6f * k, 14f * k, 22f * k), slash, ScaleMode.StretchToFill, true, 0f, mine ? Ink : Colours[c.Id], 0f, 0f);
            Label(new Rect(r.x + 82f * k, r.y, (ClassX - 92f) * k, r.height), c.Team, text, ink, TextAnchor.MiddleLeft);

            Color colour = RaceTower.ClassColour(c.Class);
            if (mine) colour = new Color(colour.r * 0.75f, colour.g * 0.75f, colour.b * 0.75f);
            Rect badge = Col(r, ClassX + 16f, 32f);
            badge = new Rect(badge.x, r.y + 6f * k, badge.width, 22f * k);
            Outline(badge, colour, 6f);
            Label(badge, RaceTower.ClassLetter(c.Class), small, colour, TextAnchor.MiddleCenter);
            Label(Col(r, ClassX + 64f, 44f), c.ClassPosition.ToString(), right, mine ? Ink : Dim, TextAnchor.MiddleRight);
            Label(Col(r, LapX, 64f), c.FakeLaps.ToString(), right, ink, TextAnchor.MiddleRight);
            AiDriver d = Drivers[c.Id];
            int stops = d != null && d.Racer?.Pit != null ? d.Racer.Pit.Stops : 0;
            Label(Col(r, PitX, 48f), stops.ToString(), right, mine ? Ink : Dim, TextAnchor.MiddleRight);
        }

        /// <summary>A column of a row, from <paramref name="x"/> px along the 1920 layout's row.</summary>
        private Rect Col(Rect r, float x, float w) => new Rect(r.x + x * k, r.y, w * k, r.height);

        private void Map(Rect r)
        {
            Fill(r, Panel, 9f);
            Rect inner = map.Fit(r);
            GUI.DrawTexture(inner, map.Texture, ScaleMode.StretchToFill, true, 0f, Track, 0f, 0f);
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null || i == Player) continue;
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[i]), 9f, RaceTower.ClassColour(Classes[i]));
            }
            if (Cars[Player] != null)
            {
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[Player]), 18f, Ground);
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[Player]), 13f, Color.white);
            }
        }

        private void Dot(Rect inner, Vector3 p, float size, Color colour)
        {
            Vector2 at = map.At(inner, p);
            float s = size * k;
            GUI.DrawTexture(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), disc, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
        }

        /// <summary>The driver at the wheel, fuel, tyres, condition and fatigue, and the engine map.</summary>
        private void Status(Rect panel, bool paint)
        {
            if (paint) Fill(panel, Panel, 9f);
            float x = panel.x + 24f * k, w = panel.width - 48f * k, y = panel.y + 24f * k;
            if (paint) Label(new Rect(x, y, w, 46f * k), Name(Log.Driver.Id), big, Color.white, TextAnchor.MiddleLeft);
            y += 70f * k;

            Consumables s = Supplies;
            float fuelLaps = s.FuelLaps;
            float tyreLaps = s.TyreLaps(PitPlan.WearLimit);
            if (paint)
            {
                Bar(x, ref y, w, "FUEL", s.Fuel / Mathf.Max(s.Capacity, 1f), Laps(fuelLaps), fuelLaps <= tyreLaps);
                Bar(x, ref y, w, "TYRES", 1f - s.Wear, Laps(tyreLaps), tyreLaps < fuelLaps);
                Bar(x, ref y, w, "CONDITION", (float)Log.Condition / 100f, Mathf.RoundToInt((float)Log.Condition) + "%", false);
                Bar(x, ref y, w, "FATIGUE", (float)Log.Fatigue / 100f, Mathf.RoundToInt((float)Log.Fatigue) + "%", false);
            }

            float bw = (w - 16f * k) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var m = (EngineMap)i;
                var b = new Rect(x + i * (bw + 8f * k), panel.yMax - 24f * k - 52f * k, bw, 52f * k);
                Color colour = m == EngineMap.Conserve ? ConserveColour : m == EngineMap.Push ? PushColour : White;
                bool on = s.Map == m;
                if (paint)
                {
                    if (on) Fill(b, colour, 8f);
                    else Outline(b, colour, 8f);
                    Label(b, m.ToString().ToUpperInvariant(), small, on ? Ink : colour, TextAnchor.MiddleCenter);
                }
                if (Click(b)) s.Map = m;
            }
        }

        private void Bar(float x, ref float y, float w, string label, float share, string value, bool limiting)
        {
            Color colour = limiting ? Amber : White;
            Label(new Rect(x, y, w, 26f * k), label, small, Dim, TextAnchor.MiddleLeft);
            Label(new Rect(x, y, w, 26f * k), value, right, colour, TextAnchor.MiddleRight);
            y += 34f * k;
            Fill(new Rect(x, y, w, 10f * k), new Color(1f, 1f, 1f, 0.1f), 5f);
            Fill(new Rect(x, y, w * Mathf.Clamp01(share), 10f * k), colour, 5f);
            y += 32f * k;
        }

        private static string Laps(float laps) => Mathf.FloorToInt(laps) + " LAPS";

        /// <summary>The next stop: litres, tyres, next driver and Pit now.</summary>
        private void Stop(Rect panel, bool paint)
        {
            if (paint) Fill(panel, Panel, 9f);
            Consumables s = Supplies;
            PitOrders orders = s.Orders;
            float x = panel.x + 24f * k, w = panel.width - 48f * k, y = panel.y + 24f * k;

            float room = Mathf.Max(s.Capacity - s.Fuel, 0f);
            float litres = Mathf.Clamp(orders.Litres ?? room, 0f, room);
            if (paint) Label(new Rect(x, y, w, 44f * k), "FUEL", small, Dim, TextAnchor.MiddleLeft);
            var plus = new Rect(x + w - 44f * k, y, 44f * k, 44f * k);
            var amount = new Rect(plus.x - 104f * k, y, 92f * k, 44f * k);
            var minus = new Rect(amount.x - 56f * k, y, 44f * k, 44f * k);
            if (paint)
            {
                Step(minus, "-");
                Step(plus, "+");
                Label(amount, "+" + Mathf.RoundToInt(litres) + " L", big, Color.white, TextAnchor.MiddleCenter);
            }
            if (Click(minus)) orders.Litres = Mathf.Max(0f, Mathf.Ceil(litres / FuelStep) * FuelStep - FuelStep);
            if (Click(plus)) orders.Litres = Mathf.Min(room, Mathf.Floor(litres / FuelStep) * FuelStep + FuelStep);
            y += 66f * k;

            bool sets = s.TyresLeft;
            bool fresh = orders.Tyres && sets;
            if (paint) Label(new Rect(x, y, w, 44f * k), "TYRES", small, Dim, TextAnchor.MiddleLeft);
            var tyres = new Rect(x + w - 140f * k, y, 140f * k, 44f * k);
            if (paint) Chip(tyres, fresh ? "NEW SET" : "KEEP", fresh, sets);
            if (sets && Click(tyres)) orders.Tyres = !orders.Tyres;
            y += 66f * k;

            IReadOnlyList<RaceDriver> lineup = Log.Car.Lineup;
            if (paint) Label(new Rect(x, y, w, 26f * k), "NEXT DRIVER", small, Dim, TextAnchor.MiddleLeft);
            y += 36f * k;
            int perRow = Mathf.Min(3, lineup.Count);
            float cw = (w - (perRow - 1) * 8f * k) / perRow;
            for (int i = 0; i < lineup.Count; i++)
            {
                var chip = new Rect(x + i % perRow * (cw + 8f * k), y + i / perRow * 60f * k, cw, 52f * k);
                if (paint) Chip(chip, Name(lineup[i].Id), Log.NextDriverIndex == i, true);
                if (Click(chip)) Log.NextDriverIndex = i;
            }

            var box = new Rect(x, panel.yMax - 24f * k - 76f * k, w, 76f * k);
            if (paint)
            {
                if (orders.Requested) Outline(box, Red, 9f);
                else Fill(box, Red, 9f);
                Label(box, orders.Requested ? "BOXING THIS LAP" : "PIT NOW", big, Color.white, TextAnchor.MiddleCenter);
            }
            if (!s.Retired && Click(box)) orders.Requested = !orders.Requested;
        }

        private void Step(Rect r, string sign)
        {
            Fill(r, new Color(1f, 1f, 1f, 0.1f), 8f);
            Label(r, sign, big, Color.white, TextAnchor.MiddleCenter);
        }

        private void Chip(Rect r, string label, bool on, bool usable)
        {
            if (on) Fill(r, White, 8f);
            else Outline(r, Edge, 8f);
            Color ink = on ? Ink : Color.white;
            if (!usable) ink.a = 0.3f;
            Label(r, label, small, ink, TextAnchor.MiddleCenter);
        }

        private string Name(string id)
        {
            if (id == RaceEntry.PlayerCharacterDriverId) return "YOU";
            string name = Game != null ? RaceWeekend.DriverName(id, Game) : id;
            int space = name.LastIndexOf(' ');
            return (space >= 0 ? name.Substring(space + 1) : name).ToUpperInvariant();
        }

        private void Style()
        {
            if (big != null && Mathf.Approximately(big.fontSize, Mathf.Max(1, Mathf.RoundToInt(28f * k)))) return;
            big = Make(bold, 28f);
            text = Make(semiBold, 18f);
            small = Make(bold, 16f);
            right = Make(semiBold, 18f);
        }

        private GUIStyle Make(Font font, float size) => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.RoundToInt(size * k)),
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
        };

        /// <summary>A rect on the 1920 by 1080 layout, in screen pixels, centred across a wider screen.</summary>
        private Rect R(float x, float y, float w, float h) => new Rect((x + offset) * k, y * k, w * k, h * k);

        private static void Label(Rect r, string s, GUIStyle style, Color colour, TextAnchor anchor)
        {
            style.alignment = anchor;
            style.normal.textColor = colour;
            GUI.Label(r, s, style);
        }

        private void Fill(Rect r, Color colour, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, Vector4.one * radius * k);

        private void Outline(Rect r, Color colour, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * radius * k);

        /// <summary>True when the rect is clicked. Draws nothing.</summary>
        private static bool Click(Rect r) => GUI.Button(r, GUIContent.none, GUIStyle.none);

        private void OnDestroy()
        {
            if (slash != null) Destroy(slash);
            if (disc != null) Destroy(disc);
            if (arrow != null) Destroy(arrow);
            if (map != null) Destroy(map.Texture);
            if (pip != null) pip.Release();
        }
    }
}
