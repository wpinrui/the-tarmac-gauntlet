using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The panel the Race button opens: the player's drivers in driving order, the starter on top, and Go. Dragging a
    /// driver reorders the lineup in the career (<see cref="GameActions.SetLineup"/>). Go starts the race with
    /// <see cref="RaceWeekend.Begin"/> and loads the circuit.
    /// </summary>
    public sealed class LineupPanel : MonoBehaviour
    {
        /// <summary>The scene the race runs in.</summary>
        public const string RacedayScene = "Raceday";

        private Menus menus;
        private VisualElement body, rows, go;

        // A drag in progress: the row, where it started, where it would drop, and the pointer's start.
        private VisualElement dragged;
        private int from, to;
        private float startY, step;

        private void Awake()
        {
            menus = GetComponent<Menus>();
            menus.ScreenOpened += Opened;
            GameSession.Changed += OnChanged;
        }

        private void OnDestroy()
        {
            menus.ScreenOpened -= Opened;
            GameSession.Changed -= OnChanged;
        }

        private void Opened(string slug, VisualElement panelBody)
        {
            body = slug == Menus.LineupSlug ? panelBody : null;
            if (body == null) return;
            dragged = null;

            var head = new VisualElement();
            head.AddToClassList("lu-head");
            head.Add(menus.Text("DRIVING ORDER", "lu-label"));
            head.Add(menus.Text("OVR", "lu-label"));
            body.Add(head);
            rows = new VisualElement();
            rows.AddToClassList("lu-rows");
            body.Add(rows);
            go = menus.RedButton("Go", "lu-go", Go);
            body.Add(go);
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (body != null && body.panel != null && dragged == null) Render();
        }

        private void Render()
        {
            GameState game = GameSession.State;
            rows.Clear();
            if (game == null) return;
            PlayerTeam player = game.Teams.OfType<PlayerTeam>().First();
            foreach (string id in DriverLineup.Of(game))
            {
                (string name, int ovr) = Describe(game, player, id);
                var row = new VisualElement { userData = id };
                row.AddToClassList("lu-row");
                var grip = new VisualElement { pickingMode = PickingMode.Ignore };
                grip.AddToClassList("lu-grip");
                grip.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/grip");
                row.Add(grip);
                Label n = menus.Text(name, "lu-name", true);
                n.pickingMode = PickingMode.Ignore;
                row.Add(n);
                Label o = menus.Text(ovr.ToString(CultureInfo.InvariantCulture), "lu-ovr", true);
                o.pickingMode = PickingMode.Ignore;
                o.AddToClassList(ovr >= 60 ? "v-hi" : ovr >= 50 ? "v-mid" : "v-lo");
                row.Add(o);
                row.RegisterCallback<PointerDownEvent>(e => StartDrag(row, e));
                row.RegisterCallback<PointerMoveEvent>(e => MoveDrag(row, e));
                row.RegisterCallback<PointerUpEvent>(e => EndDrag(row, e));
                row.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag(row, null));
                rows.Add(row);
            }
            go.EnableInClassList("disabled", player.EnteredCarId == null);
        }

        private static (string Name, int Ovr) Describe(GameState game, PlayerTeam player, string id)
        {
            if (id == RaceEntry.PlayerCharacterDriverId) return (player.PlayerName, player.Skills.Driver * 5);
            Driver d = game.Drivers.FirstOrDefault(x => x.Id == id);
            if (d == null) return (id, 0);
            return (d.Name, (int)JsMath.Round(DriverLifecycle.TotalDriverStats(DriverLifecycle.CalculateDriverStats(d)) / 5));
        }

        // ------------------------------------------------------------------ dragging

        private void StartDrag(VisualElement row, PointerDownEvent e)
        {
            if (dragged != null || rows.childCount < 2) return;
            dragged = row;
            from = to = rows.IndexOf(row);
            startY = e.position.y;
            step = rows[1].layout.y - rows[0].layout.y;
            row.AddToClassList("drag");
            row.CapturePointer(e.pointerId);
        }

        private void MoveDrag(VisualElement row, PointerMoveEvent e)
        {
            if (row != dragged || step <= 0) return;
            int n = rows.childCount;
            float dy = Mathf.Clamp(e.position.y - startY, -from * step, (n - 1 - from) * step);
            row.style.translate = new Translate(0, dy);
            to = Mathf.Clamp(from + Mathf.RoundToInt(dy / step), 0, n - 1);
            for (int i = 0; i < n; i++)
            {
                if (rows[i] == row) continue;
                float shift = from < to && i > from && i <= to ? -step : from > to && i < from && i >= to ? step : 0;
                rows[i].style.translate = new Translate(0, shift);
            }
        }

        private void EndDrag(VisualElement row, PointerUpEvent e)
        {
            if (row != dragged) return;
            if (e != null && row.HasPointerCapture(e.pointerId)) row.ReleasePointer(e.pointerId);
            dragged = null;
            row.RemoveFromClassList("drag");
            if (to != from && GameSession.State != null)
            {
                List<string> order = DriverLineup.Of(GameSession.State).ToList();
                string id = order[from];
                order.RemoveAt(from);
                order.Insert(to, id);
                GameSession.Apply(g => GameActions.SetLineup(g, order));
            }
            Render();
        }

        // ------------------------------------------------------------------ racing

        private static void Go()
        {
            GameState game = GameSession.State;
            if (game == null || game.Teams.OfType<PlayerTeam>().First().EnteredCarId == null) return;
            RaceWeekend.Begin(game);
            GameSession.Flush();
            SceneManager.LoadScene(RacedayScene);
        }
    }
}
