using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The player's overall position on each of the leader's laps, P1 at the top. Each driver's stints are shaded in
    /// their colour, stops are lines across the chart and issues are dots on the line: amber if fixed, red if not.
    /// Hovering reports the lap and position under the pointer through <see cref="Hovered"/>.
    /// </summary>
    internal sealed class PositionChart : VisualElement
    {
        private const float Left = 34, Top = 8, Bottom = 22, Right = 6;
        private static readonly Color Grid = new Color(1, 1, 1, 0.1f), Line = Color.white, Tick = new Color(1, 1, 1, 0.55f), Cursor = new Color(1, 1, 1, 0.35f);
        private static readonly Color Fixed = new Color32(0xff, 0xb0, 0x20, 0xff), Unfixed = new Color32(0xff, 0x5a, 0x4f, 0xff), Outline = new Color(0.07f, 0.07f, 0.07f);

        /// <summary>The lap and position under the pointer, or 0 and 0 when it leaves.</summary>
        public event Action<int, int> Hovered;

        private readonly RaceSummary summary;
        private readonly IReadOnlyList<int> positions;
        private readonly float lapScale;
        private readonly Func<string, Color> driverColour;
        private readonly int maxPosition;
        private readonly List<(Label Label, float Lap, int Position)> labels = new List<(Label, float, int)>();
        private int hoverLap;

        /// <param name="lapScale">Leader laps per lap of the player's, to place stints, stops and issues.</param>
        public PositionChart(RaceSummary summary, float lapScale, Func<string, Color> driverColour, Font font)
        {
            this.summary = summary;
            positions = summary.PositionByLap;
            this.lapScale = lapScale;
            this.driverColour = driverColour;
            maxPosition = Math.Max(10, positions.Count > 0 ? positions.Max() : 10);
            AddToClassList("chart-plot");

            int mid = Math.Max(2, (int)Math.Round(maxPosition / 2.0));
            foreach (int p in new[] { 1, mid, maxPosition }) AddLabel("P" + p.ToString(CultureInfo.InvariantCulture), 0, p, font);
            foreach (int h in new[] { 6, 12, 18 }) AddLabel(h.ToString(CultureInfo.InvariantCulture) + "H", positions.Count * h / 24f, 0, font);

            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
            RegisterCallback<PointerMoveEvent>(e => Hover(e.localPosition.x));
            RegisterCallback<PointerLeaveEvent>(_ => Hover(-1));
        }

        private int Laps => Math.Max(2, positions.Count);
        private float X(float lap) => Left + (lap - 1) / (Laps - 1) * (contentRect.width - Left - Right);
        private float Y(float position) => Top + (position - 1) / (maxPosition - 1) * (contentRect.height - Top - Bottom);
        private float PlayerLap(int lap) => Mathf.Clamp(lap * lapScale, 1, Laps);
        private int PositionAt(float lap) => positions.Count == 0 ? 0 : positions[Mathf.Clamp(Mathf.RoundToInt(lap) - 1, 0, positions.Count - 1)];

        private void AddLabel(string text, float lap, int position, Font font)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("chart-axis");
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
            Add(label);
            labels.Add((label, lap, position));
        }

        private void PlaceLabels()
        {
            foreach ((Label label, float lap, int position) in labels)
            {
                if (position > 0)
                {
                    // Down the left edge, right-aligned to the plot.
                    label.style.left = 0;
                    label.style.width = Left - 10;
                    label.style.top = Y(position) - 8;
                    label.style.unityTextAlign = TextAnchor.MiddleRight;
                }
                else
                {
                    label.style.left = X(lap) - 20;
                    label.style.width = 40;
                    label.style.top = contentRect.height - 16;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                }
            }
        }

        private void Hover(float x)
        {
            int lap = x < Left || positions.Count == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt((x - Left) / (contentRect.width - Left - Right) * (Laps - 1) + 1), 1, positions.Count);
            if (lap == hoverLap) return;
            hoverLap = lap;
            MarkDirtyRepaint();
            Hovered?.Invoke(lap, lap > 0 ? PositionAt(lap) : 0);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            if (positions.Count == 0 || contentRect.width <= Left + Right) return;
            Painter2D p = ctx.painter2D;
            float top = Y(1), bottom = Y(maxPosition);

            foreach (StintRecord s in summary.Stints)
            {
                Color c = driverColour(s.DriverId);
                c.a = 0.07f;
                Box(p, X(PlayerLap(s.StartLap)), top, X(PlayerLap(s.EndLap)), bottom, c);
            }

            p.lineWidth = 1;
            p.strokeColor = Grid;
            int mid = Math.Max(2, (int)Math.Round(maxPosition / 2.0));
            foreach (int pos in new[] { 1, mid, maxPosition }) Segment(p, X(1), Y(pos), X(Laps), Y(pos));

            p.lineWidth = 2;
            p.strokeColor = Tick;
            foreach (PitStopRecord s in summary.PitStops)
            {
                float x = X(PlayerLap(s.Lap));
                Segment(p, x, top, x, bottom);
            }

            p.lineWidth = 2;
            p.strokeColor = Line;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < positions.Count; i++)
            {
                var v = new Vector2(X(i + 1), Y(positions[i]));
                if (i == 0) p.MoveTo(v);
                else p.LineTo(v);
            }
            p.Stroke();

            foreach (IssueRecord issue in summary.Issues)
            {
                float lap = PlayerLap(issue.Lap);
                Dot(p, new Vector2(X(lap), Y(PositionAt(lap))), 4.5f, issue.FixedLap != null ? Fixed : Unfixed);
            }
            Dot(p, new Vector2(X(positions.Count), Y(positions[positions.Count - 1])), 5f, Line);
            foreach (PitStopRecord s in summary.PitStops) Dot(p, new Vector2(X(PlayerLap(s.Lap)), bottom), 4f, Line);

            if (hoverLap > 0)
            {
                p.lineWidth = 1;
                p.strokeColor = Cursor;
                Segment(p, X(hoverLap), top, X(hoverLap), bottom);
            }
        }

        private static void Segment(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y1));
            p.Stroke();
        }

        private static void Box(Painter2D p, float x0, float y0, float x1, float y1, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        private static void Dot(Painter2D p, Vector2 at, float radius, Color c)
        {
            p.fillColor = c;
            p.strokeColor = Outline;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }
}
