using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Raceday screen drawn with IMGUI on a 1920x1080 canvas scaled to the window.</summary>
    internal sealed class RaceHud
    {
        private const float W = 1920f, H = 1080f;
        private const int TopRows = 10;
        private const int NearbyRows = 9;

        private static readonly Color Panel = new Color(0.06f, 0.07f, 0.09f, 0.82f);
        private static readonly Color Highlight = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color BarBack = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color Good = new Color(0.35f, 0.80f, 0.45f);
        private static readonly Color Warn = new Color(0.95f, 0.70f, 0.20f);
        private static readonly Color Bad = new Color(0.90f, 0.30f, 0.25f);

        private readonly RaceDirector director;
        private readonly Dictionary<string, IssueTemplate> issues =
            IssueTemplates.AllIssueTemplates.ToDictionary(t => t.Id);
        private Texture2D white;
        private GUIStyle text, small, big, button, selected;
        private Vector2 resultsScroll;

        public RaceHud(RaceDirector director) => this.director = director;

        public void Draw()
        {
            Styles();
            float scale = Screen.height / H;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;

            if (director.IsPickingCar)
            {
                PickCar(width);
                return;
            }
            if (director.Paused)
            {
                Fill(new Rect(0, 0, width, H), Color.black);
                return;
            }

            Clock();
            Standings();
            if (director.IsFinished) Results(width);
            else PlayerPanel(width);
        }

        // --- Car pick (play-test entry point) ---

        private void PickCar(float width)
        {
            CarClass[] classes = { CarClass.F, CarClass.E, CarClass.D, CarClass.C, CarClass.B, CarClass.A, CarClass.F1 };
            float colW = 250f, rowH = 44f;
            float x0 = (width - classes.Length * colW) / 2;
            float y0 = 260f;
            for (int c = 0; c < classes.Length; c++)
            {
                float x = x0 + c * colW;
                Fill(new Rect(x + 6, y0 - 10, colW - 12, 6), CarFactory.ClassColour(classes[c]));
                int r = 0;
                foreach (CarModel m in CarModels.All.Where(m => m.CarClass == classes[c]))
                {
                    if (GUI.Button(new Rect(x + 6, y0 + r * (rowH + 6), colW - 12, rowH), m.Name, button)) director.StartRace(m.Id);
                    r++;
                }
            }
        }

        // --- Always on during the race ---

        private void Clock()
        {
            double left = Math.Max(0, RaceClock.TotalRaceSeconds - director.SimTime);
            int total = (int)Math.Ceiling(left);
            Fill(new Rect(20, 20, 200, 64), Panel);
            GUI.Label(new Rect(20, 20, 200, 64), $"{total / 60:00}:{total % 60:00}", big);
        }

        private void Standings()
        {
            IReadOnlyList<string> order = director.Order;
            if (order.Count == 0) return;

            int focus = Math.Max(0, IndexOf(order, director.TargetCarId));
            var rows = new List<int>();
            for (int i = 0; i < Math.Min(TopRows, order.Count); i++) rows.Add(i);
            int from = Math.Max(TopRows, Math.Min(focus - NearbyRows / 2, order.Count - NearbyRows));
            for (int i = from; i < Math.Min(order.Count, from + NearbyRows); i++) rows.Add(i);

            float x = 20, y = 100, w = 380, rowH = 30;
            Fill(new Rect(x, y, w, rows.Count * rowH + 16 + (from > TopRows ? 10 : 0)), Panel);
            y += 8;
            int last = -1;
            foreach (int i in rows)
            {
                if (last >= 0 && i != last + 1) y += 10;
                last = i;
                string carId = order[i];
                Rect row = new Rect(x, y, w, rowH);
                if (carId == director.TargetCarId) Fill(row, Highlight);
                CarEntry entry = director.Live.Entries.First(e => e.CarId == carId);
                bool retired = director.Playback.RetiredBy(carId, director.SimTime);

                GUI.Label(new Rect(x + 10, y, 44, rowH), (i + 1).ToString(), text);
                Fill(new Rect(x + 56, y + 8, 8, rowH - 16), CarFactory.ClassColour(entry.Model.CarClass));
                GUI.Label(new Rect(x + 74, y, w - 84, rowH), TeamName(entry.TeamId), retired ? small : text);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) director.Follow(carId);
                y += rowH;
            }
        }

        // --- Player's car: instructions, gauges, pit call ---

        private void PlayerPanel(float width)
        {
            LiveRace live = director.Live;
            PlayerGauges g = live.GaugesAt(director.SimTime);
            float w = 1100, h = 236;
            float x = (width - w) / 2, y = H - h - 20;
            Fill(new Rect(x, y, w, h), Panel);

            // Header: position, lap, driver.
            int pos = IndexOf(director.Order, live.PlayerCarId) + 1;
            GUI.Label(new Rect(x + 20, y + 10, 120, 44), $"P{pos}", big);
            GUI.Label(new Rect(x + 140, y + 18, 140, 30), $"Lap {g.LapsCompleted + 1}", text);
            GUI.Label(new Rect(x + 280, y + 18, 320, 30), DriverName(g.DriverId), text);

            // Instruction mode.
            InstructionMode[] modes = { InstructionMode.Push, InstructionMode.Normal, InstructionMode.Conserve };
            for (int i = 0; i < modes.Length; i++)
            {
                Rect r = new Rect(x + w - 3 * 130 - 20 + i * 130, y + 12, 124, 40);
                if (GUI.Button(r, modes[i].ToString(), live.Mode == modes[i] ? selected : button)) director.SetMode(modes[i]);
            }

            // Gauges.
            int tyreLaps = Advisory.TyreLapsLeft(live.PlayerEntry, g.Basis with { TyreWear = g.TyreWear }, live.Mode);
            int fuelLaps = Advisory.FuelLapsLeft(live.PlayerEntry, g.Basis with { FuelRemaining = g.Fuel }, live.Mode);
            float gy = y + 70, gw = 250;
            Gauge(new Rect(x + 20, gy, gw, 50), "Tyres", 1 - g.TyreWear / 100, $"{tyreLaps} laps");
            Gauge(new Rect(x + 290, gy, gw, 50), "Fuel", g.FuelCapacity > 0 ? g.Fuel / g.FuelCapacity : 0, $"{fuelLaps} laps");
            Gauge(new Rect(x + 560, gy, gw, 50), "Driver", 1 - g.Fatigue / 100, null);
            Gauge(new Rect(x + 830, gy, gw, 50), "Car", g.Condition / 100, null);

            // Pit call and its plan.
            float py = y + 140;
            bool called = live.PitPlan != null;
            if (GUI.Button(new Rect(x + 20, py, 140, 76), "Pit", called ? selected : button)) director.TogglePit();

            PitPlan plan = director.DraftPit;
            float bx = x + 180;
            if (GUI.Button(new Rect(bx, py, 120, 34), "Fuel", plan.Refuel ? selected : button))
                director.EditPit(plan with { Refuel = !plan.Refuel });
            bool tyresLeft = g.TyreSets > 0;
            if (GUI.Button(new Rect(bx + 130, py, 150, 34), $"Tyres {g.TyreSets}", plan.ChangeTyres && tyresLeft ? selected : button) && tyresLeft)
                director.EditPit(plan with { ChangeTyres = !plan.ChangeTyres });
            if (GUI.Button(new Rect(bx + 290, py, 150, 34), $"Fix {g.SpareParts}", plan.FixIssues ? selected : button))
                director.EditPit(plan with { FixIssues = !plan.FixIssues });

            // Next driver: the one at the wheel stays when nobody else is picked.
            float dx = bx;
            foreach (RaceDriver d in live.PlayerEntry.Drivers)
            {
                bool next = (plan.NextDriverId ?? g.DriverId) == d.Id;
                if (GUI.Button(new Rect(dx, py + 42, 146, 34), DriverName(d.Id), next ? selected : button))
                    director.EditPit(plan with { NextDriverId = d.Id == g.DriverId ? null : d.Id });
                dx += 152;
            }

            // Known issues.
            float ix = x + 820, iy = py;
            foreach (ActiveIssue issue in g.Issues.Take(4))
            {
                string label = issues.TryGetValue(issue.TemplateId, out IssueTemplate t) ? t.Description : issue.TemplateId;
                Fill(new Rect(ix, iy + 6, 6, 18), SeverityColour(t?.Severity));
                GUI.Label(new Rect(ix + 14, iy, 260, 30), label, small);
                iy += 22;
            }
        }

        private void Gauge(Rect r, string label, double value, string extra)
        {
            float v = Mathf.Clamp01((float)value);
            GUI.Label(new Rect(r.x, r.y, r.width, 24), label, small);
            if (extra != null) GUI.Label(new Rect(r.x, r.y, r.width, 24), extra, Right(small));
            Fill(new Rect(r.x, r.y + 28, r.width, 14), BarBack);
            Fill(new Rect(r.x, r.y + 28, r.width * v, 14), v > 0.5f ? Good : v > 0.2f ? Warn : Bad);
        }

        // --- End of race ---

        private void Results(float width)
        {
            IReadOnlyList<CarRaceResult> results = director.Live.Result.Results;
            float w = 640, h = 760;
            float x = (width - w) / 2, y = (H - h) / 2;
            Fill(new Rect(x, y, w, h), Panel);

            float rowH = 30;
            Rect view = new Rect(x + 10, y + 10, w - 20, h - 90);
            resultsScroll = GUI.BeginScrollView(view, resultsScroll, new Rect(0, 0, w - 40, results.Count * rowH));
            for (int i = 0; i < results.Count; i++)
            {
                CarRaceResult r = results[i];
                CarEntry entry = director.Live.Entries.First(e => e.CarId == r.CarId);
                Rect row = new Rect(0, i * rowH, w - 40, rowH);
                if (r.CarId == director.Live.PlayerCarId) Fill(row, Highlight);
                GUI.Label(new Rect(10, i * rowH, 50, rowH), r.FinalPosition.ToString(), text);
                Fill(new Rect(60, i * rowH + 8, 8, rowH - 16), CarFactory.ClassColour(entry.Model.CarClass));
                GUI.Label(new Rect(78, i * rowH, 360, rowH), TeamName(entry.TeamId), r.Retired ? small : text);
                GUI.Label(new Rect(w - 160, i * rowH, 100, rowH), r.Retired ? "DNF" : r.LapsCompleted.ToString(), Right(text));
            }
            GUI.EndScrollView();

            if (GUI.Button(new Rect(x + w / 2 - 120, y + h - 64, 240, 48), "Race again", button)) director.BackToGarage();
        }

        // --- Helpers ---

        private string TeamName(string teamId) =>
            director.Game.Teams.FirstOrDefault(t => t.Id == teamId)?.Name ?? teamId;

        private string DriverName(string driverId)
        {
            if (driverId == RaceEntry.PlayerCharacterDriverId) return "You";
            return director.Game.Drivers.FirstOrDefault(d => d.Id == driverId)?.Name ?? driverId;
        }

        private static int IndexOf(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
            return -1;
        }

        private static Color SeverityColour(IssueSeverity? s) => s switch
        {
            IssueSeverity.Major => Bad,
            IssueSeverity.Medium => Warn,
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        private void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        private static GUIStyle Right(GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.MiddleRight };

        private void Styles()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            text = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            text.normal.textColor = Color.white;
            small = new GUIStyle(text) { fontSize = 16 };
            small.normal.textColor = new Color(1, 1, 1, 0.7f);
            big = new GUIStyle(text) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            selected = new GUIStyle(button);
            selected.normal.textColor = Color.black;
            selected.hover.textColor = Color.black;
            selected.normal.background = MakeTex(new Color(0.95f, 0.95f, 0.95f));
            selected.hover.background = selected.normal.background;
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
