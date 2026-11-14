using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Frames per second, averaged over half a second, with frame times after it, on one line left of the top right
    /// buttons and level with them. It keeps to the same 1920 by 1080 layout as those buttons, centred across a wider
    /// screen, so it sits clear of them, the timing tower and the timing page's map at every resolution. Hidden until `
    /// (the backquote key) shows it, and ` again hides it; the choice is kept between runs of the game.
    /// The frame times come from Unity's FrameTimingManager: CPU is the main thread's work, RT the render thread's and GPU
    /// the graphics card's, each in milliseconds. Each row is the average over the last second and, dimmer, the worst
    /// frame in it. Waiting for v-sync is left out of the CPU figure, so the rows show the headroom under a 60 fps cap.
    /// A row with no timings in the last second is not drawn: the GPU row is missing when the driver gives no GPU timing.
    /// </summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        private const float Window = 0.5f;
        private const float TimingWindow = 1f;

        private sealed class Row
        {
            public readonly string Tag;
            public string Average = "", Worst = "";
            private double sum, worst;
            private int count;

            public Row(string tag) => Tag = tag;

            public void Add(double ms)
            {
                if (ms <= 0) return;
                sum += ms;
                if (ms > worst) worst = ms;
                count++;
            }

            public void Show()
            {
                Average = count > 0 ? (sum / count).ToString("0.0") : "";
                Worst = count > 0 ? worst.ToString("0.0") : "";
                sum = worst = 0;
                count = 0;
            }
        }

        private const string ShownKey = "fpsCounter.shown";
        private static bool? visible;
        private bool toggleHeld;

        /// <summary>Whether the counter shows: off on a fresh game, then as last toggled with `.</summary>
        public static bool Visible
        {
            get => visible ??= PlayerPrefs.GetInt(ShownKey, 0) != 0;
            set
            {
                visible = value;
                PlayerPrefs.SetInt(ShownKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        private readonly Row[] rows = { new Row("CPU"), new Row("RT"), new Row("GPU") };
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private GUIStyle style;
        private float elapsed, timingElapsed;
        private int frames;
        private string shown = "";

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                FrameTiming t = timing[0];
                rows[0].Add(t.cpuMainThreadFrameTime - t.cpuMainThreadPresentWaitTime);
                rows[1].Add(t.cpuRenderThreadFrameTime);
                rows[2].Add(t.gpuFrameTime);
            }
            timingElapsed += Time.unscaledDeltaTime;
            if (timingElapsed >= TimingWindow)
            {
                foreach (Row row in rows) row.Show();
                timingElapsed = 0f;
            }

            elapsed += Time.unscaledDeltaTime;
            frames++;
            if (elapsed < Window) return;
            shown = Mathf.RoundToInt(frames / elapsed).ToString();
            elapsed = 0f;
            frames = 0;
        }

        /// <summary>Where the line ends and how tall it is, px on the layout: left of the live timing buttons.</summary>
        private const float End = 1708f - 28f, Top = 40f, Height = 44f;
        /// <summary>Spaces on the layout: between a figure and its neighbour, and between one row and the next.</summary>
        private const float Gap = 8f, RowGap = 28f;

        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.keyCode == KeyCode.BackQuote)
            {
                // Once per press: a held key repeats its KeyDown.
                if (ev.type == EventType.KeyDown && !toggleHeld) Visible = !Visible;
                if (ev.type == EventType.KeyDown) toggleHeld = true;
                if (ev.type == EventType.KeyUp) toggleHeld = false;
            }
            if (!Visible) return;
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || DebugMenu.Bare) return;
            float k = Screen.height / 1080f;
            float offset = (Screen.width / k - 1920f) / 2f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 0) };

            // Columns as wide as their widest figure, so the line keeps still as the numbers change.
            style.fontSize = Mathf.RoundToInt(28 * k);
            float fps = Width("000"), gap = Gap * k, rowGap = RowGap * k;
            style.fontSize = Mathf.RoundToInt(18 * k);
            float tag = Width("GPU"), figure = Width("000.0");
            float row = tag + 2f * (gap + figure);
            float x = (End + offset) * k - (fps + rows.Length * (rowGap + row)), y = Top * k, h = Height * k;

            style.fontSize = Mathf.RoundToInt(28 * k);
            Label(new Rect(x, y, fps, h), shown, Color.white);
            x += fps;
            style.fontSize = Mathf.RoundToInt(18 * k);
            foreach (Row r in rows)
            {
                x += rowGap;
                if (r.Average.Length > 0)
                {
                    Label(new Rect(x, y, tag, h), r.Tag, new Color(1f, 1f, 1f, 0.55f));
                    Label(new Rect(x + tag + gap, y, figure, h), r.Average, Color.white);
                    Label(new Rect(x + tag + 2f * gap + figure, y, figure, h), r.Worst, new Color(1f, 1f, 1f, 0.55f));
                }
                x += row;
            }
        }

        private float Width(string text) => style.CalcSize(new GUIContent(text)).x;

        private void Label(Rect r, string text, Color colour)
        {
            float shadow = Mathf.Max(1f, style.fontSize / 14f);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + shadow, r.y + shadow, r.width, r.height), text, style);
            style.normal.textColor = colour;
            GUI.Label(r, text, style);
        }
    }
}
