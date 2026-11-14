using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The next stop's plan for your car, on P, through your own stint and a teammate's: beside the HUD, a red PIT tab
    /// with the time the stop would stand the car in its box, over four rows, each with its keys. 4 and 5 take a lap
    /// of fuel off and put one on, up to a full tank. 6 swaps new tyres and keep. 7 steps through the lineup for the
    /// next driver. 8 steps the repairs through the crew's call, all and none. A row left alone is the crew's call
    /// (<see cref="StopPlanner"/>). While a teammate drives, a fifth row calls the car in on 9, as live timing's
    /// Pit now does (<see cref="PitOrders.Requested"/>), and 9 again calls it off. Laid out on a 1920 by 1080 screen
    /// and scaled to the screen's height.
    /// </summary>
    public sealed class PitPanel : MonoBehaviour
    {
        public CarController Car;
        public KeyboardState Keys;
        public StopPlanner Planner;
        /// <summary>The career, for the drivers' names.</summary>
        public GameState Game;
        public DriveHud Hud;

        public bool Open { get; private set; }

        /// <summary>True while the panel was drawn last frame, for the broadcast graphics to keep clear of it.</summary>
        public static bool Showing { get; private set; }
        /// <summary>The panel's left edge, px on the layout from the right edge of the screen.</summary>
        public const float LeftFromRight = Right + Width;

        private static readonly Color Panel = new Color(12f / 255f, 14f / 255f, 18f / 255f, 0.72f);
        private static readonly Color Red = RaceTower.Hex(0xd8262b);
        private static readonly Color Cap = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color CapText = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);
        private static readonly RepairMode[] RepairSteps = { RepairMode.Crew, RepairMode.All, RepairMode.None };

        /// <summary>Panel size and place, px on the layout: its right edge clears the HUD's cards by 16.</summary>
        private const float Width = 380f, Header = 56f, Row = 38f, Right = DriveHud.CardsWidth + 16f, Bottom = 32f;
        /// <summary>Space above the Pit now row, px on the layout.</summary>
        private const float CallGap = 10f;

        private GUIStyle tab, time, unit, row, key;
        private Texture2D fuel, tyre, helmet, spanner;

        private Consumables Supplies => Car.Supplies;
        private CarRaceLog Log => Supplies?.Log;

        /// <summary>True while your car races on with a stop to plan, whoever drives it.</summary>
        private bool Usable => Car != null && Car.InputEnabled && Planner != null && Supplies != null && !Supplies.RaceOver;

        /// <summary>True while a teammate drives your car: you can call it in.</summary>
        private bool Teammate => Log != null && Log.Driver.Id != RaceEntry.PlayerCharacterDriverId;

        private void Update()
        {
            if (!Usable)
            {
                Open = false;
                return;
            }
            if (Hud != null && Hud.Hidden) return;   // nothing to see over the broadcast, so its keys do nothing there
            if (Keys.Pressed(KeyCode.P)) Open = !Open;
            if (!Open) return;
            PitOrders orders = Supplies.Orders;
            if (Keys.Pressed(KeyCode.Alpha4)) orders.Laps = Mathf.Max(1, Planner.Laps - 1);
            if (Keys.Pressed(KeyCode.Alpha5)) orders.Laps = Mathf.Min(Planner.FullLaps, Planner.Laps + 1);
            if (Keys.Pressed(KeyCode.Alpha6) && Supplies.TyresLeft) orders.Tyres = !Planner.Tyres;
            if (Keys.Pressed(KeyCode.Alpha7) && Log != null) Log.NextDriverIndex = (Log.NextDriverIndex + 1) % Log.Car.Lineup.Count;
            if (Keys.Pressed(KeyCode.Alpha8))
                orders.Repairs = RepairSteps[(System.Array.IndexOf(RepairSteps, orders.Repairs) + 1) % RepairSteps.Length];
            if (Keys.Pressed(KeyCode.Alpha9) && Teammate) orders.Requested = !orders.Requested;
        }

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint) return;
            Showing = false;
            if (!Open || !Usable) return;
            // Hidden over the broadcast, and under live timing, which has the same plan.
            if (DebugMenu.Clean || Hud != null && (Hud.Hidden || !Hud.enabled)) return;
            Showing = true;
            float k = Screen.height / 1080f;
            Style(k);

            var rows = new List<(Texture2D icon, string value, bool usable, string[] keys)>
            {
                (fuel, Planner.Full ? "FULL" : Planner.Laps == 1 ? "1 LAP" : Planner.Laps + " LAPS", true, new[] { "4", "5" }),
                (tyre, Planner.Tyres ? "NEW" : "KEEP", Supplies.TyresLeft, new[] { "6" }),
            };
            if (Log != null)
            {
                IReadOnlyList<RaceDriver> lineup = Log.Car.Lineup;
                rows.Add((helmet, Name(lineup[Log.NextDriverIndex].Id), lineup.Count > 1, new[] { "7" }));
                rows.Add((spanner, Supplies.Orders.Repairs.ToString().ToUpperInvariant(), Log.ActiveIssues.Count > 0 || Log.Damage > 0, new[] { "8" }));
            }

            bool callIn = Teammate;
            float height = Header + 8f + rows.Count * Row + (callIn ? CallGap + Row : 0f) + 14f;
            var panel = new Rect(Screen.width - (Right + Width) * k, Screen.height - (Bottom + height) * k, Width * k, height * k);
            Fill(panel, Panel, 9f * k, Vector4.one);
            Fill(new Rect(panel.x, panel.y, 72f * k, Header * k), Red, 9f * k, new Vector4(1f, 0f, 0f, 0f));
            Text(new Rect(panel.x, panel.y, 72f * k, Header * k), "PIT", tab, Color.white, TextAnchor.MiddleCenter);

            // The stop's time, right aligned, its unit smaller beside it.
            float unitWidth = unit.CalcSize(new GUIContent(" s")).x;
            var timeRect = new Rect(panel.x, panel.y, panel.width - 22f * k - unitWidth, Header * k);
            Text(timeRect, Planner.Seconds().ToString("0.0"), time, Color.white, TextAnchor.MiddleRight);
            Text(new Rect(timeRect.xMax, panel.y + 6f * k, unitWidth, Header * k), " s", unit, Color.white, TextAnchor.MiddleLeft);

            float y = panel.y + (Header + 8f) * k;
            float x = panel.x + 18f * k, right = panel.xMax - 18f * k;
            foreach (var (icon, value, usable, keys) in rows)
            {
                Color ink = usable ? Color.white : Dim;
                float icon22 = 22f * k;
                GUI.DrawTexture(new Rect(x, y + (Row * k - icon22) / 2f, icon22, icon22), icon, ScaleMode.StretchToFill, true, 0f, ink, 0f, 0f);
                Text(new Rect(x + 34f * k, y, right - x - 34f * k, Row * k), value, row, ink, TextAnchor.MiddleLeft);
                float kx = right;
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    kx -= 26f * k;
                    KeyCap(new Rect(kx, y + (Row - 26f) / 2f * k, 26f * k, 26f * k), keys[i], k);
                    kx -= 4f * k;
                }
                y += Row * k;
            }

            if (callIn)
            {
                // Pit now: filled red, or outlined once the car is called in.
                y += CallGap * k;
                var box = new Rect(panel.x + 12f * k, y, panel.width - 24f * k, Row * k);
                bool called = Supplies.Orders.Requested;
                if (called) GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Red, Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * 7f * k);
                else Fill(box, Red, 7f * k, Vector4.one);
                Text(new Rect(x, y, right - x, Row * k), called ? "BOXING THIS LAP" : "PIT NOW", row, Color.white, TextAnchor.MiddleLeft);
                KeyCap(new Rect(right - 26f * k, y + (Row - 26f) / 2f * k, 26f * k, 26f * k), "9", k);
            }
        }

        private void KeyCap(Rect r, string label, float k)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Cap, Vector4.one * Mathf.Max(1f, 2f * k), Vector4.one * 5f * k);
            Text(r, label, key, CapText, TextAnchor.MiddleCenter);
        }

        /// <summary>A driver's surname in capitals.</summary>
        private string Name(string id)
        {
            string name = Game != null ? RaceWeekend.DriverName(id, Game) : id;
            if (string.IsNullOrEmpty(name)) name = id;
            int space = name.LastIndexOf(' ');
            return (space >= 0 ? name.Substring(space + 1) : name).ToUpperInvariant();
        }

        private void Style(float k)
        {
            int size = Mathf.Max(1, Mathf.RoundToInt(24f * k));
            if (row != null && row.fontSize == size) return;
            if (fuel == null)
            {
                fuel = RaceTower.FuelIcon();
                tyre = RaceTower.TyreIcon();
                helmet = RaceTower.HelmetIcon();
                spanner = RaceTower.SpannerIcon();
            }
            Font bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
            Font semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
            tab = Make(bold, 24f * k);
            time = Make(bold, 40f * k);
            unit = Make(bold, 22f * k);
            row = Make(semiBold, 24f * k);
            key = Make(bold, 17f * k);
        }

        private static GUIStyle Make(Font font, float size) => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.RoundToInt(size)),
            clipping = TextClipping.Overflow,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
        };

        private static void Text(Rect r, string s, GUIStyle style, Color colour, TextAnchor anchor)
        {
            style.alignment = anchor;
            style.normal.textColor = colour;
            GUI.Label(r, s, style);
        }

        /// <summary>A filled rect, its corners rounded by radius where <paramref name="corners"/> is 1: top left, top right, bottom right, bottom left.</summary>
        private static void Fill(Rect r, Color colour, float radius, Vector4 corners) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, corners * radius);

        private void OnDestroy()
        {
            Showing = false;
            if (fuel != null) Destroy(fuel);
            if (tyre != null) Destroy(tyre);
            if (helmet != null) Destroy(helmet);
            if (spanner != null) Destroy(spanner);
        }
    }
}
