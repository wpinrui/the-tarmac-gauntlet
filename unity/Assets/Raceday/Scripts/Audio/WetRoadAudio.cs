using System.Collections.Generic;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car's tyres hissing on a wet road, synthesised live by a <see cref="WetRoadSynth"/> from the car's speed and the
    /// water on the track. Its own layer on its own child of the car, beside <see cref="TyreAudio"/>. Like the engine, the
    /// source plays a clip of constant ones that the synth multiplies, so Unity places it in 3D. Only on a wet track, and
    /// only the cars nearest the listener, as many as the quality level's <see cref="AmbienceBudget.WetCars"/>.
    /// </summary>
    public sealed class WetRoadAudio : MonoBehaviour
    {
        private const float Range = 120f;
        /// <summary>The hiss's level in the tyres' group: from the loudness pass, its loudest moments 8 dB under an engine's.</summary>
        private const float Level = 0.3f;

        private static readonly List<WetRoadAudio> all = new List<WetRoadAudio>();
        private static int rankedFrame = -1;
        private static float levelRead = float.NegativeInfinity;
        private static int voices = AmbienceMix.Budget(1).WetCars;
        private static AudioClip ones;
        private static AudioListener listener;

        private CarController car;
        private AudioSource source;
        private WetRoadSynth synth;
        private float[] mono = new float[4096];
        /// <summary>The hiss's gain, on the samples: the source itself stays at <see cref="SynthGain.Carrier"/>.</summary>
        private readonly SynthGain gain = new SynthGain();
        private volatile bool playing;

        public static WetRoadAudio Fit(CarController car, int seed)
        {
            var go = new GameObject("Wet road");
            go.transform.SetParent(car.transform, false);
            WetRoadAudio w = go.AddComponent<WetRoadAudio>();
            w.car = car;
            w.synth = new WetRoadSynth(AudioSettings.outputSampleRate, seed);

            if (ones == null)
            {
                int rate = AudioSettings.outputSampleRate;
                var data = new float[rate];
                for (int i = 0; i < rate; i++) data[i] = 1f;
                ones = AudioClip.Create("Wet road carrier", rate, 1, rate, false);
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
            s.priority = 100;   // below the tyres when Unity runs short of voices
            s.volume = SynthGain.Carrier;
            w.source = s;
            return w;
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
            gain.Target = Level * Mix.Gain(SoundGroup.Tyres);
            int grounded = 0;
            for (int i = 0; i < 4; i++) if (car.Ground(i, out _)) grounded++;
            WeatherState weather = RaceWeather.Now;
            synth.Speed = Mathf.Abs(car.Speed);
            synth.Wet = weather.Wet;
            synth.Water = weather.Water;
            synth.Grounded = grounded / 4f;
        }

        /// <summary>Once a frame: on a wet track the nearest cars within range play, and the rest go quiet.</summary>
        private static void Rank()
        {
            rankedFrame = Time.frameCount;
            if (Time.unscaledTime - levelRead > 1f)
            {
                levelRead = Time.unscaledTime;
                voices = AmbienceMix.Budget(QualityLevels.Level).WetCars;
            }
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return;
            bool wet = RaceWeather.Now.Wet > 0.01f;
            Vector3 ear = listener.transform.position;
            if (wet) all.Sort((a, b) => (a.transform.position - ear).sqrMagnitude.CompareTo((b.transform.position - ear).sqrMagnitude));
            for (int i = 0; i < all.Count; i++)
            {
                WetRoadAudio w = all[i];
                bool on = wet && i < voices && (w.transform.position - ear).sqrMagnitude < Range * Range;
                if (on == w.playing) continue;
                w.playing = on;
                if (on) w.source.Play();
                else w.source.Stop();
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
