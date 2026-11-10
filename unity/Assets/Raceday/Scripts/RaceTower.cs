using System;
using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The TV timing tower in the top left corner. A tab on top counts down the race time left. Under
    /// it, one row per car: position, a slash in the team's colour, team name, class badge and gap in
    /// fake laps (<see cref="Standings"/>). Class leaders have their position on a red block, and the
    /// player's row is white. It shows every class leader and the four cars either side of the player,
    /// then fills the screen height with the fastest class. Sizes are in design units, a hundredth of
    /// the width of a 16:9 screen.
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
        /// <summary>Index of the player's car in <see cref="Cars"/>.</summary>
        public int Player = -1;
        /// <summary>Length of the lap, m.</summary>
        public float Lap;
        /// <summary>True once the race clock runs.</summary>
        public Func<bool> Racing;

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
        private readonly List<Standings.Row> rows = new List<Standings.Row>();
        private readonly Dictionary<int, string> gaps = new Dictionary<int, string>();
        private readonly Dictionary<string, string> fitted = new Dictionary<string, string>();
        private Standings.Car[] entries;
        private float clock;
        private Font bold, semiBold;
        private GUIStyle clockStyle, posStyle, teamStyle, badgeStyle, gapStyle, headerStyle;
        private int styledFor = -1;
        private Texture2D slash, lead;
        private string[] positions;

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
        }

        private void Update()
        {
            if (entries == null) return;
            if (Racing == null || Racing()) clock += Time.deltaTime;
            order.Clear();
            for (int i = 0; i < Cars.Length; i++)
            {
                if (Cars[i] == null) continue;
                entries[i].Distance = Cars[i].Traffic.Distance;
                order.Add(entries[i]);
            }
            Standings.Rank(order, Lap);
            Standings.Rows(order, Fit(), rows);
        }

        /// <summary>How many rows fit between the top of the screen and the bottom margin.</summary>
        private static int Fit()
        {
            float u = Unit();
            float room = Screen.height / u - 2f * Margin - TabHeight - 2f * BodyPad - HeaderHeight - BreakHeight;
            return Mathf.Max(1, Mathf.FloorToInt(room / RowHeight));
        }

        /// <summary>One design unit in pixels.</summary>
        private static float Unit() => Screen.height / 1080f * 19.2f;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || rows.Count == 0) return;
            float u = Unit();
            Style(u);

            float x = Margin * u, y = Margin * u, w = Width * u;
            string time = Clock();
            float tabWidth = clockStyle.CalcSize(new GUIContent(time)).x + 2f * TabPad * u;
            var tab = new Rect(x, y, tabWidth, TabHeight * u);
            Fill(tab, TabColour, new Vector4(Radius * u, Radius * u, 0f, 0f));
            GUI.Label(tab, time, clockStyle);
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

            Text(new Rect(gapX, y, GapWidth * u, h), Gap(c), gapStyle, player ? PlayerText : Color.white);
        }

        /// <summary>The leader's fake laps, or how many the car is behind it.</summary>
        private string Gap(Standings.Car c)
        {
            if (c.Position == 1) return positions.Length > c.FakeLaps ? positions[c.FakeLaps] : c.FakeLaps.ToString();
            int gap = Standings.Gap(order, c);
            if (!gaps.TryGetValue(gap, out string s)) gaps[gap] = s = "+" + gap;
            return s;
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
            posStyle = Make(semiBold, 1.05f * u, TextAnchor.MiddleRight);
            teamStyle = Make(bold, 0.98f * u, TextAnchor.MiddleLeft);
            badgeStyle = Make(bold, 0.9f * u, TextAnchor.MiddleCenter);
            gapStyle = Make(semiBold, 1.05f * u, TextAnchor.MiddleRight);
            headerStyle = Make(semiBold, 0.74f * u, TextAnchor.MiddleRight);
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

        /// <summary>
        /// A white shape on a clear texture, smoothed at the edges: inside(x, y) in design units, y up
        /// from the bottom. Drawn tinted, at the width and height it was made for.
        /// </summary>
        private static Texture2D Shape(float width, float height, Func<float, float, bool> inside)
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

        private static string ClassLetter(CarClass c) => c == CarClass.F1 ? "F1" : c.ToString();

        private static Color Hex(int rgb) => new Color((rgb >> 16 & 0xff) / 255f, (rgb >> 8 & 0xff) / 255f, (rgb & 0xff) / 255f);
    }
}
