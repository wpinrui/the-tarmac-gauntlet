using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Management
{
    /// <summary>The menus' UI sounds, each a clip of the same name in Resources/UiSounds.</summary>
    public enum UiCue { Hover, Slider, Click, Step, ToggleOn, ToggleOff, Back, DropdownOpen, DropdownClose, Confirm, Hire, Buy, Race, Go, Error, Panel, Glide }

    /// <summary>
    /// Which sound a menu element makes, and which of several asked for in one frame plays. Sounds sit on two layers:
    /// touch, for what was pressed, and motion, for what moved on screen. Each layer plays only its strongest cue each
    /// frame, so a click on Race is the Race sound, not a click and Race at once, while a screen change still glides
    /// under the click that caused it. Kept apart from Unity so the checks can run it (tools/ui-sounds-check).
    /// </summary>
    public static class MenuSoundRules
    {
        public const int Layers = 2;

        /// <summary>
        /// Elements that answer the pointer, as the classes they need together. They match the :hover rules in
        /// Menus.uss, less the setting rows, which light up but do nothing when clicked.
        /// </summary>
        private static readonly string[][] Pressable =
        {
            new[] { "item" }, new[] { "row", "click" }, new[] { "tile", "click" }, new[] { "tab" }, new[] { "chevron" }, new[] { "red" },
            new[] { "ghost-button" }, new[] { "small-button" }, new[] { "popup-item" }, new[] { "dropdown" }, new[] { "skill-step" },
            new[] { "slot" }, new[] { "drag-row" }, new[] { "chip" }, new[] { "ng-shape" }, new[] { "ng-swatch" }, new[] { "np-close" },
            new[] { "member" }, new[] { "head", "sort" }, new[] { "stat-head" },
        };

        /// <summary>Classes that mark an element as there but not usable now, such as a purchase you can't afford.</summary>
        private static readonly string[] Unusable = { "disabled", "dim" };

        public static string ClipName(UiCue cue) => cue switch
        {
            UiCue.ToggleOn => "toggle-on",
            UiCue.ToggleOff => "toggle-off",
            UiCue.DropdownOpen => "dropdown-open",
            UiCue.DropdownClose => "dropdown-close",
            _ => cue.ToString().ToLowerInvariant(),
        };

        public static int Layer(UiCue cue) => cue == UiCue.Panel || cue == UiCue.Glide ? 1 : 0;

        /// <summary>The stronger cue wins its layer. Within a layer the enum runs weakest to strongest.</summary>
        public static int Rank(UiCue cue) => (int)cue;

        public static bool IsPressable(ICollection<string> classes) => Pressable.Any(need => need.All(classes.Contains));

        /// <summary>A hover sounds only on something that can be used now, and an empty lineup slot can't.</summary>
        public static bool Hovers(ICollection<string> classes) =>
            IsPressable(classes) && !Unusable.Any(classes.Contains) && !(classes.Contains("slot") && classes.Contains("empty"));

        /// <summary>A click on something that can't be used now is the error; anything else pressable clicks.</summary>
        public static UiCue PressCue(ICollection<string> classes) => Unusable.Any(classes.Contains) ? UiCue.Error : UiCue.Click;

        /// <summary>
        /// A slider ticks each time it crosses one of <see cref="SliderSteps"/> steps, no faster than
        /// <see cref="SliderGapSeconds"/>, and rises in pitch as it goes up.
        /// </summary>
        public const int SliderSteps = 20;
        public const float SliderGapSeconds = 0.035f, HoverGapSeconds = 0.04f;
        public static int SliderStep(float value01) => (int)System.Math.Round(System.Math.Clamp(value01, 0f, 1f) * SliderSteps);
        public static float SliderPitch(float value01) => 0.8f + 0.5f * System.Math.Clamp(value01, 0f, 1f);
    }

    /// <summary>The cues asked for this frame, the strongest per layer.</summary>
    public sealed class CueQueue
    {
        private readonly UiCue?[] wanted = new UiCue?[MenuSoundRules.Layers];

        public void Ask(UiCue cue)
        {
            int layer = MenuSoundRules.Layer(cue);
            if (wanted[layer] is UiCue held && MenuSoundRules.Rank(held) >= MenuSoundRules.Rank(cue)) return;
            wanted[layer] = cue;
        }

        /// <summary>The cues to play now, touch then motion, and an empty queue for the next frame.</summary>
        public List<UiCue> Take()
        {
            var due = new List<UiCue>();
            for (int i = 0; i < wanted.Length; i++)
            {
                if (wanted[i] is UiCue cue) due.Add(cue);
                wanted[i] = null;
            }
            return due;
        }
    }
}
