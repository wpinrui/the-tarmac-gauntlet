using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>The settings panel, the same from the main menu, the hub's Escape menu and the race's pause menu: graphics, race, then audio.</summary>
    internal static class SettingsView
    {
        /// <summary>
        /// Fills the settings panel, emptying it first. The groups scroll inside the panel, which is never taller than the
        /// screen. The graphics group ends with a row for each of <see cref="GraphicsOptions.Rows"/>, drawn again when the
        /// quality level sets them.
        /// </summary>
        internal static void Fill(VisualElement settingsPanel)
        {
            MenuDropdown.Close();
            settingsPanel.Query<VisualElement>(className: "settings-scroll").ForEach(g => g.RemoveFromHierarchy());
            ScrollView scroll = Scroll("settings-scroll");

            VisualElement graphics = SettingsGroup(0);
            IReadOnlyList<Vector2Int> sizes = GameSettings.Resolutions;
            graphics.Add(Stepper("Display mode", GameSettings.DisplayModes.Select(m => m.Name).ToArray(), GameSettings.DisplayMode, i => GameSettings.DisplayMode = i));
            VisualElement resolution = Setting("Resolution");
            resolution.Add(MenuDropdown.Make(sizes.Select(r => r.x + " × " + r.y).ToArray(), sizes.ToList().IndexOf(GameSettings.Resolution), "settings-dropdown",
                i => GameSettings.Resolution = sizes[i]));
            graphics.Add(resolution);
            var rows = new List<VisualElement>();
            void ShowRows()
            {
                foreach (VisualElement shown in rows) shown.RemoveFromHierarchy();
                rows.Clear();
                foreach (GraphicsRow row in GraphicsOptions.Rows) rows.Add(Row(row));
                foreach (VisualElement shown in rows) graphics.Add(shown);
            }
            graphics.Add(Stepper("Quality", GameSettings.QualityNames, GameSettings.Quality, i =>
            {
                GameSettings.Quality = i;
                ShowRows();
            }));
            graphics.Add(Stepper("V-sync", new[] { "Off", "On" }, GameSettings.VSync ? 1 : 0, i => GameSettings.VSync = i == 1));
            ShowRows();
            scroll.Add(graphics);

            VisualElement race = SettingsGroup(1);
            race.Add(Stepper("Gearbox", new[] { "Manual", "Automatic" }, GameSettings.Automatic ? 1 : 0, i => GameSettings.Automatic = i == 1));
            race.Add(Stepper("Steering help", new[] { "Off", "On" }, GameSettings.SteeringHelp ? 1 : 0, i => GameSettings.SteeringHelp = i == 1));
            race.Add(Stepper("Track map", new[] { "Off", "On" }, GameSettings.TrackMap ? 1 : 0, i => GameSettings.TrackMap = i == 1));
            scroll.Add(race);

            VisualElement audio = SettingsGroup(2);
            foreach (VolumeSlider slider in GameSettings.Sliders)
                audio.Add(Slider(SliderName(slider), GameSettings.Level(slider), v => GameSettings.SetLevel(slider, v)));
            scroll.Add(audio);
            settingsPanel.Add(scroll);
        }

        /// <summary>
        /// The race's camera settings, emptying the panel first: one car camera at a time from
        /// <see cref="CameraPresets.All"/>, starting on the one in use, with a slider for each of its values and a reset to
        /// its own place. The camera picked is <see cref="CameraPresets.Preview"/>, so the race shows it live behind the panel.
        /// </summary>
        internal static void FillCamera(VisualElement panel)
        {
            MenuDropdown.Close();
            panel.Query<VisualElement>(className: "settings-group").ForEach(g => g.RemoveFromHierarchy());
            VisualElement group = SettingsGroup(0);
            int picked = Mathf.Max(0, CameraPresets.All.ToList().IndexOf(CameraPresets.InUse));
            CameraPresets.Preview = CameraPresets.All[picked];
            var shown = new List<VisualElement>();
            void Draw()
            {
                foreach (VisualElement e in shown) e.RemoveFromHierarchy();
                shown.Clear();
                CameraPreset preset = CameraPresets.All[picked];
                for (int i = 0; i < CameraPresets.Fields.Length; i++)
                {
                    int field = i;
                    Vector2 range = preset.Ranges[field];
                    shown.Add(Slider(CameraPresets.Fields[field], Mathf.InverseLerp(range.x, range.y, preset[field]),
                        v => preset[field] = Mathf.Lerp(range.x, range.y, v),
                        v => Mathf.Lerp(range.x, range.y, v).ToString(field == 0 ? "0" : "0.0", CultureInfo.InvariantCulture), "wide"));
                }
                VisualElement reset = Box("setting");
                reset.Add(MenuKit.Fill());
                reset.Add(GhostButton("Reset", "settings-reset", () =>
                {
                    preset.Reset();
                    Draw();
                }));
                shown.Add(reset);
                foreach (VisualElement e in shown) group.Add(e);
            }
            group.Add(Stepper("Camera", CameraPresets.All.Select(c => c.Name).ToArray(), picked, i =>
            {
                picked = i;
                CameraPresets.Preview = CameraPresets.All[i];
                Draw();
            }));
            Draw();
            panel.Add(group);
        }

        private static string SliderName(VolumeSlider slider) => slider switch
        {
            VolumeSlider.Ui => "UI",
            _ => slider.ToString(),
        };

        /// <summary>An on/off row is a stepper, a count a slider.</summary>
        private static VisualElement Row(GraphicsRow row)
        {
            if (row.IsToggle) return Stepper(row.Label, new[] { "Off", "On" }, row.Value, i => row.Value = i);
            int most = row.Most;
            return Slider(row.Label, row.Value / (float)most, v => row.Value = Mathf.RoundToInt(v * most),
                v => Mathf.RoundToInt(v * most).ToString(CultureInfo.InvariantCulture));
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
            // An Off and On stepper is a switch, and sounds like one.
            bool toggle = options.Length == 2 && options[0] == "Off" && options[1] == "On";
            VisualElement stepper = null;
            void Draw()
            {
                VisualElement next = MenuKit.Stepper(options.Length > 0 ? options[current].ToUpperInvariant() : "", "settings-stepper", current > 0, current < options.Length - 1, d =>
                {
                    current += d;
                    if (toggle) MenuSounds.Play(current == 1 ? UiCue.ToggleOn : UiCue.ToggleOff);
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
        /// <param name="numberClass">An extra class for the number, such as wide for signed decimals.</param>
        private static VisualElement Slider(string label, float value01, Action<float> set, Func<float, string> shown = null, string numberClass = null)
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
            if (numberClass != null) number.AddToClassList(numberClass);
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
                MenuSounds.Slide(v);
            }
            Show(value01);
            track.RegisterCallback<PointerDownEvent>(e => { track.CapturePointer(e.pointerId); SetFrom(e.localPosition); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (track.HasPointerCapture(e.pointerId)) SetFrom(e.localPosition); });
            track.RegisterCallback<PointerUpEvent>(e => track.ReleasePointer(e.pointerId));
            return row;
        }
    }
}
