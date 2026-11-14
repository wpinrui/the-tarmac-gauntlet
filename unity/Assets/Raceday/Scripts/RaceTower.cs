using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The TV timing tower in the top left corner. A tab on top counts down the race time left, and a
    /// dimmer tab beside it shows the time of day. Under
    /// it, one row per car: position, a slash in the team's colour, team name, class badge and fake laps
    /// completed (<see cref="Standings"/>). Class leaders have their position on a red block, and the
    /// player's row is white. It shows every class leader and the four cars either side of the player,
    /// then fills the screen down to the driver card with the fastest class. Once the leader takes the flag, after the
    /// time runs out, the countdown gives way to a chequered flag, and each car that has taken the flag has one beside its laps. As the
    /// watched car takes it, a chequered flag and its finishing position show at the top of the screen for
    /// <see cref="BannerTime"/>. Sizes are in design units, a hundredth of the width of a 16:9 screen.
    /// </summary>
    public sealed class RaceTower : MonoBehaviour
    {
        /// <summary>Length of the race on the race clock, seconds.</summary>
        public const float RaceLength = 24f * 3600f;
        /// <summary>Race clock seconds for every second of racing.</summary>
        public const float TimeCompression = 10f;

        /// <summary>Placeholder team colours until teams have their own.</summary>
        public static readonly Color[] TeamColours =
        {
            Hex(0xe10600), Hex(0x00d2be), Hex(0x0600ef), Hex(0xff8700), Hex(0x006f62), Hex(0xf5f5f5),
            Hex(0xffd800), Hex(0x2b4562), Hex(0xb6babd), Hex(0x9b0000), Hex(0x1e5bc6), Hex(0x6cd3bf),
            Hex(0xff87bc), Hex(0x52e252), Hex(0x7a3cc8), Hex(0xc8a23c),
        };

        public RaceCar[] Cars;
        public string[] Teams;
        public CarClass[] Classes;
        public Color[] Colours;
        /// <summary>Index in <see cref="Cars"/> of the car the tower is seen from: the car the cameras ride with.</summary>
        public int Player = -1;
        /// <summary>Length of the lap, m.</summary>
        public float Lap;
        /// <summary>True once the race clock runs.</summary>
        public Func<bool> Racing;
        /// <summary>True to stop drawing the tower, while live timing is up. The clock keeps running.</summary>
        [NonSerialized] public bool Hidden;
        /// <summary>True once car i has taken the chequered flag, or null when no race is kept.</summary>
        public Func<int, bool> Flagged;
        /// <summary>How long the finishing banner stays up, real seconds.</summary>
        public const float BannerTime = 6f;
        /// <summary>True while the chequered flag banner shows at the top of the screen.</summary>
        public bool BannerShowing => bannerAt >= 0f && Time.unscaledTime - bannerAt <= BannerTime;

        // Layout, in design units.
        private const float Margin = 1.6f, Width = 24f;
        private const float TabHeight = 2.1f, TabPad = 1f, Radius = 0.45f, BodyPad = 0.25f;
        private const float HeaderHeight = 1.2f, RowHeight = 1.75f, BreakHeight = 0.55f;
        private const float PosWidth = 2.1f, PosPad = 0.45f, SlashWidth = 0.9f, ClassWidth = 2.6f, GapWidth = 3.8f, RightPad = 0.8f;
        private const float BarWidth = 0.42f, BarHeight = 1.25f, BarSkew = 24f;
        private const float LeadReach = 0.2f, LeadSlant = 0.78f;
        private const float TeamPad = 0.35f;
        private const float BadgeMin = 1.5f, BadgePad = 0.3f, BadgeBorder = 0.12f, BadgeRadius = 0.4f, BadgeText = 1.1f;

        private static readonly Color TabColour = new Color(18f / 255f, 18f / 255f, 20f / 255f, 0.82f);
        private static readonly Color BodyColour = new Color(26f / 255f, 26f / 255f, 28f / 255f, 0.72f);
        private static readonly Color RuleColour = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color BreakColour = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color LeadColour = Hex(0xe10600);
        private static readonly Color PlayerRow = Hex(0xf2f2f2);
        private static readonly Color PlayerText = Hex(0x111111);
        private static readonly Color HeaderText = new Color(1f, 1f, 1f, 0.6f);

        private readonly List<Standings.Car> order = new List<Standings.Car>();
        /// <summary>The cars in race order, leader first, as of this frame. Each one's Id indexes <see cref="Cars"/>.</summary>
        public IReadOnlyList<Standings.Car> Order => order;
        private readonly List<Standings.Row> rows = new List<Standings.Row>();
        private readonly Dictionary<int, string> laps = new Dictionary<int, string>();
        private readonly Dictionary<string, string> fitted = new Dictionary<string, string>();
        private Standings.Car[] entries;
        private float clock;
        /// <summary>Race seconds since the start, on the race clock.</summary>
        public float RaceTime => clock * TimeCompression;

        /// <summary>Moves the race clock to this many race seconds from the start. For the debug menu.</summary>
        public void SetRaceTime(float raceSeconds) => clock = Mathf.Max(0f, raceSeconds) / TimeCompression;
        private Font bold, semiBold;
        private GUIStyle clockStyle, hourStyle, posStyle, teamStyle, badgeStyle, gapStyle, headerStyle;
        private int styledFor = -1;
        private Texture2D slash, lead, flag;
        private string[] positions;
        private int finishers;
        /// <summary>When the watched car took the flag, in unscaled seconds, and the place it took it in, or -1 before.</summary>
        private float bannerAt = -1f;
        private string bannerText;
        private GUIStyle bannerStyle;

        private void Start()
        {
            entries = new Standings.Car[Cars.Length];
            for (int i = 0; i < Cars.Length; i++)
                entries[i] = new Standings.Car { Id = i, Team = Teams[i].ToUpperInvariant(), Class = Classes[i], Player = i == Player };
            positions = new string[Cars.Length + 1];
            for (int i = 0; i < positions.Length; i++) positions[i] = i.ToString();
            bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            float barSpan = BarWidth + BarHeight * Mathf.Tan(BarSkew * Mathf.Deg2Rad);
            float shift = BarHeight * Mathf.Tan(BarSkew * Mathf.Deg2Rad);
            slash = Shape(barSpan, BarHeight, (x, y) => x >= y / BarHeight * shift && x <= y / BarHeight * shift + BarWidth);
            float leadWidth = PosWidth + LeadReach;
            lead = Shape(leadWidth, RowHeight, (x, y) => x <= leadWidth - LeadSlant * (1f - y / RowHeight));
            flag = ChequeredFlag();
        }

        private void Update()
        {
            if (entries == null) return;
            bool racing = Racing == null || Racing();
            if (racing) clock += Time.deltaTime;
            order.Clear();
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null) continue;
                Standings.Car e = entries[i];
                if (!e.Finished && Flagged != null && Flagged(i)) Standings.Finish(e, Cars[i].Laps, Lap, ++finishers);
                if (!e.Finished) e.Distance = Cars[i].Traffic.Distance;
                e.Player = i == Player;
                order.Add(e);
            }
            Standings.Rank(order, Lap, racing);
            Standings.Rows(order, Fit(), rows);
            if (bannerAt < 0f && Player >= 0 && Player < entries.Length && entries[Player].Finished)
            {
                bannerAt = Time.unscaledTime;
                bannerText = "P" + entries[Player].Position;
            }
        }

        /// <summary>Car i as the tower ranks it, or null before the tower has started.</summary>
        public Standings.Car Entry(int i) => entries != null && i >= 0 && i < entries.Length ? entries[i] : null;

        /// <summary>True once the leader has taken the chequered flag, after the race time ran out.</summary>
        public bool FlagOut => finishers > 0;

        /// <summary>How many rows fit between the top of the screen and the driver card below the tower.</summary>
        private static int Fit()
        {
            float u = Unit();
            float card = DriverCard.Room / (19.2f * Scale);
            float room = Screen.height / u - Margin - card - TabHeight - 2f * BodyPad - HeaderHeight - BreakHeight;
            return Mathf.Max(1, Mathf.FloorToInt(room / RowHeight));
        }

        /// <summary>The screen pixels the tower covers, or an empty rect while it isn't drawn.</summary>
        public Rect Area
        {
            get
            {
                if (Hidden || rows.Count == 0) return Rect.zero;
                float u = Unit();
                int breaks = 0;
                foreach (Standings.Row r in rows) if (r.BreakBefore) breaks++;
                float height = TabHeight + 2f * BodyPad + HeaderHeight + rows.Count * RowHeight + breaks * BreakHeight;
                return new Rect(Margin * u, Margin * u, Width * u, height * u);
            }
        }

        /// <summary>The whole tower is drawn at this share of the design's size.</summary>
        private const float Scale = 0.7f;

        /// <summary>One design unit in pixels.</summary>
        private static float Unit() => Screen.height / 1080f * 19.2f * Scale;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Hidden || DebugMenu.Bare || Event.current.type != EventType.Repaint || rows.Count == 0) return;
            float u = Unit();
            Style(u);

            float x = Margin * u, y = Margin * u, w = Width * u;
            string time = Clock();
            float tabWidth = clockStyle.CalcSize(new GUIContent(time)).x + 2f * TabPad * u;
            var tab = new Rect(x, y, tabWidth, TabHeight * u);
            Fill(tab, TabColour, new Vector4(Radius * u, Radius * u, 0f, 0f));
            if (FlagOut)
            {
                float fh = TabHeight * 0.62f * u, fw = fh * FlagAspect;
                GUI.DrawTexture(new Rect(tab.center.x - fw / 2f, tab.center.y - fh / 2f, fw, fh), flag, ScaleMode.StretchToFill, true);
            }
            else GUI.Label(tab, time, clockStyle);
            string hour = TimeOfDayText;
            var hourTab = new Rect(tab.xMax + BodyPad * u, y, hourStyle.CalcSize(new GUIContent(hour)).x + 2f * TabPad * u, TabHeight * u);
            Fill(hourTab, TabColour, new Vector4(Radius * u, Radius * u, 0f, 0f));
            Text(hourTab, hour, hourStyle, HeaderText);
            y = tab.yMax;

            int breaks = 0;
            foreach (Standings.Row r in rows) if (r.BreakBefore) breaks++;
            float height = (2f * BodyPad + HeaderHeight + rows.Count * RowHeight + breaks * BreakHeight) * u;
            Fill(new Rect(x, y, w, height), BodyColour, new Vector4(0f, Radius * u, Radius * u, Radius * u));
            y += BodyPad * u;

            float gapX = x + w - (RightPad + GapWidth) * u;
            float classX = gapX - ClassWidth * u;
            Text(new Rect(classX, y, ClassWidth * u, HeaderHeight * u), "CLASS", headerStyle, HeaderText);
            Text(new Rect(gapX, y, GapWidth * u, HeaderHeight * u), "LAP", headerStyle, HeaderText);
            y += HeaderHeight * u;

            bool afterBreak = false;
            foreach (Standings.Row r in rows)
            {
                if (r.BreakBefore)
                {
                    Fill(new Rect(x, y, w, BreakHeight * u), BreakColour, Vector4.zero);
                    y += BreakHeight * u;
                    afterBreak = true;
                }
                DrawRow(r.Car, x, y, w, u, classX, gapX, !afterBreak);
                afterBreak = false;
                y += RowHeight * u;
            }
            Banner(u);
        }

        /// <summary>The chequered flag and the watched car's finishing position, at the top of the screen, fading out at the end.</summary>
        private void Banner(float u)
        {
            if (bannerAt < 0f) return;
            float age = Time.unscaledTime - bannerAt;
            if (age > BannerTime) return;
            float alpha = Mathf.Clamp01(BannerTime - age);
            float h = 4.2f * u, pad = 1.2f * u;
            float fh = h - 2f * pad * 0.6f, fw = fh * FlagAspect;
            float textWidth = bannerStyle.CalcSize(new GUIContent(bannerText)).x;
            float width = pad + fw + pad + textWidth + pad;
            var r = new Rect((Screen.width - width) / 2f, Margin * u, width, h);
            Color back = TabColour;
            back.a *= alpha;
            Fill(r, back, Vector4.one * Radius * u);
            GUI.DrawTexture(new Rect(r.x + pad, r.center.y - fh / 2f, fw, fh), flag, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, alpha), 0f, 0f);
            Text(new Rect(r.x + pad + fw + pad, r.y, textWidth, h), bannerText, bannerStyle, new Color(1f, 1f, 1f, alpha));
        }

        private void DrawRow(Standings.Car c, float x, float y, float w, float u, float classX, float gapX, bool rule)
        {
            float h = RowHeight * u;
            bool player = c.Player;
            if (player) Fill(new Rect(x, y, w, h), PlayerRow, Vector4.zero);
            else if (rule) Fill(new Rect(x, y, w, 1f), RuleColour, Vector4.zero);

            bool leader = c.ClassPosition == 1;
            if (leader) Draw(new Rect(x + 1f, y + 1f, (PosWidth + LeadReach) * u - 1f, h - 2f), lead, LeadColour);
            Text(new Rect(x, y, (PosWidth - PosPad) * u, h), positions[c.Position], posStyle, player && !leader ? PlayerText : Color.white);

            float barSpan = (BarWidth + BarHeight * Mathf.Tan(BarSkew * Mathf.Deg2Rad)) * u;
            float barX = x + (PosWidth + SlashWidth / 2f) * u - barSpan / 2f;
            Draw(new Rect(barX, y + (h - BarHeight * u) / 2f, barSpan, BarHeight * u), slash, Colours[c.Id]);

            float teamX = x + (PosWidth + SlashWidth + TeamPad) * u;
            float teamWidth = classX - teamX;
            Text(new Rect(teamX, y, teamWidth, h), Ellipsis(c.Team, teamWidth), teamStyle, player ? PlayerText : Color.white);

            Color colour = ClassColour(c.Class);
            if (player) colour = new Color(colour.r * 0.75f, colour.g * 0.75f, colour.b * 0.75f);
            string letter = ClassLetter(c.Class);
            float textWidth = badgeStyle.CalcSize(new GUIContent(letter)).x;
            float badgeWidth = Mathf.Max(BadgeMin * u, textWidth + 2f * BadgePad * u) + 2f * BadgeBorder * u;
            float badgeHeight = (BadgeText + 2f * BadgeBorder) * u;
            var badge = new Rect(gapX - badgeWidth, y + (h - badgeHeight) / 2f, badgeWidth, badgeHeight);
            GUI.DrawTexture(badge, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour,
                Vector4.one * Mathf.Max(1f, BadgeBorder * u), Vector4.one * BadgeRadius * u);
            Text(badge, letter, badgeStyle, colour);

            Text(new Rect(gapX, y, GapWidth * u, h), Laps(c), gapStyle, player ? PlayerText : Color.white);
            if (c.Finished)
            {
                float fh = RowFlag * u, fw = fh * FlagAspect;
                GUI.DrawTexture(new Rect(gapX + FlagPad * u, y + (h - fh) / 2f, fw, fh), flag, ScaleMode.StretchToFill, true);
            }
        }

        /// <summary>The height of the flag beside a finished car's laps, and its gap from the class badge, in design units.</summary>
        private const float RowFlag = 0.75f, FlagPad = 0.2f;

        /// <summary>The fake laps the car has completed.</summary>
        private string Laps(Standings.Car c)
        {
            if (!laps.TryGetValue(c.FakeLaps, out string s)) laps[c.FakeLaps] = s = c.FakeLaps.ToString();
            return s;
        }

        /// <summary>The race time left, as the tab on top shows it.</summary>
        public string TimeLeft => Clock();

        /// <summary>The local time of day on the race clock, in 12-hour notation, as the tab beside the countdown shows it.</summary>
        public string TimeOfDayText
        {
            get
            {
                int m = Mathf.FloorToInt(TimeOfDay.StartHour * 60f + RaceTime / 60f) % (24 * 60);
                int h = m / 60, h12 = h % 12 == 0 ? 12 : h % 12;
                return $"{h12}:{m % 60:00} {(h < 12 ? "AM" : "PM")}";
            }
        }

        /// <summary>
        /// A stop's <paramref name="seconds"/> as race time, as the race clock counts it: minutes and seconds, with the
        /// hours in front once it runs to an hour, as a long repair does.
        /// </summary>
        public static string StopTime(float seconds)
        {
            int s = Mathf.FloorToInt(Mathf.Max(0f, seconds) * TimeCompression);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        private string Clock()
        {
            float left = Mathf.Max(0f, RaceLength - clock * TimeCompression);
            int s = Mathf.CeilToInt(left);
            return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
        }

        /// <summary>The team name, cut short with an ellipsis if it is wider than the room.</summary>
        private string Ellipsis(string name, float room)
        {
            if (fitted.TryGetValue(name, out string s)) return s;
            s = name;
            if (teamStyle.CalcSize(new GUIContent(s)).x > room)
            {
                while (s.Length > 1 && teamStyle.CalcSize(new GUIContent(s + "...")).x > room) s = s.Substring(0, s.Length - 1);
                s = s.TrimEnd() + "...";
            }
            fitted[name] = s;
            return s;
        }

        private void Style(float u)
        {
            int key = Mathf.RoundToInt(u * 100f);
            if (key == styledFor) return;
            styledFor = key;
            fitted.Clear();
            clockStyle = Make(bold, 1.3f * u, TextAnchor.MiddleCenter);
            hourStyle = Make(semiBold, 1.3f * u, TextAnchor.MiddleCenter);
            posStyle = Make(semiBold, 1.05f * u, TextAnchor.MiddleRight);
            teamStyle = Make(bold, 0.98f * u, TextAnchor.MiddleLeft);
            badgeStyle = Make(bold, 0.9f * u, TextAnchor.MiddleCenter);
            gapStyle = Make(semiBold, 1.05f * u, TextAnchor.MiddleRight);
            headerStyle = Make(semiBold, 0.74f * u, TextAnchor.MiddleRight);
            bannerStyle = Make(bold, 2.4f * u, TextAnchor.MiddleLeft);
        }

        private static GUIStyle Make(Font font, float size, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.RoundToInt(size)),
            alignment = anchor,
            clipping = TextClipping.Overflow,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
        };

        private static void Text(Rect r, string s, GUIStyle style, Color colour)
        {
            style.normal.textColor = colour;
            GUI.Label(r, s, style);
        }

        private static void Fill(Rect r, Color colour, Vector4 radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, radius);

        private static void Draw(Rect r, Texture2D shape, Color colour) =>
            GUI.DrawTexture(r, shape, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);

        /// <summary>A tyre seen side on: tread and hub.</summary>
        internal static Texture2D TyreIcon() => Shape(1f, 1f, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f));
            return (d > 0.34f && d < 0.48f) || (d > 0.1f && d < 0.18f);
        });

        /// <summary>A helmet side on, its visor cut out.</summary>
        internal static Texture2D HelmetIcon() => Shape(1f, 1f, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.42f) * (y - 0.42f));
            bool shell = d < 0.44f && y > 0.12f;
            bool visor = x > 0.46f && y > 0.36f && y < 0.56f;
            return shell && !visor;
        });

        /// <summary>A spanner on the slant, its open jaw top right.</summary>
        internal static Texture2D SpannerIcon() => Shape(1f, 1f, (x, y) =>
        {
            float along = (x + y) * 0.7071f, across = (y - x) * 0.7071f;
            bool handle = Mathf.Abs(across) < 0.09f && along > 0.14f && along < 1.0f;
            float hx = x - 0.7f, hy = y - 0.7f;
            bool head = hx * hx + hy * hy < 0.23f * 0.23f;
            float jx = x - 0.8f, jy = y - 0.8f;
            bool jaw = jx * jx + jy * jy < 0.12f * 0.12f || (Mathf.Abs((jy - jx) * 0.7071f) < 0.08f && (jx + jy) > 0f);
            return (handle || head) && !jaw;
        });

        /// <summary>Width over height of the chequered flag.</summary>
        internal const float FlagAspect = 1.4f;

        /// <summary>
        /// A chequered flag rippling in the wind: black and white squares, five across and four down, with a
        /// thin dark edge so its white squares read on a white row. Drawn untinted, or tinted for its alpha.
        /// </summary>
        internal static Texture2D ChequeredFlag()
        {
            const int Rows = 96, Samples = 3, Across = 5, Down = 4;
            int columns = Mathf.RoundToInt(Rows * FlagAspect);
            var tex = new Texture2D(columns, Rows, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            const float Wave = 0.06f, Edge = 0.035f;
            var pixels = new Color32[columns * Rows];
            for (int j = 0; j < Rows; j++)
            for (int i = 0; i < columns; i++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sj = 0; sj < Samples; sj++)
                for (int si = 0; si < Samples; si++)
                {
                    float u = (i + (si + 0.5f) / Samples) / columns;
                    float v = (j + (sj + 0.5f) / Samples) / Rows;
                    // The cloth ripples up and down along its length, more towards the fly end.
                    float y = (v - 0.5f) * (1f + 2f * Wave) + 0.5f - Wave * u * Mathf.Sin(u * Mathf.PI * 2.2f);
                    if (y < 0f || y > 1f) continue;
                    bool edge = u < Edge || u > 1f - Edge || y < Edge * FlagAspect || y > 1f - Edge * FlagAspect;
                    bool dark = edge || ((Mathf.FloorToInt(u * Across) + Mathf.FloorToInt(y * Down)) & 1) == 0;
                    // A little shading in the folds.
                    float shade = 0.88f + 0.12f * Mathf.Cos(u * Mathf.PI * 2.2f);
                    float c = dark ? 0.08f : shade;
                    r += c; g += c; b += c; a += 1f;
                }
                int n = Samples * Samples;
                pixels[j * columns + i] = a > 0f
                    ? new Color32((byte)(255 * r / a), (byte)(255 * g / a), (byte)(255 * b / a), (byte)(255 * a / n))
                    : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>A fuel pump with its hose.</summary>
        internal static Texture2D FuelIcon() => Shape(1f, 1f, (x, y) =>
        {
            bool body = x > 0.12f && x < 0.58f && y > 0.05f && y < 0.92f && !(x > 0.22f && x < 0.48f && y > 0.55f && y < 0.82f);
            bool hose = (x > 0.58f && x < 0.84f && y > 0.62f && y < 0.7f) || (x > 0.76f && x < 0.86f && y > 0.2f && y < 0.7f);
            return body || hose;
        });

        /// <summary>
        /// A white shape on a clear texture, smoothed at the edges: inside(x, y) in design units, y up
        /// from the bottom. Drawn tinted, at the width and height it was made for.
        /// </summary>
        internal static Texture2D Shape(float width, float height, Func<float, float, bool> inside)
        {
            const int Rows = 128, Samples = 4;
            int columns = Mathf.CeilToInt(Rows * width / height);
            var tex = new Texture2D(columns, Rows, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[columns * Rows];
            for (int j = 0; j < Rows; j++)
            for (int i = 0; i < columns; i++)
            {
                int hits = 0;
                for (int sj = 0; sj < Samples; sj++)
                for (int si = 0; si < Samples; si++)
                {
                    float px = (i + (si + 0.5f) / Samples) / columns * width;
                    float py = (j + (sj + 0.5f) / Samples) / Rows * height;
                    if (inside(px, py)) hits++;
                }
                pixels[j * columns + i] = new Color32(255, 255, 255, (byte)(255 * hits / (Samples * Samples)));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (slash != null) Destroy(slash);
            if (lead != null) Destroy(lead);
            if (flag != null) Destroy(flag);
        }

        public static Color ClassColour(CarClass c) => c switch
        {
            CarClass.F => Hex(0xa0785a),
            CarClass.E => Hex(0x5ab8d8),
            CarClass.D => Hex(0xd060a0),
            CarClass.C => Hex(0xe0943a),
            CarClass.B => Hex(0xd04040),
            CarClass.A => Hex(0x50c878),
            _ => Hex(0xdddddd),
        };

        internal static string ClassLetter(CarClass c) => c == CarClass.F1 ? "F1" : c.ToString();

        internal static Color Hex(int rgb) => new Color((rgb >> 16 & 0xff) / 255f, (rgb >> 8 & 0xff) / 255f, (rgb & 0xff) / 255f);
    }
}
