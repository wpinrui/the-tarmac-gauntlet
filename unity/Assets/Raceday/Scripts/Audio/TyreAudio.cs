using System.Collections.Generic;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car's tyres on what they roll over, synthesised live by a <see cref="TyreSynth"/> from the wheels' slide, the
    /// car's speed and the surface under each wheel. It sits on its own child of the car, so its filter never touches the
    /// engine's source. Like <see cref="EngineAudio"/>, the source plays a clip of constant ones that the synth multiplies,
    /// so Unity places the sound in 3D. Only the cars nearest the listener play, as many as the quality level's <see cref="RaceVoices"/>, in the
    /// tyres' group of the <see cref="Mix"/>.
    /// </summary>
    public sealed class TyreAudio : MonoBehaviour
    {
        private const float Range = 150f;

        private static readonly List<TyreAudio> all = new List<TyreAudio>();
        private static int rankedFrame = -1;
        private static float levelRead = float.NegativeInfinity;
        private static int voices = RaceVoices.TyresAt(1);
        private static AudioClip ones;
        private static AudioListener listener;

        private CarController car;
        private AudioSource source;
        private TyreSynth synth;
        private float[] mono = new float[4096];
        /// <summary>The tyres' gain, on the samples: the source itself stays at <see cref="SynthGain.Carrier"/>.</summary>
        private readonly SynthGain gain = new SynthGain();
        private volatile bool playing;

        public static TyreAudio Fit(CarController car, int seed)
        {
            var go = new GameObject("Tyres");
            go.transform.SetParent(car.transform, false);
            TyreAudio t = go.AddComponent<TyreAudio>();
            t.car = car;
            // Grippier cars run wider, softer tyres, which squeal lower.
            t.synth = new TyreSynth(AudioSettings.outputSampleRate, (car.Spec.Grip - 1f) / 0.6f, seed);

            if (ones == null)
            {
                int rate = AudioSettings.outputSampleRate;
                var data = new float[rate];
                for (int i = 0; i < rate; i++) data[i] = 1f;
                ones = AudioClip.Create("Tyre carrier", rate, 1, rate, false);
                ones.SetData(data, 0);
            }
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = ones;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 5f;
            s.maxDistance = Range;
            s.priority = 96;   // below the engines when Unity runs short of voices
            s.volume = SynthGain.Carrier;
            t.source = s;
            return t;
        }

        private void OnEnable() => all.Add(this);
        private void OnDisable()
        {
            all.Remove(this);
            playing = false;
            if (source != null) source.Stop();
        }

        private void Update()
        {
            if (synth == null) return;
            if (rankedFrame != Time.frameCount) Rank();
            if (!playing) return;
            gain.Target = Mix.Gain(SoundGroup.Tyres);

            float slide = 0f;
            int kerb = 0, gravel = 0, grass = 0, grounded = 0;
            for (int i = 0; i < 4; i++)
            {
                slide = Mathf.Max(slide, car.Skid(i, out _, out _));
                if (!car.Ground(i, out SurfaceKind kind)) continue;
                grounded++;
                if (kind == SurfaceKind.Kerb) kerb++;
                else if (kind == SurfaceKind.Gravel) gravel++;
                else if (kind == SurfaceKind.Grass) grass++;
            }
            synth.Slide = slide;
            synth.Speed = Mathf.Abs(car.Speed);
            synth.Kerb = kerb / 4f;
            synth.Gravel = gravel / 4f;
            synth.Grass = grass / 4f;
            synth.Grounded = grounded / 4f;
            synth.Wet = RaceWeather.Now.Wet;
        }

        /// <summary>Once a frame: the nearest cars within range play, and the rest go quiet.</summary>
        private static void Rank()
        {
            rankedFrame = Time.frameCount;
            if (Time.unscaledTime - levelRead > 1f)
            {
                levelRead = Time.unscaledTime;
                voices = RaceVoices.TyresAt(QualityLevels.Level);
            }
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return;
            Vector3 ear = listener.transform.position;
            all.Sort((a, b) => (a.transform.position - ear).sqrMagnitude.CompareTo((b.transform.position - ear).sqrMagnitude));
            for (int i = 0; i < all.Count; i++)
            {
                TyreAudio t = all[i];
                bool on = i < voices && (t.transform.position - ear).sqrMagnitude < Range * Range;
                if (on == t.playing) continue;
                t.playing = on;
                if (on) t.source.Play();
                else t.source.Stop();
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!playing || synth == null) return;
            int frames = data.Length / channels;
            if (mono.Length < frames) mono = new float[frames];
            synth.Render(mono, frames);
            gain.Apply(mono, frames);
            for (int i = 0, k = 0; i < frames; i++)
                for (int c = 0; c < channels; c++, k++)
                    data[k] *= mono[i];
        }
    }
}
