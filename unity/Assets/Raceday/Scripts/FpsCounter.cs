using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Frames per second in the top right corner, averaged over half a second. Always shown.</summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        private const float Window = 0.5f;

        private GUIStyle style;
        private float elapsed;
        private int frames;
        private string shown = "";

        private void Update()
        {
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
            var r = new Rect(Screen.width - 220f * scale, 16f * scale, 200f * scale, 40f * scale);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 2f * scale, r.y + 2f * scale, r.width, r.height), shown, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, shown, style);
        }
    }
}
