using System.Collections.Generic;
using System.Globalization;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The first stage before the green: the TV broadcast's race preview. It opens on the start line from the air, with
    /// the event and the year. It flies a lap of the track (<see cref="PreviewPath"/>), with a diagram of the circuit
    /// beside it: the lap length, the number of turns, every turn numbered and the camera's place on the lap. Each turn
    /// is called out as the flyover reaches it. Then, over slow shots of the tightest corners, a panel at a time: each
    /// class's expected lap time, last year's podium in each class, past winners, and each class's favourite
    /// (<see cref="IntroFacts"/>). A panel with nothing to show is left out, as the podiums and winners are in the first
    /// year. The preview takes the camera (<see cref="RaceCameras.Held"/>) and hides the timing tower while it runs, and
    /// runs on the real clock, standing still while the game is paused. Laid out on a 1920 by 1080 screen.
    /// </summary>
    [DefaultExecutionOrder(100)]   // after RaceCameras, so the preview's shot is the one drawn
    public sealed class BroadcastIntro : SkippableStage
    {
        public Circuit Circuit;
        public RaceCameras Cameras;
        public RaceTower Tower;
        public IntroFacts Facts;

        public override string Name => "race preview";
        public override bool Done => done;

        /// <summary>How long each part runs, real seconds.</summary>
        private const float OpenTime = 7f, PanelTime = 8f;
        /// <summary>The preview's lens, degrees.</summary>
        private const float Fov = 50f;
        /// <summary>How long a graphic takes to come in and to go, real seconds.</summary>
        private const float In = 0.35f, Out = 0.3f;
        /// <summary>The title comes in this long into the opening shot, real seconds.</summary>
        private const float TitleAt = 0.8f;
        /// <summary>How many corners the panel shots go round.</summary>
        private const int PanelCorners = 4;

        private enum Kind { Open, Fly, Laps, Podiums, Winners, Favourites }

        private struct Part
        {
            public Kind Kind;
            public float Start, Length;
            public int Corner;
        }

        private static readonly Color Panel = new Color32(0x1a, 0x1a, 0x1c, 0xe0);
        private static readonly Color Under = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color Red = RaceTower.Hex(0xe10600);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color Mark = new Color(1f, 1f, 1f, 0.18f);

        private readonly List<Part> parts = new List<Part>();
        private PreviewPath path;
        private Camera cam;
        private bool done;
        private float clock;
        private int shown = -1;
        /// <summary>The camera's heading, a unit vector.</summary>
        private Vector3 aim;
        private bool hidTower;
        /// <summary>The turn called out, and when, on the preview's clock.</summary>
        private int called = -1;
        private float calledAt;

        private float k, offset, fade;
        private Font bold, semiBold;
        private GUIStyle title, header, cell, small, number;
        private Texture2D lead, disc;
        private TrackMap map;
        private readonly Dictionary<(string, int, float), string> fitted = new Dictionary<(string, int, float), string>();

        public override void Begin()
        {
            path = new PreviewPath(Circuit);
            cam = Cameras.GetComponent<Camera>();
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            lead = RaceTower.Shape(1f, 1f, (x, y) => x <= 1f - 0.18f * (1f - y));
            disc = RaceTower.Shape(1f, 1f, (x, y) => (x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f) < 0.25f);
            map = new TrackMap(Circuit, 380, 500, 26f, 5f);

            List<int> corners = path.Tightest(PanelCorners);
            Add(Kind.Open, OpenTime, -1);
            Add(Kind.Fly, path.Duration, -1);
            if (Facts != null)
            {
                int shot = 0;
                int Next() => corners.Count > 0 ? corners[shot++ % corners.Count] : -1;
                if (Facts.Laps.Count > 0) Add(Kind.Laps, PanelTime, Next());
                if (Facts.Podiums.Count > 0) Add(Kind.Podiums, PanelTime, Next());
                if (Facts.Winners.Count > 1) Add(Kind.Winners, PanelTime, Next());
                if (Facts.Favourites.Count > 0) Add(Kind.Favourites, PanelTime, Next());
            }
            clock = 0f;
            Cameras.Held = true;
            if (Tower != null && !Tower.Hidden)
            {
                Tower.Hidden = true;
                hidTower = true;
            }
        }

        private void Add(Kind kind, float length, int corner)
        {
            float start = parts.Count > 0 ? parts[parts.Count - 1].Start + parts[parts.Count - 1].Length : 0f;
            parts.Add(new Part { Kind = kind, Start = start, Length = length, Corner = corner });
        }

        public override void Skip() => End();

        /// <summary>Gives the camera and the timing tower back.</summary>
        private void End()
        {
            if (done) return;
            done = true;
            if (Cameras != null) Cameras.Held = false;
            if (hidTower && Tower != null) Tower.Hidden = false;
            enabled = false;
        }

        /// <summary>The part running now, or -1 once the last is over.</summary>
        private int Now()
        {
            for (int i = 0; i < parts.Count; i++)
                if (clock < parts[i].Start + parts[i].Length) return i;
            return -1;
        }

        private void Update()
        {
            if (done || path == null) return;
            if (!RaceWeekend.Paused) clock += Time.unscaledDeltaTime;
            if (Now() < 0) End();
        }

        private void LateUpdate()
        {
            if (done || path == null || cam == null) return;
            int i = Now();
            if (i < 0) return;
            Part p = parts[i];
            float into = clock - p.Start, u = into / p.Length;
            PreviewPath.Pose pose = p.Kind == Kind.Open ? path.Open(u)
                : p.Kind == Kind.Fly ? path.Fly(into)
                : p.Corner >= 0 ? path.Orbit(p.Corner, u)
                : path.Open(u);
            // A new part is a cut: the camera starts looking at its target. Within a part its heading follows, eased
            // and no faster than PreviewPath.TurnRate, so a hairpin is a pan rather than a whip.
            Vector3 want = pose.Target - pose.Position;
            want = want.sqrMagnitude > 1e-4f ? want.normalized : Vector3.forward;
            aim = i != shown ? want : PreviewPath.Turn(aim, want, Time.unscaledDeltaTime);
            shown = i;
            cam.transform.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(aim, Vector3.up));
            cam.fieldOfView = Fov;
            if (Cameras.Hud != null) Cameras.Hud.Hidden = true;
        }

        // ---------------------------------------------------------------- graphics

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || done || DebugMenu.Bare) return;
            int i = Now();
            if (i < 0) return;
            k = Screen.height / 1080f;
            offset = (Screen.width / k - 1920f) / 2f;
            Style();
            Part p = parts[i];
            float into = clock - p.Start;
            switch (p.Kind)
            {
                case Kind.Open:
                    if (Fade(into - TitleAt, p.Length - TitleAt)) Title();
                    break;
                case Kind.Fly:
                    if (Fade(into, p.Length)) Diagram(path.DistanceAt(into));
                    break;
                case Kind.Laps:
                    if (Fade(into, p.Length)) Laps(into);
                    break;
                case Kind.Podiums:
                    if (Fade(into, p.Length)) Podiums(into);
                    break;
                case Kind.Winners:
                    if (Fade(into, p.Length)) Winners(into);
                    break;
                case Kind.Favourites:
                    if (Fade(into, p.Length)) Favourites(into);
                    break;
            }
        }

        /// <summary>Sets the graphic's fade from how far into its time it is: in over <see cref="In"/>, out over <see cref="Out"/>. False while it is not up.</summary>
        private bool Fade(float into, float length)
        {
            if (into < 0f || into > length) return false;
            fade = Mathf.Clamp01(Mathf.Min(into / In, (length - into) / Out));
            return fade > 0f;
        }

        /// <summary>How far a graphic slides as it comes in, px on the layout.</summary>
        private float Slide => (1f - fade) * 40f;

        private const string EventName = "THE 24H TARMAC GAUNTLET";

        /// <summary>The event and the year, low in the middle of the screen, above the skip hint.</summary>
        private void Title()
        {
            string year = Facts != null ? Facts.Year.ToString(CultureInfo.InvariantCulture) : IntroFacts.CalendarYear(1).ToString(CultureInfo.InvariantCulture);
            float h = 104f;
            float yw = Width(title, year) + 56f, ew = Width(title, EventName) + 72f;
            float x = 960f - (yw + ew) / 2f, y = 1080f - 200f - h + Slide;
            Fill(R(x, y, yw + ew, h), Panel, 10f);
            Draw(R(x, y, yw + 18f, h), lead, Red);
            Label(R(x, y, yw, h), year, title, Color.white, TextAnchor.MiddleCenter);
            Label(R(x + yw + 36f, y, ew - 36f, h), EventName, title, Color.white, TextAnchor.MiddleLeft);
        }

        /// <summary>The circuit diagram on the right, and the turn being called out on the left.</summary>
        private void Diagram(float distance)
        {
            const float W = 420f, H = 620f, Top = 64f;
            float x = 1920f - 72f - W + Slide, y = 200f;
            Fill(R(x, y, W, H), Panel, 10f);
            string length = (Circuit.Lap / 1000f).ToString("0.000", CultureInfo.InvariantCulture) + " KM";
            string turns = path.Corners + " TURNS";
            Label(R(x + 24f, y, W - 48f, Top), length, header, Color.white, TextAnchor.MiddleLeft);
            Label(R(x + 24f, y, W - 48f, Top), turns, cell, Dim, TextAnchor.MiddleRight);
            GUI.DrawTexture(R(x, y + Top - 2f, W, 2f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(Under), 0f, 0f);

            Rect inner = map.Fit(R(x + 20f, y + Top + 10f, W - 40f, H - Top - 30f));
            GUI.DrawTexture(inner, map.Texture, ScaleMode.StretchToFill, true, 0f, A(Track), 0f, 0f);
            int calling = path.CornerAt(distance);
            CornerData[] corners = Circuit.Data.corners;
            for (int c = 0; c < path.Corners; c++)
            {
                CornerData corner = corners[c];
                int mid = Circuit.IndexOf((corner.s + corner.e) / 2f);
                Vector3 centre = Circuit.Centre[mid];
                Vector2 at = map.At(inner, centre);
                Vector2 away = map.At(inner, centre + Circuit.Left[mid] * (PreviewPath.Direction(corner) * 10f)) - at;
                Vector2 spot = at + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up) * 22f * k;
                float s = 28f * k;
                var r = new Rect(spot.x - s / 2f, spot.y - s / 2f, s, s);
                GUI.DrawTexture(r, disc, ScaleMode.StretchToFill, true, 0f, A(c == calling ? Red : Mark), 0f, 0f);
                Label(r, corner.number.ToString(CultureInfo.InvariantCulture), number, Color.white, TextAnchor.MiddleCenter);
            }
            Vector3 here = Circuit.Centre[Circuit.IndexOf(distance)];
            Dot(inner, here, 20f, Panel);
            Dot(inner, here, 14f, Color.white);

            if (calling != called)
            {
                called = calling;
                calledAt = clock;
            }
            if (calling >= 0) Turn(corners[calling].number);
        }

        /// <summary>The turn called out, bottom left: its number on red. Each call comes in afresh.</summary>
        private void Turn(int n)
        {
            float was = fade;
            fade = Mathf.Min(was, Mathf.Clamp01((clock - calledAt) / In));
            const float H = 96f;
            string word = "TURN";
            string digits = n.ToString(CultureInfo.InvariantCulture);
            float nw = Mathf.Max(H, Width(title, digits) + 40f), ww = Width(header, word) + 48f;
            float x = 72f - Slide, y = 1080f - 200f - H;
            Fill(R(x, y, nw + ww, H), Panel, 10f);
            Label(R(x + 24f, y, ww - 24f, H), word, header, Color.white, TextAnchor.MiddleLeft);
            Draw(R(x + ww, y, nw, H), lead, Red);
            Label(R(x + ww, y, nw - 10f, H), digits, title, Color.white, TextAnchor.MiddleCenter);
            fade = was;
        }

        // The panels: a red header over a row per class, centred on the screen.
        private const float HeaderHeight = 64f, RowHeight = 56f, Pad = 24f, BadgeWidth = 44f, BadgeHeight = 30f;

        /// <summary>Draws a panel's frame and header, and returns its top left, px on the layout.</summary>
        private Vector2 Frame(string heading, float width, int rows)
        {
            float h = HeaderHeight + rows * RowHeight + 12f;
            float x = 960f - width / 2f, y = 520f - h / 2f + Slide;
            Fill(R(x, y, width, h), Panel, 10f);
            GUI.DrawTexture(R(x, y, width, HeaderHeight), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(Red), Vector4.zero, new Vector4(10f, 10f, 0f, 0f) * k);
            Label(R(x + Pad, y, width - 2f * Pad, HeaderHeight), heading, header, Color.white, TextAnchor.MiddleLeft);
            return new Vector2(x, y + HeaderHeight + 6f);
        }

        /// <summary>Fades each row in a little after the one above it.</summary>
        private float Row(int j, float into) => Mathf.Clamp01((into - In - 0.07f * j) / 0.25f);

        private void Laps(float into)
        {
            List<IntroFacts.ClassLap> laps = Facts.Laps;
            float width = Pad + BadgeWidth + 28f + Width(cell, "0:00.0") + 40f + Pad;
            width = Mathf.Max(width, Width(header, "LAP TIMES") + 2f * Pad);
            Vector2 at = Frame("LAP TIMES", width, laps.Count);
            float was = fade;
            for (int j = 0; j < laps.Count; j++)
            {
                fade = was * Row(j, into);
                float y = at.y + j * RowHeight;
                Badge(at.x + Pad, y, laps[j].Class);
                Label(R(at.x + Pad + BadgeWidth + 28f, y, width - BadgeWidth - 2f * Pad - 28f, RowHeight), IntroFacts.LapText(laps[j].Seconds), cell, Color.white, TextAnchor.MiddleLeft);
            }
            fade = was;
        }

        private void Podiums(float into)
        {
            List<IntroFacts.Podium> podiums = Facts.Podiums;
            const float Place = 28f, Team = 300f, Gap = 24f;
            float width = Pad + BadgeWidth + 3f * (Gap + Place + Team) + Pad;
            Vector2 at = Frame(Facts.PodiumYear.ToString(CultureInfo.InvariantCulture) + " PODIUMS", width, podiums.Count);
            float was = fade;
            for (int j = 0; j < podiums.Count; j++)
            {
                fade = was * Row(j, into);
                float y = at.y + j * RowHeight;
                Badge(at.x + Pad, y, podiums[j].Class);
                float x = at.x + Pad + BadgeWidth;
                for (int p = 0; p < podiums[j].Teams.Length; p++)
                {
                    x += Gap;
                    Label(R(x, y, Place, RowHeight), (p + 1).ToString(CultureInfo.InvariantCulture), cell, Dim, TextAnchor.MiddleLeft);
                    Label(R(x + Place, y, Team, RowHeight), Fit(podiums[j].Teams[p].ToUpperInvariant(), cell, Team), cell, Color.white, TextAnchor.MiddleLeft);
                    x += Place + Team;
                }
            }
            fade = was;
        }

        private void Winners(float into)
        {
            List<IntroFacts.Winner> winners = Facts.Winners;
            const float Year = 90f, Team = 420f, Gap = 24f;
            float width = Pad + Year + Gap + Team + Gap + BadgeWidth + Pad;
            Vector2 at = Frame("WINNERS", width, winners.Count);
            float was = fade;
            for (int j = 0; j < winners.Count; j++)
            {
                fade = was * Row(j, into);
                float y = at.y + j * RowHeight;
                Label(R(at.x + Pad, y, Year, RowHeight), winners[j].Year.ToString(CultureInfo.InvariantCulture), cell, Dim, TextAnchor.MiddleLeft);
                Label(R(at.x + Pad + Year + Gap, y, Team, RowHeight), Fit(winners[j].Team.ToUpperInvariant(), cell, Team), cell, Color.white, TextAnchor.MiddleLeft);
                Badge(at.x + Pad + Year + Gap + Team + Gap, y, winners[j].Class);
            }
            fade = was;
        }

        private void Favourites(float into)
        {
            List<IntroFacts.Favourite> favourites = Facts.Favourites;
            const float Team = 420f, Model = 300f, Gap = 28f;
            float width = Pad + BadgeWidth + Gap + Team + Gap + Model + Pad;
            Vector2 at = Frame("FAVOURITES", width, favourites.Count);
            float was = fade;
            for (int j = 0; j < favourites.Count; j++)
            {
                fade = was * Row(j, into);
                float y = at.y + j * RowHeight;
                Badge(at.x + Pad, y, favourites[j].Class);
                float x = at.x + Pad + BadgeWidth + Gap;
                Label(R(x, y, Team, RowHeight), Fit(favourites[j].Team.ToUpperInvariant(), cell, Team), cell, Color.white, TextAnchor.MiddleLeft);
                Label(R(x + Team + Gap, y, Model, RowHeight), Fit(favourites[j].Model, small, Model), small, Dim, TextAnchor.MiddleLeft);
            }
            fade = was;
        }

        // ---------------------------------------------------------------- drawing

        private void Badge(float x, float rowTop, CarClass c)
        {
            Rect r = R(x, rowTop + (RowHeight - BadgeHeight) / 2f, BadgeWidth, BadgeHeight);
            Color colour = RaceTower.ClassColour(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, A(colour), Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * 6f * k);
            Label(r, RaceTower.ClassLetter(c), number, colour, TextAnchor.MiddleCenter);
        }

        private void Dot(Rect inner, Vector3 p, float size, Color colour)
        {
            Vector2 at = map.At(inner, p);
            float s = size * k;
            GUI.DrawTexture(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), disc, ScaleMode.StretchToFill, true, 0f, A(colour), 0f, 0f);
        }

        /// <summary>A colour at the graphic's fade.</summary>
        private Color A(Color c) => new Color(c.r, c.g, c.b, c.a * fade);

        /// <summary>Text width, px on the layout.</summary>
        private float Width(GUIStyle style, string s) => style.CalcSize(new GUIContent(s)).x / k;

        /// <summary>The text, cut short with an ellipsis if it is wider than the room, px on the layout.</summary>
        private string Fit(string s, GUIStyle style, float room)
        {
            var key = (s, style.fontSize, room);
            if (fitted.TryGetValue(key, out string f)) return f;
            f = s;
            if (Width(style, f) > room)
            {
                while (f.Length > 1 && Width(style, f + "...") > room) f = f.Substring(0, f.Length - 1);
                f = f.TrimEnd() + "...";
            }
            fitted[key] = f;
            return f;
        }

        private void Style()
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(26f * k));
            if (cell != null && cell.fontSize == key) return;
            fitted.Clear();
            title = Make(bold, 52f);
            header = Make(bold, 30f);
            cell = Make(bold, 26f);
            small = Make(semiBold, 22f);
            number = Make(bold, 16f);
        }

        private GUIStyle Make(Font font, float size) => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.RoundToInt(size * k)),
            clipping = TextClipping.Overflow,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
        };

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

        private void OnDestroy()
        {
            if (Cameras != null && !done) Cameras.Held = false;
            if (lead != null) Destroy(lead);
            if (disc != null) Destroy(disc);
            if (map != null) Destroy(map.Texture);
        }
    }
}
