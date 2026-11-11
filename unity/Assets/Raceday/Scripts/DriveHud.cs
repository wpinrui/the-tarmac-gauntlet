using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Speed and gear in the bottom right corner while driving, in the race font. Above them a rev bar
    /// lights from the left as the engine climbs to the redline, and flashes blue at the shift point.
    /// Above that are the fuel and tyre bars, each behind its icon. The gear is blue in automatic and
    /// white in manual. The speed turns amber while the pit lane limiter is on. The fuel bar is green in
    /// Conserve, white in Normal and orange in Push. The tyre bar turns red once the tyres are nearly
    /// worn out. Green and red bars to the left of the speed show the throttle and brake. Hidden in the
    /// free camera. Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public CarController Car;
        /// <summary>True while the TV view shows the broadcast.</summary>
        [System.NonSerialized] public bool Hidden;

        private GUIStyle gear, speed;
        private Texture2D fuelIcon, tyreIcon;
        private static readonly Color Auto = new Color(0.55f, 0.75f, 1f);
        private static readonly Color Limiter = new Color(1f, 0.75f, 0.2f);
        private static readonly Color Conserve = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color Push = new Color(1f, 0.5f, 0.15f);
        private static readonly Color Worn = new Color(0.95f, 0.25f, 0.2f);
        private static readonly Color ThrottleColour = new Color(0.35f, 0.85f, 0.4f);
        private static readonly Color BrakeColour = new Color(0.95f, 0.25f, 0.2f);
        private static readonly Color Track = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color Rev = new Color(0.92f, 0.92f, 0.92f);
        private static readonly Color RevHigh = new Color(1f, 0.75f, 0.2f);
        private static readonly Color RevRed = new Color(0.95f, 0.25f, 0.2f);

        /// <summary>Lights in the rev bar, and the engine fraction where it flashes to shift up.</summary>
        private const int RevLights = 10;
        private const float ShiftPoint = 0.95f;
        /// <summary>Titillium's digit height, and the space below the baseline in its line, as fractions of the font size.</summary>
        private const float CapHeight = 0.68f, Descent = 0.388f, LineHeight = 1.521f;

        private void OnGUI()
        {
            if (Car == null || Hidden || !Car.InputEnabled || Event.current.type != EventType.Repaint) return;
            float scale = Screen.height / 1080f;
            if (gear == null)
            {
                gear = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, padding = new RectOffset(), font = Resources.Load<Font>("Fonts/TitilliumWeb-Bold") };
                speed = new GUIStyle(gear) { font = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold") };
                fuelIcon = RaceTower.FuelIcon();
                tyreIcon = RaceTower.TyreIcon();
            }
            gear.fontSize = Mathf.RoundToInt(110 * scale);
            speed.fontSize = Mathf.RoundToInt(48 * scale);
            gear.normal.textColor = Car.Automatic ? Auto : Color.white;
            speed.normal.textColor = Car.Limiting ? Limiter : Color.white;

            // Stacked up from the gear's baseline, 40 above the bottom of the screen.
            float margin = 40f * scale, width = 160f * scale;
            float right = Screen.width - margin, left = right - width;
            float gearBase = Screen.height - margin, gearTop = gearBase - CapHeight * gear.fontSize;
            float speedBase = gearTop - 16f * scale, speedTop = speedBase - CapHeight * speed.fontSize;
            Shadowed(Line(left, width, speedBase, speed.fontSize), Mathf.RoundToInt(Car.Speed * 3.6f).ToString(), speed, scale);
            Shadowed(Line(left, width, gearBase, gear.fontSize), Car.Gear == 0 ? "R" : Car.Gear.ToString(), gear, scale);

            // Throttle and brake, as tall bars on the left under the rev bar.
            float pedalWidth = 10f * scale, pedalGap = 6f * scale;
            float px = left + 2f * pedalWidth + pedalGap;
            Pedal(new Rect(px - pedalWidth, speedTop, pedalWidth, gearBase - speedTop), Car.Brake, BrakeColour);
            Pedal(new Rect(px - 2f * pedalWidth - pedalGap, speedTop, pedalWidth, gearBase - speedTop), Car.Throttle, ThrottleColour);

            // The rev bar, across the full width above the speed.
            float revHeight = 12f * scale, revGap = 4f * scale;
            float revTop = speedTop - 12f * scale - revHeight;
            float light = (width - (RevLights - 1) * revGap) / RevLights;
            float rev = Car.EngineFraction;
            bool shift = rev >= ShiftPoint && Car.Gear > 0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f;
            for (int i = 0; i < RevLights; i++)
            {
                bool lit = rev * RevLights >= i + 0.5f;
                Color c = !lit ? Track : shift ? Auto : i >= RevLights - 2 ? RevRed : i >= RevLights - 5 ? RevHigh : Rev;
                Fill(new Rect(left + i * (light + revGap), revTop, light, revHeight), c);
            }

            Consumables s = Car.Supplies;
            if (s == null) return;
            float icon = 22f * scale, barHeight = 7f * scale, rowGap = 8f * scale;
            float tyres = 1f - s.Wear;
            Color fuelColour = s.Map == EngineMap.Push ? Push : s.Map == EngineMap.Conserve ? Conserve : Color.white;
            float tyreRow = revTop - 14f * scale - icon;
            float fuelRow = tyreRow - rowGap - icon;
            Supply(new Rect(left, tyreRow, width, icon), tyreIcon, tyres, tyres < 0.15f ? Worn : Color.white, barHeight);
            Supply(new Rect(left, fuelRow, width, icon), fuelIcon, s.Fuel / s.Capacity, fuelColour, barHeight);
        }

        /// <summary>The rect of a line of text with its baseline at baseline.</summary>
        private static Rect Line(float left, float width, float baseline, int size) =>
            new Rect(left, baseline + Descent * size - LineHeight * size, width, LineHeight * size);

        /// <summary>An icon on the left of a row, then a thin bar filled from the left in the rest of it.</summary>
        private static void Supply(Rect row, Texture2D icon, float fill, Color colour, float barHeight)
        {
            GUI.DrawTexture(new Rect(row.x, row.y, row.height, row.height), icon, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
            float x = row.x + row.height * 1.5f;
            var bar = new Rect(x, row.center.y - barHeight / 2f, row.xMax - x, barHeight);
            Fill(bar, Track);
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fill), bar.height), colour);
        }

        /// <summary>A tall bar filled from the bottom, on a dark track.</summary>
        private static void Pedal(Rect r, float fill, Color colour)
        {
            Fill(r, Track);
            float h = r.height * Mathf.Clamp01(fill);
            Fill(new Rect(r.x, r.yMax - h, r.width, h), colour);
        }

        private static void Fill(Rect r, Color colour) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);

        private static void Shadowed(Rect r, string text, GUIStyle style, float scale)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 2f * scale, r.y + 2f * scale, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        private void OnDestroy()
        {
            if (fuelIcon != null) Destroy(fuelIcon);
            if (tyreIcon != null) Destroy(tyreIcon);
        }
    }
}
