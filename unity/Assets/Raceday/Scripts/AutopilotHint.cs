using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// While the AI drives your car in your own stint, a hint at the bottom of the screen: the Delete key, then
    /// "Toggle AI autopilot on/off", drawn as the hold-Enter hint is (<see cref="StageSequence"/>). It sits a row
    /// above that hint while both show, and above the HUD's dial while that shows (<see cref="DriveHud.Clearance"/>). Laid out on a 1920 by 1080 screen and scaled to its height.
    /// </summary>
    public sealed class AutopilotHint : MonoBehaviour
    {
        /// <summary>True while the hint should show.</summary>
        public System.Func<bool> Showing;
        /// <summary>The stages whose hold-Enter hint this one sits above.</summary>
        public StageSequence[] Stages;

        private const string Key = "Del";
        private const string Words = "Toggle AI autopilot on/off";

        private static readonly Color Ink = new Color(0.07f, 0.07f, 0.07f);
        private static readonly Color Cap = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.6f);

        private GUIStyle text, cap;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || DebugMenu.Bare || Showing == null || !Showing()) return;
            float k = Screen.height / 1080f;
            if (text == null)
            {
                Font font = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
                text = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(),
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    font = font,
                };
                cap = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, font = Resources.Load<Font>("Fonts/TitilliumWeb-Bold") };
            }
            text.fontSize = Mathf.RoundToInt(26f * k);
            cap.fontSize = Mathf.RoundToInt(20f * k);

            var words = new GUIContent(Words);
            var key = new GUIContent(Key);
            float row = 36f * k, keyHeight = 34f * k, gap = 10f * k;
            float keyWidth = Mathf.Max(50f * k, cap.CalcSize(key).x + 20f * k);
            float w = text.CalcSize(words).x;
            float width = keyWidth + gap + w;
            float raise = 0f;
            if (Stages != null)
                foreach (StageSequence s in Stages)
                    if (s != null && s.HintShown) raise = 52f * k;
            // Above the HUD's dial while it shows.
            if (DriveHud.Clearance > 0f) raise = Mathf.Max(raise, (DriveHud.Clearance + 10f - 64f) * k + row / 2f);
            float x = (Screen.width - width) / 2f, top = Screen.height - 64f * k - row / 2f - raise, mid = top + row / 2f;

            var keyRect = new Rect(x, mid - keyHeight / 2f, keyWidth, keyHeight);
            GUI.DrawTexture(keyRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Cap, 0f, 6f * k);
            cap.normal.textColor = Ink;
            GUI.Label(keyRect, key, cap);
            x += keyWidth + gap;

            var r = new Rect(x, top, w, row);
            text.normal.textColor = Shadow;
            GUI.Label(new Rect(r.x + 2f * k, r.y + 2f * k, r.width, r.height), words, text);
            text.normal.textColor = Color.white;
            GUI.Label(r, words, text);
        }
    }
}
