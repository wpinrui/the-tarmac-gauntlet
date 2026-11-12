using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The sound of the place round the race: each grandstand's crowd, the PA, the rain and thunder, the countryside and
    /// the wind. The crowd and PA are placed in the world: each stand plays from the point of its front nearest the
    /// listener, so it is loud beside a stand and fades away from it, and only the nearest stands play. A stand's crowd
    /// murmurs, and cheers up as cars pass it; the start, the finish and an overtake in front of it bring a roar. The PA
    /// talks now and then from the main grandstand, wordless and echoing. Rain, thunder and countryside play round the
    /// listener: the rain from the weather, thunder in its storms, birds by day and crickets by night away from the
    /// stands. The wind is synthesised from the camera's own speed (<see cref="WindSynth"/>), so it rushes on the bonnet
    /// and barely stirs on a TV camera. The mix rules are <see cref="AmbienceMix"/>; the voices each quality level may
    /// play are its <see cref="AmbienceBudget"/>. The clips are public-domain field recordings in Resources/Ambience,
    /// built by tools/ambience/build.py.
    /// </summary>
    public sealed class RaceAmbience : MonoBehaviour
    {
        [NonSerialized] public Circuit Circuit;
        [NonSerialized] public Grandstands Stands;
        [NonSerialized] public RaceCar[] Cars;
        [NonSerialized] public RaceTower Tower;
        [NonSerialized] public RaceDirector Director;
        [NonSerialized] public RaceCameras Cameras;
        [NonSerialized] public BroadcastDirector Broadcast;
        [NonSerialized] public TimeOfDay Sky;
        /// <summary>Whether the race's weather is a thundery spell (<see cref="WeatherKind.Thundery"/>).</summary>
        [NonSerialized] public bool Thundery;

        /// <summary>How far in front of the track's edge a stand's crowd sits, m, and how high, m.</summary>
        private const float StandSetback = 16f, StandHeight = 5f;
        /// <summary>A car this far either side of a stand's ends counts as passing it, m.</summary>
        private const float PassMargin = 25f;
        /// <summary>Two cars this close on the lap are racing each other: a swap between them is an overtake, m.</summary>
        private const float PassGap = 40f;
        /// <summary>Real seconds between looking for overtakes, and the least between two roars at a stand.</summary>
        private const float PassEvery = 0.25f, RoarRest = 12f;
        /// <summary>How long the start and the finish keep the crowd on its feet, real seconds.</summary>
        private const float BigMoment = 14f;
        /// <summary>Real seconds a source takes to fade in or out as its level changes.</summary>
        private const float Fade = 0.6f;
        /// <summary>Camera speed past which a move is a cut, not motion, m/s.</summary>
        private const float CutSpeed = 160f;

        private sealed class Stand
        {
            public bool Main;
            public float From, Length;
            public Vector3[] Front;
            public Vector3 Nearest;
            public float Distance;
            public AudioSource Calm, Lively;
            public float Excitement, Held, LastRoar = -100f;
            public readonly Dictionary<int, float> Was = new Dictionary<int, float>();
        }

        private readonly List<Stand> stands = new List<Stand>();
        private readonly List<Stand> byDistance = new List<Stand>();
        private AudioClip[] roars, announcements, thunders;
        private AudioSource[] roarVoices;
        private AudioSource pa, rainLight, rainHeavy, thunder, countryDay, countryNight, windSource;
        private AudioLowPassFilter thunderTone;
        private WindVoice wind;
        private AmbienceBudget budget;
        private float levelRead = float.NegativeInfinity, passLooked, nextAnnouncement, nextThunder = -1f;
        private int lastAnnouncement = -1;
        private bool wasRacing, wasFinished, primed, started;
        private Vector3 lastEar;
        private bool haveEar;
        private float cameraSpeed;
        private readonly Dictionary<int, float> now = new Dictionary<int, float>();
        private readonly List<int> inSpan = new List<int>();

        private void Start()
        {
            budget = AmbienceMix.Budget(QualityLevels.Level);
            AudioClip Clip(string name) => Resources.Load<AudioClip>("Ambience/" + name);
            AudioClip[] Clips(string name, int count)
            {
                var clips = new AudioClip[count];
                for (int i = 0; i < count; i++) clips[i] = Clip($"{name}-{i + 1}");
                return clips;
            }
            roars = Clips("crowd-roar", 3);
            announcements = Clips("pa", 3);
            thunders = Clips("thunder", 6);

            AudioClip calm = Clip("crowd-calm"), lively = Clip("crowd-lively");
            if (Stands != null && Circuit != null)
            {
                foreach (StandSite site in Grandstands.Sites)
                {
                    Stand s = Build(site);
                    s.Calm = Voice("Crowd " + site.Name, calm, true, 40);
                    s.Lively = Voice("Cheering " + site.Name, lively, true, 40);
                    stands.Add(s);
                }
            }
            roarVoices = new AudioSource[Math.Max(1, budget.Roars)];
            for (int i = 0; i < roarVoices.Length; i++) roarVoices[i] = Voice("Roar", null, true, 44, loop: false);
            pa = Voice("PA", null, true, 48, loop: false);
            pa.minDistance = 35f;
            pa.maxDistance = 900f;
            pa.spread = 160f;
            rainLight = Voice("Light rain", Clip("rain-light"), false, 40);
            rainHeavy = Voice("Heavy rain", Clip("rain-heavy"), false, 40);
            thunder = Voice("Thunder", null, false, 44, loop: false);
            thunderTone = thunder.gameObject.AddComponent<AudioLowPassFilter>();
            countryDay = Voice("Birds", Clip("country-day"), false, 56);
            countryNight = Voice("Crickets", Clip("country-night"), false, 56);
            windSource = Voice("Wind", WindVoice.Carrier(), false, 40);
            wind = windSource.gameObject.AddComponent<WindVoice>();
            nextAnnouncement = Time.time + UnityEngine.Random.Range(8f, 25f);
            started = true;
        }

        /// <summary>A looped or one-shot source on its own child, so each can carry its own filter.</summary>
        private AudioSource Voice(string name, AudioClip clip, bool placed, int priority, bool loop = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.volume = 0f;
            s.priority = priority;
            s.dopplerLevel = 0f;
            s.spatialBlend = placed ? 1f : 0f;
            if (placed)
            {
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 30f;
                s.maxDistance = 700f;
                s.spread = 120f;   // a stand is wide: its crowd comes from a broad arc, not a point
            }
            return s;
        }

        /// <summary>A stand's front, a point every few metres along it, set back from the track's edge on its side.</summary>
        private Stand Build(StandSite site)
        {
            (float from, float to) = Stands.Span(site);
            float length = Circuit.Ahead(from, to);
            float side = Stands.SideOf(site) == 0 ? 1f : -1f;
            int count = Mathf.Max(2, Mathf.CeilToInt(length / 5f) + 1);
            var front = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int k = Circuit.IndexOf(from + length * i / (count - 1));
                front[i] = Circuit.Centre[k] + Circuit.Left[k] * (side * (Circuit.HalfWidth(k) + StandSetback)) + Vector3.up * StandHeight;
            }
            return new Stand { Main = site.Main, From = from, Length = length, Front = front, Nearest = front[0] };
        }

        private void Update()
        {
            if (!started) return;
            if (Time.unscaledTime - levelRead > 1f)
            {
                levelRead = Time.unscaledTime;
                budget = AmbienceMix.Budget(QualityLevels.Level);
            }
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 ear = cam.transform.position;
            float dt = Time.deltaTime;
            if (haveEar && dt > 0f)
            {
                float v = (ear - lastEar).magnitude / dt;
                if (v < CutSpeed) cameraSpeed = v;   // a cut leaves the last speed, then the next frame reads true
            }
            else if (dt <= 0f) cameraSpeed = 0f;
            lastEar = ear;
            haveEar = true;

            WeatherState weather = RaceWeather.Now;
            float night = Sky != null ? Sky.Lights : 0f;
            float nearest = Crowds(ear, weather.Rain, night);
            Announce(night);
            Rain(weather.Rain);
            Thunder(weather.Rain);
            Country(nearest, night, weather.Rain);
            Wind(weather.Wind);
        }

        // ---------------------------------------------------------------- crowd

        /// <summary>Plays the nearest stands' crowds at their excitement. Returns how far the nearest stand is, m.</summary>
        private float Crowds(Vector3 ear, float rain, float night)
        {
            if (stands.Count == 0) return float.PositiveInfinity;
            bool racing = Tower == null || Tower.Racing == null || Tower.Racing();
            bool finished = Director != null && Director.Finished;
            if (!primed)
            {
                // A race resumed from a save, or already finished, doesn't roar as the scene opens.
                primed = true;
                wasRacing = racing && Tower != null && Tower.RaceTime > 5f;
                wasFinished = finished;
            }
            bool big = (racing && !wasRacing) || (finished && !wasFinished);
            wasRacing = racing;
            wasFinished = finished;

            bool look = Time.time - passLooked >= PassEvery;
            if (look) passLooked = Time.time;
            byDistance.Clear();
            foreach (Stand s in stands)
            {
                s.Nearest = NearestOn(s.Front, ear, out s.Distance);
                float target = AmbienceMix.Passing(Passing(s, look));
                if (big) s.Held = Time.time + BigMoment;
                if (Time.time < s.Held) target = 1f;
                float rate = target > s.Excitement ? 1.6f : 0.35f;   // up in a breath, down over a few seconds
                s.Excitement = Mathf.MoveTowards(s.Excitement, target, rate * Time.deltaTime);
                byDistance.Add(s);
            }
            byDistance.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (big)
            {
                // The stand nearest the listener roars, and the main grandstand too while a second roar voice is free.
                Roar(byDistance[0], force: true);
                Stand main = stands.Find(x => x.Main);
                if (main != null && main != byDistance[0] && budget.Roars > 1) Roar(main, force: true);
            }
            for (int i = 0; i < byDistance.Count; i++)
            {
                Stand s = byDistance[i];
                bool heard = i < budget.Stands;
                (float calm, float lively) = AmbienceMix.Crowd(s.Excitement, night, rain);
                s.Calm.transform.position = s.Nearest;
                s.Lively.transform.position = s.Nearest;
                Level(s.Calm, heard ? calm : 0f);
                Level(s.Lively, heard ? lively * 0.9f : 0f);
            }
            return byDistance[0].Distance;
        }

        /// <summary>How many cars are passing a stand now. Every so often it also looks for an overtake between two of
        /// them, which brings a roar.</summary>
        private int Passing(Stand s, bool look)
        {
            if (Cars == null) return 0;
            int count = 0;
            if (look) now.Clear();
            for (int i = 0; i < Cars.Length; i++)
            {
                RaceCar car = Cars[i];
                if (car == null || !car.isActiveAndEnabled || car.InPitLane) continue;
                float along = Mathf.Repeat(Mathf.Repeat(car.Traffic.Distance, Circuit.Lap) - s.From + PassMargin, Circuit.Lap);
                if (along > s.Length + 2f * PassMargin) continue;
                count++;
                if (look) now[i] = along;
            }
            if (!look) return count;

            bool passed = false;
            inSpan.Clear();
            inSpan.AddRange(now.Keys);
            for (int a = 0; a < inSpan.Count && !passed; a++)
            for (int b = a + 1; b < inSpan.Count; b++)
            {
                int i = inSpan[a], j = inSpan[b];
                if (!s.Was.TryGetValue(i, out float wi) || !s.Was.TryGetValue(j, out float wj)) continue;
                float before = wi - wj, after = now[i] - now[j];
                if (Mathf.Abs(after) < PassGap && Mathf.Abs(before) < PassGap && Mathf.Sign(before) != Mathf.Sign(after) && before != 0f)
                {
                    passed = true;
                    break;
                }
            }
            s.Was.Clear();
            foreach (KeyValuePair<int, float> kv in now) s.Was[kv.Key] = kv.Value;
            if (passed)
            {
                s.Held = Math.Max(s.Held, Time.time + 4f);
                Roar(s, force: false);
            }
            return count;
        }

        /// <summary>A roar from a stand, on a free roar voice or the one that has played longest.</summary>
        private void Roar(Stand s, bool force)
        {
            if (!force && Time.time - s.LastRoar < RoarRest) return;
            s.LastRoar = Time.time;
            AudioSource voice = roarVoices[0];
            int voices = Mathf.Min(roarVoices.Length, Math.Max(1, budget.Roars));
            for (int i = 0; i < voices; i++)
            {
                if (!roarVoices[i].isPlaying) { voice = roarVoices[i]; break; }
                if (roarVoices[i].time > voice.time) voice = roarVoices[i];
            }
            voice.transform.position = s.Nearest;
            voice.clip = roars[UnityEngine.Random.Range(0, roars.Length)];
            voice.volume = (s.Main ? 1f : 0.85f) * (1f - 0.4f * (Sky != null ? Sky.Lights : 0f));
            voice.Play();
        }

        // ---------------------------------------------------------------- PA

        /// <summary>Now and then an announcement from the main grandstand, less often through the night.</summary>
        private void Announce(float night)
        {
            if (Time.time < nextAnnouncement || pa.isPlaying) return;
            Stand main = stands.Find(s => s.Main);
            if (main == null) return;
            nextAnnouncement = Time.time + UnityEngine.Random.Range(45f, 120f) * (1f + 1.5f * night);
            int pick = UnityEngine.Random.Range(0, announcements.Length);
            if (pick == lastAnnouncement) pick = (pick + 1) % announcements.Length;
            lastAnnouncement = pick;
            pa.transform.position = main.Nearest + Vector3.up * 6f;   // the speakers hang under the stand's roof
            pa.clip = announcements[pick];
            pa.volume = 0.9f;
            pa.Play();
        }

        // ---------------------------------------------------------------- weather

        private void Rain(float mmPerHour)
        {
            (float light, float heavy) = AmbienceMix.Rain(mmPerHour, budget.RainLayers);
            Level(rainLight, 0.8f * light);
            Level(rainHeavy, 0.9f * heavy);
        }

        private void Thunder(float mmPerHour)
        {
            if (!AmbienceMix.Thunder(Thundery, mmPerHour))
            {
                nextThunder = -1f;
                return;
            }
            // The first clap waits a little after the storm starts.
            if (nextThunder < 0f) nextThunder = Time.time + AmbienceMix.ThunderGap(mmPerHour, UnityEngine.Random.value) * 0.5f;
            if (Time.time < nextThunder) return;
            nextThunder = Time.time + AmbienceMix.ThunderGap(mmPerHour, UnityEngine.Random.value);
            // Near strikes are loud and bright, far ones a dull rumble.
            float near = UnityEngine.Random.value;
            thunder.clip = thunders[UnityEngine.Random.Range(0, thunders.Length)];
            thunder.volume = Mathf.Lerp(0.4f, 1f, near);
            thunderTone.cutoffFrequency = Mathf.Lerp(900f, 16000f, near * near);
            thunder.Play();
        }

        private void Country(float metresToStand, float night, float mmPerHour)
        {
            (float day, float dark) = AmbienceMix.Country(AmbienceMix.Away(metresToStand), night, mmPerHour, budget.CountryLayers);
            Level(countryDay, 0.7f * day);
            Level(countryNight, 0.6f * dark);
        }

        private void Wind(float breeze)
        {
            bool onboard = Cameras != null && (Cameras.Onboard || (Cameras.Broadcasting && Broadcast != null && Broadcast.OnboardShot));
            bool chase = Cameras != null && Cameras.Chasing;
            wind.Synth.Speed = cameraSpeed;
            wind.Synth.Exposure = AmbienceMix.Exposure(onboard, chase);
            wind.Synth.Breeze = breeze;
            Level(windSource, 1f);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Glides a source toward its level, starting it when it becomes audible and stopping it once silent.</summary>
        private static void Level(AudioSource s, float target)
        {
            if (s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, target, Time.unscaledDeltaTime / Fade);
            if (s.volume > 0.001f && !s.isPlaying)
            {
                // Each loop starts somewhere different, so two stands never murmur in step.
                s.timeSamples = UnityEngine.Random.Range(0, Math.Max(1, s.clip.samples - 1));
                s.Play();
            }
            else if (s.volume <= 0.001f && target <= 0f && s.isPlaying) s.Stop();
        }

        private static Vector3 NearestOn(Vector3[] line, Vector3 p, out float distance)
        {
            Vector3 best = line[0];
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector3 a = line[i], ab = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                Vector3 q = a + ab * t;
                float d = (q - p).sqrMagnitude;
                if (d < bestSq) (bestSq, best) = (d, q);
            }
            distance = Mathf.Sqrt(bestSq);
            return best;
        }
    }

    /// <summary>
    /// Runs the <see cref="WindSynth"/> on the audio thread for the wind's source, in stereo. Like the engines, the source
    /// plays a clip of constant ones and the synth multiplies it, so the source's volume still applies.
    /// </summary>
    public sealed class WindVoice : MonoBehaviour
    {
        private static AudioClip ones;
        private float[] air = new float[8192];

        public WindSynth Synth { get; private set; }

        /// <summary>A clip of constant ones for the source to play.</summary>
        public static AudioClip Carrier()
        {
            if (ones != null) return ones;
            int rate = AudioSettings.outputSampleRate;
            var data = new float[rate];
            for (int i = 0; i < rate; i++) data[i] = 1f;
            ones = AudioClip.Create("Wind carrier", rate, 1, rate, false);
            ones.SetData(data, 0);
            return ones;
        }

        private void Awake() => Synth = new WindSynth(AudioSettings.outputSampleRate, 7);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            WindSynth synth = Synth;
            if (synth == null) return;
            if (air.Length < data.Length) air = new float[data.Length];
            synth.Render(air, data.Length / channels, channels);
            for (int i = 0; i < data.Length; i++) data[i] *= air[i];
        }
    }
}
