using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Frames per second in the top right corner, averaged over half a second, with frame times underneath. Always shown.
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

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || DebugMenu.Bare) return;
            float scale = Screen.height / 1080f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.RoundToInt(28 * scale);
            float right = Screen.width - 20f * scale, top = 16f * scale;
            Label(new Rect(right - 200f * scale, top, 200f * scale, 40f * scale), shown, Color.white);

            style.fontSize = Mathf.RoundToInt(18 * scale);
            float y = top + 40f * scale, line = 24f * scale, column = 58f * scale;
            foreach (Row row in rows)
            {
                if (row.Average.Length == 0) continue;
                Label(new Rect(right - column, y, column, line), row.Worst, new Color(1f, 1f, 1f, 0.55f));
                Label(new Rect(right - 2f * column, y, column, line), row.Average, Color.white);
                Label(new Rect(right - 3f * column, y, column, line), row.Tag, new Color(1f, 1f, 1f, 0.55f));
                y += line;
            }
        }

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
