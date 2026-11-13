using TarmacGauntlet.Audio;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The race's recorded sounds (pit stops, hits and scrapes): every bank's takes from Resources/RaceSfx, played in
    /// 3D from a small pool of voices sized by the quality level (<see cref="SfxVoices"/>). A one-shot plays once
    /// (<see cref="Play"/>); a held sound loops while its owner holds it each frame and fades out once let go
    /// (<see cref="Hold"/>). When every voice is busy the quietest at the listener gives way to a louder sound, and a
    /// quieter one is dropped. All of it plays in the effects' group of the <see cref="Mix"/>. Made on first use in
    /// the race scene and gone with it.
    /// </summary>
    public sealed class RaceSfx : MonoBehaviour
    {
        /// <summary>How long a held sound takes to fade out once let go, s.</summary>
        private const float Release = 0.12f;
        /// <summary>How much a take's pitch varies, as a share, so repeats never sound the same.</summary>
        private const float Spread = 0.05f;

        private static RaceSfx instance;

        private sealed class Voice
        {
            public AudioSource Source;
            public float Volume, MinDistance, MaxDistance, Gain;
            public object Owner;
            public SfxBank Bank;
            public int Held;
        }

        private AudioClip[][] banks;
        private int[] last;
        private Voice[] shots, loops;
        private bool[] busy;
        private float[] heard;
        private int shotCount, loopCount;
        private float levelRead = float.NegativeInfinity;
        private AudioListener listener;
        private readonly System.Random random = new System.Random(5);

        /// <summary>Plays one take from a bank once at a point, at a volume and pitch (each varied a little).</summary>
        public static void Play(SfxBank bank, Vector3 at, float volume, float minDistance, float maxDistance, float pitch = 1f)
        {
            RaceSfx sfx = Get();
            if (sfx != null) sfx.Shot(bank, at, volume, minDistance, maxDistance, pitch);
        }

        /// <summary>
        /// Keeps the owner's looping sound from a bank playing at a point this frame, at a volume and pitch. Call every
        /// frame it should sound; once a frame passes without it, it fades out.
        /// </summary>
        public static void Hold(object owner, SfxBank bank, Vector3 at, float volume, float pitch, float minDistance, float maxDistance)
        {
            RaceSfx sfx = Get();
            if (sfx != null) sfx.Keep(owner, bank, at, volume, pitch, minDistance, maxDistance);
        }

        private static RaceSfx Get()
        {
            if (instance == null) instance = new GameObject("Race sounds").AddComponent<RaceSfx>();
            return instance;
        }

        private void Awake()
        {
            int n = System.Enum.GetValues(typeof(SfxBank)).Length;
            banks = new AudioClip[n][];
            last = new int[n];
            for (int b = 0; b < n; b++)
            {
                banks[b] = Resources.LoadAll<AudioClip>("RaceSfx/" + Folder((SfxBank)b));
                System.Array.Sort(banks[b], (x, y) => string.CompareOrdinal(x.name, y.name));
                last[b] = -1;
            }
            int most = Mathf.Max(SfxVoices.OneShots[SfxVoices.OneShots.Length - 1], SfxVoices.Loops[SfxVoices.Loops.Length - 1]);
            busy = new bool[most];
            heard = new float[most];
            shots = Make(SfxVoices.OneShots[SfxVoices.OneShots.Length - 1], "Sound", false, 80);
            loops = Make(SfxVoices.Loops[SfxVoices.Loops.Length - 1], "Held sound", true, 100);
            ReadLevel();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private Voice[] Make(int count, string name, bool loop, int priority)
        {
            var voices = new Voice[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"{name} {i + 1}");
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = loop;
                s.spatialBlend = 1f;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.priority = priority;
                voices[i] = new Voice { Source = s, Held = -1 };
            }
            return voices;
        }

        private void ReadLevel()
        {
            levelRead = Time.unscaledTime;
            int level = QualityLevels.Level;
            shotCount = Mathf.Min(SfxVoices.OneShotsAt(level), shots.Length);
            loopCount = Mathf.Min(SfxVoices.LoopsAt(level), loops.Length);
        }

        private Vector3 Ear()
        {
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            return listener != null ? listener.transform.position : Vector3.zero;
        }

        private void Shot(SfxBank bank, Vector3 at, float volume, float minDistance, float maxDistance, float pitch)
        {
            AudioClip[] takes = banks[(int)bank];
            if (takes.Length == 0) return;
            Vector3 ear = Ear();
            float loudness = SfxVoices.Heard(volume, Vector3.Distance(at, ear), minDistance, maxDistance);
            for (int i = 0; i < shotCount; i++)
            {
                Voice v = shots[i];
                busy[i] = v.Source.isPlaying;
                heard[i] = busy[i] ? SfxVoices.Heard(v.Volume, Vector3.Distance(v.Source.transform.position, ear), v.MinDistance, v.MaxDistance) : 0f;
            }
            int k = SfxVoices.Choose(busy, heard, shotCount, loudness);
            if (k < 0) return;
            int take = SfxVoices.Take(takes.Length, last[(int)bank], random);
            last[(int)bank] = take;
            Voice voice = shots[k];
            voice.Volume = volume;
            voice.MinDistance = minDistance;
            voice.MaxDistance = maxDistance;
            AudioSource s = voice.Source;
            s.Stop();
            s.transform.position = at;
            s.clip = takes[take];
            s.volume = Mathf.Clamp01(volume) * Mix.Gain(SoundGroup.Effects);
            s.pitch = pitch * (1f + Spread * (2f * (float)random.NextDouble() - 1f));
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.Play();
        }

        private void Keep(object owner, SfxBank bank, Vector3 at, float volume, float pitch, float minDistance, float maxDistance)
        {
            AudioClip[] takes = banks[(int)bank];
            if (takes.Length == 0) return;
            Voice voice = null;
            for (int i = 0; i < loops.Length; i++)
                if (loops[i].Owner == owner && loops[i].Bank == bank) { voice = loops[i]; break; }
            Vector3 ear = Ear();
            if (voice == null)
            {
                float loudness = SfxVoices.Heard(volume, Vector3.Distance(at, ear), minDistance, maxDistance);
                for (int i = 0; i < loopCount; i++)
                {
                    Voice v = loops[i];
                    busy[i] = v.Owner != null;
                    heard[i] = busy[i] ? SfxVoices.Heard(v.Volume, Vector3.Distance(v.Source.transform.position, ear), v.MinDistance, v.MaxDistance) : 0f;
                }
                int k = SfxVoices.Choose(busy, heard, loopCount, loudness);
                if (k < 0) return;
                int take = SfxVoices.Take(takes.Length, last[(int)bank], random);
                last[(int)bank] = take;
                voice = loops[k];
                voice.Owner = owner;
                voice.Bank = bank;
                voice.Gain = 0f;
                AudioSource s = voice.Source;
                s.Stop();
                s.clip = takes[take];
                // Each scrape starts somewhere new in its loop.
                s.time = (float)random.NextDouble() * s.clip.length * 0.9f;
                s.volume = 0f;
                s.Play();
            }
            voice.Volume = volume;
            voice.MinDistance = minDistance;
            voice.MaxDistance = maxDistance;
            voice.Held = Time.frameCount;
            voice.Source.transform.position = at;
            voice.Source.minDistance = minDistance;
            voice.Source.maxDistance = maxDistance;
            voice.Source.pitch = voice.Gain <= 0f ? pitch : Mathf.Lerp(voice.Source.pitch, pitch, Smooth());
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime - levelRead > 1f) ReadLevel();
            float step = Time.unscaledDeltaTime / Release;
            float gain = Mix.Gain(SoundGroup.Effects);
            for (int i = 0; i < loops.Length; i++)
            {
                Voice v = loops[i];
                if (v.Owner == null) continue;
                bool held = v.Held >= Time.frameCount - 1 && i < loopCount;
                v.Gain = Mathf.MoveTowards(v.Gain, held ? 1f : 0f, step);
                // Volume follows quickly but smoothly, so a scrape swells and dies with the speed rather than jumping.
                v.Source.volume = Mathf.Lerp(v.Source.volume, Mathf.Clamp01(v.Volume) * v.Gain * gain, Smooth());
                if (!held && v.Gain <= 0f)
                {
                    v.Source.Stop();
                    v.Owner = null;
                }
            }
            for (int i = 0; i < shots.Length; i++)
            {
                AudioSource s = shots[i].Source;
                if (!s.isPlaying) continue;
                if (i >= shotCount) s.Stop();
                else s.volume = Mathf.Clamp01(shots[i].Volume) * gain;   // a slider or the pause moving under a ringing hit
            }
        }

        /// <summary>The share of the way a held sound's volume and pitch move to where they are going this frame.</summary>
        private static float Smooth() => 1f - Mathf.Exp(-Time.unscaledDeltaTime * 25f);

        private static string Folder(SfxBank bank) => bank switch
        {
            SfxBank.Gun => "gun",
            SfxBank.Air => "air",
            SfxBank.JackUp => "jack-up",
            SfxBank.JackDrop => "jack-drop",
            SfxBank.FuelClunk => "fuel-clunk",
            SfxBank.FuelFlow => "fuel-flow",
            SfxBank.HitLight => "hit-light",
            SfxBank.HitHeavy => "hit-heavy",
            SfxBank.HitSoft => "hit-soft",
            _ => "scrape",
        };
    }
}
