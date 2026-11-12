using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The broadcast's graphics, in the timing tower's style, shown only while the director has the
    /// camera (<see cref="BroadcastDirector.OnAir"/>). The battle strip, bottom centre, shows a fight:
    /// both cars and the gap in seconds, with an amber arrow while the chaser closes. The driver strip,
    /// bottom left, names the driver the first time a car is on air in a stint. The pit timer, bottom
    /// right, runs while a stop is on air, with its fuel, tyres and driver change lit as each is done.
    /// The bottom graphics share one baseline, keep right of the timing tower, and the driver strip and
    /// pit timer rise above the battle strip rather than sit over it.
    /// The track map, right, shows during helicopter shots, and always with Track map on in the settings. Laid out on a 1920 by 1080 screen.
    /// </summary>
    public sealed class BroadcastGraphics : MonoBehaviour
    {
        public BroadcastDirector Director;
        public RaceCar[] Cars;
        public AiDriver[] Drivers;
        public string[] Teams;
        public CarClass[] Classes;
        public Color[] Colours;
        /// <summary>Each car's model name.</summary>
        public string[] Models;
        public RaceDay Day;
        public GameState Game;
        public Circuit Circuit;
        public int Player = -1;
        public RaceTower Tower;

        /// <summary>The driver strip shows this long, real seconds.</summary>
        private const float StripTime = 6f;

        private static readonly Color Panel = new Color32(0x1a, 0x1a, 0x1c, 0xe0);
        private static readonly Color Under = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color Red = RaceTower.Hex(0xe10600);
        private static readonly Color Amber = RaceTower.Hex(0xffb020);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.3f);

        private readonly Dictionary<int, string> named = new Dictionary<int, string>();
        private int stories = -1;
        private int strip = -1;
        private float stripUntil;
        private int pairFront = -1, pairBack = -1;
        private float lastGap, gapAt, trend;

        private float k, offset;
        private Font bold, semiBold, regular;
        private GUIStyle pos, team, badge, nameStyle, first, small, timer, gapStyle;
        private Texture2D slash, lead, arrow, disc, fuel, tyre, helmet;
        private TrackMap map;

        private void Start()
        {
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            regular = semiBold;
            float lean = Mathf.Tan(24f * Mathf.Deg2Rad);
            slash = RaceTower.Shape(0.42f + 1.25f * lean, 1.25f, (x, y) => x >= y * lean && x <= y * lean + 0.42f);
            lead = RaceTower.Shape(1f, 1f, (x, y) => x <= 1f - 0.18f * (1f - y));
            arrow = RaceTower.Shape(1.14f, 1f, (x, y) => Mathf.Abs(x - 0.57f) <= 0.57f * y);
            disc = RaceTower.Shape(1f, 1f, (x, y) => (x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f) < 0.25f);
            tyre = RaceTower.TyreIcon();
            fuel = RaceTower.FuelIcon();
            helmet = RaceTower.HelmetIcon();
            map = new TrackMap(Circuit, 300, 400, 20f, 5f);
        }

        private void Update()
        {
            if (Director == null || !Director.OnAir) return;
            // A new story: name the driver the first time their car is on air in a stint.
            if (Director.Stories != stories)
            {
                stories = Director.Stories;
                int s = Director.Subject;
                if (s >= 0 && s < Day.Cars.Count)
                {
                    string id = Day.Cars[s].Driver.Id;
                    if (!named.TryGetValue(s, out string was) || was != id)
                    {
                        named[s] = id;
                        strip = s;
                        stripUntil = Time.unscaledTime + StripTime;
                    }
                }
            }
            // The gap's trend, once a real second.
            int front = Director.Subject, back = Director.Other;
            if (back < 0) return;
            float gap = Gap(front, back);
            if (front != pairFront || back != pairBack)
            {
                (pairFront, pairBack, lastGap, gapAt, trend) = (front, back, gap, Time.unscaledTime, 0f);
            }
            else if (Time.unscaledTime - gapAt >= 1f)
            {
                trend = gap - lastGap;
                (lastGap, gapAt) = (gap, Time.unscaledTime);
            }
        }

        private float Gap(int front, int back) =>
            (Cars[front].Traffic.Distance - Cars[back].Traffic.Distance) / Mathf.Max(Cars[back].Traffic.Speed, 10f);

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || Director == null || !Director.OnAir) return;
            if (Tower != null && Tower.Hidden || DebugMenu.Bare) return;
            k = Screen.height / 1080f;
            offset = (Screen.width / k - 1920f) / 2f;
            Style();

            int s = Director.Subject;
            if (s < 0 || Cars[s] == null) return;
            // The tower, on the layout: the strips keep clear of it and of each other.
            Rect area = Tower != null ? Tower.Area : Rect.zero;
            tower = area.width > 0f ? new Rect(area.x / k - offset, area.y / k, area.width / k, area.height / k) : Rect.zero;
            Rect battle = Director.Other >= 0 ? Battle(s, Director.Other) : Rect.zero;
            if (strip >= 0 && Time.unscaledTime < stripUntil && Cars[strip] != null) DriverStrip(strip, battle);
            if (Director.Stopping) PitTimer(s, battle);
            if (DriveSettings.TrackMap || Director.Helicopter) Map();
        }

        /// <summary>The space kept between graphics, px on the layout.</summary>
        private const float Spacing = 16f;
        /// <summary>The bottom of the lower graphics, px on the layout.</summary>
        private const float Baseline = 1080f - 72f;

        private Rect tower;

        /// <summary>
        /// Where a lower graphic of this size goes, px on the layout: on the baseline at x, or right of the
        /// tower if it would sit over it, and lifted above the battle strip if it would sit over that.
        /// </summary>
        private Rect Place(float x, float width, float height, Rect battle)
        {
            var r = new Rect(x, Baseline - height, width, height);
            if (tower.width > 0f && r.Overlaps(tower)) r.x = tower.xMax + Spacing;
            if (battle.width > 0f && r.Overlaps(battle)) r.y = battle.yMin - Spacing - height;
            return r;
        }

        private Rect Battle(int front, int back)
        {
            float h = 64f;
            float frontWidth = Block(front, 0f, 0f, h, false);
            string gap = Gap(front, back).ToString("0.000");
            float gapWidth = 44f + gapStyle.CalcSize(new GUIContent(gap)).x / k + 22f;
            float backWidth = Block(back, 0f, 0f, h, false);
            float width = frontWidth + gapWidth + backWidth;
            float x = 960f - width / 2f, y = Baseline - h;
            if (tower.width > 0f && x < tower.xMax + Spacing && y < tower.yMax) x = tower.xMax + Spacing;
            Fill(R(x, y, width, h), Panel, 9f);
            Block(front, x, y, h, true);
            Rect g = R(x + frontWidth, y, gapWidth, h);
            GUI.DrawTexture(g, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Under, 0f, 0f);
            if (trend < -0.005f) Draw(R(x + frontWidth + 22f, y + 25f, 16f, 14f), arrow, Amber, true);
            Label(R(x + frontWidth + 44f, y, gapWidth - 44f, h), gap, gapStyle, Color.white, TextAnchor.MiddleLeft);
            Block(back, x + frontWidth + gapWidth, y, h, true);
            return new Rect(x, y, width, h);
        }

        /// <summary>A car's position, slash, team and class badge in a strip: draws it if asked, and returns its width, px on the layout.</summary>
        private float Block(int i, float x, float y, float h, bool paint)
        {
            int position = Position(i);
            string teamName = Teams[i].ToUpperInvariant();
            float teamWidth = team.CalcSize(new GUIContent(teamName)).x / k;
            string letter = RaceTower.ClassLetter(Classes[i]);
            float badgeWidth = Mathf.Max(30f, badge.CalcSize(new GUIContent(letter)).x / k + 16f);
            float width = h + 12f + 10f + 12f + teamWidth + 12f + badgeWidth + 20f;
            if (!paint) return width;
            if (ClassLeader(i)) Draw(R(x, y, h, h), lead, Red, false);
            Label(R(x, y, h, h), position.ToString(), pos, Color.white, TextAnchor.MiddleCenter);
            Draw(R(x + h + 12f, y + (h - 36f) / 2f, 14f, 36f), slash, Colours[i], false);
            Label(R(x + h + 34f, y, teamWidth, h), teamName, team, Color.white, TextAnchor.MiddleLeft);
            Badge(R(x + h + 46f + teamWidth, y + (h - 26f) / 2f, badgeWidth, 26f), letter, RaceTower.ClassColour(Classes[i]));
            return width;
        }

        private void DriverStrip(int i, Rect battle)
        {
            CarRaceLog log = Day.Cars[i];
            string full = Game != null ? RaceWeekend.DriverName(log.Driver.Id, Game) : log.Driver.Id;
            int space = full.LastIndexOf(' ');
            string initial = space > 0 ? full.Substring(0, 1).ToUpperInvariant() + "." : "";
            string surname = (space > 0 ? full.Substring(space + 1) : full).ToUpperInvariant();
            string teamName = Teams[i].ToUpperInvariant();
            string letter = RaceTower.ClassLetter(Classes[i]);

            float initialWidth = initial.Length > 0 ? first.CalcSize(new GUIContent(initial)).x / k + 6f : 0f;
            float nameWidth = nameStyle.CalcSize(new GUIContent(surname)).x / k;
            float row2 = small.CalcSize(new GUIContent(teamName)).x / k + 14f + small.CalcSize(new GUIContent(Models[i])).x / k + 14f + 40f;
            float width = Mathf.Max(520f, Mathf.Max(68f + 14f + 10f + 14f + initialWidth + nameWidth + 24f, 82f + row2 + 24f));
            Rect at = Place(72f, width, 108f, battle);
            float x = at.x, y = at.y;
            Fill(R(x, y, width, 108f), Panel, 9f);
            Label(R(x, y, 68f, 68f), Position(i).ToString(), pos, Color.white, TextAnchor.MiddleCenter);
            Draw(R(x + 82f, y + 14f, 16f, 40f), slash, Colours[i], false);
            float nx = x + 82f + 30f;
            if (initial.Length > 0) Label(R(nx, y, initialWidth, 68f), initial, first, Color.white, TextAnchor.MiddleLeft);
            Label(R(nx + initialWidth, y, nameWidth + 4f, 68f), surname, nameStyle, Color.white, TextAnchor.MiddleLeft);
            Rect under = R(x, y + 68f, width, 40f);
            GUI.DrawTexture(under, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Under, Vector4.zero, new Vector4(0f, 0f, 9f * k, 9f * k));
            float tw = small.CalcSize(new GUIContent(teamName)).x / k;
            Label(R(x + 82f, y + 68f, tw, 40f), teamName, small, Color.white, TextAnchor.MiddleLeft);
            Label(R(x + 82f + tw + 14f, y + 68f, width - tw - 160f, 40f), Models[i], small, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
            Badge(R(x + width - 24f - 30f, y + 68f + 8f, 30f, 24f), letter, RaceTower.ClassColour(Classes[i]));
        }

        private void PitTimer(int i, Rect battle)
        {
            PitService service = Drivers[i] != null && Drivers[i].Racer?.Pit != null ? Drivers[i].Racer.Pit.Service : null;
            if (service == null) service = Cars[i].Service;
            if (service == null) return;
            string teamName = Teams[i].ToUpperInvariant();
            string letter = RaceTower.ClassLetter(Classes[i]);
            float tw = team.CalcSize(new GUIContent(teamName)).x / k;
            float width = Mathf.Max(72f + 14f + 10f + 14f + tw + 14f + 30f + 20f, 380f);
            Rect at = Place(1920f - 72f - width, width, 128f, battle);
            float x = at.x, y = at.y;
            Fill(R(x, y, width, 128f), Panel, 9f);
            Draw(R(x, y, 72f, 56f), lead, Red, false);
            Label(R(x, y, 64f, 56f), "PIT", small, Color.white, TextAnchor.MiddleCenter);
            Draw(R(x + 86f, y + 12f, 13f, 32f), slash, Colours[i], false);
            Label(R(x + 110f, y, tw, 56f), teamName, team, Color.white, TextAnchor.MiddleLeft);
            Badge(R(x + 124f + tw, y + 16f, 30f, 24f), letter, RaceTower.ClassColour(Classes[i]));
            GUI.DrawTexture(R(x, y + 56f, width, 72f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Under, Vector4.zero, new Vector4(0f, 0f, 9f * k, 9f * k));
            Label(R(x + 24f, y + 56f, 160f, 72f), service.Elapsed.ToString("0.0"), timer, Color.white, TextAnchor.MiddleLeft);
            float ix = x + width - 24f - 30f;
            if (service.NewDriver) { Draw(R(ix, y + 77f, 30f, 30f), helmet, service.Done ? Color.white : Dim, false); ix -= 48f; }
            if (service.NewTyres) { Draw(R(ix, y + 77f, 30f, 30f), tyre, service.Done ? Color.white : Dim, false); ix -= 48f; }
            Draw(R(ix, y + 77f, 30f, 30f), fuel, service.Fuelled ? Color.white : Dim, false);
        }

        private void Map()
        {
            Rect panel = R(1920f - 72f - 300f, 340f, 300f, 400f);
            Fill(panel, new Color32(0x1a, 0x1a, 0x1c, 0xd0), 9f);
            Rect inner = map.Fit(panel);
            GUI.DrawTexture(inner, map.Texture, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.28f), 0f, 0f);
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null || !Cars[i].isActiveAndEnabled || i == Player) continue;
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[i]), 8f, RaceTower.ClassColour(Classes[i]));
            }
            if (Player >= 0 && Cars[Player] != null)
            {
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[Player]), 16f, Panel);
                Dot(inner, TrackMap.OnTrack(Circuit, Cars[Player]), 12f, Color.white);
            }
        }

        private void Dot(Rect inner, Vector3 p, float size, Color colour)
        {
            Vector2 at = map.At(inner, p);
            float s = size * k;
            GUI.DrawTexture(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), disc, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
        }

        private int Position(int i)
        {
            float d = Cars[i].Traffic.Distance;
            int p = 1;
            for (int j = 0; j < Cars.Length; j++)
                if (j != i && Cars[j] != null && Cars[j].Traffic.Distance > d) p++;
            return p;
        }

        private bool ClassLeader(int i)
        {
            float d = Cars[i].Traffic.Distance;
            for (int j = 0; j < Cars.Length; j++)
                if (j != i && Cars[j] != null && Classes[j] == Classes[i] && Cars[j].Traffic.Distance > d) return false;
            return true;
        }

        private void Badge(Rect r, string letter, Color colour)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * 6f * k);
            Label(r, letter, badge, colour, TextAnchor.MiddleCenter);
        }

        private void Style()
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(30f * k));
            if (pos != null && pos.fontSize == key) return;
            pos = Make(bold, 30f);
            team = Make(bold, 26f);
            badge = Make(bold, 16f);
            nameStyle = Make(bold, 34f);
            first = Make(regular, 26f);
            small = Make(semiBold, 19f);
            timer = Make(bold, 46f);
            gapStyle = Make(bold, 30f);
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

        private static void Label(Rect r, string s, GUIStyle style, Color colour, TextAnchor anchor)
        {
            style.alignment = anchor;
            style.normal.textColor = colour;
            GUI.Label(r, s, style);
        }

        private void Fill(Rect r, Color colour, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, Vector4.one * radius * k);

        /// <summary>A shape, tinted. Flipped draws it upside down, as the closing arrow points down.</summary>
        private static void Draw(Rect r, Texture2D shape, Color colour, bool flipped)
        {
            if (flipped) r = new Rect(r.x, r.yMax, r.width, -r.height);
            GUI.DrawTexture(r, shape, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
        }

        private void OnDestroy()
        {
            foreach (Texture2D t in new[] { slash, lead, arrow, disc, fuel, tyre, helmet })
                if (t != null) Destroy(t);
            if (map != null) Destroy(map.Texture);
        }
    }
}
