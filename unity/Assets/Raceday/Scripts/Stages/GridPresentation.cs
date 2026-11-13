using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The second stage before the green: the broadcast presents the starting grid, in the order of
    /// <see cref="GridRunsheet"/>. Each featured car gets a slow dolly shot from in front of it, wherever it stands,
    /// with a strip bottom left: its place, driver, team, model and class. Pole and each class pole carry a tab
    /// saying so, and your car's strip is light, as your row is on the timing tower. Then the whole grid shows on
    /// the board, staggered two by two as the cars line up, a page at a time, over a crane shot across the field.
    /// The stage has the camera and hides the timing tower and the HUD while it runs, and gives them back when it ends or is skipped.
    /// Laid out on a 1920 by 1080 screen, like <see cref="BroadcastGraphics"/>.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after RaceCameras, so this stage's shot is the one drawn
    public sealed class GridPresentation : SkippableStage
    {
        public RaceCar[] Cars;
        public string[] Teams;
        public CarClass[] Classes;
        public Color[] Colours;
        /// <summary>Each car's model name.</summary>
        public string[] Models;
        public RaceDay Day;
        public GameState Game;
        /// <summary>Your car's place on the grid, or -1 for none.</summary>
        public int Player = -1;
        public RaceTower Tower;
        public DriveHud Hud;
        public Camera Camera;

        public override string Name => "grid";
        public override bool Done => done;

        /// <summary>A featured car is on this long, real seconds.</summary>
        private const float CarTime = 4.5f;
        /// <summary>A page of the board is up this long, real seconds.</summary>
        private const float PageTime = 5.5f;
        /// <summary>Graphics slide and fade in over this long, real seconds.</summary>
        private const float InTime = 0.3f;

        private List<GridRunsheet.Beat> beats;
        private int at = -1;
        private float clock;
        private bool done, towerWas;

        // The shot on air: the camera moves from one point to the other over the beat, both measured from the car, looking at it.
        private Vector3 from, to, target;
        private float fov;
        private bool focus;
        private int side = 1;
        // The crane over the field, which runs on across every page of the board.
        private bool craned;
        private Vector3 craneFrom, craneTo, craneLook;
        private float boardStart, boardLength;

        private static readonly Color Panel = new Color32(0x1a, 0x1a, 0x1c, 0xe0);
        private static readonly Color Under = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color Red = RaceTower.Hex(0xe10600);
        private static readonly Color Light = RaceTower.Hex(0xf2f2f2);
        private static readonly Color Dark = RaceTower.Hex(0x111111);

        private float k, offset, alpha;
        private Font bold, semiBold;
        private GUIStyle pos, nameStyle, first, small, tab, badge, cellName, cellTeam, cellPos, title;
        private Texture2D slash, lead;
        private readonly Dictionary<(string, int), string> fitted = new Dictionary<(string, int), string>();

        public override void Begin()
        {
            beats = GridRunsheet.For(Classes ?? new CarClass[0], Player);
            at = 0;
            clock = 0f;
            done = beats.Count == 0 || Cars == null;
            if (Tower != null) towerWas = Tower.Hidden;
            if (!done) Place();
        }

        public override void Skip() => Finish();

        private void Finish()
        {
            if (done) return;
            done = true;
            if (Tower != null) Tower.Hidden = towerWas;
        }

        private bool Running => beats != null && !done;
        private GridRunsheet.Beat Beat => beats[at];

        private static float Length(GridRunsheet.Beat b) => b.Kind == GridRunsheet.Kind.Board ? PageTime : CarTime;

        private void Start()
        {
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            float lean = Mathf.Tan(24f * Mathf.Deg2Rad);
            slash = RaceTower.Shape(0.42f + 1.25f * lean, 1.25f, (x, y) => x >= y * lean && x <= y * lean + 0.42f);
            lead = RaceTower.Shape(1f, 1f, (x, y) => x <= 1f - 0.18f * (1f - y));
        }

        private void Update()
        {
            if (!Running || RaceWeekend.Paused) return;
            clock += Time.unscaledDeltaTime;
            if (clock < Length(Beat)) return;
            clock = 0f;
            at++;
            if (at >= beats.Count) Finish();
            else Place();
        }

        private void LateUpdate()
        {
            if (!Running || Camera == null) return;
            if (Tower != null) Tower.Hidden = true;
            if (Hud != null) Hud.Hidden = true;
            float t = Mathf.Clamp01(clock / Length(Beat));
            Vector3 p, look;
            if (Beat.Kind == GridRunsheet.Kind.Board)
            {
                float c = Mathf.Clamp01((boardStart + clock) / boardLength);
                c = c * c * (3f - 2f * c);
                p = Vector3.Lerp(craneFrom, craneTo, c);
                look = craneLook;
            }
            else
            {
                // The shot rides with the car, should it move.
                RaceCar car = Cars[Beat.Car];
                Vector3 now = car != null ? car.transform.position + Vector3.up * 0.7f : target;
                t = t * t * (3f - 2f * t);
                p = now + Vector3.Lerp(from, to, t);
                look = now;
            }
            Camera.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look - p, Vector3.up));
            Camera.fieldOfView = fov;
            if (focus) PostEffects.Focus(Camera, Vector3.Distance(p, look));
        }

        // ---------------------------------------------------------------- shots

        /// <summary>Sets up the shot for the beat now on air.</summary>
        private void Place()
        {
            GridRunsheet.Beat b = Beat;
            if (b.Kind == GridRunsheet.Kind.Board)
            {
                Crane(b.Car);
                return;
            }
            RaceCar car = Cars[b.Car];
            if (car == null) { focus = false; return; }
            Transform t = car.transform;
            target = t.position + Vector3.up * 0.7f;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            // Alternate sides from one car to the next, as a TV crew walks the grid, unless the other side is clearer.
            side = -side;
            float best = float.MinValue, angle = 30f;
            int bestSide = side;
            foreach (int s in new[] { side, -side })
                foreach (float a in Angles)
                {
                    Vector3 p = target + Quaternion.AngleAxis(s * a, Vector3.up) * fwd * Distance + Vector3.up * 0.5f;
                    float clear = Clearance(p, target, car);
                    if (clear >= Clear) { best = clear; angle = a; bestSide = s; goto found; }
                    if (clear > best) (best, angle, bestSide) = (clear, a, s);
                }
            found:
            side = bestSide;
            // The dolly swings in a little toward the car's nose and rises as it goes.
            from = Quaternion.AngleAxis(side * angle, Vector3.up) * fwd * Distance + Vector3.up * 0.4f;
            to = Quaternion.AngleAxis(side * (angle - Swing), Vector3.up) * fwd * (Distance - 0.8f) + Vector3.up * 0.8f;
            fov = 2f * Mathf.Atan(Frame / Distance) * Mathf.Rad2Deg;
            focus = true;
        }

        /// <summary>How far round from the nose the camera stands, degrees, in order of preference.</summary>
        private static readonly float[] Angles = { 32f, 22f, 44f, 12f };
        /// <summary>The camera stands this far from the car's middle, m.</summary>
        private const float Distance = 7.5f;
        /// <summary>The dolly swings round this far over the shot, degrees.</summary>
        private const float Swing = 10f;
        /// <summary>Half the frame's height at the car, m.</summary>
        private const float Frame = 2.6f;
        /// <summary>Room the line of sight wants from any other car, m.</summary>
        private const float Clear = 2.8f;

        /// <summary>
        /// The least room between the line from the camera to the car and any other car, m, or nothing if a wall
        /// or a building is in the way. Cars are measured by their middles.
        /// </summary>
        private float Clearance(Vector3 camera, Vector3 car, RaceCar self)
        {
            Vector3 d = car - camera;
            float length = d.magnitude;
            foreach (RaycastHit hit in Physics.RaycastAll(camera, d / length, length, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<RaceCar>() == null) return float.MinValue + 1f;
            float least = float.MaxValue;
            foreach (RaceCar other in Cars)
            {
                if (other == null || other == self || !other.isActiveAndEnabled) continue;
                Vector3 o = other.transform.position + Vector3.up * 0.7f;
                float along = Mathf.Clamp(Vector3.Dot(o - camera, d) / (length * length), 0f, 1f);
                least = Mathf.Min(least, Vector3.Distance(o, camera + d * along));
            }
            return least;
        }

        /// <summary>The crane over the field for the board: set up once, on the first page, and run across every page.</summary>
        private void Crane(int page)
        {
            focus = false;
            fov = 40f;
            int pages = 0;
            foreach (GridRunsheet.Beat b in beats) if (b.Kind == GridRunsheet.Kind.Board) pages++;
            boardLength = pages * PageTime;
            boardStart = page / GridRunsheet.PageSize * PageTime;
            if (craned) return;
            craned = true;
            Vector3 middle = Vector3.zero, heading = Vector3.zero;
            int n = 0;
            foreach (RaceCar car in Cars)
            {
                if (car == null || !car.isActiveAndEnabled) continue;
                middle += car.transform.position;
                heading += Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
                n++;
            }
            if (n == 0) return;
            middle /= n;
            // Cars in their garages all face the pit lane: the field's heading is then across it, so the crane
            // looks along whichever line the cars make, from the side they face.
            Vector3 fwd = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, fwd);
            craneLook = middle;
            craneFrom = middle + fwd * 55f - across * 35f + Vector3.up * 32f;
            craneTo = middle + fwd * 55f + across * 25f + Vector3.up * 26f;
        }

        // ---------------------------------------------------------------- graphics

        private void OnGUI()
        {
            if (!Running || Event.current.type != EventType.Repaint || DebugMenu.Bare) return;
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            k = Screen.height / 1080f;
            offset = (Screen.width / k - 1920f) / 2f;
            Style();
            alpha = Mathf.Clamp01(clock / InTime);
            GridRunsheet.Beat b = Beat;
            if (b.Kind == GridRunsheet.Kind.Board) Board(b.Car);
            else if (Cars[b.Car] != null) Strip(b.Car, b.Kind);
        }

        /// <summary>The featured car's strip, bottom left, sliding in from the left.</summary>
        private void Strip(int i, GridRunsheet.Kind kind)
        {
            string full = DriverName(i);
            int space = full.LastIndexOf(' ');
            string initial = space > 0 ? full.Substring(0, 1).ToUpperInvariant() + "." : "";
            string surname = (space > 0 ? full.Substring(space + 1) : full).ToUpperInvariant();
            string teamName = Teams[i].ToUpperInvariant();
            string model = Models[i];
            string letter = RaceTower.ClassLetter(Classes[i]);
            bool mine = kind == GridRunsheet.Kind.Yours;
            Color fill = mine ? Light : Panel;
            Color text = mine ? Dark : Color.white;

            float initialWidth = initial.Length > 0 ? W(first, initial) + 6f : 0f;
            float nameWidth = W(nameStyle, surname);
            float teamWidth = W(small, teamName), modelWidth = W(small, model);
            float width = Mathf.Max(520f, Mathf.Max(112f + initialWidth + nameWidth + 24f, 82f + teamWidth + 14f + modelWidth + 14f + 30f + 24f));
            float slide = (1f - Ease(alpha)) * 60f;
            float x = 72f - slide, y = 1080f - 112f - 108f;   // clear of the skip hint below

            string label = kind == GridRunsheet.Kind.Pole ? "POLE" : kind == GridRunsheet.Kind.ClassPole ? "CLASS POLE" : null;
            if (label != null)
            {
                float tw = W(tab, label) + 28f;
                Fill(R(x, y - 36f, tw, 36f), Red, 6f);
                Label(R(x, y - 36f, tw, 36f), label, tab, Color.white, TextAnchor.MiddleCenter);
            }
            Fill(R(x, y, width, 108f), fill, 9f);
            if (kind == GridRunsheet.Kind.Pole || kind == GridRunsheet.Kind.ClassPole) Draw(R(x, y, 68f, 68f), lead, Red);
            Label(R(x, y, 68f, 68f), (i + 1).ToString(), pos, kind == GridRunsheet.Kind.Pole || kind == GridRunsheet.Kind.ClassPole ? Color.white : text, TextAnchor.MiddleCenter);
            Draw(R(x + 82f, y + 14f, 16f, 40f), slash, Colours[i]);
            float nx = x + 112f;
            if (initial.Length > 0) Label(R(nx, y, initialWidth, 68f), initial, first, text, TextAnchor.MiddleLeft);
            Label(R(nx + initialWidth, y, nameWidth + 4f, 68f), surname, nameStyle, text, TextAnchor.MiddleLeft);
            GUI.DrawTexture(R(x, y + 68f, width, 40f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(Under), Vector4.zero, new Vector4(0f, 0f, 9f * k, 9f * k));
            Label(R(x + 82f, y + 68f, teamWidth, 40f), teamName, small, text, TextAnchor.MiddleLeft);
            Label(R(x + 82f + teamWidth + 14f, y + 68f, modelWidth + 4f, 40f), model, small, new Color(text.r, text.g, text.b, 0.7f), TextAnchor.MiddleLeft);
            Badge(R(x + width - 24f - 30f, y + 68f + 8f, 30f, 24f), letter, RaceTower.ClassColour(Classes[i]), badge);
        }

        // The board's layout, px on the layout.
        private const float CellWidth = 540f, CellHeight = 58f, RowPitch = 64f, ColumnGap = 28f, BoardPad = 24f, TitleHeight = 64f;

        /// <summary>A page of the board: places from <paramref name="page"/>, odd places on the left, even places a half row lower on the right.</summary>
        private void Board(int page)
        {
            int count = Mathf.Min(GridRunsheet.PageSize, Cars.Length - page);
            int rows = (count + 1) / 2;
            float width = BoardPad * 2f + CellWidth * 2f + ColumnGap;
            float height = TitleHeight + rows * RowPitch + RowPitch / 2f + BoardPad;
            float x = 960f - width / 2f, y = Mathf.Max(48f, (1080f - height) / 2f);
            // The board stays up from page to page: only the first page fades in with it, the later ones' places alone.
            float cells = alpha;
            if (at > 0 && beats[at - 1].Kind == GridRunsheet.Kind.Board) alpha = 1f;
            Fill(R(x, y, width, height), Panel, 9f);
            Draw(R(x, y, 18f, TitleHeight), lead, Red);
            Label(R(x + BoardPad + 8f, y, width, TitleHeight), "STARTING GRID", title, Color.white, TextAnchor.MiddleLeft);
            alpha = cells;
            float top = y + TitleHeight;
            for (int n = 0; n < count; n++)
            {
                int i = page + n;
                int row = n / 2, column = n % 2;
                float cx = x + BoardPad + column * (CellWidth + ColumnGap);
                float cy = top + row * RowPitch + column * RowPitch / 2f;
                Cell(i, cx, cy);
            }
        }

        private void Cell(int i, float x, float y)
        {
            bool mine = i == Player;
            Color text = mine ? Dark : Color.white;
            if (mine) Fill(R(x, y, CellWidth, CellHeight), Light, 6f);
            else Fill(R(x, y, CellWidth, CellHeight), Under, 6f);
            bool leads = GridRunsheet.LeadsClass(Classes, i);
            if (leads) Draw(R(x, y, 58f, CellHeight), lead, Red);
            Label(R(x, y, 58f, CellHeight), (i + 1).ToString(), cellPos, leads ? Color.white : text, TextAnchor.MiddleCenter);
            Draw(R(x + 66f, y + 13f, 13f, 32f), slash, Colours[i]);
            string letter = RaceTower.ClassLetter(Classes[i]);
            float badgeWidth = Mathf.Max(30f, W(badge, letter) + 16f);
            float room = CellWidth - 92f - badgeWidth - 22f;
            string full = DriverName(i);
            int space = full.LastIndexOf(' ');
            string surname = Fit((space > 0 ? full.Substring(space + 1) : full).ToUpperInvariant(), cellName, room);
            string teamName = Fit(Teams[i].ToUpperInvariant(), cellTeam, room);
            Label(R(x + 92f, y + 2f, room, 32f), surname, cellName, text, TextAnchor.MiddleLeft);
            Label(R(x + 92f, y + 31f, room, 24f), teamName, cellTeam, new Color(text.r, text.g, text.b, 0.65f), TextAnchor.MiddleLeft);
            Badge(R(x + CellWidth - 14f - badgeWidth, y + (CellHeight - 26f) / 2f, badgeWidth, 26f), letter, RaceTower.ClassColour(Classes[i]), badge);
        }

        private string DriverName(int i)
        {
            if (Day == null || i >= Day.Cars.Count) return "";
            string id = Day.Cars[i].Driver.Id;
            return Game != null ? RaceWeekend.DriverName(id, Game) : id;
        }

        /// <summary>The text, cut short with an ellipsis if it is wider than the room, px on the layout.</summary>
        private string Fit(string s, GUIStyle style, float room)
        {
            var key = (s, style.fontSize * 10000 + Mathf.RoundToInt(room));
            if (fitted.TryGetValue(key, out string done)) return done;
            string shown = s;
            if (W(style, s) > room)
            {
                int n = s.Length;
                while (n > 1 && W(style, s.Substring(0, n).TrimEnd() + "…") > room) n--;
                shown = s.Substring(0, n).TrimEnd() + "…";
            }
            fitted[key] = shown;
            return shown;
        }

        private static float Ease(float t) => 1f - (1f - t) * (1f - t);

        private void Style()
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(30f * k));
            if (pos != null && pos.fontSize == key) return;
            fitted.Clear();
            pos = Make(bold, 30f);
            nameStyle = Make(bold, 34f);
            first = Make(semiBold, 26f);
            small = Make(semiBold, 19f);
            tab = Make(bold, 17f);
            badge = Make(bold, 16f);
            cellPos = Make(bold, 24f);
            cellName = Make(bold, 23f);
            cellTeam = Make(semiBold, 15f);
            title = Make(bold, 30f);
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

        /// <summary>A text's width, px on the layout.</summary>
        private float W(GUIStyle style, string s) => style.CalcSize(new GUIContent(s)).x / k;

        /// <summary>A colour at the graphics' fade.</summary>
        private Color A(Color c) => new Color(c.r, c.g, c.b, c.a * alpha);

        /// <summary>A rect on the 1920 by 1080 layout, in screen pixels, centred across a wider screen.</summary>
        private Rect R(float x, float y, float w, float h) => new Rect((x + offset) * k, y * k, w * k, h * k);

        private void Label(Rect r, string s, GUIStyle style, Color colour, TextAnchor anchor)
        {
            style.alignment = anchor;
            style.normal.textColor = A(colour);
            GUI.Label(r, s, style);
        }

        private void Fill(Rect r, Color colour, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(colour), Vector4.zero, Vector4.one * radius * k);

        private void Draw(Rect r, Texture2D shape, Color colour) =>
            GUI.DrawTexture(r, shape, ScaleMode.StretchToFill, true, 0f, A(colour), 0f, 0f);

        private void Badge(Rect r, string letter, Color colour, GUIStyle style)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(colour), Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * 6f * k);
            Label(r, letter, style, colour, TextAnchor.MiddleCenter);
        }

        private void OnDestroy()
        {
            if (Tower != null && Running) Tower.Hidden = towerWas;
            if (slash != null) Destroy(slash);
            if (lead != null) Destroy(lead);
        }
    }
}
