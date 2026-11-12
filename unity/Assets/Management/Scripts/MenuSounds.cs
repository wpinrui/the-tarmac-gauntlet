using System;
using System.Collections.Generic;
using TarmacGauntlet.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Plays the menus' UI sounds from Resources/UiSounds. <see cref="Attach"/> gives a menu root its hover and click
    /// sounds for every pressable element (<see cref="MenuSoundRules"/>); screens ask for the rest by name with
    /// <see cref="Play"/>, such as Race or a camera glide. Cues asked for in one frame play together at the end of it,
    /// the strongest per layer. One player lives across scenes, so Go rings on through the loading screen, and it plays
    /// over the race's pause, past the listener's muffle. It plays in the UI's group of the <see cref="Mix"/>.
    /// </summary>
    public sealed class MenuSounds : MonoBehaviour
    {
        private static MenuSounds instance;

        private readonly CueQueue queue = new CueQueue();
        private readonly Dictionary<UiCue, AudioClip> clips = new Dictionary<UiCue, AudioClip>();
        private AudioSource source, slider;
        private float lastHover = -1, lastSlider = -1;
        private int sliderStep = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private static MenuSounds Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("Menu Sounds");
                DontDestroyOnLoad(go);
                return instance = go.AddComponent<MenuSounds>();
            }
        }

        /// <summary>Asks for a cue this frame.</summary>
        public static void Play(UiCue cue) => Instance.queue.Ask(cue);

        /// <summary>A slider moved to a value from 0 to 1: a tick at each step it crosses.</summary>
        public static void Slide(float value01)
        {
            MenuSounds s = Instance;
            int step = MenuSoundRules.SliderStep(value01);
            if (step == s.sliderStep) return;
            s.sliderStep = step;
            float now = Time.unscaledTime;
            if (s.lastSlider >= 0 && now - s.lastSlider < MenuSoundRules.SliderGapSeconds) return;
            s.lastSlider = now;
            if (!s.clips.TryGetValue(UiCue.Slider, out AudioClip clip)) return;
            s.slider.volume = Mix.Gain(SoundGroup.Ui);
            s.slider.pitch = MenuSoundRules.SliderPitch(value01);
            s.slider.PlayOneShot(clip);
        }

        /// <summary>Hover and click sounds for every pressable element under a menu root.</summary>
        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<PointerEnterEvent>(e => Instance.Entered(e.target as VisualElement), TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(e => Instance.Clicked(e.target as VisualElement), TrickleDown.TrickleDown);
        }

        private void Awake()
        {
            foreach (UiCue cue in Enum.GetValues(typeof(UiCue)))
            {
                var clip = Resources.Load<AudioClip>("UiSounds/" + MenuSoundRules.ClipName(cue));
                if (clip != null) clips[cue] = clip;
                else Debug.LogError("UI sounds: Resources/UiSounds/" + MenuSoundRules.ClipName(cue) + ".wav did not load, so it won't play.");
            }
            source = Source();
            slider = Source();
        }

        private AudioSource Source()
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0;
            s.ignoreListenerPause = true;
            s.bypassListenerEffects = true;
            return s;
        }

        /// <summary>Pointer enter events reach every element the pointer goes into; only a pressable one hovers.</summary>
        private void Entered(VisualElement e)
        {
            if (e == null || !MenuSoundRules.Hovers(Classes(e))) return;
            float now = Time.unscaledTime;
            if (lastHover >= 0 && now - lastHover < MenuSoundRules.HoverGapSeconds) return;
            lastHover = now;
            queue.Ask(UiCue.Hover);
        }

        /// <summary>A click lands on the deepest element; the sound is the pressable element it is part of.</summary>
        private void Clicked(VisualElement e)
        {
            for (; e != null; e = e.parent)
            {
                List<string> classes = Classes(e);
                if (!MenuSoundRules.IsPressable(classes)) continue;
                queue.Ask(MenuSoundRules.PressCue(classes));
                return;
            }
        }

        private static List<string> Classes(VisualElement e) => new List<string>(e.GetClasses());

        private void LateUpdate()
        {
            source.volume = Mix.Gain(SoundGroup.Ui);
            foreach (UiCue cue in queue.Take())
                if (clips.TryGetValue(cue, out AudioClip clip)) source.PlayOneShot(clip);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
