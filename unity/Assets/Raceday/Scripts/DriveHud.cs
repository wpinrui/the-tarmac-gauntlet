using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Speed and gear in the bottom right corner while driving. The gear is blue in automatic and
    /// white in manual. Hidden in the free camera.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public CarController Car;

        private GUIStyle gear, speed;
        private static readonly Color Auto = new Color(0.55f, 0.75f, 1f);

        private void OnGUI()
        {
            if (Car == null || !Car.InputEnabled || Event.current.type != EventType.Repaint) return;
            float scale = Screen.height / 1080f;
            if (gear == null)
            {
                gear = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
                speed = new GUIStyle(gear);
            }
            gear.fontSize = Mathf.RoundToInt(96 * scale);
            speed.fontSize = Mathf.RoundToInt(44 * scale);
            gear.normal.textColor = Car.Automatic ? Auto : Color.white;
            speed.normal.textColor = Color.white;

            float margin = 40f * scale, width = 260f * scale;
            float x = Screen.width - margin - width;
            var gearRect = new Rect(x, Screen.height - margin - 110f * scale, width, 110f * scale);
            var speedRect = new Rect(x, gearRect.y - 52f * scale, width, 52f * scale);
            Shadowed(speedRect, Mathf.RoundToInt(Car.Speed * 3.6f).ToString(), speed, scale);
            Shadowed(gearRect, Car.Gear == 0 ? "R" : Car.Gear.ToString(), gear, scale);
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, float scale)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 2f * scale, r.y + 2f * scale, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
    }
}
