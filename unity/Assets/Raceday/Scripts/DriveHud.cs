using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The HUD while driving, kept small. At the bottom centre a round dial: rev ticks round the gear, which light
    /// from the left as the engine climbs to the redline and flash blue at the shift point, and the speed in km/h
    /// under the gear. The gear is blue in automatic and white in manual, and the speed turns amber while the pit
    /// lane limiter is on.
    ///
    /// In the bottom right corner, three cards. The top one is the car's condition, as a number and a bar. Once the
    /// car is damaged, its bar is striped as far as the spare parts on hand would repair it at a stop and grey on to
    /// what a full repair gives back, and a line under it gives the parts on hand, the parts a full repair takes and
    /// the condition a stop would repair it to. The condition is amber once damaged, red once the damage would bring an
    /// AI car in (<see cref="PitPlan.DamageLimit"/>). The middle card is the fuel and tyres, each a bar with the laps
    /// it has left, to a tenth, as learned over the stint (<see cref="Consumables.Stint"/>); the tyres turn red once
    /// nearly worn out. The bottom card is the engine maps, each named beside its key, the one in use filled in its
    /// colour. Laid out on a 1920 by 1080 screen and scaled to the screen's height.
    /// </summary>
    public sealed class DriveHud : MonoBehaviour
    {
        public CarController Car;
        /// <summary>True while the TV view shows the broadcast.</summary>
        [System.NonSerialized] public bool Hidden;

        /// <summary>Height the dial takes up from the bottom of the screen, px on the layout, while it shows; else 0.</summary>
        public static float Clearance { get; private set; }

        /// <summary>The engine maps in the order of their keys, with their names.</summary>
        private static readonly EngineMap[] Maps = { EngineMap.Conserve, EngineMap.Normal, EngineMap.Push };
        private static readonly string[] MapNames = { "Conserve", "Normal", "Push" };

        private static readonly Color Ink = new Color(0.07f, 0.07f, 0.07f);
        private static readonly Color Panel = new Color(0.08f, 0.08f, 0.09f, 0.86f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.13f);
        private static readonly Color Seg = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color Missing = new Color(1f, 1f, 1f, 0.24f);
        private static readonly Color Auto = new Color(0.55f, 0.75f, 1f);
        private static readonly Color Amber = new Color(1f, 0.75f, 0.2f);
        private static readonly Color Conserve = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color Push = new Color(1f, 0.55f, 0.17f);
        private static readonly Color Red = new Color(0.95f, 0.27f, 0.24f);
        private static readonly Color Rev = new Color(0.93f, 0.93f, 0.93f);

        /// <summary>Ticks round the dial, the degrees they sweep, and the engine fraction where they flash to shift up.</summary>
        private const int RevTicks = 21;
        private const float Sweep = 240f, ShiftPoint = 0.95f;
        /// <summary>Sizes on the layout, px.</summary>
        private const float Margin = 32f, DialRadius = 80f, CardWidth = 270f, CardGap = 8f, CardPad = 14f, Corner = 10f;
        /// <summary>Width the cards take from the right edge of the screen, px on the layout, for the pit panel to sit beside.</summary>
        public const float CardsWidth = Margin + CardWidth;
        /// <summary>Titillium's digit height, and the space below the baseline in its line, as fractions of the font size.</summary>
        private const float CapHeight = 0.68f, Descent = 0.388f, LineHeight = 1.521f;

        private GUIStyle bold, semi;
        private Texture2D fuelIcon, tyreIcon, hatch;
        private float k;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void OnDisable() => Clearance = 0f;

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Car == null || Hidden || DebugMenu.Bare || !Car.InputEnabled) { Clearance = 0f; return; }
            k = Screen.height / 1080f;
            if (bold == null)
            {
                bold = new GUIStyle(GUI.skin.label) { padding = new RectOffset(), wordWrap = false, clipping = TextClipping.Overflow, font = Resources.Load<Font>("Fonts/TitilliumWeb-Bold") };
                semi = new GUIStyle(bold) { font = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold") };
                fuelIcon = RaceTower.FuelIcon();
                tyreIcon = RaceTower.TyreIcon();
                hatch = Hatch();
            }
            Clearance = Margin + 2f * DialRadius;
            Dial();
            Cards();
        }

        /// <summary>Rev ticks round the gear, the speed under it.</summary>
        private void Dial()
        {
            var c = new Vector2(Screen.width / 2f, Screen.height - (Margin + DialRadius) * k);
            float r = DialRadius * k;
            Fill(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), Panel, r);

            float rev = Car.EngineFraction;
            bool shift = rev >= ShiftPoint && Car.Gear > 0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f;
            float tickWidth = 4f * k, tickLength = 9f * k, outer = 70f * k;
            Matrix4x4 was = GUI.matrix;
            for (int i = 0; i < RevTicks; i++)
            {
                bool lit = rev * RevTicks >= i + 0.5f;
                Color col = !lit ? Track : shift ? Auto : i >= RevTicks - 3 ? Red : i >= RevTicks - 8 ? Amber : Rev;
                GUIUtility.RotateAroundPivot(-Sweep / 2f + i * Sweep / (RevTicks - 1), c);
                Fill(new Rect(c.x - tickWidth / 2f, c.y - outer, tickWidth, tickLength), col, tickWidth / 2f);
                GUI.matrix = was;
            }

            Text(Car.Gear == 0 ? "R" : Car.Gear.ToString(), bold, 72, Car.Automatic ? Auto : Color.white, c.x, c.y + 22f * k, TextAnchor.LowerCenter);
            // The speed and its unit, centred together.
            string kmh = Mathf.RoundToInt(Car.Speed * 3.6f).ToString();
            float speedWidth = Width(kmh, semi, 22), unitWidth = Width("km/h", semi, 12), gap = 4f * k;
            float x = c.x - (speedWidth + gap + unitWidth) / 2f, baseline = c.y + 54f * k;
            Text(kmh, semi, 22, Car.Limiting ? Amber : Color.white, x, baseline, TextAnchor.LowerLeft);
            Text("km/h", semi, 12, Dim, x + speedWidth + gap, baseline, TextAnchor.LowerLeft);
        }

        /// <summary>The condition, supplies and engine map cards, stacked up from the bottom right corner.</summary>
        private void Cards()
        {
            Consumables s = Car.Supplies;
            if (s == null) return;
            float left = Screen.width - (Margin + CardWidth) * k, width = CardWidth * k, pad = CardPad * k;
            float bottom = Screen.height - Margin * k;

            // Engine maps.
            float segHeight = 26f * k, mapPad = 8f * k;
            var card = new Rect(left, bottom - segHeight - 2f * mapPad, width, segHeight + 2f * mapPad);
            Fill(card, Panel, Corner * k);
            float segGap = 4f * k, segWidth = (width - 2f * mapPad - 2f * segGap) / 3f;
            for (int i = 0; i < Maps.Length; i++)
            {
                bool on = s.Map == Maps[i];
                var seg = new Rect(card.x + mapPad + i * (segWidth + segGap), card.y + mapPad, segWidth, segHeight);
                Color colour = Maps[i] == EngineMap.Push ? Push : Maps[i] == EngineMap.Conserve ? Conserve : Color.white;
                Fill(seg, on ? colour : Seg, 6f * k);
                Color ink = on ? Ink : new Color(1f, 1f, 1f, 0.6f);
                float key = 14f * k, gap = 5f * k, name = Width(MapNames[i], semi, 13);
                float x = seg.center.x - (key + gap + name) / 2f;
                var cap = new Rect(x, seg.center.y - key / 2f, key, key);
                GUI.DrawTexture(cap, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, ink, 1.5f * k, 3f * k);
                Text((i + 1).ToString(), bold, 10, ink, cap.center.x, cap.center.y + CapHeight * 10f * k / 2f, TextAnchor.LowerCenter);
                Text(MapNames[i], semi, 13, ink, x + key + gap, seg.center.y + CapHeight * 13f * k / 2f, TextAnchor.LowerLeft);
            }

            // Fuel and tyres.
            float row = 20f * k, rowGap = 6f * k;
            card = new Rect(left, card.y - CardGap * k - (2f * row + rowGap + 2f * pad), width, 2f * row + rowGap + 2f * pad);
            Fill(card, Panel, Corner * k);
            float tyres = 1f - s.Wear;
            Color tyreColour = tyres < 0.15f ? Red : Color.white;
            Supply(new Rect(card.x + pad, card.y + pad, width - 2f * pad, row), fuelIcon, s.Fuel / s.Capacity, Color.white, s.FuelLaps);
            Supply(new Rect(card.x + pad, card.y + pad + row + rowGap, width - 2f * pad, row), tyreIcon, tyres, tyreColour, s.TyreLaps(PitPlan.WearLimit));

            // Condition, and once damaged what a stop would repair.
            CarRaceLog log = s.Log;
            if (log == null) return;
            bool damaged = log.Damage > 0;
            float head = 22f * k, bar = 8f * k, line = 16f * k, gapY = 6f * k;
            float height = 2f * pad + head + gapY + bar + (damaged ? gapY + line : 0f);
            card = new Rect(left, card.y - CardGap * k - height, width, height);
            Fill(card, Panel, Corner * k);
            float condition = (float)log.ConditionAfterRace;
            Color hurt = !damaged ? Color.white : log.Damage >= PitPlan.DamageLimit ? Red : Amber;
            float inner = width - 2f * pad, x0 = card.x + pad, y = card.y + pad;
            float headBase = y + head / 2f + CapHeight * 22f * k / 2f;
            Text("Condition", semi, 14, Dim, x0, headBase, TextAnchor.LowerLeft);
            Text(Mathf.RoundToInt(condition) + "%", semi, 22, hurt, x0 + inner, headBase, TextAnchor.LowerRight);
            y += head + gapY;

            var track = new Rect(x0, y, inner, bar);
            Fill(track, Track, bar / 2f);
            Fill(new Rect(x0, y, inner * condition / 100f, bar), hurt, bar / 2f);
            if (!damaged) return;
            float restore = Mathf.Min((float)log.Damage, (float)CarDamage.PointsFrom(log.SpareParts));
            float full = (float)log.Condition;
            float hatchX = x0 + inner * condition / 100f, hatchW = inner * restore / 100f;
            var striped = new Rect(hatchX, y, hatchW, bar);
            GUI.DrawTextureWithTexCoords(striped, hatch, new Rect(0f, 0f, striped.width / (8f * k), 1f));
            Fill(new Rect(hatchX + hatchW, y, inner * (full - condition - restore) / 100f, bar), Missing);
            y += bar + gapY;

            float lineBase = y + line / 2f + CapHeight * 14f * k / 2f;
            int need = CarDamage.PartsFor(log.Damage);
            string parts = log.SpareParts + (log.SpareParts == 1 ? " part, " : " parts, ") + need + " needed";
            Text(parts, semi, 14, log.SpareParts >= need ? Color.white : hurt, x0, lineBase, TextAnchor.LowerLeft);
            if (restore > 0f) Text("Repairs to " + Mathf.RoundToInt(condition + restore) + "%", semi, 14, new Color(1f, 1f, 1f, 0.75f), x0 + inner, lineBase, TextAnchor.LowerRight);
        }

        /// <summary>An icon, a thin bar filled from the left, and the laps left with its unit on the right.</summary>
        private void Supply(Rect row, Texture2D icon, float fill, Color colour, float lapsLeft)
        {
            float icon16 = 16f * k;
            GUI.DrawTexture(new Rect(row.x, row.center.y - icon16 / 2f, icon16, icon16), icon, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
            float baseline = row.center.y + CapHeight * 18f * k / 2f;
            float unitWidth = Width("laps", semi, 12), gap = 4f * k;
            Text("laps", semi, 12, Dim, row.xMax, baseline, TextAnchor.LowerRight);
            Text(Mathf.Min(lapsLeft, 999.9f).ToString("0.0"), semi, 18, colour, row.xMax - unitWidth - gap, baseline, TextAnchor.LowerRight);
            float x = row.x + icon16 + 12f * k, barHeight = 5f * k;
            var bar = new Rect(x, row.center.y - barHeight / 2f, row.xMax - 74f * k - x, barHeight);
            Fill(bar, Track, barHeight / 2f);
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fill), bar.height), colour, barHeight / 2f);
        }

        /// <summary>Width of text at a layout font size, px on screen.</summary>
        private float Width(string text, GUIStyle style, float size)
        {
            style.fontSize = Mathf.RoundToInt(size * k);
            return style.CalcSize(new GUIContent(text)).x;
        }

        /// <summary>Text at a layout font size with its baseline at baseline, anchored at x by its left, centre or right, over a soft shadow.</summary>
        private void Text(string text, GUIStyle style, float size, Color colour, float x, float baseline, TextAnchor anchor)
        {
            style.fontSize = Mathf.RoundToInt(size * k);
            style.alignment = anchor;
            float w = style.CalcSize(new GUIContent(text)).x + 2f;
            float left = anchor == TextAnchor.LowerLeft ? x : anchor == TextAnchor.LowerRight ? x - w : x - w / 2f;
            float fs = style.fontSize;
            var r = new Rect(left, baseline + Descent * fs - LineHeight * fs, w, LineHeight * fs);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.5f * colour.a);
            GUI.Label(new Rect(r.x + 1f * k, r.y + 1f * k, r.width, r.height), text, style);
            style.normal.textColor = colour;
            GUI.Label(r, text, style);
        }

        private static void Fill(Rect r, Color colour, float radius = 0f)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, 0f, Mathf.Min(radius, r.width / 2f, r.height / 2f));
        }

        /// <summary>Diagonal white stripes, bright and faint, that tile across.</summary>
        private static Texture2D Hatch()
        {
            const int n = 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color(1f, 1f, 1f, (x + y) % n < n / 2 ? 0.8f : 0.3f);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private void OnDestroy()
        {
            if (fuelIcon != null) Destroy(fuelIcon);
            if (tyreIcon != null) Destroy(tyreIcon);
            if (hatch != null) Destroy(hatch);
        }
    }
}
