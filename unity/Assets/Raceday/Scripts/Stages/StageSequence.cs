using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Runs <see cref="SkippableStage"/>s one after another: the race's intro before the green, and its run after the
    /// flag. While a stage runs, a hint at the bottom of the screen reads "Press and hold [Enter] to skip [stage]",
    /// with a circle on its left. Holding Enter fills the circle as a sector, clockwise from the top, and the full
    /// circle skips that one stage (<see cref="SkipHold"/>). Laid out on a 1920 by 1080 screen and scaled to its height.
    /// </summary>
    public sealed class StageSequence : MonoBehaviour
    {
        public KeyboardState Keys;

        /// <summary>True from <see cref="Play"/> until the last stage is done.</summary>
        public bool Running => current >= 0 && current < stages.Count;

        /// <summary>True once every stage is done.</summary>
        public bool Finished { get; private set; }

        /// <summary>The stage running now, or null.</summary>
        public SkippableStage Current => Running ? stages[current] : null;

        /// <summary>Raised once, when the last stage is done.</summary>
        public event Action Completed;

        private readonly List<SkippableStage> stages = new List<SkippableStage>();
        private int current = -1;
        private readonly SkipHold hold = new SkipHold();

        /// <summary>Adds a stage to run after the ones before it. It is switched off until its turn.</summary>
        public void Add(SkippableStage stage)
        {
            if (stage == null || current >= 0) return;
            stage.enabled = false;
            stages.Add(stage);
        }

        /// <summary>Starts the first stage. Calls after the first do nothing.</summary>
        public void Play()
        {
            if (current >= 0 || Finished) return;
            Next();
        }

        /// <summary>Ends the sequence without running its stages, as for a race resumed after them.</summary>
        public void MarkPlayed()
        {
            if (current >= 0 || Finished) return;
            current = stages.Count - 1;
            Next();
        }

        private void Next()
        {
            hold.Reset();
            current++;
            if (current < stages.Count)
            {
                SkippableStage stage = stages[current];
                stage.enabled = true;
                stage.Begin();
                return;
            }
            Finished = true;
            Completed?.Invoke();
        }

        private void Update()
        {
            if (!Running) return;
            SkippableStage stage = stages[current];
            if (stage.Done)
            {
                Next();
                return;
            }
            if (!stage.Skippable)
            {
                hold.Reset();
                return;
            }
            bool held = Keys != null && (Keys.Held(KeyCode.Return) || Keys.Held(KeyCode.KeypadEnter));
            if (!hold.Step(held, Time.unscaledDeltaTime)) return;
            stage.Skip();
            if (stage.Done) Next();
        }

        // ---------------------------------------------------------------- the hint

        private static readonly Color Ink = new Color(0.07f, 0.07f, 0.07f);
        private static readonly Color Track = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color Cap = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.6f);

        private GUIStyle text;
        private Texture2D disc, enter, sector;
        private Color32[] sectorPixels;
        private float sectorFill = -1f;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || !Running || DebugMenu.Bare) return;
            SkippableStage stage = stages[current];
            if (!stage.Skippable) return;
            float k = Screen.height / 1080f;
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(),
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    font = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold"),
                };
                disc = RaceTower.Shape(1f, 1f, (x, y) => (x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f) < 0.25f);
                enter = RaceTower.Shape(1f, 1f, EnterArrow);
            }
            text.fontSize = Mathf.RoundToInt(26f * k);

            var before = new GUIContent("Press and hold");
            var after = new GUIContent("to skip " + stage.Name);
            float row = 36f * k, circle = 30f * k, keyWidth = 50f * k, keyHeight = 34f * k;
            float gap = 10f * k, circleGap = 14f * k;
            float w1 = text.CalcSize(before).x, w2 = text.CalcSize(after).x;
            float width = circle + circleGap + w1 + gap + keyWidth + gap + w2;
            float x = (Screen.width - width) / 2f, top = Screen.height - 64f * k - row / 2f, mid = top + row / 2f;

            var round = new Rect(x, mid - circle / 2f, circle, circle);
            GUI.DrawTexture(round, disc, ScaleMode.StretchToFill, true, 0f, Track, 0f, 0f);
            if (hold.Fill > 0f) GUI.DrawTexture(round, Sector(hold.Fill, Mathf.CeilToInt(circle)), ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
            x += circle + circleGap;

            Shadowed(new Rect(x, top, w1, row), before, k);
            x += w1 + gap;

            var key = new Rect(x, mid - keyHeight / 2f, keyWidth, keyHeight);
            GUI.DrawTexture(key, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Cap, 0f, 6f * k);
            float glyph = 22f * k;
            GUI.DrawTexture(new Rect(key.center.x - glyph / 2f, key.center.y - glyph / 2f, glyph, glyph), enter, ScaleMode.StretchToFill, true, 0f, Ink, 0f, 0f);
            x += keyWidth + gap;

            Shadowed(new Rect(x, top, w2, row), after, k);
        }

        /// <summary>The return arrow on the Enter key: down the right, along the bottom and out to the left in a point.</summary>
        private static bool EnterArrow(float x, float y)
        {
            bool stem = x > 0.74f && x < 0.86f && y > 0.38f && y < 0.9f;
            bool bar = x > 0.3f && x < 0.86f && y > 0.38f && y < 0.5f;
            bool head = x > 0.1f && x < 0.36f && Mathf.Abs(y - 0.44f) < x - 0.1f;
            return stem || bar || head;
        }

        /// <summary>
        /// A white sector of a circle size px across, fill of the way round clockwise from the top, its edges
        /// smoothed over a pixel. Remade only when the fill changes.
        /// </summary>
        private Texture2D Sector(float fill, int size)
        {
            size = Mathf.Max(size, 8);
            if (sector == null || sector.width != size)
            {
                if (sector != null) Destroy(sector);
                sector = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                sectorPixels = new Color32[size * size];
                sectorFill = -1f;
            }
            if (fill == sectorFill) return sector;
            sectorFill = fill;
            float c = size / 2f, radius = c, sweep = fill * 2f * Mathf.PI;
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float dx = i + 0.5f - c, dy = j + 0.5f - c;   // y up: row 0 is the bottom
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Clamp01(radius - r + 0.5f);
                float a = Mathf.Atan2(dx, dy);   // 0 at the top, clockwise
                if (a < 0f) a += 2f * Mathf.PI;
                float cover;
                if (fill >= 1f) cover = 1f;
                else if (a <= sweep) cover = Mathf.Clamp01(Across(Mathf.Min(a, sweep - a), r) + 0.5f);
                else cover = Mathf.Clamp01(0.5f - Across(Mathf.Min(a - sweep, 2f * Mathf.PI - a), r));
                sectorPixels[j * size + i] = new Color32(255, 255, 255, (byte)(255f * edge * cover));
            }
            sector.SetPixels32(sectorPixels);
            sector.Apply();
            return sector;
        }

        /// <summary>How far a point r from the centre lies from a radial edge an angle away, px.</summary>
        private static float Across(float angle, float r) => r * Mathf.Sin(Mathf.Min(angle, Mathf.PI / 2f));

        private void Shadowed(Rect r, GUIContent content, float k)
        {
            text.normal.textColor = Shadow;
            GUI.Label(new Rect(r.x + 2f * k, r.y + 2f * k, r.width, r.height), content, text);
            text.normal.textColor = Color.white;
            GUI.Label(r, content, text);
        }

        private void OnDestroy()
        {
            if (disc != null) Destroy(disc);
            if (enter != null) Destroy(enter);
            if (sector != null) Destroy(sector);
        }
    }
}
