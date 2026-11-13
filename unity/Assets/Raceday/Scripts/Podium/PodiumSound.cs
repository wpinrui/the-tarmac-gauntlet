using TarmacGauntlet.Audio;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The podium ceremony's sound, kept in time with its <see cref="PodiumShow"/>. The crews below murmur, and swell
    /// with how many of them are cheering. They roar as each place's drivers reach their step, as each trophy goes up,
    /// at the corks and at the confetti. Each champagne bottle pops its cork and fizzes while it sprays. The confetti
    /// cannon booms and the paper flutters down. The crowd plays in the ambience's group of the <see cref="Mix"/>, the
    /// rest in the effects'. The crowd is the race ambience's own recordings (Resources/Ambience); the corks, fizz and
    /// confetti are made by tools/podium/sounds.py (Resources/Podium).
    /// </summary>
    public sealed class PodiumSound : MonoBehaviour
    {
        /// <summary>The crowd bed's level waiting and in full cheer, before the mix.</summary>
        private const float BedQuiet = 0.25f, BedLoud = 0.85f;
        /// <summary>How fast the bed follows the crowd, per second.</summary>
        private const float BedFollow = 1.5f;
        /// <summary>When each place's cork goes, after the show's pop, s: never quite together.</summary>
        private static readonly float[] CorkLag = { 0f, 0.11f, 0.19f };

        private PodiumFrame frame;
        private AudioSource bed, confettiVoice;
        private readonly AudioSource[] roars = new AudioSource[2], corks = new AudioSource[3], fizz = new AudioSource[3];
        private AudioClip[] roarClips, corkClips;
        private readonly float[] roarLevels = new float[2];
        private float bedLevel, last = -1f;
        private int nextRoar;
        private bool playing;

        /// <summary>Makes the voices, the crowd's at the crew below the podium.</summary>
        public void Make(PodiumFrame f, Vector3 crowdCentre)
        {
            frame = f;
            AudioClip Load(string path) => Resources.Load<AudioClip>(path);
            roarClips = new[] { Load("Ambience/crowd-roar-1"), Load("Ambience/crowd-roar-2"), Load("Ambience/crowd-roar-3") };
            corkClips = new[] { Load("Podium/cork-1"), Load("Podium/cork-2"), Load("Podium/cork-3") };
            Vector3 crowdAt = f.At(crowdCentre);
            bed = Voice("Podium crowd", Load("Ambience/crowd-lively"), true, crowdAt, 14f);
            for (int i = 0; i < roars.Length; i++) roars[i] = Voice("Podium roar", null, false, crowdAt, 14f);
            for (int p = 0; p < 3; p++)
            {
                corks[p] = Voice("Podium cork", null, false, crowdAt, 6f);
                fizz[p] = Voice("Podium fizz", Load("Podium/fizz"), true, crowdAt, 5f);
            }
            confettiVoice = Voice("Podium confetti", Load("Podium/confetti"), false, f.At(new Vector3(0f, PodiumPlan.CanopyHeight + PodiumPlan.Floor, PodiumPlan.StandZ)), 10f);
        }

        private AudioSource Voice(string name, AudioClip clip, bool loop, Vector3 at, float near)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.volume = 0f;
            s.dopplerLevel = 0f;
            // Mostly placed, so a close shot is louder than a wide one, but never lost on the widest shot.
            s.spatialBlend = 0.7f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near;
            s.maxDistance = 400f;
            s.spread = 90f;
            s.priority = 40;
            return s;
        }

        /// <summary>Starts a ceremony's sound.</summary>
        public void Begin()
        {
            Stop();
            playing = true;
            last = -1f;
            bedLevel = BedQuiet;
            if (bed.clip != null)
            {
                bed.time = Random.Range(0f, bed.clip.length);
                bed.Play();
            }
        }

        /// <summary>
        /// The sound at time t of the show. cheering is the share of the crowd cheering now; spray, each place's
        /// spray from 0 to 1, or below 0 when it is not spraying.
        /// </summary>
        public void Step(PodiumShow show, float t, float cheering, float[] spray, Vector3[] mouths)
        {
            if (!playing) return;
            float crowdGain = Mix.Gain(SoundGroup.Ambience) * Mix.CrowdDuck, fx = Mix.Gain(SoundGroup.Effects);
            bedLevel = Mathf.MoveTowards(bedLevel, Mathf.Lerp(BedQuiet, BedLoud, cheering), BedFollow * Time.unscaledDeltaTime);
            bed.volume = bedLevel * crowdGain;
            for (int i = 0; i < roars.Length; i++) if (roars[i].isPlaying) roars[i].volume = roarLevels[i] * crowdGain;

            for (int p = 0; p < 3; p++)
            {
                if (show.Drivers[p] <= 0) continue;
                if (Crossed(show.Arrived(p) + 0.3f, t)) Roar(0.55f);
                if (Crossed(PodiumShow.Present[p] + PodiumShow.Handed, t)) Roar(0.7f + 0.15f * (2 - p) / 2f);
                if (Crossed(PodiumShow.PopAt + CorkLag[p], t))
                {
                    corks[p].transform.position = frame.At(mouths[p]);
                    corks[p].pitch = Random.Range(0.94f, 1.06f);
                    corks[p].PlayOneShot(corkClips[p], 1f);
                }
                corks[p].volume = fx;
                AudioSource f = fizz[p];
                if (spray[p] >= 0f)
                {
                    f.transform.position = frame.At(mouths[p]);
                    if (!f.isPlaying && f.clip != null)
                    {
                        f.time = Random.Range(0f, f.clip.length);
                        f.pitch = 0.95f + 0.05f * p;
                        f.Play();
                    }
                    f.volume = Mathf.Lerp(0.35f, 0.8f, spray[p]) * fx;
                }
                else if (f.isPlaying)
                {
                    f.volume = Mathf.MoveTowards(f.volume, 0f, 2f * Time.unscaledDeltaTime);
                    if (f.volume <= 0f) f.Stop();
                }
            }
            if (Crossed(PodiumShow.PopAt + 0.25f, t)) Roar(0.8f);
            if (Crossed(PodiumShow.ConfettiAt, t))
            {
                if (confettiVoice.clip != null) confettiVoice.Play();
                Roar(1f);
            }
            confettiVoice.volume = fx;
            last = t;
        }

        /// <summary>True the first step at or past moment.</summary>
        private bool Crossed(float moment, float t) => last < moment && t >= moment;

        /// <summary>A roar from the crowd, on the voice that has played longest.</summary>
        private void Roar(float level)
        {
            int i = nextRoar;
            nextRoar = (nextRoar + 1) % roars.Length;
            AudioClip clip = roarClips[Random.Range(0, roarClips.Length)];
            if (clip == null) return;
            roarLevels[i] = level;
            roars[i].clip = clip;
            roars[i].pitch = Random.Range(0.96f, 1.04f);
            roars[i].volume = level * Mix.Gain(SoundGroup.Ambience) * Mix.CrowdDuck;
            roars[i].Play();
        }

        /// <summary>Silences everything at once.</summary>
        public void Stop()
        {
            playing = false;
            if (bed == null) return;
            bed.Stop();
            foreach (AudioSource s in roars) s.Stop();
            foreach (AudioSource s in corks) s.Stop();
            foreach (AudioSource s in fizz) s.Stop();
            confettiVoice.Stop();
        }
    }
}
