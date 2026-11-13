using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The driver card, bottom left under the timing tower, which stops short to leave it room: the driver now in the car the cameras ride with,
    /// their nationality's flag, initial and surname, and their overall on a white block, over a row of their
    /// five stats, each with its number and a bar. While that car rolls through the pit lane to a stop that
    /// changes the driver, a strip above it, tabbed NEXT, shows who gets in. It keeps out of the way of the broadcast, which
    /// names its own drivers, and of live timing, the race preview and the podium, which hide the tower.
    /// Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class DriverCard : MonoBehaviour
    {
        public RaceCameras Views;
        public CarController[] Cars;
        public RaceCar[] Racing;
        public RaceDay Day;
        public GameState Game;
        public RaceTower Tower;

        private static readonly Color Panel = new Color32(0x1a, 0x1a, 0x1c, 0xe0);
        private static readonly Color Under = new Color(0f, 0f, 0f, 0.38f);
        private static readonly Color Red = RaceTower.Hex(0xd8262b);
        private static readonly Color Block = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color Ink = new Color(0.07f, 0.07f, 0.08f);
        private static readonly Color InkDim = new Color(0.07f, 0.07f, 0.08f, 0.6f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.16f);
        private static readonly string[] StatNames = { "PAC", "CON", "STA", "SAF", "SMO" };

        /// <summary>Sizes on the layout, px.</summary>
        private const float CardWidth = 330f, Strip = 56f, Stats = 60f, Pad = 14f, Block58 = 58f, Radius = 6f;
        private const float NextHeight = 40f, NextTab = 66f, Gap = 8f, TowerGap = 16f, Bottom = 40f;
        /// <summary>Height kept clear under the timing tower for the card and the next driver strip, px on the layout.</summary>
        public const float Room = Bottom + Strip + Stats + Gap + NextHeight + TowerGap;

        private readonly NextDriverWatch watch = new NextDriverWatch();
        private readonly Dictionary<string, Shown> shown = new Dictionary<string, Shown>();
        private readonly Dictionary<string, Texture2D> flags = new Dictionary<string, Texture2D>();
        private GUIStyle initial, surname, ovr, label, number, nextInitial, nextSurname, nextOvr, tab;
        private float k;

        /// <summary>A driver's facts with their text made once, not every frame.</summary>
        private sealed class Shown
        {
            public DriverFacts Facts;
            public string Ovr;
            public string[] Stats;
        }

        private void Update()
        {
            if (Day == null || Racing == null) return;
            for (int i = 0; i < Racing.Length && i < Day.Cars.Count; i++)
                watch.Step(i, Racing[i] != null && Racing[i].InPitLane, Day.Cars[i]);
        }

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || DebugMenu.Bare || Day == null || Views == null || Cars == null) return;
            if (Tower == null || Tower.Hidden || Views.Held || Views.Broadcasting) return;
            if (Views.Hud != null && (Views.Hud.Hidden || !Views.Hud.enabled)) return;
            int car = System.Array.IndexOf(Cars, Views.Car);
            if (car < 0 || car >= Day.Cars.Count) return;
            CarRaceLog log = Day.Cars[car];
            k = Screen.height / 1080f;
            Style();

            Shown now = Get(log.Driver);
            RaceDriver nextDriver = watch.Next(car, log);
            Shown next = nextDriver != null ? Get(nextDriver) : null;

            float nameWidth = NameWidth(now, initial, surname);
            // As wide as the tower, or as the longest name needs.
            Rect area = Tower.Area;
            float width = Mathf.Max(area.width > 0f ? area.width / k : CardWidth, Pad + 36f + Pad + nameWidth + 18f + Block58);
            if (next != null)
                width = Mathf.Max(width, NextTab + 12f + 28f + 10f + NameWidth(next, nextInitial, nextSurname) + 14f + Width(nextOvr, next.Ovr) + Pad);

            float x = area.width > 0f ? area.x / k : 21f;
            float y = 1080f - Bottom - Strip - Stats;
            Card(now, x, y, width, nameWidth);
            if (next != null) Next(next, x, y - Gap - NextHeight, width);
        }

        private void Card(Shown s, float x, float y, float width, float nameWidth)
        {
            DriverFacts f = s.Facts;
            Fill(R(x, y, width, Strip + Stats), Panel, new Vector4(Radius, Radius, Radius, Radius));
            Flag(f.Nation, R(x + Pad, y + (Strip - 27f) / 2f, 36f, 27f));
            Name(s, x + Pad + 36f + Pad, y, Strip, initial, surname);

            // The overall, on its white block at the right of the strip.
            float bx = x + width - Block58;
            Fill(R(bx, y, Block58, Strip), Block, new Vector4(0f, Radius, 0f, 0f));
            Text(R(bx, y + 5f, Block58, 12f), "OVR", label, InkDim, TextAnchor.MiddleCenter);
            Text(R(bx, y + 16f, Block58, 36f), s.Ovr, ovr, Ink, TextAnchor.MiddleCenter);

            // The five stats, each a label, its number and a bar.
            float sy = y + Strip;
            Fill(R(x, sy, width, Stats), Under, new Vector4(0f, 0f, Radius, Radius));
            const float ColumnGap = 10f;
            float column = (width - 2f * Pad - 4f * ColumnGap) / 5f;
            for (int i = 0; i < 5; i++)
            {
                float cx = x + Pad + i * (column + ColumnGap);
                Text(R(cx, sy + 8f, column, 13f), StatNames[i], label, Dim, TextAnchor.MiddleLeft);
                Text(R(cx, sy + 22f, column, 22f), s.Stats[i], number, Color.white, TextAnchor.MiddleLeft);
                Rect bar = R(cx, sy + 47f, column, 4f);
                Fill(bar, BarTrack, Vector4.one * 2f);
                Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(f.Stats[i] / 100f), bar.height), Color.white, Vector4.one * 2f);
            }
        }

        /// <summary>The driver who gets in at the coming stop: a red NEXT tab, then flag, name and overall.</summary>
        private void Next(Shown s, float x, float y, float width)
        {
            Fill(R(x, y, width, NextHeight), Panel, Vector4.one * Radius);
            Fill(R(x, y, NextTab, NextHeight), Red, new Vector4(Radius, 0f, 0f, Radius));
            Text(R(x, y, NextTab, NextHeight), "NEXT", tab, Color.white, TextAnchor.MiddleCenter);
            float cx = x + NextTab + 12f;
            Flag(s.Facts.Nation, R(cx, y + (NextHeight - 21f) / 2f, 28f, 21f));
            cx += 28f + 10f;
            cx = Name(s, cx, y, NextHeight, nextInitial, nextSurname);
            Text(R(cx + 10f, y, Width(nextOvr, s.Ovr) + 4f, NextHeight), s.Ovr, nextOvr, Dim, TextAnchor.MiddleLeft);
        }

        /// <summary>The initial, smaller, then the surname, centred on the row. Returns where the name ends, px on the layout.</summary>
        private float Name(Shown s, float x, float y, float height, GUIStyle small, GUIStyle big)
        {
            DriverFacts f = s.Facts;
            if (f.Initial.Length > 0)
            {
                float w = Width(small, f.Initial) + 5f;
                Text(R(x, y, w, height), f.Initial, small, Color.white, TextAnchor.MiddleLeft);
                x += w;
            }
            float sw = Width(big, f.Surname);
            Text(R(x, y, sw + 4f, height), f.Surname, big, Color.white, TextAnchor.MiddleLeft);
            return x + sw;
        }

        private float NameWidth(Shown s, GUIStyle small, GUIStyle big) =>
            (s.Facts.Initial.Length > 0 ? Width(small, s.Facts.Initial) + 5f : 0f) + Width(big, s.Facts.Surname);

        /// <summary>Text width, px on the layout.</summary>
        private float Width(GUIStyle style, string s) => style.CalcSize(new GUIContent(s)).x / k;

        private void Flag(string nation, Rect r)
        {
            Texture2D flag = FlagOf(nation);
            if (flag == null) return;
            GUI.DrawTexture(r, flag, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 2f * k);
        }

        private Texture2D FlagOf(string nation)
        {
            if (string.IsNullOrEmpty(nation)) return null;
            if (!flags.TryGetValue(nation, out Texture2D flag)) flags[nation] = flag = Resources.Load<Texture2D>("Flags/" + nation);
            return flag;
        }

        private Shown Get(RaceDriver driver)
        {
            if (shown.TryGetValue(driver.Id, out Shown s)) return s;
            DriverFacts f = DriverFacts.Of(driver, Game);
            var stats = new string[f.Stats.Length];
            for (int i = 0; i < stats.Length; i++) stats[i] = f.Stats[i].ToString();
            s = new Shown { Facts = f, Ovr = f.Ovr.ToString(), Stats = stats };
            shown[driver.Id] = s;
            return s;
        }

        private void Style()
        {
            if (initial == null)
            {
                Font bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
                Font semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
                var basic = new GUIStyle(GUI.skin.label) { padding = new RectOffset(), margin = new RectOffset(), wordWrap = false, clipping = TextClipping.Overflow };
                initial = new GUIStyle(basic) { font = semiBold };
                surname = new GUIStyle(basic) { font = bold };
                ovr = new GUIStyle(basic) { font = bold };
                label = new GUIStyle(basic) { font = bold };
                number = new GUIStyle(basic) { font = bold };
                nextInitial = new GUIStyle(basic) { font = semiBold };
                nextSurname = new GUIStyle(basic) { font = bold };
                nextOvr = new GUIStyle(basic) { font = bold };
                tab = new GUIStyle(basic) { font = bold };
            }
            initial.fontSize = Size(22f);
            surname.fontSize = Size(30f);
            ovr.fontSize = Size(30f);
            label.fontSize = Size(11f);
            number.fontSize = Size(18f);
            nextInitial.fontSize = Size(16f);
            nextSurname.fontSize = Size(22f);
            nextOvr.fontSize = Size(18f);
            tab.fontSize = Size(17f);
        }

        private int Size(float px) => Mathf.Max(1, Mathf.RoundToInt(px * k));

        private Rect R(float x, float y, float w, float h) => new Rect(x * k, y * k, w * k, h * k);

        private void Fill(Rect r, Color colour, Vector4 radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, radius * k);

        private static void Text(Rect r, string s, GUIStyle style, Color colour, TextAnchor anchor)
        {
            style.alignment = anchor;
            style.normal.textColor = colour;
            GUI.Label(r, s, style);
        }
    }
}
