using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Speed and gear in the bottom right corner while driving, in the race font. Above them a rev bar
    /// lights from the left as the engine climbs to the redline, and flashes blue at the shift point.
    /// Above that are the fuel and tyre bars, each behind its icon, with the laps it has left at its
    /// right end, as learned over the stint (<see cref="Consumables.Stint"/>). The gear is blue in automatic and
    /// white in manual. The speed turns amber while the pit lane limiter is on. The fuel bar is green in
    /// Conserve, white in Normal and orange in Push. Above the fuel bar the engine maps sit on their keys, 1, 2
    /// and 3, the one in use lit in its colour. The tyre bar turns red once the tyres are nearly
    /// worn out. Once the car is damaged, two rows sit above the fuel bar. Behind a spanner, the car's condition as a bar
    /// and a number, amber, red once the damage is bad enough to bring an AI car in (<see cref="PitPlan.DamageLimit"/>); the
    /// faint part of the bar is what the spare parts on hand would restore at a stop. Behind a nut, the spare parts on hand
    /// over the parts a full repair takes, and on the right the condition those parts restore. Green and red bars to the left of the speed show the throttle and brake. Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public CarController Car;
        /// <summary>True while the TV view shows the broadcast.</summary>
        [System.NonSerialized] public bool Hidden;

        private GUIStyle gear, speed, key, laps;
        /// <summary>The engine maps in the order of their keys.</summary>
        private static readonly EngineMap[] Maps = { EngineMap.Conserve, EngineMap.Normal, EngineMap.Push };
        private static readonly Color Ink = new Color(0.07f, 0.07f, 0.07f);
        private Texture2D fuelIcon, tyreIcon, spannerIcon, nutIcon;
        private static readonly Color Auto = new Color(0.55f, 0.75f, 1f);
        private static readonly Color Limiter = new Color(1f, 0.75f, 0.2f);
        private static readonly Color Conserve = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color Push = new Color(1f, 0.5f, 0.15f);
        private static readonly Color Worn = new Color(0.95f, 0.25f, 0.2f);
        /// <summary>The condition the spare parts on hand would restore.</summary>
        private static readonly Color Restore = new Color(1f, 1f, 1f, 0.45f);
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
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Car == null || Hidden || DebugMenu.Bare || !Car.InputEnabled || Event.current.type != EventType.Repaint) return;
            float scale = Screen.height / 1080f;
            if (gear == null)
            {
                gear = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, padding = new RectOffset(), font = Resources.Load<Font>("Fonts/TitilliumWeb-Bold") };
                speed = new GUIStyle(gear) { font = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold") };
                fuelIcon = RaceTower.FuelIcon();
                tyreIcon = RaceTower.TyreIcon();
                spannerIcon = RaceTower.SpannerIcon();
                nutIcon = RaceTower.NutIcon();
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
            if (laps == null) laps = new GUIStyle(speed);
            laps.fontSize = Mathf.RoundToInt(22 * scale);
            Supply(new Rect(left, tyreRow, width, icon), tyreIcon, tyres, tyres < 0.15f ? Worn : Color.white, barHeight, s.TyreLaps(PitPlan.WearLimit), laps, scale);
            Supply(new Rect(left, fuelRow, width, icon), fuelIcon, s.Fuel / s.Capacity, fuelColour, barHeight, s.FuelLaps, laps, scale);
            float topRow = fuelRow;
            CarRaceLog log = s.Log;
            if (log != null && log.Damage > 0)
            {
                // What a stop would put back with the parts on hand, and what all of it takes.
                double restore = Mathf.Min((float)log.Damage, (float)CarDamage.PointsFrom(log.SpareParts));
                int need = CarDamage.PartsFor(log.Damage);
                float condition = (float)log.ConditionAfterRace;
                Color hurt = log.Damage >= PitPlan.DamageLimit ? Worn : Limiter;
                float partsRow = fuelRow - rowGap - icon;
                topRow = partsRow - rowGap - icon;
                var row = new Rect(left, topRow, width, icon);
                Supply(row, spannerIcon, condition / 100f, hurt, barHeight, condition, laps, scale);
                float number = 40f * scale, barX = row.x + row.height * 1.5f, barWidth = row.xMax - number - 8f * scale - barX;
                Fill(new Rect(barX + barWidth * condition / 100f, row.center.y - barHeight / 2f, barWidth * (float)restore / 100f, barHeight), Restore);

                var parts = new Rect(left, partsRow, width, icon);
                GUI.DrawTexture(new Rect(parts.x, parts.y, parts.height, parts.height), nutIcon, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 0f);
                float baseline = parts.center.y + CapHeight * laps.fontSize / 2f;
                laps.normal.textColor = log.SpareParts >= need ? Color.white : hurt;
                laps.alignment = TextAnchor.LowerLeft;
                Shadowed(Line(barX, barWidth, baseline, laps.fontSize), log.SpareParts + "/" + need, laps, scale);
                laps.alignment = TextAnchor.LowerRight;
                laps.normal.textColor = Restore;
                if (restore > 0) Shadowed(Line(parts.xMax - number - 8f * scale - 60f * scale, 60f * scale + 8f * scale + number, baseline, laps.fontSize), "+" + Mathf.RoundToInt((float)restore), laps, scale);
            }

            // The engine maps, on their keys, above the fuel bar.
            if (key == null) key = new GUIStyle(speed) { alignment = TextAnchor.MiddleCenter };
            key.fontSize = Mathf.RoundToInt(18 * scale);
            float keyHeight = 26f * scale, keyGap = 6f * scale;
            float keyWidth = (width - 2f * keyGap) / 3f;
            float mapRow = topRow - 12f * scale - keyHeight;
            for (int i = 0; i < Maps.Length; i++)
            {
                bool lit = s.Map == Maps[i];
                var r = new Rect(left + i * (keyWidth + keyGap), mapRow, keyWidth, keyHeight);
                Color c = Maps[i] == EngineMap.Push ? Push : Maps[i] == EngineMap.Conserve ? Conserve : Color.white;
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, lit ? c : Track, 0f, 5f * scale);
                key.normal.textColor = lit ? Ink : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(r, (i + 1).ToString(), key);
            }
        }

        /// <summary>The rect of a line of text with its baseline at baseline.</summary>
        private static Rect Line(float left, float width, float baseline, int size) =>
            new Rect(left, baseline + Descent * size - LineHeight * size, width, LineHeight * size);

        /// <summary>An icon on the left of a row, a thin bar filled from the left, and a whole number on the right.</summary>
        private static void Supply(Rect row, Texture2D icon, float fill, Color colour, float barHeight, float lapsLeft, GUIStyle style, float scale)
        {
            GUI.DrawTexture(new Rect(row.x, row.y, row.height, row.height), icon, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
            float number = 40f * scale;
            style.normal.textColor = colour;
            Shadowed(Line(row.xMax - number, number, row.center.y + CapHeight * style.fontSize / 2f, style.fontSize), Mathf.FloorToInt(Mathf.Min(lapsLeft, 999f)).ToString(), style, scale);
            float x = row.x + row.height * 1.5f;
            var bar = new Rect(x, row.center.y - barHeight / 2f, row.xMax - number - 8f * scale - x, barHeight);
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
            if (spannerIcon != null) Destroy(spannerIcon);
            if (nutIcon != null) Destroy(nutIcon);
        }
    }
}
