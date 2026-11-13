using System.Collections.Generic;
using TarmacGauntlet.Audio;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Team HQ's music: the tracks in Resources/Music, shuffled. Every track plays once before any plays again, and
    /// no track plays twice running. It lives across scenes and plays on through the loading screen. Leaving the HQ for a
    /// race, it holds until the race is built and drawn (<see cref="SceneLoader.Arrived"/>), then crosses over into the
    /// race's sound over <see cref="Mix.Crossfade"/>. Leaving a race for the HQ, it rises as the race's sound falls, from
    /// the moment the load starts (<see cref="SceneLoader.Leaving"/>). It plays in the music's group of the
    /// <see cref="Mix"/>, under the master volume, and past the listener's effects. While a load leaves no listener in
    /// the game, it carries one of its own so the music is still heard.
    /// </summary>
    public sealed class MenuMusic : MonoBehaviour
    {
        /// <summary>The fade in as the game opens on the HQ, s.</summary>
        private const float OpenSeconds = 2f;
        /// <summary>Silence between one track ending and the next starting, s.</summary>
        private const float GapSeconds = 2f;

        private static MenuMusic instance;

        private readonly List<int> deck = new List<int>();
        private AudioSource source;
        private AudioListener ownEar;
        private AudioClip[] tracks;
        private int last = -1;
        private float fade, gap, fadeSeconds = OpenSeconds;
        private bool wanted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneLoader.Leaving -= Leave;
            SceneLoader.Leaving += Leave;
            SceneLoader.Arrived -= Arrive;
            SceneLoader.Arrived += Arrive;
            if (IsHub(SceneManager.GetActiveScene().name)) Play(OpenSeconds);
        }

        private static bool IsHub(string scene) => scene == RaceSummaryScreen.HubScene;

        /// <summary>Starts the music, or keeps it going, fading up over some seconds.</summary>
        private static void Play(float seconds)
        {
            if (instance == null)
            {
                var go = new GameObject("Menu Music");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<MenuMusic>();
            }
            instance.wanted = true;
            instance.fadeSeconds = seconds;
        }

        private static void Leave()
        {
            bool fromHub = IsHub(SceneManager.GetActiveScene().name), toHub = IsHub(SceneLoader.Target);
            if (fromHub && !toHub)
            {
                // The music carries on over the loading screen; the race stays silent until it crosses over.
                Mix.FadeRace(0f, 0f);
            }
            else if (!fromHub && toHub)
            {
                Mix.FadeRace(0f, Mix.Crossfade);
                Play(Mix.Crossfade);
            }
        }

        private static void Arrive()
        {
            if (IsHub(SceneManager.GetActiveScene().name))
            {
                Mix.FadeRace(1f, 0f);
                Play(Mix.Crossfade);
                return;
            }
            Mix.FadeRace(1f, Mix.Crossfade);
            if (instance == null) return;
            instance.wanted = false;
            instance.fadeSeconds = Mix.Crossfade;
        }

        private void Awake()
        {
            tracks = Resources.LoadAll<AudioClip>("Music");
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.volume = 0;
            source.bypassListenerEffects = true;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            KeepAnEar();
            fade = Mathf.MoveTowards(fade, wanted ? 1 : 0, dt / fadeSeconds);
            source.volume = Mix.CrossGain(fade) * Mix.Gain(SoundGroup.Music);
            if (!wanted)
            {
                // Faded out: stop, so coming back starts a fresh track at once.
                if (fade <= 0 && source.isPlaying) source.Stop();
                gap = 0;
                return;
            }
            if (source.isPlaying || tracks.Length == 0) return;
            gap -= dt;
            if (gap > 0) return;
            source.clip = tracks[Next()];
            source.Play();
            gap = GapSeconds;
        }

        /// <summary>
        /// Between one scene's camera going and the next one's arriving there is no listener, and nothing is heard. While
        /// the music plays through a load it carries its own, and drops it as soon as a scene brings one.
        /// </summary>
        private void KeepAnEar()
        {
            if (!SceneLoader.Loading && ownEar == null) return;
            int ears = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            if (ownEar == null)
            {
                if (ears == 0 && fade > 0) ownEar = gameObject.AddComponent<AudioListener>();
            }
            else if (ears > 1 || !SceneLoader.Loading && fade <= 0)
            {
                Destroy(ownEar);
                ownEar = null;
            }
        }

        /// <summary>The next track off a shuffled deck, dealt again once empty with the last track kept off the top.</summary>
        private int Next()
        {
            if (deck.Count == 0)
            {
                for (int i = 0; i < tracks.Length; i++) deck.Add(i);
                for (int i = deck.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                }
                if (deck.Count > 1 && deck[0] == last) (deck[0], deck[deck.Count - 1]) = (deck[deck.Count - 1], deck[0]);
            }
            last = deck[0];
            deck.RemoveAt(0);
            return last;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
