using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Speed and gear in the bottom right corner while driving, with a fuel bar and a tyre bar above
    /// them. The gear is blue in automatic and white in manual. The speed turns amber while the pit
    /// lane limiter is on. The fuel bar is green in Conserve, white in Normal and orange in Push.
    /// The tyre bar turns red once the tyres are nearly worn out. Hidden in the free camera.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public CarController Car;

        private GUIStyle gear, speed;
        private static readonly Color Auto = new Color(0.55f, 0.75f, 1f);
        private static readonly Color Limiter = new Color(1f, 0.75f, 0.2f);
        private static readonly Color Conserve = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color Push = new Color(1f, 0.5f, 0.15f);
        private static readonly Color Worn = new Color(0.95f, 0.25f, 0.2f);

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
            speed.normal.textColor = Car.Limiting ? Limiter : Color.white;

            float margin = 40f * scale, width = 260f * scale;
            float x = Screen.width - margin - width;
            var gearRect = new Rect(x, Screen.height - margin - 110f * scale, width, 110f * scale);
            var speedRect = new Rect(x, gearRect.y - 52f * scale, width, 52f * scale);
            Shadowed(speedRect, Mathf.RoundToInt(Car.Speed * 3.6f).ToString(), speed, scale);
            Shadowed(gearRect, Car.Gear == 0 ? "R" : Car.Gear.ToString(), gear, scale);

            Consumables s = Car.Supplies;
            if (s == null) return;
            float barWidth = 180f * scale, barHeight = 7f * scale, gap = 6f * scale;
            float bx = Screen.width - margin - barWidth;
            float tyres = 1f - s.Wear;
            Color fuelColour = s.Map == EngineMap.Push ? Push : s.Map == EngineMap.Conserve ? Conserve : Color.white;
            Bar(new Rect(bx, speedRect.y - gap - barHeight, barWidth, barHeight), tyres, tyres < 0.15f ? Worn : Color.white);
            Bar(new Rect(bx, speedRect.y - 2f * (gap + barHeight), barWidth, barHeight), s.Fuel / s.Capacity, fuelColour);
        }

        /// <summary>A thin bar filled from the right, on a dark track.</summary>
        private static void Bar(Rect r, float fill, Color colour)
        {
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = colour;
            float w = r.width * Mathf.Clamp01(fill);
            GUI.DrawTexture(new Rect(r.xMax - w, r.y, w, r.height), Texture2D.whiteTexture);
            GUI.color = was;
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
