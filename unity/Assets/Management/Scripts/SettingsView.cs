using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>The settings panel, the same from the main menu, the hub's Escape menu and the race's pause menu: graphics, then audio.</summary>
    internal static class SettingsView
    {
        /// <summary>Fills the settings panel, emptying it first.</summary>
        internal static void Fill(VisualElement settingsPanel)
        {
            settingsPanel.Query<VisualElement>(className: "settings-group").ForEach(g => g.RemoveFromHierarchy());

            VisualElement graphics = SettingsGroup(0);
            IReadOnlyList<Vector2Int> sizes = GameSettings.Resolutions;
            graphics.Add(Stepper("Display mode", GameSettings.DisplayModes.Select(m => m.Name).ToArray(), GameSettings.DisplayMode, i => GameSettings.DisplayMode = i));
            graphics.Add(Stepper("Resolution", sizes.Select(r => r.x + " × " + r.y).ToArray(), sizes.ToList().IndexOf(GameSettings.Resolution), i => GameSettings.Resolution = sizes[i]));
            graphics.Add(Stepper("Quality", GameSettings.QualityNames, GameSettings.Quality, i => GameSettings.Quality = i));
            graphics.Add(Stepper("V-sync", new[] { "Off", "On" }, GameSettings.VSync ? 1 : 0, i => GameSettings.VSync = i == 1));
            int most = GameSettings.MostRealLights;
            graphics.Add(Slider("Real lights", GameSettings.RealLights / (float)most, v => GameSettings.RealLights = Mathf.RoundToInt(v * most),
                v => Mathf.RoundToInt(v * most).ToString(CultureInfo.InvariantCulture)));
            settingsPanel.Add(graphics);

            VisualElement audio = SettingsGroup(1);
            audio.Add(Slider("Volume", GameSettings.Volume, v => GameSettings.Volume = v));
            settingsPanel.Add(audio);
        }

        private static VisualElement SettingsGroup(int index)
        {
            var group = new VisualElement();
            group.AddToClassList("settings-group");
            if (index > 0) group.AddToClassList("spaced");
            return group;
        }

        private static VisualElement Setting(string label)
        {
            VisualElement row = Box("setting");
            row.Add(Text(label.ToUpperInvariant(), "setting-label"));
            return row;
        }

        private static VisualElement Stepper(string label, string[] options, int index, Action<int> set)
        {
            VisualElement row = Setting(label);
            int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
            VisualElement stepper = null;
            void Draw()
            {
                VisualElement next = MenuKit.Stepper(options.Length > 0 ? options[current].ToUpperInvariant() : "", "settings-stepper", current > 0, current < options.Length - 1, d =>
                {
                    current += d;
                    set(current);
                    Draw();
                });
                if (stepper != null) row.Remove(stepper);
                row.Add(stepper = next);
            }
            Draw();
            return row;
        }

        /// <param name="shown">The number beside the slider for a value; a percentage when not given.</param>
        private static VisualElement Slider(string label, float value01, Action<float> set, Func<float, string> shown = null)
        {
            VisualElement row = Setting(label);
            var slider = new VisualElement();
            slider.AddToClassList("slider");
            var track = new VisualElement();
            track.AddToClassList("track");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("track-line");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("track-fill");
            line.Add(fill);
            track.Add(line);
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("knob");
            track.Add(knob);
            Label number = Text("", "slider-value", true);
            slider.Add(track);
            slider.Add(number);
            row.Add(slider);

            void Show(float v)
            {
                fill.style.width = Length.Percent(v * 100);
                knob.style.left = Length.Percent(v * 100);
                number.text = shown != null ? shown(v) : Mathf.RoundToInt(v * 100).ToString(CultureInfo.InvariantCulture);
            }
            void SetFrom(Vector2 local)
            {
                float v = Mathf.Clamp01(local.x / Mathf.Max(1, track.layout.width));
                Show(v);
                set(v);
            }
            Show(value01);
            track.RegisterCallback<PointerDownEvent>(e => { track.CapturePointer(e.pointerId); SetFrom(e.localPosition); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (track.HasPointerCapture(e.pointerId)) SetFrom(e.localPosition); });
            track.RegisterCallback<PointerUpEvent>(e => track.ReleasePointer(e.pointerId));
            return row;
        }
    }
}
