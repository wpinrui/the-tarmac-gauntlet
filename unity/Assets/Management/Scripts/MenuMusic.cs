using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Team HQ's music: the tracks in Resources/Music, shuffled. Every track plays once before any plays again, and
    /// no track plays twice running. The music fades in as the HQ loads and fades out as soon as the game starts loading
    /// another scene (<see cref="SceneLoader.Leaving"/>), so it never plays in a race. It lives across scenes, so the
    /// shuffle carries on with a fresh track when the player comes back. Its level is <see cref="GameSettings.Music"/>,
    /// under the master volume.
    /// </summary>
    public sealed class MenuMusic : MonoBehaviour
    {
        private const float FadeInSeconds = 2f, FadeOutSeconds = 0.8f;
        /// <summary>Silence between one track ending and the next starting, s.</summary>
        private const float GapSeconds = 2f;

        private static MenuMusic instance;

        private readonly List<int> deck = new List<int>();
        private AudioSource source;
        private AudioClip[] tracks;
        private int last = -1;
        private float fade, gap;
        private bool wanted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
            SceneLoader.Leaving -= Leave;
            SceneLoader.Leaving += Leave;
            Follow(SceneManager.GetActiveScene());
        }

        private static void Loaded(Scene scene, LoadSceneMode mode) => Follow(scene);

        private static void Follow(Scene scene)
        {
            bool hq = scene.name == RaceSummaryScreen.HubScene;
            if (hq && instance == null)
            {
                var go = new GameObject("Menu Music");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<MenuMusic>();
            }
            if (instance != null) instance.wanted = hq;
        }

        private static void Leave()
        {
            if (instance != null) instance.wanted = false;
        }

        private void Awake()
        {
            tracks = Resources.LoadAll<AudioClip>("Music");
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.volume = 0;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fade = Mathf.MoveTowards(fade, wanted ? 1 : 0, dt / (wanted ? FadeInSeconds : FadeOutSeconds));
            source.volume = fade * GameSettings.Music;
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
