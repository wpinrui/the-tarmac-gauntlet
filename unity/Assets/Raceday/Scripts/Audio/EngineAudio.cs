using System.Collections.Generic;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car's engine sound: its <see cref="EngineSound"/> synthesised live by an <see cref="EngineSynth"/>.
    /// The source plays a clip of constant ones and the synth multiplies it in <see cref="OnAudioFilterRead"/>,
    /// so Unity still places the sound in 3D and fades it with distance. Only the cars nearest the listener play, as many
    /// as the quality level's <see cref="RaceVoices"/>, in the engines' group of the <see cref="Mix"/>. The nearest moving car
    /// tells the mix how close it is, so the crowd and PA dip under it.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class EngineAudio : MonoBehaviour
    {
        private const float Range = 350f;

        private static readonly List<EngineAudio> all = new List<EngineAudio>();
        private static int rankedFrame = -1;
        private static float levelRead = float.NegativeInfinity;
        private static int voices = RaceVoices.EnginesAt(1);
        private static AudioClip ones;
        private static AudioListener listener;

        public EngineSound Sound { get; private set; }

        private CarController car;
        private AudioSource source;
        private EngineSynth synth;
        private float[] mono = new float[4096];
        private int lastGear;
        private volatile bool playing;
        /// <summary>Switched off: the race is over and the car parked, so it makes no sound.</summary>
        private bool off;

        /// <summary>Switches every car's engine off, for good: the race is over and the cars are parked.</summary>
        public static void SwitchOffAll()
        {
            foreach (EngineAudio e in all) e.off = true;
        }

        public void Setup(string modelId, int seed)
        {
            car = GetComponent<CarController>();
            Sound = EngineSounds.For(modelId);
            synth = new EngineSynth(Sound, AudioSettings.outputSampleRate, seed);

            if (ones == null)
            {
                int rate = AudioSettings.outputSampleRate;
                var data = new float[rate];
                for (int i = 0; i < rate; i++) data[i] = 1f;
                ones = AudioClip.Create("Engine carrier", rate, 1, rate, false);
                ones.SetData(data, 0);
            }
            source = gameObject.AddComponent<AudioSource>();
            source.clip = ones;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;   // the carrier is constant, so Unity's doppler would do nothing
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 6f;
            source.maxDistance = Range;
            source.priority = 64;
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
            source.volume = Mix.Gain(SoundGroup.Engines);

            CarSpec spec = car.Spec;
            int gear = Mathf.Max(car.Gear, 1);
            if (car.Gear != lastGear && lastGear > 0 && car.Gear > lastGear) synth.Shift();
            lastGear = car.Gear;

            // The engine's share of the car's redline from the wheels and gear, as the physics works it out.
            // It works the same while the car glides with no physics.
            float fraction = Mathf.Abs(car.Speed) / spec.WheelRadius * spec.Gears[gear - 1] / spec.Redline;
            float throttle = car.Crashed ? 0f : car.Throttle;
            float rpm = fraction * Sound.RedlineRpm;
            // Pulling away, the clutch slips and the revs rise ahead of the wheels.
            if (gear == 1) rpm = Mathf.Max(rpm, Mathf.Lerp(Sound.IdleRpm, 0.45f * Sound.RedlineRpm, throttle));

            synth.Rpm = Mathf.Max(rpm, Sound.IdleRpm);
            synth.Load = throttle;
            synth.Speed = Mathf.Abs(car.Speed);
        }

        /// <summary>Once a frame: the nearest cars within range play, and the rest go quiet. The nearest tells the mix
        /// how close it is.</summary>
        private static void Rank()
        {
            rankedFrame = Time.frameCount;
            if (Time.unscaledTime - levelRead > 1f)
            {
                levelRead = Time.unscaledTime;
                voices = RaceVoices.EnginesAt(QualityLevels.Level);
            }
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            if (listener == null) return;
            Vector3 ear = listener.transform.position;
            all.Sort((a, b) => (a.transform.position - ear).sqrMagnitude.CompareTo((b.transform.position - ear).sqrMagnitude));
            for (int i = 0; i < all.Count; i++)
            {
                EngineAudio e = all[i];
                float sq = (e.transform.position - ear).sqrMagnitude;
                bool on = !e.off && i < voices && sq < Range * Range;
                if (i == 0 && on) Mix.Engine(Mix.Closeness(Mathf.Sqrt(sq), Mathf.Abs(e.car.Speed)));
                if (on == e.playing) continue;
                e.playing = on;
                if (on) e.source.Play();
                else e.source.Stop();
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!playing || synth == null) return;
            int frames = data.Length / channels;
            if (mono.Length < frames) mono = new float[frames];
            synth.Render(mono, frames);
            for (int i = 0, k = 0; i < frames; i++)
                for (int c = 0; c < channels; c++, k++)
                    data[k] *= mono[i];
        }
    }
}
